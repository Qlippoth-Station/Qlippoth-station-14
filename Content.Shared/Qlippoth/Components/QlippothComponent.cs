using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Qlippoth.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QlippothComponent : Component
{
    [DataField("phase"), AutoNetworkedField]
    public QGatePhase Phase { get; set; } = QGatePhase.Phase1Rift;

    [DataField("qlippothCounter"), AutoNetworkedField]
    public int QlippothCounter { get; set; } = 3;

    [DataField("maxCounter"), AutoNetworkedField]
    public int MaxCounter { get; set; } = 3;

    [DataField("stressLevel"), AutoNetworkedField]
    public float StressLevel { get; set; } = 0f;

    [DataField("isMeltingDown"), AutoNetworkedField]
    public bool IsMeltingDown { get; set; } = false;

    [DataField("containmentChamberId"), AutoNetworkedField]
    public string? ContainmentChamberId { get; set; }
}
