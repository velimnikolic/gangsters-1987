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

        /// <summary>tile-mainroad-straight: the dual carriageway running through.</summary>
        MainStraight,

        /// <summary>
        /// tile-road-mainroad-intersection-t: the boulevard running through with a minor street
        /// teeing in. At 0 degrees the branch is North and the carriageway runs East-West.
        /// </summary>
        MainTJunction,

        /// <summary>
        /// tile-road-mainroad-intersection: the boulevard crossing a minor street. At 0 degrees
        /// the carriageway runs North-South.
        /// </summary>
        MainCross,
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

        /// <summary>
        /// The same question for a cell on the dual carriageway.
        ///
        /// The mainroad prefabs carry the SAME tileShape values as their minor-road counterparts
        /// - straight=Straight, road-mainroad-intersection-t=T, road-mainroad-intersection=Cross
        /// - all verified in the prefab files. Since tileShape is the only thing
        /// Tile.GetNeighborTiles() switches on, every rotation below is the one Lookup already
        /// derives for that mask, and the derivation in this class's doc applies unchanged.
        ///
        /// What IS new is that these tiles are no longer symmetric under rotation: two of their
        /// arms carry four lanes and the others carry two, so the mask alone no longer settles
        /// the rotation. Hence the axis parameter.
        ///
        /// The measured arms at 0 degrees, read out of the prefabs' Path components:
        ///
        ///   tile-mainroad-straight                N,S = carriageway
        ///   tile-road-mainroad-intersection       N,S = carriageway   E,W = street
        ///   tile-road-mainroad-intersection-t     E,W = carriageway   N   = street branch
        ///
        /// Note the Cross and the T disagree about which axis is the carriageway at 0 degrees.
        /// That is the pack's convention, not a mistake here: the T's odd arm is its "forward",
        /// and the odd arm is the BRANCH, so the carriageway has to lie across it.
        ///
        /// Only five masks can reach this function - see CityGenerator.Subdivide, which explains
        /// why the boulevard can never curve, taper or dead-end inside the city. Anything else
        /// is a layout bug rather than a shape to draw, so it returns None and the caller warns.
        /// </summary>
        public static RoadTilePlacement LookupMain(Sides sides, bool northSouth)
        {
            // Along-axis neighbours are the carriageway; the other two are side streets.
            var along = northSouth ? Sides.North | Sides.South : Sides.East | Sides.West;
            var across = northSouth ? Sides.East | Sides.West : Sides.North | Sides.South;

            // The avenue always continues in at least one direction - at the end cells because
            // the map edge cut it there, everywhere else because it spans the map. A cell that
            // carries none of it is not on the boulevard at all.
            if ((sides & along) == Sides.None)
                return new RoadTilePlacement(RoadTileKind.None, 0f);

            // Nothing across: a plain run of dual carriageway, including the two end cells where
            // the map edge slices it. Same rule and same rotation as Lookup's one-connection
            // case - the axis of the street, not the direction of its one neighbour.
            if ((sides & across) == Sides.None)
                return new RoadTilePlacement(RoadTileKind.MainStraight, northSouth ? 0f : 90f);

            // Both sides: a full crossroads. The Cross probes all four ways, so its rotation is
            // free as far as linking goes - it is chosen purely to lay the four lanes along the
            // avenue, which at 0 degrees run North-South.
            if ((sides & across) == across)
                return new RoadTilePlacement(RoadTileKind.MainCross, northSouth ? 0f : 90f);

            // One side: a minor street tees in. The T's missing arm is the one it faces away
            // from, so the rotation is fixed by where the branch is - and it comes out the same
            // as Lookup's TJunction entry for this mask, since the shapes are the same.
            return new RoadTilePlacement(RoadTileKind.MainTJunction, (sides & across) switch
            {
                Sides.North => 0f,
                Sides.East => 90f,
                Sides.South => 180f,
                _ => 270f, // West
            });
        }
    }
}
