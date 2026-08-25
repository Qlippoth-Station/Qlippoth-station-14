using Robust.Shared.GameStates;

namespace Content.Shared.Qlippoth.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ContainmentChamberComponent : Component
{
    [DataField("chamberId"), AutoNetworkedField]
    public string ChamberId { get; set; } = string.Empty;

    [DataField("sector"), AutoNetworkedField]
    public string Sector { get; set; } = "Engineering";

    [DataField("isBuilt"), AutoNetworkedField]
    public bool IsBuilt { get; set; } = false;

    [DataField("isOccupied"), AutoNetworkedField]
    public bool IsOccupied { get; set; } = false;

    [DataField("containedQlippothUid"), AutoNetworkedField]
    public EntityUid? ContainedQlippoth { get; set; }

    [DataField("isBreached"), AutoNetworkedField]
    public bool IsBreached { get; set; }

    [DataField("breachThreshold")]
    public float BreachThreshold { get; set; } = 100f;

    [DataField("containmentRadius")]
    public float ContainmentRadius { get; set; } = 2.5f;

    [DataField("escapeSpeed")]
    public float EscapeSpeed { get; set; } = 1.5f;
}
