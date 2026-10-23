#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._RedStar.Beekeeping.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared._RedStar.Beekeeping.Components;
using Content.Shared._RedStar.Beekeeping.Data;
using Content.Shared._RedStar.Beekeeping.Prototypes;
using Content.Shared._RedStar.Beekeeping.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._RedStar.Beekeeping;

/// <summary>
/// A queen's traits stay hidden until the colony shows them or she has worked in a hive a while, the analyzer shows
/// only what is known, and the new traits add up as written.
/// </summary>
[TestOf(typeof(BeeQueenSystem))]
public sealed class TraitDiscoveryTest : GameTest
{
    [Test]
    public async Task RevealAndObserve()
    {
        var server = Server;
        var map = await Pair.CreateTestMap();
        var slots = server.System<ItemSlotsSystem>();
        var queens = server.System<BeeQueenSystem>();
        var analyzer = server.System<ApiaryAnalyzerSystem>();

        await server.WaitAssertion(() =>
        {
            var hive = server.EntMan.SpawnEntity("Beehive", map.GridCoords);
            var hiveComp = server.EntMan.GetComponent<BeehiveComponent>(hive);
            var queen = server.EntMan.SpawnEntity("BeeQueen", map.GridCoords);
            var comp = server.EntMan.GetComponent<BeeQueenComponent>(queen);
            comp.Traits.Clear();
            comp.Traits.AddRange(new ProtoId<BeeTraitPrototype>[] { "Sting", "Productive" });
            comp.Known.Clear();

            var hidden = analyzer.BuildState(queen);
            Assert.Multiple(() =>
            {
                Assert.That(hidden!.Traits, Is.Empty);
                Assert.That(hidden.HiddenTraits, Is.EqualTo(2));
            });

            // Something else happening shows nothing; a sting shows the sting only.
            queens.Reveal((queen, comp), BeeTraitReveal.Egg);
            Assert.That(comp.Known, Is.Empty);
            queens.Reveal((queen, comp), BeeTraitReveal.Sting);
            Assert.That(comp.Known, Is.EquivalentTo(new[] { "Sting" }));

            var half = analyzer.BuildState(queen);
            Assert.Multiple(() =>
            {
                Assert.That(half!.Traits, Is.EquivalentTo(new[] { "Sting" }));
                Assert.That(half.HiddenTraits, Is.EqualTo(1));
            });

            // Working in a hive long enough shows a trait that shows in nothing else.
            Assert.That(slots.TryInsert(hive, hiveComp.QueenSlot, queen, null));
            queens.UpdateObservation((hive, hiveComp), (float) comp.ObserveTime.TotalSeconds / 2f);
            Assert.That(comp.Known, Has.Count.EqualTo(1));
            queens.UpdateObservation((hive, hiveComp), (float) comp.ObserveTime.TotalSeconds / 2f + 1f);
            Assert.That(comp.Known, Is.EquivalentTo(new[] { "Sting", "Productive" }));
        });
    }

    /// <summary>
    /// A daughter knows only what was known of the traits she actually got.
    /// </summary>
    [Test]
    public async Task KnownOfActiveTraits()
    {
        await Server.WaitAssertion(() =>
        {
            var traits = new ProtoId<BeeTraitPrototype>[] { "Sting", "Productive" };
            var known = new ProtoId<BeeTraitPrototype>[] { "Productive", "Lazy" };
            Assert.That(SharedBeehiveSystem.KnownOf(traits, known), Is.EquivalentTo(new[] { "Productive" }));
        });
    }

    [Test]
    public async Task NewTraitTotals()
    {
        var hive = Server.System<BeehiveSystem>();
        await Server.WaitAssertion(() =>
        {
            BeeTraitTotals Of(string id) => hive.GetTotals(new ProtoId<BeeTraitPrototype>[] { id });

            Assert.Multiple(() =>
            {
                Assert.That(Of("Bountiful").FlightPollination, Is.GreaterThan(0f));
                Assert.That(Of("FrostHardy").ColdTolerance, Is.EqualTo(30f));
                Assert.That(Of("HeatHardy").HeatTolerance, Is.EqualTo(30f));
                Assert.That(Of("Calm").AngryPerHit, Is.EqualTo(-1));
                Assert.That(Of("Calm").AngryDamageStep, Is.EqualTo(2f));
                Assert.That(Of("Waxy").CapWax, Is.EqualTo(2));
                Assert.That(Of("Honeyed").CombYield, Is.EqualTo(1.5f));
                Assert.That(Of("Restless").EggWait, Is.EqualTo(0.5f));
                Assert.That(Of("ShortLived").Lifetime, Is.EqualTo(0.5f));
                Assert.That(Of("Skittish").ColdTolerance, Is.LessThan(0f));
                Assert.That(Of("Skittish").PressureTolerance, Is.LessThan(0f));
            });
        });
    }
}
