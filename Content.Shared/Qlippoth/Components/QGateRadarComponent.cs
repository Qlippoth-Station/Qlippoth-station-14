using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Qlippoth.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QGateRadarComponent : Component
{
    [DataField("lastTrackedProbability"), AutoNetworkedField]
    public float LastTrackedProbability { get; set; } = 0f;

    [DataField("predictedPhase"), AutoNetworkedField]
    public QGatePhase PredictedPhase { get; set; } = QGatePhase.Phase1Rift;

    [DataField("etaSeconds"), AutoNetworkedField]
    public int EtaSeconds { get; set; } = 0;
}

[Serializable, NetSerializable]
public enum QGateRadarUiKey : byte
{
    Key
}
