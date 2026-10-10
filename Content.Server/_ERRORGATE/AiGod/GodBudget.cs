using System.Linq;
namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     Her "wrath budget": points that build up with time and are spent by actions, so that small signs are frequent and big
///     acts are rare. It also holds the cooldowns per target and for announcements. Plain code with the time passed in,
///     so it can be tested.
/// </summary>
public sealed class GodBudget
{
    public const float SubtleCost = 1f;
    public const float GlitchCost = 1f;
    public const float AnnounceCost = 8f;

    public float Points;
    public float PerMinute = 3f;
    public float Cap = 20f;

    /// <summary>Seconds before the same target can be the target of another action.</summary>
    public float TargetCooldown = 90f;

    /// <summary>Seconds between two announcements.</summary>
    public float AnnounceCooldown = 600f;

    public int AnnouncePerHour = 3;

    private TimeSpan _last;
    private readonly Dictionary<string, TimeSpan> _targetLast = new();
    private readonly List<TimeSpan> _announces = new();

    public static float CostOf(GodAction action)
    {
        return action.Type switch
        {
            GodActionType.Announce => AnnounceCost,
            GodActionType.Glitch => GlitchCost,
            _ => SubtleCost,
        };
    }

    public void Reset(TimeSpan now)
    {
        Points = Cap;
        _last = now;
        _targetLast.Clear();
        _announces.Clear();
    }

    /// <summary>
    ///     Adds the points earned since the last call.
    /// </summary>
    public void Advance(TimeSpan now)
    {
        if (now > _last)
            Points = Math.Min(Cap, Points + (float) (now - _last).TotalMinutes * PerMinute);

        _last = now;
    }

    /// <summary>
    ///     Null when the action may go ahead, otherwise the reason it may not.
    /// </summary>
    public string? Check(GodAction action, TimeSpan now)
    {
        var cost = CostOf(action);
        if (Points < cost)
            return $"not enough points ({Points:0.#} of {cost:0.#})";

        foreach (var key in Keys(action))
        {
            if (_targetLast.TryGetValue(key, out var last) && (now - last).TotalSeconds < TargetCooldown)
                return $"{key} was addressed {(int) (now - last).TotalSeconds}s ago";
        }

        if (action.Type == GodActionType.Announce)
        {
            if (_announces.Count > 0 && (now - _announces[^1]).TotalSeconds < AnnounceCooldown)
                return "announced too recently";

            if (_announces.Count(a => (now - a).TotalHours < 1) >= AnnouncePerHour)
                return "too many announcements this hour";
        }

        return null;
    }

    public void Spend(GodAction action, TimeSpan now)
    {
        Points -= CostOf(action);

        foreach (var key in Keys(action))
        {
            _targetLast[key] = now;
        }

        if (action.Type == GodActionType.Announce)
            _announces.Add(now);
    }

    private static IEnumerable<string> Keys(GodAction action)
    {
        if (action.Sector != null)
        {
            yield return "sector " + action.Sector;
            yield break;
        }

        foreach (var target in action.Targets)
        {
            yield return $"S{target}";
        }
    }
}
