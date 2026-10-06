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
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Placement;
using Robust.Shared.Network;
using Robust.Server.Player;
using Robust.Shared.Utility;

namespace Content.Server.Qlippoth.Systems;

public sealed partial class ContainmentDimensionSystem : EntitySystem
{
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    public MapId ContainmentMapId { get; private set; } = MapId.Nullspace;
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

                if (offset.Length() > chamber.ContainmentRadius + 1f)
                {
                    chamber.IsOccupied = false;
                    chamber.ContainedQlippoth = null;
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

        if (!_mapLoader.TryLoadMap(
                new ResPath("/Maps/qlippoth_dimension.yml"),
                out var map,
                out var grids,
                DeserializationOptions.Default with { InitializeMaps = true }))
        {
            throw new InvalidOperationException("Failed to load the static Qlippoth Containment Dimension map.");
        }

        if (grids.Count != 1)
            throw new InvalidOperationException($"The Qlippoth Containment Dimension map must contain one grid, but loaded {grids.Count}.");

        var mapUid = map.Value.Owner;
        ContainmentMapId = map.Value.Comp.MapId;
        _containmentGrid = grids.Single().Owner;
        _metadata.SetEntityName(mapUid, "Qlippoth Containment Dimension");
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;
        _atmosphere.SetMapAtmosphere(mapUid, false, new GasMixture(moles, Atmospherics.T20C));
        var gravity = EnsureComp<GravityComponent>(mapUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(mapUid, gravity);
        LinkStaticResearchConsole();
    }

    public bool IsContainmentDimension(MapId mapId)
    {
        return mapId != MapId.Nullspace && mapId == ContainmentMapId;
    }

    public bool TryGetContainmentEntryCoordinates(out MapCoordinates coordinates)
    {
        EnsureContainmentDimensionCreated();
        var portals = EntityQueryEnumerator<ContainmentPortalComponent, TransformComponent>();
        while (portals.MoveNext(out var uid, out var portal, out var xform))
        {
            if (xform.MapID != ContainmentMapId || !portal.IsExitPortal)
                continue;

            coordinates = _transform.GetMapCoordinates(uid);
            return true;
        }

        coordinates = default;
        return false;
    }

    private void LinkStaticResearchConsole()
    {
        var chamber = GetChamberAt(new Vector2(-17.5f, 36.5f));
        if (chamber == EntityUid.Invalid)
            throw new InvalidOperationException("The static Containment Dimension map is missing its starter chamber.");

        var consoles = EntityQueryEnumerator<QlippothResearchConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out var console, out var xform))
        {
            if (xform.MapID != ContainmentMapId || Vector2.Distance(xform.Coordinates.Position, new Vector2(-18.5f, 36.5f)) > 0.75f)
                continue;

            console.LinkedChamber = chamber;
            Dirty(uid, console);
            return;
        }

        throw new InvalidOperationException("The static Containment Dimension map is missing its starter research console.");
    }

    private EntityUid SpawnChamberRoom(string chamberPrototype, Vector2 center)
    {
        if (_containmentGrid == EntityUid.Invalid || !TryComp<MapGridComponent>(_containmentGrid, out var grid))
            return EntityUid.Invalid;

        var floor = new Tile(_tileDefinitions["FloorSteel"].TileId);
        var centerTile = new Vector2i((int) MathF.Floor(center.X), (int) MathF.Floor(center.Y));
        for (var x = -2; x <= 2; x++)
        for (var y = -2; y <= 2; y++)
        {
            var tile = centerTile + new Vector2i(x, y);
            if (_maps.GetTileRef(_containmentGrid, grid, tile).Tile.IsEmpty)
                _maps.SetTile(_containmentGrid, grid, tile, floor);
        }

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
            new Vector2(-15.5f, 50.5f),
            new Vector2(-46.5f, -21.5f),
            new Vector2(-47.5f, -36.5f),
            new Vector2(-17.5f, -53.5f)
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

}
