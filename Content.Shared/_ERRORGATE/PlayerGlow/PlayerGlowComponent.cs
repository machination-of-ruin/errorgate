namespace Content.Shared._ERRORGATE.PlayerGlow;

/// <summary>
///     Marks the faint light around your own character and holds its numbers. It lives on a light entity that only the client of the
///     player spawns (PlayerGlowSystem in Content.Client), never on the server, and it is not networked.
///
///     The glow is not a light source: it only brings the surroundings up to <see cref="Floor"/>. The brighter the place already is
///     (map light, lamps, a held light), the less the glow gives, and it gives nothing once the place is as bright as the floor.
/// </summary>
[RegisterComponent]
public sealed partial class PlayerGlowComponent : Component
{
    /// <summary>The brightness (0 to 1) the glow brings a dark place up to at the character, and never above.</summary>
    [DataField]
    public float Floor = 0.14f;

    /// <summary>Seconds between two measurements of how bright the place already is.</summary>
    [DataField]
    public float SampleInterval = 0.12f;

    /// <summary>How fast the glow comes up when it gets darker, per second. Slow: the eyes take their time to get used to the dark.</summary>
    [DataField]
    public float SmoothingUp = 0.35f;

    /// <summary>How fast the glow goes away when it gets lighter, per second. Fast, so it never adds to a lit place.</summary>
    [DataField]
    public float SmoothingDown = 6f;

    /// <summary>How far the lamps around the character are looked for.</summary>
    [DataField]
    public float LampReach = 15f;

    /// <summary>Where the glow is at the moment, and where it is going (the client works these out).</summary>
    [ViewVariables]
    public float Energy;

    [ViewVariables]
    public float TargetEnergy;

    [ViewVariables]
    public float SinceSample;
}
