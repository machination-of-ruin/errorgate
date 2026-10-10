using Content.Shared._ERRORGATE.Anomalies;

namespace Content.Server._ERRORGATE.LifeLog;

public enum HarmSource : byte
{
    None,

    /// <summary>Another player.</summary>
    Player,

    /// <summary>A mob, or an object somebody used.</summary>
    Thing,

    /// <summary>A world fault (heat, arc, collapse).</summary>
    Fault,

    /// <summary>The player's own doing.</summary>
    Self,

    /// <summary>Fire, cold, no air, blood loss, poison: damage nobody dealt.</summary>
    Environment,
}

/// <summary>
///     What happened to one character during one life, kept to write the log shown at death. Plain data. The names are the
///     character names at the time, not account names.
/// </summary>
public sealed class LifeRecord
{
    public TimeSpan Born;
    public string Name = string.Empty;

    // Who or what harmed this character last. Attacks and environmental damage are kept apart, so that blood loss
    // after a shot does not hide who fired it.
    public HarmSource LastAttackKind;
    public string LastAttackName = string.Empty;
    public ErrorgateAnomalyKind LastAttackFault;
    public float LastAttackDamage;
    public TimeSpan LastAttackTime;

    public string LastEnvironment = string.Empty;
    public TimeSpan LastEnvironmentTime;

    // What this character did to others
    public readonly Dictionary<string, float> HurtPlayers = new();
    public readonly Dictionary<string, float> HurtMobs = new();
    public readonly List<string> KilledPlayers = new();
    public readonly List<string> KilledMobs = new();

    // Talk: players who were in hearing range when this character spoke, with the number of lines
    public readonly Dictionary<string, int> SpokeWith = new();
    public int LinesSpoken;
    public string? LastWords;

    // Players who spoke to this character: names with line counts
    public readonly Dictionary<string, int> SpokenToBy = new();
}
