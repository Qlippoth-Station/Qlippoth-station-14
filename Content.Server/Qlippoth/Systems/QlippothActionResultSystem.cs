using System.Linq;
using System.Numerics;
using Content.Server.AlertLevel;
using Content.Server.Atmos.EntitySystems;   // AtmosphereSystem, FlammableSystem
using Content.Server.Body.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Electrocution;
using Content.Server.Emp;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Flash;
using Content.Server.Fluids.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Mind;
using Content.Server.Polymorph.Systems;
using Content.Server.Popups;                // PopupSystem
using Content.Server.Qlippoth.Systems;      // SanitySystem, CorruptionSystem, QGateSystem
using Content.Server.RoundEnd;
using Content.Server.Station.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Atmos;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors;                 // door events
using Content.Shared.Doors.Components;      // DoorComponent, DoorBoltComponent
using Content.Shared.Doors.Systems;         // SharedDoorSystem
using Content.Shared.Gibbing;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Jittering;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Pinpointer;            // SharedPinpointerSystem
using Content.Shared.Qlippoth.Components;
using Content.Shared.Station.Components;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;          // SharedAudioSystem
using Robust.Shared.GameObjects;            // SharedTransformSystem, EntityLookupSystem
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;                 // IRobustRandom
using Robust.Shared.Timing;                 // IGameTiming

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Executes the results of Qlippoth actions.
    /// Called by QlippothActionInitiationSystem when an initiation condition is met.
    ///
    /// Results themselves are one-shot (Execute runs once). Anything that has to keep going after that
    /// (a door held for 15 s, a speed debuff that expires, a chain scheduled for later...) lives here as a "lasting effect" region:
    /// a marker component on the affected entity or a queue on the system, the event subscriptions that enforce it, and the expiry in Update().
    ///
    /// The public helpers (ResolveTargets, ResolveDestination, SpawnBrood, ReplaceEntity...) are what the result data classes call;
    /// they own the SS14 system calls so the data classes stay small.
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
        [Dependency] public DamageableSystem Damageable = default!;
        [Dependency] public SharedStunSystem Stun = default!;
        [Dependency] public SharedStaminaSystem Stamina = default!;
        [Dependency] public ElectrocutionSystem Electrocution = default!;
        [Dependency] public FlashSystem Flash = default!;
        [Dependency] public StatusEffectsSystem StatusEffects = default!;
        [Dependency] public SharedJitteringSystem Jitter = default!;
        [Dependency] public FlammableSystem Flammable = default!;
        [Dependency] public BloodstreamSystem Bloodstream = default!;
        [Dependency] public MobStateSystem MobState = default!;
        [Dependency] public GibbingSystem Gibbing = default!;
        [Dependency] public SharedHandsSystem Hands = default!;
        [Dependency] public ChatSystem Chat = default!;
        [Dependency] public SharedPointLightSystem PointLight = default!;
        [Dependency] public SharedPoweredLightSystem PoweredLight = default!;
        [Dependency] public EmpSystem Emp = default!;
        [Dependency] public ExplosionSystem Explosion = default!;
        [Dependency] public PullingSystem Pulling = default!;
        [Dependency] public ThrowingSystem Throwing = default!;
        [Dependency] public SharedPhysicsSystem Physics = default!;
        [Dependency] public PolymorphSystem Polymorph = default!;
        [Dependency] public GameTicker GameTicker = default!;
        [Dependency] public RoundEndSystem RoundEnd = default!;
        [Dependency] public PuddleSystem Puddle = default!;
        [Dependency] public MetaDataSystem MetaData = default!;
        [Dependency] private SmokeSystem _smoke = default!;
        [Dependency] private SolutionContainerSystem _solutions = default!;
        [Dependency] private GunSystem _gun = default!;
        [Dependency] private MindSystem _mind = default!;
        [Dependency] private AlertLevelSystem _alertLevel = default!;
        [Dependency] private StationSystem _station = default!;
        [Dependency] private SharedMapSystem _map = default!;
        [Dependency] private TileSystem _tile = default!;
        [Dependency] private ITileDefinitionManager _tileDefinitions = default!;
        [Dependency] private IPrototypeManager _prototypes = default!;
        [Dependency] private IChatManager _chatManager = default!;
        [Dependency] private MovementSpeedModifierSystem _speed = default!;
        [Dependency] private QlippothActionInitiationSystem _initiation = default!;
        [Dependency] private QGateSystem _gates = default!;
        #endregion

        public ISawmill Sawmill = default!;

        public override void Initialize()
        {
            base.Initialize();
            Sawmill = Logger.GetSawmill("qlippoth");

            SubscribeLocalEvent<QlippothDoorHoldComponent, BeforeDoorOpenedEvent>(OnHeldDoorBeforeOpen);
            SubscribeLocalEvent<QlippothDoorHoldComponent, BeforeDoorClosedEvent>(OnHeldDoorBeforeClose);
            SubscribeLocalEvent<QlippothDoorHoldComponent, BeforeDoorAutoCloseEvent>(OnHeldDoorBeforeAutoClose);
            SubscribeLocalEvent<QlippothDoorHoldComponent, DoorStateChangedEvent>(OnHeldDoorStateChanged);

            SubscribeLocalEvent<QlippothSpeedModifierComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
            SubscribeLocalEvent<QlippothSpeedModifierComponent, ComponentShutdown>(OnSpeedShutdown);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);
            ExpireDoorHolds();
            ExpireSpeedModifiers();
            FireDueChains();
        }

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
                    if (!Exists(uid))
                        return;
                    if (!result.Execute(uid, this, eventArgs) && !action.ContinueOnFailure)
                        break;
                }
            }
        }

        #region target / actor / destination resolution
        /// <summary>
        /// The entity a targeted result should act on: the eventArgs target if the initiation supplied one, otherwise the Qlippoth itself.
        /// </summary>
        public EntityUid ResolveTarget(EntityUid uid, object? eventArgs)
        {
            return QlippothUtil.Unwrap(eventArgs) is IQlippothTargetedEventArgs targeted && Exists(targeted.Target) ? targeted.Target : uid;
        }

        /// <summary>
        /// The mob that caused the initiation (holder, user, action performer), if the initiation knows one.
        /// </summary>
        public EntityUid? ResolveActor(EntityUid uid, object? eventArgs)
        {
            return QlippothUtil.Unwrap(eventArgs) is IQlippothActorEventArgs actor && Exists(actor.Actor) ? actor.Actor : null;
        }

        /// <summary>Every entity a result with this targeting acts on. Never includes deleted entities.</summary>
        public IEnumerable<EntityUid> ResolveTargets(EntityUid uid, object? eventArgs, QlippothTargeting? targeting)
        {
            targeting ??= new QlippothTargeting();
            switch (targeting.Mode)
            {
                case QlippothTargetMode.Target:
                    return One(ResolveTarget(uid, eventArgs));
                case QlippothTargetMode.Actor:
                    return ResolveActor(uid, eventArgs) is { } actor ? One(actor) : Enumerable.Empty<EntityUid>();
                case QlippothTargetMode.Self:
                    return One(uid);
                case QlippothTargetMode.Holder:
                    return _initiation.GetHolder(uid) is { } holder && Exists(holder) ? One(holder) : Enumerable.Empty<EntityUid>();
                case QlippothTargetMode.PullPartner:
                {
                    if (TryComp<PullableComponent>(uid, out var pullable) && pullable.Puller is { } puller)
                        return One(puller);
                    if (TryComp<PullerComponent>(uid, out var pullerComponent) && pullerComponent.Pulling is { } pulling)
                        return One(pulling);
                    return Enumerable.Empty<EntityUid>();
                }
                case QlippothTargetMode.Offspring:
                {
                    if (!TryComp<QlippothActionsComponent>(uid, out var actions))
                        return Enumerable.Empty<EntityUid>();
                    actions.Offspring.RemoveWhere(child => !Exists(child));
                    return Cap(actions.Offspring.Where(child => _initiation.PassesFilter(uid, child, targeting.Filter)), targeting.MaxTargets);
                }
                case QlippothTargetMode.InRange:
                case QlippothTargetMode.RandomInRange:
                case QlippothTargetMode.NearestInRange:
                case QlippothTargetMode.QlippothsInRange:
                {
                    var required = targeting.Mode == QlippothTargetMode.QlippothsInRange ? "QlippothActions" : null;
                    var found = _initiation.EntitiesInRange(uid, targeting.Range, required, targeting.Filter, 0, targeting.IncludeSelf).Select(f => f.Uid).ToList();
                    return targeting.Mode switch
                    {
                        QlippothTargetMode.RandomInRange => found.Count > 0 ? One(Random.Pick(found)) : Enumerable.Empty<EntityUid>(),
                        QlippothTargetMode.NearestInRange => found.Take(1),
                        _ => Cap(found, targeting.MaxTargets),
                    };
                }
                case QlippothTargetMode.AroundTarget:
                {
                    var center = ResolveTarget(uid, eventArgs);
                    var found = _initiation.EntitiesInRange(center, targeting.Range, null, targeting.Filter, 0, includeSelf: true)
                        .Select(f => f.Uid)
                        .Where(other => targeting.IncludeSelf || other != uid)
                        .ToList();
                    return Cap(found, targeting.MaxTargets);
                }
            }
            return Enumerable.Empty<EntityUid>();
        }

        private static IEnumerable<EntityUid> One(EntityUid uid)
        {
            yield return uid;
        }

        private IEnumerable<EntityUid> Cap(IEnumerable<EntityUid> targets, int max)
        {
            if (max <= 0)
                return targets;
            var list = targets.ToList();
            Random.Shuffle(list);
            return list.Take(max);
        }

        /// <summary>Filter check exposed for flow results.</summary>
        public bool PassesFilter(EntityUid self, EntityUid target, QlippothTargetFilter? filter) => _initiation.PassesFilter(self, target, filter);

        /// <summary>Turn a QlippothDestination into coordinates. Null if it cannot be resolved (no target, no grid...).</summary>
        public EntityCoordinates? ResolveDestination(EntityUid uid, object? eventArgs, QlippothDestination? destination)
        {
            destination ??= new QlippothDestination();
            EntityCoordinates? result = destination.Mode switch
            {
                QlippothDestinationMode.Self => Transform(uid).Coordinates,
                QlippothDestinationMode.Target => Transform(ResolveTarget(uid, eventArgs)).Coordinates,
                QlippothDestinationMode.Actor => ResolveActor(uid, eventArgs) is { } actor ? Transform(actor).Coordinates : null,
                QlippothDestinationMode.RandomNearSelf => Scatter(Transform(uid).Coordinates, destination.Range),
                QlippothDestinationMode.RandomNearTarget => Scatter(Transform(ResolveTarget(uid, eventArgs)).Coordinates, destination.Range),
                QlippothDestinationMode.RandomOnGrid => RandomTileOnGrid(Transform(uid).GridUid),
                QlippothDestinationMode.RandomOnStation => RandomTileOnStation(uid),
                QlippothDestinationMode.NearestWithComponent => destination.Component != null && FindNearestWithComponent(uid, destination.Component) is { } nearest
                    ? Transform(nearest).Coordinates
                    : null,
                QlippothDestinationMode.Offset => Transform(uid).Coordinates,
                _ => null,
            };

            if (result == null)
                return null;
            if (destination.Offset != Vector2.Zero)
                result = result.Value.Offset(destination.Offset);
            return result;
        }

        /// <summary>Random point within radius tiles of coordinates.</summary>
        public EntityCoordinates Scatter(EntityCoordinates coordinates, float radius)
        {
            if (radius <= 0f)
                return coordinates;
            var offset = Random.NextAngle().ToVec() * Random.NextFloat(0f, radius);
            return coordinates.Offset(offset);
        }

        private EntityCoordinates? RandomTileOnGrid(EntityUid? gridUid)
        {
            if (gridUid == null || !TryComp<MapGridComponent>(gridUid, out var grid))
                return null;
            var tiles = _map.GetAllTiles(gridUid.Value, grid).Where(t => !t.Tile.IsEmpty).ToList();
            if (tiles.Count == 0)
                return null;
            var tile = Random.Pick(tiles);
            return _map.GridTileToLocal(gridUid.Value, grid, tile.GridIndices).Offset(new Vector2(0.5f, 0.5f));
        }

        private EntityCoordinates? RandomTileOnStation(EntityUid uid)
        {
            var station = _station.GetOwningStation(uid);
            if (station != null && TryComp<StationDataComponent>(station, out var data) && data.Grids.Count > 0)
            {
                var grids = data.Grids.Where(Exists).ToList();
                if (grids.Count > 0)
                    return RandomTileOnGrid(Random.Pick(grids));
            }
            return RandomTileOnGrid(Transform(uid).GridUid);
        }

        /// <summary>Closest entity on the same map with the component (YAML name), excluding the Qlippoth itself.</summary>
        public EntityUid? FindNearestWithComponent(EntityUid uid, string componentName)
        {
            if (!QlippothEntityManager.ComponentFactory.TryGetRegistration(componentName, out var registration))
            {
                Sawmill.Warning($"{uid}: unknown component '{componentName}'.");
                return null;
            }

            var xform = Transform(uid);
            var origin = QlippothTransform.GetWorldPosition(xform);
            EntityUid? best = null;
            var bestDistance = float.MaxValue;

            foreach (var (otherUid, _) in QlippothEntityManager.GetAllComponents(registration.Type))
            {
                if (otherUid == uid || !TryComp<TransformComponent>(otherUid, out var otherXform) || otherXform.MapID != xform.MapID)
                    continue;

                var distance = (QlippothTransform.GetWorldPosition(otherXform) - origin).LengthSquared();
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = otherUid;
            }

            return best;
        }

        public string EntityName(EntityUid uid)
        {
            return TryComp<MetaDataComponent>(uid, out var meta) ? meta.EntityName : string.Empty;
        }

        public string? PrototypeIdOf(EntityUid uid)
        {
            return TryComp<MetaDataComponent>(uid, out var meta) ? meta.EntityPrototype?.ID : null;
        }

        public bool HasPlayer(EntityUid uid)
        {
            return TryComp<MindContainerComponent>(uid, out var container) && container.HasMind;
        }
        #endregion

        #region state
        /// <summary>Set a key in the Qlippoth's state dictionary (see QlippothAction.RequireState). Raises OnStateChangedInitiation.</summary>
        public void SetState(EntityUid uid, string key, string value)
        {
            if (!TryComp<QlippothActionsComponent>(uid, out var actions))
                return;
            actions.State[key] = value;
            _initiation.Dispatch<OnStateChangedInitiation>(uid, new QlippothStateEventArgs(key, value));
        }

        public string? GetState(EntityUid uid, string key)
        {
            return TryComp<QlippothActionsComponent>(uid, out var actions) && actions.State.TryGetValue(key, out var value) ? value : null;
        }

        public bool ClearState(EntityUid uid, string key)
        {
            return TryComp<QlippothActionsComponent>(uid, out var actions) && actions.State.Remove(key);
        }

        public bool CheckSituation(EntityUid uid, bool? held, bool? contained, bool? anchored, bool? inContainer, bool? hasPlayer, float? minDamage, float? maxDamage)
        {
            if (held != null && (_initiation.GetHolder(uid) != null) != held.Value)
                return false;
            if (contained != null && _initiation.IsContained(uid) != contained.Value)
                return false;
            if (anchored != null && Transform(uid).Anchored != anchored.Value)
                return false;
            if (inContainer != null && (_initiation.GetContainerOwner(uid) != null) != inContainer.Value)
                return false;
            if (hasPlayer != null && HasPlayer(uid) != hasPlayer.Value)
                return false;
            var damage = _initiation.TotalDamage(uid);
            if (minDamage != null && damage < minDamage.Value)
                return false;
            if (maxDamage != null && damage > maxDamage.Value)
                return false;
            return true;
        }

        public int ResetCooldowns(EntityUid uid, string? actionName)
        {
            if (!TryComp<QlippothActionsComponent>(uid, out var actions))
                return 0;
            var count = 0;
            foreach (var action in actions.Actions)
            {
                if (actionName != null && action.ActionName != actionName)
                    continue;
                action.NextReadyAt = TimeSpan.Zero;
                count++;
            }
            return count;
        }
        #endregion

        #region spawning
        /// <summary>Spawn a prototype at coordinates (+ random scatter). Null if the prototype is unknown.</summary>
        public EntityUid? SpawnAt(string prototype, EntityCoordinates coordinates, float scatter)
        {
            if (!_prototypes.HasIndex<EntityPrototype>(prototype))
            {
                Sawmill.Warning($"Unknown prototype '{prototype}'.");
                return null;
            }
            var spawned = Spawn(prototype, Scatter(coordinates, scatter));
            return Exists(spawned) ? spawned : null;
        }

        /// <summary>Shared implementation of SpawnMobResult / SpawnQlippothResult / SpawnCopyResult.</summary>
        public bool SpawnBrood(EntityUid uid, object? eventArgs, string prototype, int count, QlippothDestination destination, float scatter,
            bool ghostRole, string roleName, string roleDescription, string roleRules, bool trackOffspring, int maxAlive, bool copyState = false)
        {
            if (ResolveDestination(uid, eventArgs, destination) is not { } coordinates)
                return false;

            TryComp<QlippothActionsComponent>(uid, out var actions);
            actions?.Offspring.RemoveWhere(child => !Exists(child));

            var any = false;
            for (var i = 0; i < count; i++)
            {
                if (maxAlive > 0 && actions != null && actions.Offspring.Count >= maxAlive)
                    break;

                var spawned = SpawnAt(prototype, coordinates, scatter);
                if (spawned == null)
                    return any;

                if (ghostRole)
                    MakeGhostRole(spawned.Value, roleName, roleDescription, roleRules);
                if (trackOffspring)
                    TrackOffspring(uid, spawned.Value);
                if (copyState && actions != null && TryComp<QlippothActionsComponent>(spawned.Value, out var childActions))
                {
                    foreach (var (key, value) in actions.State)
                        childActions.State[key] = value;
                }
                any = true;
            }
            return any;
        }

        public bool MakeGhostRole(EntityUid entity, string roleName, string roleDescription, string roleRules)
        {
            if (!Exists(entity) || !HasComp<MindContainerComponent>(entity))
                return false;

            var ghostRole = EnsureComp<GhostRoleComponent>(entity);
            ghostRole.RoleName = Loc.GetString(roleName);
            ghostRole.RoleDescription = Loc.GetString(roleDescription);
            ghostRole.RoleRules = Loc.GetString(roleRules);
            EnsureComp<GhostTakeoverAvailableComponent>(entity);
            return true;
        }

        public void TrackOffspring(EntityUid parent, EntityUid child)
        {
            if (!TryComp<QlippothActionsComponent>(parent, out var actions))
                return;
            actions.Offspring.Add(child);
            var marker = EnsureComp<QlippothOffspringComponent>(child);
            marker.Parent = parent;
            marker.Reported = false;
        }

        /// <summary>Spawn and launch a projectile from the shooter toward the target. Spread in degrees.</summary>
        public bool ShootAt(EntityUid shooter, EntityUid target, string projectile, float speed, float spread)
        {
            var from = QlippothTransform.GetMapCoordinates(shooter);
            var to = QlippothTransform.GetMapCoordinates(target);
            if (from.MapId != to.MapId)
                return false;

            var direction = to.Position - from.Position;
            if (direction.LengthSquared() < 0.01f)
                return false;
            if (spread > 0f)
                direction = (new Angle(direction) + Angle.FromDegrees(Random.NextFloat(-spread, spread))).ToVec();

            var spawned = SpawnAt(projectile, Transform(shooter).Coordinates, 0f);
            if (spawned == null)
                return false;

            _gun.ShootProjectile(spawned.Value, direction, Vector2.Zero, shooter, shooter, speed);
            return true;
        }

        /// <summary>Center coordinates of every tile within a square radius around the entity (its grid only).</summary>
        public IEnumerable<EntityCoordinates> TilesInRange(EntityUid uid, int radius)
        {
            var xform = Transform(uid);
            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
                yield break;

            var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    var indices = center + new Vector2i(dx, dy);
                    var tile = _map.GetTileRef(gridUid, grid, indices);
                    if (tile.Tile.IsEmpty)
                        continue;
                    yield return _map.GridTileToLocal(gridUid, grid, indices).Offset(new Vector2(0.5f, 0.5f));
                }
            }
        }

        public bool HasPrototypeAt(EntityCoordinates coordinates, string prototype)
        {
            foreach (var other in Lookup.GetEntitiesInRange(coordinates, 0.4f))
            {
                if (PrototypeIdOf(other) == prototype)
                    return true;
            }
            return false;
        }
        #endregion

        #region movement
        public void TeleportEntity(EntityUid entity, EntityCoordinates coordinates)
        {
            if (TryComp<PullableComponent>(entity, out var pullable) && pullable.Puller != null)
                Pulling.TryStopPull(entity, pullable);
            if (Transform(entity).Anchored)
                QlippothTransform.Unanchor(entity);
            QlippothTransform.SetCoordinates(entity, coordinates);
        }
        #endregion

        #region chat
        public bool SendPrivateMessage(EntityUid target, string message)
        {
            if (!TryComp<ActorComponent>(target, out var actor))
                return false;
            var wrapped = Loc.GetString("qlippoth-whisper-wrap", ("message", message));
            _chatManager.ChatMessageToOne(ChatChannel.Local, message, wrapped, EntityUid.Invalid, false, actor.PlayerSession.Channel, Color.MediumPurple);
            return true;
        }
        #endregion

        #region atmos / chemistry
        /// <summary>Tile mixtures in a square radius around the entity (radius 0 = just its own tile).</summary>
        public IEnumerable<GasMixture> TileMixturesAround(EntityUid uid, int radius)
        {
            if (radius <= 0)
            {
                if (Atmosphere.GetContainingMixture(uid, excite: true) is { } own)
                    yield return own;
                yield break;
            }

            var xform = Transform(uid);
            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
                yield break;

            var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    var mixture = Atmosphere.GetTileMixture(gridUid, null, center + new Vector2i(dx, dy), excite: true);
                    if (mixture != null)
                        yield return mixture;
                }
            }
        }

        public bool ExposeHotspot(EntityUid uid, float temperature, float volume)
        {
            var xform = Transform(uid);
            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
                return false;
            var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            Atmosphere.HotspotExpose(gridUid, indices, temperature, volume, uid);
            return true;
        }

        public bool StartSmoke(EntityCoordinates coordinates, Solution solution, float duration, int spread, bool foam)
        {
            var cloud = SpawnAt(foam ? "Foam" : "Smoke", coordinates, 0f);
            if (cloud == null)
                return false;
            _smoke.StartSmoke(cloud.Value, solution, duration, spread);
            return true;
        }

        private bool TryGetSolution(EntityUid target, string? name, out Entity<SolutionComponent> solutionEntity)
        {
            solutionEntity = default;
            if (name != null)
            {
                if (!_solutions.TryGetSolution(target, name, out var named))
                    return false;
                solutionEntity = named.Value;
                return true;
            }

            foreach (var (_, solution) in _solutions.EnumerateSolutions(target))
            {
                solutionEntity = solution;
                return true;
            }
            return false;
        }

        public bool AddReagent(EntityUid target, string? solutionName, Solution toAdd)
        {
            if (solutionName == null && HasComp<Content.Shared.Body.Components.BloodstreamComponent>(target))
                return Bloodstream.TryAddToBloodstream(target, toAdd);
            if (!TryGetSolution(target, solutionName, out var solution))
                return false;
            return _solutions.TryAddSolution(solution, toAdd);
        }

        public bool EmptySolution(EntityUid target, string? solutionName)
        {
            if (!TryGetSolution(target, solutionName, out var solution))
                return false;
            _solutions.RemoveAllSolution(solution);
            return true;
        }

        public bool HeatSolution(EntityUid target, string? solutionName, float temperature)
        {
            if (!TryGetSolution(target, solutionName, out var solution))
                return false;
            _solutions.SetTemperature(solution, temperature);
            return true;
        }

        public bool SpillSolution(EntityUid target, string? solutionName)
        {
            if (!TryGetSolution(target, solutionName, out var solution) || solution.Comp.Solution.Volume <= 0)
                return false;
            var spilled = _solutions.SplitSolution(solution, solution.Comp.Solution.Volume);
            return Puddle.TrySpillAt(Transform(target).Coordinates, spilled, out _);
        }
        #endregion

        #region conversion
        /// <summary>Replace an entity with another prototype at the same place. Returns the new entity.</summary>
        public EntityUid? ReplaceEntity(EntityUid target, string prototype, bool transferMind, bool keepName, bool copyState)
        {
            var xform = Transform(target);
            var coordinates = xform.Coordinates;
            var rotation = xform.LocalRotation;
            var anchored = xform.Anchored;
            var name = EntityName(target);

            if (anchored)
                QlippothTransform.Unanchor(target);

            var replacement = SpawnAt(prototype, coordinates, 0f);
            if (replacement == null)
                return null;

            QlippothTransform.SetLocalRotation(replacement.Value, rotation);
            if (anchored)
                QlippothTransform.AnchorEntity(replacement.Value);
            if (keepName)
                MetaData.SetEntityName(replacement.Value, name);

            if (copyState && TryComp<QlippothActionsComponent>(target, out var oldActions) && TryComp<QlippothActionsComponent>(replacement.Value, out var newActions))
            {
                foreach (var (key, value) in oldActions.State)
                    newActions.State[key] = value;
            }

            if (TryComp<QlippothOffspringComponent>(target, out var offspring) && Exists(offspring.Parent))
            {
                offspring.Reported = true;
                TrackOffspring(offspring.Parent, replacement.Value);
            }

            if (transferMind && _mind.TryGetMind(target, out var mindId, out var mind))
                _mind.TransferTo(mindId, replacement.Value, ghostCheckOverride: true, mind: mind);

            QueueDel(target);
            return replacement;
        }

        public int ConvertTiles(EntityUid uid, string tileId, int radius, float chance, List<string> from)
        {
            if (!_tileDefinitions.TryGetDefinition(tileId, out var definition) || definition is not ContentTileDefinition replacement)
            {
                Sawmill.Warning($"ConvertTiles: unknown tile '{tileId}'.");
                return 0;
            }

            var xform = Transform(uid);
            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
                return 0;

            var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            var count = 0;
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    var tile = _map.GetTileRef(gridUid, grid, center + new Vector2i(dx, dy));
                    if (tile.Tile.IsEmpty || tile.Tile.TypeId == replacement.TileId)
                        continue;
                    if (from.Count > 0 && (!_tileDefinitions.TryGetDefinition(tile.Tile.TypeId, out var current) || !from.Contains(current.ID)))
                        continue;
                    if (chance < 1f && !Random.Prob(chance))
                        continue;
                    if (_tile.ReplaceTile(tile, replacement))
                        count++;
                }
            }
            return count;
        }

        public bool RemoveComponentNamed(EntityUid target, string componentName)
        {
            if (!QlippothEntityManager.ComponentFactory.TryGetRegistration(componentName, out var registration))
            {
                Sawmill.Warning($"RemoveComponents: unknown component '{componentName}'.");
                return false;
            }
            return QlippothEntityManager.RemoveComponent(target, registration.Type);
        }

        public bool TransferMinds(EntityUid self, EntityUid target, QlippothMindTransfer mode)
        {
            var hasSelf = _mind.TryGetMind(self, out var selfMind, out var selfComponent);
            var hasTarget = _mind.TryGetMind(target, out var targetMind, out var targetComponent);

            switch (mode)
            {
                case QlippothMindTransfer.SelfToTarget:
                    if (!hasSelf)
                        return false;
                    if (hasTarget)
                        _mind.TransferTo(targetMind, null, ghostCheckOverride: true, mind: targetComponent);
                    _mind.TransferTo(selfMind, target, ghostCheckOverride: true, mind: selfComponent);
                    return true;
                case QlippothMindTransfer.TargetToSelf:
                    if (!hasTarget)
                        return false;
                    if (hasSelf)
                        _mind.TransferTo(selfMind, null, ghostCheckOverride: true, mind: selfComponent);
                    _mind.TransferTo(targetMind, self, ghostCheckOverride: true, mind: targetComponent);
                    return true;
                case QlippothMindTransfer.Swap:
                    if (!hasSelf && !hasTarget)
                        return false;
                    if (hasSelf)
                        _mind.TransferTo(selfMind, null, ghostCheckOverride: true, createGhost: false, mind: selfComponent);
                    if (hasTarget)
                        _mind.TransferTo(targetMind, self, ghostCheckOverride: true, mind: targetComponent);
                    if (hasSelf)
                        _mind.TransferTo(selfMind, target, ghostCheckOverride: true, mind: selfComponent);
                    return true;
            }
            return false;
        }
        #endregion

        #region events
        public int EndGameRules(string ruleId)
        {
            var count = 0;
            foreach (var rule in GameTicker.GetActiveGameRules().ToArray())
            {
                if (PrototypeIdOf(rule) != ruleId)
                    continue;
                if (GameTicker.EndGameRule(rule))
                    count++;
            }
            return count;
        }

        public bool SetAlertLevel(EntityUid uid, string level, bool announce, bool force, bool locked)
        {
            var station = _station.GetOwningStation(uid);
            if (station == null)
            {
                var stations = _station.GetStations();
                if (stations.Count == 0)
                    return false;
                station = stations[0];
            }
            _alertLevel.SetLevel(station.Value, level, announce, announce, force, locked);
            return true;
        }

        public int BroadcastSignal(EntityUid sender, string signal, float range, bool global, bool includeSelf, QlippothTargetFilter filter)
        {
            var senderXform = Transform(sender);
            var origin = QlippothTransform.GetWorldPosition(senderXform);
            var eventArgs = new QlippothSignalEventArgs(sender, signal);
            var count = 0;

            var query = EntityQueryEnumerator<QlippothActionsComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (uid == sender && !includeSelf)
                    continue;
                if (!global)
                {
                    if (xform.MapID != senderXform.MapID)
                        continue;
                    if (range > 0f && (QlippothTransform.GetWorldPosition(xform) - origin).Length() > range)
                        continue;
                }
                if (!_initiation.PassesFilter(sender, uid, filter))
                    continue;

                _initiation.Dispatch<OnSignalInitiation>(uid, eventArgs);
                count++;
            }
            return count;
        }

        public int BreachGates(EntityUid uid, bool nearestOnly)
        {
            var candidates = new List<(EntityUid Gate, float Distance)>();
            var origin = QlippothTransform.GetMapCoordinates(uid);
            var query = EntityQueryEnumerator<QGateComponent, TransformComponent>();
            while (query.MoveNext(out var gate, out var component, out var xform))
            {
                if (component.IsBreached || component.IsCleared)
                    continue;
                var position = QlippothTransform.GetMapCoordinates(xform);
                var distance = position.MapId == origin.MapId ? (position.Position - origin.Position).Length() : float.MaxValue;
                candidates.Add((gate, distance));
            }

            if (candidates.Count == 0)
                return 0;
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (nearestOnly)
                candidates = candidates.Take(1).ToList();

            foreach (var (gate, _) in candidates)
                _gates.TriggerBreach(gate);
            return candidates.Count;
        }
        #endregion

        #region lasting effect: chains scheduled for later
        // DelayResult queues (uid, key, args, due); FireDueChains raises OnChainInitiation when the time comes.
        private readonly List<PendingChain> _pendingChains = new();

        private sealed class PendingChain
        {
            public EntityUid Uid;
            public string Key = string.Empty;
            public object? EventArgs;
            public TimeSpan DueAt;
        }

        /// <summary>Fire OnChainInitiation { key } on the Qlippoth now, forwarding the original event args. Returns how many actions fired.</summary>
        public int Chain(EntityUid uid, string key, object? eventArgs)
        {
            return _initiation.Dispatch<OnChainInitiation>(uid, new QlippothChainEventArgs(key, eventArgs));
        }

        public void ScheduleChain(EntityUid uid, string key, float delaySeconds, object? eventArgs, bool refresh)
        {
            var dueAt = Timing.CurTime + TimeSpan.FromSeconds(delaySeconds);
            if (refresh)
            {
                foreach (var pending in _pendingChains)
                {
                    if (pending.Uid != uid || pending.Key != key)
                        continue;
                    pending.DueAt = dueAt;
                    pending.EventArgs = eventArgs;
                    return;
                }
            }
            _pendingChains.Add(new PendingChain { Uid = uid, Key = key, EventArgs = eventArgs, DueAt = dueAt });
        }

        public int CancelChain(EntityUid uid, string key)
        {
            return _pendingChains.RemoveAll(pending => pending.Uid == uid && pending.Key == key);
        }

        private void FireDueChains()
        {
            if (_pendingChains.Count == 0)
                return;

            var now = Timing.CurTime;
            var due = _pendingChains.Where(pending => now >= pending.DueAt).ToList();
            if (due.Count == 0)
            {
                _pendingChains.RemoveAll(pending => !Exists(pending.Uid));
                return;
            }

            _pendingChains.RemoveAll(pending => now >= pending.DueAt || !Exists(pending.Uid));
            foreach (var pending in due)
            {
                if (Exists(pending.Uid))
                    _initiation.Dispatch<OnChainInitiation>(pending.Uid, new QlippothChainEventArgs(pending.Key, pending.EventArgs));
            }
        }
        #endregion

        #region lasting effect: speed modifier
        public bool ApplySpeedModifier(EntityUid target, float walk, float sprint, float duration)
        {
            if (!HasComp<MovementSpeedModifierComponent>(target))
                return false;

            var modifier = EnsureComp<QlippothSpeedModifierComponent>(target);
            modifier.Walk = walk;
            modifier.Sprint = sprint;
            modifier.Until = Timing.CurTime + TimeSpan.FromSeconds(duration);
            _speed.RefreshMovementSpeedModifiers(target);
            return true;
        }

        private void OnRefreshSpeed(EntityUid uid, QlippothSpeedModifierComponent modifier, RefreshMovementSpeedModifiersEvent args)
        {
            args.ModifySpeed(modifier.Walk, modifier.Sprint);
        }

        private void OnSpeedShutdown(EntityUid uid, QlippothSpeedModifierComponent modifier, ComponentShutdown args)
        {
            _speed.RefreshMovementSpeedModifiers(uid);
        }

        private void ExpireSpeedModifiers()
        {
            var now = Timing.CurTime;
            var query = EntityQueryEnumerator<QlippothSpeedModifierComponent>();
            while (query.MoveNext(out var uid, out var modifier))
            {
                if (now >= modifier.Until)
                    RemCompDeferred<QlippothSpeedModifierComponent>(uid);
            }
        }
        #endregion

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

    /// <summary>Marker for SpeedModifierResult: multiplies walk / sprint speed until <see cref="Until"/>. Managed by QlippothActionResultSystem.</summary>
    [RegisterComponent]
    public sealed partial class QlippothSpeedModifierComponent : Component
    {
        public float Walk = 1f;
        public float Sprint = 1f;
        public TimeSpan Until;
    }
}
