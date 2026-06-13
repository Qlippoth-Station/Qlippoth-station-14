using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Defines how the Qlippoth moves and when it can interact with others.
    /// </summary>
    [RegisterComponent]
    public sealed partial class QlippothMovementComponent : Component
    {
        [DataField]
        public MovementType MovementType { get; set; } = MovementType.Inanimate;
    }
}
