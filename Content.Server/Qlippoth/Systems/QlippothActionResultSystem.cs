using Content.Server.Atmos.EntitySystems;   // AtmosphereSystem
using Content.Server.Popups;                // PopupSystem
using Content.Server.Qlippoth.Systems;      // SanitySystem
using Content.Shared.Doors;                 // door events
using Content.Shared.Doors.Components;      // DoorComponent, DoorBoltComponent
using Content.Shared.Doors.Systems;         // SharedDoorSystem
using Content.Shared.Pinpointer;            // SharedPinpointerSystem
using Robust.Shared.GameObjects;            // SharedTransformSystem, EntityLookupSystem
using Robust.Shared.Audio.Systems;          // SharedAudioSystem
using Robust.Shared.Random;                 // IRobustRandom
using Robust.Shared.Timing;                 // IGameTiming

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Executes the results of Qlippoth actions.
    /// Called by QlippothActionInitiationSystem when an initiation condition is met.
    ///
    /// Results themselves are one-shot (Execute runs once). Anything that has to keep going after that
    /// (a door held for 15 s, an effect that expires...) lives here as a "lasting effect" region:
    /// a marker component on the affected entity, the event subscriptions that enforce it, and the expiry in Update().
    /// </summary>
    public sealed partial class QlippothActionResultSystem : EntitySystem
    {
        #region dependencies
        [Dependency] public SharedAudioSystem Audio = default!;
        [Dependency] public AtmosphereSystem Atmosphere = default!;
        [Dependency] public SharedTransformSystem QlippothTransform = default!;
        [Dependency] public IEntityManager QlippothEntityManager = default!;
        [Dependency] public PopupSystem Popup = default!;
        [Dependency] public SanitySystem Sanity = default!;
        [Dependency] public CorruptionSystem Corruption = default!;
        [Dependency] public EntityLookupSystem Lookup = default!;
        [Dependency] public SharedPinpointerSystem Pinpointer = default!;
        [Dependency] public SharedDoorSystem Door = default!;
        [Dependency] public IGameTiming Timing = default!;
        [Dependency] public IRobustRandom Random = default!;
        #endregion

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<QlippothDoorHoldComponent, BeforeDoorOpenedEvent>(OnHeldDoorBeforeOpen);
            SubscribeLocalEvent<QlippothDoorHoldComponent, BeforeDoorClosedEvent>(OnHeldDoorBeforeClose);
            SubscribeLocalEvent<QlippothDoorHoldComponent, BeforeDoorAutoCloseEvent>(OnHeldDoorBeforeAutoClose);
            SubscribeLocalEvent<QlippothDoorHoldComponent, DoorStateChangedEvent>(OnHeldDoorStateChanged);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);
            ExpireDoorHolds();
        }

        #region helpers for results
        /// <summary>
        /// The entity a targeted result should act on: the eventArgs target if the initiation supplied one, otherwise the Qlippoth itself.
        /// </summary>
        public EntityUid ResolveTarget(EntityUid uid, object? eventArgs)
        {
            return eventArgs is IQlippothTargetedEventArgs targeted ? targeted.Target : uid;
        }

        /// <summary>
        /// The mob that caused the initiation (holder, user, action performer), if the initiation knows one.
        /// </summary>
        public EntityUid? ResolveActor(EntityUid uid, object? eventArgs)
        {
            return eventArgs is IQlippothActorEventArgs actor ? actor.Actor : null;
        }

        public string EntityName(EntityUid uid)
        {
            return QlippothEntityManager.TryGetComponent<MetaDataComponent>(uid, out var meta) ? meta.EntityName : string.Empty;
        }

        /// <summary>Set a key in the Qlippoth's state dictionary (see QlippothAction.RequireState).</summary>
        public void SetState(EntityUid uid, string key, string value)
        {
            if (TryComp<QlippothActionsComponent>(uid, out var actions))
                actions.State[key] = value;
        }

        public string? GetState(EntityUid uid, string key)
        {
            return TryComp<QlippothActionsComponent>(uid, out var actions) && actions.State.TryGetValue(key, out var value) ? value : null;
        }
        #endregion

        /// <summary>
        /// Called from Initiation system, applies results of initiated actions.
        /// Results run in order; if one returns false (it could not do its job) the rest of that action's results are skipped
        /// unless the action sets continueOnFailure.
        /// </summary>
        public void ExecuteResults(EntityUid uid, List<QlippothAction> actions, object? eventArgs = null)
        {
            foreach (var action in actions)
            {
                foreach (var result in action.Results)
                {
                    if (!result.Execute(uid, this, eventArgs) && !action.ContinueOnFailure)
                        break;
                }
            }
        }

        #region lasting effect: door hold
        // Used by OpenDoorResult / HoldDoorsInRangeResult. A held door carries QlippothDoorHoldComponent;
        // the subscriptions below refuse open/close attempts against the hold, ExpireDoorHolds() clears it.

        /// <summary>
        /// Start (or refresh) a hold on a door. Sealed: close + bolt. Released: unbolt + open.
        /// Returns false for non-doors and welded doors, which cannot be held.
        /// </summary>
        public bool HoldDoor(EntityUid doorUid, QlippothDoorHoldMode mode, float durationSeconds, EntityUid? user = null)
        {
            if (!TryComp<DoorComponent>(doorUid, out var door) || door.State == DoorState.Welded)
                return false;

            var isNew = !HasComp<QlippothDoorHoldComponent>(doorUid);
            var hold = EnsureComp<QlippothDoorHoldComponent>(doorUid);
            if (isNew)
                hold.WasBolted = TryComp<DoorBoltComponent>(doorUid, out var bolt) && bolt.BoltsDown;

            hold.Mode = mode;
            hold.Until = Timing.CurTime + TimeSpan.FromSeconds(durationSeconds);

            switch (mode)
            {
                case QlippothDoorHoldMode.Sealed:
                    if (door.State is DoorState.Open or DoorState.Opening)
                        Door.StartClosing(doorUid, door, user); // bolts drop once it reports Closed (OnHeldDoorStateChanged)
                    else if (door.State is DoorState.Closed or DoorState.Denying)
                        SetDoorBolts(doorUid, true);
                    break;
                case QlippothDoorHoldMode.Released:
                    SetDoorBolts(doorUid, false);
                    if (door.State is DoorState.Closed or DoorState.Closing or DoorState.Denying)
                        Door.StartOpening(doorUid, door, user);
                    break;
            }

            return true;
        }

        /// <summary>Unbolt and open a door regardless of access. Returns false for non-doors and welded doors.</summary>
        public bool ForceOpenDoor(EntityUid doorUid, EntityUid? user = null)
        {
            if (!TryComp<DoorComponent>(doorUid, out var door) || door.State == DoorState.Welded)
                return false;

            SetDoorBolts(doorUid, false);
            if (door.State is DoorState.Closed or DoorState.Denying)
                Door.StartOpening(doorUid, door, user);
            return true;
        }

        public void SetDoorBolts(EntityUid doorUid, bool down)
        {
            if (TryComp<DoorBoltComponent>(doorUid, out var bolt))
                Door.SetBoltsDown((doorUid, bolt), down);
        }

        private void ExpireDoorHolds()
        {
            var now = Timing.CurTime;
            var query = EntityQueryEnumerator<QlippothDoorHoldComponent, DoorComponent>();
            while (query.MoveNext(out var uid, out var hold, out var door))
            {
                if (now < hold.Until)
                    continue;

                var mode = hold.Mode;
                var wasBolted = hold.WasBolted;
                RemComp<QlippothDoorHoldComponent>(uid);

                switch (mode)
                {
                    case QlippothDoorHoldMode.Sealed when !wasBolted:
                        SetDoorBolts(uid, false);
                        break;
                    case QlippothDoorHoldMode.Released when door.State == DoorState.Open:
                        Door.TryClose(uid, door); // safety-checked; stays open if someone is in the way
                        break;
                }
            }
        }

        private void OnHeldDoorBeforeOpen(EntityUid uid, QlippothDoorHoldComponent hold, BeforeDoorOpenedEvent args)
        {
            if (hold.Mode == QlippothDoorHoldMode.Sealed)
                args.Cancel();
        }

        private void OnHeldDoorBeforeClose(EntityUid uid, QlippothDoorHoldComponent hold, BeforeDoorClosedEvent args)
        {
            if (hold.Mode == QlippothDoorHoldMode.Released)
                args.Cancel();
        }

        private void OnHeldDoorBeforeAutoClose(EntityUid uid, QlippothDoorHoldComponent hold, BeforeDoorAutoCloseEvent args)
        {
            if (hold.Mode == QlippothDoorHoldMode.Released)
                args.Cancel();
        }

        private void OnHeldDoorStateChanged(EntityUid uid, QlippothDoorHoldComponent hold, DoorStateChangedEvent args)
        {
            if (hold.Mode == QlippothDoorHoldMode.Sealed && args.State == DoorState.Closed)
                SetDoorBolts(uid, true);
        }
        #endregion
    }
}
