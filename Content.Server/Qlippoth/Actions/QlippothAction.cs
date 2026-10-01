using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared.Whitelist;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// A single Qlippoth action.
    /// Pairs one initiation condition with one or more results.
    ///
    /// YAML example:
    ///   - !type:QlippothAction
    ///     actionName: parry
    ///     initiation: !type:OnHolderDamagedInitiation
    ///     requireState: { stance: defensive }   # optional, see QlippothActionsComponent.State
    ///     cooldown: 6                            # optional, seconds
    ///     chance: 0.5                            # optional, 0..1
    ///     results:
    ///       - !type:NegateDamageResult
    ///       - !type:PlaySoundResult
    ///         soundPath: /Audio/Weapons/block_metal1.ogg
    /// </summary>
    [DataDefinition]
    public sealed partial class QlippothAction
    {
        [DataField(required: true)]
        public string ActionName { get; set; } = default!;

        [DataField(required: true)]
        public QlippothInitiation Initiation { get; set; } = default!;

        [DataField]
        public List<QlippothResult> Results { get; set; } = new();

        /// <summary>
        /// Desc: Only fire if every entry matches the Qlippoth's current state (QlippothActionsComponent.State).
        /// Guide: Change state with SetStateResult / ToggleStateResult / IncrementStateResult.
        /// Note: OPTIONAL! (states are not required for every qlippoth)
        /// </summary>
        [DataField]
        public Dictionary<string, string> RequireState { get; set; } = new();

        /// <summary> Desc: Do not fire while any entry matches the current state.
        /// Note: OPTIONAL! (states are not required for every qlippoth)
        /// </summary>
        [DataField]
        public Dictionary<string, string> ForbidState { get; set; } = new();

        /// <summary> Desc: Seconds this action cannot fire again after firing. 0 = no cooldown.
        /// Note: OPTIONAL!
        /// </summary>
        [DataField]
        public float Cooldown { get; set; } = 0f;

        /// <summary> Desc: Chance (0..1) that the action fires when its initiation triggers. Rolled after state and cooldown checks.
        /// Note: OPTIONAL! (for very specific cases, default guarantees every effect)
        /// </summary>
        [DataField]
        public float Chance { get; set; } = 1f;

        /// <summary>Desc: The action stops firing after this many times. 0 = unlimited.
        /// Note: OPTIONAL!
        /// </summary>
        [DataField]
        public int MaxFires { get; set; } = 0;

        /// <summary> Desc: Keep running the remaining results even if one of them reports failure.
        /// Note: Keep this as is unless you know what you are doing.
        /// </summary>
        [DataField]
        public bool ContinueOnFailure { get; set; } = false;



        #region Runtime fields - DO NOT CHANGE -
        /// <summary>
        /// These fields are used with other fields so do not touch
        /// </summary>

        /// <summary> Runtime: earliest time the action may fire again.
        /// Desc: Memory for cooldown
        /// </summary>
        public TimeSpan NextReadyAt;

        /// <summary> Runtime: how many times it fired.
        /// Desc: Memory for MaxFires
        /// </summary>
        public int TimesFired;
        #endregion
    }

    #region Shared building blocks: filter / targeting / destination
    /// <summary>
    /// Desc: Shared building blocks for initiations and results.
    /// Info: Its up to the developer to use these blocks for their qlippoths. They are supposed to be supporting utility fields,
    /// everything here is optional.
    ///
    /// QlippothTargetFilter  - "does this entity count?"      (used by initiations that look at other entities and by range results)
    /// QlippothTargeting     - "who does this result act on?" (every result that acts on an entity carries one; default = the initiation's target)
    /// QlippothDestination   - "where should something go?"   (teleport / spawn positions)
    ///
    /// They are plain data definitions, so any initiation or result can embed them as a DataField.
    /// The actual lookups are done by QlippothActionResultSystem / QlippothActionInitiationSystem helpers.
    ///
    /// Reference examples (DO NOT DELETE them): Resources/Prototypes/Entities/Qlippoths/qlippoths.yml, section
    /// "SHARED BUILDING BLOCK EXAMPLES": QlippothExampleFilter, QlippothExampleTargeting, QlippothExampleDestination.
    /// Each "USAGE EXAMPLE" comment there marks one working use of a field.
    /// </summary>

    /// <summary>
    /// Entity filter shared by initiations and results. Every field is optional; an empty filter accepts everything.
    /// YAML:
    ///   filter:
    ///     requiredComponent: Sanity     # YAML component name
    ///     whitelist: { tags: [Meat] }   # standard EntityWhitelist
    ///     onlyAlive: true
    ///     maxSanity: 40
    /// Evaluated by QlippothActionInitiationSystem.PassesFilter.
    /// Reference example: entity QlippothExampleFilter in Resources/Prototypes/Entities/Qlippoths/qlippoths.yml
    /// (aura filter, usedFilter/userFilter pair, collision filter, RequireTargetResult gate).
    /// </summary>
    [DataDefinition]
    public sealed partial class QlippothTargetFilter
    {
        /// <summary> Headline: Component Filters
        /// Desc: Entity must have this component (YAML name, e.g. Sanity, Door, Airlock).</summary>
        [DataField]
        public string? RequiredComponent { get; set; }

        /// <summary> Desc: Entity must have every component in this list.</summary>
        [DataField]
        public List<string> RequiredComponents { get; set; } = new();

        /// <summary> Desc: Entity must have none of these components.</summary>
        [DataField]
        public List<string> ForbiddenComponents { get; set; } = new();

        /// <summary> Headline: SS14 Whitelist & Blacklist integration.
        /// Desc: Uses tags, components, sizes to filter entities.
        /// </summary>
        [DataField]
        public EntityWhitelist? Whitelist { get; set; }

        [DataField]
        public EntityWhitelist? Blacklist { get; set; }

        /// <summary> Desc: Only mobs whose MobState is Alive.</summary>
        [DataField]
        public bool OnlyAlive { get; set; }

        /// <summary> Desc: Only mobs whose MobState is Dead.</summary>
        [DataField]
        public bool OnlyDead { get; set; }

        /// <summary> Desc: Only entities that have a mob state at all (mobs), skips items and structures.</summary>
        [DataField]
        public bool OnlyMobs { get; set; }

        /// <summary> Desc: Only entities currently controlled by a player.</summary>
        [DataField]
        public bool OnlyPlayers { get; set; }

        /// <summary> Desc: Only entities carrying SanityComponent with sanity at or above this.
        /// WARNING: Sanity system is not fully implemented, stay clear
        /// </summary>
        [DataField]
        public float? MinSanity { get; set; }

        /// <summary> Desc: Only entities carrying SanityComponent with sanity at or below this.
        /// WARNING: Sanity system is not fully implemented, stay clear
        /// </summary>
        [DataField]
        public float? MaxSanity { get; set; }

        /// <summary> Desc: true = only corrupted crew, false = only uncorrupted crew, null = don't care.
        /// WARNING: Corruption system is not fully implemented, stay clear
        /// </summary>
        [DataField]
        public bool? Corrupted { get; set; }

        /// <summary> Desc: Only entities with this many total damage or more.</summary>
        [DataField]
        public float? MinDamage { get; set; }

        /// <summary> Desc: true = only anchored entities, false = only unanchored, null = don't care.</summary>
        [DataField]
        public bool? Anchored { get; set; }

        /// <summary> Desc: Skip other Qlippoths (entities with QlippothComponent).</summary>
        [DataField]
        public bool ExcludeQlippoths { get; set; }

        /// <summary> Desc: Skip entities this Qlippoth spawned (QlippothOffspringComponent pointing at it).</summary>
        [DataField]
        public bool ExcludeOffspring { get; set; }

        /// <summary> Desc: Skip the mob currently holding / wearing this Qlippoth.</summary>
        [DataField]
        public bool ExcludeHolder { get; set; }
    }

    public enum QlippothTargetMode : byte
    {
        /// <summary> Desc: The initiation's target, falling back to the Qlippoth itself.</summary>
        Target,
        /// <summary> Desc: The mob that caused the initiation (holder, user, performer). Nothing if unknown.</summary>
        Actor,
        /// <summary> Desc: The Qlippoth itself.</summary>
        Self,
        /// <summary> Desc: The mob holding or wearing the Qlippoth. Nothing if it is not held.</summary>
        Holder,
        /// <summary> Desc: Master of the qlippoth, for obedient Qlippoths.</summary>
        Master,
        /// <summary> Desc: Every entity within Range of the Qlippoth that passes Filter.</summary>
        InRange,
        /// <summary> Desc: Every entity within Range of the initiation's target that passes Filter.</summary>
        AroundTarget,
        /// <summary> Desc: One random entity within Range of the Qlippoth that passes Filter.</summary>
        RandomInRange,
        /// <summary> Desc: The closest entity within Range of the Qlippoth that passes Filter.</summary>
        NearestInRange,
        /// <summary> Desc: The entity the Qlippoth is currently pulling, or the one pulling it.</summary>
        PullPartner,
        /// <summary> Desc: Every entity this Qlippoth spawned that is still alive (QlippothOffspringComponent).</summary>
        Offspring,
        /// <summary> Desc: Every other Qlippoth within Range (entities with QlippothActionsComponent).</summary>
        QlippothsInRange,
    }

    /// <summary>
    /// Who a result acts on. Embedded in results as <c>targeting:</c>. Default acts on the initiation's target (old behaviour).
    /// YAML:
    ///   targeting: { mode: InRange, range: 5, maxTargets: 3, filter: { onlyAlive: true } }
    /// Resolved by QlippothActionResultSystem.ResolveTargets.
    /// Reference example: entity QlippothExampleTargeting in Resources/Prototypes/Entities/Qlippoths/qlippoths.yml
    /// (one verb per mode: Target, Self, InRange, AroundTarget, NearestInRange, RandomInRange, Actor, PullPartner, Offspring, QlippothsInRange).
    /// </summary>
    [DataDefinition]
    public sealed partial class QlippothTargeting
    {
        [DataField]
        public QlippothTargetMode Mode { get; set; } = QlippothTargetMode.Target;

        /// <summary> Desc: Tiles, for the range modes.</summary>
        [DataField]
        public float Range { get; set; } = 5f;

        // defined in this file
        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        /// <summary> Desc: Cap for the multi-target modes. 0 = no cap. Targets beyond the cap are dropped at random.</summary>
        [DataField]
        public int MaxTargets { get; set; } = 0;

        /// <summary> Desc: Include the Qlippoth itself in range modes.</summary>
        [DataField]
        public bool IncludeSelf { get; set; }

        /// <summary>Convenience: a targeting that resolves to the Qlippoth itself.</summary>
        public static QlippothTargeting SelfOnly() => new() { Mode = QlippothTargetMode.Self };
    }

    public enum QlippothDestinationMode : byte
    {
        /// <summary> Desc: Where the Qlippoth is.</summary>
        Self,
        /// <summary> Desc: Where the initiation's target is.</summary>
        Target,
        /// <summary> Desc: Where the actor is.</summary>
        Actor,
        /// <summary> Desc: A random spot within Range of the Qlippoth.</summary>
        RandomNearSelf,
        /// <summary> Desc: A random spot within Range of the initiation's target.</summary>
        RandomNearTarget,
        /// <summary> Desc: A random floor tile anywhere on the Qlippoth's grid.</summary>
        RandomOnGrid,
        /// <summary> Desc: A random floor tile anywhere on the station that owns the Qlippoth (falls back to its grid).</summary>
        RandomOnStation,
        /// <summary> Desc: Next to the closest entity (same map) that has Component, e.g. QGate, ContainmentPortal.</summary>
        NearestWithComponent,
        /// <summary> Desc: Fixed offset from the Qlippoth, in tiles.</summary>
        Offset,
    }

    /// <summary>
    /// A place. Used by teleport / spawn results as <c>destination:</c>.
    /// YAML: destination: { mode: RandomNearSelf, range: 4 }
    /// Resolved by QlippothActionResultSystem.ResolveDestination.
    /// Reference example: entity QlippothExampleDestination in Resources/Prototypes/Entities/Qlippoths/qlippoths.yml
    /// (RandomNearSelf, RandomOnStation, Target + scatter, Offset, NearestWithComponent, RandomOnGrid, targeting + destination together).
    /// </summary>
    [DataDefinition]
    public sealed partial class QlippothDestination
    {
        // defined in this file
        [DataField]
        public QlippothDestinationMode Mode { get; set; } = QlippothDestinationMode.Self;

        /// <summary> Desc: Range of the destination.</summary>
        [DataField]
        public float Range { get; set; } = 3f;

        /// <summary> Desc: For NearestWithComponent.</summary>
        [DataField]
        public string? Component { get; set; }

        /// <summary> Desc: For Offset. Also added on top of every other mode.</summary>
        [DataField]
        public Vector2 Offset { get; set; } = Vector2.Zero;
    }
    #endregion
}
