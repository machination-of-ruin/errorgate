using System.Text;

namespace Content.Server._ERRORGATE.AiGod;

public enum GodEventKind : byte
{
    Death,
    Combat,
    Arrival,
    Chat,

    /// <summary>Something the world did: an anomaly, a storm, a spawn, anything not caused by a player.</summary>
    World,
}

public enum GodEventSeverity : byte
{
    Low,
    Medium,
    High,
}

/// <summary>
///     One thing MACHINATION OF RUIN has noticed. The text is a plain observation, it is shown to the model, never to players.
/// </summary>
public readonly record struct GodEvent(
    TimeSpan Time,
    GodEventKind Kind,
    GodEventSeverity Severity,
    string Text,
    IReadOnlyList<string> Subjects);

/// <summary>
///     What has been observed since the model was last asked. A bounded list of events, plus a separate chat buffer
///     that is summarised into a single event when it is flushed, so a busy channel cannot flood a request.
/// </summary>
public sealed class GodEventBuffer
{
    public const int MaxChatLineLength = 200;

    private readonly List<GodEvent> _events = new();
    private readonly List<(string Speaker, string Message, bool Whisper)> _chat = new();

    public int EventCapacity = 60;
    public int ChatCapacity = 80;

    public IReadOnlyList<GodEvent> Events => _events;
    public int ChatCount => _chat.Count;

    public void Add(GodEvent ev)
    {
        _events.Add(ev);

        if (EventCapacity > 0 && _events.Count > EventCapacity)
            _events.RemoveRange(0, _events.Count - EventCapacity);
    }

    public void AddChat(string speaker, string message, bool whisper)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        _chat.Add((speaker, message, whisper));

        if (ChatCapacity > 0 && _chat.Count > ChatCapacity)
            _chat.RemoveRange(0, _chat.Count - ChatCapacity);
    }

    /// <summary>
    ///     Turns the buffered chat into one event. Null when nobody spoke.
    /// </summary>
    public GodEvent? FlushChat(TimeSpan now)
    {
        if (_chat.Count == 0)
            return null;

        var text = new StringBuilder();
        text.Append(_chat.Count).Append(" lines of speech:");

        var speakers = new List<string>();
        foreach (var (speaker, message, whisper) in _chat)
        {
            var line = message.Length > MaxChatLineLength ? message[..MaxChatLineLength] + "..." : message;
            text.Append("\n  ").Append(speaker).Append(whisper ? " (whispering)" : "").Append(": \"").Append(line).Append('"');

            if (!speakers.Contains(speaker))
                speakers.Add(speaker);
        }

        _chat.Clear();
        return new GodEvent(now, GodEventKind.Chat, GodEventSeverity.Low, text.ToString(), speakers);
    }

    /// <summary>
    ///     Everything observed so far, in the order it was recorded with the chat summary last, and the buffer is emptied.
    /// </summary>
    public List<GodEvent> Drain(TimeSpan now)
    {
        if (FlushChat(now) is { } chat)
            Add(chat);

        var result = new List<GodEvent>(_events);
        _events.Clear();
        return result;
    }

    public void Clear()
    {
        _events.Clear();
        _chat.Clear();
    }
}
