#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._ERRORGATE.AiGod;
using Content.Server.Chat.Systems;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Language;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     AI GOD step 1, the senses: sectors, the ledger, prayers at altars, mentions in speech, the digest.
/// </summary>
[TestFixture]
public sealed class GodStepOneTest
{
    // Sectors

    [Test]
    public async Task SectorsAreLetteredWestToEastAndNumberedNorthToSouth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var sectors = entMan.System<SectorSystem>();
        var mapSys = entMan.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        EntityUid gridUid = default;
        MapGridComponent? grid = null;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(ErrorgateCVars.GodSectorSize, 20);
            sectors.Reset();

            mapSys.CreateMap(out var mapId);
            var made = server.MapMan.CreateGridEntity(mapId);
            gridUid = made.Owner;
            grid = made.Comp;

            // 40 by 40 tiles of floor, two squares each way, and one lone tile far away that makes no square of its own
            var plating = new Tile(tileDefs["Plating"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 40; x++)
            {
                for (var y = 0; y < 40; y++)
                {
                    tiles.Add((new Vector2i(x, y), plating));
                }
            }

            tiles.Add((new Vector2i(99, 39), plating));
            mapSys.SetTiles(gridUid, grid, tiles);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(sectors.SectorOf(gridUid, grid!, new Vector2i(5, 35)), Is.EqualTo("A1"), "North-west is A1.");
            Assert.That(sectors.SectorOf(gridUid, grid!, new Vector2i(25, 35)), Is.EqualTo("B1"));
            Assert.That(sectors.SectorOf(gridUid, grid!, new Vector2i(5, 5)), Is.EqualTo("A2"), "Rows count southward.");
            Assert.That(sectors.SectorOf(gridUid, grid!, new Vector2i(25, 5)), Is.EqualTo("B2"));
            Assert.That(sectors.SectorOf(gridUid, grid!, new Vector2i(99, 39)), Is.Null, "A square with next to no floor does not exist.");
            Assert.That(sectors.SectorNames(gridUid, grid!), Is.EqualTo(new[] { "A1", "B1", "A2", "B2" }));
            Assert.That(sectors.TryGetCenter(gridUid, grid!, "Z9", out _), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(0, "A")]
    [TestCase(25, "Z")]
    [TestCase(26, "AA")]
    [TestCase(27, "AB")]
    public void ColumnsRunPastZ(int column, string letters)
    {
        Assert.That(SectorSystem.GridSectors.Label(column, 2), Is.EqualTo(letters + "3"));
    }

    // Ledger, prayers, mentions

    private sealed class World
    {
        public Content.IntegrationTests.Pair.TestPair Pair = default!;
        public EntityUid Ivan;
        public EntityUid Boris;
        public EntityUid Rat;
        public Robust.Shared.Player.ICommonSession Session = default!;
    }

    private static async Task<World> Setup()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var world = new World { Pair = pair, Session = server.ResolveDependency<IPlayerManager>().Sessions.Single() };

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(ErrorgateCVars.GodEnabled, true);
            entMan.System<GodObserverSystem>().Clear();
            entMan.System<GodLedgerSystem>().Clear();

            entMan.System<MapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var meta = entMan.System<MetaDataSystem>();
            world.Ivan = entMan.SpawnEntity("MobHuman", coords);
            world.Boris = entMan.SpawnEntity("MobHuman", coords);
            world.Rat = entMan.SpawnEntity("MobHuman", coords);
            meta.SetEntityName(world.Ivan, "Ivan Petrov");
            meta.SetEntityName(world.Boris, "Boris");
            meta.SetEntityName(world.Rat, "sewer rat");

            var mind = entMan.System<SharedMindSystem>();
            mind.TransferTo(mind.CreateMind(world.Session.UserId), world.Ivan);

            // Boris is a second player: a mind with an account but no connection
            var borisMind = mind.CreateMind(null);
            typeof(MindComponent).GetProperty(nameof(MindComponent.UserId))!.SetValue(entMan.GetComponent<MindComponent>(borisMind), (NetUserId?) new NetUserId(Guid.NewGuid()));
            mind.TransferTo(borisMind, world.Boris);
        });

        return world;
    }

    private static void Hurt(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid victim, EntityUid? origin, int amount)
    {
        var spec = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(amount));
        server.EntMan.System<DamageableSystem>().TryChangeDamage(victim, spec, origin: origin);
    }

    private static void Say(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid who, string words)
    {
        var universal = server.ProtoMan.Index<LanguagePrototype>("Universal");
        server.EntMan.EventBus.RaiseLocalEvent(who, new EntitySpokeEvent(who, words, null, false, universal), true);
    }

    [Test]
    public async Task TheLedgerKeepsASubjectPerPlayerWithTheirRecord()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var entMan = server.EntMan;
        var ledger = entMan.System<GodLedgerSystem>().Ledger;

        await server.WaitPost(() =>
        {
            Hurt(server, world.Ivan, world.Boris, 20);
            Hurt(server, world.Rat, world.Ivan, 30);
            entMan.System<MobStateSystem>().ChangeMobState(world.Rat, MobState.Dead);
            Say(server, world.Ivan, "stay back");
            Hurt(server, world.Ivan, world.Boris, 900);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(ledger.Count, Is.EqualTo(2), "One subject per player, none for the rat.");

            var ivan = ledger.ByName("Ivan Petrov")!;
            var boris = ledger.ByName("Boris")!;
            Assert.That(new[] { ivan.Number, boris.Number }, Is.EquivalentTo(new[] { 1, 2 }));

            var dump = string.Join(" / ", ledger.All.Select(x => $"S{x.Number} {x.Name} err={x.Errors} alive={x.Alive} kp={x.KillsOfPlayers} km={x.KillsOfMobs} dealt={x.DamageDealt} taken={x.DamageTaken} spoke={x.SpeechLines}"));
            Assert.That(ivan.Errors, Is.EqualTo(1), "Death is an error. " + dump);
            Assert.That(ivan.Alive, Is.False);
            Assert.That(ivan.KillsOfMobs, Is.EqualTo(1), "mob kill " + dump);
            Assert.That(ivan.DamageDealt, Is.EqualTo(30f));
            Assert.That(ivan.DamageTaken, Is.GreaterThanOrEqualTo(920f), "what was dealt, after modifiers");
            Assert.That(ivan.SpeechLines, Is.EqualTo(1), "speech " + dump);
            Assert.That(ivan.LastWords, Is.EqualTo("stay back"));

            Assert.That(boris.KillsOfPlayers, Is.EqualTo(1), "player kill " + dump);
            Assert.That(boris.Errors, Is.EqualTo(0));
            Assert.That(boris.LastEvent, Is.EqualTo("killed Ivan Petrov"));
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task PrayersAtAnAltarReachHerAndOthersDoNot()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var entMan = server.EntMan;
        var observer = entMan.System<GodObserverSystem>();
        var ledger = entMan.System<GodLedgerSystem>().Ledger;

        await server.WaitPost(() =>
        {
            var coords = entMan.GetComponent<TransformComponent>(world.Ivan).Coordinates;
            var altar = entMan.SpawnEntity("AltarHeaven", coords);
            var crowbar = entMan.SpawnEntity("Crowbar", coords);

            entMan.EventBus.RaiseEvent(EventSource.Local, new PrayedEvent(world.Session, altar, "Open the door. I did not mean to leave him."));
            entMan.EventBus.RaiseEvent(EventSource.Local, new PrayedEvent(world.Session, crowbar, "this is not an altar"));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(observer.Buffer.Prayers, Has.Count.EqualTo(1));
            var prayer = observer.Buffer.Prayers[0];
            Assert.That(prayer.Text, Is.EqualTo("Open the door. I did not mean to leave him."));
            Assert.That(prayer.Name, Is.EqualTo("Ivan Petrov"));
            Assert.That(prayer.Subject, Is.EqualTo(ledger.ByName("Ivan Petrov")!.Number));
            Assert.That(ledger.ByName("Ivan Petrov")!.Prayers, Is.EqualTo(1));
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task MentionsAreKeptRawAndOrdinarySpeechIsOnlyContext()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var entMan = server.EntMan;
        var observer = entMan.System<GodObserverSystem>();

        // Boris has no connection, so his speech is not heard by the observer (it listens to connected players)
        await server.WaitPost(() =>
        {
            Say(server, world.Ivan, "God, are you listening?");
            Say(server, world.Ivan, "Hand me the rifle");
            Say(server, world.Ivan, "I think she is watching us");
            Say(server, world.Ivan, "I took her bag");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(observer.Buffer.Mentions.Select(m => m.Text), Is.EqualTo(new[] { "God, are you listening?", "I think she is watching us" }));
            Assert.That(observer.Buffer.Speech.Select(m => m.Text), Is.EqualTo(new[] { "Hand me the rifle", "I took her bag" }));
        });

        // Nothing is kept while she is switched off
        await server.WaitPost(() =>
        {
            observer.Clear();
            server.CfgMan.SetCVar(ErrorgateCVars.GodEnabled, false);
            Say(server, world.Ivan, "God, are you there?");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(observer.Buffer.Mentions, Is.Empty);
            Assert.That(observer.Buffer.Speech, Is.Empty);
        });

        await world.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThePreviewDigestShowsTheRoundWithoutEmptyingTheBuffer()
    {
        var world = await Setup();
        var server = world.Pair.Server;
        var entMan = server.EntMan;
        var digest = entMan.System<GodDigestSystem>();
        var observer = entMan.System<GodObserverSystem>();

        await server.WaitPost(() =>
        {
            var coords = entMan.GetComponent<TransformComponent>(world.Ivan).Coordinates;
            var altar = entMan.SpawnEntity("AltarHeaven", coords);
            entMan.EventBus.RaiseEvent(EventSource.Local, new PrayedEvent(world.Session, altar, "Please."));
            Hurt(server, world.Ivan, world.Boris, 20);
        });

        await server.WaitAssertion(() =>
        {
            var first = digest.Preview();
            Assert.That(first.Empty, Is.False);
            Assert.That(first.Text, Does.Contain("[ROUND]"));
            Assert.That(first.Text, Does.Contain("PRAYER S"));
            Assert.That(first.Text, Does.Contain("IVAN PETROV: \"Please.\""));
            Assert.That(first.Text, Does.Match(@"S\d IVAN PETROV \| alive"));
            Assert.That(first.Text, Does.Contain("was hurt by S"), "Event texts carry subject numbers.");

            var second = digest.Preview();
            Assert.That(second.Text, Is.EqualTo(first.Text), "A preview leaves the buffer as it was.");
            Assert.That(observer.Buffer.Prayers, Has.Count.EqualTo(1));

            var taken = digest.Take();
            Assert.That(taken.Text, Is.EqualTo(first.Text));
            Assert.That(observer.Buffer.Prayers, Is.Empty, "Taking empties it.");
            Assert.That(digest.Take().Empty, Is.True);
        });

        await world.Pair.CleanReturnAsync();
    }
}
