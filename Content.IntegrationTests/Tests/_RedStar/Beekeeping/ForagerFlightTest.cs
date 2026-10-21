#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._RedStar.Beekeeping;
using Content.Shared._RedStar.Beekeeping;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._RedStar.Beekeeping;

/// <summary>
/// A visible forager flies its legs without the server moving it in between, and comes home.
/// </summary>
[TestOf(typeof(BeeForagerSystem))]
public sealed class ForagerFlightTest : GameTest
{
    [Test]
    public async Task ForagerFliesLegsAndLands()
    {
        var server = Server;
        var map = await Pair.CreateTestMap();
        EntityUid hive = default;
        EntityUid bee = default;

        await server.WaitPost(() =>
        {
            var mapSystem = server.System<SharedMapSystem>();
            for (var x = 1; x <= 4; x++)
            {
                mapSystem.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            }

            hive = server.EntMan.SpawnEntity("Beehive", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var tray = server.EntMan.SpawnEntity("HydroponicsTrayEmpty", new EntityCoordinates(map.Grid, 4.5f, 0.5f));
            var hiveEnt = new Entity<BeehiveComponent>(hive, server.EntMan.GetComponent<BeehiveComponent>(hive));
            Assert.That(server.System<BeeForagerSystem>().TrySpawnForager(hiveEnt, server.EntMan.GetComponent<TransformComponent>(hive), tray));
            bee = hiveEnt.Comp.Foragers[0];
        });

        // Past growing out of the hive: on the way to the tray, and still where the leg started.
        await RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            var flight = server.EntMan.GetComponent<BeeFlightComponent>(bee);
            var forager = server.EntMan.GetComponent<BeeForagerComponent>(bee);
            Assert.That(forager.Phase, Is.EqualTo(BeeForagerPhase.Outbound));
            Assert.That(flight.Motion, Is.EqualTo(BeeFlightMotion.Line));
            var position = server.EntMan.GetComponent<TransformComponent>(bee).LocalPosition;
            Assert.That(Vector2.Distance(position, flight.From), Is.LessThan(0.01f));
        });

        // There and back with the hover and the dive takes a few seconds; then the bee is gone into the hive.
        await RunSeconds(15);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(bee), "The forager did not come home.");
            Assert.That(server.EntMan.GetComponent<BeehiveComponent>(hive).Bees, Is.EqualTo(0), "A forager only shows a visit.");
        });
    }
}
