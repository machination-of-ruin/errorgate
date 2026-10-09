using Content.Server.Movement.Components;
using Content.Shared.Camera;
using Content.Shared.Movement.Systems;

namespace Content.Server._ERRORGATE.LookFar;

/// <summary>
///     Looking far moves the camera up to <c>MaxOffset</c> tiles away from the player, so the player's PVS range has
///     to grow with it or the far corners of the screen show entities popping in.
/// </summary>
public sealed class LookFarPvsSystem : EntitySystem
{
    [Dependency] private readonly SharedContentEyeSystem _eye = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EyeCursorOffsetComponent, GetEyePvsScaleEvent>(OnGetScale);
        SubscribeLocalEvent<EyeCursorOffsetComponent, MapInitEvent>(OnMapInit);
    }

    private void OnGetScale(Entity<EyeCursorOffsetComponent> ent, ref GetEyePvsScaleEvent args)
    {
        args.Scale += ent.Comp.PvsIncrease;
    }

    private void OnMapInit(Entity<EyeCursorOffsetComponent> ent, ref MapInitEvent args)
    {
        _eye.UpdatePvsScale(ent);
    }
}
