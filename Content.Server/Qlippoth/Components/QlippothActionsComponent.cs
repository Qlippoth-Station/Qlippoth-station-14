using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using System.Collections.Generic;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Holds all actions of a Qlippoth.
    /// </summary>
    [RegisterComponent]
    public sealed partial class QlippothActionsComponent : Component
    {
        [DataField]
        public List<QlippothAction> Actions { get; set; } = new();
    }
}
