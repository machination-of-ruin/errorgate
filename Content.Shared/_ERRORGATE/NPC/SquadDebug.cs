using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._ERRORGATE.NPC;

/// <summary>Client to server: start or stop the squad debug feed (admins only).</summary>
[Serializable, NetSerializable]
public sealed class SquadDebugRequestEvent(bool enable) : EntityEventArgs
{
    public readonly bool Enable = enable;
}

/// <summary>One soldier or bandit in the squad debug feed.</summary>
[Serializable, NetSerializable]
public sealed record SquadDebugMember(NetEntity Entity, string Name, Vector2 Position, Vector2? Target, string Plan, bool Leader, bool Dead);

/// <summary>One squad in the squad debug feed: <see cref="Id"/> is zero for NPCs that belong to none.</summary>
[Serializable, NetSerializable]
public sealed record SquadDebugSquad(NetEntity Id, string Faction, string Task, string State, MapId Map, Vector2? Goal, Vector2? Home, float HomeRadius, List<SquadDebugMember> Members);

/// <summary>Server to client: what the squads are doing, and the points of interest (building spots) patrols pick from.</summary>
[Serializable, NetSerializable]
public sealed class SquadDebugEvent(List<SquadDebugSquad> squads, List<(MapId Map, Vector2 Position)> points) : EntityEventArgs
{
    public readonly List<SquadDebugSquad> Squads = squads;
    public readonly List<(MapId Map, Vector2 Position)> Points = points;
}
