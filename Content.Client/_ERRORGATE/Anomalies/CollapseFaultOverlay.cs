using Content.Shared._ERRORGATE.Anomalies;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ERRORGATE.Anomalies;

/// <summary>
///     Drains the colour from the screen as the local player gets close to a collapse fault.
/// </summary>
public sealed class CollapseFaultOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private static readonly ProtoId<ShaderPrototype> SaturationShader = "SaturationScale";

    // Colour that is left right next to the fault
    private const float MinSaturation = 0.05f;

    private readonly SharedTransformSystem _xformSys;
    private readonly ShaderInstance _shader;
    private float _drain;

    public CollapseFaultOverlay()
    {
        IoCManager.InjectDependencies(this);
        _xformSys = _entMan.System<SharedTransformSystem>();
        _shader = _proto.Index(SaturationShader).InstanceUnique();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var proximity = AnomalyProximity.Get(_entMan, _xformSys, _player, ErrorgateAnomalyKind.Collapse);

        // Smoothstep, so the world greys out slowly at first and then all at once
        var target = proximity * proximity * (3f - 2f * proximity);
        _drain += (target - _drain) * MathF.Min(1f, 3f * args.DeltaSeconds);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_drain < 0.01f)
            return false;

        if (!_entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eye))
            return false;

        return args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("saturation", MathHelper.Lerp(1f, MinSaturation, _drain));

        var handle = args.WorldHandle;
        handle.SetTransform(System.Numerics.Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}

/// <summary>
///     Keeps <see cref="CollapseFaultOverlay"/> on the screen.
/// </summary>
public sealed class CollapseFaultOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlays.AddOverlay(new CollapseFaultOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<CollapseFaultOverlay>();
    }
}
