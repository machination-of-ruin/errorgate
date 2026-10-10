using System.Linq;
using Content.Server._ERRORGATE.AiGod;
using NUnit.Framework;

namespace Content.Tests.Server.AiGod;

/// <summary>
///     Reading what the model sends back: forgiving about the wrapping, strict about the content.
/// </summary>
[TestFixture]
public sealed class GodReplyParserTest
{
    [Test]
    public void ACleanReplyIsRead()
    {
        var reply = GodReplyParser.Parse("""
            {"notes": "S4 prays often.", "actions": [
              {"type": "subtle", "targets": ["S4", "S9"], "text": "S4: STILL PRESENT."},
              {"type": "announce", "text": "ONE LESS."},
              {"type": "glitch", "sector": "c4"},
              {"type": "glitch", "targets": [7]}
            ]}
            """);

        Assert.That(reply.Parsed, Is.True);
        Assert.That(reply.Problems, Is.EqualTo(new[] { "more than 3 actions, the rest were dropped" }));
        Assert.That(reply.Notes, Is.EqualTo("S4 prays often."));
        Assert.That(reply.Actions, Has.Count.EqualTo(3), "Three actions at most, the fourth is dropped.");

        Assert.That(reply.Actions[0].Type, Is.EqualTo(GodActionType.Subtle));
        Assert.That(reply.Actions[0].Targets, Is.EqualTo(new[] { 4, 9 }));
        Assert.That(reply.Actions[0].Text, Is.EqualTo("S4: STILL PRESENT."));
        Assert.That(reply.Actions[1].Type, Is.EqualTo(GodActionType.Announce));
        Assert.That(reply.Actions[2].Sector, Is.EqualTo("C4"), "Sector names are made capitals.");
    }

    [Test]
    public void TheWrappingIsForgiven()
    {
        var fenced = GodReplyParser.Parse("Here is my answer:\n```json\n{\"notes\": \"\", \"actions\": [{\"type\": \"glitch\", \"targets\": [\"S1\"]},],}\n```\nDone.");

        Assert.That(fenced.Parsed, Is.True);
        Assert.That(fenced.Actions, Has.Count.EqualTo(1));
        Assert.That(fenced.Actions[0].Targets, Is.EqualTo(new[] { 1 }));
    }

    [TestCase("")]
    [TestCase("I am the machine. I do nothing.")]
    [TestCase("{ not json")]
    [TestCase("[1, 2, 3]")]
    public void NoJsonObjectMeansNothing(string text)
    {
        var reply = GodReplyParser.Parse(text);

        Assert.That(reply.Parsed, Is.False);
        Assert.That(reply.Actions, Is.Empty);
        Assert.That(reply.Problems, Is.Not.Empty);
    }

    [Test]
    public void NothingToDoIsAReply()
    {
        var reply = GodReplyParser.Parse("{\"notes\": \"quiet\", \"actions\": []}");

        Assert.That(reply.Parsed, Is.True);
        Assert.That(reply.Actions, Is.Empty);
        Assert.That(reply.Problems, Is.Empty);
        Assert.That(reply.Notes, Is.EqualTo("quiet"));
    }

    [Test]
    public void UnknownThingsAreDroppedAndReported()
    {
        var reply = GodReplyParser.Parse("""
            {"actions": [
              {"type": "kill", "targets": ["S4"]},
              "just a string",
              {"type": "subtle", "targets": ["S4", "somebody"], "text": "ERROR."},
              {"text": "no type"}
            ]}
            """);

        Assert.That(reply.Actions, Has.Count.EqualTo(1), "Only the subtle message is a known action.");
        Assert.That(reply.Actions[0].Targets, Is.EqualTo(new[] { 4 }));
        Assert.That(reply.Problems.Any(p => p.Contains("unknown action type kill")), Is.True);
        Assert.That(reply.Problems.Any(p => p.Contains("not an object")), Is.True);
        Assert.That(reply.Problems.Any(p => p.Contains("not a subject: somebody")), Is.True);
        Assert.That(reply.Problems.Any(p => p.Contains("unknown action type (none)")), Is.True);
    }

    [Test]
    public void NotesAreCutAndTargetsAreNotRepeated()
    {
        var reply = GodReplyParser.Parse("{\"notes\": \"" + new string('n', 500) + "\", \"actions\": [{\"type\": \"subtle\", \"targets\": [\"S4\", \"4\", 4], \"text\": \"X\"}]}");

        Assert.That(reply.Notes, Has.Length.EqualTo(GodReplyParser.MaxNotesLength));
        Assert.That(reply.Actions[0].Targets, Is.EqualTo(new[] { 4 }));
    }

    [Test]
    public void ALoneTargetFieldIsAccepted()
    {
        var reply = GodReplyParser.Parse("{\"actions\": [{\"type\": \"subtle\", \"target\": \"S12\", \"text\": \"X\"}]}");

        Assert.That(reply.Actions[0].Targets, Is.EqualTo(new[] { 12 }));
    }

    [Test]
    public void WriterListsAreRead()
    {
        var lines = GodReplyParser.ParseLines("```{\"error\": [\"ONE.\", 5, \"TWO.\"], \"omen\": [\"A.\"], \"prayer\": \"nope\", \"other\": [\"X\"]}```");

        Assert.That(lines["error"], Is.EqualTo(new[] { "ONE.", "TWO." }), "Only strings count.");
        Assert.That(lines["omen"], Is.EqualTo(new[] { "A." }));
        Assert.That(lines.ContainsKey("prayer"), Is.False, "A list that is not a list is ignored.");
        Assert.That(lines.ContainsKey("other"), Is.False);
        Assert.That(GodReplyParser.ParseLines("nothing"), Is.Empty);
    }
}
