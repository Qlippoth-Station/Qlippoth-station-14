using System.Numerics;
using Content.Shared.Qlippoth.Components;
using Robust.Client.Placement;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Qlippoth;

/// <summary>
/// Aligns a containment chamber blueprint to a tile and colors its full footprint
/// according to whether the chamber can be built at the hovered position.
/// </summary>
public sealed class QlippothChamberPlacementMode : PlacementMode
{
    public QlippothChamberPlacementMode(PlacementManager pMan) : base(pMan)
    {
    }

    public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
    {
        MouseCoords = ScreenToCursorGrid(mouseScreen).AlignWithClosestGridTile(2f,
            pManager.EntityManager, pManager.MapManager);
        var gridId = pManager.EntityManager.System<SharedTransformSystem>().GetGrid(MouseCoords);
        if (!pManager.EntityManager.TryGetComponent<MapGridComponent>(gridId, out var grid))
            return;

        CurrentTile = pManager.EntityManager.System<SharedMapSystem>().GetTileRef(gridId.Value, grid, MouseCoords);
        GridDistancing = grid.TileSize;
        MouseCoords = new EntityCoordinates(MouseCoords.EntityId,
            new Vector2(CurrentTile.X + grid.TileSize / 2f, CurrentTile.Y + grid.TileSize / 2f));
    }

    public override bool IsValidPosition(EntityCoordinates position)
    {
        if (!RangeCheck(position))
            return false;

        var transform = pManager.EntityManager.System<SharedTransformSystem>();
        var mapId = transform.GetMapId(position);
        var containmentMap = MapId.Nullspace;
        var chambers = pManager.EntityManager.EntityQuery<ContainmentChamberComponent, TransformComponent>();
        while (chambers.MoveNext(out _, out _, out var chamberTransform))
        {
            containmentMap = chamberTransform.MapID;
            break;
        }

        if (containmentMap == MapId.Nullspace || mapId != containmentMap)
            return false;

        var gridId = transform.GetGrid(position);
        if (gridId is not { } validGrid || !pManager.EntityManager.TryGetComponent<MapGridComponent>(validGrid, out var grid))
            return false;

        var maps = pManager.EntityManager.System<SharedMapSystem>();
        var centerTile = new Vector2i((int) MathF.Floor(position.Position.X), (int) MathF.Floor(position.Position.Y));
        for (var x = -2; x <= 2; x++)
        for (var y = -2; y <= 2; y++)
        {
            var tile = centerTile + new Vector2i(x, y);
            if (maps.GetTileRef(validGrid, grid, tile).Tile.IsEmpty)
                return false;

            var anchored = maps.GetAnchoredEntitiesEnumerator(validGrid, grid, tile);
            if (anchored.MoveNext(out _))
                return false;
        }

        return true;
    }
}
