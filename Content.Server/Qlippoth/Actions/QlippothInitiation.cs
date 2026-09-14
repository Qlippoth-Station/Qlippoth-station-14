using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Qlippoth;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Qlippoth
{
    #region Event Args
    // Initiations hand these to results as eventArgs. An initiation that has a "victim" or "other party"
    // declares its own record here, right beside itself, and implements IQlippothTargetedEventArgs so
    // targeted results (sanity damage, popups, effects at the target) act on that entity instead of the Qlippoth.

    /// <summary>
    /// Implemented by eventArgs that carry a "victim" or "other party" for the action.
    /// Results that act on someone other than the Qlippoth itself read the target from here;
    /// if the eventArgs don't implement it they fall back to the Qlippoth (see QlippothActionResultSystem.ResolveTarget).
    /// </summary>
    public interface IQlippothTargetedEventArgs
    {
        EntityUid Target { get; }
    }

    /// <summary>
    /// Implemented by eventArgs that know which mob caused the initiation (the holder, the user, the action performer).
    /// Results that talk to a player (popups) use it via QlippothActionResultSystem.ResolveActor.
    /// </summary>
    public interface IQlippothActorEventArgs
    {
        EntityUid Actor { get; }
    }

    /// <summary>Passed with <see cref="OnPullInitiation"/>. Target is the entity pulling the Qlippoth.</summary>
    public sealed record QlippothPullEventArgs(EntityUid Target) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with <see cref="OnCorruptionAppliedInitiation"/> / <see cref="OnCorruptionPulseInitiation"/>. Target is the corrupted crew member.</summary>
    public sealed record QlippothCorruptionEventArgs(EntityUid Target, int Severity) : IQlippothTargetedEventArgs;

    /// <summary>Passed with <see cref="ProximityInitiation"/>, once per entity in range. Target is that entity.</summary>
    public sealed record QlippothProximityEventArgs(EntityUid Target, float Distance) : IQlippothTargetedEventArgs;

    /// <summary>Passed with <see cref="OnActionInitiation"/>. Target and Actor are both the player who pressed the action.</summary>
    public sealed record QlippothActionEventArgs(EntityUid Target, string Key) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with <see cref="OnUsedOnInitiation"/>. Target is the clicked entity, Actor the player holding the Qlippoth.</summary>
    public sealed record QlippothInteractEventArgs(EntityUid Target, EntityUid Actor) : IQlippothTargetedEventArgs, IQlippothActorEventArgs;

    /// <summary>Passed with <see cref="OnPickedUpInitiation"/>, <see cref="OnDroppedInitiation"/> and held-only <see cref="IntervalInitiation"/>. Target and Actor are the holder.</summary>
    public sealed record QlippothHolderEventArgs(EntityUid Target) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with <see cref="OnHolderDamagedInitiation"/>. Target is the holder; Damage is the live event, results may change its Damage.</summary>
    public sealed record QlippothHolderDamagedEventArgs(EntityUid Target, DamageModifyEvent Damage) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with <see cref="OnMeleeHitInitiation"/>. Target is the first entity hit, Actor the attacker; Hit is the live event, results may add BonusDamage.</summary>
    public sealed record QlippothMeleeHitEventArgs(EntityUid Target, EntityUid Actor, MeleeHitEvent Hit) : IQlippothTargetedEventArgs, IQlippothActorEventArgs;
    #endregion

    /// <summary>
    /// Abstract base for all Qlippoth initiations.
    /// Defines when a Qlippoth action is triggered.
    ///
    /// Two hooks:
    ///  - Register: optional, for initiations that need per-entity setup. Most don't.
    ///  - Matches: optional filter on the incoming eventArgs (right action key, target has a component, damage is external...).
    ///    The action fires only if the initiation type matches AND Matches returns true.
    /// </summary>
    [ImplicitDataDefinitionForInheritors]
    public abstract partial class QlippothInitiation
    {
        public virtual void Register(EntityUid uid, QlippothActionInitiationSystem initiationSystem) { }

        public virtual bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs) => true;
    }

    #region Initiation Types
    [DataDefinition]
    public abstract partial class TriggerInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // Trigger - Activation depends on a condition specific to the Qlippoth. Usually does not interact with in-game events.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class ExternalInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // External - Activated by others, ex: in-hand activation by another player.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class TimedInitiation : QlippothInitiation
    // -------------------------------------------------------------------------
    // Timed - Activated after a certain amount of time or in intervals, after initialization, arrival to the station, containment etc.
    // May be used in combination with other initiations with chained actions.
    // Ticked by QlippothActionInitiationSystem.Update.
    // -------------------------------------------------------------------------
    {
        [DataField]
        public float Interval { get; set; } = 10f;

        /// <summary>Only tick while a mob holds the Qlippoth in hand. The holder is then the eventArgs target.</summary>
        [DataField]
        public bool OnlyWhileHeld { get; set; } = false;

        /// <summary>Runtime: when this initiation next fires.</summary>
        public TimeSpan NextFireAt;
    }

    [DataDefinition]
    public abstract partial class EventBasedInitiation : QlippothInitiation
    // -------------------------------------------------------------------------
    // EventBased - Activates at the start or end of other in-game events. This does not necessarily mean coded event system, more focused on gameplay events.
    // -------------------------------------------------------------------------
    {
        [DataField]
        public string EventId { get; set; } = string.Empty;
    }

    [DataDefinition]
    public abstract partial class InternalInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // Internal - Activates when the controller of the Qlippoth decides to. Requires UI for Players.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class InterfaceInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // Interface - Opens up a UI Interface, acts more like a result that opens up more actions. Interface could be shown to the controller or others that interact with it.
    // -------------------------------------------------------------------------
    #endregion

    #region Fundamental Initiation Implementations
    [DataDefinition]
    public sealed partial class InRangeClickInitiation : ExternalInitiation
    {
        public override void Register(EntityUid uid, QlippothActionInitiationSystem initiationSystem)
        {
            ISawmill sawmill = Logger.GetSawmill("qlippoth");
            sawmill.Warning("InRangeClickInitiation Register() is not subscribed yet.");
        }
    }

    /// <summary>
    /// Fires when someone starts pulling this Qlippoth.
    /// eventArgs is a <see cref="QlippothPullEventArgs"/>; results that act on the puller use its Target.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnPullInitiation : TriggerInitiation { }

    /// <summary>
    /// Fires when a player presses one of the Qlippoth's item actions (ActionGrant + ItemActionGrant on the entity).
    /// The action prototype's event must be a QlippothActionEvent whose key equals <see cref="Key"/>.
    /// eventArgs is a <see cref="QlippothActionEventArgs"/>; Target is the player.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnActionInitiation : ExternalInitiation
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothActionEventArgs args && args.Key == Key;
    }

    /// <summary>
    /// Fires when a player uses the held Qlippoth on another entity (click it while in hand).
    /// eventArgs is a <see cref="QlippothInteractEventArgs"/>; Target is the clicked entity.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnUsedOnInitiation : ExternalInitiation
    {
        /// <summary>Only fire if the clicked entity has this component (YAML name, e.g. Door). Null = any entity.</summary>
        [DataField]
        public string? RequiredComponent { get; set; }

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothInteractEventArgs args
               && (RequiredComponent == null || initiationSystem.HasComponentNamed(args.Target, RequiredComponent));
    }

    /// <summary>Fires when a mob takes the Qlippoth into a hand. eventArgs is a <see cref="QlippothHolderEventArgs"/>.</summary>
    [DataDefinition]
    public sealed partial class OnPickedUpInitiation : TriggerInitiation { }

    /// <summary>Fires when the Qlippoth leaves a mob's hand. eventArgs is a <see cref="QlippothHolderEventArgs"/>.</summary>
    [DataDefinition]
    public sealed partial class OnDroppedInitiation : TriggerInitiation { }

    /// <summary>
    /// Fires when whoever holds the Qlippoth is about to take damage. Runs before the damage lands,
    /// so results like NegateDamageResult can change it. eventArgs is a <see cref="QlippothHolderDamagedEventArgs"/>.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnHolderDamagedInitiation : TriggerInitiation
    {
        /// <summary>Ignore damage with no source, self-inflicted damage, and damage dealt by the Qlippoth itself.</summary>
        [DataField]
        public bool ExternalOnly { get; set; } = true;

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
        {
            if (eventArgs is not QlippothHolderDamagedEventArgs args || !args.Damage.Damage.AnyPositive())
                return false;

            if (!ExternalOnly)
                return true;

            var origin = args.Damage.Origin;
            return origin != null && origin != args.Target && origin != uid;
        }
    }

    /// <summary>
    /// Fires when the Qlippoth (as a melee weapon) lands a hit. eventArgs is a <see cref="QlippothMeleeHitEventArgs"/>;
    /// results like BonusMeleeDamageResult can add to the hit.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnMeleeHitInitiation : TriggerInitiation
    {
        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothMeleeHitEventArgs args && args.Hit.IsHit && args.Hit.HitEntities.Count > 0;
    }

    /// <summary>
    /// Fires every <see cref="TimedInitiation.Interval"/> seconds. With onlyWhileHeld it ticks only in a mob's hand
    /// and the eventArgs is a <see cref="QlippothHolderEventArgs"/>; otherwise eventArgs is null.
    /// </summary>
    [DataDefinition]
    public sealed partial class IntervalInitiation : TimedInitiation { }

    /// <summary>
    /// Aura. Every <see cref="TimedInitiation.Interval"/> seconds, fires once for each entity within <see cref="Range"/> tiles
    /// that has <see cref="RequiredComponent"/>. eventArgs is a <see cref="QlippothProximityEventArgs"/>; Target is that entity.
    /// This is how a Qlippoth drains sanity or corrupts crew nearby: pair it with DamageSanityResult / ApplyCorruptionResult.
    /// Note: the action's cooldown applies to the whole tick, not per target.
    /// </summary>
    [DataDefinition]
    public sealed partial class ProximityInitiation : TimedInitiation
    {
        [DataField]
        public float Range { get; set; } = 8f;

        /// <summary>Only entities with this component (YAML name) count. Default Sanity = crew members.</summary>
        [DataField]
        public string RequiredComponent { get; set; } = "Sanity";
    }

    /// <summary>
    /// Fires once when this Qlippoth corrupts a crew member (ApplyCorruptionResult / CorruptionSystem.ApplyCorruption).
    /// eventArgs is a <see cref="QlippothCorruptionEventArgs"/>; results that act on the victim use its Target.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnCorruptionAppliedInitiation : TriggerInitiation { }

    /// <summary>
    /// Fires every corruption pulse (the corruption's pulseInterval) for each crew member this Qlippoth corrupted.
    /// eventArgs is a <see cref="QlippothCorruptionEventArgs"/>. Dispatched by CorruptionSystem.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnCorruptionPulseInitiation : TriggerInitiation { }
    #endregion

    #region Complex Initiation Implementations
    #endregion

    #region Other Initiation Implementations
    #endregion
}
