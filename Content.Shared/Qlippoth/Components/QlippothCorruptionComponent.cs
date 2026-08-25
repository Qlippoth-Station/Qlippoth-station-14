using Robust.Shared.GameStates;
using Content.Shared.Qlippoth;

namespace Content.Shared.Qlippoth.Components;

/// <summary>
/// A temporary corruption state applied by severe Qlippoth exposure.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QlippothCorruptionComponent : Component
{
    [DataField, AutoNetworkedField]
    public float RemainingSeconds = 60f;

    [DataField, AutoNetworkedField]
    public QGatePhase SourcePhase = QGatePhase.Phase4Abyss;

    [DataField, AutoNetworkedField]
    public int Severity = 1;

    [DataField, AutoNetworkedField]
    public float PulseTimeRemaining;

    [DataField, AutoNetworkedField]
    public EntityUid? SourceQlippoth;
}

