using System.Linq;
using Content.Shared._ERRORGATE.AiGod;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.Chapel;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     The director that needs no model: small signs now and then, a line for the witnesses of a death, an answer to a prayer.
///     It only ever offers actions to <see cref="GodDirectorSystem"/>, which checks and paces them. It is also what keeps her
///     whisper going when a model is not configured or does not answer.
/// </summary>
public sealed class GodScriptedSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GodLedgerSystem _ledger = default!;
    [Dependency] private readonly GodDirectorSystem _director = default!;

    // Seconds after a death before the witnesses hear of it, and how many of them do
    private const float DeathDelay = 3f;
    private const int MaxWitnesses = 2;

    // Share of small signs that are a glitch and not a line
    private const float GlitchShare = 0.3f;

    // The last lines said, not repeated at once
    private const int MemorySize = 4;

    private readonly List<(TimeSpan Due, Action Act)> _later = new();
    private readonly List<string> _said = new();
    private TimeSpan _nextWhisper;
    private bool _enabled;
    private string _mode = "scripted";
    private float _interval = 150f;
    private float _prayerDelay = 8f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GodSubjectDiedEvent>(OnDied);
        SubscribeLocalEvent<PrayedEvent>(OnPrayed);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _later.Clear();
            _said.Clear();
            _nextWhisper = TimeSpan.Zero;
        });

        Subs.CVar(_cfg, ErrorgateCVars.GodEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodMode, v => _mode = v.Trim().ToLowerInvariant(), true);
        // A new interval starts the wait over
        Subs.CVar(_cfg, ErrorgateCVars.GodWhisperInterval, v =>
        {
            _interval = v;
            _nextWhisper = TimeSpan.Zero;
        }, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodPrayerDelay, v => _prayerDelay = v, true);
    }

    /// <summary>
    ///     The scripted rules run in "scripted" and "assisted" mode (and later as the fallback of "full").
    /// </summary>
    private bool Active => _enabled && _mode is not ("off" or "full");

    private GodLinesPrototype Lines => _proto.Index<GodLinesPrototype>("Default");

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        for (var i = _later.Count - 1; i >= 0; i--)
        {
            if (_later[i].Due > now)
                continue;

            var act = _later[i].Act;
            _later.RemoveAt(i);
            if (Active)
                act();
        }

        if (!Active)
            return;

        // The first sign comes after a while, not the moment she starts
        if (_nextWhisper == TimeSpan.Zero)
            _nextWhisper = now + TimeSpan.FromSeconds(_interval * _random.NextFloat(0.6f, 1.4f));

        if (now < _nextWhisper)
            return;

        // Around the average, never regular
        _nextWhisper = now + TimeSpan.FromSeconds(_interval * _random.NextFloat(0.6f, 1.4f));
        Whisper();
    }

    /// <summary>
    ///     A small sign for one subject, chosen by how interesting they are.
    /// </summary>
    private void Whisper()
    {
        var candidates = _ledger.Ledger.All.Where(s => _ledger.TryGetReachable(s, out _, out _)).ToList();
        if (candidates.Count == 0)
            return;

        var now = _timing.CurTime;
        var subject = Weighted(candidates, s => 1f + GodDigest.Salience(s, now));

        if (_random.Prob(GlitchShare))
        {
            _director.Submit(new GodAction { Type = GodActionType.Glitch, Targets = { subject.Number } }, GodSource.Scripted);
            return;
        }

        var line = PickLine(Lines.Omen, subject, subject.Sector);
        if (line != null)
            _director.Submit(new GodAction { Type = GodActionType.Subtle, Targets = { subject.Number }, Text = line }, GodSource.Scripted);
    }

    /// <summary>
    ///     A few seconds after a death, the ones who were close hear of it. A death alone in the wilderness is silent.
    /// </summary>
    private void OnDied(GodSubjectDiedEvent ev)
    {
        if (!Active)
            return;

        var dead = ev.Subject;
        var witnesses = dead.Near.ToList();

        _later.Add((_timing.CurTime + TimeSpan.FromSeconds(DeathDelay), () =>
        {
            var heard = 0;
            foreach (var number in witnesses)
            {
                if (heard >= MaxWitnesses || _ledger.Ledger.ByNumber(number) is not { } witness || !_ledger.TryGetReachable(witness, out _, out _))
                    continue;

                var line = PickLine(Lines.Error, dead, dead.Sector);
                if (line == null)
                    return;

                _director.Submit(new GodAction { Type = GodActionType.Subtle, Targets = { witness.Number }, Text = line }, GodSource.Scripted);
                heard++;
            }
        }));
    }

    private void OnPrayed(PrayedEvent ev)
    {
        if (!Active || !HasComp<SacrificialAltarComponent>(ev.Target) || ev.Sender.AttachedEntity is not { } body
            || !_ledger.TrySubject(body, out var subject))
        {
            return;
        }

        var delay = _prayerDelay * _random.NextFloat(0.7f, 1.5f);
        _later.Add((_timing.CurTime + TimeSpan.FromSeconds(delay), () =>
        {
            var line = PickLine(Lines.Prayer, subject, subject.Sector);
            if (line != null)
                _director.Submit(new GodAction { Type = GodActionType.Subtle, Targets = { subject.Number }, Text = line }, GodSource.Scripted);
        }));
    }

    /// <summary>
    ///     A line from the list with the slots filled in, not one said lately, null when none fits (a line that needs a sector
    ///     when the subject is not in one is left out).
    /// </summary>
    private string? PickLine(List<string> lines, Subject about, string? sector)
    {
        var fitting = lines
            .Where(l => sector != null || !l.Contains("{sector}"))
            .Select(l => Fill(l, about, sector))
            .Where(l => !_said.Contains(l))
            .ToList();

        // Everything was said lately: allow a repeat rather than silence
        if (fitting.Count == 0)
        {
            fitting = lines
                .Where(l => sector != null || !l.Contains("{sector}"))
                .Select(l => Fill(l, about, sector))
                .ToList();
        }

        if (fitting.Count == 0)
            return null;

        var chosen = _random.Pick(fitting);
        _said.Add(chosen);
        if (_said.Count > MemorySize)
            _said.RemoveAt(0);

        return chosen;
    }

    public static string Fill(string line, Subject about, string? sector)
    {
        return line
            .Replace("{name}", about.Name.ToUpperInvariant())
            .Replace("{number}", about.Number.ToString())
            .Replace("{sector}", sector ?? string.Empty);
    }

    private T Weighted<T>(List<T> items, Func<T, float> weight)
    {
        var total = items.Sum(weight);
        var roll = _random.NextFloat() * total;
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll <= 0f)
                return item;
        }

        return items[^1];
    }
}
