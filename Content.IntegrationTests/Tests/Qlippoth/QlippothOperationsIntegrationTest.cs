using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server.Qlippoth;
using Content.Server.Qlippoth.Components;
using Content.Server.Qlippoth.Systems;
using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Qlippoth;

[TestFixture]
[TestOf(typeof(CorruptionSystem))]
[TestOf(typeof(QlippothMarketSystem))]
public sealed class QlippothOperationsIntegrationTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: QlippothCorruptionTestTarget
          components:
          - type: Sanity

        - type: entity
          id: QlippothResearchTestSubject
        """;

    [Test]
    public async Task CorruptionCanBeAppliedOnceAndExplicitlyTreated()
    {
        var pair = Pair;
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        EntityUid target = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var systems = server.ResolveDependency<IEntitySystemManager>();
            var corruptionSystem = systems.GetEntitySystem<CorruptionSystem>();
            target = entMan.SpawnEntity("QlippothCorruptionTestTarget", testMap.GridCoords);
            var profile = new CorruptionProfile(60f, 3, 0f, 0f, 0f, 0f);

            Assert.That(corruptionSystem.ApplyCorruption(target, null, profile), Is.True);
            Assert.That(corruptionSystem.ApplyCorruption(target, null, profile), Is.False,
                "A target already carrying corruption must not be re-exposed.");
            Assert.That(entMan.TryGetComponent(target, out QlippothCorruptionComponent corruption), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(corruption.Severity, Is.EqualTo(3));
                Assert.That(corruption.RemainingSeconds, Is.EqualTo(60f));
            });
            Assert.That(corruptionSystem.RemoveCorruption(target, QlippothCorruptionRemovalReason.Treated), Is.True);
        });

        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<QlippothCorruptionComponent>(target), Is.False,
                "Treatment did not remove the active corruption state.");
        });
    }

    [Test]
    public async Task MarketRejectsUnavailableChamberWithoutConsumingStock()
    {
        var pair = Pair;
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var market = server.ResolveDependency<IEntitySystemManager>()
                .GetEntitySystem<QlippothMarketSystem>();
            EntProtoId specimen = "MobQlippothPhase1";
            market.AddSecuredQlippothToMarket(specimen, QGatePhase.Phase1Rift);

            var result = market.PurchaseQlippoth(specimen, EntityUid.Invalid, "missing-chamber");

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(QlippothMarketPurchaseResult.ChamberUnavailable));
                Assert.That(market.GetAvailableQlippoths(), Does.Contain(specimen),
                    "A rejected delivery request must leave the specimen in market stock.");
            });
        });
    }

    [Test]
    public async Task ResearchActionProgressIsBoundToItsSpecimenAndNode()
    {
        var pair = Pair;
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var research = server.ResolveDependency<IEntitySystemManager>()
                .GetEntitySystem<QlippothResearchConsoleSystem>();
            var specimen = entMan.SpawnEntity("QlippothResearchTestSubject", testMap.GridCoords);
            var otherSpecimen = entMan.SpawnEntity("QlippothResearchTestSubject", testMap.GridCoords);
            var profile = entMan.EnsureComponent<QlippothResearchProfileComponent>(specimen);
            profile.GraphGenerated = true;
            profile.Nodes.Add(new QlippothResearchNodeProgress
            {
                Id = "observation",
                RequiredProgress = 2,
                Activities = new List<QlippothResearchActivity> { QlippothResearchActivity.Observation },
            });

            var experiment = new QlippothResearchExperimentEventArgs(
                specimen, EntityUid.Invalid, EntityUid.Invalid, "observation", QlippothResearchActivity.Observation);

            Assert.That(research.AdvanceFromAction(experiment, 1), Is.True);
            Assert.That(research.AdvanceFromAction(experiment with { NodeId = "missing" }, 1), Is.False);
            Assert.That(research.AdvanceFromAction(experiment with { Target = otherSpecimen }, 1), Is.False);
            Assert.That(research.AdvanceFromAction(experiment, 0), Is.False);
            Assert.That(profile.Nodes[0].Progress, Is.EqualTo(1));
            Assert.That(entMan.HasComponent<QlippothResearchProfileComponent>(otherSpecimen), Is.False,
                "Research progress must not be copied to a different specimen.");
        });
    }
}
