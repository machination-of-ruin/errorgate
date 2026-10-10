namespace Content.Server._ERRORGATE.LifeLog;

public enum LogKind : byte
{
    /// <summary>Said by the character out loud.</summary>
    Speech,

    /// <summary>Whispered by the character.</summary>
    Whisper,

    /// <summary>Said by somebody else within hearing range.</summary>
    Heard,

    /// <summary>Harm the character took. The subject is who or what dealt it.</summary>
    DamageIn,

    /// <summary>Harm the character dealt to a player or a mob.</summary>
    DamageOut,

    /// <summary>A kill by the character.</summary>
    Deleted,
}

/// <summary>
///     One line of the log. For damage, <see cref="Amount"/> is the total of <see cref="Count"/> hits merged into the line.
/// </summary>
public sealed class LogEntry
{
    public TimeSpan Time;
    public LogKind Kind;
    public string Subject = string.Empty;

    /// <summary>The words, for speech.</summary>
    public string Text = string.Empty;

    public float Amount;
    public int Count = 1;

    /// <summary>Damage nobody dealt (fire, cold, no air, blood loss, starvation...). Small totals are left out of the log.</summary>
    public bool Environmental;
}

/// <summary>
///     What happened to one character during one life, as a list of events, to write the log shown at death. Plain data.
///     Names are character names at the time of the event, not account names.
/// </summary>
public sealed class LifeRecord
{
    /// <summary>Events kept per life, the oldest are dropped.</summary>
    public const int Capacity = 24;

    public TimeSpan Born;
    public readonly List<LogEntry> Events = new();
}
