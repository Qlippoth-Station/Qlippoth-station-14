using System.Linq;
using Content.Server.AlertLevel;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Qlippoth.Systems;
using Content.Server.RoundEnd;
using Content.Shared.Atmos;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.GameTicking.Components;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Light.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Power;
using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.StepTrigger.Components;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Listens to SS14 events and forwards matching Qlippoth actions to QlippothActionResultSystem.
    /// Each initiation type has its own subscriber below; polled initiations (timers, auras, environment checks) are ticked in Update.
    ///
    /// Also exposes the helper queries the initiation data classes call from their Matches() / Poll()
    /// (filters, range lookups, tile atmos readings, holder bookkeeping).
    /// </summary>
    public sealed partial class QlippothActionInitiationSystem : EntitySystem
    {
        [Dependency] private QlippothActionResultSystem _resultSystem = default!;
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private IRobustRandom _random = default!;
        [Dependency] private EntityLookupSystem _lookup = default!;
        [Dependency] private SharedTransformSystem _transform = default!;
        [Dependency] private EntityWhitelistSystem _whitelist = default!;
        [Dependency] private MobStateSystem _mobState = default!;
        [Dependency] private DamageableSystem _damageable = default!;
        [Dependency] private AtmosphereSystem _atmosphere = default!;
        [Dependency] private SharedContainerSystem _container = default!;
        [Dependency] private GameTicker _gameTicker = default!;
        [Dependency] private RoundEndSystem _roundEnd = default!;

        public static readonly VerbCategory QlippothVerbCategory = new("verb-categories-qlippoth", null);

        /// <summary>
        /// Actual subscriptions to events, happen here.
        /// Initiation classes just decide which subscriptions apply to that specific Qlippoth (via Matches / Poll).
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();

            // lifecycle
            SubscribeLocalEvent<QlippothActionsComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<QlippothActionsComponent, DestructionEventArgs>(OnDestroyed);

            // external
            SubscribeLocalEvent<QlippothActionsComponent, QlippothActionEvent>(OnActionPressed);
            SubscribeLocalEvent<QlippothActionsComponent, AfterInteractEvent>(OnAfterInteract);
            SubscribeLocalEvent<QlippothActionsComponent, ActivateInWorldEvent>(OnActivate);
            SubscribeLocalEvent<QlippothActionsComponent, InteractHandEvent>(OnInteractHand);
            SubscribeLocalEvent<QlippothActionsComponent, UseInHandEvent>(OnUseInHand);
            SubscribeLocalEvent<QlippothActionsComponent, InteractUsingEvent>(OnInteractUsing);
            SubscribeLocalEvent<QlippothActionsComponent, AttackedEvent>(OnAttacked);
            SubscribeLocalEvent<QlippothActionsComponent, GotEquippedHandEvent>(OnEquippedHand);
            SubscribeLocalEvent<QlippothActionsComponent, GotUnequippedHandEvent>(OnUnequippedHand);
            SubscribeLocalEvent<QlippothActionsComponent, GotEquippedEvent>(OnEquipped);
            SubscribeLocalEvent<QlippothActionsComponent, GotUnequippedEvent>(OnUnequipped);
            SubscribeLocalEvent<QlippothActionsComponent, ThrownEvent>(OnThrown);
            SubscribeLocalEvent<QlippothActionsComponent, LandEvent>(OnLand);
            SubscribeLocalEvent<QlippothActionsComponent, ThrowDoHitEvent>(OnThrowHit);
            SubscribeLocalEvent<QlippothActionsComponent, ListenEvent>(OnListen);
            SubscribeLocalEvent<QlippothActionsComponent, EntGotInsertedIntoContainerMessage>(OnGotInserted);
            SubscribeLocalEvent<QlippothActionsComponent, EntGotRemovedFromContainerMessage>(OnGotRemoved);
            SubscribeLocalEvent<QlippothActionsComponent, EntInsertedIntoContainerMessage>(OnEntityInserted);
            SubscribeLocalEvent<QlippothActionsComponent, EntRemovedFromContainerMessage>(OnEntityRemoved);

            // trigger
            SubscribeLocalEvent<QlippothActionsComponent, PullStartedMessage>(OnPullStarted);
            SubscribeLocalEvent<QlippothActionsComponent, PullStoppedMessage>(OnPullStopped);
            SubscribeLocalEvent<QlippothActionsComponent, MeleeHitEvent>(OnMeleeHit);
#pragma warning disable CS0618 // DamageChangedEvent is obsolete upstream; see QlippothDamagedEventArgs for why we still use it.
            SubscribeLocalEvent<QlippothActionsComponent, DamageChangedEvent>(OnDamaged);
#pragma warning restore CS0618
            SubscribeLocalEvent<QlippothActionsComponent, MobStateChangedEvent>(OnMobStateChanged);
            SubscribeLocalEvent<QlippothActionsComponent, StartCollideEvent>(OnCollide);
            SubscribeLocalEvent<QlippothActionsComponent, StepTriggeredOffEvent>(OnStepTriggered);
            SubscribeLocalEvent<QlippothActionsComponent, AnchorStateChangedEvent>(OnAnchorChanged);
            SubscribeLocalEvent<QlippothActionsComponent, PowerChangedEvent>(OnPowerChanged);

            // internal
            SubscribeLocalEvent<QlippothActionsComponent, EntitySpokeEvent>(OnSpoke);
            SubscribeLocalEvent<QlippothActionsComponent, EmoteEvent>(OnEmote);
            SubscribeLocalEvent<QlippothActionsComponent, MindAddedMessage>(OnMindAdded);
            SubscribeLocalEvent<QlippothActionsComponent, MindRemovedMessage>(OnMindRemoved);

            // interface
            SubscribeLocalEvent<QlippothActionsComponent, GetVerbsEvent<ActivationVerb>>(OnGetActivationVerbs);
            SubscribeLocalEvent<QlippothActionsComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
            SubscribeLocalEvent<QlippothActionsComponent, ExaminedEvent>(OnExamined);

            // holder relays
            SubscribeLocalEvent<QlippothHolderComponent, DamageModifyEvent>(OnHolderDamageModify);
            SubscribeLocalEvent<QlippothHolderComponent, MobStateChangedEvent>(OnHolderMobStateChanged);
            SubscribeLocalEvent<QlippothHolderComponent, EmoteEvent>(OnHolderEmote);

            // offspring
            SubscribeLocalEvent<QlippothOffspringComponent, MobStateChangedEvent>(OnOffspringMobState);
            SubscribeLocalEvent<QlippothOffspringComponent, EntityTerminatingEvent>(OnOffspringTerminating);

            // event based (broadcast)
            SubscribeLocalEvent<GameRuleStartedEvent>(OnGameRuleStarted);
            SubscribeLocalEvent<GameRuleEndedEvent>(OnGameRuleEnded);
            SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
            SubscribeLocalEvent<RoundEndSystemChangedEvent>(OnRoundEndSystemChanged);
            SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEnd);
        }

        /// <summary>Ticks every polled initiation (timers, auras, environment checks).</summary>
        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var now = _timing.CurTime;
            var query = EntityQueryEnumerator<QlippothActionsComponent>();
            while (query.MoveNext(out var uid, out var actionsComponent))
            {
                // snapshot: results may add/remove actions while we iterate
                foreach (var action in actionsComponent.Actions.ToArray())
                {
                    if (action.Initiation is not IQlippothPolledInitiation polled || now < polled.NextPollAt)
                        continue;

                    var interval = polled.Interval;
                    if (polled is TimedInitiation timed && timed.Variance > 0f)
                        interval += _random.NextFloat(-timed.Variance, timed.Variance);
                    polled.NextPollAt = now + TimeSpan.FromSeconds(Math.Max(0.05f, interval));

                    polled.Poll(uid, actionsComponent, action, this);
                }
            }
        }

        #region Lifecycle
        private void OnMapInit(EntityUid uid, QlippothActionsComponent component, MapInitEvent args)
        {
            var now = _timing.CurTime;
            foreach (var action in component.Actions)
            {
                action.Initiation.Register(uid, this);
                if (action.Initiation is IQlippothPolledInitiation polled)
                {
                    var first = polled is TimedInitiation { StartDelay: { } delay } ? delay : polled.Interval;
                    polled.NextPollAt = now + TimeSpan.FromSeconds(first);
                }
            }

            DispatchActions(uid, component, typeof(OnSpawnInitiation), null);
        }

        private void OnDestroyed(EntityUid uid, QlippothActionsComponent component, DestructionEventArgs args)
        {
            DispatchActions(uid, component, typeof(OnDestroyedInitiation), null);
        }
        #endregion

        #region Dispatch core
        /// <summary>
        /// Detects the right action to initiate and relays actions to Result System.
        /// An action fires when: its initiation is of the requested type, the initiation's Matches() accepts the eventArgs,
        /// the Qlippoth's state satisfies the action's requireState, and the action is off cooldown.
        /// Matching actions are collected first and executed afterwards, so a result changing state does not affect
        /// which actions fire for this event.
        /// Returns how many actions fired.
        /// </summary>
        private int DispatchActions(EntityUid uid, QlippothActionsComponent actionsComponent, Type initiationType, object? eventArgs = null)
        {
            return DispatchActions(uid, actionsComponent, a => initiationType.IsInstanceOfType(a.Initiation), eventArgs);
        }

        private int DispatchActions(EntityUid uid, QlippothActionsComponent actionsComponent, Func<QlippothAction, bool> select, object? eventArgs)
        {
            var now = _timing.CurTime;
            List<QlippothAction> triggeredActions = new();
            foreach (var action in actionsComponent.Actions)
            {
                if (!select(action))
                    continue;
                if (!action.Initiation.Matches(uid, this, eventArgs))
                    continue;
                if (!StateMatches(actionsComponent, action))
                    continue;
                if (action.Cooldown > 0f && now < action.NextReadyAt)
                    continue;
                if (action.Chance < 1f && !_random.Prob(action.Chance))
                    continue;
                if (action.MaxFires > 0 && action.TimesFired >= action.MaxFires)
                    continue;

                action.NextReadyAt = now + TimeSpan.FromSeconds(action.Cooldown);
                action.TimesFired++;
                triggeredActions.Add(action);
            }

            if (triggeredActions.Count > 0)
                _resultSystem.ExecuteResults(uid, triggeredActions, eventArgs);
            return triggeredActions.Count;
        }

        public static bool StateMatches(QlippothActionsComponent actionsComponent, QlippothAction action)
        {
            foreach (var (key, required) in action.RequireState)
            {
                if (!actionsComponent.State.TryGetValue(key, out var current) || current != required)
                    return false;
            }
            foreach (var (key, forbidden) in action.ForbidState)
            {
                if (actionsComponent.State.TryGetValue(key, out var current) && current == forbidden)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Entry point for other Qlippoth systems (corruption, containment, gates...) that detect a condition themselves
        /// and want the matching actions to run. Does nothing if the entity has no QlippothActionsComponent.
        /// </summary>
        public int Dispatch<TInitiation>(EntityUid uid, object? eventArgs = null) where TInitiation : QlippothInitiation
        {
            if (!TryComp<QlippothActionsComponent>(uid, out var actionsComponent))
                return 0;
            return DispatchActions(uid, actionsComponent, typeof(TInitiation), eventArgs);
        }

        /// <summary>Dispatch an initiation type on every Qlippoth in the game (station-wide events).</summary>
        public void DispatchAll<TInitiation>(object? eventArgs = null) where TInitiation : QlippothInitiation
        {
            var query = EntityQueryEnumerator<QlippothActionsComponent>();
            while (query.MoveNext(out var uid, out var actionsComponent))
                DispatchActions(uid, actionsComponent, typeof(TInitiation), eventArgs);
        }

        /// <summary>Fire a single specific action (used by polled initiations). Honors state, cooldown, chance.</summary>
        public int Fire(EntityUid uid, QlippothActionsComponent actions, QlippothAction action, object? eventArgs)
        {
            return DispatchActions(uid, actions, a => a == action, eventArgs);
        }

        /// <summary>Common gate for TimedInitiation ticks: held / contained / chance / max fires.</summary>
        public bool TimedGate(EntityUid uid, QlippothActionsComponent actions, TimedInitiation timed)
        {
            if (timed.MaxFires > 0 && timed.Fired >= timed.MaxFires)
                return false;

            if (timed.OnlyWhileHeld)
            {
                var holder = actions.Holder ?? actions.Wearer;
                if (holder == null || !Exists(holder.Value))
                    return false;
            }

            if (timed.OnlyWhileContained != null && IsContained(uid) != timed.OnlyWhileContained.Value)
                return false;

            return timed.Chance >= 1f || _random.Prob(timed.Chance);
        }
        #endregion

        #region Query helpers for initiation data classes
        /// <summary>Component lookup by YAML name, for initiations that filter on "target has X".</summary>
        public bool HasComponentNamed(EntityUid uid, string componentName)
            => QlippothUtil.HasComponentNamed(EntityManager, uid, componentName);

        public bool PassesFilter(EntityUid self, EntityUid target, QlippothTargetFilter? filter)
        {
            if (filter == null)
                return true;
            if (!Exists(target))
                return false;

            if (filter.RequiredComponent != null && !HasComponentNamed(target, filter.RequiredComponent))
                return false;
            foreach (var component in filter.RequiredComponents)
            {
                if (!HasComponentNamed(target, component))
                    return false;
            }
            foreach (var component in filter.ForbiddenComponents)
            {
                if (HasComponentNamed(target, component))
                    return false;
            }
            if (filter.Whitelist != null && !_whitelist.IsWhitelistPass(filter.Whitelist, target))
                return false;
            if (filter.Blacklist != null && _whitelist.IsWhitelistPass(filter.Blacklist, target))
                return false;

            var hasMobState = HasComp<MobStateComponent>(target);
            if (filter.OnlyMobs && !hasMobState)
                return false;
            if (filter.OnlyAlive && !(hasMobState && _mobState.IsAlive(target)))
                return false;
            if (filter.OnlyDead && !(hasMobState && _mobState.IsDead(target)))
                return false;
            if (filter.OnlyPlayers && !(TryComp<MindContainerComponent>(target, out var mindContainer) && mindContainer.HasMind))
                return false;

            if (filter.MinSanity != null || filter.MaxSanity != null)
            {
                var sanity = GetSanity(target);
                if (sanity == null)
                    return false;
                if (filter.MinSanity != null && sanity.Value < filter.MinSanity.Value)
                    return false;
                if (filter.MaxSanity != null && sanity.Value > filter.MaxSanity.Value)
                    return false;
            }

            if (filter.Corrupted != null && HasComp<QlippothCorruptionComponent>(target) != filter.Corrupted.Value)
                return false;
            if (filter.MinDamage != null && TotalDamage(target) < filter.MinDamage.Value)
                return false;
            if (filter.Anchored != null && IsAnchored(target) != filter.Anchored.Value)
                return false;
            if (filter.ExcludeQlippoths && HasComp<QlippothComponent>(target))
                return false;
            if (filter.ExcludeOffspring && TryComp<QlippothOffspringComponent>(target, out var offspring) && offspring.Parent == self)
                return false;
            if (filter.ExcludeHolder && IsHolderOf(self, target))
                return false;

            return true;
        }

        /// <summary>
        /// Entities within range of the Qlippoth (same map), sorted nearest first, with a required component and filter.
        /// requiredComponent null/empty = any entity. maxCount 0 = all.
        /// </summary>
        public IEnumerable<(EntityUid Uid, float Distance)> EntitiesInRange(EntityUid uid, float range, string? requiredComponent, QlippothTargetFilter? filter, int maxCount, bool includeSelf = false)
        {
            var xform = Transform(uid);
            var origin = _transform.GetWorldPosition(xform);
            var found = new List<(EntityUid, float)>();

            foreach (var other in _lookup.GetEntitiesInRange(xform.Coordinates, range))
            {
                if (other == uid && !includeSelf)
                    continue;
                if (!string.IsNullOrEmpty(requiredComponent) && !HasComponentNamed(other, requiredComponent))
                    continue;
                if (!PassesFilter(uid, other, filter))
                    continue;

                var distance = (_transform.GetWorldPosition(other) - origin).Length();
                found.Add((other, distance));
            }

            found.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            return maxCount > 0 ? found.Take(maxCount) : found;
        }

        public bool IsHolderOf(EntityUid qlippoth, EntityUid mob)
        {
            return TryComp<QlippothActionsComponent>(qlippoth, out var actions) && (actions.Holder == mob || actions.Wearer == mob);
        }

        public EntityUid? GetHolder(EntityUid qlippoth)
        {
            return TryComp<QlippothActionsComponent>(qlippoth, out var actions) ? actions.Holder ?? actions.Wearer : null;
        }

        public float? GetSanity(EntityUid uid)
        {
            return TryComp<SanityComponent>(uid, out var sanity) ? sanity.CurrentSanity : null;
        }

        public float TotalDamage(EntityUid uid)
        {
            return HasComp<Content.Shared.Damage.Components.DamageableComponent>(uid) ? _damageable.GetTotalDamage(uid).Float() : 0f;
        }

        public bool IsAnchored(EntityUid uid) => Transform(uid).Anchored;

        public bool IsContained(EntityUid uid)
        {
            var query = EntityQueryEnumerator<ContainmentChamberComponent>();
            while (query.MoveNext(out _, out var chamber))
            {
                if (chamber.ContainedQlippoth == uid)
                    return true;
            }
            return false;
        }

        public EntityUid? GetPuller(EntityUid uid)
        {
            return TryComp<PullableComponent>(uid, out var pullable) ? pullable.Puller : null;
        }

        public EntityUid? GetContainerOwner(EntityUid uid)
        {
            return _container.TryGetContainingContainer((uid, null, null), out var container) ? container.Owner : null;
        }

        public float TileGasMoles(EntityUid uid, Gas gas)
        {
            var mixture = _atmosphere.GetContainingMixture(uid);
            return mixture?.GetMoles(gas) ?? 0f;
        }

        public float? TileTemperature(EntityUid uid)
        {
            return _atmosphere.GetContainingMixture(uid)?.Temperature;
        }

        public float TilePressure(EntityUid uid)
        {
            return _atmosphere.GetContainingMixture(uid)?.Pressure ?? 0f;
        }

        public int CountLitLights(EntityUid uid, float range)
        {
            var count = 0;
            var coordinates = Transform(uid).Coordinates;
            foreach (var other in _lookup.GetEntitiesInRange(coordinates, range))
            {
                if (other == uid)
                    continue;
                if (TryComp<PoweredLightComponent>(other, out var powered) && powered.On && powered.CurrentLit)
                    count++;
                else if (!HasComp<PoweredLightComponent>(other) && TryComp<SharedPointLightComponent>(other, out var light) && light.Enabled)
                    count++;
            }
            return count;
        }

        public float RoundSeconds()
        {
            return (float) _gameTicker.RoundDuration().TotalSeconds;
        }

        public void EnsureListener(EntityUid uid, float range)
        {
            var listener = EnsureComp<ActiveListenerComponent>(uid);
            listener.Range = Math.Max(listener.Range, range);
        }

        public void EnsureStepTrigger(EntityUid uid)
        {
            EnsureComp<StepTriggerComponent>(uid);
        }
        #endregion

        #region Holder bookkeeping
        private void AttachHolder(EntityUid uid, EntityUid holder)
        {
            EnsureComp<QlippothHolderComponent>(holder).Held.Add(uid);
        }

        private void DetachHolder(EntityUid uid, EntityUid holder)
        {
            if (!TryComp<QlippothHolderComponent>(holder, out var holderComponent))
                return;
            holderComponent.Held.Remove(uid);
            if (holderComponent.Held.Count == 0)
                RemCompDeferred<QlippothHolderComponent>(holder);
        }
        #endregion

        #region Event Handlers: external
        private void OnActionPressed(EntityUid uid, QlippothActionsComponent component, QlippothActionEvent args)
        {
            if (args.Handled)
                return;

            var eventArgs = new QlippothActionEventArgs(args.Performer, args.Key);
            var fired = DispatchActions(uid, component, a => a.Initiation is OnActionInitiation or OnSelfActionInitiation, eventArgs);
            if (fired == 0)
                return;

            args.Handled = true;
            args.Toggle = args.FlipToggle; // SharedActionsSystem flips the icon after this handler returns
        }

        private void OnAfterInteract(EntityUid uid, QlippothActionsComponent component, AfterInteractEvent args)
        {
            if (args.Handled || !args.CanReach || args.Target is not { } target)
                return;

            if (DispatchActions(uid, component, typeof(OnUsedOnInitiation), new QlippothInteractEventArgs(target, args.User)) > 0)
                args.Handled = true;
        }

        private void OnActivate(EntityUid uid, QlippothActionsComponent component, ActivateInWorldEvent args)
        {
            if (args.Handled)
                return;
            if (DispatchActions(uid, component, typeof(OnActivateInitiation), new QlippothActorEventArgs(args.User, args.User)) > 0)
                args.Handled = true;
        }

        private void OnInteractHand(EntityUid uid, QlippothActionsComponent component, InteractHandEvent args)
        {
            if (args.Handled)
                return;
            if (DispatchActions(uid, component, typeof(OnInteractHandInitiation), new QlippothActorEventArgs(args.User, args.User)) > 0)
                args.Handled = true;
        }

        private void OnUseInHand(EntityUid uid, QlippothActionsComponent component, UseInHandEvent args)
        {
            if (args.Handled)
                return;
            if (DispatchActions(uid, component, typeof(OnUseInHandInitiation), new QlippothHolderEventArgs(args.User)) > 0)
                args.Handled = true;
        }

        private void OnInteractUsing(EntityUid uid, QlippothActionsComponent component, InteractUsingEvent args)
        {
            if (args.Handled)
                return;

            var eventArgs = new QlippothUsedEventArgs(args.Used, args.User);
            var consume = false;
            var fired = DispatchActions(uid, component, a =>
            {
                if (a.Initiation is not OnInteractUsingInitiation initiation)
                    return false;
                consume |= initiation.ConsumeUsed;
                return true;
            }, eventArgs);

            if (fired == 0)
                return;

            args.Handled = true;
            if (consume && Exists(args.Used))
                QueueDel(args.Used);
        }

        private void OnAttacked(EntityUid uid, QlippothActionsComponent component, AttackedEvent args)
        {
            DispatchActions(uid, component, typeof(OnAttackedInitiation), new QlippothAttackedEventArgs(args.User, args.Used, args));
        }

        private void OnEquippedHand(EntityUid uid, QlippothActionsComponent component, GotEquippedHandEvent args)
        {
            component.Holder = args.User;
            AttachHolder(uid, args.User);
            DispatchActions(uid, component, typeof(OnPickedUpInitiation), new QlippothHolderEventArgs(args.User));
        }

        private void OnUnequippedHand(EntityUid uid, QlippothActionsComponent component, GotUnequippedHandEvent args)
        {
            if (component.Holder == args.User)
                component.Holder = null;
            if (component.Wearer != args.User)
                DetachHolder(uid, args.User);

            DispatchActions(uid, component, typeof(OnDroppedInitiation), new QlippothHolderEventArgs(args.User));
        }

        private void OnEquipped(EntityUid uid, QlippothActionsComponent component, GotEquippedEvent args)
        {
            component.Wearer = args.EquipTarget;
            AttachHolder(uid, args.EquipTarget);
            DispatchActions(uid, component, typeof(OnEquippedInitiation), new QlippothEquipEventArgs(args.EquipTarget, args.Slot));
        }

        private void OnUnequipped(EntityUid uid, QlippothActionsComponent component, GotUnequippedEvent args)
        {
            if (component.Wearer == args.EquipTarget)
                component.Wearer = null;
            if (component.Holder != args.EquipTarget)
                DetachHolder(uid, args.EquipTarget);

            DispatchActions(uid, component, typeof(OnUnequippedInitiation), new QlippothEquipEventArgs(args.EquipTarget, args.Slot));
        }

        private void OnThrown(EntityUid uid, QlippothActionsComponent component, ref ThrownEvent args)
        {
            DispatchActions(uid, component, typeof(OnThrownInitiation), args.User is { } user ? new QlippothHolderEventArgs(user) : null);
        }

        private void OnLand(EntityUid uid, QlippothActionsComponent component, ref LandEvent args)
        {
            DispatchActions(uid, component, typeof(OnLandInitiation), args.User is { } user ? new QlippothHolderEventArgs(user) : null);
        }

        private void OnThrowHit(EntityUid uid, QlippothActionsComponent component, ref ThrowDoHitEvent args)
        {
            object eventArgs = args.Component.Thrower is { } thrower
                ? new QlippothActorEventArgs(args.Target, thrower)
                : new QlippothTargetEventArgs(args.Target);
            DispatchActions(uid, component, typeof(OnThrowHitInitiation), eventArgs);
        }

        private void OnListen(EntityUid uid, QlippothActionsComponent component, ListenEvent args)
        {
            DispatchActions(uid, component, typeof(OnHeardInitiation), new QlippothSpeechEventArgs(args.Source, args.Message));
        }

        private void OnGotInserted(EntityUid uid, QlippothActionsComponent component, EntGotInsertedIntoContainerMessage args)
        {
            DispatchActions(uid, component, typeof(OnInsertedIntoContainerInitiation), new QlippothContainerEventArgs(args.Container.Owner, args.Container.ID));
        }

        private void OnGotRemoved(EntityUid uid, QlippothActionsComponent component, EntGotRemovedFromContainerMessage args)
        {
            DispatchActions(uid, component, typeof(OnRemovedFromContainerInitiation), new QlippothContainerEventArgs(args.Container.Owner, args.Container.ID));
        }

        private void OnEntityInserted(EntityUid uid, QlippothActionsComponent component, EntInsertedIntoContainerMessage args)
        {
            DispatchActions(uid, component, typeof(OnEntityInsertedInitiation), new QlippothContainerEventArgs(args.Entity, args.Container.ID));
        }

        private void OnEntityRemoved(EntityUid uid, QlippothActionsComponent component, EntRemovedFromContainerMessage args)
        {
            DispatchActions(uid, component, typeof(OnEntityRemovedInitiation), new QlippothContainerEventArgs(args.Entity, args.Container.ID));
        }
        #endregion

        #region Event Handlers: trigger
        private void OnPullStarted(EntityUid uid, QlippothActionsComponent component, PullStartedMessage eventArgs)
        {
            // Wrap the SS14 event so targeted results act on the puller rather than falling back to the Qlippoth.
            DispatchActions(uid, component, typeof(OnPullInitiation), new QlippothPullEventArgs(eventArgs.PullerUid));
        }

        private void OnPullStopped(EntityUid uid, QlippothActionsComponent component, PullStoppedMessage eventArgs)
        {
            DispatchActions(uid, component, typeof(OnPullStoppedInitiation), new QlippothPullEventArgs(eventArgs.PullerUid));
        }

        private void OnMeleeHit(EntityUid uid, QlippothActionsComponent component, MeleeHitEvent args)
        {
            var target = args.HitEntities.Count > 0 ? args.HitEntities[0] : uid;
            var eventArgs = new QlippothMeleeHitEventArgs(target, args.User, args);
            DispatchActions(uid, component, a => a.Initiation is OnMeleeHitInitiation or OnSelfMeleeHitInitiation, eventArgs);
        }

#pragma warning disable CS0618 // DamageChangedEvent is obsolete upstream; see QlippothDamagedEventArgs for why we still use it.
        private void OnDamaged(EntityUid uid, QlippothActionsComponent component, DamageChangedEvent args)
        {
            var target = args.Origin is { } origin && Exists(origin) ? origin : uid;
            DispatchActions(uid, component, typeof(OnDamagedInitiation), new QlippothDamagedEventArgs(target, args));
        }
#pragma warning restore CS0618

        private void OnMobStateChanged(EntityUid uid, QlippothActionsComponent component, MobStateChangedEvent args)
        {
            DispatchActions(uid, component, typeof(OnMobStateInitiation), new QlippothMobStateEventArgs(uid, args.OldMobState, args.NewMobState));
        }

        private void OnCollide(EntityUid uid, QlippothActionsComponent component, ref StartCollideEvent args)
        {
            DispatchActions(uid, component, typeof(OnCollideInitiation), new QlippothTargetEventArgs(args.OtherEntity));
        }

        private void OnStepTriggered(EntityUid uid, QlippothActionsComponent component, ref StepTriggeredOffEvent args)
        {
            DispatchActions(uid, component, typeof(OnStepTriggerInitiation), new QlippothTargetEventArgs(args.Tripper));
        }

        private void OnAnchorChanged(EntityUid uid, QlippothActionsComponent component, ref AnchorStateChangedEvent args)
        {
            DispatchActions(uid, component, typeof(OnAnchorChangedInitiation), null);
        }

        private void OnPowerChanged(EntityUid uid, QlippothActionsComponent component, ref PowerChangedEvent args)
        {
            var powered = args.Powered;
            foreach (var action in component.Actions)
            {
                if (action.Initiation is OnPowerChangedInitiation initiation)
                    initiation.LastPowered = powered;
            }
            DispatchActions(uid, component, typeof(OnPowerChangedInitiation), null);
        }
        #endregion

        #region Event Handlers: internal
        private void OnSpoke(EntityUid uid, QlippothActionsComponent component, EntitySpokeEvent args)
        {
            DispatchActions(uid, component, typeof(OnSpokeInitiation), new QlippothSpeechEventArgs(uid, args.Message));
        }

        private void OnEmote(EntityUid uid, QlippothActionsComponent component, ref EmoteEvent args)
        {
            DispatchActions(uid, component, typeof(OnEmoteInitiation), new QlippothEmoteEventArgs(uid, args.Emote.ID));
        }

        private void OnMindAdded(EntityUid uid, QlippothActionsComponent component, MindAddedMessage args)
        {
            DispatchActions(uid, component, typeof(OnMindAddedInitiation), new QlippothTargetEventArgs(uid));
        }

        private void OnMindRemoved(EntityUid uid, QlippothActionsComponent component, MindRemovedMessage args)
        {
            DispatchActions(uid, component, typeof(OnMindRemovedInitiation), new QlippothTargetEventArgs(uid));
        }
        #endregion

        #region Event Handlers: interface
        private void OnGetActivationVerbs(EntityUid uid, QlippothActionsComponent component, GetVerbsEvent<ActivationVerb> args)
        {
            AddVerbs<ActivationVerb>(uid, component, args.User, args.CanAccess, args.CanInteract, alternative: false, verb => args.Verbs.Add(verb));
        }

        private void OnGetAlternativeVerbs(EntityUid uid, QlippothActionsComponent component, GetVerbsEvent<AlternativeVerb> args)
        {
            AddVerbs<AlternativeVerb>(uid, component, args.User, args.CanAccess, args.CanInteract, alternative: true, verb => args.Verbs.Add(verb));
        }

        private void AddVerbs<TVerb>(EntityUid uid, QlippothActionsComponent component, EntityUid user, bool canAccess, bool canInteract, bool alternative, Action<TVerb> add)
            where TVerb : Verb, new()
        {
            foreach (var action in component.Actions)
            {
                if (action.Initiation is not VerbInitiation initiation || initiation.Alternative != alternative)
                    continue;
                if (initiation.RequireInteract && (!canAccess || !canInteract))
                    continue;
                if (!StateMatches(component, action))
                    continue;
                if (!PassesFilter(uid, user, initiation.UserFilter))
                    continue;

                var show = true;
                foreach (var (key, value) in initiation.ShowWhenState)
                {
                    if (!component.State.TryGetValue(key, out var current) || current != value)
                        show = false;
                }
                if (!show)
                    continue;

                var verbKey = initiation.Key;
                var verb = new TVerb
                {
                    Text = Loc.GetString(initiation.Text),
                    Category = QlippothVerbCategory,
                    Priority = initiation.Priority,
                    Act = () =>
                    {
                        if (TryComp<QlippothActionsComponent>(uid, out var current))
                            DispatchActions(uid, current, typeof(VerbInitiation), new QlippothVerbEventArgs(user, verbKey));
                    },
                };
                add(verb);
            }
        }

        private void OnExamined(EntityUid uid, QlippothActionsComponent component, ExaminedEvent args)
        {
            DispatchActions(uid, component, typeof(OnExaminedInitiation), new QlippothExamineEventArgs(args.Examiner, args));
        }
        #endregion

        #region Event Handlers: holder relays
        /// <summary>Damage about to land on someone holding Qlippoths: give each held Qlippoth a chance to react.</summary>
        private void OnHolderDamageModify(EntityUid holderUid, QlippothHolderComponent holder, DamageModifyEvent args)
        {
            foreach (var held in holder.Held.ToArray())
            {
                if (TryComp<QlippothActionsComponent>(held, out var component))
                    DispatchActions(held, component, typeof(OnHolderDamagedInitiation), new QlippothHolderDamagedEventArgs(holderUid, args));
            }
        }

        private void OnHolderMobStateChanged(EntityUid holderUid, QlippothHolderComponent holder, MobStateChangedEvent args)
        {
            foreach (var held in holder.Held.ToArray())
            {
                if (TryComp<QlippothActionsComponent>(held, out var component))
                    DispatchActions(held, component, typeof(OnHolderMobStateInitiation), new QlippothMobStateEventArgs(holderUid, args.OldMobState, args.NewMobState));
            }
        }

        private void OnHolderEmote(EntityUid holderUid, QlippothHolderComponent holder, ref EmoteEvent args)
        {
            foreach (var held in holder.Held.ToArray())
            {
                if (TryComp<QlippothActionsComponent>(held, out var component))
                    DispatchActions(held, component, typeof(OnEmoteInitiation), new QlippothEmoteEventArgs(holderUid, args.Emote.ID));
            }
        }
        #endregion

        #region Event Handlers: offspring
        private void OnOffspringMobState(EntityUid uid, QlippothOffspringComponent offspring, MobStateChangedEvent args)
        {
            if (args.NewMobState != MobState.Dead)
                return;
            NotifyOffspringDied(uid, offspring, args.OldMobState);
        }

        private void OnOffspringTerminating(EntityUid uid, QlippothOffspringComponent offspring, ref EntityTerminatingEvent args)
        {
            if (offspring.Reported)
                return;
            NotifyOffspringDied(uid, offspring, MobState.Alive);
        }

        private void NotifyOffspringDied(EntityUid uid, QlippothOffspringComponent offspring, MobState oldState)
        {
            offspring.Reported = true;
            if (!Exists(offspring.Parent) || !TryComp<QlippothActionsComponent>(offspring.Parent, out var parent))
                return;
            parent.Offspring.Remove(uid);
            DispatchActions(offspring.Parent, parent, typeof(OnOffspringDiedInitiation), new QlippothMobStateEventArgs(uid, oldState, MobState.Dead));
        }
        #endregion

        #region Event Handlers: event based (broadcast)
        private void OnGameRuleStarted(ref GameRuleStartedEvent args)
        {
            DispatchAll<OnGameRuleStartedInitiation>(new QlippothGameRuleEventArgs(args.RuleEntity, args.RuleId));
        }

        private void OnGameRuleEnded(ref GameRuleEndedEvent args)
        {
            DispatchAll<OnGameRuleEndedInitiation>(new QlippothGameRuleEventArgs(args.RuleEntity, args.RuleId));
        }

        private void OnAlertLevelChanged(AlertLevelChangedEvent args)
        {
            DispatchAll<OnAlertLevelInitiation>(new QlippothAlertLevelEventArgs(args.Station, args.AlertLevel));
        }

        private void OnRoundEndSystemChanged(RoundEndSystemChangedEvent args)
        {
            var called = _roundEnd.IsRoundEndRequested();
            var query = EntityQueryEnumerator<QlippothActionsComponent>();
            while (query.MoveNext(out var uid, out var component))
            {
                foreach (var action in component.Actions)
                {
                    if (action.Initiation is OnShuttleCalledInitiation initiation)
                        initiation.LastCalled = called;
                }
                DispatchActions(uid, component, typeof(OnShuttleCalledInitiation), null);
            }
        }

        private void OnRoundEnd(RoundEndTextAppendEvent args)
        {
            DispatchAll<OnRoundEndInitiation>();
        }

        #endregion
    }
}
