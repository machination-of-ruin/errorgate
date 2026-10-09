namespace Content.Server._ERRORGATE.DoorShock;

/// <summary>
///     Shocks whoever attacks this entity while it is powered, so players learn that it cannot be broken.
/// </summary>
[RegisterComponent]
public sealed partial class ShockOnHitComponent : Component
{
    [DataField]
    public int ShockDamage = 5;

    [DataField]
    public TimeSpan ShockTime = TimeSpan.FromSeconds(1);

    [DataField]
    public float SiemensCoefficient = 1f;
}
