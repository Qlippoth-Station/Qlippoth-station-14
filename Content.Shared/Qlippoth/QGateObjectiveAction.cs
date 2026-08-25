using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Qlippoth;

/// <summary>
/// A single Q-Gate objective action.
/// Pairs one initiation condition with one or more results.
///
/// YAML example:
///   - !type:QGateObjectiveAction
///     actionName: Stabilize Rift
///     initiation: !type:QGateObjectiveInteractInitiation
///     results:
///       - !type:PlayQGateObjectiveSoundResult
///         soundPath: /Audio/Machines/airlock_open.ogg
///       - !type:CompleteQGateObjectiveResult
/// </summary>
[DataDefinition]
public sealed partial class QGateObjectiveAction
{
    [DataField(required: true)]
    public string ActionName { get; set; } = string.Empty;

    [DataField(required: true)]
    public QGateObjectiveInitiation Initiation { get; set; } = default!;

    [DataField]
    public List<QGateObjectiveResult> Results { get; set; } = new();
}

/// <summary>
/// Abstract base for objective action initiation conditions.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class QGateObjectiveInitiation;

/// <summary>
/// Initiates an objective action when a crew member interacts with it.
/// </summary>
[DataDefinition]
public sealed partial class QGateObjectiveInteractInitiation : QGateObjectiveInitiation;

/// <summary>
/// Abstract base for objective action results.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class QGateObjectiveResult;

/// <summary>
/// Marks the objective complete and reports it to the linked Q-Gate.
/// </summary>
[DataDefinition]
public sealed partial class CompleteQGateObjectiveResult : QGateObjectiveResult;

/// <summary>
/// Plays a sound at the objective when the action executes.
/// </summary>
[DataDefinition]
public sealed partial class PlayQGateObjectiveSoundResult : QGateObjectiveResult
{
    [DataField(required: true)]
    public string SoundPath { get; set; } = string.Empty;

    [DataField]
    public float Volume { get; set; }
}

/// <summary>
/// Spawns an entity at the objective when the action executes.
/// </summary>
[DataDefinition]
public sealed partial class SpawnQGateObjectiveEntityResult : QGateObjectiveResult
{
    [DataField(required: true)]
    public string Prototype { get; set; } = string.Empty;

    [DataField]
    public int Count { get; set; } = 1;
}
