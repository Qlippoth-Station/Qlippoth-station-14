using Robust.Shared.Audio;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// This component is used to define a main structure for handling all Qlippoth's that can be picked up and be activated.
    /// </summary>
    [RegisterComponent]
    public sealed partial class QlippothHandSizeComponent : Component
    {
        [DataField]
        public SoundSpecifier? QlippothSound = new SoundPathSpecifier("/Audio/Qlippoth/qlippoth.ogg"); // default placeholder sound
    }
}
