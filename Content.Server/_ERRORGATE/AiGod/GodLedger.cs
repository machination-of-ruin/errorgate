using Robust.Shared.Network;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     What MACHINATION OF RUIN knows about one player this round. The player is a subject with a number (S1, S2, ...)
///     that lasts the whole round, whatever characters they play. Account names are never exposed, only the character name.
///     Plain data: the digest is built from it, and it is reset every round.
/// </summary>
public sealed class Subject
{
    public int Number;
    public NetUserId User;

    /// <summary>Name of the current (or last) character.</summary>
    public string Name = string.Empty;

    public int Lives;

    /// <summary>Deaths. Death is the only thing counted as an error.</summary>
    public int Errors;

    public bool Alive;
    public TimeSpan LifeStart;

    /// <summary>Sector the character is in (or was in when they died), null when unknown.</summary>
    public string? Sector;

    public readonly HashSet<string> SectorsVisited = new();

    /// <summary>Numbers of the subjects within a few tiles at the last look.</summary>
    public readonly List<int> Near = new();

    public int KillsOfPlayers;
    public int KillsOfMobs;
    public float DamageDealt;
    public float DamageTaken;
    public int SpeechLines;
    public int Prayers;
    public string? LastWords;

    /// <summary>The latest notable thing, in words, and when it was.</summary>
    public string? LastEvent;
    public TimeSpan LastEventTime;

    /// <summary>How interesting this subject is right now: recent, severe things count most.</summary>
    public float Salience;
}

/// <summary>
///     All subjects of the round.
/// </summary>
public sealed class GodLedger
{
    private readonly Dictionary<NetUserId, Subject> _subjects = new();
    private int _next = 1;

    public IEnumerable<Subject> All => _subjects.Values;
    public int Count => _subjects.Count;

    public Subject GetOrAdd(NetUserId user)
    {
        if (!_subjects.TryGetValue(user, out var subject))
        {
            subject = new Subject { Number = _next++, User = user };
            _subjects[user] = subject;
        }

        return subject;
    }

    public bool TryGet(NetUserId user, out Subject subject)
    {
        return _subjects.TryGetValue(user, out subject!);
    }

    public Subject? ByNumber(int number)
    {
        foreach (var subject in _subjects.Values)
        {
            if (subject.Number == number)
                return subject;
        }

        return null;
    }

    public Subject? ByName(string name)
    {
        foreach (var subject in _subjects.Values)
        {
            if (string.Equals(subject.Name, name, StringComparison.OrdinalIgnoreCase))
                return subject;
        }

        return null;
    }

    public void Clear()
    {
        _subjects.Clear();
        _next = 1;
    }
}
