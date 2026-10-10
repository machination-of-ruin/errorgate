using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Content.Shared._ERRORGATE.AiGod;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.Chapel;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     The director that asks the model. Every few minutes (sooner for a prayer or a run of deaths) it sends the digest of the
///     round and gets back what she wants to do, which goes through <see cref="GodDirectorSystem.Submit"/> like everything
///     else. Calls are stateless: a fixed system prompt, a fresh digest, and the notes she wrote to herself last time. When
///     the model does not answer, nothing is lost: the events go back in the buffer, and in "full" mode the scripted rules take over.
/// </summary>
public sealed class GodLlmDirectorSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly GodLlmSystem _llm = default!;
    [Dependency] private readonly GodObserverSystem _observer = default!;
    [Dependency] private readonly GodDigestSystem _digest = default!;
    [Dependency] private readonly GodDirectorSystem _director = default!;

    // The most lines the model may add to each category of the line bank
    private const int MaxExtraLines = 40;

    private static readonly Regex Slot = new(@"\{(\w+)\}", RegexOptions.Compiled);
    private static readonly string[] Slots = { "name", "number", "sector" };

    private bool _enabled;
    private string _mode = "scripted";
    private float _interval = 480f;
    private float _earlyGap = 90f;
    private int _spikeDeaths = 3;
    private float _writerInterval = 1800f;

    private bool _calling;
    private TimeSpan _nextCall;
    private TimeSpan _lastCall;
    private TimeSpan _nextWriter;
    private readonly List<TimeSpan> _recentDeaths = new();

    /// <summary>The last digest sent, for admins.</summary>
    public string LastDigest = string.Empty;

    /// <summary>The last reply received, for admins.</summary>
    public string LastReply = string.Empty;

    /// <summary>What was wrong with the last reply.</summary>
    public readonly List<string> LastProblems = new();

    public int Calls;
    public int Failures;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GodSubjectDiedEvent>(OnDied);
        SubscribeLocalEvent<PrayedEvent>(OnPrayed);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());

        Subs.CVar(_cfg, ErrorgateCVars.GodEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodMode, v => _mode = v.Trim().ToLowerInvariant(), true);
        Subs.CVar(_cfg, ErrorgateCVars.GodLlmEarlyGap, v => _earlyGap = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodLlmSpikeDeaths, v => _spikeDeaths = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodLlmInterval, v =>
        {
            _interval = v;
            _nextCall = TimeSpan.Zero;
        }, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodWriterInterval, v =>
        {
            _writerInterval = v;
            _nextWriter = TimeSpan.Zero;
        }, true);
    }

    public void Reset()
    {
        _nextCall = TimeSpan.Zero;
        _nextWriter = TimeSpan.Zero;
        _lastCall = TimeSpan.Zero;
        _recentDeaths.Clear();
        LastDigest = string.Empty;
        LastReply = string.Empty;
        LastProblems.Clear();
        Calls = 0;
        Failures = 0;
    }

    private bool Active => _enabled && _mode is "assisted" or "full";

    private GodPromptPrototype Prompt => _proto.Index<GodPromptPrototype>("Default");

    /// <summary>
    ///     Makes the next call happen as soon as the minimum gap allows. For a prayer, a run of deaths, or an admin.
    /// </summary>
    public void RequestSoon()
    {
        var now = _timing.CurTime;
        var earliest = _lastCall == TimeSpan.Zero ? now : _lastCall + TimeSpan.FromSeconds(_earlyGap);
        var due = earliest > now ? earliest : now;

        if (_nextCall == TimeSpan.Zero || due < _nextCall)
            _nextCall = due;
    }

    private void OnDied(GodSubjectDiedEvent ev)
    {
        var now = _timing.CurTime;
        _recentDeaths.Add(now);
        _recentDeaths.RemoveAll(t => now - t > TimeSpan.FromSeconds(60));

        if (Active && _spikeDeaths > 0 && _recentDeaths.Count >= _spikeDeaths)
            RequestSoon();
    }

    private void OnPrayed(PrayedEvent ev)
    {
        if (Active && HasComp<SacrificialAltarComponent>(ev.Target))
            RequestSoon();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Active || _calling)
            return;

        var now = _timing.CurTime;

        if (_nextCall == TimeSpan.Zero)
            _nextCall = now + TimeSpan.FromSeconds(_interval);

        if (now < _nextCall)
            return;

        _ = TurnAsync();
    }

    /// <summary>
    ///     One turn: write the digest, ask, and offer what comes back. The writer prompt runs now and then too.
    /// </summary>
    public async Task TurnAsync()
    {
        if (_calling)
            return;

        _calling = true;
        try
        {
            var now = _timing.CurTime;
            _lastCall = now;
            _nextCall = now + TimeSpan.FromSeconds(_interval);

            await Decide();

            if (_writerInterval > 0f && (_nextWriter == TimeSpan.Zero || _timing.CurTime >= _nextWriter))
            {
                _nextWriter = _timing.CurTime + TimeSpan.FromSeconds(_writerInterval);
                await WriteLines();
            }
        }
        catch (Exception e)
        {
            Log.Error($"The model turn failed: {e}");
        }
        finally
        {
            _calling = false;
        }
    }

    private async Task Decide()
    {
        var drain = _observer.Buffer.DrainAll(_timing.CurTime);
        var result = _digest.Build(drain);

        if (result.Empty)
            return;

        // Mention lines that did not fit are folded into a summary first, so nothing is simply cut
        var summary = string.Empty;
        if (result.OverflowMentions.Count > 0)
        {
            summary = await Summarise(result.OverflowMentions);
            result = _digest.Build(drain, summary);
        }

        LastDigest = result.Text;
        Calls++;

        var reply = await _llm.Complete(new[]
        {
            new LlmMessage("system", Prompt.System),
            new LlmMessage("user", result.Text),
        });

        if (reply == null)
        {
            Failures++;
            _director.LlmFailing = true;
            _observer.Buffer.Restore(drain);
            return;
        }

        LastReply = reply;
        var parsed = GodReplyParser.Parse(reply);
        LastProblems.Clear();
        LastProblems.AddRange(parsed.Problems);

        if (!parsed.Parsed)
        {
            // An answer, but not one that can be used: not the endpoint's fault, nothing is put back
            Failures++;
            _director.LlmFailing = true;
            _director.RecordRejected(GodSource.Llm, "the reply held no JSON object");
            return;
        }

        _director.LlmFailing = false;
        _digest.Notes = parsed.Notes;

        foreach (var problem in parsed.Problems)
        {
            _director.RecordRejected(GodSource.Llm, problem);
        }

        foreach (var action in parsed.Actions)
        {
            _director.Submit(action, GodSource.Llm);
        }
    }

    private async Task<string> Summarise(List<GodQuote> lines)
    {
        var text = string.Join("\n", lines.Select(q => $"S{q.Subject}: \"{q.Text}\""));
        var summary = await _llm.Complete(new[]
        {
            new LlmMessage("system", Prompt.Summary),
            new LlmMessage("user", text),
        }, chained: true);

        return string.IsNullOrWhiteSpace(summary)
            ? $"{lines.Count} earlier lines about her were not read"
            : summary.Trim();
    }

    /// <summary>
    ///     Asks for new lines for the line bank. Every line is checked like a message: only known slots, nothing that reads as an
    ///     order, not a repeat. What passes joins the bank for this round.
    /// </summary>
    private async Task WriteLines()
    {
        var reply = await _llm.Complete(new[]
        {
            new LlmMessage("system", Prompt.Writer),
            new LlmMessage("user", "Write the lines now."),
        }, chained: true);

        if (reply == null)
            return;

        var validator = new GodActionValidator(_proto.Index<GodVoicePrototype>("Default"));
        var lines = _proto.Index<GodLinesPrototype>("Default");
        var sample = new Subject { Number = 7, Name = "Ivan Petrov" };

        foreach (var (category, written) in GodReplyParser.ParseLines(reply))
        {
            var known = category switch
            {
                "error" => lines.Error,
                "omen" => lines.Omen,
                _ => lines.Prayer,
            };

            if (!_director.ExtraLines.TryGetValue(category, out var extra))
                _director.ExtraLines[category] = extra = new List<string>();

            foreach (var raw in written)
            {
                var line = GodActionValidator.Clean(raw);
                if (extra.Count >= MaxExtraLines || extra.Contains(line) || known.Contains(line) || !SlotsAreKnown(line))
                    continue;

                var test = new GodAction { Type = GodActionType.Subtle, Targets = { 1 }, Text = GodScriptedSystem.Fill(line, sample, "C4") };
                if (validator.Validate(test, new GodValidationContext()) == null)
                    extra.Add(line);
            }
        }
    }

    private static bool SlotsAreKnown(string line)
    {
        return Slot.Matches(line).All(m => Slots.Contains(m.Groups[1].Value));
    }
}
