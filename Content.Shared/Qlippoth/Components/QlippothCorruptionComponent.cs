using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared.Qlippoth.Components;

/// <summary>
/// A temporary corruption state applied by Qlippoth exposure (ApplyCorruptionResult).
/// The numbers come from the Qlippoth that applied it, not from global config.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QlippothCorruptionComponent : Component
{
    [DataField, AutoNetworkedField]
    public float RemainingSeconds = 60f;

    [DataField, AutoNetworkedField]
    public int Severity = 1;

    /// <summary>Sanity lost per second per severity point while corrupted.</summary>
    [DataField, AutoNetworkedField]
    public float SanityDrainPerSecond = 2f;

    /// <summary>Seconds between pulses (spread attempt + OnCorruptionPulseInitiation on the source).</summary>
    [DataField, AutoNetworkedField]
    public float PulseInterval = 10f;

    [DataField, AutoNetworkedField]
    public float PulseTimeRemaining;

    /// <summary>Chance per pulse to corrupt each uncorrupted crew member within SpreadRadius. 0 = does not spread.</summary>
    [DataField, AutoNetworkedField]
    public float SpreadChance = 0.15f;

    [DataField, AutoNetworkedField]
    public float SpreadRadius = 5f;

    [DataField, AutoNetworkedField]
    public EntityUid? SourceQlippoth;

    [DataField, AutoNetworkedField]
    public bool IsRemoving;

    [DataField, AutoNetworkedField]
    public MapId AppliedMapId = MapId.Nullspace;
}

/// <summary>The numbers a Qlippoth hands over when it corrupts someone.</summary>
public readonly record struct CorruptionProfile(
    float Duration,
    int Severity,
    float SanityDrainPerSecond,
    float PulseInterval,
    float SpreadChance,
    float SpreadRadius);

public enum QlippothCorruptionRemovalReason : byte
{
    Treated,
    Expired
}

public sealed record QlippothCorruptionAppliedEvent(
    EntityUid Target,
    EntityUid? SourceQlippoth,
    int Severity,
    bool Spread);

public sealed record QlippothCorruptionSpreadEvent(EntityUid Source, EntityUid Target, int Severity);

public sealed record QlippothCorruptionTransferredEvent(
    EntityUid Target,
    MapId FromMap,
    MapId ToMap,
    float RemainingSeconds);

public sealed record QlippothCorruptionPulseEvent(EntityUid Target, EntityUid? SourceQlippoth, int Severity);

public sealed record QlippothCorruptionRemovedEvent(
    EntityUid Target,
    EntityUid? SourceQlippoth,
    int Severity,
    QlippothCorruptionRemovalReason Reason);

public sealed record QlippothCorruptionSourceLostEvent(EntityUid Target, EntityUid SourceQlippoth);

public sealed record QlippothCorruptionMitigatedEvent(EntityUid Target, EntityUid? SourceQlippoth, EntityUid Chamber);
