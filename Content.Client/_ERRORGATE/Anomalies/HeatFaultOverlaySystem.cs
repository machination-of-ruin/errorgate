using Robust.Client.Graphics;

namespace Content.Client._ERRORGATE.Anomalies;

/// <summary>
///     Keeps <see cref="HeatFaultOverlay"/> on the screen.
/// </summary>
public sealed class HeatFaultOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlays.AddOverlay(new HeatFaultOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<HeatFaultOverlay>();
    }
}
