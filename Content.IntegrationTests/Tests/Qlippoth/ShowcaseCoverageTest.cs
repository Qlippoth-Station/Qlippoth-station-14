#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Qlippoth;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;

namespace Content.IntegrationTests.Tests.Qlippoth;

/// <summary>
/// Every QlippothResult must be used on ResultShowcaseIdol and every QlippothInitiation on InitiationShowcaseIdol
/// (Resources/Prototypes/Entities/Qlippoths/qlippoths.yml). Those two idols are the in-game testers for the whole
/// action system; a result or initiation that is not on them cannot be tested by hand and silently rots.
/// When this test fails, add the missing class to the matching idol, with an "Expect:" / "trigger:" note.
/// </summary>
[TestFixture]
public sealed class ShowcaseCoverageTest : GameTest
{
    private const string ResultIdol = "ResultShowcaseIdol";
    private const string InitiationIdol = "InitiationShowcaseIdol";

    [Test]
    public async Task EveryResultAndInitiationIsOnAShowcaseIdol()
    {
        var server = Pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var compFactory = server.ResolveDependency<IComponentFactory>();
        var reflection = server.ResolveDependency<IReflectionManager>();

        await server.WaitAssertion(() =>
        {
            var resultTypes = reflection.GetAllChildren(typeof(QlippothResult)).Where(t => !t.IsAbstract).ToList();
            var initiationTypes = reflection.GetAllChildren(typeof(QlippothInitiation)).Where(t => !t.IsAbstract).ToList();
            Assert.That(resultTypes, Is.Not.Empty, "no QlippothResult classes found; reflection is broken");
            Assert.That(initiationTypes, Is.Not.Empty, "no QlippothInitiation classes found; reflection is broken");

            var usedResults = new HashSet<Type>();
            var usedInitiations = new HashSet<Type>();
            foreach (var action in ActionsOf(ResultIdol))
                Collect(action.Results, usedResults, usedInitiations);
            foreach (var action in ActionsOf(InitiationIdol))
            {
                usedInitiations.Add(action.Initiation.GetType());
                Collect(action.Results, usedResults, usedInitiations);
            }

            var missingResults = resultTypes.Where(t => !usedResults.Contains(t)).Select(t => t.Name).OrderBy(n => n).ToList();
            var missingInitiations = initiationTypes.Where(t => !usedInitiations.Contains(t)).Select(t => t.Name).OrderBy(n => n).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(missingResults, Is.Empty,
                    $"These results are not used anywhere on {ResultIdol} (qlippoths.yml). Add each one to a verb there with an \"Expect:\" note: {string.Join(", ", missingResults)}");
                Assert.That(missingInitiations, Is.Empty,
                    $"These initiations are not used on {InitiationIdol} (qlippoths.yml). Add one action per initiation there with a DebugAnnounceResult and a \"trigger:\" note: {string.Join(", ", missingInitiations)}");
            });
        });

        List<QlippothAction> ActionsOf(string prototypeId)
        {
            Assert.That(protoMan.TryIndex<EntityPrototype>(prototypeId, out var proto), Is.True, $"prototype {prototypeId} is missing");
            Assert.That(proto!.TryGetComponent<QlippothActionsComponent>(out var actions, compFactory), Is.True, $"{prototypeId} has no QlippothActions component");
            return actions!.Actions;
        }
    }

    /// <summary>
    /// Walk a result list recursively: Group / ForEachTarget / Repeat hold sub-lists, Random holds weighted options,
    /// and any future container result is picked up through its public properties.
    /// </summary>
    private static void Collect(object? node, HashSet<Type> results, HashSet<Type> initiations)
    {
        switch (node)
        {
            case null or string:
                return;
            case QlippothResult result:
                results.Add(result.GetType());
                foreach (var property in result.GetType().GetProperties())
                {
                    if (property.GetIndexParameters().Length > 0)
                        continue;
                    var value = property.GetValue(result);
                    if (value is QlippothResult or QlippothWeightedResult or IEnumerable and not string)
                        Collect(value, results, initiations);
                }
                return;
            case QlippothWeightedResult weighted:
                Collect(weighted.Result, results, initiations);
                return;
            case QlippothInitiation initiation:
                initiations.Add(initiation.GetType());
                return;
            case IEnumerable list:
                foreach (var item in list)
                    Collect(item, results, initiations);
                return;
        }
    }
}
