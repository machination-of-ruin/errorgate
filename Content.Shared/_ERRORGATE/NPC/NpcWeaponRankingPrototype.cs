using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;

namespace Content.Shared._ERRORGATE.NPC;

/// <summary>
///     How good soldiers and bandits think a gun is: the higher the rank, the sooner they draw it. Guns are listed by the prototype
///     without the <c>RandomMag</c> suffix; a gun that is not listed has <see cref="Default"/>.
/// </summary>
[Prototype("npcWeaponRanking")]
public sealed class NpcWeaponRankingPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public int Default = 10;

    [DataField(customTypeSerializer: typeof(PrototypeIdDictionarySerializer<int, EntityPrototype>))]
    public Dictionary<string, int> Ranks = new();
}
