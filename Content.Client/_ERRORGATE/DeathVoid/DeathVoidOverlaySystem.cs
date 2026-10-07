using System.Numerics;
using Content.Shared._ERRORGATE.DeathVoid;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Client._ERRORGATE.DeathVoid;

/// <summary>
///     Blacks out the screen while the local player is in the death void.
/// </summary>
public sealed class DeathVoidOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;

    private DeathVoidOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeathVoidComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<DeathVoidComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<DeathVoidComponent, LocalPlayerDetachedEvent>(OnDetached);
        SubscribeLocalEvent<DeathVoidComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<DeathVoidComponent> ent, ref ComponentStartup args)
    {
        if (_player.LocalEntity == ent.Owner)
            AddOverlay();
    }

    private void OnAttached(Entity<DeathVoidComponent> ent, ref LocalPlayerAttachedEvent args) => AddOverlay();

    private void OnDetached(Entity<DeathVoidComponent> ent, ref LocalPlayerDetachedEvent args) => RemoveOverlay();

    private void OnShutdown(Entity<DeathVoidComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity == ent.Owner)
            RemoveOverlay();
    }

    private void AddOverlay()
    {
        if (_overlay != null)
            return;

        _overlay = new DeathVoidOverlay();
        _overlays.AddOverlay(_overlay);
    }

    private void RemoveOverlay()
    {
        if (_overlay == null)
            return;

        _overlays.RemoveOverlay(_overlay);
        _overlay = null;
    }
}

public sealed class DeathVoidOverlay : Overlay
{
    [Dependency] private readonly IResourceCache _cache = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Font _font;

    public DeathVoidOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 1000;
        _font = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 36);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        handle.DrawRect(args.ViewportBounds, Color.Black);

        var text = Loc.GetString("errorgate-death-void-title");
        var size = handle.GetDimensions(_font, text, 1f);
        var pos = args.ViewportBounds.Center - size / 2;
        handle.DrawString(_font, new Vector2(pos.X, pos.Y), text, Color.DarkRed);
    }
}
