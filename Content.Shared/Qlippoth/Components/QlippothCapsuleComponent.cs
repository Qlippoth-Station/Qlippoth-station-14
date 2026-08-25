using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Qlippoth.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QlippothCapsuleComponent : Component
{
    [DataField("containedQlippothProto"), AutoNetworkedField]
    public EntProtoId? ContainedQlippothProto { get; set; }

    [DataField("isLocked"), AutoNetworkedField]
    public bool IsLocked { get; set; } = true;

    [DataField("targetChamberId"), AutoNetworkedField]
    public string TargetChamberId { get; set; } = string.Empty;

    [DataField("failureAnnounced"), AutoNetworkedField]
    public bool FailureAnnounced { get; set; }
}
