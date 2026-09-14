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

        /// <summary>
        /// Free-form state the actions can read (QlippothAction.RequireState) and write (SetStateResult / ToggleStateResult).
        /// Example: stance: defensive.
        /// </summary>
        [DataField]
        public Dictionary<string, string> State { get; set; } = new();

        /// <summary>Runtime: the mob currently holding this Qlippoth in hand, if any.</summary>
        public EntityUid? Holder;
    }

    /// <summary>
    /// Put on whoever is holding one or more Qlippoths in hand, so events that happen to the holder
    /// (damage taken etc.) can be relayed to the held Qlippoths' actions (see OnHolderDamagedInitiation).
    /// Managed by QlippothActionInitiationSystem; removed when the last held Qlippoth leaves the hands.
    /// </summary>
    [RegisterComponent]
    public sealed partial class QlippothHolderComponent : Component
    {
        public HashSet<EntityUid> Held = new();
    }
}
