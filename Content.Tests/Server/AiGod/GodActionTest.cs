using System;
using System.Collections.Generic;
using Content.Server._ERRORGATE.AiGod;
using Content.Shared._ERRORGATE.AiGod;
using NUnit.Framework;

namespace Content.Tests.Server.AiGod;

/// <summary>
///     The validator and the budget: what is thrown away before anything happens.
/// </summary>
[TestFixture]
public sealed class GodActionTest
{
    private static GodVoicePrototype Voice()
    {
        // The same patterns as Resources/Prototypes/_ERRORGATE/AiGod/voice.yml
        return new GodVoicePrototype
        {
            BannedWords = { "badword" },
            OrderPatterns =
            {
                @"\byou\s+(must|should|need\s+to|have\s+to|ought\s+to|shall|will\s+have\s+to)\b",
                @"\b(we|they|everyone|all\s+of\s+you|he|she|the\s+\w+)\s+(must|should|needs?\s+to|has\s+to|have\s+to)\b",
                @"(^|[.!?:;]\s*)(go|run|walk|move|head|travel|return|come|follow|find|seek|search|take|bring|fetch|give|use|open|close|enter|leave|stay|hide|kill|attack|destroy|protect|help|save|trust|obey|worship|pray|kneel|rise|fight|stop|wait|listen|look|watch|remember)\s+(to|toward|towards|into|the|a|an|your|their|him|her|them|us|me|back|here|there|away|north|south|east|west|up|down|out|now|together|alone)\b",
                @"\b(the\s+way|the\s+key|the\s+answer|the\s+solution|your\s+(goal|task|mission|purpose|objective))\b",
                @"\b(objective|mission|quest|task)\b",
                @"\b(try|consider|remember)\s+(to|that|the)\b",
                @"\bif\s+you\s+want\b",
            },
        };
    }

    private static GodActionValidator Validator() => new(Voice());

    private static GodAction Subtle(string text, params int[] targets)
    {
        return new GodAction { Type = GodActionType.Subtle, Targets = new List<int>(targets), Text = text };
    }

    [TestCase("SUBJECT S4: ERROR LOGGED.")]
    [TestCase("S17 DID NOT SLEEP. THE DOOR REMEMBERS.")]
    [TestCase("SECTOR C4: ONE LESS.")]
    [TestCase("It was cold where Ivan fell.")]
    [TestCase("ERROR LOGGED. NO CAUSE RECORDED.")]
    public void PlainLogLinesPass(string line)
    {
        var action = Subtle(line, 4);
        Assert.That(Validator().Validate(action, new GodValidationContext()), Is.Null, line);
    }

    [TestCase("You must go to the factory.")]
    [TestCase("you should trust the stranger")]
    [TestCase("Go to sector C4.")]
    [TestCase("S4 is dead. Find the key.")]
    [TestCase("Open the door and run.")]
    [TestCase("Your goal is the bunker.")]
    [TestCase("Everyone needs to leave")]
    [TestCase("Try to remember that")]
    [TestCase("If you want to live")]
    [TestCase("This is a quest.")]
    [TestCase("Help him")]
    public void OrdersAndHintsAreThrownAway(string line)
    {
        var result = Validator().Validate(Subtle(line, 4), new GodValidationContext());
        Assert.That(result, Is.Not.Null, line);
    }

    [Test]
    public void MarkupIsStrippedAndWhitespaceSqueezed()
    {
        Assert.That(GodActionValidator.Clean("[color=red]  ERROR \n\t LOGGED [/color]"), Is.EqualTo("ERROR LOGGED"));
        Assert.That(GodActionValidator.Clean("a\u0007b"), Is.EqualTo("ab"));

        var action = Subtle("[bold]S4:[/bold]   ERROR", 4);
        Assert.That(Validator().Validate(action, new GodValidationContext()), Is.Null);
        Assert.That(action.Text, Is.EqualTo("S4: ERROR"), "The cleaned text is what is kept.");
    }

    [Test]
    public void TextRulesAreEnforced()
    {
        var context = new GodValidationContext { SubtleMaxLength = 20, AnnounceMaxLength = 30 };
        var validator = Validator();

        Assert.That(validator.Validate(Subtle("", 1), context), Is.EqualTo("no text"));
        Assert.That(validator.Validate(Subtle("[b][/b]", 1), context), Is.EqualTo("no text"));
        Assert.That(validator.Validate(Subtle(new string('A', 21), 1), context), Does.Contain("longer than 20"));
        Assert.That(validator.Validate(Subtle(new string('A', 20), 1), context), Is.Null);
        Assert.That(validator.Validate(Subtle("ERROR é", 1), context), Is.EqualTo("characters outside plain text"));
        Assert.That(validator.Validate(Subtle("a badword here", 1), context), Is.EqualTo("banned word"));

        var announce = new GodAction { Type = GodActionType.Announce, Text = new string('A', 31) };
        Assert.That(validator.Validate(announce, context), Does.Contain("longer than 30"));
    }

    [Test]
    public void TargetsAreChecked()
    {
        var validator = Validator();
        var context = new GodValidationContext
        {
            SubjectExists = n => n != 9,
            SubjectReachable = n => n != 5,
            SectorExists = s => s == "C4",
        };

        Assert.That(validator.Validate(Subtle("ERROR", 1), context), Is.Null);
        Assert.That(validator.Validate(Subtle("ERROR"), context), Is.EqualTo("a subtle message needs a target"));
        Assert.That(validator.Validate(Subtle("ERROR", 9), context), Is.EqualTo("no subject S9"));
        Assert.That(validator.Validate(Subtle("ERROR", 5), context), Is.EqualTo("S5 cannot be reached"));
        Assert.That(validator.Validate(Subtle("ERROR", 1, 2, 3, 4, 6, 7, 8, 10, 11), context), Is.EqualTo("too many targets"));

        var glitch = new GodAction { Type = GodActionType.Glitch, Sector = "C4" };
        Assert.That(validator.Validate(glitch, context), Is.Null);
        glitch.Sector = "Z9";
        Assert.That(validator.Validate(glitch, context), Is.EqualTo("no sector Z9"));
        glitch.Sector = "C4";
        glitch.Targets.Add(1);
        Assert.That(validator.Validate(glitch, context), Does.Contain("not both"));
        Assert.That(validator.Validate(new GodAction { Type = GodActionType.Glitch }, context), Is.EqualTo("a glitch needs a subject or a sector"));
        Assert.That(validator.Validate(new GodAction { Type = GodActionType.Glitch, Targets = { 1 }, Kind = "fire" }, context), Does.Contain("unknown glitch kind"));

        var announce = new GodAction { Type = GodActionType.Announce, Text = "ERROR", Targets = { 1 } };
        Assert.That(validator.Validate(announce, context), Is.EqualTo("an announcement is for everyone"));
    }

    // Budget

    private static readonly TimeSpan T0 = TimeSpan.FromMinutes(10);

    private static GodBudget Budget()
    {
        var budget = new GodBudget { PerMinute = 6f, Cap = 20f, TargetCooldown = 60f, AnnounceCooldown = 300f, AnnouncePerHour = 2 };
        budget.Reset(T0);
        return budget;
    }

    [Test]
    public void PointsBuildUpToTheCapAndAreSpent()
    {
        var budget = Budget();
        Assert.That(budget.Points, Is.EqualTo(20f), "A round starts with a full budget.");

        var announce = new GodAction { Type = GodActionType.Announce, Text = "X" };
        budget.Spend(announce, T0);
        Assert.That(budget.Points, Is.EqualTo(12f));

        budget.Advance(T0 + TimeSpan.FromMinutes(1));
        Assert.That(budget.Points, Is.EqualTo(18f), "Six points a minute.");

        budget.Advance(T0 + TimeSpan.FromMinutes(30));
        Assert.That(budget.Points, Is.EqualTo(20f), "The cap holds.");
    }

    [Test]
    public void NotEnoughPointsStopsAnAction()
    {
        var budget = Budget();
        budget.Points = 0.5f;

        Assert.That(budget.Check(Subtle("X", 1), T0), Does.StartWith("not enough points"));

        budget.Points = 1f;
        Assert.That(budget.Check(Subtle("X", 1), T0), Is.Null);
    }

    [Test]
    public void ATargetHasACooldown()
    {
        var budget = Budget();
        var action = Subtle("X", 4);

        Assert.That(budget.Check(action, T0), Is.Null);
        budget.Spend(action, T0);

        Assert.That(budget.Check(Subtle("Y", 4), T0 + TimeSpan.FromSeconds(30)), Does.Contain("S4 was addressed 30s ago"));
        Assert.That(budget.Check(Subtle("Y", 5), T0 + TimeSpan.FromSeconds(30)), Is.Null, "Someone else is fine.");
        Assert.That(budget.Check(Subtle("Y", 4), T0 + TimeSpan.FromSeconds(61)), Is.Null);

        // A sector is a target of its own
        var glitch = new GodAction { Type = GodActionType.Glitch, Sector = "C4" };
        budget.Spend(glitch, T0);
        Assert.That(budget.Check(new GodAction { Type = GodActionType.Glitch, Sector = "C4" }, T0 + TimeSpan.FromSeconds(5)), Does.Contain("sector C4"));
        Assert.That(budget.Check(new GodAction { Type = GodActionType.Glitch, Sector = "D4" }, T0 + TimeSpan.FromSeconds(5)), Is.Null);
    }

    [Test]
    public void AnnouncementsAreRare()
    {
        var budget = Budget();
        budget.Cap = 100f;
        budget.Points = 100f;

        var announce = new GodAction { Type = GodActionType.Announce, Text = "X" };
        Assert.That(budget.Check(announce, T0), Is.Null);
        budget.Spend(announce, T0);

        Assert.That(budget.Check(announce, T0 + TimeSpan.FromSeconds(100)), Is.EqualTo("announced too recently"));
        Assert.That(budget.Check(announce, T0 + TimeSpan.FromSeconds(301)), Is.Null);
        budget.Spend(announce, T0 + TimeSpan.FromSeconds(301));

        Assert.That(budget.Check(announce, T0 + TimeSpan.FromSeconds(700)), Is.EqualTo("too many announcements this hour"));
        Assert.That(budget.Check(announce, T0 + TimeSpan.FromMinutes(61)), Is.Null, "The hour passes.");
    }
}
