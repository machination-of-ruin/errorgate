using Robust.Shared.Prototypes;

namespace Content.Shared._ERRORGATE.AiGod;

/// <summary>
///     The words that mark a line of speech as a reference to her or to a higher power. Data, so it can be tuned
///     without code: see <c>Resources/Prototypes/_ERRORGATE/AiGod/mentions.yml</c>.
/// </summary>
[Prototype("godMentions")]
public sealed partial class GodMentionsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Words and phrases that count on their own.</summary>
    [DataField]
    public List<string> Words = new();

    /// <summary>Pronouns that only count next to one of the <see cref="WatchWords"/> ("she is watching").</summary>
    [DataField]
    public List<string> Pronouns = new();

    [DataField]
    public List<string> WatchWords = new();
}
