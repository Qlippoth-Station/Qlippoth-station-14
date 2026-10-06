namespace Content.Server.Qlippoth.Components;

public enum QlippothQGearType : byte
{
    ResonanceProbe,
    InversionTongs,
    BoundaryAnchor,
    GateCompass,
    HorizonLens,
}

[RegisterComponent]
public sealed partial class QlippothQGearComponent : Component
{
    [DataField(required: true)]
    public QlippothQGearType GearType;

    [DataField]
    public float CooldownSeconds = 3f;

    [DataField]
    public TimeSpan NextUseAt;
}
