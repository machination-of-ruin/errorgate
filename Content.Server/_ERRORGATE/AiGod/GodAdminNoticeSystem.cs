using Content.Server.Chat.Managers;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     Tells the admins in the admin chat when a decision of hers waits for approval, with the commands to answer it. Without
///     this approval mode is invisible: nothing happens until someone thinks to run godlog.
/// </summary>
public sealed class GodAdminNoticeSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly GodDirectorSystem _director = default!;

    public override void Initialize()
    {
        base.Initialize();

        _director.DecisionPending += OnPending;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _director.DecisionPending -= OnPending;
    }

    private void OnPending(GodDecision decision)
    {
        _chat.SendAdminAnnouncement(
            $"AI GOD #{decision.Id} ({decision.Source}) waits for approval: {decision.Action}. " +
            $"godapprove {decision.Id} or goddeny {decision.Id}, it expires in {_director.ApprovalTimeout} s.");
    }
}
