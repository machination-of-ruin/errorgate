using Robust.Shared.Prototypes;

namespace Content.Shared._ERRORGATE.AiGod;

/// <summary>
///     The fixed texts sent to the model. Data, so they can be tuned without a rebuild:
///     see <c>Resources/Prototypes/_ERRORGATE/AiGod/prompt.yml</c>.
/// </summary>
[Prototype("godPrompt")]
public sealed partial class GodPromptPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Who she is, her rules, what the digest is, and the reply format. Sent as the system message of every call.</summary>
    [DataField]
    public string System = string.Empty;

    /// <summary>Asks the model to write new lines for the line bank.</summary>
    [DataField]
    public string Writer = string.Empty;

    /// <summary>Asks the model to compress mention lines that did not fit.</summary>
    [DataField]
    public string Summary = string.Empty;
}
