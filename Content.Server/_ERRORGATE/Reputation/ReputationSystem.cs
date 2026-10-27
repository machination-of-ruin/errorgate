using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Reputation;

/// <summary>Where a standing puts a player with a faction (docs/design/reputation.md).</summary>
public enum Standing
{
    /// <summary>Shot on sight. Everybody starts here (standing 0).</summary>
    Hostile,

    /// <summary>The faction's NPCs ignore the player.</summary>
    Neutral,

    /// <summary>Ignored as well; the faction's base and trade will open to these players.</summary>
    Friendly,
}

/// <summary>
///     The standing of every player with the factions of the world, from -1000 to +1000, stored per account (table
///     <c>faction_reputation</c>). Standing 0 is hostile: a faction shoots a player on sight until they earn their way up. The NPCs
///     read it through marker factions (<c>HostileToMilitary</c>, <c>HostileToBandit</c>): the faction lists the marker as hostile, and
///     a player carries the marker exactly while their standing with that faction is hostile. Loaded when the player connects (before
///     they can join the round), saved every minute, at disconnect and at shutdown. Guests are never saved.
/// </summary>
public sealed class ReputationSystem : EntitySystem
{
    public const int Min = -1000;
    public const int Max = 1000;

    /// <summary>The first standing that is neutral (below it: hostile).</summary>
    public const int NeutralFrom = 100;

    /// <summary>The first standing that is friendly.</summary>
    public const int FriendlyFrom = 400;

    /// <summary>The factions a player has a standing with.</summary>
    public static readonly string[] Factions = ["Military", "Bandit"];

    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(60);

    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ITaskManager _task = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;

    private readonly Dictionary<NetUserId, Dictionary<string, int>> _standing = new();
    private readonly HashSet<NetUserId> _dirty = new();
    private readonly List<Task> _saving = new();
    private TimeSpan _nextSave;
    private bool _notify = true;

    public override void Initialize()
    {
        base.Initialize();

        _userDb.AddOnLoadPlayer(LoadPlayer);
        _userDb.AddOnPlayerDisconnect(PlayerDisconnected);
        SubscribeLocalEvent<NpcFactionMemberComponent, PlayerAttachedEvent>(OnAttached);
        Subs.CVar(_cfg, ErrorgateCVars.ReputationNotifications, v => _notify = v, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _task.BlockWaitOnTask(Task.WhenAll(Save(), Task.WhenAll(_saving)));
    }

    public override void Update(float frameTime)
    {
        if (_timing.RealTime < _nextSave)
            return;

        _nextSave = _timing.RealTime + SaveInterval;
        Track(Save());
    }

    // -- Standing ----------------------------------------------------------------------------------------------------

    public static Standing BandOf(int value) => value >= FriendlyFrom ? Standing.Friendly : value >= NeutralFrom ? Standing.Neutral : Standing.Hostile;

    public int GetStanding(NetUserId user, string faction)
    {
        return _standing.TryGetValue(user, out var standings) && standings.TryGetValue(faction, out var value) ? value : 0;
    }

    public Standing GetBand(NetUserId user, string faction) => BandOf(GetStanding(user, faction));

    /// <summary>Moves a standing by <paramref name="delta"/> (clamped to the range) and tells the player. Returns the new standing.</summary>
    public int Adjust(NetUserId user, string faction, int delta, bool notify = true)
    {
        return Set(user, faction, GetStanding(user, faction) + delta, notify);
    }

    /// <summary>Sets a standing (clamped to the range) and tells the player. Returns the new standing.</summary>
    public int Set(NetUserId user, string faction, int value, bool notify = true)
    {
        value = Math.Clamp(value, Min, Max);
        var old = GetStanding(user, faction);
        if (value == old)
            return value;

        if (!_standing.TryGetValue(user, out var standings))
            _standing[user] = standings = new Dictionary<string, int>();

        standings[faction] = value;
        _dirty.Add(user);

        if (!_players.TryGetSessionById(user, out var session))
            return value;

        if (session.AttachedEntity is { } entity)
            SyncFactions(entity, user);

        if (notify && _notify)
            Notify(session, faction, old, value);

        return value;
    }

    private void Notify(ICommonSession session, string faction, int old, int value)
    {
        var name = Loc.GetString($"reputation-faction-{faction.ToLowerInvariant()}");
        var delta = value - old;
        var change = Loc.GetString("reputation-change", ("faction", name), ("delta", delta > 0 ? $"+{delta}" : delta.ToString()));
        _chat.DispatchServerMessage(session, change);

        var band = BandOf(value);
        if (band != BandOf(old))
            _chat.DispatchServerMessage(session, Loc.GetString($"reputation-band-{band.ToString().ToLowerInvariant()}", ("faction", name)));
    }

    // -- Factions of the player entity -------------------------------------------------------------------------------

    private void OnAttached(Entity<NpcFactionMemberComponent> ent, ref PlayerAttachedEvent args)
    {
        SyncFactions(ent, args.Player.UserId);
    }

    /// <summary>A player carries the marker faction of every faction they are hostile with.</summary>
    public void SyncFactions(EntityUid entity, NetUserId user)
    {
        foreach (var faction in Factions)
        {
            if (GetBand(user, faction) == Standing.Hostile)
                _factions.AddFaction(entity, $"HostileTo{faction}");
            else
                _factions.RemoveFaction(entity, $"HostileTo{faction}");
        }
    }

    // -- Database ----------------------------------------------------------------------------------------------------

    private async Task LoadPlayer(ICommonSession session, CancellationToken cancel)
    {
        var standings = new Dictionary<string, int>();
        if (session.AuthType != LoginType.GuestAssigned)
        {
            foreach (var row in await _db.GetFactionReputations(session.UserId.UserId, cancel))
                standings[row.Faction] = Math.Clamp(row.Value, Min, Max);
        }

        _standing[session.UserId] = standings;
    }

    private void PlayerDisconnected(ICommonSession session)
    {
        Track(Save(session.UserId));
        _standing.Remove(session.UserId);
        _dirty.Remove(session.UserId);
    }

    private Task Save(NetUserId? only = null)
    {
        var updates = new List<FactionReputationUpdate>();
        foreach (var user in only is { } one ? [one] : _dirty.ToArray())
        {
            if (!_standing.TryGetValue(user, out var standings) || IsGuest(user))
                continue;

            foreach (var (faction, value) in standings)
                updates.Add(new FactionReputationUpdate(user, faction, value));

            _dirty.Remove(user);
        }

        return updates.Count == 0 ? Task.CompletedTask : _db.SetFactionReputations(updates);
    }

    private bool IsGuest(NetUserId user) => _players.TryGetSessionById(user, out var session) && session.AuthType == LoginType.GuestAssigned;

    private void Track(Task task)
    {
        _saving.RemoveAll(t => t.IsCompleted);
        _saving.Add(task);
    }
}
