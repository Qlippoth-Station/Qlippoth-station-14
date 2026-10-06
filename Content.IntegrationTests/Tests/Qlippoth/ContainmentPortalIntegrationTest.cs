using Content.IntegrationTests.Fixtures;
using Content.Server.Qlippoth.Systems;
using Content.Shared.Interaction;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Qlippoth;

[TestFixture]
[TestOf(typeof(ContainmentPortalSystem))]
public sealed class ContainmentPortalIntegrationTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: QlippothPortalTestTraveler
        """;

    [Test]
    public async Task CivilianPortalReturnsTravelerToOriginAndRejectsOtherDepartments()
    {
        var pair = Pair;
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var systems = server.ResolveDependency<IEntitySystemManager>();
            var containment = systems.GetEntitySystem<ContainmentDimensionSystem>();
            var transformSystem = systems.GetEntitySystem<SharedTransformSystem>();
            var traveler = entMan.SpawnEntity("QlippothPortalTestTraveler", testMap.GridCoords);
            var entrance = entMan.SpawnEntity("ContainmentPortalCivilian", testMap.GridCoords);
            var originalCoordinates = transformSystem.GetMapCoordinates(traveler);

            entMan.EventBus.RaiseLocalEvent(entrance,
                new ActivateInWorldEvent(traveler, entrance, complex: true));

            var travelerTransform = entMan.GetComponent<TransformComponent>(traveler);
            Assert.That(containment.IsContainmentDimension(travelerTransform.MapID), Is.True,
                "The department portal did not move the traveler into the containment dimension.");

            var correctExit = EntityUid.Invalid;
            var wrongExit = EntityUid.Invalid;
            var portals = entMan.EntityQueryEnumerator<ContainmentPortalComponent, TransformComponent>();
            while (portals.MoveNext(out var portalUid, out var portal, out var portalTransform))
            {
                if (!portal.IsExitPortal || portalTransform.MapID != containment.ContainmentMapId)
                    continue;

                if (portal.Department == ContainmentDepartment.Civilian)
                    correctExit = portalUid;
                else
                    wrongExit = portalUid;
            }

            Assert.That(correctExit, Is.Not.EqualTo(EntityUid.Invalid),
                "No Civilian return portal was generated in the containment dimension.");
            Assert.That(wrongExit, Is.Not.EqualTo(EntityUid.Invalid),
                "No other-department return portal was generated in the containment dimension.");

            entMan.EventBus.RaiseLocalEvent(wrongExit,
                new ActivateInWorldEvent(traveler, wrongExit, complex: true));
            Assert.That(containment.IsContainmentDimension(travelerTransform.MapID), Is.True,
                "A return portal for another department incorrectly accepted the traveler.");

            entMan.EventBus.RaiseLocalEvent(correctExit,
                new ActivateInWorldEvent(traveler, correctExit, complex: true));

            var returnedCoordinates = transformSystem.GetMapCoordinates(traveler);
            Assert.Multiple(() =>
            {
                Assert.That(returnedCoordinates.MapId, Is.EqualTo(originalCoordinates.MapId));
                Assert.That(returnedCoordinates.Position, Is.EqualTo(originalCoordinates.Position));
            });
        });
    }
}
