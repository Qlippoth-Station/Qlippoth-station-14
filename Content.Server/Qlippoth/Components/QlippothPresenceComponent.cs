using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Defines in what form the Qlippoth exists and how it can be perceived.
    /// </summary>
    [RegisterComponent]
    public sealed partial class QlippothPresenceComponent : Component
    {
        [DataField]
        public PresenceType PresenceType { get; set; } = PresenceType.Object;

        /// <summary>
        /// To keep track of how the Qlippoth might interact with the games physics. Required for object type Qlippoths.
        /// Not impactful for event and may be impactful for curse types.
        /// </summary>
        [DataField]
        public ObjectSize ObjectSize { get; set; } = ObjectSize.Large;
    }
}
