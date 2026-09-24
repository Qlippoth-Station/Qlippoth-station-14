using System.Linq;
using System.Numerics;
using Content.Shared.Atmos;
using Content.Shared.Mobs;
using Content.Shared.Speech.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Qlippoth
{
    #region Event Args
    // Initiations hand these to results as eventArgs. An initiation that has a "victim" or "other party"
    // declares its own record here and implements IQlippothTargetedEventArgs so targeted results
    // (sanity damage, popups, effects at the target) act on that entity instead of the Qlippoth.

    // Info: Records are used as more convenient data containers, they are immutable and are easier to compare.


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

    /// <summary>Generic "something happened involving this entity". Used by most trigger initiations.
    /// Target is used by the Result system, mostly the target of the effects but sometimes also the initiator.
    /// </summary>
    public sealed record QlippothTargetEventArgs(EntityUid Target) : IQlippothTargetedEventArgs;

    /// <summary>Generic "a mob did something to / with this Qlippoth". Target and Actor may differ (e.g. thrown by A, hit B).
    /// Target: Will be passed onto the result as the target of effects.
    /// Actor: Initiator of this event.
    /// </summary>
    public sealed record QlippothActorEventArgs(EntityUid Target, EntityUid Actor) : IQlippothTargetedEventArgs, IQlippothActorEventArgs;

    /// <summary>Passed with OnPullInitiation / OnPullStoppedInitiation.
    /// Target is the entity pulling the Qlippoth.
    /// </summary>
    public sealed record QlippothPullEventArgs(EntityUid Target) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target; // as Target is the "puller", if actor is required it can be interchanged with target in that case.
    }

    /// <summary>Passed with OnCorruptionAppliedInitiation / OnCorruptionPulseInitiation.
    /// Target is the corrupted crew member.
    /// !!This is not fully implemented yet
    /// </summary>
    public sealed record QlippothCorruptionEventArgs(EntityUid Target, int Severity) : IQlippothTargetedEventArgs;

    /// <summary>Passed with ProximityInitiation and the range-tracking initiations, once per entity.
    /// Target entity is calculated by the initiation so it can vary with different proximity-based initiations.
    /// </summary>
    public sealed record QlippothProximityEventArgs(EntityUid Target, float Distance) : IQlippothTargetedEventArgs;

    /// <summary>Passed with OnActionInitiation / OnSelfActionInitiation.
    /// Target and Actor are both the player who pressed the action.
    /// </summary>
    public sealed record QlippothActionEventArgs(EntityUid Target, string Key) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with OnUsedOnInitiation.
    /// Target is the clicked entity, Actor the player holding the Qlippoth.
    /// </summary>
    public sealed record QlippothInteractEventArgs(EntityUid Target, EntityUid Actor) : IQlippothTargetedEventArgs, IQlippothActorEventArgs;

    /// <summary>Passed with OnInteractUsingInitiation.
    /// Target is the item used on the Qlippoth, Actor the user.
    /// </summary>
    public sealed record QlippothUsedEventArgs(EntityUid Target, EntityUid Actor) : IQlippothTargetedEventArgs, IQlippothActorEventArgs;

    /// <summary>Passed with pick up / drop / equip / unequip and held-only timed initiations.
    /// Target and Actor are the holder.
    /// </summary>
    public sealed record QlippothHolderEventArgs(EntityUid Target) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with OnEquippedInitiation / OnUnequippedInitiation.
    /// Slot is the inventory slot name.
    /// </summary>
    public sealed record QlippothEquipEventArgs(EntityUid Target, string Slot) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with OnHolderDamagedInitiation.
    /// Target is the holder;
    /// !!Damage is the live event, results may change its Damage.
    /// </summary>
    public sealed record QlippothHolderDamagedEventArgs(EntityUid Target, Content.Shared.Damage.Systems.DamageModifyEvent Damage) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with OnMeleeHitInitiation.
    /// Target is the first entity hit, Actor the attacker;
    /// !!Hit is the live event, results may add BonusDamage.
    /// </summary>
    public sealed record QlippothMeleeHitEventArgs(EntityUid Target, EntityUid Actor, Content.Shared.Weapons.Melee.Events.MeleeHitEvent Hit) : IQlippothTargetedEventArgs, IQlippothActorEventArgs;

    /// <summary>Passed with OnAttackedInitiation. T
    /// arget and Actor are the attacker;
    /// !!Attack is the live event (BonusDamage can be changed).
    /// </summary>
    public sealed record QlippothAttackedEventArgs(EntityUid Target, EntityUid Used, Content.Shared.Weapons.Melee.Events.AttackedEvent Attack) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with OnDamagedInitiation.
    /// Target is whoever dealt the damage (falls back to the Qlippoth);
    /// Damage is the change.
    ///
    /// TODO: DamageChangedEvent is marked obsolete upstream ("will be replaced with damage-model specific events;
    /// general 'took damage' can be served by DamageDealtEvent"), but it is NOT a drop-in replacement, so we keep using it
    /// and suppress CS0618 here:
    ///   - DamageChangedEvent is raised AFTER the damage is written and carries the delta that was actually applied,
    ///     DamageIncreased, and the DamageableComponent. DamageDealtEvent is raised BEFORE it is written and carries only
    ///     the requested damage, the origin and InterruptsDoAfters.
    ///   - DamageDealtEvent is only raised on the ChangeDamage path, so SetAllDamage / ClearAllDamage (admin heals) would
    ///     stop triggering.
    ///   - OnDamagedInitiation's totalAbove / totalBelow read the post-hit total, which is not available yet when
    ///     DamageDealtEvent fires.
    /// Migrate when upstream actually removes the event, together with ContainmentDimensionSystem.OnChamberDamaged,
    /// and move the threshold checks to the polled OnHealthThresholdInitiation.
    /// </summary>
#pragma warning disable CS0618 // DamageChangedEvent is obsolete upstream; see the TODO above.
    public sealed record QlippothDamagedEventArgs(EntityUid Target, Content.Shared.Damage.Systems.DamageChangedEvent Damage) : IQlippothTargetedEventArgs;
#pragma warning restore CS0618

    /// <summary>Passed with OnMobStateInitiation / OnHolderMobStateInitiation / OnOffspringDiedInitiation.
    /// Target is the mob whose state changed (Qlippoth).
    /// </summary>
    public sealed record QlippothMobStateEventArgs(EntityUid Target, Content.Shared.Mobs.MobState OldState, Content.Shared.Mobs.MobState NewState) : IQlippothTargetedEventArgs;

    /// <summary>Passed with OnHeardInitiation / OnSpokeInitiation.
    /// Target and Actor are the speaker.
    /// </summary>
    public sealed record QlippothSpeechEventArgs(EntityUid Target, string Message) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with OnEmoteInitiation.
    /// Target is the emoter (the Qlippoth or its holder).
    /// TODO: this could be expanded with "emoting in proximity" and "master system". Master System is on the way.
    /// </summary>
    public sealed record QlippothEmoteEventArgs(EntityUid Target, string EmoteId) : IQlippothTargetedEventArgs;

    /// <summary>Passed with OnExaminedInitiation.
    /// Target/Actor is the examiner;
    /// !!Examine is the live event so ExamineTextResult can push text.
    /// </summary>
    public sealed record QlippothExamineEventArgs(EntityUid Target, Content.Shared.Examine.ExaminedEvent Examine) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with VerbInitiation.
    /// Target/Actor is the player who clicked the verb.
    /// </summary>
    public sealed record QlippothVerbEventArgs(EntityUid Target, string Key) : IQlippothTargetedEventArgs, IQlippothActorEventArgs
    {
        public EntityUid Actor => Target;
    }

    /// <summary>Passed with the container initiations. Works with containing Qlippoths and Qlippoths that contain as well.
    /// Target is the entity that has the container (First case ex: locker; second case ex: Mimic Chest Qlippoth (made up)).
    /// ContainerId is the actual container entity.
    /// </summary>
    public sealed record QlippothContainerEventArgs(EntityUid Target, string ContainerId) : IQlippothTargetedEventArgs;

    /// <summary>Passed with OnSignalInitiation.
    /// Target is the Qlippoth that sent the signal.
    /// </summary>
    public sealed record QlippothSignalEventArgs(EntityUid Target, string Signal) : IQlippothTargetedEventArgs;

    /// <summary>Passed with OnStateChangedInitiation.</summary>
    public sealed record QlippothStateEventArgs(string Key, string Value);

    /// <summary>Passed with OnGameRuleStartedInitiation / OnGameRuleEndedInitiation.</summary>
    public sealed record QlippothGameRuleEventArgs(EntityUid RuleEntity, string RuleId);

    /// <summary>Passed with OnAlertLevelInitiation.</summary>
    public sealed record QlippothAlertLevelEventArgs(EntityUid Station, string Level);

    /// <summary>Passed with the environment polling initiations (gas, temperature, pressure).
    /// Value is the measured number.
    /// </summary>
    public sealed record QlippothEnvironmentEventArgs(float Value);

    /// <summary>Passed with OnArrivedInitiation / containment initiations.
    /// Target is the chamber / gate / capsule involved, if any.
    /// </summary>
    public sealed record QlippothArrivalEventArgs(EntityUid Target, QlippothArrivalKind Kind) : IQlippothTargetedEventArgs;

    /// <summary>
    /// Used to activate results that depend on the arrival type of the Qlippoth.
    /// </summary>
    public enum QlippothArrivalKind : byte
    {
        Any,
        /// <summary>Spawned on the station because a Q-Gate timer ran out.</summary>
        GateBreach,
        /// <summary>Spawned inside a containment chamber from a transport capsule.</summary>
        ContainmentDock,
        /// <summary>Spawned inside a rift dungeon when a gate opened.</summary>
        RiftDungeon,
        /// <summary>Summoned by a ritual. </summary>
        Summoned,
    }

    /// <summary>
    /// Passed with OnChainInitiation. Wraps the eventArgs of the action that started the chain,
    /// so chained results still know the original target / actor (ResolveTarget / ResolveActor look through it).
    /// Inner is the eventArgs of the previous event, including another QlippothChainEventArgs (for multi-step chains).
    /// </summary>
    public sealed record QlippothChainEventArgs(string Key, object? Inner);
    #endregion

    #region Helpers
    /// <summary>Small static helpers both systems and the data classes use.</summary>
    public static class QlippothUtil
    {
        /// <summary>
        /// Checks for components
        /// </summary>
        /// <returns>Boolean</returns>
        public static bool HasComponentNamed(IEntityManager entityManager, EntityUid uid, string componentName)
        {
            return entityManager.ComponentFactory.TryGetRegistration(componentName, out var registration)
                   && entityManager.HasComponent(uid, registration.Type);
        }

        /// <summary>Unwrap chain args so targeted lookups see the original event.</summary>
        public static object? Unwrap(object? eventArgs)
        {
            while (eventArgs is QlippothChainEventArgs chain)
                eventArgs = chain.Inner;
            return eventArgs;
        }

        public static bool ContainsKeyword(string message, List<string> keywords, bool caseSensitive)
        {
            if (keywords.Count == 0)
                return true;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return keywords.Any(keyword => message.Contains(keyword, comparison));
        }
    }
    #endregion

    // Event args records are in the Event Args region at the top of this file.
    // Initiations are grouped below by the design-doc category (External / Trigger / Timed / Event Based / Internal / Interface).

    /// <summary>
    /// Abstract base for all Qlippoth initiations.
    /// Defines when a Qlippoth action is triggered.
    ///
    /// Two hooks:
    ///  - Register: optional, for initiations that need per-entity setup (adds a required component, etc.). Called on MapInit.
    ///  - Matches: optional filter on the incoming eventArgs (right action key, target has a component, damage is external...).
    ///    The action fires only if the initiation type matches AND Matches returns true.
    ///
    /// Initiations that have to look at the world on a timer (auras, gas levels, someone walking into range...)
    /// implement <see cref="IQlippothPolledInitiation"/> and are ticked by QlippothActionInitiationSystem.Update.
    /// </summary>
    [ImplicitDataDefinitionForInheritors]
    public abstract partial class QlippothInitiation
    {
        public virtual void Register(EntityUid uid, QlippothActionInitiationSystem initiationSystem) { }

        public virtual bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs) => true;
    }

    /// <summary>
    /// Type: INTERFACE (a contract, not an initiation you can write in YAML).
    /// Desc: Marks an initiation that is ticked by QlippothActionInitiationSystem.Update every <see cref="Interval"/> seconds
    /// instead of (or in addition to) listening to an SS14 event. Poll() decides itself whether to fire, by calling
    /// initiationSystem.Fire(...). Auras, environment readings and enter/leave-range checks work this way.
    ///
    /// Why it lives here, next to QlippothInitiation, and why it is an interface rather than a base class:
    /// polled initiations exist in more than one design-doc category. TimedInitiation and everything under it are Timed,
    /// while OnEnterRange / OnLeaveRange / OnCrowd / OnGas / OnTemperature / OnPressure / OnHealthThreshold /
    /// OnHolderSanity / OnLightLevel are Trigger. Those classes already inherit their category base class, and C# allows
    /// only one base class, so "can be ticked" has to be expressed as an interface that any category can implement.
    /// Keeping it directly under QlippothInitiation makes the pair of contracts (abstract base + optional interface)
    /// readable in one place; it is not part of the category structure below.
    ///
    /// Implementers own the three members: Interval (seconds between ticks), NextPollAt (runtime bookkeeping, set by the
    /// system) and Poll (the tick body).
    /// </summary>
    public interface IQlippothPolledInitiation
    {
        float Interval { get; }
        TimeSpan NextPollAt { get; set; }
        void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem);
    }

    #region Initiation Types
    [DataDefinition]
    public abstract partial class TriggerInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // Trigger - Activation depends on a condition specific to the Qlippoth, mostly about its physical situation
    // (being pulled, damaged, hit, something walking into range, the air around it...).
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class ExternalInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // External - Activated by another player: clicking it, using it in hand, using it on something, speaking to it...
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class TimedInitiation : QlippothInitiation, IQlippothPolledInitiation
    // -------------------------------------------------------------------------
    // Timed - Activated after a certain amount of time or in intervals since initialization (MapInit).
    // Delays counted from other moments (pickup, containment...) are built by chaining: OnPickedUpInitiation -> DelayResult -> OnChainInitiation.
    // Ticked by QlippothActionInitiationSystem.Update.
    // -------------------------------------------------------------------------
    {
        [DataField]
        public float Interval { get; set; } = 10f;

        /// <summary>Seconds before the first tick. Default = one Interval.</summary>
        [DataField]
        public float? StartDelay { get; set; }

        /// <summary>Random +/- seconds added to every interval. 0 = exact.</summary>
        [DataField]
        public float Variance { get; set; } = 0f;

        /// <summary>Chance (0..1) that a tick actually fires.</summary>
        [DataField]
        public float Chance { get; set; } = 1f;

        /// <summary>Stop after firing this many times. 0 = forever.</summary>
        [DataField]
        public int MaxFires { get; set; } = 0;

        /// <summary>Only tick while a mob holds or wears the Qlippoth. The holder is then the eventArgs target.</summary>
        [DataField]
        public bool OnlyWhileHeld { get; set; } = false;

        /// <summary>Only tick while the Qlippoth is in a containment chamber / not in one. Null = don't care.</summary>
        [DataField]
        public bool? OnlyWhileContained { get; set; }

        /// <summary>Runtime: when this initiation next ticks.</summary>
        public TimeSpan NextPollAt { get; set; }

        /// <summary>Runtime: how many times it has fired.</summary>
        public int Fired;

        public virtual void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            if (!initiationSystem.TimedGate(uid, actions, this))
                return;

            var holder = actions.Holder ?? actions.Wearer;
            var eventArgs = holder != null ? new QlippothHolderEventArgs(holder.Value) : null;
            if (initiationSystem.Fire(uid, actions, action, eventArgs) > 0)
                Fired++;
        }
    }

    [DataDefinition]
    public abstract partial class EventBasedInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // EventBased - Activates at the start or end of other in-game events: station events (game rules), alert levels,
    // containment / gate milestones, signals sent by other Qlippoths.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class InternalInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // Internal - Activates when the controller of the Qlippoth decides to: its own action buttons, its own speech / emotes,
    // or an earlier action of the same Qlippoth chaining into this one.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class InterfaceInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // Interface - Another player opens an interface on the Qlippoth (context-menu verbs, examine text) which both shows
    // information and offers more ways to start actions.
    // -------------------------------------------------------------------------
    #endregion

    #region Fundamental Initiation Implementations (original set)
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
    /// For a Qlippoth mob pressing its OWN buttons use OnSelfActionInitiation.
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

        /// <summary>Finer filter on the clicked entity.</summary>
        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothInteractEventArgs args
               && (RequiredComponent == null || initiationSystem.HasComponentNamed(args.Target, RequiredComponent))
               && initiationSystem.PassesFilter(uid, args.Target, Filter);
    }

    /// <summary>Fires when a mob takes the Qlippoth into a hand. eventArgs is a <see cref="QlippothHolderEventArgs"/>.</summary>
    [DataDefinition]
    public sealed partial class OnPickedUpInitiation : TriggerInitiation { }

    /// <summary>Fires when the Qlippoth leaves a mob's hand. eventArgs is a <see cref="QlippothHolderEventArgs"/>.</summary>
    [DataDefinition]
    public sealed partial class OnDroppedInitiation : TriggerInitiation { }

    /// <summary>
    /// Fires when whoever holds or wears the Qlippoth is about to take damage. Runs before the damage lands,
    /// so results like NegateDamageResult / ScaleIncomingDamageResult can change it. eventArgs is a <see cref="QlippothHolderDamagedEventArgs"/>.
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
    /// Use maxFires: 1 (or AfterDelayInitiation) for a one-shot timer.
    /// </summary>
    [DataDefinition]
    public sealed partial class IntervalInitiation : TimedInitiation { }

    /// <summary>
    /// Aura. Every <see cref="TimedInitiation.Interval"/> seconds, fires once for each entity within <see cref="Range"/> tiles
    /// that has <see cref="RequiredComponent"/> and passes <see cref="Filter"/>. eventArgs is a <see cref="QlippothProximityEventArgs"/>; Target is that entity.
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

        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        /// <summary>Fire for at most this many entities per tick (closest first). 0 = all.</summary>
        [DataField]
        public int MaxTargets { get; set; } = 0;

        public override void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            if (!initiationSystem.TimedGate(uid, actions, this))
                return;

            var fired = 0;
            foreach (var (other, distance) in initiationSystem.EntitiesInRange(uid, Range, RequiredComponent, Filter, MaxTargets))
                fired += initiationSystem.Fire(uid, actions, action, new QlippothProximityEventArgs(other, distance));

            if (fired > 0)
                Fired++;
        }
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

    #region External Initiation Implementations
    // -------------------------------------------------------------------------
    // External initiations: another player does something to the Qlippoth.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires when a player clicks the Qlippoth in the world (structures, machines, large objects) or activates it in hand.
    /// eventArgs is a <see cref="QlippothActorEventArgs"/>; Target and Actor are the player.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnActivateInitiation : ExternalInitiation
    {
        /// <summary>Filter on the player who clicked.</summary>
        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothActorEventArgs args && initiationSystem.PassesFilter(uid, args.Target, Filter);
    }

    /// <summary>
    /// Fires when a player touches the Qlippoth with an empty hand. eventArgs is a <see cref="QlippothActorEventArgs"/>; Target and Actor are the player.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnInteractHandInitiation : ExternalInitiation
    {
        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothActorEventArgs args && initiationSystem.PassesFilter(uid, args.Target, Filter);
    }

    /// <summary>
    /// Fires when the holder activates the Qlippoth in hand (Z / use). eventArgs is a <see cref="QlippothHolderEventArgs"/>.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnUseInHandInitiation : ExternalInitiation { }

    /// <summary>
    /// Fires when a player uses another item on the Qlippoth (feeding it, hitting it with a tool, inserting a sample...).
    /// eventArgs is a <see cref="QlippothUsedEventArgs"/>; Target is the item used, Actor the player.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnInteractUsingInitiation : ExternalInitiation
    {
        /// <summary>Filter on the item used (requiredComponent, whitelist tags...).</summary>
        [DataField]
        public QlippothTargetFilter UsedFilter { get; set; } = new();

        /// <summary>Filter on the player.</summary>
        [DataField]
        public QlippothTargetFilter UserFilter { get; set; } = new();

        /// <summary>Delete the used item after the action fires (consume it).</summary>
        [DataField]
        public bool ConsumeUsed { get; set; }

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothUsedEventArgs args
               && initiationSystem.PassesFilter(uid, args.Target, UsedFilter)
               && initiationSystem.PassesFilter(uid, args.Actor, UserFilter);
    }

    /// <summary>
    /// Fires when a mob melee-attacks the Qlippoth. eventArgs is a <see cref="QlippothAttackedEventArgs"/>; Target/Actor is the attacker.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnAttackedInitiation : ExternalInitiation
    {
        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothAttackedEventArgs args && initiationSystem.PassesFilter(uid, args.Target, Filter);
    }

    /// <summary>Fires when someone throws the Qlippoth. eventArgs is a <see cref="QlippothHolderEventArgs"/> (the thrower) or null.</summary>
    [DataDefinition]
    public sealed partial class OnThrownInitiation : ExternalInitiation { }

    /// <summary>Fires when the thrown Qlippoth lands. eventArgs is a <see cref="QlippothHolderEventArgs"/> (the thrower) or null.</summary>
    [DataDefinition]
    public sealed partial class OnLandInitiation : ExternalInitiation { }

    /// <summary>
    /// Fires when the thrown Qlippoth hits something. eventArgs is a <see cref="QlippothActorEventArgs"/> (Target = what it hit, Actor = thrower)
    /// or a <see cref="QlippothTargetEventArgs"/> when the thrower is unknown.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnThrowHitInitiation : ExternalInitiation
    {
        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is IQlippothTargetedEventArgs targeted && initiationSystem.PassesFilter(uid, targeted.Target, Filter);
    }

    /// <summary>
    /// Fires when the Qlippoth (clothing) is equipped to an inventory slot. eventArgs is a <see cref="QlippothEquipEventArgs"/>; Target is the wearer.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnEquippedInitiation : ExternalInitiation
    {
        /// <summary>Only these slot names (head, mask, outerClothing...). Empty = any slot.</summary>
        [DataField]
        public List<string> Slots { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothEquipEventArgs args && (Slots.Count == 0 || Slots.Contains(args.Slot));
    }

    /// <summary>Fires when the Qlippoth (clothing) is unequipped. eventArgs is a <see cref="QlippothEquipEventArgs"/>.</summary>
    [DataDefinition]
    public sealed partial class OnUnequippedInitiation : ExternalInitiation
    {
        [DataField]
        public List<string> Slots { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothEquipEventArgs args && (Slots.Count == 0 || Slots.Contains(args.Slot));
    }

    /// <summary>
    /// Fires when someone speaks within earshot of the Qlippoth. Adds ActiveListenerComponent on Register.
    /// eventArgs is a <see cref="QlippothSpeechEventArgs"/>; Target/Actor is the speaker.
    /// Example: keywords: ["hastur"] makes the Qlippoth react to its name being said.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnHeardInitiation : ExternalInitiation
    {
        /// <summary>Fire only if the message contains one of these. Empty = any speech.</summary>
        [DataField]
        public List<string> Keywords { get; set; } = new();

        [DataField]
        public bool CaseSensitive { get; set; }

        /// <summary>Listening range in tiles.</summary>
        [DataField]
        public float Range { get; set; } = 8f;

        /// <summary>Ignore the Qlippoth's own speech and its holder's.</summary>
        [DataField]
        public bool IgnoreSelfAndHolder { get; set; } = true;

        [DataField]
        public QlippothTargetFilter SpeakerFilter { get; set; } = new();

        public override void Register(EntityUid uid, QlippothActionInitiationSystem initiationSystem)
        {
            initiationSystem.EnsureListener(uid, Range);
        }

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
        {
            if (eventArgs is not QlippothSpeechEventArgs args)
                return false;
            if (IgnoreSelfAndHolder && (args.Target == uid || initiationSystem.IsHolderOf(uid, args.Target)))
                return false;
            return QlippothUtil.ContainsKeyword(args.Message, Keywords, CaseSensitive)
                   && initiationSystem.PassesFilter(uid, args.Target, SpeakerFilter);
        }
    }

    /// <summary>
    /// Fires when the Qlippoth is put into a container: a locker, a bag, a crate, a containment capsule...
    /// eventArgs is a <see cref="QlippothContainerEventArgs"/>; Target is the container's owner.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnInsertedIntoContainerInitiation : ExternalInitiation
    {
        /// <summary>Only these container ids (storagebase, entity_storage...). Empty = any.</summary>
        [DataField]
        public List<string> ContainerIds { get; set; } = new();

        [DataField]
        public QlippothTargetFilter OwnerFilter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothContainerEventArgs args
               && (ContainerIds.Count == 0 || ContainerIds.Contains(args.ContainerId))
               && initiationSystem.PassesFilter(uid, args.Target, OwnerFilter);
    }

    /// <summary>Fires when the Qlippoth is taken out of a container. eventArgs is a <see cref="QlippothContainerEventArgs"/>.</summary>
    [DataDefinition]
    public sealed partial class OnRemovedFromContainerInitiation : ExternalInitiation
    {
        [DataField]
        public List<string> ContainerIds { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothContainerEventArgs args && (ContainerIds.Count == 0 || ContainerIds.Contains(args.ContainerId));
    }

    /// <summary>
    /// Fires when something is inserted into one of the Qlippoth's own containers (an item slot, a storage, a "mouth").
    /// eventArgs is a <see cref="QlippothContainerEventArgs"/>; Target is the inserted entity.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnEntityInsertedInitiation : ExternalInitiation
    {
        [DataField]
        public List<string> ContainerIds { get; set; } = new();

        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothContainerEventArgs args
               && (ContainerIds.Count == 0 || ContainerIds.Contains(args.ContainerId))
               && initiationSystem.PassesFilter(uid, args.Target, Filter);
    }

    /// <summary>Fires when something is removed from one of the Qlippoth's own containers. eventArgs is a <see cref="QlippothContainerEventArgs"/>.</summary>
    [DataDefinition]
    public sealed partial class OnEntityRemovedInitiation : ExternalInitiation
    {
        [DataField]
        public List<string> ContainerIds { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothContainerEventArgs args && (ContainerIds.Count == 0 || ContainerIds.Contains(args.ContainerId));
    }
    #endregion

    #region Internal Initiation Implementations
    // -------------------------------------------------------------------------
    // Internal initiations: the Qlippoth (its controller, or its own earlier actions) decides.
    // -------------------------------------------------------------------------

    /// <summary>
    /// A player-controlled Qlippoth mob presses one of ITS OWN action buttons (ActionGrant on the mob, event QlippothActionEvent with this key).
    /// Unlike OnActionInitiation this only fires when the performer is the Qlippoth itself.
    /// eventArgs is a <see cref="QlippothActionEventArgs"/>; Target is the Qlippoth.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnSelfActionInitiation : InternalInitiation
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothActionEventArgs args && args.Key == Key && args.Target == uid;
    }

    /// <summary>
    /// Fired by ChainResult / DelayResult / SignalResult(toSelf) of this same Qlippoth. The building block for multi-step behaviours:
    /// action A does something, then chains "phase2"; the OnChainInitiation { key: phase2 } action has its own cooldown, requireState and results.
    /// eventArgs is a <see cref="QlippothChainEventArgs"/> wrapping the original event, so targets carry over.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnChainInitiation : InternalInitiation
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothChainEventArgs args && args.Key == Key;
    }

    /// <summary>
    /// The Qlippoth's controller says something (say / whisper). eventArgs is a <see cref="QlippothSpeechEventArgs"/>; Target is the Qlippoth.
    /// Keywords let a spoken "incantation" trigger a power.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnSpokeInitiation : InternalInitiation
    {
        [DataField]
        public List<string> Keywords { get; set; } = new();

        [DataField]
        public bool CaseSensitive { get; set; }

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothSpeechEventArgs args && args.Target == uid && QlippothUtil.ContainsKeyword(args.Message, Keywords, CaseSensitive);
    }

    /// <summary>
    /// The Qlippoth's controller (or, for held Qlippoths, the holder) performs an emote (Scream, Laugh...). eventArgs is a <see cref="QlippothEmoteEventArgs"/>.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnEmoteInitiation : InternalInitiation
    {
        /// <summary>Emote prototype ids that count. Empty = any emote.</summary>
        [DataField]
        public List<string> Emotes { get; set; } = new();

        /// <summary>Also fire when the mob holding the Qlippoth emotes.</summary>
        [DataField]
        public bool IncludeHolder { get; set; } = false;

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
        {
            if (eventArgs is not QlippothEmoteEventArgs args)
                return false;
            if (args.Target != uid && !(IncludeHolder && initiationSystem.IsHolderOf(uid, args.Target)))
                return false;
            return Emotes.Count == 0 || Emotes.Contains(args.EmoteId);
        }
    }

    /// <summary>A player takes control of the Qlippoth (ghost role taken, admin possession). eventArgs is a <see cref="QlippothTargetEventArgs"/> (the Qlippoth).</summary>
    [DataDefinition]
    public sealed partial class OnMindAddedInitiation : InternalInitiation { }

    /// <summary>The player controlling the Qlippoth leaves it (ghosts, disconnects). eventArgs is a <see cref="QlippothTargetEventArgs"/> (the Qlippoth).</summary>
    [DataDefinition]
    public sealed partial class OnMindRemovedInitiation : InternalInitiation { }

    /// <summary>
    /// The Qlippoth mob lands a melee hit on something with its own body / unarmed attack (mob Qlippoths attacking).
    /// eventArgs is a <see cref="QlippothMeleeHitEventArgs"/>; Target is the first entity hit, Actor is the Qlippoth.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnSelfMeleeHitInitiation : InternalInitiation
    {
        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothMeleeHitEventArgs args && args.Actor == uid && args.Hit.IsHit && args.Hit.HitEntities.Count > 0
               && initiationSystem.PassesFilter(uid, args.Target, Filter);
    }
    #endregion

    #region Trigger Initiation Implementations
    // -------------------------------------------------------------------------
    // Trigger initiations: a condition about the Qlippoth's own situation or surroundings is met.
    // -------------------------------------------------------------------------

    /// <summary>Fires when someone stops pulling the Qlippoth. eventArgs is a <see cref="QlippothPullEventArgs"/>.</summary>
    [DataDefinition]
    public sealed partial class OnPullStoppedInitiation : TriggerInitiation { }

    /// <summary>
    /// Fires when the Qlippoth itself takes (or heals) damage. eventArgs is a <see cref="QlippothDamagedEventArgs"/>;
    /// Target is the damage source when known, otherwise the Qlippoth.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnDamagedInitiation : TriggerInitiation
    {
        /// <summary>true = only when damage goes up, false = only when it goes down (healing), null = both.</summary>
        [DataField]
        public bool? Increased { get; set; } = true;

        /// <summary>Only when the total damage delta of this event is at least this much.</summary>
        [DataField]
        public float MinDelta { get; set; } = 0f;

        /// <summary>Only when one of these damage types is in the delta (Heat, Slash...). Empty = any.</summary>
        [DataField]
        public List<string> DamageTypes { get; set; } = new();

        /// <summary>Only when the Qlippoth's total damage after the hit is at least this.</summary>
        [DataField]
        public float? TotalAbove { get; set; }

        /// <summary>Only when the Qlippoth's total damage after the hit is at most this.</summary>
        [DataField]
        public float? TotalBelow { get; set; }

        /// <summary>Ignore damage with no origin or self-inflicted damage.</summary>
        [DataField]
        public bool ExternalOnly { get; set; }

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
        {
            if (eventArgs is not QlippothDamagedEventArgs args)
                return false;

            var damage = args.Damage;
            if (Increased != null && damage.DamageIncreased != Increased.Value)
                return false;
            if (ExternalOnly && (damage.Origin == null || damage.Origin == uid))
                return false;

            var delta = damage.DamageDelta;
            if (MinDelta > 0f && (delta == null || Math.Abs(delta.GetTotal().Float()) < MinDelta))
                return false;
            if (DamageTypes.Count > 0 && (delta == null || !DamageTypes.Exists(type => delta.DamageDict.TryGetValue(type, out var value) && value != 0)))
                return false;

            var total = initiationSystem.TotalDamage(uid);
            if (TotalAbove != null && total < TotalAbove.Value)
                return false;
            if (TotalBelow != null && total > TotalBelow.Value)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Fires when the Qlippoth's own mob state changes (mob Qlippoths). eventArgs is a <see cref="QlippothMobStateEventArgs"/>.
    /// Example: states: [Dead] + SpawnQlippothResult = it comes back as something else.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnMobStateInitiation : TriggerInitiation
    {
        /// <summary>New states that count. Empty = any change.</summary>
        [DataField]
        public List<MobState> States { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothMobStateEventArgs args && (States.Count == 0 || States.Contains(args.NewState));
    }

    /// <summary>
    /// Fires when the mob holding / wearing the Qlippoth changes mob state (falls into crit, dies...).
    /// eventArgs is a <see cref="QlippothMobStateEventArgs"/>; Target is the holder.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnHolderMobStateInitiation : TriggerInitiation
    {
        [DataField]
        public List<MobState> States { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothMobStateEventArgs args && (States.Count == 0 || States.Contains(args.NewState));
    }

    /// <summary>
    /// Fires when the Qlippoth is destroyed (Destructible threshold) and is about to be deleted. Last chance to leave something behind.
    /// eventArgs is null.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnDestroyedInitiation : TriggerInitiation { }

    /// <summary>Fires once when the Qlippoth is spawned into the world (MapInit). eventArgs is null.</summary>
    [DataDefinition]
    public sealed partial class OnSpawnInitiation : TriggerInitiation { }

    /// <summary>
    /// Fires when something physically collides with the Qlippoth. eventArgs is a <see cref="QlippothTargetEventArgs"/>; Target is the other body.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnCollideInitiation : TriggerInitiation
    {
        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothTargetEventArgs args && initiationSystem.PassesFilter(uid, args.Target, Filter);
    }

    /// <summary>
    /// Fires when something steps onto the Qlippoth (needs StepTrigger on the entity; Register adds one if missing).
    /// eventArgs is a <see cref="QlippothTargetEventArgs"/>; Target is the entity that stepped on it.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnStepTriggerInitiation : TriggerInitiation
    {
        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public override void Register(EntityUid uid, QlippothActionInitiationSystem initiationSystem)
        {
            initiationSystem.EnsureStepTrigger(uid);
        }

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothTargetEventArgs args && initiationSystem.PassesFilter(uid, args.Target, Filter);
    }

    /// <summary>Fires when the Qlippoth is anchored or unanchored (wrenched down / pried loose). eventArgs is null.</summary>
    [DataDefinition]
    public sealed partial class OnAnchorChangedInitiation : TriggerInitiation
    {
        /// <summary>true = only when it becomes anchored, false = only when unanchored, null = both.</summary>
        [DataField]
        public bool? Anchored { get; set; }

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => Anchored == null || initiationSystem.IsAnchored(uid) == Anchored.Value;
    }

    /// <summary>Fires when a machine Qlippoth gains or loses power (ApcPowerReceiver). eventArgs is null.</summary>
    [DataDefinition]
    public sealed partial class OnPowerChangedInitiation : TriggerInitiation
    {
        /// <summary>true = only when powered, false = only when unpowered, null = both.</summary>
        [DataField]
        public bool? Powered { get; set; }

        /// <summary>Runtime: last power state reported by the event.</summary>
        public bool LastPowered;

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => Powered == null || LastPowered == Powered.Value;
    }

    /// <summary>
    /// Fires when one of the Qlippoth's state keys is set (SetStateResult / ToggleStateResult / IncrementStateResult).
    /// eventArgs is a <see cref="QlippothStateEventArgs"/>. Lets a state machine react to its own transitions.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnStateChangedInitiation : TriggerInitiation
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        /// <summary>Only when the key becomes this value. Null = any value.</summary>
        [DataField]
        public string? Value { get; set; }

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothStateEventArgs args && args.Key == Key && (Value == null || args.Value == Value);
    }

    /// <summary>
    /// Fires when a mob this Qlippoth spawned (SpawnMobResult / SpawnQlippothResult with trackOffspring) dies or is deleted.
    /// eventArgs is a <see cref="QlippothMobStateEventArgs"/>; Target is the dead offspring (may already be deleted).
    /// </summary>
    [DataDefinition]
    public sealed partial class OnOffspringDiedInitiation : TriggerInitiation { }

    /// <summary>
    /// Fires once when an entity walks into <see cref="Range"/> (it was not in range at the previous poll).
    /// eventArgs is a <see cref="QlippothProximityEventArgs"/>. Polled every <see cref="Interval"/> seconds.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnEnterRangeInitiation : TriggerInitiation, IQlippothPolledInitiation
    {
        [DataField]
        public float Interval { get; set; } = 0.5f;

        [DataField]
        public float Range { get; set; } = 5f;

        [DataField]
        public string RequiredComponent { get; set; } = "Sanity";

        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public TimeSpan NextPollAt { get; set; }

        /// <summary>Runtime: who was in range at the last poll.</summary>
        public HashSet<EntityUid> Inside = new();

        public void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            var now = new HashSet<EntityUid>();
            foreach (var (other, distance) in initiationSystem.EntitiesInRange(uid, Range, RequiredComponent, Filter, 0))
            {
                now.Add(other);
                if (!Inside.Contains(other))
                    initiationSystem.Fire(uid, actions, action, new QlippothProximityEventArgs(other, distance));
            }
            Inside = now;
        }
    }

    /// <summary>
    /// Fires once when an entity that was in <see cref="Range"/> leaves it (or vanishes).
    /// eventArgs is a <see cref="QlippothTargetEventArgs"/>; Target is the entity that left. Polled every <see cref="Interval"/> seconds.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnLeaveRangeInitiation : TriggerInitiation, IQlippothPolledInitiation
    {
        [DataField]
        public float Interval { get; set; } = 0.5f;

        [DataField]
        public float Range { get; set; } = 5f;

        [DataField]
        public string RequiredComponent { get; set; } = "Sanity";

        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        public TimeSpan NextPollAt { get; set; }

        public HashSet<EntityUid> Inside = new();

        public void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            var now = new HashSet<EntityUid>();
            foreach (var (other, _) in initiationSystem.EntitiesInRange(uid, Range, RequiredComponent, Filter, 0))
                now.Add(other);

            foreach (var gone in Inside)
            {
                if (!now.Contains(gone))
                    initiationSystem.Fire(uid, actions, action, new QlippothTargetEventArgs(gone));
            }
            Inside = now;
        }
    }

    /// <summary>
    /// Fires while the count of entities in range is within [MinCount, MaxCount] (a crowd, or being left alone).
    /// eventArgs is a <see cref="QlippothEnvironmentEventArgs"/> with the count. Polled every <see cref="Interval"/> seconds.
    /// With edgeTriggered it fires only when the condition starts being true.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnCrowdInitiation : TriggerInitiation, IQlippothPolledInitiation
    {
        [DataField]
        public float Interval { get; set; } = 2f;

        [DataField]
        public float Range { get; set; } = 6f;

        [DataField]
        public string RequiredComponent { get; set; } = "Sanity";

        [DataField]
        public QlippothTargetFilter Filter { get; set; } = new();

        [DataField]
        public int MinCount { get; set; } = 3;

        [DataField]
        public int MaxCount { get; set; } = int.MaxValue;

        [DataField]
        public bool EdgeTriggered { get; set; } = true;

        public TimeSpan NextPollAt { get; set; }

        public bool WasTrue;

        public void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            var count = 0;
            foreach (var _ in initiationSystem.EntitiesInRange(uid, Range, RequiredComponent, Filter, 0))
                count++;

            var isTrue = count >= MinCount && count <= MaxCount;
            if (isTrue && (!EdgeTriggered || !WasTrue))
                initiationSystem.Fire(uid, actions, action, new QlippothEnvironmentEventArgs(count));
            WasTrue = isTrue;
        }
    }

    /// <summary>
    /// Fires when the gas on the Qlippoth's tile is within [MinMoles, MaxMoles]. eventArgs is a <see cref="QlippothEnvironmentEventArgs"/> (moles).
    /// Polled every <see cref="Interval"/> seconds; edgeTriggered fires only when the condition starts being true.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnGasInitiation : TriggerInitiation, IQlippothPolledInitiation
    {
        [DataField]
        public float Interval { get; set; } = 2f;

        [DataField(required: true)]
        public Gas Gas { get; set; } = Gas.Plasma;

        [DataField]
        public float MinMoles { get; set; } = 1f;

        [DataField]
        public float MaxMoles { get; set; } = float.MaxValue;

        [DataField]
        public bool EdgeTriggered { get; set; } = true;

        public TimeSpan NextPollAt { get; set; }

        public bool WasTrue;

        public void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            var moles = initiationSystem.TileGasMoles(uid, Gas);
            var isTrue = moles >= MinMoles && moles <= MaxMoles;
            if (isTrue && (!EdgeTriggered || !WasTrue))
                initiationSystem.Fire(uid, actions, action, new QlippothEnvironmentEventArgs(moles));
            WasTrue = isTrue;
        }
    }

    /// <summary>
    /// Fires when the air temperature on the Qlippoth's tile is within [Min, Max] kelvin. eventArgs is a <see cref="QlippothEnvironmentEventArgs"/>.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnTemperatureInitiation : TriggerInitiation, IQlippothPolledInitiation
    {
        [DataField]
        public float Interval { get; set; } = 2f;

        [DataField]
        public float Min { get; set; } = 0f;

        [DataField]
        public float Max { get; set; } = float.MaxValue;

        [DataField]
        public bool EdgeTriggered { get; set; } = true;

        public TimeSpan NextPollAt { get; set; }

        public bool WasTrue;

        public void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            var temperature = initiationSystem.TileTemperature(uid);
            var isTrue = temperature != null && temperature.Value >= Min && temperature.Value <= Max;
            if (isTrue && (!EdgeTriggered || !WasTrue))
                initiationSystem.Fire(uid, actions, action, new QlippothEnvironmentEventArgs(temperature!.Value));
            WasTrue = isTrue;
        }
    }

    /// <summary>
    /// Fires when the air pressure on the Qlippoth's tile is within [Min, Max] kPa (space = 0). eventArgs is a <see cref="QlippothEnvironmentEventArgs"/>.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnPressureInitiation : TriggerInitiation, IQlippothPolledInitiation
    {
        [DataField]
        public float Interval { get; set; } = 2f;

        [DataField]
        public float Min { get; set; } = 0f;

        [DataField]
        public float Max { get; set; } = float.MaxValue;

        [DataField]
        public bool EdgeTriggered { get; set; } = true;

        public TimeSpan NextPollAt { get; set; }

        public bool WasTrue;

        public void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            var pressure = initiationSystem.TilePressure(uid);
            var isTrue = pressure >= Min && pressure <= Max;
            if (isTrue && (!EdgeTriggered || !WasTrue))
                initiationSystem.Fire(uid, actions, action, new QlippothEnvironmentEventArgs(pressure));
            WasTrue = isTrue;
        }
    }

    /// <summary>
    /// Fires when the Qlippoth's own total damage crosses a threshold (health-based phases for mob / structure Qlippoths).
    /// eventArgs is a <see cref="QlippothEnvironmentEventArgs"/> (total damage). Polled; fires once per crossing.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnHealthThresholdInitiation : TriggerInitiation, IQlippothPolledInitiation
    {
        [DataField]
        public float Interval { get; set; } = 1f;

        /// <summary>Total damage at which it fires.</summary>
        [DataField(required: true)]
        public float Threshold { get; set; }

        /// <summary>true = fire when damage rises past the threshold, false = when it drops below.</summary>
        [DataField]
        public bool Above { get; set; } = true;

        public TimeSpan NextPollAt { get; set; }

        public bool WasTrue;

        public void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            var total = initiationSystem.TotalDamage(uid);
            var isTrue = Above ? total >= Threshold : total <= Threshold;
            if (isTrue && !WasTrue)
                initiationSystem.Fire(uid, actions, action, new QlippothEnvironmentEventArgs(total));
            WasTrue = isTrue;
        }
    }

    /// <summary>
    /// Fires when the holder's sanity is within [Min, Max]. Polled; edgeTriggered fires once per crossing.
    /// eventArgs is a <see cref="QlippothHolderEventArgs"/>.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnHolderSanityInitiation : TriggerInitiation, IQlippothPolledInitiation
    {
        [DataField]
        public float Interval { get; set; } = 1f;

        [DataField]
        public float Min { get; set; } = 0f;

        [DataField]
        public float Max { get; set; } = 30f;

        [DataField]
        public bool EdgeTriggered { get; set; } = true;

        public TimeSpan NextPollAt { get; set; }

        public bool WasTrue;

        public void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            var holder = actions.Holder ?? actions.Wearer;
            var sanity = holder != null ? initiationSystem.GetSanity(holder.Value) : null;
            var isTrue = sanity != null && sanity.Value >= Min && sanity.Value <= Max;
            if (isTrue && (!EdgeTriggered || !WasTrue))
                initiationSystem.Fire(uid, actions, action, new QlippothHolderEventArgs(holder!.Value));
            WasTrue = isTrue;
        }
    }

    /// <summary>
    /// Fires when the Qlippoth is in darkness or light. Uses the presence of lit PoweredLight / PointLight sources in range as the measure.
    /// eventArgs is a <see cref="QlippothEnvironmentEventArgs"/> (number of lit sources). Polled; edgeTriggered fires once per change.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnLightLevelInitiation : TriggerInitiation, IQlippothPolledInitiation
    {
        [DataField]
        public float Interval { get; set; } = 2f;

        [DataField]
        public float Range { get; set; } = 4f;

        /// <summary>true = fire in darkness (no lit sources), false = fire when lit.</summary>
        [DataField]
        public bool Dark { get; set; } = true;

        [DataField]
        public bool EdgeTriggered { get; set; } = true;

        public TimeSpan NextPollAt { get; set; }

        public bool WasTrue;

        public void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            var lit = initiationSystem.CountLitLights(uid, Range);
            var isTrue = Dark ? lit == 0 : lit > 0;
            if (isTrue && (!EdgeTriggered || !WasTrue))
                initiationSystem.Fire(uid, actions, action, new QlippothEnvironmentEventArgs(lit));
            WasTrue = isTrue;
        }
    }
    #endregion

    #region Timed Initiation Implementations
    // -------------------------------------------------------------------------
    // Timed initiations: fire after / every N seconds since the Qlippoth was spawned.
    // IntervalInitiation and ProximityInitiation (the auras) are in the original set region above.
    // All of them share TimedInitiation's knobs: interval, startDelay, variance, chance, maxFires, onlyWhileHeld, onlyWhileContained.
    // -------------------------------------------------------------------------

    /// <summary>
    /// One-shot timer: fires once, <see cref="Delay"/> seconds after the Qlippoth spawned. eventArgs is null (or the holder with onlyWhileHeld).
    /// Sugar for IntervalInitiation { interval: delay, maxFires: 1 }.
    /// </summary>
    [DataDefinition]
    public sealed partial class AfterDelayInitiation : TimedInitiation
    {
        [DataField]
        public float Delay { get; set; } = 60f;

        public override void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            Interval = Delay;
            MaxFires = 1;
            base.Poll(uid, actions, action, initiationSystem);
        }
    }

    /// <summary>
    /// Fires once when the round has been running for at least <see cref="AtRoundTime"/> seconds (a Qlippoth that "wakes up" late in the shift).
    /// eventArgs is null.
    /// </summary>
    [DataDefinition]
    public sealed partial class RoundTimeInitiation : TimedInitiation
    {
        [DataField(required: true)]
        public float AtRoundTime { get; set; }

        public override void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            if (Fired > 0 || initiationSystem.RoundSeconds() < AtRoundTime)
                return;
            if (!initiationSystem.TimedGate(uid, actions, this))
                return;
            if (initiationSystem.Fire(uid, actions, action, null) > 0)
                Fired++;
        }
    }

    /// <summary>
    /// Fires every <see cref="TimedInitiation.Interval"/> seconds, but only while a state key holds a value
    /// (a "charging" or "enraged" phase that keeps ticking). Sugar for IntervalInitiation + requireState, kept separate so the
    /// tick counter resets when the state leaves the value. eventArgs is null.
    /// </summary>
    [DataDefinition]
    public sealed partial class WhileStateInitiation : TimedInitiation
    {
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        [DataField(required: true)]
        public string Value { get; set; } = string.Empty;

        public override void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            if (!actions.State.TryGetValue(Key, out var current) || current != Value)
            {
                Fired = 0;
                return;
            }
            base.Poll(uid, actions, action, initiationSystem);
        }
    }

    /// <summary>
    /// Fires every <see cref="TimedInitiation.Interval"/> seconds while the Qlippoth is being pulled. eventArgs is a <see cref="QlippothPullEventArgs"/> (the puller).
    /// </summary>
    [DataDefinition]
    public sealed partial class WhilePulledInitiation : TimedInitiation
    {
        public override void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            if (initiationSystem.GetPuller(uid) is not { } puller)
                return;
            if (!initiationSystem.TimedGate(uid, actions, this))
                return;
            if (initiationSystem.Fire(uid, actions, action, new QlippothPullEventArgs(puller)) > 0)
                Fired++;
        }
    }

    /// <summary>
    /// Fires every <see cref="TimedInitiation.Interval"/> seconds while the Qlippoth is inside a container (locker, crate, bag, capsule).
    /// eventArgs is a <see cref="QlippothTargetEventArgs"/> (the container owner).
    /// </summary>
    [DataDefinition]
    public sealed partial class WhileInContainerInitiation : TimedInitiation
    {
        public override void Poll(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, QlippothActionInitiationSystem initiationSystem)
        {
            if (initiationSystem.GetContainerOwner(uid) is not { } owner)
                return;
            if (!initiationSystem.TimedGate(uid, actions, this))
                return;
            if (initiationSystem.Fire(uid, actions, action, new QlippothTargetEventArgs(owner)) > 0)
                Fired++;
        }
    }
    #endregion

    #region Event Based Initiation Implementations
    // -------------------------------------------------------------------------
    // Event-based initiations: something happens in the round (station events, alerts, containment milestones, other Qlippoths).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires when a game rule / station event starts (meteor swarm, power outage, a gamemode...). eventArgs is a <see cref="QlippothGameRuleEventArgs"/>.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnGameRuleStartedInitiation : EventBasedInitiation
    {
        /// <summary>Rule prototype ids that count. Empty = any rule.</summary>
        [DataField]
        public List<string> RuleIds { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothGameRuleEventArgs args && (RuleIds.Count == 0 || RuleIds.Contains(args.RuleId));
    }

    /// <summary>Fires when a game rule / station event ends. eventArgs is a <see cref="QlippothGameRuleEventArgs"/>.</summary>
    [DataDefinition]
    public sealed partial class OnGameRuleEndedInitiation : EventBasedInitiation
    {
        [DataField]
        public List<string> RuleIds { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothGameRuleEventArgs args && (RuleIds.Count == 0 || RuleIds.Contains(args.RuleId));
    }

    /// <summary>Fires when the station alert level changes (green, blue, red, delta...). eventArgs is a <see cref="QlippothAlertLevelEventArgs"/>.</summary>
    [DataDefinition]
    public sealed partial class OnAlertLevelInitiation : EventBasedInitiation
    {
        /// <summary>Levels that count. Empty = any change.</summary>
        [DataField]
        public List<string> Levels { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothAlertLevelEventArgs args && (Levels.Count == 0 || Levels.Contains(args.Level));
    }

    /// <summary>
    /// Fires when another Qlippoth broadcasts a signal (SignalResult) with this key. eventArgs is a <see cref="QlippothSignalEventArgs"/>; Target is the sender.
    /// Lets Qlippoths cooperate: one screams, the others answer.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnSignalInitiation : EventBasedInitiation
    {
        [DataField(required: true)]
        public string Signal { get; set; } = string.Empty;

        /// <summary>Filter on the sending Qlippoth (e.g. whitelist by tag / prototype).</summary>
        [DataField]
        public QlippothTargetFilter SenderFilter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothSignalEventArgs args && args.Signal == Signal && initiationSystem.PassesFilter(uid, args.Target, SenderFilter);
    }

    /// <summary>
    /// Fires when the Qlippoth arrives somewhere by one of the Qlippoth pipelines: spawned on the station by a gate breach,
    /// docked into a containment chamber, or placed in a rift dungeon. eventArgs is a <see cref="QlippothArrivalEventArgs"/>.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnArrivedInitiation : EventBasedInitiation
    {
        [DataField]
        public QlippothArrivalKind Kind { get; set; } = QlippothArrivalKind.Any;

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothArrivalEventArgs args && (Kind == QlippothArrivalKind.Any || args.Kind == Kind);
    }

    /// <summary>Fires when the Qlippoth is secured in a containment chamber. eventArgs is a <see cref="QlippothArrivalEventArgs"/>; Target is the chamber.</summary>
    [DataDefinition]
    public sealed partial class OnContainedInitiation : EventBasedInitiation { }

    /// <summary>Fires when the chamber holding this Qlippoth is breached (damaged past its threshold). eventArgs is a <see cref="QlippothTargetEventArgs"/>; Target is the chamber.</summary>
    [DataDefinition]
    public sealed partial class OnContainmentBreachedInitiation : EventBasedInitiation { }

    /// <summary>Fires when the Qlippoth has fully escaped a breached chamber. eventArgs is a <see cref="QlippothTargetEventArgs"/>; Target is the chamber.</summary>
    [DataDefinition]
    public sealed partial class OnEscapedContainmentInitiation : EventBasedInitiation { }

    /// <summary>Fires on the rift Qlippoth when its Q-Gate is cleared by the crew (objectives done). eventArgs is a <see cref="QlippothTargetEventArgs"/>; Target is the gate.</summary>
    [DataDefinition]
    public sealed partial class OnGateClearedInitiation : EventBasedInitiation { }

    /// <summary>Fires on every Qlippoth when any Q-Gate on the station breaches. eventArgs is a <see cref="QlippothTargetEventArgs"/>; Target is the gate.</summary>
    [DataDefinition]
    public sealed partial class OnAnyGateBreachedInitiation : EventBasedInitiation { }

    /// <summary>Fires on every Qlippoth when a new Q-Gate appears on the station. eventArgs is a <see cref="QlippothTargetEventArgs"/>; Target is the gate.</summary>
    [DataDefinition]
    public sealed partial class OnGateSpawnedInitiation : EventBasedInitiation { }

    /// <summary>Fires on every Qlippoth when the emergency shuttle is called / recalled. eventArgs is null.</summary>
    [DataDefinition]
    public sealed partial class OnShuttleCalledInitiation : EventBasedInitiation
    {
        /// <summary>true = only on call, false = only on recall, null = both.</summary>
        [DataField]
        public bool? Called { get; set; } = true;

        public bool LastCalled;

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => Called == null || LastCalled == Called.Value;
    }

    /// <summary>Fires on every Qlippoth when the round ends. Last words. eventArgs is null.</summary>
    [DataDefinition]
    public sealed partial class OnRoundEndInitiation : EventBasedInitiation { }
    #endregion

    #region Interface Initiation Implementations
    // -------------------------------------------------------------------------
    // Interface initiations: a player opens something on the Qlippoth that shows information and offers choices.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds an entry to the Qlippoth's right-click context menu. Clicking it fires the action.
    /// Several VerbInitiations on one Qlippoth = a menu of choices; pair with requireState to show different verbs in different states.
    /// eventArgs is a <see cref="QlippothVerbEventArgs"/>; Target/Actor is the player.
    /// </summary>
    [DataDefinition]
    public sealed partial class VerbInitiation : InterfaceInitiation
    {
        /// <summary>Identifies this verb; the initiation only fires for its own key.</summary>
        [DataField(required: true)]
        public string Key { get; set; } = string.Empty;

        /// <summary>Locale key (or raw text) shown in the menu.</summary>
        [DataField(required: true)]
        public string Text { get; set; } = string.Empty;

        /// <summary>Show as an alt-click verb instead of a normal activation verb.</summary>
        [DataField]
        public bool Alternative { get; set; }

        /// <summary>Menu ordering; higher is earlier.</summary>
        [DataField]
        public int Priority { get; set; } = 0;

        /// <summary>Player must be able to reach / interact with the Qlippoth.</summary>
        [DataField]
        public bool RequireInteract { get; set; } = true;

        /// <summary>Only show the verb to players passing this filter.</summary>
        [DataField]
        public QlippothTargetFilter UserFilter { get; set; } = new();

        /// <summary>Only show the verb while the Qlippoth's state matches (in addition to the action's own requireState, which also hides it).</summary>
        [DataField]
        public Dictionary<string, string> ShowWhenState { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothVerbEventArgs args && args.Key == Key && initiationSystem.PassesFilter(uid, args.Target, UserFilter);
    }

    /// <summary>
    /// Fires when a player examines the Qlippoth (shift-click). Pair with ExamineTextResult to show state-dependent descriptions,
    /// or with DamageSanityResult to punish looking at it. eventArgs is a <see cref="QlippothExamineEventArgs"/>; Target/Actor is the examiner.
    /// </summary>
    [DataDefinition]
    public sealed partial class OnExaminedInitiation : InterfaceInitiation
    {
        /// <summary>Only when the examiner is close enough for details.</summary>
        [DataField]
        public bool DetailsRangeOnly { get; set; }

        [DataField]
        public QlippothTargetFilter ExaminerFilter { get; set; } = new();

        public override bool Matches(EntityUid uid, QlippothActionInitiationSystem initiationSystem, object? eventArgs)
            => eventArgs is QlippothExamineEventArgs args
               && (!DetailsRangeOnly || args.Examine.IsInDetailsRange)
               && initiationSystem.PassesFilter(uid, args.Target, ExaminerFilter);
    }
    #endregion
}
