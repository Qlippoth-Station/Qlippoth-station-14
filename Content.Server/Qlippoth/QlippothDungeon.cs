using System.Numerics;
using Content.Server.Qlippoth.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// What the rift dimension behind a Q-Gate looks like. Chosen per Qlippoth (QlippothComponent.Dungeon).
    /// Implementations build onto a fresh grid and say where players enter and where the Qlippoth stands.
    ///
    /// YAML:
    ///   dungeon: !type:ArenaDungeon
    ///     width: 24
    ///     height: 18
    ///     floor: FloorBasalt
    ///     wall: WallRockBasalt
    /// </summary>
    [ImplicitDataDefinitionForInheritors]
    public abstract partial class QlippothDungeon
    {
        public abstract QlippothDungeonLayout Build(QGateSystem gates, EntityUid gridUid, MapGridComponent grid, EntityUid gate);
    }

    /// <summary>Entry = where the gate drops players, QlippothSpot = where the Qlippoth is spawned, both in grid coordinates.</summary>
    public readonly record struct QlippothDungeonLayout(Vector2 Entry, Vector2 QlippothSpot, int ObjectiveCount);

    /// <summary>
    /// A walled rectangle with one entrance on the south wall, the Qlippoth in the middle and
    /// the objectives lined up along the north side.
    /// </summary>
    [DataDefinition]
    public sealed partial class ArenaDungeon : QlippothDungeon
    {
        [DataField]
        public int Width { get; set; } = 20;

        [DataField]
        public int Height { get; set; } = 15;

        /// <summary>Tile definition id.</summary>
        [DataField]
        public string Floor { get; set; } = "FloorSteel";

        /// <summary>Wall entity prototype.</summary>
        [DataField]
        public string Wall { get; set; } = "WallReinforced";

        /// <summary>Objective entities (QGateDungeonObjective) the crew must complete to clear the rift.</summary>
        [DataField]
        public List<EntProtoId> Objectives { get; set; } = new()
        {
            "QGateObjectiveStabilize",
            "QGateObjectiveData",
            "QGateObjectiveSeal",
        };

        public override QlippothDungeonLayout Build(QGateSystem gates, EntityUid gridUid, MapGridComponent grid, EntityUid gate)
        {
            var width = Math.Max(8, Width);
            var height = Math.Max(8, Height);

            var floorTiles = new List<Vector2i>();
            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
                floorTiles.Add(new Vector2i(x, y));
            gates.PlaceFloor(gridUid, grid, Floor, floorTiles);

            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
            {
                if (x != 0 && x != width - 1 && y != 0 && y != height - 1)
                    continue;

                // Leave a gap in the south wall next to the entry.
                if (y == 0 && x is >= 3 and <= 5)
                    continue;

                gates.PlaceEntity(gridUid, Wall, new Vector2(x + 0.5f, y + 0.5f));
            }

            // Same layout as the original generator: first two in the front corners, third at the back middle,
            // any extra ones spread along the back wall.
            for (var i = 0; i < Objectives.Count; i++)
            {
                var position = i switch
                {
                    0 => new Vector2(4.5f, 4.5f),
                    1 => new Vector2(width - 4.5f, 4.5f),
                    2 => new Vector2(width / 2f, height - 3.5f),
                    _ => new Vector2(width * (i - 2) / (float) (Objectives.Count - 2), height - 3.5f),
                };
                gates.PlaceObjective(gridUid, gate, Objectives[i], position);
            }

            return new QlippothDungeonLayout(
                Entry: new Vector2(width / 2f, 1.5f),
                QlippothSpot: new Vector2(width / 2f, height / 2f),
                ObjectiveCount: Objectives.Count);
        }
    }
}
