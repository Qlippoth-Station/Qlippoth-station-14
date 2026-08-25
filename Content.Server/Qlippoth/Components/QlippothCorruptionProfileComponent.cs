using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Qlippoth;

[RegisterComponent]
public sealed partial class QlippothCorruptionProfileComponent : Component
{
    [DataField]
    public List<QlippothCorruptionEffect> ApplyEffects { get; set; } = new();

    [DataField]
    public List<QlippothCorruptionEffect> PulseEffects { get; set; } = new();
}

[ImplicitDataDefinitionForInheritors]
public abstract partial class QlippothCorruptionEffect
{
    public abstract void Execute(EntityUid target, EntityUid source, CorruptionSystem system);
}

[DataDefinition]
public sealed partial class SpawnCorruptionEffect : QlippothCorruptionEffect
{
    [DataField(required: true)]
    public string Prototype { get; set; } = string.Empty;

    [DataField]
    public int Count { get; set; } = 1;

    public override void Execute(EntityUid target, EntityUid source, CorruptionSystem system)
    {
        system.SpawnEffect(target, Prototype, Count);
    }
}

[DataDefinition]
public sealed partial class DamageSanityCorruptionEffect : QlippothCorruptionEffect
{
    [DataField]
    public float Amount { get; set; } = 2f;

    public override void Execute(EntityUid target, EntityUid source, CorruptionSystem system)
    {
        system.DamageSanity(target, Amount);
    }
}

[DataDefinition]
public sealed partial class PopupCorruptionEffect : QlippothCorruptionEffect
{
    [DataField(required: true)]
    public string Message { get; set; } = string.Empty;

    public override void Execute(EntityUid target, EntityUid source, CorruptionSystem system)
    {
        system.ShowPopup(target, Message);
    }
}
