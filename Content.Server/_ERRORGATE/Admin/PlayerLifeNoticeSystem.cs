using Content.Server.Chat.Managers;
using Content.Shared.GameTicking;

namespace Content.Server._ERRORGATE.Admin;

/// <summary>
///     Tells the admins in the admin chat when a player spawns. (Deaths are announced by <c>DeathVoidSystem</c>, which sees
///     every death once, also when the body is gibbed and its mind is already gone.)
/// </summary>
public sealed class PlayerLifeNoticeSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawned);
    }

    private void OnSpawned(PlayerSpawnCompleteEvent args)
    {
        var how = args.LateJoin ? "joined" : "spawned";
        _chat.SendAdminAnnouncement($"SPAWN: {args.Player.Name} {how} as {Name(args.Mob)}.");
    }
}
