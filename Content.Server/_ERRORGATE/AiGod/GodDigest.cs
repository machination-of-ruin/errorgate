using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     What the digest builder is given. Plain data, filled by the ledger, the buffer and the world.
/// </summary>
public sealed class GodDigestInput
{
    public TimeSpan Now;
    public TimeSpan RoundTime;

    // The world, a null is left out
    public int Players;
    public int Deaths;
    public string? TimeOfDay;
    public string? Weather;
    public int? FaultsAwake;
    public int? LootLeftPercent;

    public List<Subject> Subjects = new();
    public GodDrain Drain = new();

    /// <summary>Her last actions, already worded ("subtle S17 T-3m done").</summary>
    public List<string> HerRecent = new();

    /// <summary>Wrath budget, cooldowns, mood, in one line.</summary>
    public string State = string.Empty;

    /// <summary>What she wrote last time.</summary>
    public string Notes = string.Empty;

    /// <summary>A summary of mention lines that did not fit, written by a pre-pass call. Empty when there is none.</summary>
    public string EarlierMentionsSummary = string.Empty;
}

/// <summary>
///     The digest and what could not be shown in it.
/// </summary>
public sealed class GodDigestResult
{
    public string Text = string.Empty;
    public int Tokens;

    /// <summary>Mention lines beyond the limit, for the pre-pass that summarises them.</summary>
    public List<GodQuote> OverflowMentions = new();

    /// <summary>True when nothing happened and nobody prayed: there is no point calling the model.</summary>
    public bool Empty;
}

/// <summary>
///     Builds the text the model sees: a compact, ranked, size-limited account of the round. Deterministic and free of
///     side effects, so it can be tested. It never contains account names, and the only free text from players is the
///     quoted speech and prayers.
/// </summary>
public static class GodDigest
{
    public const int MaxTokens = 2000;
    public const int MaxMentionLines = 10;
    public const int MaxEventLines = 20;
    public const int MaxSubjectLines = 8;
    public const int MaxRecentLines = 6;

    private const int MaxNotesLength = 300;

    public static int ApproxTokens(string text) => (text.Length + 3) / 4;

    public static GodDigestResult Build(GodDigestInput input, int maxTokens = MaxTokens)
    {
        var result = new GodDigestResult();
        var drain = input.Drain;

        // Mentions: the newest lines raw, the rest is for the pre-pass
        var mentions = drain.Mentions.ToList();
        if (mentions.Count > MaxMentionLines)
        {
            result.OverflowMentions = mentions.Take(mentions.Count - MaxMentionLines).ToList();
            mentions = mentions.Skip(mentions.Count - MaxMentionLines).ToList();
        }

        result.Empty = drain.Events.Count == 0 && drain.Prayers.Count == 0 && mentions.Count == 0 && result.OverflowMentions.Count == 0;

        var names = NameMap(input.Subjects);

        var eventsCap = MaxEventLines;
        var subjectsCap = MaxSubjectLines;
        var recentCap = MaxRecentLines;
        var speechCap = drain.Speech.Count;

        string text;
        while (true)
        {
            text = Assemble(input, mentions, names, eventsCap, subjectsCap, recentCap, speechCap);
            if (ApproxTokens(text) <= maxTokens)
                break;

            // Over budget: the context speech goes first, then older events, then the quieter subjects. Prayers and
            // mentions are never cut here (they are limited where they are made).
            if (speechCap > 0)
                speechCap = 0;
            else if (eventsCap > 5)
                eventsCap -= 5;
            else if (subjectsCap > 4)
                subjectsCap--;
            else if (recentCap > 2)
                recentCap--;
            else if (eventsCap > 0)
                eventsCap--;
            else
                break;
        }

        result.Text = text;
        result.Tokens = ApproxTokens(text);
        return result;
    }

    private static string Assemble(
        GodDigestInput input,
        List<GodQuote> mentions,
        List<(string Name, Subject Subject)> names,
        int eventsCap,
        int subjectsCap,
        int recentCap,
        int speechCap)
    {
        var text = new StringBuilder();
        var drain = input.Drain;

        text.Append("[ROUND] ").AppendLine(RoundLine(input));

        if (drain.Prayers.Count > 0 || mentions.Count > 0 || input.EarlierMentionsSummary != string.Empty)
        {
            text.AppendLine("[PRAYERS AND MENTIONS]");
            foreach (var prayer in drain.Prayers)
            {
                text.Append("PRAYER ").AppendLine(QuoteLine(prayer));
            }

            if (input.EarlierMentionsSummary != string.Empty)
                text.Append("EARLIER MENTIONS (summary): ").AppendLine(input.EarlierMentionsSummary);

            foreach (var mention in mentions)
            {
                text.Append("MENTION ").AppendLine(QuoteLine(mention));
            }
        }

        text.AppendLine("[SUBJECTS]");
        AppendSubjects(text, input, subjectsCap);

        if (eventsCap > 0 && drain.Events.Count > 0)
        {
            text.AppendLine("[EVENTS]");
            foreach (var line in EventLines(input, names, eventsCap))
            {
                text.AppendLine(line);
            }
        }

        if (speechCap > 0 && drain.Speech.Count > 0)
        {
            text.AppendLine("[SPEECH]");
            foreach (var quote in drain.Speech.TakeLast(speechCap))
            {
                text.Append("SAID ").AppendLine(QuoteLine(quote));
            }
        }

        if (input.HerRecent.Count > 0 && recentCap > 0)
        {
            text.AppendLine("[HER RECENT]");
            foreach (var line in input.HerRecent.TakeLast(recentCap))
            {
                text.AppendLine(line);
            }
        }

        if (input.State != string.Empty)
            text.Append("[STATE] ").AppendLine(input.State);

        if (input.Notes != string.Empty)
        {
            var notes = input.Notes.Length > MaxNotesLength ? input.Notes[..MaxNotesLength] : input.Notes;
            text.Append("[NOTES] ").AppendLine(notes);
        }

        // Always plain newlines, whatever the platform
        return text.ToString().ReplaceLineEndings("\n").TrimEnd();
    }

    private static string RoundLine(GodDigestInput input)
    {
        var parts = new List<string>
        {
            $"T+{(int) input.RoundTime.TotalMinutes}m",
            $"{input.Players} players",
            $"{input.Deaths} deaths",
        };

        if (input.TimeOfDay != null)
            parts.Add(input.TimeOfDay);
        if (input.Weather != null)
            parts.Add(input.Weather);
        if (input.FaultsAwake is { } faults)
            parts.Add($"{faults} faults awake");
        if (input.LootLeftPercent is { } loot)
            parts.Add($"loot {loot}% left");

        return string.Join(" | ", parts);
    }

    private static void AppendSubjects(StringBuilder text, GodDigestInput input, int cap)
    {
        var ranked = input.Subjects
            .OrderByDescending(s => Salience(s, input.Now))
            .ThenBy(s => s.Number)
            .ToList();

        foreach (var subject in ranked.Take(cap))
        {
            text.AppendLine(SubjectLine(subject, input.Now));
        }

        var rest = ranked.Skip(cap).ToList();
        if (rest.Count > 0)
        {
            var alive = rest.Count(s => s.Alive);
            text.AppendLine($"{rest.Count} others: {alive} alive, {rest.Count - alive} dead, quiet");
        }
        else if (ranked.Count == 0)
        {
            text.AppendLine("none");
        }
    }

    /// <summary>
    ///     How interesting a subject is right now. Recent, severe things count most.
    /// </summary>
    public static float Salience(Subject subject, TimeSpan now)
    {
        var age = Math.Max(0, (now - subject.LastEventTime).TotalSeconds);
        var recency = subject.LastEvent == null ? 0f : (float) Math.Exp(-age / 120.0);

        return recency * 5f
               + subject.KillsOfPlayers * 3f
               + subject.KillsOfMobs * 0.5f
               + subject.DamageDealt / 100f
               + subject.DamageTaken / 150f
               + subject.Prayers * 4f
               + (subject.Alive ? 1f : 0f);
    }

    private static string SubjectLine(Subject s, TimeSpan now)
    {
        var parts = new List<string> { $"S{s.Number} {s.Name.ToUpperInvariant()}" };

        parts.Add(s.Alive ? $"alive {(int) (now - s.LifeStart).TotalMinutes}m" : "dead");
        parts.Add($"lives {s.Lives}");
        parts.Add($"errors {s.Errors}");

        if (s.Sector != null)
            parts.Add(s.Sector);

        if (s.Near.Count > 0)
            parts.Add("with " + string.Join(",", s.Near.Select(n => $"S{n}")));

        if (s.LastEvent != null)
            parts.Add($"last: {s.LastEvent} T-{Minutes(now - s.LastEventTime)}");

        if (s.KillsOfPlayers + s.KillsOfMobs > 0)
            parts.Add($"kills {s.KillsOfPlayers}p {s.KillsOfMobs}m");

        if (s.SpeechLines > 0)
            parts.Add($"spoke {s.SpeechLines}");

        if (s.Prayers > 0)
            parts.Add($"prayed {s.Prayers}");

        if (!s.Alive && s.LastWords != null)
            parts.Add($"last words \"{s.LastWords}\"");

        return string.Join(" | ", parts);
    }

    private static IEnumerable<string> EventLines(GodDigestInput input, List<(string Name, Subject Subject)> names, int cap)
    {
        var merged = new List<(GodEvent First, string Text, int Count)>();
        foreach (var ev in input.Drain.Events)
        {
            var text = Tag(ev.Text, names);
            var index = merged.FindIndex(m => m.Text == text && m.First.Kind == ev.Kind);
            if (index >= 0)
                merged[index] = (merged[index].First, text, merged[index].Count + 1);
            else
                merged.Add((ev, text, 1));
        }

        // The most severe, then the newest, up to the cap, shown in the order they happened
        var chosen = merged
            .OrderByDescending(m => m.First.Severity)
            .ThenByDescending(m => m.First.Time)
            .Take(cap)
            .OrderBy(m => m.First.Time)
            .ToList();

        foreach (var (first, text, count) in chosen)
        {
            var line = $"T-{Minutes(input.Now - first.Time)} {text.Replace('\n', ' ')}";
            yield return count > 1 ? $"{line} (x{count})" : line;
        }
    }

    private static string QuoteLine(GodQuote q)
    {
        var who = q.Subject > 0 ? $"S{q.Subject} {q.Name.ToUpperInvariant()}" : q.Name.ToUpperInvariant();
        var place = q.Sector != null ? $" {q.Sector}" : string.Empty;
        var whisper = q.Whisper ? " (whispering)" : string.Empty;
        return $"{who}{place}{whisper}: \"{q.Text.ReplaceLineEndings(" ")}\"";
    }

    /// <summary>
    ///     Character names, longest first, so a name inside a longer one is not tagged twice.
    /// </summary>
    private static List<(string Name, Subject Subject)> NameMap(List<Subject> subjects)
    {
        return subjects
            .Where(s => s.Name != string.Empty)
            .Select(s => (s.Name, s))
            .OrderByDescending(p => p.Name.Length)
            .ToList();
    }

    /// <summary>
    ///     Puts the subject number in front of every known character name in an event text.
    /// </summary>
    private static string Tag(string text, List<(string Name, Subject Subject)> names)
    {
        if (names.Count == 0)
            return text;

        // One pass over all names, longest first, so a name inside a longer one (or inside a tag just made) is not tagged again
        var pattern = string.Join("|", names.Select(n => Regex.Escape(n.Name)));
        return Regex.Replace(text, @"(?<!\w)(?:" + pattern + @")(?!\w)", match =>
        {
            var subject = names.First(n => string.Equals(n.Name, match.Value, StringComparison.OrdinalIgnoreCase)).Subject;
            return $"S{subject.Number} {match.Value.ToUpperInvariant()}";
        }, RegexOptions.IgnoreCase);
    }

    private static string Minutes(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        return span.TotalMinutes >= 1 ? $"{(int) span.TotalMinutes}m" : $"{(int) span.TotalSeconds}s";
    }
}
