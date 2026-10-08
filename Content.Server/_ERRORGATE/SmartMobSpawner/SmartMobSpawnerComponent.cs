using System.Threading;
using Robust.Shared.Prototypes;

namespace Content.Server._ERRORGATE.SmartMobSpawner;

/// <summary>
/// Spawns a MobPrototype entity and stores its UID.
/// Starts a respawn timer once the entity was deleted.
/// </summary>
[RegisterComponent]
public sealed partial class SmartMobSpawnerComponent : Component
{
    [DataField(required: true)]
    public EntProtoId MobPrototype = string.Empty;

    /// <summary>
    /// If false, will start a respawn timer instead of spawning right away.
    /// </summary>
    [DataField]
    public bool SpawnMobOnInit = true;

    /// <summary>
    /// Respawn delay in seconds, calculated from <see cref="SpawnRate"/>.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int RespawnTime;

    /// <summary>
    /// Will try to spawn that many mobs per hour (3600 seconds).
    /// </summary>
    [DataField]
    public float SpawnRate = 1.0f;

    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid SpawnedMob = EntityUid.Invalid;

    public CancellationTokenSource? TokenSource;
}
