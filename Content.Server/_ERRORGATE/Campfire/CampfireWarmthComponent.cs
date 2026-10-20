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

    /// <summary>Damage healed per round of each of the <see cref="Types"/>.</summary>
    [DataField]
    public float Heal = 1f;

    /// <summary>
    ///     The damage types the fire mends, each by <see cref="Heal"/> a round: brute, burn and poison. Healing a whole group (as it was
    ///     before) spreads the amount over all its types, so a wound of one type healed a fraction of it and the rest of the types never.
    /// </summary>
    [DataField]
    public List<string> Types = new() { "Blunt", "Slash", "Piercing", "Heat", "Shock", "Cold", "Caustic", "Poison" };

    [ViewVariables]
    public TimeSpan NextHeal;
}
