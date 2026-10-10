using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ERRORGATE.Anomalies;

/// <summary>
///     Screen static that builds up as the local player gets close to an arc fault.
/// </summary>
public sealed class ArcFaultOverlay : Overlay
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private static readonly ProtoId<ShaderPrototype> StaticShader = "ErrorgateAnomalyStatic";

    // Strength of the static right next to the fault
    private const float MaxStrength = 0.55f;
    private const float MaxStrengthReducedMotion = 0.2f;

    private readonly SharedTransformSystem _xformSys;
    private readonly ShaderInstance _shader;
    private float _strength;

    public ArcFaultOverlay()
    {
        IoCManager.InjectDependencies(this);
        _xformSys = _entMan.System<SharedTransformSystem>();
        _shader = _proto.Index(StaticShader).InstanceUnique();
        ZIndex = 50;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var proximity = AnomalyProximity.Get(_entMan, _xformSys, _player, ErrorgateAnomalyKind.Arc);

        // Already clearly there at three quarters of the way out (6 of 8 tiles), ramp up quickly when getting close,
        // fade out slower
        var target = MathF.Pow(proximity, 0.6f);
        var speed = target > _strength ? 6f : 2f;
        _strength += (target - _strength) * MathF.Min(1f, speed * args.DeltaSeconds);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_strength < 0.01f)
            return false;

        if (!_entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eye))
            return false;

        return args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var max = _cfg.GetCVar(CCVars.ReducedMotion) ? MaxStrengthReducedMotion : MaxStrength;
        _shader.SetParameter("strength", _strength * max);

        var handle = args.ScreenHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.ViewportBounds, Color.White);
        handle.UseShader(null);
    }
}

/// <summary>
///     Keeps <see cref="ArcFaultOverlay"/> on the screen.
/// </summary>
public sealed class ArcFaultOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlays.AddOverlay(new ArcFaultOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<ArcFaultOverlay>();
    }
}
