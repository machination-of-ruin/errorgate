using System.Text.Json;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     What a reply of the model turned into.
/// </summary>
public sealed class GodReply
{
    /// <summary>What she wrote to herself, cut to <see cref="GodReplyParser.MaxNotesLength"/>.</summary>
    public string Notes = string.Empty;

    public List<GodAction> Actions = new();

    /// <summary>Everything in the reply that was thrown away, and why.</summary>
    public List<string> Problems = new();

    /// <summary>False when the reply held no JSON object at all.</summary>
    public bool Parsed;
}

/// <summary>
///     Reads the model's reply. It is forgiving about the wrapping (code fences, a sentence before the JSON, trailing commas)
///     and strict about the content: only known action types and known fields are taken, and nothing is taken on trust.
/// </summary>
public static class GodReplyParser
{
    public const int MaxNotesLength = 300;
    public const int MaxActions = 3;

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    ///     The first JSON object in a piece of text, or null.
    /// </summary>
    public static JsonDocument? ExtractObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            var document = JsonDocument.Parse(text[start..(end + 1)], Options);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static GodReply Parse(string text, int maxActions = MaxActions)
    {
        var reply = new GodReply();

        using var document = ExtractObject(text);
        if (document == null)
        {
            reply.Problems.Add("no JSON object in the reply");
            return reply;
        }

        reply.Parsed = true;
        var root = document.RootElement;

        if (root.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.String)
        {
            var value = notes.GetString()!.Trim();
            reply.Notes = value.Length > MaxNotesLength ? value[..MaxNotesLength] : value;
        }

        if (!root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
            return reply;

        foreach (var item in actions.EnumerateArray())
        {
            if (reply.Actions.Count >= maxActions)
            {
                reply.Problems.Add($"more than {maxActions} actions, the rest were dropped");
                break;
            }

            var action = ReadAction(item, reply.Problems);
            if (action != null)
                reply.Actions.Add(action);
        }

        return reply;
    }

    private static GodAction? ReadAction(JsonElement item, List<string> problems)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            problems.Add("an action that is not an object");
            return null;
        }

        var type = Text(item, "type")?.Trim().ToLowerInvariant();
        var action = new GodAction();

        switch (type)
        {
            case "subtle":
                action.Type = GodActionType.Subtle;
                break;
            case "announce":
                action.Type = GodActionType.Announce;
                break;
            case "glitch":
                action.Type = GodActionType.Glitch;
                break;
            default:
                problems.Add($"unknown action type {type ?? "(none)"}");
                return null;
        }

        action.Text = Text(item, "text") ?? string.Empty;
        action.Sector = Text(item, "sector")?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(action.Sector))
            action.Sector = null;

        if (Text(item, "kind") is { } kind && kind.Trim().Length > 0)
            action.Kind = kind.Trim().ToLowerInvariant();

        foreach (var property in new[] { "targets", "target" })
        {
            if (!item.TryGetProperty(property, out var targets))
                continue;

            if (targets.ValueKind == JsonValueKind.Array)
            {
                foreach (var target in targets.EnumerateArray())
                {
                    AddTarget(action, target, problems);
                }
            }
            else
            {
                AddTarget(action, targets, problems);
            }
        }

        return action;
    }

    private static void AddTarget(GodAction action, JsonElement target, List<string> problems)
    {
        string? text = target.ValueKind switch
        {
            JsonValueKind.String => target.GetString(),
            JsonValueKind.Number => target.GetRawText(),
            _ => null,
        };

        if (text != null && GodCommandHelpers.TryNumber(text.Trim(), out var number) && !action.Targets.Contains(number))
            action.Targets.Add(number);
        else if (text == null || !GodCommandHelpers.TryNumber(text.Trim(), out _))
            problems.Add($"a target that is not a subject: {text ?? target.ValueKind.ToString()}");
    }

    private static string? Text(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>
    ///     The lists of lines the writer prompt asks for, by category (error, omen, prayer). Anything else is ignored.
    /// </summary>
    public static Dictionary<string, List<string>> ParseLines(string text)
    {
        var result = new Dictionary<string, List<string>>();

        using var document = ExtractObject(text);
        if (document == null)
            return result;

        foreach (var category in new[] { "error", "omen", "prayer" })
        {
            if (!document.RootElement.TryGetProperty(category, out var list) || list.ValueKind != JsonValueKind.Array)
                continue;

            var lines = new List<string>();
            foreach (var line in list.EnumerateArray())
            {
                if (line.ValueKind == JsonValueKind.String && line.GetString() is { } value)
                    lines.Add(value);
            }

            result[category] = lines;
        }

        return result;
    }
}
