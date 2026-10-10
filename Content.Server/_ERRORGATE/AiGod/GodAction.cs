using System.Linq;
namespace Content.Server._ERRORGATE.AiGod;

public enum GodActionType : byte
{
    /// <summary>A quiet message to one subject or a group.</summary>
    Subtle,

    /// <summary>A message to the whole map.</summary>
    Announce,

    /// <summary>Static and noise on the screen of a subject, or of everyone in a sector. No text.</summary>
    Glitch,
}

public enum GodSource : byte
{
    /// <summary>Fixed rules and the line bank.</summary>
    Scripted,

    /// <summary>The model.</summary>
    Llm,

    /// <summary>An admin, through godforce.</summary>
    Admin,
}

public enum GodActionStatus : byte
{
    /// <summary>Waiting for an admin.</summary>
    Pending,

    /// <summary>Done.</summary>
    Executed,

    /// <summary>Would have been done, but dry-run is on.</summary>
    DryRun,

    /// <summary>An admin said no.</summary>
    Denied,

    /// <summary>Nobody answered in time, counts as denied.</summary>
    Expired,

    /// <summary>Thrown out by the validator, the budget or a cooldown. The reason says which.</summary>
    Rejected,
}

/// <summary>
///     Something she wants to do. Subjects are named by number (S17 is 17).
/// </summary>
public sealed class GodAction
{
    public GodActionType Type;

    /// <summary>Subject numbers, for a subtle message or a glitch.</summary>
    public List<int> Targets = new();

    /// <summary>A sector name (C4), for a glitch on everyone there.</summary>
    public string? Sector;

    /// <summary>The words, for a subtle message and an announcement.</summary>
    public string Text = string.Empty;

    /// <summary>What kind of glitch: "static" for now.</summary>
    public string Kind = "static";

    public override string ToString()
    {
        var where = Sector != null ? $"sector {Sector}" : Targets.Count > 0 ? string.Join(",", Targets.Select(t => $"S{t}")) : "all";
        return Type == GodActionType.Glitch ? $"glitch {Kind} {where}" : $"{Type.ToString().ToLowerInvariant()} {where}: {Text}";
    }
}

/// <summary>
///     An action with what happened to it, kept for <c>godlog</c>.
/// </summary>
public sealed class GodDecision
{
    public int Id;
    public TimeSpan Time;
    public GodSource Source;
    public GodAction Action = new();
    public GodActionStatus Status;

    /// <summary>Why it was rejected, denied or expired.</summary>
    public string? Reason;

    public float Cost;

    /// <summary>For pending decisions: when they expire.</summary>
    public TimeSpan ExpiresAt;

    public override string ToString()
    {
        var reason = Reason != null ? $" ({Reason})" : string.Empty;
        return $"#{Id} {Source} {Action} -> {Status}{reason}";
    }
}
