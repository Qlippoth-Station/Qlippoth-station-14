using System.Linq;
using Content.Server.Qlippoth.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Qlippoth;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Listens to SS14 events and forwards matching Qlippoth actions to QlippothActionResultSystem.
    /// Each initiation type has its own subscriber below.
    /// </summary>
    public sealed partial class QlippothActionInitiationSystem : EntitySystem
    {
        [Dependency] private QlippothActionResultSystem _resultSystem = default!;
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private EntityLookupSystem _lookup = default!;
        [Dependency] private SharedTransformSystem _transform = default!;

        /// <summary>
        /// Actual subscriptions to events, happen here.
        /// Initiation components just decide which subscriptions apply to that specific Qlippoth.
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<QlippothActionsComponent, PullStartedMessage>(OnPullStarted);
            SubscribeLocalEvent<QlippothActionsComponent, QlippothActionEvent>(OnActionPressed);
            SubscribeLocalEvent<QlippothActionsComponent, AfterInteractEvent>(OnAfterInteract);
            SubscribeLocalEvent<QlippothActionsComponent, GotEquippedHandEvent>(OnEquippedHand);
            SubscribeLocalEvent<QlippothActionsComponent, GotUnequippedHandEvent>(OnUnequippedHand);
            SubscribeLocalEvent<QlippothActionsComponent, MeleeHitEvent>(OnMeleeHit);

            SubscribeLocalEvent<QlippothHolderComponent, DamageModifyEvent>(OnHolderDamageModify);
        }

        /// <summary>Ticks every TimedInitiation.</summary>
        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var now = _timing.CurTime;
            var query = EntityQueryEnumerator<QlippothActionsComponent>();
            while (query.MoveNext(out var uid, out var actionsComponent))
            {
                foreach (var action in actionsComponent.Actions)
                {
                    if (action.Initiation is not TimedInitiation timed || now < timed.NextFireAt)
                        continue;

                    timed.NextFireAt = now + TimeSpan.FromSeconds(timed.Interval);

                    if (timed is ProximityInitiation proximity)
                    {
                        TickProximity(uid, actionsComponent, action, proximity);
                        continue;
                    }

                    var holder = actionsComponent.Holder;
                    if (timed.OnlyWhileHeld && (holder == null || !Exists(holder.Value)))
                        continue;

                    var eventArgs = holder != null ? new QlippothHolderEventArgs(holder.Value) : null;
                    DispatchActions(uid, actionsComponent, a => a == action, eventArgs);
                }
            }
        }

        /// <summary>One aura tick: fire the action once per qualifying entity in range.</summary>
        private void TickProximity(EntityUid uid, QlippothActionsComponent actionsComponent, QlippothAction action, ProximityInitiation proximity)
        {
            if (proximity.OnlyWhileHeld && actionsComponent.Holder == null)
                return;

            var xform = Transform(uid);
            var origin = _transform.GetWorldPosition(xform);
            foreach (var other in _lookup.GetEntitiesInRange(xform.Coordinates, proximity.Range))
            {
                if (other == uid || !HasComponentNamed(other, proximity.RequiredComponent))
                    continue;

                var distance = (_transform.GetWorldPosition(other) - origin).Length();
                DispatchActions(uid, actionsComponent, a => a == action, new QlippothProximityEventArgs(other, distance));
            }
        }

        /// <summary>
        /// Detects the right action to initiate and relays actions to Result System.
        /// An action fires when: its initiation is of the requested type, the initiation's Matches() accepts the eventArgs,
        /// the Qlippoth's state satisfies the action's requireState, and the action is off cooldown.
        /// Matching actions are collected first and executed afterwards, so a result changing state does not affect
        /// which actions fire for this event.
        /// Returns how many actions fired.
        /// </summary>
        private int DispatchActions(EntityUid uid, QlippothActionsComponent actionsComponent, System.Type initiationType, object? eventArgs = null)
        {
            // input the lowest level class in handlers so it doesnt get confused
            return DispatchActions(uid, actionsComponent, a => a.Initiation.GetType() == initiationType, eventArgs);
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

                action.NextReadyAt = now + TimeSpan.FromSeconds(action.Cooldown);
                triggeredActions.Add(action);
            }

            if (triggeredActions.Count > 0)
                _resultSystem.ExecuteResults(uid, triggeredActions, eventArgs);
            return triggeredActions.Count;
        }

        private static bool StateMatches(QlippothActionsComponent actionsComponent, QlippothAction action)
        {
            foreach (var (key, required) in action.RequireState)
            {
                if (!actionsComponent.State.TryGetValue(key, out var current) || current != required)
                    return false;
            }
            return true;
        }

        /// <summary>Component lookup by YAML name, for initiations that filter on "target has X".</summary>
        public bool HasComponentNamed(EntityUid uid, string componentName)
        {
            return EntityManager.ComponentFactory.TryGetRegistration(componentName, out var registration)
                   && EntityManager.HasComponent(uid, registration.Type);
        }

        #region Event Handlers
        private void OnPullStarted(EntityUid uid, QlippothActionsComponent component, PullStartedMessage eventArgs)
        {
            // Wrap the SS14 event so targeted results act on the puller rather than falling back to the Qlippoth.
            DispatchActions(uid, component, typeof(OnPullInitiation), new QlippothPullEventArgs(eventArgs.PullerUid));
        }

        private void OnActionPressed(EntityUid uid, QlippothActionsComponent component, QlippothActionEvent args)
        {
            if (args.Handled)
                return;

            var fired = DispatchActions(uid, component, typeof(OnActionInitiation), new QlippothActionEventArgs(args.Performer, args.Key));
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

        private void OnEquippedHand(EntityUid uid, QlippothActionsComponent component, GotEquippedHandEvent args)
        {
            component.Holder = args.User;
            EnsureComp<QlippothHolderComponent>(args.User).Held.Add(uid);

            DispatchActions(uid, component, typeof(OnPickedUpInitiation), new QlippothHolderEventArgs(args.User));
        }

        private void OnUnequippedHand(EntityUid uid, QlippothActionsComponent component, GotUnequippedHandEvent args)
        {
            if (component.Holder == args.User)
                component.Holder = null;

            if (TryComp<QlippothHolderComponent>(args.User, out var holder))
            {
                holder.Held.Remove(uid);
                if (holder.Held.Count == 0)
                    RemCompDeferred<QlippothHolderComponent>(args.User);
            }

            DispatchActions(uid, component, typeof(OnDroppedInitiation), new QlippothHolderEventArgs(args.User));
        }

        private void OnMeleeHit(EntityUid uid, QlippothActionsComponent component, MeleeHitEvent args)
        {
            var target = args.HitEntities.Count > 0 ? args.HitEntities[0] : uid;
            DispatchActions(uid, component, typeof(OnMeleeHitInitiation), new QlippothMeleeHitEventArgs(target, args.User, args));
        }

        /// <summary>Damage about to land on someone holding Qlippoths: give each held Qlippoth a chance to react.</summary>
        private void OnHolderDamageModify(EntityUid holderUid, QlippothHolderComponent holder, DamageModifyEvent args)
        {
            foreach (var held in holder.Held.ToArray())
            {
                if (TryComp<QlippothActionsComponent>(held, out var component))
                    DispatchActions(held, component, typeof(OnHolderDamagedInitiation), new QlippothHolderDamagedEventArgs(holderUid, args));
            }
        }
        #endregion

        #region External Dispatch
        /// <summary>
        /// Entry point for other Qlippoth systems (corruption, containment, gates...) that detect a condition themselves
        /// and want the matching actions to run. Does nothing if the entity has no QlippothActionsComponent.
        /// </summary>
        public void Dispatch<TInitiation>(EntityUid uid, object? eventArgs = null) where TInitiation : QlippothInitiation
        {
            if (!TryComp<QlippothActionsComponent>(uid, out var actionsComponent))
                return;
            DispatchActions(uid, actionsComponent, typeof(TInitiation), eventArgs);
        }
        #endregion

        #region Qlippoth Specific Event Definitions
        #endregion
    }
}
