using System.Linq;
using Content.Server.Qlippoth;
using Content.Server.Qlippoth.Components;
using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Content.Shared.Damage.Systems;
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
    private static readonly TimeSpan EvidenceProgressCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EvidenceRetention = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaxEvidenceAge = TimeSpan.FromSeconds(5);
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
            new() { QlippothResearchActivity.ContainmentDiagnostics },
            new() { QlippothResearchEvidenceType.ChamberDiagnostics }),
        new("qlippoth-research-node-response", "qlippoth-research-node-response-desc",
            new() { QlippothResearchActivity.Observation, QlippothResearchActivity.ControlledTest }),
        new("qlippoth-research-node-signal", "qlippoth-research-node-signal-desc",
            new() { QlippothResearchActivity.ResonanceScan, QlippothResearchActivity.ContainmentDiagnostics }),
    };

    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private QlippothActionInitiationSystem _initiation = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ContainmentDimensionSystem _containment = default!;
    private TimeSpan _nextUiRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QlippothResearchConsoleComponent, QlippothResearchActivityMessage>(OnActivityRequested);
        SubscribeLocalEvent<QlippothResearchConsoleComponent, QlippothResearchGearMessage>(OnGearRequested);
        SubscribeLocalEvent<QlippothResearchConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<QlippothCorruptionAppliedEvent>(OnCorruptionAppliedEvidence);
        SubscribeLocalEvent<QlippothCorruptionPulseEvent>(OnCorruptionPulseEvidence);
        SubscribeLocalEvent<QlippothCorruptionRemovedEvent>(OnCorruptionRemovedEvidence);
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

    public void RecordGateObjectiveEvidence(
        EntityUid specimen,
        EntityUid objectiveUid,
        EntityUid gateUid,
        QGateObjectiveType objectiveType,
        TimeSpan occurredAt)
    {
        if (!IsFreshEvidence(occurredAt) ||
            !Exists(specimen) || !TryComp<QlippothComponent>(specimen, out var qlippoth) ||
            !TryComp<QGateComponent>(gateUid, out var gate) ||
            !qlippoth.GatePhases.Contains(gate.Phase) ||
            !TryComp<QGateDungeonObjectiveComponent>(objectiveUid, out var objective) ||
            !objective.Completed || objective.Gate != gateUid || objective.ObjectiveType != objectiveType)
            return;

        var evidenceType = objectiveType switch
        {
            QGateObjectiveType.StabilizeRift => QlippothResearchEvidenceType.GateStabilized,
            QGateObjectiveType.ExtractAnomalyData => QlippothResearchEvidenceType.GateDataExtracted,
            QGateObjectiveType.SealContainment => QlippothResearchEvidenceType.GateSealed,
            _ => (QlippothResearchEvidenceType?) null,
        };
        if (evidenceType == null)
            return;

        if (objectiveType == QGateObjectiveType.ExtractAnomalyData &&
            ((byte) gate.Phase < (byte) QGatePhase.Phase4Abyss ||
             !qlippoth.GatePhases.Contains(gate.Phase)))
            return;

        RecordEvidence(specimen, qlippoth, evidenceType.Value,
            $"gate-objective:{gateUid}:{objectiveUid}:{objectiveType}", occurredAt,
            objectiveType.ToString(), (byte) gate.Phase >= (byte) QGatePhase.Phase4Abyss);
    }

    public void RecordContainmentEvidence(
        EntityUid chamberUid,
        EntityUid specimen,
        QlippothResearchEvidenceType evidenceType,
        int breachSequence,
        TimeSpan occurredAt)
    {
        if (!IsFreshEvidence(occurredAt) || breachSequence <= 0 ||
            !TryComp<ContainmentChamberComponent>(chamberUid, out var chamber) ||
            !chamber.IsBuilt || !chamber.IsOccupied || chamber.ContainedQlippoth != specimen ||
            !TryComp<TransformComponent>(chamberUid, out var chamberTransform) ||
            !_containment.IsContainmentDimension(chamberTransform.MapID) ||
            !Exists(specimen) || !TryComp<QlippothComponent>(specimen, out var qlippoth))
            return;

        if ((evidenceType == QlippothResearchEvidenceType.ChamberBreached && !chamber.IsBreached) ||
            (evidenceType == QlippothResearchEvidenceType.ChamberRepaired && chamber.IsBreached) ||
            (evidenceType is not QlippothResearchEvidenceType.ChamberBreached and
                not QlippothResearchEvidenceType.ChamberRepaired))
            return;

        var eventId = $"chamber:{chamberUid}:{breachSequence}:{evidenceType}";
        RecordEvidence(specimen, qlippoth, evidenceType, eventId, occurredAt, chamber.ChamberId);
    }

    private void OnCorruptionAppliedEvidence(QlippothCorruptionAppliedEvent args)
    {
        if (args.SourceQlippoth is not { } source ||
            !TryComp<QlippothCorruptionComponent>(args.Target, out var corruption) ||
            corruption.SourceQlippoth != source || corruption.IsRemoving)
            return;

        RecordCorruptionEvidence(source, args.Target,
            args.Spread ? QlippothResearchEvidenceType.CorruptionSpread : QlippothResearchEvidenceType.CorruptionApplied,
            args.EvidenceId, args.OccurredAt, args.Severity);
    }

    private void OnCorruptionPulseEvidence(QlippothCorruptionPulseEvent args)
    {
        if (args.SourceQlippoth is not { } source ||
            !TryComp<QlippothCorruptionComponent>(args.Target, out var corruption) ||
            corruption.SourceQlippoth != source || corruption.IsRemoving)
            return;

        RecordCorruptionEvidence(source, args.Target, QlippothResearchEvidenceType.CorruptionPulse,
            args.EvidenceId, args.OccurredAt, args.Severity);
    }

    private void OnCorruptionRemovedEvidence(QlippothCorruptionRemovedEvent args)
    {
        if (args.Reason != QlippothCorruptionRemovalReason.Treated ||
            args.SourceQlippoth is not { } source ||
            !TryComp<QlippothCorruptionComponent>(args.Target, out var corruption) ||
            corruption.SourceQlippoth != source || !corruption.IsRemoving)
            return;

        RecordCorruptionEvidence(source, args.Target, QlippothResearchEvidenceType.CorruptionTreated,
            args.EvidenceId, args.OccurredAt, args.Severity);
    }

    private void RecordCorruptionEvidence(
        EntityUid specimen,
        EntityUid target,
        QlippothResearchEvidenceType evidenceType,
        long evidenceId,
        TimeSpan occurredAt,
        int severity)
    {
        if (evidenceId <= 0 || !IsFreshEvidence(occurredAt) ||
            !Exists(specimen) || !TryComp<QlippothComponent>(specimen, out var qlippoth))
            return;

        var highTier = qlippoth.GatePhases.Any(phase => (byte) phase >= (byte) QGatePhase.Phase4Abyss);
        RecordEvidence(specimen, qlippoth, evidenceType,
            $"corruption:{specimen}:{target}:{evidenceId}", occurredAt,
            $"target:{target};severity:{severity}", highTier);
    }

    private bool RecordEvidence(
        EntityUid specimen,
        QlippothComponent qlippoth,
        QlippothResearchEvidenceType evidenceType,
        string eventId,
        TimeSpan occurredAt,
        string context,
        bool highTier = false,
        string? preferredNodeId = null)
    {
        var profile = EnsureComp<QlippothResearchProfileComponent>(specimen);
        EnsureGraph(profile, qlippoth);
        if (profile.Evidence.Any(record => record.Id == eventId))
            return false;

        var requiredEvidenceReceipts = new HashSet<string>();
        foreach (var requiredNode in profile.Nodes)
        foreach (var requiredType in requiredNode.RequiredEvidenceTypes)
        {
            var receipt = profile.Evidence
                .Where(record => record.Type == requiredType &&
                                 (!requiredNode.RequiresHighTierEvidence || record.HighTier))
                .OrderByDescending(record => record.RecordedAt)
                .FirstOrDefault();
            if (receipt != null)
                requiredEvidenceReceipts.Add(receipt.Id);
        }

        profile.Evidence.RemoveAll(record =>
            occurredAt - record.RecordedAt > EvidenceRetention &&
            record.Type != QlippothResearchEvidenceType.ChamberDiagnostics &&
            !requiredEvidenceReceipts.Contains(record.Id));
        profile.Evidence.Add(new QlippothResearchEvidenceRecord
        {
            Id = eventId,
            Type = evidenceType,
            RecordedAt = occurredAt,
            Context = context,
            HighTier = highTier,
        });

        var progressCoolingDown = profile.Evidence.Any(record =>
            record.ProgressGranted &&
            record.Type == evidenceType &&
            record.HighTier == highTier &&
            occurredAt - record.RecordedAt < EvidenceProgressCooldown);
        var node = profile.Nodes.FirstOrDefault(candidate =>
            !candidate.Unlocked && !candidate.IsGear && IsAvailable(profile, candidate) &&
            (candidate.RequiredEvidenceTypes.Count == 0 ||
             candidate.RequiredEvidenceTypes.Contains(evidenceType)) &&
            (!candidate.RequiresHighTierEvidence || highTier) &&
            MatchesEvidence(candidate, evidenceType) &&
            (preferredNodeId == null || candidate.Id == preferredNodeId));
        if (node == null || progressCoolingDown)
            return false;

        AddProgress(profile, node, 1);
        profile.Evidence[^1].ProgressGranted = true;
        return true;
    }

    private bool IsFreshEvidence(TimeSpan occurredAt)
    {
        var age = _timing.CurTime - occurredAt;
        return age >= TimeSpan.Zero && age <= MaxEvidenceAge;
    }

    private static bool MatchesEvidence(
        QlippothResearchNodeProgress node,
        QlippothResearchEvidenceType evidenceType)
    {
        return evidenceType switch
        {
            QlippothResearchEvidenceType.GateStabilized or
                QlippothResearchEvidenceType.GateDataExtracted or
                QlippothResearchEvidenceType.GateSealed =>
                node.Activities.Contains(QlippothResearchActivity.Observation) ||
                node.Activities.Contains(QlippothResearchActivity.ResonanceScan),
            QlippothResearchEvidenceType.ChamberBreached or
                QlippothResearchEvidenceType.ChamberRepaired =>
                node.Activities.Contains(QlippothResearchActivity.ContainmentDiagnostics) ||
                node.Activities.Contains(QlippothResearchActivity.ControlledTest),
            QlippothResearchEvidenceType.CorruptionApplied or
                QlippothResearchEvidenceType.CorruptionSpread =>
                node.Activities.Contains(QlippothResearchActivity.ResonanceScan) ||
                node.Activities.Contains(QlippothResearchActivity.ControlledTest),
            QlippothResearchEvidenceType.CorruptionPulse =>
                node.Activities.Contains(QlippothResearchActivity.ResonanceScan) ||
                node.Activities.Contains(QlippothResearchActivity.ContainmentDiagnostics),
            QlippothResearchEvidenceType.CorruptionTreated =>
                node.Activities.Contains(QlippothResearchActivity.Observation) ||
                node.Activities.Contains(QlippothResearchActivity.ContainmentDiagnostics),
            QlippothResearchEvidenceType.ChamberDiagnostics =>
                node.Activities.Contains(QlippothResearchActivity.ContainmentDiagnostics),
            _ => false,
        };
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
        var isDiagnosticAttempt = message.Activity == QlippothResearchActivity.ContainmentDiagnostics;
        if (node == null || node.IsGear || node.Unlocked ||
            !node.Activities.Contains(message.Activity) ||
            (!IsAvailable(profile, node) && !(isDiagnosticAttempt && HasPrerequisites(profile, node))))
        {
            UpdateUiState(uid, console);
            return;
        }

        var diagnosticEvidenceGranted = false;
        if (message.Activity == QlippothResearchActivity.ContainmentDiagnostics)
        {
            if (!TryRecordContainmentDiagnostics(chamber, specimen, qlippoth, node.Id, out diagnosticEvidenceGranted))
            {
                profile.LatestOutcome = "research-console-diagnostics-unavailable";
                UpdateUiState(uid, console);
                return;
            }
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
        var responseCount = _initiation.Dispatch<OnResearchExperimentInitiation>(specimen, experiment);

        if (!diagnosticEvidenceGranted)
            AddProgress(profile, node, 1);

        profile.LatestOutcome = message.Activity == QlippothResearchActivity.ControlledTest
            ? responseCount > 0
                ? "research-console-test-response"
                : "research-console-test-baseline"
            : message.Activity == QlippothResearchActivity.ContainmentDiagnostics
                ? diagnosticEvidenceGranted
                    ? "research-console-diagnostics-recorded"
                    : "research-console-diagnostics-no-change"
                : "research-console-experiment-recorded";
        UpdateUiState(uid, console);
    }

    private bool TryRecordContainmentDiagnostics(
        EntityUid chamberUid,
        EntityUid specimen,
        QlippothComponent qlippoth,
        string nodeId,
        out bool progressGranted)
    {
        progressGranted = false;
        if (!TryComp<ContainmentChamberComponent>(chamberUid, out var chamber) ||
            !chamber.IsBuilt || !chamber.IsOccupied || chamber.ContainedQlippoth != specimen ||
            !TryComp<TransformComponent>(chamberUid, out var chamberTransform) ||
            !TryComp<TransformComponent>(specimen, out var specimenTransform) ||
            chamberTransform.MapID != specimenTransform.MapID ||
            !_containment.IsContainmentDimension(chamberTransform.MapID))
            return false;

        var damageRatio = chamber.BreachThreshold <= 0f
            ? 0f
            : _damageable.GetTotalDamage(chamberUid).Float() / chamber.BreachThreshold;
        var damageBand = damageRatio <= 0f ? 0 : damageRatio >= 0.5f ? 2 : 1;
        var anchored = _containment.IsBoundaryAnchorActive(chamberUid);
        var signature =
            $"{chamber.Sector}:{chamber.IsBuilt}:{chamber.IsBreached}:{damageBand}:{anchored}:{chamber.ContainmentRadius}";
        progressGranted = RecordEvidence(
            specimen,
            qlippoth,
            QlippothResearchEvidenceType.ChamberDiagnostics,
            $"diagnostics:{chamberUid}:{signature}",
            _timing.CurTime,
            $"sector:{chamber.Sector};built:{chamber.IsBuilt};breached:{chamber.IsBreached};damageBand:{damageBand};anchored:{anchored};radius:{chamber.ContainmentRadius}",
            preferredNodeId: nodeId);
        return true;
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
                RequiredEvidenceTypes = template.RequiredEvidenceTypes == null
                    ? new List<QlippothResearchEvidenceType>()
                    : new List<QlippothResearchEvidenceType>(template.RequiredEvidenceTypes),
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
                RequiredEvidenceTypes = new List<QlippothResearchEvidenceType>(checkpoint.RequiredEvidenceTypes),
                RequiresHighTierEvidence = checkpoint.RequiresHighTierEvidence,
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
        return HasPrerequisites(profile, node) &&
               (node.RequiredEvidenceTypes.Count == 0 ||
                profile.Evidence.Any(evidence =>
                    node.RequiredEvidenceTypes.Contains(evidence.Type) &&
                    (!node.RequiresHighTierEvidence || evidence.HighTier)));
    }

    private static bool HasPrerequisites(
        QlippothResearchProfileComponent profile,
        QlippothResearchNodeProgress node)
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
        string? latestOutcome = chamberOccupied &&
                                TryComp<QlippothResearchProfileComponent>(specimen, out var currentProfile)
            ? currentProfile.LatestOutcome
            : null;
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
                    !node.Unlocked && (IsAvailable(profile, node) ||
                        (node.Activities.Contains(QlippothResearchActivity.ContainmentDiagnostics) &&
                         HasPrerequisites(profile, node))),
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
                latestOutcome,
                nodes));
    }

    private sealed record ResearchNodeTemplate(
        string Name,
        string Description,
        List<QlippothResearchActivity> Activities,
        List<QlippothResearchEvidenceType>? RequiredEvidenceTypes = null);
}
