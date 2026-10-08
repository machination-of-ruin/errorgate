namespace Content.Server._ERRORGATE.Despawn;

/// <summary>
/// Despawns mobs that stay in the dead mob state for DespawnAfterSeconds.
/// </summary>
[RegisterComponent]
public sealed partial class DespawnDeadBodyComponent : Component
{
    [DataField]
    public int DespawnAfterSeconds = 600; // 10 minutes

    // Gibbing drops all items so it is preferred for players
    [DataField]
    public bool GibInsteadOfDeleting = true;

    /// <summary>
    /// When the body goes away, null while the mob is alive. Not saved.
    /// </summary>
    public TimeSpan? DespawnAt;
}
