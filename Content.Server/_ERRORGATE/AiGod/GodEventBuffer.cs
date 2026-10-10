using System.Text;

namespace Content.Server._ERRORGATE.AiGod;

public enum GodEventKind : byte
{
    Prayer,

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
///     A line somebody said or prayed, raw. The subject number is the ledger's, the name the character's.
/// </summary>
public readonly record struct GodQuote(TimeSpan Time, int Subject, string Name, string? Sector, string Text, bool Whisper);

/// <summary>
///     Everything taken out of the buffer for one call to the model.
/// </summary>
public sealed class GodDrain
{
    public List<GodEvent> Events = new();
    public List<GodQuote> Prayers = new();
    public List<GodQuote> Mentions = new();
    public List<GodQuote> Speech = new();
}

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

    // Raw lines: prayers and mentions are kept in full, plus the last few ordinary lines as context
    private readonly List<GodQuote> _prayers = new();
    private readonly List<GodQuote> _mentions = new();
    private readonly List<GodQuote> _speech = new();

    public int QuoteCapacity = 40;
    public int ContextCapacity = 12;

    public IReadOnlyList<GodQuote> Prayers => _prayers;
    public IReadOnlyList<GodQuote> Mentions => _mentions;
    public IReadOnlyList<GodQuote> Speech => _speech;

    public void AddPrayer(GodQuote quote)
    {
        _prayers.Add(quote);
        if (QuoteCapacity > 0 && _prayers.Count > QuoteCapacity)
            _prayers.RemoveRange(0, _prayers.Count - QuoteCapacity);
    }

    public void AddMention(GodQuote quote)
    {
        _mentions.Add(quote);
        if (QuoteCapacity > 0 && _mentions.Count > QuoteCapacity)
            _mentions.RemoveRange(0, _mentions.Count - QuoteCapacity);
    }

    /// <summary>
    ///     An ordinary line of speech, kept only as context: the newest <see cref="ContextCapacity"/> stay.
    /// </summary>
    public void AddSpeech(GodQuote quote)
    {
        _speech.Add(quote);
        if (ContextCapacity >= 0 && _speech.Count > ContextCapacity)
            _speech.RemoveRange(0, _speech.Count - ContextCapacity);
    }

    /// <summary>
    ///     Everything for one call: events (with the chat summary), prayers, mentions and the context speech.
    ///     The buffer is emptied.
    /// </summary>
    public GodDrain DrainAll(TimeSpan now)
    {
        var drain = new GodDrain
        {
            Events = Drain(now),
            Prayers = new List<GodQuote>(_prayers),
            Mentions = new List<GodQuote>(_mentions),
            Speech = new List<GodQuote>(_speech),
        };

        _prayers.Clear();
        _mentions.Clear();
        _speech.Clear();
        return drain;
    }

    /// <summary>
    ///     Puts a drain back, for when the call it was taken for failed. What came in meanwhile stays after it.
    /// </summary>
    public void Restore(GodDrain drain)
    {
        _events.InsertRange(0, drain.Events);
        if (EventCapacity > 0 && _events.Count > EventCapacity)
            _events.RemoveRange(0, _events.Count - EventCapacity);

        _prayers.InsertRange(0, drain.Prayers);
        if (QuoteCapacity > 0 && _prayers.Count > QuoteCapacity)
            _prayers.RemoveRange(0, _prayers.Count - QuoteCapacity);

        _mentions.InsertRange(0, drain.Mentions);
        if (QuoteCapacity > 0 && _mentions.Count > QuoteCapacity)
            _mentions.RemoveRange(0, _mentions.Count - QuoteCapacity);
    }

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
        _prayers.Clear();
        _mentions.Clear();
        _speech.Clear();
    }
}
