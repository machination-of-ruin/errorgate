using Robust.Shared.Prototypes;

namespace Content.Shared._ERRORGATE.AiGod;

/// <summary>
///     What she may not say. Data, so it can be tuned without code: see <c>Resources/Prototypes/_ERRORGATE/AiGod/voice.yml</c>.
///     She never guides: no orders, hints or goals. The patterns catch the usual ways a line turns into an instruction. They
///     are a safety net for what a model writes, not a style guide.
/// </summary>
[Prototype("godVoice")]
public sealed partial class GodVoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Words that are never shown, whole words, case insensitive.</summary>
    [DataField]
    public List<string> BannedWords = new();

    /// <summary>Regular expressions (case insensitive) for text that reads as an order, a hint or a goal.</summary>
    [DataField]
    public List<string> OrderPatterns = new();
}
