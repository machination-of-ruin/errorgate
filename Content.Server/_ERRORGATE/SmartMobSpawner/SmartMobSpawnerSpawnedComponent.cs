namespace Content.Server._ERRORGATE.SmartMobSpawner;

/// <summary>
/// Added on entities spawned by SmartMobSpawner.
/// Stores the UID of the spawner so the spawner
/// can be called when the entity is deleted.
/// </summary>
[RegisterComponent]
public sealed partial class SmartMobSpawnerSpawnedComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid SpawnedBy = EntityUid.Invalid;
}
