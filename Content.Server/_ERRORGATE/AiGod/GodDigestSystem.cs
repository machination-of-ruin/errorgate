using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._ERRORGATE.Anomalies;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     Puts the digest together from the ledger, the event buffer and the world. <see cref="Preview"/> leaves the buffer as
///     it is (for admins), <see cref="Take"/> empties it (for a call to the model).
/// </summary>
public sealed class GodDigestSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly GodLedgerSystem _ledger = default!;
    [Dependency] private readonly GodObserverSystem _observer = default!;
    [Dependency] private readonly GodDirectorSystem _director = default!;

    /// <summary>What she wrote last time.</summary>
    public string Notes = string.Empty;

    public GodDigestResult Preview()
    {
        var buffer = _observer.Buffer;
        var drain = new GodDrain
        {
            Events = buffer.Events.ToList(),
            Prayers = buffer.Prayers.ToList(),
            Mentions = buffer.Mentions.ToList(),
            Speech = buffer.Speech.ToList(),
        };

        // The chat that has not been summarised yet is part of the preview too
        return GodDigest.Build(Input(drain));
    }

    public GodDigestResult Take()
    {
        var drain = _observer.Buffer.DrainAll(_timing.CurTime);
        return GodDigest.Build(Input(drain));
    }

    /// <summary>
    ///     Builds the digest of an already taken drain, with a summary of the mentions that did not fit.
    /// </summary>
    public GodDigestResult Build(GodDrain drain, string earlierMentions = "")
    {
        return GodDigest.Build(Input(drain, earlierMentions));
    }

    private GodDigestInput Input(GodDrain drain, string earlierMentions = "")
    {
        var subjects = _ledger.Ledger.All.ToList();
        var now = _timing.CurTime;

        return new GodDigestInput
        {
            Now = now,
            RoundTime = now - _ticker.RoundStartTimeSpan,
            Players = subjects.Count(s => s.Alive),
            Deaths = subjects.Sum(s => s.Errors),
            FaultsAwake = FaultsAwake(),
            Subjects = subjects,
            Drain = drain,
            HerRecent = _director.RecentLines(GodDigest.MaxRecentLines),
            State = _director.StateLine(),
            Notes = Notes,
            EarlierMentionsSummary = earlierMentions,
        };
    }

    private int FaultsAwake()
    {
        var count = 0;
        var query = EntityQueryEnumerator<ErrorgateAnomalyComponent>();
        while (query.MoveNext(out _, out var anomaly))
        {
            if (anomaly.Active && anomaly.Engaged)
                count++;
        }

        return count;
    }
}
