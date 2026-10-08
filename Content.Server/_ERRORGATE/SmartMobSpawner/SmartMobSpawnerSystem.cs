using System.Threading;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._ERRORGATE.SmartMobSpawner;

public sealed class SmartMobSpawnerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SmartMobSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SmartMobSpawnerComponent, ComponentShutdown>(OnSpawnerShutdown);
        SubscribeLocalEvent<SmartMobSpawnerSpawnedComponent, ComponentShutdown>(OnMobCompShutdown);
    }

    private void OnMapInit(EntityUid uid, SmartMobSpawnerComponent component, MapInitEvent args)
    {
        if (component.SpawnMobOnInit)
            SpawnMob(uid, component);
        else
            StartRespawnTimer(uid, component);
    }

    private void OnSpawnerShutdown(EntityUid uid, SmartMobSpawnerComponent component, ComponentShutdown args)
    {
        component.TokenSource?.Cancel();
    }

    private void OnMobCompShutdown(EntityUid uid, SmartMobSpawnerSpawnedComponent component, ComponentShutdown args)
    {
        var spawner = component.SpawnedBy;

        if (TerminatingOrDeleted(spawner))
            return;

        if (!TryComp<SmartMobSpawnerComponent>(spawner, out var spawnerComp))
        {
            Log.Error($"Spawned mob {uid} was removed but spawner {spawner} has no SmartMobSpawnerComponent!");
            return;
        }

        StartRespawnTimer(spawner, spawnerComp);
    }

    private void StartRespawnTimer(EntityUid uid, SmartMobSpawnerComponent component)
    {
        component.TokenSource?.Cancel();
        component.TokenSource = new CancellationTokenSource();
        component.RespawnTime = (int) (3600f / component.SpawnRate);

        Log.Debug($"Mob spawner {uid} started a {component.RespawnTime} seconds respawn timer for a {component.MobPrototype}");
        Timer.Spawn(TimeSpan.FromSeconds(component.RespawnTime), () => OnTimerFired(uid, component), component.TokenSource.Token);
    }

    private void OnTimerFired(EntityUid uid, SmartMobSpawnerComponent component)
    {
        if (TerminatingOrDeleted(uid))
            return;

        // Can only happen if the SmartMobSpawnerSpawnedComponent was removed from the entity
        // without deleting the entity itself. The previous mob is still alive, so do not spawn another.
        if (Exists(component.SpawnedMob))
        {
            Log.Debug($"Spawner {uid} skipped spawning {component.MobPrototype} while {component.SpawnedMob} still exists.");
            return;
        }

        SpawnMob(uid, component);
    }

    private void SpawnMob(EntityUid uid, SmartMobSpawnerComponent component)
    {
        var coordinates = Transform(uid).Coordinates;
        component.SpawnedMob = SpawnAtPosition(component.MobPrototype, coordinates);
        var spawnedMobComp = EnsureComp<SmartMobSpawnerSpawnedComponent>(component.SpawnedMob);
        spawnedMobComp.SpawnedBy = uid;
    }
}
