using Robust.Shared.GameStates;

namespace Content.Shared.Qlippoth.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SanityComponent : Component
{
    [DataField("sanity"), AutoNetworkedField]
    public float CurrentSanity { get; set; } = 100f;

    [DataField("maxSanity"), AutoNetworkedField]
    public float MaxSanity { get; set; } = 100f;

    [DataField("drainMultiplier"), AutoNetworkedField]
    public float DrainMultiplier { get; set; } = 1.0f;

}
