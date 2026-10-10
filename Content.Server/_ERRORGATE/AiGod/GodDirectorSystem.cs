using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Shared._ERRORGATE.AiGod;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     Where every action of MACHINATION OF RUIN goes through, whoever thought of it (the scripted rules, the model, an
///     admin): it is checked, paid for, held for approval or logged as a dry run, and only then done. The directors that
///     think of actions call <see cref="Submit"/>, they never touch the world themselves.
/// </summary>
public sealed class GodDirectorSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly GodLedgerSystem _ledger = default!;
    [Dependency] private readonly SectorSystem _sectors = default!;

    private const int LogSize = 100;
    public const string Sender = "MACHINATION OF RUIN";

    public readonly GodBudget Budget = new();

    /// <summary>The newest decisions, oldest first.</summary>
    public readonly List<GodDecision> Log = new();

    /// <summary>Stops everything (godpause) without changing the valves.</summary>
    public bool Paused;

    private GodActionValidator? _validator;
    private int _nextId = 1;
    private bool _enabled;
    private bool _dryRun = true;
    private bool _approval = true;
    private bool _uppercase = true;
    private int _approvalTimeout = 120;
    private int _minPlayers = 1;
    private float _quietSeconds = 120f;
    private int _subtleMax = 140;
    private int _announceMax = 200;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());

        Subs.CVar(_cfg, ErrorgateCVars.GodEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodDryRun, v => _dryRun = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodApproval, v => _approval = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodUppercase, v => _uppercase = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodApprovalTimeoutSeconds, v => _approvalTimeout = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodMinPlayers, v => _minPlayers = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodQuietSeconds, v => _quietSeconds = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodSubtleMaxLength, v => _subtleMax = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodAnnounceMaxLength, v => _announceMax = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodBudgetPerMinute, v => Budget.PerMinute = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodBudgetCap, v => Budget.Cap = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodTargetCooldown, v => Budget.TargetCooldown = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodAnnounceCooldown, v => Budget.AnnounceCooldown = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.GodAnnouncePerHour, v => Budget.AnnouncePerHour = v, true);

        Budget.Reset(_timing.CurTime);
    }

    public void Reset()
    {
        Log.Clear();
        _nextId = 1;
        Paused = false;
        Budget.Reset(_timing.CurTime);
    }

    private GodActionValidator Validator => _validator ??= new GodActionValidator(_proto.Index<GodVoicePrototype>("Default"));

    /// <summary>
    ///     Offers an action. It is checked, paid for and then done, held for an admin, or logged as a dry run.
    ///     The decision says what became of it.
    /// </summary>
    public GodDecision Submit(GodAction action, GodSource source)
    {
        var now = _timing.CurTime;
        var decision = new GodDecision { Id = _nextId++, Time = now, Source = source, Action = action, Cost = GodBudget.CostOf(action) };
        Remember(decision);

        Budget.Advance(now);

        var reason = Refusal(action, source, now);
        if (reason != null)
        {
            decision.Status = GodActionStatus.Rejected;
            decision.Reason = reason;
            return decision;
        }

        // An admin who forces an action has already decided: no approval, no points
        if (source != GodSource.Admin && _approval)
        {
            decision.Status = GodActionStatus.Pending;
            decision.ExpiresAt = now + TimeSpan.FromSeconds(_approvalTimeout);
            return decision;
        }

        Carry(decision, source != GodSource.Admin);
        return decision;
    }

    /// <summary>
    ///     Why an action may not go ahead right now, null when it may.
    /// </summary>
    private string? Refusal(GodAction action, GodSource source, TimeSpan now)
    {
        if (!_enabled)
            return "switched off";

        if (Paused)
            return "paused";

        if (source != GodSource.Admin)
        {
            if ((now - _ticker.RoundStartTimeSpan).TotalSeconds < _quietSeconds)
                return "quiet period at the start of the round";

            if (_ledger.Ledger.All.Count(s => s.Alive) < _minPlayers)
                return "too few players";
        }

        var invalid = Validator.Validate(action, Context());
        if (invalid != null)
            return invalid;

        return source == GodSource.Admin ? null : Budget.Check(action, now);
    }

    /// <summary>
    ///     Does the action (or only pretends, in a dry run) and settles the decision.
    /// </summary>
    private void Carry(GodDecision decision, bool paid)
    {
        var now = _timing.CurTime;
        if (paid)
            Budget.Spend(decision.Action, now);

        if (_dryRun)
        {
            decision.Status = GodActionStatus.DryRun;
            return;
        }

        Execute(decision.Action);
        decision.Status = GodActionStatus.Executed;
    }

    /// <summary>
    ///     An admin says yes. The action is checked again, subjects may have died since.
    /// </summary>
    public bool Approve(int id, out string message)
    {
        var decision = Log.FirstOrDefault(d => d.Id == id);
        if (decision == null || decision.Status != GodActionStatus.Pending)
        {
            message = $"No pending decision #{id}.";
            return false;
        }

        var now = _timing.CurTime;
        Budget.Advance(now);

        var reason = Refusal(decision.Action, GodSource.Admin, now);
        if (reason != null)
        {
            decision.Status = GodActionStatus.Rejected;
            decision.Reason = reason;
            message = $"#{id} can no longer be done: {reason}.";
            return false;
        }

        Carry(decision, paid: true);
        message = decision.ToString();
        return true;
    }

    public bool Deny(int id, out string message)
    {
        var decision = Log.FirstOrDefault(d => d.Id == id);
        if (decision == null || decision.Status != GodActionStatus.Pending)
        {
            message = $"No pending decision #{id}.";
            return false;
        }

        decision.Status = GodActionStatus.Denied;
        message = decision.ToString();
        return true;
    }

    public IEnumerable<GodDecision> Pending => Log.Where(d => d.Status == GodActionStatus.Pending);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        Budget.Advance(now);

        foreach (var decision in Log)
        {
            if (decision.Status == GodActionStatus.Pending && now >= decision.ExpiresAt)
            {
                decision.Status = GodActionStatus.Expired;
                decision.Reason = "nobody answered";
            }
        }
    }

    /// <summary>
    ///     Her recent actions as lines for the digest, newest last: what she did, to whom, how long ago.
    /// </summary>
    public List<string> RecentLines(int count)
    {
        var now = _timing.CurTime;
        return Log
            .Where(d => d.Status is GodActionStatus.Executed or GodActionStatus.DryRun)
            .TakeLast(count)
            .Select(d =>
            {
                var age = now - d.Time;
                var ago = age.TotalMinutes >= 1 ? $"{(int) age.TotalMinutes}m" : $"{(int) age.TotalSeconds}s";
                return $"T-{ago} {d.Action}";
            })
            .ToList();
    }

    /// <summary>
    ///     One line of state for the digest and for godstatus.
    /// </summary>
    public string StateLine()
    {
        var mode = _cfg.GetCVar(ErrorgateCVars.GodMode);
        return $"budget {Budget.Points:0}/{Budget.Cap:0} | mode {mode} | {(_dryRun ? "dry-run" : "live")} | approval {(_approval ? "on" : "off")}{(Paused ? " | paused" : string.Empty)}";
    }

    private void Remember(GodDecision decision)
    {
        Log.Add(decision);
        if (Log.Count > LogSize)
            Log.RemoveRange(0, Log.Count - LogSize);
    }

    private GodValidationContext Context()
    {
        return new GodValidationContext
        {
            SubtleMaxLength = _subtleMax,
            AnnounceMaxLength = _announceMax,
            SubjectExists = n => _ledger.Ledger.ByNumber(n) != null,
            SubjectReachable = n => _ledger.Ledger.ByNumber(n) is { } s && _ledger.TryGetReachable(s, out _, out _),
            SectorExists = SectorExists,
        };
    }

    private bool SectorExists(string sector)
    {
        // Every sector of every grid the subjects are on; the names are only letters and numbers
        foreach (var subject in _ledger.Ledger.All)
        {
            if (subject.SectorsVisited.Contains(sector) || subject.Sector == sector)
                return true;
        }

        return _sectors.AnyGridHas(sector);
    }

    // Doing it

    private void Execute(GodAction action)
    {
        switch (action.Type)
        {
            case GodActionType.Subtle:
                foreach (var number in action.Targets)
                {
                    if (_ledger.Ledger.ByNumber(number) is { } subject && _ledger.TryGetReachable(subject, out var body, out var session))
                        SendSubtle(body, session, action.Text);
                }

                break;

            case GodActionType.Announce:
                var text = Shown(action.Text);
                _chat.DispatchGlobalAnnouncement(text, Sender, playSound: true, colorOverride: Color.DarkRed);
                break;

            case GodActionType.Glitch:
                foreach (var subject in GlitchTargets(action))
                {
                    if (_ledger.TryGetReachable(subject, out _, out var session))
                        RaiseNetworkEvent(new GodGlitchEvent(4f, 0.8f), session);
                }

                break;
        }
    }

    private IEnumerable<Subject> GlitchTargets(GodAction action)
    {
        if (action.Sector != null)
            return _ledger.Ledger.All.Where(s => s.Alive && s.Sector == action.Sector).ToList();

        return action.Targets.Select(n => _ledger.Ledger.ByNumber(n)).OfType<Subject>().ToList();
    }

    private void SendSubtle(EntityUid body, ICommonSession session, string text)
    {
        var shown = Shown(text);

        _popup.PopupEntity(shown, body, session, PopupType.Large);
        _chatManager.ChatMessageToOne(
            ChatChannel.Local,
            shown,
            $"[italic][color=#8b0000]{FormattedMessage.EscapeText(shown)}[/color][/italic]",
            EntityUid.Invalid,
            false,
            session.Channel,
            Color.DarkRed);

        _adminLogger.Add(LogType.AdminMessage, LogImpact.Low, $"{ToPrettyString(body):player} received a subtle message from {Sender}: {shown}");
    }

    private string Shown(string text)
    {
        return _uppercase ? text.ToUpperInvariant() : text;
    }
}
