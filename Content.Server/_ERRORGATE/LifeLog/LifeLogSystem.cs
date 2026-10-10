using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.LifeLog;

/// <summary>
///     Keeps the last things that happened to every player character (harm taken and dealt, kills, what was said and
///     heard) and writes them as a short technical log when they die, shown with the death message. Plain bookkeeping with
///     fixed wording, no model involved. The record belongs to the mind, so it survives the body being gibbed.
/// </summary>
public sealed class LifeLogSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>How many lines the log shows, not counting the closing line.</summary>
    public const int ShownLines = 8;

    // Hits from the same source this close together are one line. A kill is credited to the last player who hurt the
    // victim within the blame window.
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BlameWindow = TimeSpan.FromSeconds(60);

    private const int MaxWordsLength = 40;

    private readonly Dictionary<EntityUid, LifeRecord> _records = new();

    // Mobs (and players) hurt by a player recently: victim -> attacker's mind
    private readonly Dictionary<EntityUid, (EntityUid Mind, TimeSpan Time)> _hurtBy = new();

    private readonly HashSet<Entity<MobStateComponent>> _hearers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawned);
        SubscribeLocalEvent<MobStateComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());
    }

    public void Clear()
    {
        _records.Clear();
        _hurtBy.Clear();
    }

    /// <summary>
    ///     The mind of a body, if a player has it.
    /// </summary>
    private bool TryPlayerMind(EntityUid body, out EntityUid mindId)
    {
        mindId = default;

        if (!TryComp<MindContainerComponent>(body, out var container)
            || container.Mind is not { } mind
            || !TryComp<MindComponent>(mind, out var mindComp)
            || mindComp.UserId == null)
        {
            return false;
        }

        mindId = mind;
        return true;
    }

    private LifeRecord RecordOf(EntityUid mindId)
    {
        if (!_records.TryGetValue(mindId, out var record))
        {
            record = new LifeRecord { Born = _timing.CurTime };
            _records[mindId] = record;
        }

        return record;
    }

    /// <summary>
    ///     Adds a line. Damage of the same kind and subject in a row is merged into the previous line.
    /// </summary>
    private void Add(LifeRecord record, LogKind kind, string subject, string text = "", float amount = 0f)
    {
        var now = _timing.CurTime;

        if (kind is LogKind.DamageIn or LogKind.DamageOut
            && record.Events.Count > 0
            && record.Events[^1] is { } last
            && last.Kind == kind
            && last.Subject == subject
            && now - last.Time < MergeWindow)
        {
            last.Amount += amount;
            last.Count++;
            last.Time = now;
            return;
        }

        record.Events.Add(new LogEntry { Time = now, Kind = kind, Subject = subject, Text = text, Amount = amount });

        if (record.Events.Count > LifeRecord.Capacity)
            record.Events.RemoveRange(0, record.Events.Count - LifeRecord.Capacity);
    }

    private void OnSpawned(PlayerSpawnCompleteEvent args)
    {
        if (TryPlayerMind(args.Mob, out var mindId))
        {
            // A new life: the record starts from nothing
            _records.Remove(mindId);
            RecordOf(mindId);
        }
    }

    private void OnDamageChanged(Entity<MobStateComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        var total = (float) args.DamageDelta.GetTotal();
        if (total <= 0f)
            return;

        var now = _timing.CurTime;
        var victimIsPlayer = TryPlayerMind(ent.Owner, out var victimMind);
        var origin = args.Origin is { } o && Exists(o) ? o : (EntityUid?) null;
        var attackerIsPlayer = false;
        EntityUid attackerMind = default;

        if (origin is { } attacker && attacker != ent.Owner)
            attackerIsPlayer = TryPlayerMind(attacker, out attackerMind);

        // The attacker's side: what they did
        if (attackerIsPlayer)
        {
            Add(RecordOf(attackerMind), LogKind.DamageOut, Upper(Name(ent.Owner)), amount: total);
            _hurtBy[ent.Owner] = (attackerMind, now);
        }

        // The victim's side: what was done to them
        if (!victimIsPlayer)
            return;

        var victim = RecordOf(victimMind);
        string source;

        if (origin is not { } from)
            source = EnvironmentCause(args.DamageDelta);
        else if (from == ent.Owner)
            source = Loc.GetString("life-log-source-self");
        else if (TryComp<ErrorgateAnomalyComponent>(from, out var fault))
            source = Loc.GetString("life-log-source-fault", ("kind", Loc.GetString("life-log-fault-" + fault.Kind.ToString().ToLowerInvariant())));
        else
            source = Upper(Name(from));

        Add(victim, LogKind.DamageIn, source, amount: total);
    }

    private string EnvironmentCause(DamageSpecifier delta)
    {
        var biggest = delta.DamageDict.Where(d => d.Value > 0).OrderByDescending(d => (float) d.Value).FirstOrDefault();
        var cause = biggest.Key switch
        {
            "Heat" or "Caustic" => "fire",
            "Cold" => "cold",
            "Asphyxiation" => "air",
            "Bloodloss" => "blood",
            "Poison" => "poison",
            "Radiation" => "radiation",
            _ => "unknown",
        };

        return Loc.GetString("life-log-env-" + cause);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        // A kill is credited to the last player who hurt the victim, whoever or whatever the victim was
        if (_hurtBy.Remove(args.Target, out var hit)
            && _timing.CurTime - hit.Time < BlameWindow
            && _records.TryGetValue(hit.Mind, out var killer))
        {
            Add(killer, LogKind.Deleted, Upper(Name(args.Target)));
        }
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        if (!TryPlayerMind(args.Source, out var speakerMind))
            return;

        var words = Trim(args.Message);
        Add(RecordOf(speakerMind), args.IsWhisper ? LogKind.Whisper : LogKind.Speech, string.Empty, words);

        // Who was close enough to hear it
        var range = args.IsWhisper ? SharedChatSystem.WhisperClearRange : SharedChatSystem.VoiceRange;
        _hearers.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(args.Source), range, _hearers);

        var speakerName = Upper(Name(args.Source));
        foreach (var (hearer, _) in _hearers)
        {
            if (hearer == args.Source || !TryPlayerMind(hearer, out var hearerMind))
                continue;

            Add(RecordOf(hearerMind), LogKind.Heard, speakerName, words);
        }
    }

    private static string Trim(string message)
    {
        message = message.ReplaceLineEndings(" ").Trim();
        return message.Length > MaxWordsLength ? message[..MaxWordsLength] + "..." : message;
    }

    /// <summary>
    ///     The log lines for the character a mind has just lost to death, null when nothing is known about them.
    ///     The record belongs to the mind, so this works even when the body is already gone.
    /// </summary>
    public List<string>? BuildDeathLog(EntityUid mindId)
    {
        return _records.TryGetValue(mindId, out var record) ? BuildLines(record, _timing.CurTime) : null;
    }

    public List<string> BuildLines(LifeRecord record, TimeSpan now)
    {
        var lines = new List<string> { Loc.GetString("life-log-header") };

        var shown = record.Events.Skip(Math.Max(0, record.Events.Count - ShownLines)).ToList();

        // A short life starts at its beginning
        if (record.Events.Count < ShownLines)
            lines.Add(Loc.GetString("life-log-born", ("time", Clock(now - record.Born))));

        foreach (var entry in shown)
        {
            lines.Add(Line(entry, now));
        }

        lines.Add(Loc.GetString("life-log-end"));
        return lines;
    }

    private string Line(LogEntry entry, TimeSpan now)
    {
        var time = Clock(now - entry.Time);
        var amount = (int) MathF.Round(entry.Amount);
        var merged = entry.Count > 1;

        return entry.Kind switch
        {
            LogKind.Speech => Loc.GetString("life-log-speech", ("time", time), ("text", entry.Text)),
            LogKind.Whisper => Loc.GetString("life-log-whisper", ("time", time), ("text", entry.Text)),
            LogKind.Heard => Loc.GetString("life-log-heard", ("time", time), ("subject", entry.Subject), ("text", entry.Text)),
            LogKind.DamageIn => Loc.GetString(merged ? "life-log-damage-in-merged" : "life-log-damage-in",
                ("time", time), ("subject", entry.Subject), ("amount", amount), ("count", entry.Count)),
            LogKind.DamageOut => Loc.GetString(merged ? "life-log-damage-out-merged" : "life-log-damage-out",
                ("time", time), ("subject", entry.Subject), ("amount", amount), ("count", entry.Count)),
            _ => Loc.GetString("life-log-deleted", ("time", time), ("subject", entry.Subject)),
        };
    }

    /// <summary>
    ///     Minutes and seconds, 04:52.
    /// </summary>
    private static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        return $"{(int) span.TotalMinutes:00}:{span.Seconds:00}";
    }

    private static string Upper(string text) => text.ToUpperInvariant();

    private string Name(EntityUid uid) => MetaData(uid).EntityName;
}
