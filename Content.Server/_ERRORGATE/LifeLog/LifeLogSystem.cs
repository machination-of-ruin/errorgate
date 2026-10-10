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
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.LifeLog;

/// <summary>
///     Keeps what happened to every player character (who hurt them, who they hurt, who they talked to) and writes it as a
///     short cold log when they die, shown with the death message. Plain bookkeeping with fixed wording, no model involved.
///     The record belongs to the mind, so it survives the body being gibbed.
/// </summary>
public sealed class LifeLogSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    // A hit counts as the cause of a death, or of a kill, for this long
    private static readonly TimeSpan BlameWindow = TimeSpan.FromSeconds(60);

    private const int MaxNames = 4;
    private const int MaxWordsLength = 80;

    private readonly Dictionary<EntityUid, LifeRecord> _records = new();

    // Mobs (and players) hurt by a player recently, so a kill can be credited: victim -> attacker's mind
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

    private LifeRecord RecordOf(EntityUid mindId, EntityUid body)
    {
        if (!_records.TryGetValue(mindId, out var record))
        {
            record = new LifeRecord { Born = _timing.CurTime };
            _records[mindId] = record;
        }

        record.Name = Name(body);
        return record;
    }

    private void OnSpawned(PlayerSpawnCompleteEvent args)
    {
        if (TryPlayerMind(args.Mob, out var mindId))
        {
            // A new life: the record starts from nothing
            _records.Remove(mindId);
            RecordOf(mindId, args.Mob);
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
        if (attackerIsPlayer && origin is { } hurter)
        {
            var record = RecordOf(attackerMind, hurter);
            var table = victimIsPlayer ? record.HurtPlayers : record.HurtMobs;
            table[Name(ent.Owner)] = table.GetValueOrDefault(Name(ent.Owner)) + total;
            _hurtBy[ent.Owner] = (attackerMind, now);
        }

        // The victim's side: what was done to them
        if (!victimIsPlayer)
            return;

        var victim = RecordOf(victimMind, ent.Owner);

        if (origin is not { } source)
        {
            NoteEnvironment(victim, args.DamageDelta, now);
            return;
        }

        if (source == ent.Owner)
        {
            NoteAttack(victim, HarmSource.Self, string.Empty, default, total, now);
        }
        else if (TryComp<ErrorgateAnomalyComponent>(source, out var fault))
        {
            NoteAttack(victim, HarmSource.Fault, string.Empty, fault.Kind, total, now);
        }
        else if (attackerIsPlayer)
        {
            NoteAttack(victim, HarmSource.Player, Name(source), default, total, now);
        }
        else
        {
            NoteAttack(victim, HarmSource.Thing, Name(source), default, total, now);
        }
    }

    private static void NoteAttack(LifeRecord record, HarmSource kind, string name, ErrorgateAnomalyKind fault, float damage, TimeSpan now)
    {
        // Hits from the same source in a row add up
        var sameSource = record.LastAttackKind == kind && record.LastAttackName == name && record.LastAttackFault == fault
                         && now - record.LastAttackTime < BlameWindow;

        record.LastAttackDamage = sameSource ? record.LastAttackDamage + damage : damage;
        record.LastAttackKind = kind;
        record.LastAttackName = name;
        record.LastAttackFault = fault;
        record.LastAttackTime = now;
    }

    private static void NoteEnvironment(LifeRecord record, DamageSpecifier delta, TimeSpan now)
    {
        var biggest = delta.DamageDict.Where(d => d.Value > 0).OrderByDescending(d => (float) d.Value).FirstOrDefault();
        record.LastEnvironment = biggest.Key switch
        {
            "Heat" or "Caustic" => "fire",
            "Cold" => "cold",
            "Asphyxiation" => "air",
            "Bloodloss" => "blood",
            "Poison" => "poison",
            "Radiation" => "radiation",
            _ => "unknown",
        };
        record.LastEnvironmentTime = now;
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
            var name = Name(args.Target);
            var list = TryPlayerMind(args.Target, out _) ? killer.KilledPlayers : killer.KilledMobs;
            list.Add(name);
        }
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        if (!TryPlayerMind(args.Source, out var speakerMind))
            return;

        var speaker = RecordOf(speakerMind, args.Source);
        speaker.LinesSpoken++;
        speaker.LastWords = args.Message.Length > MaxWordsLength ? args.Message[..MaxWordsLength] + "..." : args.Message;

        // Who was close enough to hear it
        var range = args.IsWhisper ? SharedChatSystem.WhisperClearRange : SharedChatSystem.VoiceRange;
        _hearers.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(args.Source), range, _hearers);

        foreach (var (hearer, _) in _hearers)
        {
            if (hearer == args.Source || !TryPlayerMind(hearer, out var hearerMind))
                continue;

            var hearerName = Name(hearer);
            speaker.SpokeWith[hearerName] = speaker.SpokeWith.GetValueOrDefault(hearerName) + 1;

            var heard = RecordOf(hearerMind, hearer);
            var speakerName = Name(args.Source);
            heard.SpokenToBy[speakerName] = heard.SpokenToBy.GetValueOrDefault(speakerName) + 1;
        }
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
        var lines = new List<string> { Loc.GetString("life-log-header", ("name", Upper(record.Name))) };

        var minutes = (int) (now - record.Born).TotalMinutes;
        lines.Add(minutes < 1
            ? Loc.GetString("life-log-lifespan-short")
            : Loc.GetString("life-log-lifespan", ("minutes", minutes)));

        lines.Add(HarmLine(record, now));

        lines.Add(record.KilledPlayers.Count + record.KilledMobs.Count > 0
            ? Loc.GetString("life-log-killed", ("names", Names(record.KilledPlayers.Concat(record.KilledMobs))))
            : record.HurtPlayers.Count + record.HurtMobs.Count > 0
                ? Loc.GetString("life-log-hurt", ("names", Names(record.HurtPlayers.Keys.Concat(record.HurtMobs.Keys))))
                : Loc.GetString("life-log-hurt-none"));

        if (record.SpokeWith.Count > 0)
        {
            var closest = record.SpokeWith.OrderByDescending(p => p.Value).Take(MaxNames).ToList();
            lines.Add(Loc.GetString("life-log-spoke",
                ("names", Names(closest.Select(p => p.Key))),
                ("lines", record.LinesSpoken)));
        }
        else if (record.SpokenToBy.Count > 0)
        {
            lines.Add(Loc.GetString("life-log-spoken-to", ("names", Names(record.SpokenToBy.Keys))));
        }
        else
        {
            lines.Add(Loc.GetString("life-log-spoke-none"));
        }

        lines.Add(record.LastWords != null
            ? Loc.GetString("life-log-last-words", ("words", record.LastWords))
            : Loc.GetString("life-log-last-words-none"));

        lines.Add(Loc.GetString("life-log-footer"));
        return lines;
    }

    private string HarmLine(LifeRecord record, TimeSpan now)
    {
        var recentAttack = record.LastAttackKind != HarmSource.None && now - record.LastAttackTime < BlameWindow;
        if (recentAttack)
        {
            var damage = (int) MathF.Round(record.LastAttackDamage);
            return record.LastAttackKind switch
            {
                HarmSource.Player => Loc.GetString("life-log-harmed-player", ("source", Upper(record.LastAttackName)), ("damage", damage)),
                HarmSource.Fault => Loc.GetString("life-log-harmed-fault",
                    ("source", Loc.GetString("life-log-fault-" + record.LastAttackFault.ToString().ToLowerInvariant())),
                    ("damage", damage)),
                HarmSource.Self => Loc.GetString("life-log-harmed-self", ("damage", damage)),
                _ => Loc.GetString("life-log-harmed-thing", ("source", Upper(record.LastAttackName)), ("damage", damage)),
            };
        }

        if (record.LastEnvironment != string.Empty)
            return Loc.GetString("life-log-harmed-environment", ("source", Loc.GetString("life-log-env-" + record.LastEnvironment)));

        return Loc.GetString("life-log-harmed-none");
    }

    private static string Names(IEnumerable<string> names)
    {
        var list = names.Distinct().Select(Upper).ToList();
        if (list.Count <= MaxNames)
            return string.Join(", ", list);

        return string.Join(", ", list.Take(MaxNames)) + " AND " + (list.Count - MaxNames) + " MORE";
    }

    private static string Upper(string text) => text.ToUpperInvariant();

    private string Name(EntityUid uid) => MetaData(uid).EntityName;
}
