using Content.Server.Chat.Managers;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     Tells the admins in the admin chat what she does: when a decision waits for approval (with the commands to answer
///     it, without this approval mode is invisible) and when an action is done.
/// </summary>
public sealed class GodAdminNoticeSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly GodDirectorSystem _director = default!;

    public override void Initialize()
    {
        base.Initialize();

        _director.DecisionPending += OnPending;
        _director.DecisionDone += OnDone;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _director.DecisionPending -= OnPending;
        _director.DecisionDone -= OnDone;
    }

    /// <summary>
    ///     Every action she takes (or would take, in a dry run) is shown to the admins as it happens.
    /// </summary>
    private void OnDone(GodDecision decision)
    {
        var what = decision.Status == GodActionStatus.DryRun ? "would do (dry run)" : "did";
        _chat.SendAdminAnnouncement($"AI GOD #{decision.Id} ({decision.Source}) {what}: {decision.Action}.");
    }

    private void OnPending(GodDecision decision)
    {
        _chat.SendAdminAnnouncement(
            $"AI GOD #{decision.Id} ({decision.Source}) waits for approval: {decision.Action}. " +
            $"godapprove {decision.Id} or goddeny {decision.Id}, it expires in {_director.ApprovalTimeout} s.");
    }
}
