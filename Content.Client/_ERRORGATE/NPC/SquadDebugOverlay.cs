using System.Numerics;
using Content.Shared._ERRORGATE.NPC;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Console;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._ERRORGATE.NPC;

/// <summary>
///     <c>showsquads</c>: draws every squad (task, state, goal, the guard radius), all in red, its members with what they are doing
///     (the plan step) and where they are going, and the building points patrols pick from. Needs the debug admin flag.
///     The walking nodes of the pathfinder: the <c>pathfinder</c> window.
/// </summary>
public sealed class SquadDebugSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IResourceCache _cache = default!;

    private SquadDebugOverlay? _overlay;

    public bool Enabled => _overlay != null;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SquadDebugEvent>(OnEvent);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Set(false);
    }

    public void Set(bool enable)
    {
        if (enable == Enabled)
            return;

        if (enable)
        {
            _overlay = new SquadDebugOverlay(_cache);
            _overlays.AddOverlay(_overlay);
        }
        else
        {
            _overlays.RemoveOverlay(_overlay!);
            _overlay = null;
        }

        RaiseNetworkEvent(new SquadDebugRequestEvent(enable));
    }

    private void OnEvent(SquadDebugEvent ev)
    {
        if (_overlay == null)
            return;

        _overlay.Squads = ev.Squads;
        _overlay.Points = ev.Points;
    }
}

public sealed class SquadDebugOverlay : Overlay
{
    private static readonly Color Red = Color.FromHex("#FF0000");

    private readonly Font _font;

    public List<SquadDebugSquad> Squads = new();
    public List<(MapId Map, Vector2 Position)> Points = new();

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public SquadDebugOverlay(IResourceCache cache)
    {
        _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.ViewportControl is not { } view)
            return;

        var handle = args.ScreenHandle;
        var pixelsPerTile = (view.WorldToScreen(Vector2.UnitX) - view.WorldToScreen(Vector2.Zero)).Length();

        foreach (var (map, position) in Points)
        {
            if (map != args.MapId || !args.WorldAABB.Contains(position))
                continue;

            handle.DrawCircle(view.WorldToScreen(position), 4f, Color.White.WithAlpha(0.6f), false);
        }

        foreach (var squad in Squads)
        {
            var alone = squad.Id == NetEntity.Invalid;
            var color = Red;
            if (!alone && squad.Map != args.MapId)
                continue;

            if (squad.Goal is { } goal)
            {
                var goalScreen = view.WorldToScreen(goal);
                handle.DrawCircle(goalScreen, 8f, color.WithAlpha(0.8f), false);
                handle.DrawString(_font, goalScreen + new Vector2(10, -6), $"goal {squad.Id}", color);
            }

            if (squad.Home is { } home)
                handle.DrawCircle(view.WorldToScreen(home), squad.HomeRadius * pixelsPerTile, color.WithAlpha(0.15f), true);

            Vector2? header = null;
            foreach (var member in squad.Members)
            {
                if (!args.WorldAABB.Contains(member.Position) && (member.Target == null || !args.WorldAABB.Contains(member.Target.Value)))
                    continue;

                var screen = view.WorldToScreen(member.Position);
                header ??= screen;
                var shade = member.Dead ? Color.Gray : color;
                handle.DrawCircle(screen, member.Leader ? 9f : 6f, shade.WithAlpha(0.8f), member.Leader);
                if (member.Target is { } target)
                    handle.DrawLine(screen, view.WorldToScreen(target), shade.WithAlpha(0.5f));

                handle.DrawString(_font, screen + new Vector2(10, -4), $"{member.Name} {member.Plan}", shade);
            }

            if (header is { } at)
                handle.DrawString(_font, at + new Vector2(10, -18), alone ? "no squad" : $"{squad.Faction} squad {squad.Id}: {squad.Task}, {squad.State}", color);
        }
    }
}

public sealed class ShowSquadsCommand : IConsoleCommand
{
    public string Command => "showsquads";
    public string Description => "Shows the NPC squads: tasks, goals, members and what they do, and the building points of patrols. Needs the debug admin flag.";
    public string Help => Command;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var system = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SquadDebugSystem>();
        system.Set(!system.Enabled);
        shell.WriteLine(system.Enabled ? "Squad overlay on." : "Squad overlay off.");
    }
}
