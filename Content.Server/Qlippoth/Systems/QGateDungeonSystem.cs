using System.Numerics;
using Content.Shared.Maps;
using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Qlippoth.Systems;

/// Creates the isolated rift arena associated with a breached Q-Gate.
public sealed partial class QGateDungeonSystem : EntitySystem
{
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;

    public RiftDungeon CreateRiftDungeon(QGatePhase phase, EntityUid gate)
    {
        var mapId = _mapManager.CreateMap();
        var gridEntity = _mapManager.CreateGridEntity(mapId);
        var gridUid = gridEntity.Owner;
        var grid = gridEntity.Comp;
        var floor = new Tile(_tileDefinitions[GetFloor(phase)].TileId);
        var wallPrototype = GetWall(phase);
        var width = 16 + (int) phase * 4;
        var height = 12 + (int) phase * 3;

        var tiles = new List<(Vector2i Index, Tile Tile)>();
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            tiles.Add((new Vector2i(x, y), floor));

        _maps.SetTiles(gridUid, grid, tiles);

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            if (x != 0 && x != width - 1 && y != 0 && y != height - 1)
                continue;

            // Leave a single entrance in the south wall for the Q-Gate connection.
            if (y == 0 && x is >= 3 and <= 5)
                continue;

            Spawn(wallPrototype, new EntityCoordinates(gridUid, new Vector2(x + 0.5f, y + 0.5f)));
        }

        var center = new EntityCoordinates(gridUid, new Vector2(width / 2f, height / 2f));
        var qlippoth = Spawn(GetQlippoth(phase), center);
        SpawnObjective(gridUid, gate, "QGateObjectiveStabilize", QGateObjectiveType.StabilizeRift,
            new Vector2(4.5f, 4.5f));
        SpawnObjective(gridUid, gate, "QGateObjectiveData", QGateObjectiveType.ExtractAnomalyData,
            new Vector2(width - 4.5f, 4.5f));
        SpawnObjective(gridUid, gate, "QGateObjectiveSeal", QGateObjectiveType.SealContainment,
            new Vector2(width / 2f, height - 3.5f));
        var entry = new MapCoordinates(new Vector2(width / 2f, 1.5f), mapId);
        return new RiftDungeon(mapId, entry, qlippoth);
    }

    private void SpawnObjective(EntityUid gridUid, EntityUid gate, string prototype, QGateObjectiveType type, Vector2 position)
    {
        var objective = Spawn(prototype, new EntityCoordinates(gridUid, position));
        var component = EnsureComp<QGateDungeonObjectiveComponent>(objective);
        component.Gate = gate;
        component.ObjectiveType = type;
        Dirty(objective, component);
    }

    public void CloseRift(RiftDungeon dungeon)
    {
        if (_mapManager.MapExists(dungeon.MapId))
            _mapManager.DeleteMap(dungeon.MapId);
    }

    public readonly record struct RiftDungeon(MapId MapId, MapCoordinates Entry, EntityUid Qlippoth);

    private static string GetFloor(QGatePhase phase)
    {
        return phase >= QGatePhase.Phase4Abyss ? "FloorBasalt" : "FloorSteel";
    }

    private static string GetWall(QGatePhase phase)
    {
        return phase >= QGatePhase.Phase4Abyss ? "WallRockBasalt" : "WallReinforced";
    }

    private static string GetQlippoth(QGatePhase phase)
    {
        return phase switch
        {
            QGatePhase.Phase1Rift => "MobQlippothPhase1",
            QGatePhase.Phase2Verge => "MobQlippothPhase2",
            QGatePhase.Phase3Eclipse => "MobQlippothPhase3",
            QGatePhase.Phase4Abyss => "MobQlippothPhase4",
            QGatePhase.Phase5Horizon => "MobQlippothPhase5",
            _ => "MobQlippothPhase1"
        };
    }
}
