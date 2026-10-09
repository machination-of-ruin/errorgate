namespace Content.Shared._ERRORGATE.DistantGunfire;

/// <summary>
///     Put on cartridges and shells: how far, in tiles, a shot fired with this ammo can be heard as a distant echo.
///     Ammo without it falls back to the default of the distant gunfire system.
/// </summary>
[RegisterComponent]
public sealed partial class DistantGunfireAudibleComponent : Component
{
    [DataField]
    public float Range = 50f;
}
