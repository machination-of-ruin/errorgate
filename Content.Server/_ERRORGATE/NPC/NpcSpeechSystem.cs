using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.NPC.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>Remembers when an NPC last spoke, so it does not talk all the time.</summary>
[RegisterComponent]
public sealed partial class NpcSpeechComponent : Component
{
    public TimeSpan Next;
}

/// <summary>
///     The lines soldiers and bandits say about what they are doing: seeing an enemy, reloading, a comrade down, going into a building,
///     picking something up, and the talk between them when the squad stands. The lines are in
///     <c>Locale/en-US/_ERRORGATE/npc-speech.ftl</c>, <c>npc-speech-{faction}-{category}-{n}</c> with n from 1 up.
/// </summary>
public sealed class NpcSpeechSystem : EntitySystem
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<string, int> _counts = new();

    /// <summary>Says a line of the category with the given chance, if the NPC has not spoken lately. True if it spoke.</summary>
    public bool TrySay(EntityUid npc, string category, float chance)
    {
        if (!Exists(npc) || TerminatingOrDeleted(npc) || !_random.Prob(chance))
            return false;

        var faction = _factions.IsMember(npc, "Military") ? "military" : _factions.IsMember(npc, "Bandit") ? "bandit" : null;
        if (faction == null)
            return false;

        var speech = EnsureComp<NpcSpeechComponent>(npc);
        if (speech.Next > _timing.CurTime)
            return false;

        var prefix = $"npc-speech-{faction}-{category}";
        if (!_counts.TryGetValue(prefix, out var count))
        {
            while (Loc.TryGetString($"{prefix}-{count + 1}", out _))
            {
                count++;
            }

            _counts[prefix] = count;
        }

        if (count == 0)
            return false;

        speech.Next = _timing.CurTime + Cooldown;
        _chat.TrySendInGameICMessage(npc, Loc.GetString($"{prefix}-{_random.Next(1, count + 1)}"), InGameICChatType.Speak, false);
        return true;
    }
}
