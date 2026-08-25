using Robust.Shared.GameStates;
using Content.Shared.Qlippoth;

namespace Content.Shared.Qlippoth.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QGateDungeonObjectiveComponent : Component
{
    [DataField("objectiveType"), AutoNetworkedField]
    public QGateObjectiveType ObjectiveType { get; set; }

    [AutoNetworkedField]
    public EntityUid Gate { get; set; } = EntityUid.Invalid;

    [AutoNetworkedField]
    public bool Completed { get; set; }

    [DataField("actions")]
    public List<QGateObjectiveAction> Actions { get; set; } = new();
}
