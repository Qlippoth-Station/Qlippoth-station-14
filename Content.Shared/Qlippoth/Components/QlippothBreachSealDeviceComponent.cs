namespace Content.Shared.Qlippoth.Components;

/// <summary>
/// Marks a Science device that can remove a breached Q-Gate without securing its Qlippoth.
/// </summary>
[RegisterComponent]
public sealed partial class QlippothBreachSealDeviceComponent : Component
{
	[DataField("sealDuration")]
	public TimeSpan SealDuration { get; set; } = TimeSpan.FromSeconds(5);
}

