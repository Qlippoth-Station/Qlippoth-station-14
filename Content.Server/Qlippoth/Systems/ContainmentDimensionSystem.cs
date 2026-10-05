using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.Map.Components;
using Robust.Shared.Map;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Qlippoth.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared.Interaction;
using Robust.Shared.Placement;
using Robust.Shared.Network;
using Robust.Server.Player;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Qlippoth.Systems;

public sealed partial class ContainmentDimensionSystem : EntitySystem
{
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private QlippothActionInitiationSystem _initiation = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public MapId ContainmentMapId { get; private set; } = MapId.Nullspace;
    private bool _layoutBuilt;
    private EntityUid _containmentGrid = EntityUid.Invalid;
    private int _nextChamberIndex;
    private readonly Dictionary<EntityUid, EntityUid> _breachAlarms = new();
    private static readonly (ContainmentDepartment Department, Vector2 Position)[] DepartmentPortals =
    {
        (ContainmentDepartment.Cargo, new Vector2(-15.5f, 7.5f)),
        (ContainmentDepartment.Civilian, new Vector2(-5.5f, 7.5f)),
        (ContainmentDepartment.Command, new Vector2(4.5f, 7.5f)),
        (ContainmentDepartment.Engineering, new Vector2(14.5f, 7.5f)),
        (ContainmentDepartment.Medical, new Vector2(-15.5f, 0.5f)),
        (ContainmentDepartment.Security, new Vector2(-5.5f, 0.5f)),
        (ContainmentDepartment.Science, new Vector2(4.5f, 0.5f)),
        (ContainmentDepartment.Silicon, new Vector2(14.5f, 0.5f)),
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlacementEntityEvent>(OnChamberPlacement);
        SubscribeLocalEvent<ContainmentChamberComponent, EntityTerminatingEvent>(OnChamberTerminating);
#pragma warning disable CS0618 // DamageChangedEvent is obsolete upstream; see QlippothDamagedEventArgs (QlippothInitiation.cs) for why we still use it.
           SubscribeLocalEvent<ContainmentChamberComponent, DamageChangedEvent>(OnChamberDamaged);
#pragma warning restore CS0618
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var chambers = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
        while (chambers.MoveNext(out _, out var chamber, out var chamberTransform))
        {
            if (!chamber.IsOccupied || chamber.ContainedQlippoth is not { } qlippoth)
                continue;

            if (!TryComp(qlippoth, out TransformComponent? qlippothTransform) ||
                qlippothTransform.MapID != chamberTransform.MapID)
            {
                TryClearChamberOccupant(chamberTransform.Owner, qlippoth);
                continue;
            }

            var offset = qlippothTransform.Coordinates.Position - chamberTransform.Coordinates.Position;
            if (chamber.IsBreached)
            {
                var direction = offset.LengthSquared() > 0.01f
                    ? Vector2.Normalize(offset)
                    : Vector2.UnitY;
                _transform.SetCoordinates(qlippoth,
                    qlippothTransform.Coordinates.Offset(direction * chamber.EscapeSpeed * frameTime));

                if (offset.Length() > chamber.ContainmentRadius + 1f)
                {
                    TryClearChamberOccupant(chamberTransform.Owner, qlippoth);
                    _initiation.Dispatch<OnEscapedContainmentInitiation>(qlippoth, new QlippothTargetEventArgs(chamberTransform.Owner));
                }
            }
            else if (TryComp<PhysicsComponent>(qlippoth, out var physics) &&
                     offset.Length() > chamber.ContainmentRadius - 0.5f)
            {
                var distance = offset.Length();
                var direction = distance > 0.01f ? offset / distance : Vector2.UnitY;
                var outwardSpeed = MathF.Max(0f, Vector2.Dot(physics.LinearVelocity, direction));
                var penetration = MathF.Max(0f, distance - chamber.ContainmentRadius);
                var impulseMagnitude = physics.Mass * (outwardSpeed + penetration * 4f + 0.5f) * frameTime;
                _physics.ApplyLinearImpulse(qlippoth, -direction * impulseMagnitude, body: physics);
            }
        }
    }

#pragma warning disable CS0618 // DamageChangedEvent is obsolete upstream; see QlippothDamagedEventArgs (QlippothInitiation.cs) for why we still use it.
    private void OnChamberDamaged(EntityUid uid, ContainmentChamberComponent chamber, DamageChangedEvent args)
    {
        var damage = _damageable.GetTotalDamage((uid, args.Damageable));
        if (chamber.IsBreached && damage < chamber.BreachThreshold)
        {
            chamber.IsBreached = false;
            Dirty(uid, chamber);
            if (_breachAlarms.Remove(uid, out var alarm) && Exists(alarm))
                QueueDel(alarm);
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString("containment-chamber-repaired", ("chamber", chamber.ChamberId)),
                "CentCom Containment", playSound: true, colorOverride: Color.FromHex("#32CD32"));
            return;
        }

        if (chamber.IsBreached || damage < chamber.BreachThreshold)
            return;

        chamber.IsBreached = true;
        Dirty(uid, chamber);
        var alarmCoordinates = Transform(uid).Coordinates;
        _breachAlarms[uid] = Spawn("QlippothContainmentBreachAlarm", alarmCoordinates);
        if (chamber.ContainedQlippoth is { } contained && Exists(contained))
            _initiation.Dispatch<OnContainmentBreachedInitiation>(contained, new QlippothTargetEventArgs(uid));
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("containment-chamber-breach", ("chamber", chamber.ChamberId)),
            "CentCom Emergency Alert", playSound: true, colorOverride: Color.FromHex("#DC143C"));
    }
#pragma warning restore CS0618

    private void OnChamberTerminating(
        EntityUid uid,
        ContainmentChamberComponent chamber,
        ref EntityTerminatingEvent args)
    {
        if (_breachAlarms.Remove(uid, out var alarm) && Exists(alarm))
            QueueDel(alarm);

        if (!chamber.IsOccupied || chamber.ContainedQlippoth is not { } qlippoth ||
            !Exists(qlippoth) || !TryComp<TransformComponent>(uid, out var chamberTransform) ||
            !IsContainmentDimension(chamberTransform.MapID) ||
            !TryComp<TransformComponent>(qlippoth, out _))
            return;

        _transform.SetCoordinates(qlippoth, chamberTransform.Coordinates.Offset(new Vector2(0f, -3f)));
        _initiation.Dispatch<OnEscapedContainmentInitiation>(qlippoth, new QlippothTargetEventArgs(uid));
    }

    public EntityUid CreateEngineeringBlueprint(EntityUid console)
    {
        EnsureContainmentDimensionCreated();
        return Spawn("ContainmentChamberConstructionKit", Transform(console).Coordinates);
    }

    private void OnChamberPlacement(PlacementEntityEvent args)
    {
        if (args.PlacementEventAction != PlacementEventAction.Create)
            return;

        // Verify the user placing the chamber is holding a chamber construction kit
        if (args.PlacerNetUserId is not { } userId ||
            !_players.TryGetSessionById(userId, out var session) ||
            session.AttachedEntity is not { } player ||
            !_hands.TryGetActiveItem(new Entity<HandsComponent?>(player, null), out var held) ||
            !TryComp<QlippothChamberConstructionKitComponent>(held, out _))
        {
            if (HasComp<ContainmentChamberComponent>(args.EditedEntity))
                QueueDel(args.EditedEntity);
            return;
        }

        var mapCoordinates = _transform.ToMapCoordinates(args.Coordinates);
        var pos = args.Coordinates.Position;
        var snappedPos = new Vector2(MathF.Floor(pos.X) + 0.5f, MathF.Floor(pos.Y) + 0.5f);
        if (!IsContainmentDimension(mapCoordinates.MapId) || args.Coordinates.EntityId != _containmentGrid ||
            !IsEngineeringBuildablePosition(snappedPos) || !CanPlaceChamber(snappedPos))
        {
            QueueDel(args.EditedEntity);
            if (IsContainmentDimension(mapCoordinates.MapId) && args.Coordinates.EntityId == _containmentGrid &&
                !IsEngineeringBuildablePosition(snappedPos))
            {
                _popup.PopupEntity(Loc.GetString("containment-chamber-invalid-build-zone"), player, player);
            }
            return;
        }

        QueueDel(args.EditedEntity);
        if (!TryBuildEngineeringChamberAt(snappedPos, out _, out _))
            return;

        QueueDel(held.Value);
    }

    public void EnsureContainmentDimensionCreated()
    {
        if (ContainmentMapId != MapId.Nullspace && _mapManager.MapExists(ContainmentMapId))
        {
            if (_containmentGrid != EntityUid.Invalid && Exists(_containmentGrid))
                return;

            _containmentGrid = EntityUid.Invalid;
            foreach (var grid in _mapManager.GetAllGrids(ContainmentMapId))
            {
                _containmentGrid = grid.Owner;
                break;
            }

            if (_containmentGrid == EntityUid.Invalid)
            {
                _layoutBuilt = false;
                BuildContainmentLayout();
            }

            return;
        }

        var mapUid = _maps.CreateMap(out var mapId);
        ContainmentMapId = mapId;
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;
        _atmosphere.SetMapAtmosphere(mapUid, false, new GasMixture(moles, Atmospherics.T20C));
        var gravity = EnsureComp<GravityComponent>(mapUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(mapUid, gravity);
        BuildContainmentLayout();
    }

    public bool IsContainmentDimension(MapId mapId)
    {
        return mapId != MapId.Nullspace && mapId == ContainmentMapId;
    }

    /// <summary>
    /// Resolves the arrival point for a map-placed department portal. The positions are within
    /// the generated shared facility grid; station map authors can place the matching entry
    /// prototype in that department when the custom station map is created.
    /// </summary>
    public bool TryGetDepartmentPortalCoordinates(ContainmentDepartment department, out MapCoordinates coordinates)
    {
        EnsureContainmentDimensionCreated();
        foreach (var (candidate, position) in DepartmentPortals)
        {
            if (candidate != department)
                continue;

            coordinates = new MapCoordinates(position, ContainmentMapId);
            return IsDepartmentPortalReady(coordinates);
        }

        coordinates = default;
        return false;
    }

    public bool IsDepartmentPortalReady(ContainmentDepartment department)
    {
        foreach (var (candidate, position) in DepartmentPortals)
        {
            if (candidate == department)
                return IsDepartmentPortalReady(new MapCoordinates(position, ContainmentMapId));
        }

        return false;
    }

    private bool IsDepartmentPortalReady(MapCoordinates coordinates)
    {
        return ContainmentMapId != MapId.Nullspace &&
               _containmentGrid != EntityUid.Invalid &&
               Exists(_containmentGrid) &&
               _mapManager.MapExists(ContainmentMapId) &&
               _mapManager.TryFindGridAt(coordinates, out var gridUid, out _) &&
               gridUid == _containmentGrid;
    }

    public List<QlippothAvailableChamber> GetAvailableChambers()
    {
        var chambersAvailable = new List<QlippothAvailableChamber>();
        var chamberIds = new HashSet<string>(StringComparer.Ordinal);
        var duplicateChamberIds = new HashSet<string>(StringComparer.Ordinal);
        var chambers = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
        while (chambers.MoveNext(out _, out var chamber, out var xform))
        {
            if (xform.MapID != ContainmentMapId || chamber.ChamberId.Length == 0)
                continue;

            if (!chamberIds.Add(chamber.ChamberId))
                duplicateChamberIds.Add(chamber.ChamberId);

            if (chamber.IsBuilt && !chamber.IsOccupied && !chamber.IsBreached)
                chambersAvailable.Add(new QlippothAvailableChamber(chamber.ChamberId, chamber.Sector));
        }

        chambersAvailable.RemoveAll(chamber => duplicateChamberIds.Contains(chamber.ChamberId));
        chambersAvailable.Sort((left, right) =>
        {
            var sectorOrder = StringComparer.Ordinal.Compare(left.Sector, right.Sector);
            return sectorOrder != 0 ? sectorOrder : StringComparer.Ordinal.Compare(left.ChamberId, right.ChamberId);
        });
        return chambersAvailable;
    }

    public bool IsChamberAvailable(string chamberId)
    {
        if (string.IsNullOrWhiteSpace(chamberId))
            return false;

        EntityUid foundUid = EntityUid.Invalid;
        ContainmentChamberComponent? foundChamber = null;
        var chambers = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
        while (chambers.MoveNext(out _, out var chamber, out var xform))
        {
            if (xform.MapID != ContainmentMapId || chamber.ChamberId != chamberId)
                continue;

            if (foundChamber != null)
                return false;

            foundUid = xform.Owner;
            foundChamber = chamber;
        }

        return foundChamber is { IsBuilt: true, IsOccupied: false, IsBreached: false } &&
               foundUid != EntityUid.Invalid;
    }

    public bool TryAssignQlippothToChamber(EntityUid chamberUid, EntityUid qlippothUid)
    {
        if (!TryComp<ContainmentChamberComponent>(chamberUid, out var chamber) ||
            !TryComp<TransformComponent>(chamberUid, out var chamberTransform) ||
            !Exists(qlippothUid) ||
            !HasComp<QlippothComponent>(qlippothUid) ||
            !IsContainmentDimension(chamberTransform.MapID) ||
            chamber.ChamberId.Length == 0 ||
            !chamber.IsBuilt || chamber.IsOccupied || chamber.IsBreached)
            return false;

        var chambers = EntityQueryEnumerator<ContainmentChamberComponent>();
        while (chambers.MoveNext(out var otherUid, out var other))
        {
            if (otherUid != chamberUid && other.ContainedQlippoth == qlippothUid)
                return false;
        }

        chamber.IsOccupied = true;
        chamber.ContainedQlippoth = qlippothUid;
        Dirty(chamberUid, chamber);
        return true;
    }

    public bool TryClearChamberOccupant(EntityUid chamberUid, EntityUid expectedQlippoth)
    {
        if (!TryComp<ContainmentChamberComponent>(chamberUid, out var chamber) ||
            chamber.ContainedQlippoth != expectedQlippoth)
            return false;

        chamber.IsOccupied = false;
        chamber.ContainedQlippoth = null;
        Dirty(chamberUid, chamber);
        return true;
    }

    public bool IsCorruptionProtected(EntityUid target, EntityUid? sourceQlippoth, out EntityUid protectingChamber)
    {
        protectingChamber = EntityUid.Invalid;
        if (!TryComp<TransformComponent>(target, out var targetTransform))
            return false;

        var targetPosition = _transform.GetMapCoordinates(target).Position;
        MapCoordinates? sourceCoordinates = sourceQlippoth is { } source &&
                                            TryComp<TransformComponent>(source, out _)
            ? _transform.GetMapCoordinates(source)
            : null;

        var chambers = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
        while (chambers.MoveNext(out var chamberUid, out var chamber, out var chamberTransform))
        {
            if (chamber.IsBreached || !chamber.IsBuilt || chamberTransform.MapID != targetTransform.MapID)
                continue;

            var chamberPosition = _transform.GetMapCoordinates(chamberUid).Position;
            var targetInside = Vector2.Distance(targetPosition, chamberPosition) <= chamber.ContainmentRadius;
            var sourceInside = chamber.ContainedQlippoth is { } occupant &&
                               sourceQlippoth == occupant &&
                               sourceCoordinates is { } coordinates &&
                               coordinates.MapId == chamberTransform.MapID &&
                               Vector2.Distance(coordinates.Position, chamberPosition) <= chamber.ContainmentRadius;

            if (sourceInside == targetInside)
                continue;

            protectingChamber = chamberUid;
            return true;
        }

        return false;
    }

    private void BuildContainmentLayout()
    {
        if (_layoutBuilt)
            return;

        var gridEntity = _mapManager.CreateGridEntity(ContainmentMapId);
        var gridUid = gridEntity.Owner;
        _containmentGrid = gridUid;
        var grid = gridEntity.Comp;
        var floor = new Tile(_tileDefinitions["FloorSteel"].TileId);
        const int minX = -20;
        const int maxX = 19;
        const int minY = -12;
        const int maxY = 11;

        var roomTiles = new List<(Vector2i Index, Tile Tile)>();
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
            roomTiles.Add((new Vector2i(x, y), floor));

        _maps.SetTiles(gridUid, grid, roomTiles);

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            if (x != minX && x != maxX && y != minY && y != maxY)
                continue;

            Spawn("WallReinforced", new EntityCoordinates(gridUid, new Vector2(x + 0.5f, y + 0.5f)));
        }

        BuildPowerGrid(gridUid);
        SpawnContainmentLights(gridUid);
        BuildDepartmentSectors(gridUid);
        foreach (var (department, position) in DepartmentPortals)
        {
            var portal = Spawn("ContainmentDimensionExitPortal", new EntityCoordinates(gridUid, position));
            if (!TryComp<ContainmentPortalComponent>(portal, out var portalComponent))
                continue;

            portalComponent.Department = department;
            Dirty(portal, portalComponent);
            _metadata.SetEntityName(portal, $"{department} Containment Return Portal");
        }

        // These consoles are shared by the containment facility rather than tied to a chamber.
        Spawn("ComputerQGateTracker", new EntityCoordinates(gridUid, new Vector2(-4.5f, -10.5f)));
        Spawn("ComputerContainmentBlueprint", new EntityCoordinates(gridUid, new Vector2(3.5f, -10.5f)));
        Spawn("ComputerQlippothMarket", new EntityCoordinates(gridUid, new Vector2(11.5f, -10.5f)));

        var commandChamber = SpawnChamberRoom("CommandStarterContainmentChamber", new Vector2(12.5f, -7.5f));
        if (commandChamber != EntityUid.Invalid)
            SpawnResearchConsole(new Vector2(12.5f, -7.5f), commandChamber);
        _layoutBuilt = true;
    }

    private EntityUid SpawnChamberRoom(string chamberPrototype, Vector2 center)
    {
        if (_containmentGrid == EntityUid.Invalid)
            return EntityUid.Invalid;

        for (var x = -2; x <= 2; x++)
        for (var y = -2; y <= 2; y++)
        {
            if (x != -2 && x != 2 && y != -2 && y != 2)
                continue;

            // Keep the south wall open as the chamber entrance.
            if (y == -2 && x == 0)
                continue;

            Spawn("WallReinforced", new EntityCoordinates(_containmentGrid, center + new Vector2(x, y)));
        }

        return Spawn(chamberPrototype, new EntityCoordinates(_containmentGrid, center));
    }

    public bool TryBuildEngineeringChamber(out EntityUid chamberUid, out string chamberId)
    {
        EnsureContainmentDimensionCreated();
        chamberUid = EntityUid.Invalid;
        chamberId = string.Empty;

        var positions = new[]
        {
            new Vector2(-12.5f, -7.5f),
            new Vector2(-4.5f, -7.5f),
            new Vector2(3.5f, -7.5f),
            new Vector2(12.5f, -7.5f)
        };

        if (_containmentGrid == EntityUid.Invalid)
            return false;

        for (var i = 0; i < positions.Length; i++)
        {
            var index = (_nextChamberIndex + i) % positions.Length;
            var position = positions[index];
            var occupied = false;
            var query = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapID == ContainmentMapId && Vector2.Distance(xform.Coordinates.Position, position) < 0.75f)
                {
                    occupied = true;
                    break;
                }
            }

            if (occupied)
                continue;

            chamberId = $"engineering-{index + 1}";
            SpawnChamberRoom("ContainmentChamberMarker", position);
            chamberUid = GetChamberAt(position);
            if (chamberUid == EntityUid.Invalid)
                return false;

            if (!TryComp<ContainmentChamberComponent>(chamberUid, out var chamber))
                return false;

            chamber.ChamberId = chamberId;
            chamber.Sector = "Engineering";
            chamber.IsBuilt = true;
            Dirty(chamberUid, chamber);
            _metadata.SetEntityName(chamberUid, $"Engineering Containment Chamber {index + 1}");
            SpawnResearchConsole(position, chamberUid);
            _nextChamberIndex = index + 1;
            return true;
        }

        return false;
    }

    private bool TryBuildEngineeringChamberAt(Vector2 position, out EntityUid chamberUid, out string chamberId)
    {
        chamberUid = EntityUid.Invalid;
        chamberId = string.Empty;
        if (!IsEngineeringBuildablePosition(position) || !CanPlaceChamber(position))
            return false;

        var index = _nextChamberIndex++;
        chamberId = $"engineering-{index + 1}";
        SpawnChamberRoom("ContainmentChamberMarker", position);
        chamberUid = GetChamberAt(position);
        if (chamberUid == EntityUid.Invalid || !TryComp<ContainmentChamberComponent>(chamberUid, out var chamber))
            return false;

        chamber.ChamberId = chamberId;
        chamber.Sector = "Engineering";
        chamber.IsBuilt = true;
        Dirty(chamberUid, chamber);
        _metadata.SetEntityName(chamberUid, $"Engineering Containment Chamber {index + 1}");
        SpawnResearchConsole(position, chamberUid);
        return true;
    }

    private static bool IsEngineeringBuildablePosition(Vector2 position)
    {
        return MathF.Abs(position.Y + 7.5f) <= 0.01f;
    }

    private bool CanPlaceChamber(Vector2 center)
    {
        if (_containmentGrid == EntityUid.Invalid || !TryComp<MapGridComponent>(_containmentGrid, out var grid))
            return false;

        var centerTile = new Vector2i((int) MathF.Floor(center.X), (int) MathF.Floor(center.Y));
        for (var x = -2; x <= 2; x++)
        for (var y = -2; y <= 2; y++)
        {
            var tile = centerTile + new Vector2i(x, y);
            if (_maps.GetTileRef(_containmentGrid, grid, tile).Tile.IsEmpty)
                return false;

            var anchored = _maps.GetAnchoredEntitiesEnumerator(_containmentGrid, grid, tile);
            if (anchored.MoveNext(out _))
                return false;
        }

        return true;
    }

    private void SpawnResearchConsole(Vector2 center, EntityUid chamberUid)
    {
        var position = center + new Vector2(1f, -3.5f);
        var consoleUid = Spawn("ComputerQlippothResearch", new EntityCoordinates(_containmentGrid, position));
        if (!TryComp<QlippothResearchConsoleComponent>(consoleUid, out var console))
            return;

        console.LinkedChamber = chamberUid;
        Dirty(consoleUid, console);
    }

    private EntityUid GetChamberAt(Vector2 position)
    {
        var query = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == ContainmentMapId && Vector2.Distance(xform.Coordinates.Position, position) < 0.75f)
                return uid;
        }

        return EntityUid.Invalid;
    }

    private void BuildPowerGrid(EntityUid gridUid)
    {
        Spawn("DebugAPCRecharging", new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f)));

        for (var x = -5; x <= 4; x++)
            Spawn("CableApcExtension", new EntityCoordinates(gridUid, new Vector2(x + 0.5f, 0.5f)));

        for (var x = -18; x <= 18; x++)
            Spawn("CableApcExtension", new EntityCoordinates(gridUid, new Vector2(x + 0.5f, 0.5f)));
    }

    private void BuildDepartmentSectors(EntityUid gridUid)
    {
        foreach (var (_, center) in DepartmentPortals)
        {
            for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
            {
                if (x != -3 && x != 3 && y != -3 && y != 3)
                    continue;

                // Each sector opens eastward into the shared facility corridor.
                if (x == 3 && y == 0)
                    continue;

                Spawn("WallReinforced",
                    new EntityCoordinates(gridUid, center + new Vector2(x, y)));
            }
        }
    }

    private void SpawnContainmentLights(EntityUid gridUid)
    {
        foreach (var position in new[]
        {
            new Vector2(-14.5f, 7.5f), new Vector2(0.5f, 7.5f), new Vector2(14.5f, 7.5f),
            new Vector2(-14.5f, -6.5f), new Vector2(0.5f, -6.5f), new Vector2(14.5f, -6.5f)
        })
        {
            Spawn("QlippothContainmentLight", new EntityCoordinates(gridUid, position));
        }
    }

    private void BuildSector(EntityUid gridUid, MapGridComponent grid, Vector2i origin, int width, int height,
        Tile floor, string consolePrototype, string sector, string wallPrototype)
    {
        var tiles = new List<(Vector2i Index, Tile Tile)>();
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            tiles.Add((origin + new Vector2i(x, y), floor));

        _maps.SetTiles(gridUid, grid, tiles);

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            if (x != 0 && x != width - 1 && y != 0 && y != height - 1)
                continue;

            // Open the side facing the central corridor.
            var opensToCorridor =
                (origin.X < 0 && x == width - 1 && y is >= 6 and <= 9) ||
                (origin.X >= 0 && x == 0 && y is >= 6 and <= 9) ||
                (origin.Y > 0 && y == 0 && x is >= 10 and <= 13) ||
                (origin.Y < 0 && y == height - 1 && x is >= 10 and <= 13);

            if (!opensToCorridor)
                Spawn(wallPrototype, new EntityCoordinates(gridUid, new Vector2(origin.X + x + 0.5f, origin.Y + y + 0.5f)));
        }

        var consolePosition = new Vector2(origin.X + 3.5f, origin.Y + 3.5f);
        var console = Spawn(consolePrototype, new EntityCoordinates(gridUid, consolePosition));
        _metadata.SetEntityName(console, $"{sector} Qlippoth Console");
    }
}
