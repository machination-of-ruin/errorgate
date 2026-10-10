using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Content.Shared._ERRORGATE.AiGod;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     What the validator needs to know about the world, as plain data and delegates, so it can be tested.
/// </summary>
public sealed class GodValidationContext
{
    public int SubtleMaxLength = 140;
    public int AnnounceMaxLength = 200;

    /// <summary>Whether a subject number exists.</summary>
    public Func<int, bool> SubjectExists = _ => true;

    /// <summary>Whether a subject can be addressed right now (alive, in the world, not in the death void).</summary>
    public Func<int, bool> SubjectReachable = _ => true;

    /// <summary>Whether a sector name exists on the map.</summary>
    public Func<string, bool> SectorExists = _ => true;
}

/// <summary>
///     Checks an action before anything happens. A failed check throws the action away (and says why), it never repairs
///     anything except cleaning the text.
/// </summary>
public sealed class GodActionValidator
{
    private readonly List<Regex> _orders;
    private readonly Regex _banned;

    private static readonly Regex Markup = new(@"\[/?[^\]\[]*\]", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public GodActionValidator(GodVoicePrototype voice)
    {
        _orders = voice.OrderPatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
            .ToList();

        _banned = voice.BannedWords.Count == 0
            ? new Regex("(?!)")
            : new Regex(@"\b(?:" + string.Join("|", voice.BannedWords.Select(w => Regex.Escape(w.Trim()))) + @")\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    /// <summary>
    ///     Removes markup and control characters and squeezes whitespace. Everything shown to a player goes through this.
    /// </summary>
    public static string Clean(string text)
    {
        text = Markup.Replace(text, string.Empty);

        var plain = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == '\n' || c == '\r' || c == '\t')
                plain.Append(' ');
            else if (!char.IsControl(c))
                plain.Append(c);
        }

        return Spaces.Replace(plain.ToString(), " ").Trim();
    }

    /// <summary>
    ///     Returns null when the action is fine, otherwise the reason. The text of the action is cleaned in place.
    /// </summary>
    public string? Validate(GodAction action, GodValidationContext context)
    {
        switch (action.Type)
        {
            case GodActionType.Subtle:
                if (action.Targets.Count == 0)
                    return "a subtle message needs a target";

                if (action.Targets.Count > 8)
                    return "too many targets";

                return CheckTargets(action, context) ?? CheckText(action, context.SubtleMaxLength);

            case GodActionType.Announce:
                if (action.Targets.Count > 0 || action.Sector != null)
                    return "an announcement is for everyone";

                return CheckText(action, context.AnnounceMaxLength);

            case GodActionType.Glitch:
                if (action.Kind != "static")
                    return $"unknown glitch kind {action.Kind}";

                if (action.Sector != null)
                {
                    if (action.Targets.Count > 0)
                        return "a glitch is for a sector or for subjects, not both";

                    return context.SectorExists(action.Sector) ? null : $"no sector {action.Sector}";
                }

                if (action.Targets.Count == 0)
                    return "a glitch needs a subject or a sector";

                return CheckTargets(action, context);

            default:
                return "unknown action";
        }
    }

    private static string? CheckTargets(GodAction action, GodValidationContext context)
    {
        foreach (var target in action.Targets)
        {
            if (!context.SubjectExists(target))
                return $"no subject S{target}";

            if (!context.SubjectReachable(target))
                return $"S{target} cannot be reached";
        }

        return null;
    }

    private string? CheckText(GodAction action, int maxLength)
    {
        action.Text = Clean(action.Text);

        if (action.Text.Length == 0)
            return "no text";

        if (action.Text.Length > maxLength)
            return $"text longer than {maxLength} characters";

        // Plain printable characters and common punctuation, nothing that could carry anything else
        foreach (var c in action.Text)
        {
            if (c > '~' || c < ' ')
                return "characters outside plain text";
        }

        if (_banned.IsMatch(action.Text))
            return "banned word";

        foreach (var order in _orders)
        {
            if (order.IsMatch(action.Text))
                return "reads as an order or a hint";
        }

        return null;
    }
}
