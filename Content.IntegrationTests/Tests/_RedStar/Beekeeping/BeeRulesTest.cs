#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._RedStar.Beekeeping.Components;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._RedStar.Beekeeping;

/// <summary>
/// The colony's numbers from the design: how fast the queen lays, when the queen egg comes, what a comb holds.
/// </summary>
[TestOf(typeof(BeehiveComponent))]
public sealed class BeeRulesTest : GameTest
{
    private EntityUid _hive;

    /// <summary>
    /// A breathable test map with a hive, two frames and a queen without traits.
    /// </summary>
    private async Task Build()
    {
        var server = Server;
        var map = await Pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);

            var slots = server.System<ItemSlotsSystem>();
            _hive = server.EntMan.SpawnEntity("Beehive", map.GridCoords);
            var comp = server.EntMan.GetComponent<BeehiveComponent>(_hive);
            for (var i = 0; i < 2; i++)
                Assert.That(slots.TryInsert(_hive, comp.FrameSlots[i], server.EntMan.SpawnEntity("BeeFrame", map.GridCoords), null));

            var queen = server.EntMan.SpawnEntity("BeeQueen", map.GridCoords);
            server.EntMan.GetComponent<BeeQueenComponent>(queen).Traits.Clear();
            Assert.That(slots.TryInsert(_hive, comp.QueenSlot, queen, null));
        });
        await RunTicksSync(5);
    }

    /// <summary>
    /// Keeps the hive fed and the scout at home, so only laying changes the colony.
    /// </summary>
    private async Task<int> Colony()
    {
        var count = 0;
        await Server.WaitPost(() =>
        {
            var comp = Server.EntMan.GetComponent<BeehiveComponent>(_hive);
            comp.Nectar = comp.MaxNectar;
            comp.NextScoutTrip = TimeSpan.MaxValue;
            count = comp.Bees + comp.OutBees;
        });
        return count;
    }

    /// <summary>
    /// Правило: матка делает новую пчелу не чаще раза в минуту, даже когда нектара вдоволь.
    /// </summary>
    [Test]
    public async Task QueenLaysOncePerMinute()
    {
        await Build();
        await Server.WaitPost(() =>
        {
            var comp = Server.EntMan.GetComponent<BeehiveComponent>(_hive);
            Assert.That(comp.LayInterval, Is.EqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(comp.MaxBees, Is.GreaterThan(3));
            comp.Bees = 1;
        });

        var start = await Colony();
        await RunSeconds(5);
        var first = await Colony();
        Assert.That(first, Is.EqualTo(start + 1), "the first bee comes as soon as there is nectar");

        // Plenty of nectar for the whole minute, and still no second bee before it ends.
        for (var i = 0; i < 5; i++)
        {
            await RunSeconds(10);
            Assert.That(await Colony(), Is.EqualTo(first), $"a second bee at {5 + (i + 1) * 10} s");
        }

        await RunSeconds(10);
        Assert.That(await Colony(), Is.EqualTo(first + 1), "the second bee a minute after the first");
    }

    /// <summary>
    /// Правило: яйцо матки появляется, когда колония полная уже 3 минуты, и не раньше.
    /// </summary>
    [Test]
    public async Task QueenEggAfterThreeFullMinutes()
    {
        await Build();
        await Server.WaitPost(() =>
        {
            var comp = Server.EntMan.GetComponent<BeehiveComponent>(_hive);
            Assert.That(comp.EggFullTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
            comp.Bees = comp.MaxBees;
            comp.EggCooldownUntil = TimeSpan.Zero;
        });

        await Colony();
        await RunSeconds(170);
        await Server.WaitAssertion(() =>
            Assert.That(Server.EntMan.GetComponent<BeehiveComponent>(_hive).HasEgg, Is.False, "egg before 3 minutes"));

        await RunSeconds(15);
        await Server.WaitAssertion(() =>
            Assert.That(Server.EntMan.GetComponent<BeehiveComponent>(_hive).HasEgg, Is.True, "no egg after 3 minutes"));
    }

    /// <summary>
    /// Правило: одна сота даёт 5 ед. мёда и 2 ед. витаминов.
    /// </summary>
    [Test]
    public async Task CombHoldsHoneyAndVitamins()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var comb = Server.EntMan.SpawnEntity("FoodHoneycomb", map.GridCoords);
            Assert.That(Server.System<SharedSolutionContainerSystem>()
                .TryGetSolution(comb, "food", out _, out var solution));
            Assert.Multiple(() =>
            {
                Assert.That(solution!.GetTotalPrototypeQuantity("Honey").Float(), Is.EqualTo(5f));
                Assert.That(solution.GetTotalPrototypeQuantity("Vitamin").Float(), Is.EqualTo(2f));
            });
        });
    }
}
