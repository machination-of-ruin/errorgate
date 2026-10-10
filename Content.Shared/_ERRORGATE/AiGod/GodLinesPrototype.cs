using Robust.Shared.Prototypes;

namespace Content.Shared._ERRORGATE.AiGod;

/// <summary>
///     The line bank: what she says when no model is asked. Slots are {name}, {number} and {sector}. Every line has to pass the
///     voice rules (no orders, no hints), a test checks that. The model can add to these in writer mode later.
/// </summary>
[Prototype("godLines")]
public sealed partial class GodLinesPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Said to those nearby when a subject dies. {name} and {number} are the dead one.</summary>
    [DataField]
    public List<string> Error = new();

    /// <summary>Small signs for one subject, now and then. {name}, {number} and {sector} are the subject's.</summary>
    [DataField]
    public List<string> Omen = new();

    /// <summary>The answer to a prayer at an altar. {name} and {number} are the one who prayed.</summary>
    [DataField]
    public List<string> Prayer = new();
}
