using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Qlippoth.Components;

/// <summary>
/// Console component for running research experiments on the Qlippoth in its linked chamber.
/// The specimen owns its generated graph; this component stores only the chamber link.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QlippothResearchConsoleComponent : Component
{
    /// <summary>
    /// The linked containment chamber entity this console monitors.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? LinkedChamber;

}

[Serializable, NetSerializable]
public enum QlippothResearchConsoleUiKey : byte
{
    Key
}

/// <summary>
/// Read-only view of the Qlippoth currently contained by this console's linked chamber.
/// Research ownership stays server-side on the QlippothResearchProfileComponent.
/// </summary>
[Serializable, NetSerializable]
public sealed class QlippothResearchConsoleBuiState : BoundUserInterfaceState
{
    public bool HasLinkedChamber;
    public bool ChamberOccupied;
    public string? QlippothName;
    public float ActivityCooldown;
    public List<QlippothResearchNodeState> Nodes;

    public QlippothResearchConsoleBuiState(
        bool hasLinkedChamber,
        bool chamberOccupied,
        string? qlippothName,
        float activityCooldown,
        List<QlippothResearchNodeState> nodes)
    {
        HasLinkedChamber = hasLinkedChamber;
        ChamberOccupied = chamberOccupied;
        QlippothName = qlippothName;
        ActivityCooldown = activityCooldown;
        Nodes = nodes;
    }
}
