namespace Content.Server._ERRORGATE.Despawn;

/// <summary>
/// Despawns entities that lie on a grid for DespawnAfterSeconds.
/// </summary>
[RegisterComponent]
public sealed partial class DespawnItemComponent : Component
{
    [DataField]
    public int DespawnAfterSeconds = 1200; // 20 minutes

    [DataField]
    public bool Despawn = true;

    /// <summary>
    /// When the item goes away, null while it is carried, stored or not on a grid. Not saved.
    /// </summary>
    public TimeSpan? DespawnAt;
}
