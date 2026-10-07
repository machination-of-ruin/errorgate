using System.Globalization;
using Content.Server.Chat.Managers;
using Content.Server.Mind;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;

namespace Content.Server.Roles.Jobs;

/// <summary>
///     Handles the job data on mind entities.
/// </summary>
public sealed class JobSystem : SharedJobSystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly RoleSystem _roles = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoleAddedEvent>(OnRoleAddedEvent);
        SubscribeLocalEvent<RoleRemovedEvent>(OnRoleRemovedEvent);
    }

    private void OnRoleAddedEvent(RoleAddedEvent args)
    {
        if (args.RoleTypeUpdate)
            _roles.RoleUpdateMessage(args.Mind);
    }

    private void OnRoleRemovedEvent(RoleRemovedEvent args)
    {
        if (args.RoleTypeUpdate)
            _roles.RoleUpdateMessage(args.Mind);
    }

    public void MindAddJob(EntityUid mindId, string jobPrototypeId)
    {
        if (MindHasJobWithId(mindId, jobPrototypeId))
            return;

        _roles.MindAddJobRole(mindId, null, false, jobPrototypeId);
    }
}
