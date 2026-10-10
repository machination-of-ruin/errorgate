using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared._ERRORGATE.AiGod;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     First stage of noticing a mention: a cheap check of one line of speech. The model gets the lines that pass raw,
///     and the last few ordinary lines as well, so it can notice what this check misses.
/// </summary>
public sealed class GodMentionFilter
{
    private readonly Regex _words;
    private readonly Regex _pronouns;
    private readonly Regex _watch;

    public GodMentionFilter(GodMentionsPrototype list)
    {
        _words = Build(list.Words);
        _pronouns = Build(list.Pronouns);
        _watch = Build(list.WatchWords);
    }

    public bool IsMention(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (_words.IsMatch(text))
            return true;

        // "she", "her" and "it" are everywhere: only next to a word about watching, hearing or judging
        return _pronouns.IsMatch(text) && _watch.IsMatch(text);
    }

    private static Regex Build(List<string> entries)
    {
        if (entries.Count == 0)
            return new Regex("(?!)");

        // Whole words, case insensitive. A word may end in more letters ("god" also catches "gods", "godly").
        var alternatives = string.Join("|", entries.Select(e =>
            string.Join(@"\s+", e.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape))));
        return new Regex($@"\b(?:{alternatives})\w*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }
}
