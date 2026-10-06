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
        /// Free-form state the actions can read (QlippothAction.RequireState) and write (SetStateResult / ToggleStateResult / IncrementStateResult).
        /// Example: stance: defensive.
        /// </summary>
        [DataField]
        public Dictionary<string, string> State { get; set; } = new();

        /// <summary>Runtime: the mob currently holding this Qlippoth in hand, if any.</summary>
        public EntityUid? Holder;

        /// <summary>Runtime: the mob currently wearing this Qlippoth (clothing), if any.</summary>
        public EntityUid? Wearer;

        /// <summary>Runtime: mobs / Qlippoths this one spawned with trackOffspring, still alive.</summary>
        public HashSet<EntityUid> Offspring = new();
    }

    /// <summary>
    /// Put on whoever is holding (in hand) or wearing one or more Qlippoths, so events that happen to the holder
    /// (damage taken, mob state, emotes) can be relayed to the held Qlippoths' actions (OnHolderDamagedInitiation etc.).
    /// Managed by QlippothActionInitiationSystem; removed when the last held Qlippoth leaves.
    /// </summary>
    [RegisterComponent]
    public sealed partial class QlippothHolderComponent : Component
    {
        public HashSet<EntityUid> Held = new();
    }

    /// <summary>
    /// Marks an entity spawned by a Qlippoth (SpawnMobResult / SpawnQlippothResult with trackOffspring).
    /// Lets the parent cap its brood (maxAlive), target it (targeting mode Offspring) and react to its death (OnOffspringDiedInitiation).
    /// </summary>
    [RegisterComponent]
    public sealed partial class QlippothOffspringComponent : Component
    {
        public EntityUid Parent;

        /// <summary>Runtime: death already reported to the parent.</summary>
        public bool Reported;
    }
}
