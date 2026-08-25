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
using Content.Shared.Interaction;
using Robust.Shared.Placement;
using Robust.Shared.Network;
using Robust.Server.Player;

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
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    public MapId ContainmentMapId { get; private set; } = MapId.Nullspace;
    private bool _layoutBuilt;
    private EntityUid _containmentGrid = EntityUid.Invalid;
    private int _nextChamberIndex;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlacementEntityEvent>(OnChamberPlacement);
           SubscribeLocalEvent<ContainmentChamberComponent, DamageChangedEvent>(OnChamberDamaged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var chambers = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
        while (chambers.MoveNext(out _, out var chamber, out var chamberTransform))
        {
            if (!chamber.IsOccupied || chamber.ContainedQlippoth is not { } qlippoth ||
                !TryComp<TransformComponent>(qlippoth, out var qlippothTransform) ||
                qlippothTransform.MapID != chamberTransform.MapID)
                continue;

            var offset = qlippothTransform.Coordinates.Position - chamberTransform.Coordinates.Position;
            if (!chamber.IsBreached && offset.Length() > chamber.ContainmentRadius)
                _transform.SetCoordinates(qlippoth, chamberTransform.Coordinates);
            else if (chamber.IsBreached)
            {
                var direction = offset.LengthSquared() > 0.01f
                    ? Vector2.Normalize(offset)
                    : Vector2.UnitY;
                _transform.SetCoordinates(qlippoth,
                    qlippothTransform.Coordinates.Offset(direction * chamber.EscapeSpeed * frameTime));

                if (offset.Length() > chamber.ContainmentRadius + 1f &&
                    TryComp<QlippothComponent>(qlippoth, out var qlippothComponent))
                {
                    qlippothComponent.ContainmentChamberId = null;
                    chamber.IsOccupied = false;
                    chamber.ContainedQlippoth = null;
                    Dirty(qlippoth, qlippothComponent);
                    Dirty(chamberTransform.Owner, chamber);
                }
            }
        }
    }

    private void OnChamberDamaged(EntityUid uid, ContainmentChamberComponent chamber, DamageChangedEvent args)
    {
        if (chamber.IsBreached || _damageable.GetTotalDamage((uid, args.Damageable)) < chamber.BreachThreshold)
            return;

        chamber.IsBreached = true;
        Dirty(uid, chamber);
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("containment-chamber-breach", ("chamber", chamber.ChamberId)),
            "CentCom Emergency Alert", playSound: true, colorOverride: Color.FromHex("#DC143C"));
    }

    public EntityUid CreateEngineeringBlueprint(EntityUid console)
    {
        EnsureContainmentDimensionCreated();
        return Spawn("BlueprintContainmentChamber", Transform(console).Coordinates);
    }

    private void OnChamberPlacement(PlacementEntityEvent args)
    {
        if (args.PlacementEventAction != PlacementEventAction.Create ||
            !TryComp<QlippothChamberConstructionKitComponent>(args.EditedEntity, out _))
            return;

        var mapCoordinates = _transform.ToMapCoordinates(args.Coordinates);
        if (!IsContainmentDimension(mapCoordinates.MapId) || args.Coordinates.EntityId != _containmentGrid ||
            !CanPlaceChamber(args.Coordinates.Position))
        {
            QueueDel(args.EditedEntity);
            return;
        }

        QueueDel(args.EditedEntity);
        if (!TryBuildEngineeringChamberAt(args.Coordinates.Position, out _, out _))
            return;

        if (args.PlacerNetUserId is { } userId && _players.TryGetSessionById(userId, out var session) &&
            session.AttachedEntity is { } player &&
            _hands.TryGetActiveItem(new Entity<HandsComponent?>(player, null), out var held) &&
            TryComp<QlippothChamberConstructionKitComponent>(held, out _))
        {
            QueueDel(held.Value);
        }
    }

    public void EnsureContainmentDimensionCreated()
    {
        if (ContainmentMapId != MapId.Nullspace && _mapManager.MapExists(ContainmentMapId))
            return;

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
        Spawn("ContainmentDimensionExitPortal", new EntityCoordinates(gridUid, new Vector2(-15.5f, 0.5f)));

        // These consoles are shared by the containment facility rather than tied to a chamber.
        Spawn("ComputerQGateTracker", new EntityCoordinates(gridUid, new Vector2(-4.5f, 9.5f)));
        Spawn("ComputerContainmentBlueprint", new EntityCoordinates(gridUid, new Vector2(3.5f, 9.5f)));
        Spawn("ComputerQlippothMarket", new EntityCoordinates(gridUid, new Vector2(11.5f, 9.5f)));

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
        if (!CanPlaceChamber(position))
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
