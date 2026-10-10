using Content.Shared._ERRORGATE.AiGod;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ERRORGATE.AiGod;

/// <summary>
///     Screen static when MACHINATION OF RUIN glitches the player (<see cref="GodGlitchEvent"/>). It uses a red, fainter version of the static
///     shader of the arc fault, builds up fast and fades out. With reduced motion it is much weaker.
/// </summary>
public sealed class GodGlitchOverlay : Overlay
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private static readonly ProtoId<ShaderPrototype> StaticShader = "ErrorgateGodStatic";

    private const float ReducedMotionFactor = 0.3f;

    private readonly ShaderInstance _shader;
    private float _remaining;
    private float _total = 1f;
    private float _strength;

    public GodGlitchOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(StaticShader).InstanceUnique();
        ZIndex = 60;
    }

    public void Start(float seconds, float strength)
    {
        _total = Math.Max(0.1f, seconds);
        _remaining = _total;
        _strength = Math.Clamp(strength, 0f, 1f);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        _remaining = Math.Max(0f, _remaining - args.DeltaSeconds);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_remaining <= 0f)
            return false;

        if (!_entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eye))
            return false;

        return args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Quick on, slow out
        var fade = Math.Clamp(_remaining / _total, 0f, 1f);
        var rise = Math.Clamp((_total - _remaining) / 0.15f, 0f, 1f);
        var factor = _cfg.GetCVar(CCVars.ReducedMotion) ? ReducedMotionFactor : 1f;

        _shader.SetParameter("strength", _strength * fade * rise * factor);

        var handle = args.ScreenHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.ViewportBounds, Color.White);
        handle.UseShader(null);
    }
}

/// <summary>
///     Receives the glitch from the server and keeps the overlay on the screen.
/// </summary>
public sealed class GodGlitchOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;

    private GodGlitchOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new GodGlitchOverlay();
        _overlays.AddOverlay(_overlay);
        SubscribeNetworkEvent<GodGlitchEvent>(OnGlitch);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<GodGlitchOverlay>();
    }

    private void OnGlitch(GodGlitchEvent ev)
    {
        _overlay?.Start(ev.Seconds, ev.Strength);
    }
}
