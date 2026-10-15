using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._ERRORGATE.Physics;

/// <summary>
///     Empties the list of grids that moved, on every map, right after the physics step.
///
///     The broadphase keeps that list until the step has found a moving body to check; on a map where nothing moves (every location of
///     the world but the one people are in) it returns early and never empties it. The grids stay flagged and are checked against each
///     other again in every step, and the client does that for every predicted step of every map (it holds the grids of all of them):
///     with the seventy grids of the city that cost most of a frame. The step has already handled the flagged grids when it returns, so
///     emptying the list afterwards is what the engine does itself on its normal path.
/// </summary>
public sealed class MovedGridsFlushSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(SharedPhysicsSystem));
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<MovedGridsComponent>();
        while (query.MoveNext(out _, out var moved))
        {
            if (moved.MovedGrids.Count > 0)
                moved.MovedGrids.Clear();
        }
    }
}
