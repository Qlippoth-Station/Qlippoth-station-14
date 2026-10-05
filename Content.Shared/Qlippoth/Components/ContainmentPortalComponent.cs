using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Qlippoth.Components;

[Serializable, NetSerializable]
public enum ContainmentDepartment : byte
{
    Cargo,
    Civilian,
    Command,
    Engineering,
    Medical,
    Security,
    Science,
    Silicon,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ContainmentPortalComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsExitPortal;

    /// <summary>Which department's area this portal serves on the Containment Dimension map.</summary>
    [DataField, AutoNetworkedField]
    public ContainmentDepartment Department = ContainmentDepartment.Command;
}
