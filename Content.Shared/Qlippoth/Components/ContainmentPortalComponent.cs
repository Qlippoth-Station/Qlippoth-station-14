using Robust.Shared.GameStates;

namespace Content.Shared.Qlippoth.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ContainmentPortalComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsExitPortal;
}
