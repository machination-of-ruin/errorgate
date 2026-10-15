namespace Content.Server._ERRORGATE.Campfire;

/// <summary>
///     A fire that mends those who rest beside it: anyone alive who is lying down or sitting (buckled) within <see cref="Range"/> tiles
///     slowly heals. See <see cref="CampfireWarmthSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class CampfireWarmthComponent : Component
{
    /// <summary>How near a person has to be, in tiles.</summary>
    [DataField]
    public float Range = 2.5f;

    /// <summary>Seconds between two rounds of healing.</summary>
    [DataField]
    public float IntervalSeconds = 5f;

    /// <summary>Damage healed per round: this much of brute and this much of burn.</summary>
    [DataField]
    public float Heal = 1f;

    [ViewVariables]
    public TimeSpan NextHeal;
}
