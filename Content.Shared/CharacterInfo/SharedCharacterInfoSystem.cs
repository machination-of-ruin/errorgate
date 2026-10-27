using Content.Shared.Objectives;
using Robust.Shared.Serialization;

namespace Content.Shared.CharacterInfo;

[Serializable, NetSerializable]
public sealed class RequestCharacterInfoEvent : EntityEventArgs
{
    public readonly NetEntity NetEntity;

    public RequestCharacterInfoEvent(NetEntity netEntity)
    {
        NetEntity = netEntity;
    }
}

[Serializable, NetSerializable]
public sealed class CharacterInfoEvent : EntityEventArgs
{
    public readonly NetEntity NetEntity;
    public readonly string JobTitle;
    public readonly Dictionary<string, List<ObjectiveInfo>> Objectives;
    public readonly string? Briefing;

    /// <summary>ERRORGATE: the standing with each faction, as lines ready to show.</summary>
    public readonly List<string> Reputation;

    public CharacterInfoEvent(NetEntity netEntity, string jobTitle, Dictionary<string, List<ObjectiveInfo>> objectives, string? briefing, List<string> reputation)
    {
        NetEntity = netEntity;
        JobTitle = jobTitle;
        Objectives = objectives;
        Briefing = briefing;
        Reputation = reputation;
    }
}
