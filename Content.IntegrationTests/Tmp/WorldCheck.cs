using System.Linq;
using System.Threading.Tasks;
using Content.Server._ERRORGATE.World;
using Content.Shared.Doors.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tmp;

// Throwaway engine check of a built location (not committed).
[TestFixture]
public sealed class WorldCheck
{
    [Test]
    public async Task Check()
    {
        var seeds = (System.Environment.GetEnvironmentVariable("CHECK_SEEDS") ?? "1,2,3").Split(',').Select(int.Parse).ToArray();
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var world = server.EntMan.System<WorldSystem>();
        foreach (var id in new[] { "Village", "SnowCity" })
        {
            foreach (var seed in seeds)
            {
                await server.WaitPost(() => Assert.That(world.TryBuild(id, seed, out var e), e));
                await server.WaitRunTicks(5);
                await server.WaitAssertion(() =>
                {
                    var em = server.EntMan;
                    var map = em.System<SharedMapSystem>();
                    var xf = em.System<SharedTransformSystem>();
                    var loc = world.Locations[id];
                    int ok = 0, bad = 0, grids = 0, tiles = 0;
                    var owner = new System.Collections.Generic.Dictionary<Vector2i, string>();
                    int outOfRect = 0, dup = 0, outShape = 0;
                    foreach (var (grid2, p2) in loc.Built)
                    {
                        var gc = em.GetComponent<MapGridComponent>(grid2);
                        var origin = xf.GetWorldPosition(grid2);
                        foreach (var t in map.GetAllTiles(grid2, gc))
                        {
                            var wt = new Vector2i((int) System.MathF.Floor(origin.X + t.GridIndices.X + 0.5f), (int) System.MathF.Floor(origin.Y + t.GridIndices.Y + 0.5f));
                            if (wt.X < p2.Min.X || wt.Y < p2.Min.Y || wt.X > p2.Max.X || wt.Y > p2.Max.Y) { outOfRect++; if (outOfRect < 4) TestContext.Out.WriteLine($"CHK out of rect: {p2.Info.Path} turns {p2.Turns} min {p2.Min} max {p2.Max} tile {wt} origin {origin} localMin {p2.Info.LocalMin} size {p2.Info.Width}x{p2.Info.Height}"); }
                            if (!owner.TryAdd(wt, p2.Info.Path)) dup++;
                            if (!loc.Layout.InShape(wt)) { outShape++; if (outShape < 4) TestContext.Out.WriteLine($"CHK outside the shape: {p2.Info.Path} role {p2.Info.Role} rect {p2.Min}..{p2.Max} tile {wt} dist {loc.Layout.Dist(wt)}"); }
                        }
                    }
                    var builtSet = loc.Built.Select(b => b.Grid).ToHashSet();
                    var groundTiles = new System.Collections.Generic.HashSet<Vector2i>();
                    var q0 = em.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
                    while (q0.MoveNext(out var gu, out var gg, out var gx))
                    {
                        if (gx.MapID != loc.MapId || builtSet.Contains(gu)) continue;
                        foreach (var t in map.GetAllTiles(gu, gg)) groundTiles.Add(t.GridIndices);
                    }
                    var missing = loc.Layout.ShapeTiles().Where(t => !groundTiles.Contains(t) && !owner.ContainsKey(t)).ToList();
                    var extra = groundTiles.Where(t => !loc.Layout.InShape(t)).ToList();
                    TestContext.Out.WriteLine($"CHK shape tiles with no ground and no structure: {missing.Count} {string.Join(' ', missing.Take(6))}; ground tiles outside the shape: {extra.Count} {string.Join(' ', extra.Take(6))}");
                    var perGrid = new System.Collections.Generic.List<string>();
                    var q1 = em.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
                    while (q1.MoveNext(out var gu1, out var gg1, out var gx1))
                    {
                        if (gx1.MapID != loc.MapId) continue;
                        if (!builtSet.Contains(gu1)) perGrid.Add($"{gu1}:{map.GetAllTiles(gu1, gg1).Count()}:body");
                    }
                    TestContext.Out.WriteLine($"CHK non-structure grids: {string.Join(' ', perGrid)}; structure grids tile sum {loc.Built.Sum(b => map.GetAllTiles(b.Grid, em.GetComponent<MapGridComponent>(b.Grid)).Count())}");
                    var overlapGround = owner.Keys.Count(k => groundTiles.Contains(k));
                    var voidUnder = owner.Keys.Count(k => !groundTiles.Contains(k));
                    TestContext.Out.WriteLine($"CHK {id} seed {seed}: tiles outside their rectangle {outOfRect}, shared by two structures {dup}, structure tiles over ground {overlapGround}, ground tiles {groundTiles.Count}, structure tiles {owner.Count}");
                    foreach (var (grid, p) in loc.Built)
                    {
                        var doors = new System.Collections.Generic.List<Vector2i>();
                        var en = em.GetComponent<TransformComponent>(grid).ChildEnumerator;
                        while (en.MoveNext(out var child))
                        {
                            if (!em.HasComponent<DoorComponent>(child)) continue;
                            var w = xf.GetWorldPosition(child) - xf.GetWorldPosition(loc.Ground);
                            doors.Add(new Vector2i((int) System.MathF.Floor(w.X), (int) System.MathF.Floor(w.Y)));
                        }
                        if (doors.Contains(p.Door)) ok++; else bad++;
                    }
                    var q = em.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
                    while (q.MoveNext(out var uid, out var g, out var x))
                    {
                        if (x.MapID != loc.MapId) continue;
                        grids++;
                        tiles += map.GetAllTiles(uid, g).Count();
                    }
                    TestContext.Out.WriteLine("CHK " + id + " seed " + seed + ": new hand-cut buildings placed: " + string.Join(", ", loc.Built.Where(b2 => b2.Placement.Info.Path.Contains("apartments") || b2.Placement.Info.Path.Contains("dormitory") || b2.Placement.Info.Path.Contains("factory")).Select(b2 => b2.Placement.Info.Path + " " + b2.Placement.Info.Width + "x" + b2.Placement.Info.Height)));
                    TestContext.Out.WriteLine($"CHK {id} seed {seed}: grids {grids}, tiles {tiles}, structures {loc.Built.Count} (front door where predicted {ok}, elsewhere {bad}), entities {loc.Entities}, {loc.BuildMilliseconds:0} ms");
                });
            }
        }
        await pair.CleanReturnAsync();
    }
}
