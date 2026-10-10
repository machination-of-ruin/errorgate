using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Atmos.Components;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
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

    // Damage nobody dealt (bleeding, no air, starvation) arrives in tiny ticks. It is added up per cause over this long, and
    // a cause that adds up to less than the minor limit is not in the log at all.
    private static readonly TimeSpan EnvironmentWindow = TimeSpan.FromSeconds(30);
    private const float MinorEnvironmentDamage = 5f;
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
        // Before the thresholds: the hit that kills is what sends the player to the void and builds the log, so it has to be
        // written down first
        SubscribeLocalEvent<MobStateComponent, DamageChangedEvent>(OnDamageChanged, before: new[] { typeof(MobThresholdSystem) });
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

        // The same source hitting again joins its earlier line, whatever small environmental damage (burning, bleeding)
        // landed in between: those are mostly hidden and must not break the chain. It moves to the end with the time of
        // the latest hit, so the log stays in order.
        if (kind is LogKind.DamageIn or LogKind.DamageOut)
        {
            for (var i = record.Events.Count - 1; i >= 0; i--)
            {
                var previous = record.Events[i];
                if (now - previous.Time >= MergeWindow)
                    break;

                if (previous.Environmental)
                    continue;

                if (previous.Kind == kind && previous.Subject == subject)
                {
                    previous.Amount += amount;
                    previous.Count++;
                    previous.Time = now;
                    record.Events.RemoveAt(i);
                    record.Events.Add(previous);
                    return;
                }

                break;
            }
        }

        record.Events.Add(new LogEntry { Time = now, Kind = kind, Subject = subject, Text = text, Amount = amount });

        if (record.Events.Count > LifeRecord.Capacity)
            record.Events.RemoveRange(0, record.Events.Count - LifeRecord.Capacity);
    }

    /// <summary>
    ///     Adds damage nobody dealt. It joins the latest entry of the same cause within <see cref="EnvironmentWindow"/>,
    ///     whatever happened in between, and moves to the end of the list so the log stays in order.
    /// </summary>
    private void AddEnvironment(LifeRecord record, string cause, float amount)
    {
        var now = _timing.CurTime;

        for (var i = record.Events.Count - 1; i >= 0; i--)
        {
            var entry = record.Events[i];
            if (now - entry.Time >= EnvironmentWindow)
                break;

            if (!entry.Environmental || entry.Subject != cause)
                continue;

            entry.Amount += amount;
            entry.Count++;
            entry.Time = now;
            record.Events.RemoveAt(i);
            record.Events.Add(entry);
            return;
        }

        record.Events.Add(new LogEntry { Time = now, Kind = LogKind.DamageIn, Subject = cause, Amount = amount, Environmental = true });

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
        {
            AddEnvironment(victim, EnvironmentCause(ent.Owner, args.DamageDelta), total);
            return;
        }

        if (from == ent.Owner)
            source = Loc.GetString("life-log-source-self");
        else if (TryComp<ErrorgateAnomalyComponent>(from, out var fault))
            source = Loc.GetString("life-log-source-fault", ("kind", Loc.GetString("life-log-fault-" + fault.Kind.ToString().ToLowerInvariant())));
        else
            source = Upper(Name(from));

        Add(victim, LogKind.DamageIn, source, amount: total);
    }

    private string EnvironmentCause(EntityUid victim, DamageSpecifier delta)
    {
        // Starvation deals blood loss. When a starving character takes exactly the damage hunger deals, that is the cause
        // (a bleeding wound of the very same size in the same moment would be called starvation too)
        if (TryComp<HungerComponent>(victim, out var hunger)
            && hunger.CurrentThreshold <= HungerThreshold.Starving
            && hunger.StarvationDamage is { } starvation
            && starvation.DamageDict.Count > 0
            && starvation.DamageDict.All(d => delta.DamageDict.TryGetValue(d.Key, out var dealt) && Math.Abs((float) (dealt - d.Value)) < 0.01f))
        {
            return Loc.GetString("life-log-env-starvation");
        }

        var biggest = delta.DamageDict.Where(d => d.Value > 0).OrderByDescending(d => (float) d.Value).FirstOrDefault();
        var cause = biggest.Key switch
        {
            // Burning is fire. Heat without flames is the air around a heat fault, which stays hot after the fire is out
            "Heat" => TryComp<FlammableComponent>(victim, out var flammable) && flammable.OnFire ? "fire" : "hot-air",
            "Caustic" => "fire",
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
        // Small environmental damage (a few ticks of bleeding or no air) is not worth a line
        var visible = record.Events.Where(e => !e.Environmental || e.Amount >= MinorEnvironmentDamage).ToList();
        var shown = visible.Skip(Math.Max(0, visible.Count - ShownLines)).ToList();

        // A short life starts at its beginning
        var born = visible.Count < ShownLines;

        // The same width for every time, so the bars line up
        var spans = shown.Select(e => now - e.Time).ToList();
        if (born)
            spans.Add(now - record.Born);

        var minutesWidth = Math.Max(2, spans.Select(s => ((int) Math.Max(0, s.TotalMinutes)).ToString().Length).DefaultIfEmpty(2).Max());

        var lines = new List<string>();
        if (born)
            lines.Add(Loc.GetString("life-log-born", ("time", Clock(now - record.Born, minutesWidth))));

        foreach (var entry in shown)
        {
            lines.Add(Line(entry, Clock(now - entry.Time, minutesWidth)));
        }

        lines.Add(Loc.GetString("life-log-end", ("time", Clock(TimeSpan.Zero, minutesWidth))));
        return lines;
    }

    private string Line(LogEntry entry, string time)
    {
        var amount = (int) MathF.Round(entry.Amount);

        return entry.Kind switch
        {
            LogKind.Speech => Loc.GetString("life-log-speech", ("time", time), ("text", entry.Text)),
            LogKind.Whisper => Loc.GetString("life-log-whisper", ("time", time), ("text", entry.Text)),
            LogKind.Heard => Loc.GetString("life-log-heard", ("time", time), ("subject", entry.Subject), ("text", entry.Text)),
            LogKind.DamageIn => Loc.GetString("life-log-damage-in", ("time", time), ("subject", entry.Subject), ("amount", amount)),
            LogKind.DamageOut => Loc.GetString("life-log-damage-out", ("time", time), ("subject", entry.Subject), ("amount", amount)),
            _ => Loc.GetString("life-log-deleted", ("time", time), ("subject", entry.Subject)),
        };
    }

    /// <summary>
    ///     Minutes, seconds and hundredths, 04:52.37, so that lines do not share a time.
    /// </summary>
    private static string Clock(TimeSpan span, int minutesWidth)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        return $"{((int) span.TotalMinutes).ToString().PadLeft(minutesWidth, '0')}:{span.Seconds:00}.{span.Milliseconds / 10:00}";
    }

    private static string Upper(string text) => text.ToUpperInvariant();

    private string Name(EntityUid uid) => MetaData(uid).EntityName;
}
