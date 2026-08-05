using UnityEngine;

namespace LivingCity.Generation
{
    public enum RoadTileKind
    {
        None,
        Straight,
        Curve,
        TJunction,
        Cross,

        /// <summary>
        /// tile-road-end: a cul-de-sac with a turning circle at the tile's centre. Lookup
        /// never returns this - a street cut by the map boundary uses a plain straight
        /// instead, see the one-connection cases below. Kept because the prefab is still
        /// wired up and is the right piece for a genuine dead-end street inside a block.
        /// </summary>
        End,
    }

    public readonly struct RoadTilePlacement
    {
        public readonly RoadTileKind Kind;
        public readonly float YRotation;

        public RoadTilePlacement(RoadTileKind kind, float yRotation)
        {
            Kind = kind;
            YRotation = yRotation;
        }

        public Quaternion Rotation => Quaternion.Euler(0f, YRotation, 0f);
        public bool IsValid => Kind != RoadTileKind.None;
    }

    /// <summary>
    /// Maps "which sides of this cell have road neighbours" to a tile prefab and rotation.
    ///
    /// These values are NOT arbitrary and must not be tuned by trial and error.
    /// Tile.GetNeighborTiles() probes a fixed set of LOCAL directions that depends on the
    /// prefab's tileShape field, so the correct rotation is fully determined:
    ///
    ///   tileShape      probes
    ///   Straight (2)   forward (+Z), back (-Z)
    ///   Turn     (3)   forward (+Z), left (-X)      <- note: left, not right
    ///   T        (0)   forward (+Z), right (+X), left (-X)
    ///   Cross    (1)   all four
    ///   End      (4)   forward (+Z) only
    ///
    /// With a Y rotation of theta: forward -> (sin, 0, cos), right -> (cos, 0, -sin).
    /// So at 0 degrees forward=North, right=East, left=West, back=South; each +90 rotates
    /// the whole set clockwise. Applying that to the probe sets above yields the table below.
    ///
    /// The most error-prone entry is the curve: its default (unrotated) shape connects
    /// North and WEST, not North and East. That is not just inferred from the probe set -
    /// tile-road-curve's road paths physically run between z = +15 (North edge) and
    /// x = -15 (West edge), which confirms it independently.
    /// </summary>
    public static class RoadTileTable
    {
        public static RoadTilePlacement Lookup(Sides sides)
        {
            switch (sides)
            {
                // Straight - probes forward and back.
                case Sides.North | Sides.South: return new RoadTilePlacement(RoadTileKind.Straight, 0f);
                case Sides.East | Sides.West: return new RoadTilePlacement(RoadTileKind.Straight, 90f);

                // Turn - probes forward and left, so the unrotated tile is North+West.
                case Sides.North | Sides.West: return new RoadTilePlacement(RoadTileKind.Curve, 0f);
                case Sides.North | Sides.East: return new RoadTilePlacement(RoadTileKind.Curve, 90f);
                case Sides.East | Sides.South: return new RoadTilePlacement(RoadTileKind.Curve, 180f);
                case Sides.South | Sides.West: return new RoadTilePlacement(RoadTileKind.Curve, 270f);

                // T - probes forward, right and left; the missing side is the one it faces away from.
                case Sides.North | Sides.East | Sides.West: return new RoadTilePlacement(RoadTileKind.TJunction, 0f);
                case Sides.North | Sides.East | Sides.South: return new RoadTilePlacement(RoadTileKind.TJunction, 90f);
                case Sides.East | Sides.South | Sides.West: return new RoadTilePlacement(RoadTileKind.TJunction, 180f);
                case Sides.North | Sides.South | Sides.West: return new RoadTilePlacement(RoadTileKind.TJunction, 270f);

                // Cross - probes all four, rotation is irrelevant.
                case Sides.All: return new RoadTilePlacement(RoadTileKind.Cross, 0f);

                // One connection means the map boundary cuts the street here, so this gets a
                // plain straight whose geometry simply stops at the tile edge - NOT the End
                // tile, despite the name.
                //
                // tile-road-end is a cul-de-sac, not a cut. Its road path was measured out of
                // the prefab: it enters at (x=1.5, z=15), runs down to z=2, loops across the
                // centre at (+/-1, z=-0.5) and goes back out at (x=-1.5, z=15) - a turning
                // circle in the middle of the tile, with the pavement approaching from the
                // west and wrapping round the head of the street. Dropped on a map edge it
                // reads as a lollipop, and there is one at the end of every street.
                //
                // The straight has no such head: lanes at x = +/-1.5 and pavements at x = +/-4
                // run the full z = -15..+15, so the tile's far edge is the map's edge and the
                // road is simply sliced there.
                //
                // Rotation is the axis of the street, not the direction of its one neighbour:
                // North and South are both the N-S axis, East and West both the E-W axis.
                //
                // The price is a lane and a pavement that dead-end at the boundary with no
                // onward link, which strands cars. DeadEndStitcher repairs that at startup -
                // this case and that component have to stay in sync.
                case Sides.North:
                case Sides.South: return new RoadTilePlacement(RoadTileKind.Straight, 0f);
                case Sides.East:
                case Sides.West: return new RoadTilePlacement(RoadTileKind.Straight, 90f);

                // An isolated road cell connects to nothing. CityGenerator's connectivity
                // check should have removed these before we ever get here.
                default: return new RoadTilePlacement(RoadTileKind.None, 0f);
            }
        }
    }
}
