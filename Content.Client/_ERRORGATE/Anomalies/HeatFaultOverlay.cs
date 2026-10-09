using Content.Client.Resources;
using System.Numerics;
using Content.Client.Graphics;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Color = Robust.Shared.Maths.Color;
using Texture = Robust.Client.Graphics.Texture;

namespace Content.Client._ERRORGATE.Anomalies;

/// <summary>
///     Heat haze over every heat fault, drawn with the same shader as the hot gas tile haze
///     (<c>GasTileHeatBlurOverlay</c>), but around the fault itself. It works on grids without any air.
/// </summary>
public sealed class HeatFaultOverlay : Overlay
{
    public override bool RequestScreenTexture { get; set; } = true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly ProtoId<ShaderPrototype> HeatShader = "HeatBlur";

    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SharedTransformSystem _xformSys;
    private readonly ShaderInstance _shader;
    private readonly Texture _noiseTexture;
    private readonly Texture _gradientTexture;
    private readonly OverlayResourceCache<CachedResources> _resources = new();
    private readonly List<(Vector2 Position, float Radius, float Phase)> _visible = new();

    // The haze reaches a bit past the danger radius, so it can be seen before it burns
    private const float HazeScale = 2.2f;

    private const float ShaderStrength = 0.05f;
    private const float ShaderScale = 1f;
    private const float ShaderSpeed = 0.5f;

    private const float ShaderStrengthForReducedMotion = 0.012f;
    private const float ShaderScaleReducedMotion = 0.5f;
    private const float ShaderSpeedReducedMotion = 0.25f;

    public HeatFaultOverlay()
    {
        IoCManager.InjectDependencies(this);
        _xformSys = _entManager.System<SharedTransformSystem>();

        _noiseTexture = _resourceCache.GetTexture("/Textures/Effects/HeatBlur/perlin_noise.png");
        _gradientTexture = _resourceCache.GetTexture("/Textures/Effects/HeatBlur/soft_circle.png");

        _shader = _proto.Index(HeatShader).InstanceUnique();
        _configManager.OnValueChanged(CCVars.ReducedMotion, SetReducedMotion, invokeImmediately: true);
    }

    private void SetReducedMotion(bool reducedMotion)
    {
        _shader.SetParameter("strength_scale", reducedMotion ? ShaderStrengthForReducedMotion : ShaderStrength);
        _shader.SetParameter("spatial_scale", reducedMotion ? ShaderScaleReducedMotion : ShaderScale);
        _shader.SetParameter("speed_scale", reducedMotion ? ShaderSpeedReducedMotion : ShaderSpeed);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return false;

        _visible.Clear();
        var query = _entManager.EntityQueryEnumerator<ErrorgateAnomalyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anomaly, out var xform))
        {
            if (anomaly.Kind != ErrorgateAnomalyKind.Heat || xform.MapID != args.MapId)
                continue;

            var position = _xformSys.GetWorldPosition(xform);
            var reach = anomaly.Radius * HazeScale;
            if (!args.WorldAABB.Intersects(Box2.CenteredAround(position, new Vector2(reach * 2f, reach * 2f))))
                continue;

            _visible.Add((position, anomaly.Radius, uid.Id));
        }

        if (_visible.Count == 0)
            return false;

        var res = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());
        var target = args.Viewport.RenderTarget;

        // Probably the resolution of the game window changed, remake the texture.
        if (res.HeatTarget?.Texture.Size != target.Size)
        {
            res.HeatTarget?.Dispose();
            res.HeatTarget = _clyde.CreateRenderTarget(
                target.Size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                name: nameof(HeatFaultOverlay));
        }

        var worldHandle = args.WorldHandle;
        var worldToViewportLocal = args.Viewport.GetWorldToLocalMatrix();
        var time = (float) _timing.RealTime.TotalSeconds;

        worldHandle.UseShader(_proto.Index(UnshadedShader).Instance());

        // Encode where and how strong the haze is in the red channel
        worldHandle.RenderInRenderTarget(res.HeatTarget,
            () =>
            {
                worldHandle.SetTransform(worldToViewportLocal);

                foreach (var (position, radius, phase) in _visible)
                {
                    var flicker = 0.8f + 0.2f * MathF.Sin(time * 2.3f + phase);
                    var size = radius * HazeScale * 2f;
                    worldHandle.DrawTextureRect(
                        _gradientTexture,
                        Box2.CenteredAround(position, new Vector2(size, size)),
                        new Color(flicker, 0f, 0f));
                }
            },
            // This clears the buffer to all zero first...
            new Color(0, 0, 0, 0));

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var res = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());

        if (ScreenTexture is null || res.HeatTarget is null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("NOISE_TEXTURE", _noiseTexture);

        args.WorldHandle.SetTransform(Matrix3x2.Identity);
        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawTextureRect(res.HeatTarget.Texture, args.WorldBounds);

        args.WorldHandle.UseShader(null);
        args.WorldHandle.SetTransform(Matrix3x2.Identity);
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();

        _configManager.UnsubValueChanged(CCVars.ReducedMotion, SetReducedMotion);
        base.DisposeBehavior();
    }

    internal sealed class CachedResources : IDisposable
    {
        public IRenderTexture? HeatTarget;

        public void Dispose()
        {
            HeatTarget?.Dispose();
        }
    }
}
