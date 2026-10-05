using System.Linq;
using Content.Server.Qlippoth;
using Content.Server.Qlippoth.Components;
using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// Owns per-specimen research generation, experiment validation, and Q-Gear claiming.
/// The console only operates on the Qlippoth currently linked to its chamber; the graph and all
/// durable progress live on that Qlippoth so moving or replacing a specimen cannot transfer data.
/// </summary>
public sealed partial class QlippothResearchConsoleSystem : EntitySystem
{
    private const float ExperimentCooldown = 12f;
    private const int RandomDiscoveryCount = 4;

    // These are the shared possible experiments. Each generated specimen graph chooses a different
    // subset/order and topology; Qlippoth-authored checkpoints are added on top of this common pool.
    private static readonly ResearchNodeTemplate[] CommonDiscoveries =
    {
        new("qlippoth-research-node-observation", "qlippoth-research-node-observation-desc",
            new() { QlippothResearchActivity.Observation }),
        new("qlippoth-research-node-resonance", "qlippoth-research-node-resonance-desc",
            new() { QlippothResearchActivity.ResonanceScan }),
        new("qlippoth-research-node-controlled-test", "qlippoth-research-node-controlled-test-desc",
            new() { QlippothResearchActivity.ControlledTest }),
        new("qlippoth-research-node-diagnostics", "qlippoth-research-node-diagnostics-desc",
            new() { QlippothResearchActivity.ContainmentDiagnostics }),
        new("qlippoth-research-node-response", "qlippoth-research-node-response-desc",
            new() { QlippothResearchActivity.Observation, QlippothResearchActivity.ControlledTest }),
        new("qlippoth-research-node-signal", "qlippoth-research-node-signal-desc",
            new() { QlippothResearchActivity.ResonanceScan, QlippothResearchActivity.ContainmentDiagnostics }),
    };

    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private QlippothActionInitiationSystem _initiation = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    private TimeSpan _nextUiRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QlippothResearchConsoleComponent, QlippothResearchActivityMessage>(OnActivityRequested);
        SubscribeLocalEvent<QlippothResearchConsoleComponent, QlippothResearchGearMessage>(OnGearRequested);
        SubscribeLocalEvent<QlippothResearchConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUiRefresh)
            return;

        _nextUiRefresh = _timing.CurTime + TimeSpan.FromSeconds(1);
        var consoles = EntityQueryEnumerator<QlippothResearchConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var console))
            UpdateUiState(uid, console);
    }

    /// <summary>
    /// Called only by an action result carrying the current experiment arguments. Action prototypes
    /// can add bonus progress, but they cannot choose a different specimen or bypass graph rules.
    /// </summary>
    public bool AdvanceFromAction(object? eventArgs, int amount)
    {
        if (eventArgs is not QlippothResearchExperimentEventArgs experiment || amount <= 0 ||
            !TryComp<QlippothResearchProfileComponent>(experiment.Target, out var profile) ||
            !profile.GraphGenerated)
            return false;

        var node = profile.Nodes.FirstOrDefault(candidate => candidate.Id == experiment.NodeId);
        if (node == null || node.Unlocked || node.IsGear || !node.Activities.Contains(experiment.Activity) ||
            !IsAvailable(profile, node))
            return false;

        AddProgress(profile, node, amount);
        return true;
    }

    private void OnActivityRequested(
        EntityUid uid,
        QlippothResearchConsoleComponent console,
        QlippothResearchActivityMessage message)
    {
        if (!TryGetContainedQlippoth(console, out var chamber, out var specimen, out var qlippoth))
            return;

        var profile = EnsureComp<QlippothResearchProfileComponent>(specimen);
        EnsureGraph(profile, qlippoth);
        if (_timing.CurTime < profile.NextActivityAt)
        {
            UpdateUiState(uid, console);
            return;
        }

        var node = profile.Nodes.FirstOrDefault(candidate => candidate.Id == message.NodeId);
        if (node == null || node.IsGear || node.Unlocked || !IsAvailable(profile, node) ||
            !node.Activities.Contains(message.Activity))
        {
            UpdateUiState(uid, console);
            return;
        }

        profile.NextActivityAt = _timing.CurTime + TimeSpan.FromSeconds(ExperimentCooldown);
        var experiment = new QlippothResearchExperimentEventArgs(
            specimen,
            message.Actor,
            chamber,
            node.Id,
            message.Activity);

        // Initiation actions can perform specimen-specific reactions and their results may award
        // bonus progress. The normal experiment reward below remains independent of those actions.
        _initiation.Dispatch<OnResearchExperimentInitiation>(specimen, experiment);
        AddProgress(profile, node, 1);
        UpdateUiState(uid, console);
    }

    private void OnGearRequested(
        EntityUid uid,
        QlippothResearchConsoleComponent console,
        QlippothResearchGearMessage message)
    {
        if (!TryGetContainedQlippoth(console, out _, out var specimen, out var qlippoth))
            return;

        var profile = EnsureComp<QlippothResearchProfileComponent>(specimen);
        EnsureGraph(profile, qlippoth);
        var gearNode = profile.Nodes.FirstOrDefault(node => node.Id == message.NodeId && node.IsGear);
        if (profile.GearIssued || profile.GearPrototype is not { } gearPrototype ||
            gearNode == null || !IsAvailable(profile, gearNode))
        {
            UpdateUiState(uid, console);
            return;
        }

        // Spawn before marking the entitlement claimed: a failed prototype spawn must not consume
        // the specimen's one-time reward. These message handlers run synchronously on the server.
        // Gear identity is bound to the specimen and never enters station-wide R&D.
        Spawn(gearPrototype, Transform(uid).Coordinates.Offset(_random.NextVector2(-0.25f, 0.25f)));
        profile.GearIssued = true;
        gearNode.Unlocked = true;
        UpdateUiState(uid, console);
    }

    private bool TryGetContainedQlippoth(
        QlippothResearchConsoleComponent console,
        out EntityUid chamberUid,
        out EntityUid specimenUid,
        out QlippothComponent qlippoth)
    {
        chamberUid = default;
        specimenUid = default;
        qlippoth = default!;

        if (console.LinkedChamber is not { } linked ||
            !TryComp<ContainmentChamberComponent>(linked, out var chamber) ||
            !chamber.IsOccupied ||
            chamber.ContainedQlippoth is not { } contained ||
            !TryComp<QlippothComponent>(contained, out var foundQlippoth) ||
            foundQlippoth == null)
            return false;

        qlippoth = foundQlippoth;
        chamberUid = linked;
        specimenUid = contained;
        return true;
    }

    private void EnsureGraph(QlippothResearchProfileComponent profile, QlippothComponent qlippoth)
    {
        if (profile.GraphGenerated)
            return;

        profile.Nodes.Clear();
        profile.GearPrototype = qlippoth.ResearchGearPrototype;

        // A random subset and shuffled order provide specimen-specific discoveries, while a
        // per-node dependency choice makes the generated graph structure itself differ as well.
        var discoveries = CommonDiscoveries.ToList();
        _random.Shuffle(discoveries);
        discoveries = discoveries.Take(RandomDiscoveryCount).ToList();

        var priorIds = new List<string>();
        for (var i = 0; i < discoveries.Count; i++)
        {
            var template = discoveries[i];
            var node = new QlippothResearchNodeProgress
            {
                Id = $"discovery-{i}",
                Name = template.Name,
                Description = template.Description,
                RequiredProgress = _random.Pick(new[] { 1, 2 }),
                Activities = new List<QlippothResearchActivity>(template.Activities),
            };

            if (priorIds.Count > 0)
                node.Prerequisites.Add(_random.Pick(priorIds));

            profile.Nodes.Add(node);
            priorIds.Add(node.Id);
        }

        foreach (var checkpoint in qlippoth.ResearchCheckpoints)
        {
            if (string.IsNullOrWhiteSpace(checkpoint.Id) || string.IsNullOrWhiteSpace(checkpoint.Name))
                continue;

            var node = new QlippothResearchNodeProgress
            {
                Id = $"checkpoint-{checkpoint.Id}",
                Name = checkpoint.Name,
                Description = checkpoint.Description,
                RequiredProgress = 1,
                Activities = checkpoint.Activities.Count == 0
                    ? new List<QlippothResearchActivity> { QlippothResearchActivity.Observation }
                    : new List<QlippothResearchActivity>(checkpoint.Activities),
                IsCheckpoint = true,
            };

            // A checkpoint always appears, but its place among the common discoveries is
            // specimen-specific. An empty list means it is an alternate root of this graph.
            if (priorIds.Count > 0)
                node.Prerequisites.Add(_random.Pick(priorIds));

            profile.Nodes.Add(node);
            priorIds.Add(node.Id);
        }

        // The gear node is the terminal checkpoint. Requiring every preceding node makes the
        // species-specific reward the culmination of that individual's graph, not a shared recipe.
        if (profile.GearPrototype != null)
        {
            profile.Nodes.Add(new QlippothResearchNodeProgress
            {
                Id = "qlippoth-gear",
                Name = "qlippoth-research-node-gear",
                Description = "qlippoth-research-node-gear-desc",
                IsGear = true,
                Prerequisites = new List<string>(priorIds),
            });
        }

        profile.GraphGenerated = profile.Nodes.Count > 0;
    }

    private static bool IsAvailable(QlippothResearchProfileComponent profile, QlippothResearchNodeProgress node)
    {
        return node.Prerequisites.All(required =>
            profile.Nodes.Any(candidate => candidate.Id == required && candidate.Unlocked));
    }

    private static void AddProgress(
        QlippothResearchProfileComponent profile,
        QlippothResearchNodeProgress node,
        int amount)
    {
        node.Progress = Math.Min(node.RequiredProgress, node.Progress + amount);
        if (node.Progress >= node.RequiredProgress)
            node.Unlocked = true;
    }

    private void OnUiOpened(EntityUid uid, QlippothResearchConsoleComponent console, BoundUIOpenedEvent args)
    {
        UpdateUiState(uid, console);
    }

    private void UpdateUiState(EntityUid uid, QlippothResearchConsoleComponent console)
    {
        var chamberOccupied = TryGetContainedQlippoth(console, out _, out var specimen, out var qlippoth);
        string? qlippothName = chamberOccupied ? MetaData(specimen).EntityName : null;
        var nodes = new List<QlippothResearchNodeState>();
        var cooldown = 0f;

        if (chamberOccupied)
        {
            var profile = EnsureComp<QlippothResearchProfileComponent>(specimen);
            EnsureGraph(profile, qlippoth);
            cooldown = Math.Max(0f, (float)(profile.NextActivityAt - _timing.CurTime).TotalSeconds);

            foreach (var node in profile.Nodes)
            {
                nodes.Add(new QlippothResearchNodeState(
                    node.Id,
                    node.Name,
                    node.Description,
                    node.Progress,
                    node.RequiredProgress,
                    node.Unlocked,
                    !node.Unlocked && IsAvailable(profile, node),
                    node.IsCheckpoint,
                    node.IsGear,
                    new List<QlippothResearchActivity>(node.Activities),
                    node.IsGear ? profile.GearPrototype?.ToString() : null,
                    profile.GearIssued));
            }
        }

        _ui.SetUiState(uid, QlippothResearchConsoleUiKey.Key,
            new QlippothResearchConsoleBuiState(
                console.LinkedChamber != null,
                chamberOccupied,
                qlippothName,
                cooldown,
                nodes));
    }

    private sealed record ResearchNodeTemplate(
        string Name,
        string Description,
        List<QlippothResearchActivity> Activities);
}
