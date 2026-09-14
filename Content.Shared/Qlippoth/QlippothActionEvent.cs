using Content.Shared.Actions;

namespace Content.Shared.Qlippoth;

/// <summary>
/// The one SS14 action event every Qlippoth item action uses.
/// The action prototype sets <see cref="Key"/>; on the Qlippoth side an <c>OnActionInitiation</c>
/// with the same key picks it up. No per-Qlippoth event classes needed.
///
/// YAML (Resources/Prototypes/Actions/qlippoth.yml):
///   - type: InstantAction
///     event: !type:QlippothActionEvent
///       key: seal
/// </summary>
public sealed partial class QlippothActionEvent : InstantActionEvent
{
    [DataField(required: true)]
    public string Key = string.Empty;

    /// <summary>Flip the action's toggled icon every time it is pressed (for BaseToggleAction prototypes).</summary>
    [DataField]
    public bool FlipToggle;
}
