#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._RedStar.Beekeeping;
using Content.Shared._RedStar.Beekeeping;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Inventory;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._RedStar.Beekeeping;

/// <summary>
/// A hit lets angry bees out of the colony (one per hit plus one per damage step, the scout first), a killed bee is
/// lost to the colony, and a broken hive lets everyone out with the queen defending it.
/// </summary>
[TestOf(typeof(BeehiveComponent))]
public sealed class AngryBeehiveTest : GameTest
{
    [Test]
    public async Task HitsAndBreaking()
    {
        var server = Server;
        var map = await Pair.CreateTestMap();
        var slots = server.System<ItemSlotsSystem>();
        var damageable = server.System<DamageableSystem>();
        EntityUid hive = default;

        await server.WaitPost(() =>
        {
            hive = server.EntMan.SpawnEntity("Beehive", map.GridCoords);
            var comp = server.EntMan.GetComponent<BeehiveComponent>(hive);
            Assert.That(slots.TryInsert(hive, comp.FrameSlots[0], server.EntMan.SpawnEntity("BeeFrame", map.GridCoords), null));
            Assert.That(slots.TryInsert(hive, comp.QueenSlot, server.EntMan.SpawnEntity("BeeQueen", map.GridCoords), null));
            comp.Bees = 5;

            // 25 damage: one bee for the hit and two for two full steps of 10.
            damageable.TryChangeDamage(hive, new DamageSpecifier { DamageDict = { ["Blunt"] = 25 } });

            Assert.Multiple(() =>
            {
                Assert.That(comp.AngryOut, Is.EqualTo(3));
                Assert.That(comp.Bees, Is.EqualTo(2));
                Assert.That(comp.AngryScout, Is.Not.Null);
                Assert.That(comp.ScoutState, Is.EqualTo(BeeScoutState.Outside));
            });
        });
        await RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var comp = server.EntMan.GetComponent<BeehiveComponent>(hive);
            EntityUid? worker = null;
            var bees = server.EntMan.EntityQueryEnumerator<BeehiveAngryBeeComponent>();
            while (worker == null && bees.MoveNext(out var uid, out var angry))
            {
                if (!angry.Scout)
                    worker = uid;
            }

            server.EntMan.DeleteEntity(worker);

            Assert.Multiple(() =>
            {
                Assert.That(comp.AngryOut, Is.EqualTo(2));
                Assert.That(comp.OutBees, Is.EqualTo(2));
                Assert.That(comp.Bees, Is.EqualTo(2));
            });

            damageable.TryChangeDamage(hive, new DamageSpecifier { DamageDict = { ["Blunt"] = 200 } });
        });
        await RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(hive));
            Assert.That(server.EntMan.EntityQuery<BeehiveAngryQueenComponent>().Count(), Is.EqualTo(1));
            // The queen item rides inside the angry queen; it was not lost with the hive.
            Assert.That(server.EntMan.EntityQuery<BeeQueenComponent>().Count(), Is.EqualTo(1));
            // The two bees left at home flew out too.
            Assert.That(server.EntMan.EntityQuery<BeehiveAngryBeeComponent>().Count(), Is.EqualTo(4));
        });
    }

    /// <summary>
    /// Angry bees do not go for a person in a sealed suit and helmet, and go for them again once the helmet is off.
    /// </summary>
    [Test]
    public async Task SealedPeopleAreLeftAlone()
    {
        var server = Server;
        var map = await Pair.CreateTestMap();
        var inventory = server.System<InventorySystem>();
        var faction = server.System<NpcFactionSystem>();
        EntityUid human = default;
        EntityUid bee = default;

        await server.WaitAssertion(() =>
        {
            human = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            bee = server.EntMan.SpawnEntity("MobBeehiveAngryBee", map.GridCoords);
            faction.AggroEntity(bee, human);
            var suit = server.EntMan.SpawnEntity("ClothingOuterSuitEmergency", map.GridCoords);
            var helmet = server.EntMan.SpawnEntity("ClothingHeadHelmetEVA", map.GridCoords);
            Assert.That(inventory.TryEquip(human, suit, "outerClothing", force: true));
            Assert.That(inventory.TryEquip(human, helmet, "head", force: true));
        });

        await RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(faction.IsIgnored(bee, human), "a sealed person is ignored");
            Assert.That(faction.GetHostiles(bee), Does.Not.Contain(human));
            Assert.That(inventory.TryUnequip(human, "head", out _, force: true));
        });

        await RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(faction.IsIgnored(bee, human), Is.False, "no longer ignored without the helmet");
        });
    }
}
