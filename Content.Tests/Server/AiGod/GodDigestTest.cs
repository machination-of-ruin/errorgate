using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._ERRORGATE.AiGod;
using Content.Shared._ERRORGATE.AiGod;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests.Server.AiGod;

/// <summary>
///     The digest builder, the mention filter and the event buffer: plain code, no server needed.
/// </summary>
[TestFixture]
public sealed class GodDigestTest
{
    private static readonly TimeSpan Now = TimeSpan.FromMinutes(90);

    private static Subject MakeSubject(int number, string name, bool alive = true)
    {
        return new Subject
        {
            Number = number,
            User = new NetUserId(Guid.NewGuid()),
            Name = name,
            Alive = alive,
            Lives = 1,
            LifeStart = TimeSpan.FromMinutes(60),
        };
    }

    private static GodEvent MakeEvent(string text, GodEventKind kind = GodEventKind.Combat, GodEventSeverity severity = GodEventSeverity.Low, double minutesAgo = 1)
    {
        return new GodEvent(Now - TimeSpan.FromMinutes(minutesAgo), kind, severity, text, Array.Empty<string>());
    }

    private static GodQuote MakeQuote(int subject, string name, string text, string? sector = "C4")
    {
        return new GodQuote(Now, subject, name, sector, text, false);
    }

    private static GodDigestInput Input(params Subject[] subjects)
    {
        return new GodDigestInput { Now = Now, RoundTime = Now, Players = subjects.Length, Subjects = subjects.ToList() };
    }

    [Test]
    public void NothingHappenedMeansNothingToAsk()
    {
        var result = GodDigest.Build(Input(MakeSubject(1, "Ivan")));

        Assert.That(result.Empty, Is.True);
        Assert.That(result.Text, Does.Contain("[ROUND] T+90m | 1 players | 0 deaths"));
    }

    [Test]
    public void AnythingToReportIsNotEmpty()
    {
        var input = Input(MakeSubject(1, "Ivan"));
        input.Drain.Events.Add(MakeEvent("Ivan died.", GodEventKind.Death, GodEventSeverity.High));

        Assert.That(GodDigest.Build(input).Empty, Is.False);
    }

    [Test]
    public void SubjectsAreRankedAndTheRestIsOneLine()
    {
        var subjects = Enumerable.Range(1, 12).Select(i => MakeSubject(i, $"Person{i}")).ToArray();
        subjects[9].KillsOfPlayers = 4;
        subjects[9].LastEvent = "killed Person3";
        subjects[9].LastEventTime = Now - TimeSpan.FromSeconds(20);

        var text = GodDigest.Build(Input(subjects)).Text;
        var lines = text.Split('\n').Where(l => l.StartsWith("S")).ToList();

        Assert.That(lines, Has.Count.EqualTo(GodDigest.MaxSubjectLines));
        Assert.That(lines[0], Does.StartWith("S10 PERSON10"), "The most interesting subject comes first.");
        Assert.That(lines[0], Does.Contain("kills 4p 0m"));
        Assert.That(text, Does.Contain("4 others: 4 alive, 0 dead, quiet"));
    }

    [Test]
    public void SubjectLineShowsSectorCompanyAndLastWordsOfTheDead()
    {
        var ivan = MakeSubject(17, "Ivan Petrov");
        ivan.Sector = "C4";
        ivan.Near.AddRange(new[] { 4, 9 });
        ivan.SpeechLines = 14;
        var boris = MakeSubject(4, "Boris", alive: false);
        boris.Errors = 3;
        boris.LastWords = "wait";

        var text = GodDigest.Build(Input(ivan, boris)).Text;

        Assert.That(text, Does.Contain("S17 IVAN PETROV | alive 30m | lives 1 | errors 0 | C4 | with S4,S9 | spoke 14"));
        Assert.That(text, Does.Contain("S4 BORIS | dead | lives 1 | errors 3"));
        Assert.That(text, Does.Contain("last words \"wait\""));
    }

    [Test]
    public void EventsAreTaggedMergedAndOrdered()
    {
        var ivan = MakeSubject(1, "Ivan");
        var longIvan = MakeSubject(2, "Ivan Petrov");
        var input = Input(ivan, longIvan);
        input.Drain.Events.Add(MakeEvent("Ivan Petrov was hurt by Ivan (12 damage).", minutesAgo: 5));
        input.Drain.Events.Add(MakeEvent("Ivan was hurt (3 damage).", minutesAgo: 4));
        input.Drain.Events.Add(MakeEvent("Ivan was hurt (3 damage).", minutesAgo: 3));
        input.Drain.Events.Add(MakeEvent("Ivan died.", GodEventKind.Death, GodEventSeverity.High, minutesAgo: 2));

        var lines = GodDigest.Build(input).Text.Split('\n').SkipWhile(l => l != "[EVENTS]").Skip(1).Take(3).ToList();

        Assert.That(lines[0], Does.Contain("S2 IVAN PETROV was hurt by S1 IVAN"), "A name inside a longer name is not tagged twice.");
        Assert.That(lines[1], Does.Contain("S1 IVAN was hurt (3 damage). (x2)"));
        Assert.That(lines[2], Does.Contain("S1 IVAN died."));
        Assert.That(lines[0], Does.StartWith("T-5m"));
    }

    [Test]
    public void PrayersAreAlwaysFirstAndRaw()
    {
        var input = Input(MakeSubject(3, "Olga"));
        input.Drain.Prayers.Add(MakeQuote(3, "Olga", "Please. Open the door. I did not mean to leave him there.", "D2"));
        input.Drain.Events.Add(MakeEvent("Olga arrived."));

        var text = GodDigest.Build(input).Text;
        var lines = text.Split('\n').ToList();

        Assert.That(lines.IndexOf("[PRAYERS AND MENTIONS]"), Is.LessThan(lines.IndexOf("[SUBJECTS]")));
        Assert.That(text, Does.Contain("PRAYER S3 OLGA D2: \"Please. Open the door. I did not mean to leave him there.\""));
    }

    [Test]
    public void MoreThanTenMentionsKeepTheNewestAndReturnTheRest()
    {
        var input = Input(MakeSubject(1, "Ivan"));
        for (var i = 1; i <= 15; i++)
        {
            input.Drain.Mentions.Add(MakeQuote(1, "Ivan", $"god number {i}"));
        }

        var result = GodDigest.Build(input);

        Assert.That(result.OverflowMentions.Select(q => q.Text), Is.EqualTo(Enumerable.Range(1, 5).Select(i => $"god number {i}")));
        Assert.That(result.Text, Does.Contain("god number 15"));
        Assert.That(result.Text, Does.Contain("god number 6\""));
        Assert.That(result.Text, Does.Not.Contain("god number 5\""));

        // With the summary in, the model gets it before the raw lines
        input.EarlierMentionsSummary = "five lines of the same plea";
        var withSummary = GodDigest.Build(input).Text;
        Assert.That(withSummary.IndexOf("EARLIER MENTIONS", StringComparison.Ordinal), Is.LessThan(withSummary.IndexOf("MENTION S1", StringComparison.Ordinal)));
    }

    [Test]
    public void BigRoundsStayUnderTheBudgetAndPrayersSurvive()
    {
        var subjects = Enumerable.Range(1, 40).Select(i => MakeSubject(i, $"Person Number {i}")).ToArray();
        var input = Input(subjects);
        for (var i = 0; i < 300; i++)
        {
            input.Drain.Events.Add(MakeEvent($"Person Number {i % 40 + 1} was hurt by Person Number {(i + 7) % 40 + 1} ({i} damage) near the old factory.",
                severity: (GodEventSeverity) (i % 3), minutesAgo: i / 10.0));
        }

        for (var i = 0; i < 30; i++)
        {
            input.Drain.Speech.Add(MakeQuote(i % 40 + 1, $"Person Number {i % 40 + 1}", new string('x', 150)));
        }

        input.Drain.Prayers.Add(MakeQuote(5, "Person Number 5", "a prayer that must not be lost"));
        for (var i = 0; i < 6; i++)
        {
            input.HerRecent.Add($"subtle S{i} T-{i}m done");
        }

        var result = GodDigest.Build(input);

        Assert.That(result.Tokens, Is.LessThanOrEqualTo(GodDigest.MaxTokens));
        Assert.That(result.Text, Does.Contain("a prayer that must not be lost"));
        Assert.That(result.Text, Does.Not.Contain("[SPEECH]"), "Context speech is the first thing to go.");
        Assert.That(result.Text.Split('\n').Count(l => l.StartsWith("T-")), Is.LessThanOrEqualTo(GodDigest.MaxEventLines));
    }

    [Test]
    public void NotesAreCutAndAccountNamesAreNeverThere()
    {
        var input = Input(MakeSubject(1, "Ivan"));
        input.Notes = new string('n', 500);
        input.Drain.Events.Add(MakeEvent("Ivan arrived.", GodEventKind.Arrival));

        var text = GodDigest.Build(input).Text;

        Assert.That(text, Does.Contain("[NOTES] " + new string('n', 300) + "\n").Or.Contain("[NOTES] " + new string('n', 300)));
        Assert.That(text, Does.Not.Contain(new string('n', 301)));
    }

    // Mention filter

    private static GodMentionFilter Filter()
    {
        return new GodMentionFilter(new GodMentionsPrototype
        {
            Words = { "god", "machine", "pray", "who is watching", "someone is watching" },
            Pronouns = { "she", "her", "it" },
            WatchWords = { "watching", "listening", "knows" },
        });
    }

    [TestCase("God help us", true)]
    [TestCase("i pray every night", true)]
    [TestCase("the machine knows", true)]
    [TestCase("Someone   is watching us?", true)]
    [TestCase("she is watching", true)]
    [TestCase("I think it knows", true)]
    [TestCase("praying again", true)]
    [TestCase("good morning", false)]
    [TestCase("I took her bag", false)]
    [TestCase("it is cold here", false)]
    [TestCase("she went north", false)]
    [TestCase("", false)]
    public void MentionFilterReadsOrdinarySpeech(string line, bool expected)
    {
        Assert.That(Filter().IsMention(line), Is.EqualTo(expected), line);
    }

    // Buffer

    [Test]
    public void BufferKeepsQuotesInTheirOwnLists()
    {
        var buffer = new GodEventBuffer { QuoteCapacity = 2, ContextCapacity = 3 };
        for (var i = 0; i < 4; i++)
        {
            buffer.AddPrayer(MakeQuote(1, "A", $"prayer {i}"));
            buffer.AddMention(MakeQuote(1, "A", $"mention {i}"));
            buffer.AddSpeech(MakeQuote(1, "A", $"speech {i}"));
        }

        Assert.That(buffer.Prayers.Select(q => q.Text), Is.EqualTo(new[] { "prayer 2", "prayer 3" }));
        Assert.That(buffer.Mentions.Select(q => q.Text), Is.EqualTo(new[] { "mention 2", "mention 3" }));
        Assert.That(buffer.Speech.Select(q => q.Text), Is.EqualTo(new[] { "speech 1", "speech 2", "speech 3" }));

        var drain = buffer.DrainAll(Now);
        Assert.That(drain.Prayers, Has.Count.EqualTo(2));
        Assert.That(buffer.Prayers, Is.Empty);
        Assert.That(buffer.Mentions, Is.Empty);
        Assert.That(buffer.Speech, Is.Empty);
    }
}
