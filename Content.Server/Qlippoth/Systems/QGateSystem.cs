using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Gravity;
using Content.Shared.DoAfter;
using Content.Shared.Eye;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Maps;
using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// Everything on the gate side of the game loop:
///  - spawning gates and rolling their phase,
///  - opening the rift: pick a Qlippoth for the gate phase (QlippothSystem pool), build that Qlippoth's dungeon, spawn it inside,
///  - the objectives inside the dungeon,
///  - breach (Qlippoth escapes to the station), clear (Qlippoth goes to the market), seal, evacuation.
/// Gates only know phases; which Qlippoth fits which phase is decided by the Qlippoths themselves (QlippothComponent.GatePhases).
/// </summary>
public sealed partial class QGateSystem : EntitySystem
{
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ContainmentDimensionSystem _containmentDim = default!;
    [Dependency] private QlippothMarketSystem _market = default!;
    [Dependency] private QlippothSystem _qlippoths = default!;
    [Dependency] private ContainmentPortalSystem _portals = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private VisibilitySystem _visibility = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private QlippothActionInitiationSystem _initiation = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private readonly Dictionary<EntityUid, RiftDungeon> _dungeonsByGate = new();
    private readonly Dictionary<EntityUid, EntityUid> _returnPortalsByGate = new();
    private readonly Dictionary<EntityUid, List<EntityUid>> _breachEffectsByGate = new();
    private TimeSpan _nextAutomaticSpawn;

    /// <summary>A live rift dimension. Qlippoth is null if no prototype fit the gate phase.</summary>
    public readonly record struct RiftDungeon(MapId MapId, MapCoordinates Entry, EntityUid? Qlippoth);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QGateComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<QGateComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<QGateComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<QlippothBreachSealDeviceComponent, AfterInteractEvent>(OnBreachSealDeviceUsed);
        SubscribeLocalEvent<QlippothBreachSealDeviceComponent, ActivateInWorldEvent>(OnBreachSealDeviceActivated);
        SubscribeLocalEvent<QlippothBreachSealDeviceComponent, SealQlippothGateDoAfterEvent>(OnBreachSealCompleted);
        SubscribeLocalEvent<QGateDungeonObjectiveComponent, InteractHandEvent>(OnObjectiveInteractHand);
        SubscribeLocalEvent<QGateDungeonObjectiveComponent, ActivateInWorldEvent>(OnObjectiveActivateInWorld);
        _nextAutomaticSpawn = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.QlippothGateSpawnInterval));
    }

    private void OnMapInit(EntityUid uid, QGateComponent component, MapInitEvent args)
    {
        _initiation.DispatchAll<OnGateSpawnedInitiation>(new QlippothTargetEventArgs(uid));
        var xform = Transform(uid);
        if (_containmentDim.IsContainmentDimension(xform.MapID))
        {
            // CRITICAL RULE: Q-Gates can NEVER exist inside Containment Dimension.
            QueueDel(uid);
            return;
        }

        component.SpawnedAt = _timing.CurTime;
        HideGate(uid);
        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime >= _nextAutomaticSpawn)
        {
            TrySpawnAutomaticGate();
            _nextAutomaticSpawn = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.QlippothGateSpawnInterval));
        }

        var query = EntityQueryEnumerator<QGateComponent, TransformComponent>();
        var curTime = _timing.CurTime;

        while (query.MoveNext(out var uid, out var qgate, out var xform))
        {
            // Verify map restriction
            if (_containmentDim.IsContainmentDimension(xform.MapID))
            {
                QueueDel(uid);
                continue;
            }

            if (qgate.IsBreached)
                continue;

            // A cleared rift remains alive during the evacuation window so the
            // return portal can be used before the dungeon is removed.
            if (qgate.IsCleared)
            {
                if (curTime >= qgate.PortalCloseAt)
                    CloseRift(uid, qgate);

                continue;
            }

            var elapsed = curTime - qgate.SpawnedAt;
            var warningAt = qgate.ArrivalEta - qgate.WarningEta;

            // Warn before the gate opens, then announce the confirmed opening.
            if (!qgate.FiveMinWarningSent && elapsed >= warningAt)
            {
                qgate.FiveMinWarningSent = true;
                var warningMsg = Loc.GetString("qgate-announcement-warning",
                    ("location", qgate.LocationName),
                    ("phase", GetPhaseName(qgate.Phase)));

                _chatSystem.DispatchGlobalAnnouncement(warningMsg, "CentCom", playSound: true, colorOverride: Color.FromHex("#DAA520"));
            }

            if (!qgate.ArrivalAnnouncementSent && elapsed >= qgate.ArrivalEta)
            {
                qgate.ArrivalAnnouncementSent = true;
                var spawnMsg = Loc.GetString("qgate-announcement-spawn",
                    ("location", qgate.LocationName),
                    ("phase", GetPhaseName(qgate.Phase)));

                _chatSystem.DispatchGlobalAnnouncement(spawnMsg, "CentCom", playSound: true, colorOverride: Color.FromHex("#FF4500"));
            }

            if (!qgate.RiftOpened && elapsed >= qgate.ArrivalEta)
                OpenRift(uid, qgate);

            if (qgate.RiftOpened && !qgate.ObjectiveCompleted && curTime >= qgate.RiftOpenedAt + qgate.Duration)
                TriggerBreach(uid, qgate);

            if (qgate.ObjectiveCompleted && qgate.PortalClosing && curTime >= qgate.PortalCloseAt)
                CloseRift(uid, qgate);
        }

        UpdateRadarConsoles(curTime);
    }

    #region gate spawning
    private void TrySpawnAutomaticGate()
    {
        var activeGates = 0;
        var gateQuery = EntityQueryEnumerator<QGateComponent>();
        while (gateQuery.MoveNext(out _, out var activeQgate))
        {
            if (!activeQgate.IsBreached && !activeQgate.IsCleared)
                activeGates++;
        }

        if (activeGates >= Math.Max(1, _cfg.GetCVar(CCVars.QlippothMaxActiveGates)))
            return;

        // Collect player positions on all grids to prevent spawning directly on players' feet
        var playerPositionsByGrid = new Dictionary<EntityUid, List<Vector2>>();
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out _, out var actorXform))
        {
            if (actorXform.GridUid is { } gUid && actorXform.MapID != MapId.Nullspace &&
                !_containmentDim.IsContainmentDimension(actorXform.MapID))
            {
                if (!playerPositionsByGrid.TryGetValue(gUid, out var list))
                    playerPositionsByGrid[gUid] = list = new List<Vector2>();
                list.Add(actorXform.Coordinates.Position);
            }
        }

        // Collect valid station / space grids outside the containment dimension
        var candidateGrids = new List<Entity<MapGridComponent>>();
        var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridUid, out var gridComp, out var gridXform))
        {
            if (gridXform.MapID == MapId.Nullspace || _containmentDim.IsContainmentDimension(gridXform.MapID))
                continue;

            candidateGrids.Add((gridUid, gridComp));
        }

        if (candidateGrids.Count == 0)
            return;

        // Prefer grids that have active players / activity, but fall back to any available grid
        var populatedGrids = candidateGrids.Where(g => playerPositionsByGrid.ContainsKey(g.Owner)).ToList();
        var preferredPool = populatedGrids.Count > 0 && _random.Prob(0.85f) ? populatedGrids : candidateGrids;
        _random.Shuffle(preferredPool);

        EntityCoordinates? spawnCoords = null;
        var chosenGridUid = EntityUid.Invalid;

        foreach (var (gridUid, gridComp) in preferredPool)
        {
            playerPositionsByGrid.TryGetValue(gridUid, out var playerPositions);
            spawnCoords = FindOpenTileOnGrid(gridUid, gridComp, playerPositions);
            if (spawnCoords != null)
            {
                chosenGridUid = gridUid;
                break;
            }
        }

        if (spawnCoords == null)
            return;

        var phase = RollAutomaticPhase();
        var gate = Spawn(GetGatePrototype(phase), spawnCoords.Value);
        _transform.SetLocalRotation(gate, Angle.Zero);

        if (TryComp<QGateComponent>(gate, out var qgate))
        {
            var isStation = playerPositionsByGrid.ContainsKey(chosenGridUid);
            qgate.LocationName = isStation ? "Station Sector" : "Deep Space Grid";
            Dirty(gate, qgate);
        }
    }

    private EntityCoordinates? FindOpenTileOnGrid(EntityUid gridUid, MapGridComponent grid, List<Vector2>? playerPositions)
    {
        var tiles = _maps.GetAllTiles(gridUid, grid).Where(t => !t.Tile.IsEmpty).ToList();
        if (tiles.Count == 0)
            return null;

        const float minPlayerDist = 6f; // Never spawn on top of or immediately next to players
        _random.Shuffle(tiles);

        foreach (var tile in tiles.Take(100))
        {
            var centerCoords = _maps.GridTileToLocal(gridUid, grid, tile.GridIndices).Offset(new Vector2(0.5f, 0.5f));

            // Don't spawn inside solid walls or blocking machinery
            var blocked = false;
            var anchored = _maps.GetAnchoredEntitiesEnumerator(gridUid, grid, tile.GridIndices);
            while (anchored.MoveNext(out var ancUid))
            {
                if (TryComp<PhysicsComponent>(ancUid, out var phys) && phys.CanCollide && phys.Hard)
                {
                    blocked = true;
                    break;
                }
            }
            if (blocked)
                continue;

            // Ensure safe distance from players
            if (playerPositions is { Count: > 0 })
            {
                var tooClose = false;
                foreach (var pos in playerPositions)
                {
                    if (Vector2.Distance(centerCoords.Position, pos) < minPlayerDist)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose)
                    continue;
            }

            return centerCoords;
        }

        // Fallback: pick any non-blocked tile on the grid
        foreach (var tile in tiles.Take(50))
        {
            var anchored = _maps.GetAnchoredEntitiesEnumerator(gridUid, grid, tile.GridIndices);
            var blocked = false;
            while (anchored.MoveNext(out var ancUid))
            {
                if (TryComp<PhysicsComponent>(ancUid, out var phys) && phys.CanCollide && phys.Hard)
                {
                    blocked = true;
                    break;
                }
            }
            if (!blocked)
                return _maps.GridTileToLocal(gridUid, grid, tile.GridIndices).Offset(new Vector2(0.5f, 0.5f));
        }

        return null;
    }

    private QGatePhase RollAutomaticPhase()
    {
        var weights = new[]
        {
            Math.Max(0, _cfg.GetCVar(CCVars.QlippothPhase1Weight)),
            Math.Max(0, _cfg.GetCVar(CCVars.QlippothPhase2Weight)),
            Math.Max(0, _cfg.GetCVar(CCVars.QlippothPhase3Weight)),
            Math.Max(0, _cfg.GetCVar(CCVars.QlippothPhase4Weight)),
            Math.Max(0, _cfg.GetCVar(CCVars.QlippothPhase5Weight))
        };
        var total = weights.Sum();
        if (total <= 0)
            return QGatePhase.Phase1Rift;

        var roll = _random.Next(1, total + 1);
        for (var index = 0; index < weights.Length; index++)
        {
            roll -= weights[index];
            if (roll <= 0)
                return (QGatePhase) (index + 1);
        }

        return QGatePhase.Phase1Rift;
    }

    private void HideGate(EntityUid uid)
    {
        var visibility = EnsureComp<VisibilityComponent>(uid);
        _visibility.RemoveLayer((uid, visibility), (int) VisibilityFlags.Normal, false);
        _visibility.RefreshVisibility(uid, visibility);
    }

    private void ShowGate(EntityUid uid)
    {
        var visibility = EnsureComp<VisibilityComponent>(uid);
        _visibility.AddLayer((uid, visibility), (int) VisibilityFlags.Normal, false);
        _visibility.RefreshVisibility(uid, visibility);
    }

    /// <summary>Gate entity prototype for a phase (Resources/Prototypes/Entities/Structures/Qlippoth/qgates.yml).</summary>
    public static string GetGatePrototype(QGatePhase phase)
    {
        return phase switch
        {
            QGatePhase.Phase2Verge => "QGatePhase2Verge",
            QGatePhase.Phase3Eclipse => "QGatePhase3Eclipse",
            QGatePhase.Phase4Abyss => "QGatePhase4Abyss",
            QGatePhase.Phase5Horizon => "QGatePhase5Horizon",
            _ => "QGatePhase1Rift"
        };
    }

    public string GetPhaseName(QGatePhase phase)
    {
        return phase switch
        {
            QGatePhase.Phase1Rift => Loc.GetString("qgate-phase-rift"),
            QGatePhase.Phase2Verge => Loc.GetString("qgate-phase-verge"),
            QGatePhase.Phase3Eclipse => Loc.GetString("qgate-phase-eclipse"),
            QGatePhase.Phase4Abyss => Loc.GetString("qgate-phase-abyss"),
            QGatePhase.Phase5Horizon => Loc.GetString("qgate-phase-horizon"),
            _ => "Unknown Phase"
        };
    }
    #endregion

    #region radar
    private void UpdateRadarConsoles(TimeSpan curTime)
    {
        var gates = new List<QGateComponent>();
        var gateQuery = EntityQueryEnumerator<QGateComponent>();
        while (gateQuery.MoveNext(out _, out var gate))
        {
            if (!gate.IsBreached)
                gates.Add(gate);
        }

        var radarQuery = EntityQueryEnumerator<QGateRadarComponent>();
        while (radarQuery.MoveNext(out var uid, out var radar))
        {
            if (gates.Count == 0)
            {
                if (radar.LastTrackedProbability == 0f && radar.EtaSeconds == 0)
                    continue;

                radar.LastTrackedProbability = 0f;
                radar.PredictedPhase = QGatePhase.Phase1Rift;
                radar.EtaSeconds = 0;
                Dirty(uid, radar);
                continue;
            }

            var gate = gates[0];
            var deadline = gate.IsCleared
                ? gate.PortalCloseAt
                : gate.RiftOpened
                    ? gate.RiftOpenedAt + gate.Duration
                    : gate.SpawnedAt + gate.ArrivalEta;
            var eta = Math.Max(0, (int)Math.Ceiling((deadline - curTime).TotalSeconds));

            if (radar.LastTrackedProbability == 0.75f &&
                radar.PredictedPhase == gate.Phase &&
                radar.EtaSeconds == eta)
                continue;

            radar.LastTrackedProbability = 0.75f;
            radar.PredictedPhase = gate.Phase;
            radar.EtaSeconds = eta;
            Dirty(uid, radar);
        }
    }

    public string GetTrackerDetail(TimeSpan curTime)
    {
        var lines = new List<string>();
        var query = EntityQueryEnumerator<QGateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gate, out _))
        {
            var coordinates = _transform.GetMapCoordinates(uid);
            var status = gate.IsBreached
                ? "BREACHED"
                : gate.IsCleared
                    ? $"EVACUATION: {FormatCountdown(gate.PortalCloseAt - curTime)}"
                    : gate.RiftOpened
                        ? $"RIFT OPEN | BREACH IN: {FormatCountdown(gate.RiftOpenedAt + gate.Duration - curTime)}"
                        : $"ARRIVAL IN: {FormatCountdown(gate.SpawnedAt + gate.ArrivalEta - curTime)}";
            lines.Add($"- {gate.LocationName} | {GetPhaseName(gate.Phase)} | {status} | Map {coordinates.MapId}: {coordinates.Position.X:0.0}, {coordinates.Position.Y:0.0}");
        }

        return lines.Count == 0 ? "No active Q-Gates detected." : string.Join("\n", lines);
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }
    #endregion

    #region rift dungeon
    private void OpenRift(EntityUid uid, QGateComponent qgate)
    {
        if (qgate.RiftOpened)
            return;

        qgate.RiftOpened = true;
        qgate.RiftOpenedAt = _timing.CurTime;
        ShowGate(uid);

        // The gate only knows its phase. Ask the Qlippoth pool which Qlippoth comes through, then build *that* Qlippoth's dungeon.
        QlippothDungeon dungeonDefinition = new ArenaDungeon();
        if (_qlippoths.TryPickQlippoth(qgate.Phase, out var prototype))
        {
            qgate.QlippothPrototype = prototype;
            dungeonDefinition = _qlippoths.GetPrototypeData(prototype)?.Dungeon ?? dungeonDefinition;
        }
        else
        {
            Log.Warning($"No Qlippoth prototype lists gate phase {qgate.Phase} in its gatePhases; rift at {qgate.LocationName} opens empty.");
        }

        var (dungeon, objectiveCount) = CreateRiftDungeon(dungeonDefinition, qgate.QlippothPrototype, uid);
        _dungeonsByGate[uid] = dungeon;
        if (objectiveCount > 0)
            qgate.RequiredObjectives = objectiveCount;
        Dirty(uid, qgate);
    }

    private (RiftDungeon Dungeon, int ObjectiveCount) CreateRiftDungeon(QlippothDungeon definition, EntProtoId? qlippothPrototype, EntityUid gate)
    {
        var mapUid = _maps.CreateMap(out var mapId);
        ApplyRiftEnvironment(mapUid);
        var gridEntity = _mapManager.CreateGridEntity(mapId);
        var gridUid = gridEntity.Owner;

        var layout = definition.Build(this, gridUid, gridEntity.Comp, gate);

        EntityUid? qlippoth = null;
        if (qlippothPrototype is { } prototype)
        {
            qlippoth = Spawn(prototype, new EntityCoordinates(gridUid, layout.QlippothSpot));
            _initiation.Dispatch<OnArrivedInitiation>(qlippoth.Value, new QlippothArrivalEventArgs(gate, QlippothArrivalKind.RiftDungeon));
        }

        var entry = new MapCoordinates(layout.Entry, mapId);
        return (new RiftDungeon(mapId, entry, qlippoth), layout.ObjectiveCount);
    }

    /// <summary>
    /// Gives a freshly created rift map breathable air and gravity.
    /// Without this the map falls back to <see cref="GasMixture.SpaceGas"/> and no gravity, so the crew suffocates
    /// and floats the moment they step through the gate.
    /// This is map atmosphere, not a simulated grid atmosphere: one mixture for the whole rift, no pressure or leaks.
    /// Per-dungeon atmosphere (vacuum rifts, plasma floods) belongs in <see cref="QlippothDungeon"/> later.
    /// </summary>
    private void ApplyRiftEnvironment(EntityUid mapUid)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;
        _atmosphere.SetMapAtmosphere(mapUid, false, new GasMixture(moles, Atmospherics.T20C));

        var gravity = EnsureComp<GravityComponent>(mapUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(mapUid, gravity);
    }

    // Helpers QlippothDungeon implementations build with (they are plain data classes and cannot spawn on their own).

    public void PlaceFloor(EntityUid gridUid, MapGridComponent grid, string tileId, List<Vector2i> positions)
    {
        var tile = new Tile(_tileDefinitions[tileId].TileId);
        var tiles = new List<(Vector2i Index, Tile Tile)>(positions.Count);
        foreach (var position in positions)
            tiles.Add((position, tile));
        _maps.SetTiles(gridUid, grid, tiles);
    }

    public EntityUid PlaceEntity(EntityUid gridUid, string prototype, Vector2 position)
    {
        return Spawn(prototype, new EntityCoordinates(gridUid, position));
    }

    public EntityUid PlaceObjective(EntityUid gridUid, EntityUid gate, string prototype, Vector2 position)
    {
        var objective = Spawn(prototype, new EntityCoordinates(gridUid, position));
        var component = EnsureComp<QGateDungeonObjectiveComponent>(objective);
        component.Gate = gate;
        Dirty(objective, component);
        return objective;
    }

    private void DestroyRiftDungeon(RiftDungeon dungeon)
    {
        if (_mapManager.MapExists(dungeon.MapId))
            _mapManager.DeleteMap(dungeon.MapId);
    }

    private void EvacuateDungeon(MapId dungeonMap, MapCoordinates entry, MapCoordinates exit, EntityUid? excluded = null)
    {
        var evacuees = new HashSet<EntityUid>();
        var pending = new Queue<EntityUid>();

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var actor, out _, out var actorXform))
        {
            if (actorXform.MapID == dungeonMap && actor != excluded)
                AddEvacuee(actor);
        }

        var corpses = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (corpses.MoveNext(out var corpse, out var mobState, out var corpseXform))
        {
            if (corpseXform.MapID == dungeonMap && corpse != excluded && _mobState.IsDead(corpse, mobState))
                AddEvacuee(corpse);
        }

        while (pending.TryDequeue(out var evacuee))
        {
            if (TryComp<PullerComponent>(evacuee, out var puller) && puller.Pulling is { } pulled)
                AddRelatedEvacuee(pulled);

            if (TryComp<PullableComponent>(evacuee, out var pullable) && pullable.Puller is { } pullingEntity)
                AddRelatedEvacuee(pullingEntity);
        }

        foreach (var evacuee in evacuees)
        {
            var coordinates = _transform.GetMapCoordinates(evacuee);
            var offset = coordinates.Position - entry.Position;
            _transform.SetMapCoordinates(evacuee, new MapCoordinates(exit.Position + offset, exit.MapId));
        }

        void AddEvacuee(EntityUid entity)
        {
            if (evacuees.Add(entity))
                pending.Enqueue(entity);
        }

        void AddRelatedEvacuee(EntityUid entity)
        {
            if (entity == excluded || !TryComp<TransformComponent>(entity, out var entityXform) ||
                entityXform.MapID != dungeonMap)
                return;

            AddEvacuee(entity);
        }
    }

    private void OnAfterInteract(EntityUid uid, QGateComponent qgate, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !qgate.RiftOpened || qgate.IsBreached || qgate.PortalClosing)
            return;

        if (!_dungeonsByGate.TryGetValue(uid, out var dungeon))
            return;

        _transform.SetMapCoordinates(args.User, dungeon.Entry);
        args.Handled = true;
    }

    private void OnActivateInWorld(EntityUid uid, QGateComponent qgate, ActivateInWorldEvent args)
    {
        if (args.Handled || !qgate.RiftOpened || qgate.IsBreached || qgate.PortalClosing)
            return;

        if (!_dungeonsByGate.TryGetValue(uid, out var dungeon))
            return;

        _transform.SetMapCoordinates(args.User, dungeon.Entry);
        args.Handled = true;
    }
    #endregion

    #region objectives
    private void OnObjectiveInteractHand(EntityUid uid, QGateDungeonObjectiveComponent objective, ref InteractHandEvent args)
    {
        if (args.Handled || objective.Completed || !Exists(objective.Gate))
            return;

        ExecuteObjectiveActions(uid, objective, args.User);
        args.Handled = true;
    }

    private void OnObjectiveActivateInWorld(EntityUid uid, QGateDungeonObjectiveComponent objective, ActivateInWorldEvent args)
    {
        if (args.Handled || objective.Completed || !Exists(objective.Gate))
            return;

        ExecuteObjectiveActions(uid, objective, args.User);
        args.Handled = true;
    }

    private void ExecuteObjectiveActions(EntityUid uid, QGateDungeonObjectiveComponent objective, EntityUid user)
    {
        EntityUid? riftQlippoth = null;
        if (_dungeonsByGate.TryGetValue(objective.Gate, out var dungeon) && dungeon.Qlippoth is { } q && Exists(q))
        {
            riftQlippoth = q;
            _initiation.Dispatch<OnGateObjectiveInteractedInitiation>(q,
                new QlippothGateObjectiveEventArgs(uid, objective.Gate, user, objective.ObjectiveType));
        }

        foreach (var action in objective.Actions)
        {
            if (action.Initiation is not QGateObjectiveInteractInitiation)
                continue;

            foreach (var result in action.Results)
            {
                switch (result)
                {
                    case CompleteQGateObjectiveResult:
                        CompleteObjective(uid, user);
                        break;
                    case PlayQGateObjectiveSoundResult sound:
                        _audio.PlayPvs(new SoundPathSpecifier(sound.SoundPath), uid, AudioParams.Default.WithVolume(sound.Volume));
                        break;
                    case SpawnQGateObjectiveEntityResult spawn:
                        var coordinates = Transform(uid).Coordinates;
                        for (var i = 0; i < spawn.Count; i++)
                            Spawn(spawn.Prototype, coordinates);
                        break;
                    case SignalQlippothObjectiveResult signal:
                        if (riftQlippoth != null)
                            _initiation.Dispatch<OnSignalInitiation>(riftQlippoth.Value, new QlippothSignalEventArgs(uid, signal.Signal));
                        break;
                }
            }
        }
    }

    public void CompleteObjective(EntityUid uid, EntityUid user)
    {
        if (!TryComp<QGateDungeonObjectiveComponent>(uid, out var objective) || objective.Completed)
            return;

        objective.Completed = true;
        Dirty(uid, objective);
        ReportObjectiveCompleted(objective.Gate);

        if (_dungeonsByGate.TryGetValue(objective.Gate, out var dungeon) && dungeon.Qlippoth is { } qlippoth && Exists(qlippoth))
        {
            _initiation.Dispatch<OnGateObjectiveCompletedInitiation>(qlippoth,
                new QlippothGateObjectiveEventArgs(uid, objective.Gate, user, objective.ObjectiveType));
        }
    }

    public void ReportObjectiveCompleted(EntityUid gateUid)
    {
        if (!TryComp<QGateComponent>(gateUid, out var qgate) || qgate.IsBreached || qgate.ObjectiveCompleted)
            return;

        qgate.CompletedObjectives++;
        if (qgate.CompletedObjectives >= qgate.RequiredObjectives)
            TriggerCleared(gateUid, qgate);
        else
            Dirty(gateUid, qgate);
    }
    #endregion

    #region breach / clear / seal
    private void OnBreachSealDeviceUsed(EntityUid uid, QlippothBreachSealDeviceComponent component,
        AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null || !args.CanReach)
            return;

        args.Handled = TryStartBreachSeal(uid, component, args.User, args.Target.Value);
    }

    private void OnBreachSealDeviceActivated(EntityUid uid, QlippothBreachSealDeviceComponent component,
        ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartBreachSeal(uid, component, args.User, args.Target);
    }

    private bool TryStartBreachSeal(EntityUid device, QlippothBreachSealDeviceComponent component,
        EntityUid user, EntityUid target)
    {
        if (!TryComp<QGateComponent>(target, out var qgate) || !qgate.IsBreached)
        {
            _popup.PopupEntity(Loc.GetString("qgate-seal-invalid-target"), user, user);
            return false;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, component.SealDuration,
            new SealQlippothGateDoAfterEvent(), device, target: target, used: device)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            Broadcast = true
        };

        return _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnBreachSealCompleted(EntityUid device, QlippothBreachSealDeviceComponent component,
        SealQlippothGateDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        SealBreachedGate(target);
        args.Handled = true;
    }

    private bool SealBreachedGate(EntityUid target)
    {
        if (!TryComp<QGateComponent>(target, out var qgate) || !qgate.IsBreached)
            return false;

        if (_dungeonsByGate.Remove(target, out var dungeon))
        {
            EvacuateDungeon(dungeon.MapId, dungeon.Entry, _transform.GetMapCoordinates(target), dungeon.Qlippoth);
            DestroyRiftDungeon(dungeon);
        }

        if (_returnPortalsByGate.Remove(target, out var returnPortal))
            QueueDel(returnPortal);

        if (_breachEffectsByGate.Remove(target, out var breachEffects))
        {
            foreach (var effect in breachEffects)
                QueueDel(effect);
        }

        QueueDel(target);
        _chatSystem.DispatchGlobalAnnouncement(
            Loc.GetString("qgate-announcement-sealed", ("location", qgate.LocationName)),
            "CentCom Science", playSound: true, colorOverride: Color.FromHex("#20B2AA"));
        return true;
    }

    /// <summary>Time ran out: the rift collapses and the Qlippoth that was inside appears on the station.</summary>
    public void TriggerBreach(EntityUid uid, QGateComponent? qgate = null)
    {
        if (!Resolve(uid, ref qgate) || qgate.IsBreached || qgate.IsCleared)
            return;

        qgate.IsBreached = true;
        if (_dungeonsByGate.Remove(uid, out var dungeon))
        {
            EvacuateDungeon(dungeon.MapId, dungeon.Entry, _transform.GetMapCoordinates(uid), dungeon.Qlippoth);
            DestroyRiftDungeon(dungeon);
        }

        var coordinates = Transform(uid).Coordinates;
        if (qgate.QlippothPrototype is { } prototype)
        {
            var spawned = Spawn(prototype, coordinates);
            _initiation.Dispatch<OnArrivedInitiation>(spawned, new QlippothArrivalEventArgs(uid, QlippothArrivalKind.GateBreach));
        }
        _initiation.DispatchAll<OnAnyGateBreachedInitiation>(new QlippothTargetEventArgs(uid));

        _breachEffectsByGate[uid] = new List<EntityUid>
        {
            Spawn("EffectSparks", coordinates),
            Spawn("EffectVoidBlink", coordinates)
        };
        Dirty(uid, qgate);
        var breachMsg = Loc.GetString("qgate-announcement-breach", ("location", qgate.LocationName));
        _chatSystem.DispatchGlobalAnnouncement(breachMsg, "CentCom Emergency Alert", playSound: true, colorOverride: Color.FromHex("#DC143C"));
    }

    /// <summary>All objectives done: open the way back, then CloseRift() sells the Qlippoth to the market.</summary>
    public void TriggerCleared(EntityUid uid, QGateComponent? qgate = null)
    {
        if (!Resolve(uid, ref qgate) || qgate.IsCleared || qgate.IsBreached)
            return;

        qgate.IsCleared = true;
        qgate.ObjectiveCompleted = true;
        qgate.PortalClosing = true;
        qgate.PortalCloseAt = _timing.CurTime + TimeSpan.FromMinutes(3);

        if (_dungeonsByGate.TryGetValue(uid, out var dungeon))
        {
            var returnPortal = Spawn("ContainmentDimensionExitPortal", dungeon.Entry);
            _portals.RegisterReturnPortal(returnPortal, _transform.GetMapCoordinates(uid));
            _returnPortalsByGate[uid] = returnPortal;
            if (dungeon.Qlippoth is { } riftQlippoth && Exists(riftQlippoth))
                _initiation.Dispatch<OnGateClearedInitiation>(riftQlippoth, new QlippothTargetEventArgs(uid));
        }

        Dirty(uid, qgate);
        var clearMsg = Loc.GetString("qgate-announcement-cleared", ("location", qgate.LocationName));
        _chatSystem.DispatchGlobalAnnouncement(clearMsg, "CentCom Notice", playSound: true, colorOverride: Color.FromHex("#32CD32"));
    }

    private void CloseRift(EntityUid uid, QGateComponent qgate)
    {
        if (_dungeonsByGate.Remove(uid, out var dungeon))
        {
            EvacuateDungeon(dungeon.MapId, dungeon.Entry, _transform.GetMapCoordinates(uid), dungeon.Qlippoth);
            DestroyRiftDungeon(dungeon);
        }

        if (_returnPortalsByGate.Remove(uid, out var returnPortal))
            QueueDel(returnPortal);

        qgate.PortalClosing = false;
        if (qgate.QlippothPrototype is { } prototype)
            _market.AddSecuredQlippothToMarket(prototype, qgate.Phase);
        QueueDel(uid);
    }
    #endregion
}
