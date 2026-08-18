using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Which side of a block's yard can take parked cars, and how the lot on it is laid
    /// out: how many rows deep, how far off the kerb, and where its entrance is.
    ///
    /// Pure geometry - this class instantiates nothing, the same discipline ParkingLayout
    /// keeps and for the same reason: the answer is wanted twice, once to lay the paint
    /// and once to stand the cars in it, and computing it twice is how a bay and its
    /// lines drift apart. It reads an occupancy grid and hands back one rectangle.
    ///
    /// A block interior in the RoadDemo grid is bounded by street on all four sides -
    /// the pad IS kerb to kerb - so every edge of the pad is a frontage and any of them
    /// may carry the bays. What decides it is depth: a strip has to be deeper than a car
    /// is long before a car can nose into it off the street. The strip that is deep
    /// enough for that wins; when none is, the deepest strip that still takes a car
    /// LENGTHWAYS gets kerbside parking instead, which is what a two-metre setback in
    /// front of a shop row actually holds.
    ///
    /// What a nose-in lot is made of, walking in from the kerb:
    ///
    ///   apron - ROW - [ aisle - ROW ] - whatever the yard has left
    ///
    /// with a DRIVE cut out of one end of the frontage, running the whole depth of the
    /// lot. Three things follow from that shape, and all three are the point of it:
    ///
    ///   * a row band is <see cref="RowDepth"/> deep, which is the longest car the pass
    ///     will park plus room at each end - so a car stands FULLY inside its own paint
    ///     rather than hanging out of the pack's five-metre tile by half a bumper;
    ///   * the apron keeps that paint off the kerb at the front and off whatever stands
    ///     behind at the back, so no car ever overhangs the pavement;
    ///   * a lot deep enough gets a SECOND row behind the first with an aisle between
    ///     them, and the drive is what reaches that aisle - which is why two rows are
    ///     only ever offered on a frontage that can also spare the drive.
    ///
    /// Only ONE side is ever chosen. A block ringed with bays reads as a car park with a
    /// building in it rather than as a building with parking beside it.
    /// </summary>
    public static class YardParkingPlan
    {
        /// <summary>Bays nosed in off the street, or cars standing along the kerb.</summary>
        public enum Kind { None, Nose, Parallel }

        /// <summary>The pad edge the bays stand against, named by compass so it reads
        /// the same as the grid it is measured on.</summary>
        public enum Side { South, North, West, East }

        /// <summary>
        /// The frontage one painted bay tile covers. SM_Env_Road_ParkingLines_01 is a flat
        /// 10 x 5 m piece of the PolygonCity road kit carrying three bays across its ten
        /// metres, and the kit's module is the 5 m the lot pads are already drawn on - so
        /// a tile lands on the grid without a fudge factor. Measured off the mesh, not
        /// guessed: dividers at x = 0.1, 3.35, 6.83, 9.92 running the full 5 m of depth.
        /// </summary>
        public const float TileFrontage = 10f;

        /// <summary>How far the tile reaches in from the kerb AS THE PACK SHIPS IT - which
        /// is shorter than a sedan, and so is not what the bays are laid to. They are
        /// <see cref="RowDepth"/> deep and the tile is stretched to suit, off its own
        /// measured mesh rather than off this figure; it is here to say what that stretch
        /// is starting from.</summary>
        public const float TileDepth = 5f;

        public const int BaysPerTile = 3;

        /// <summary>3.33 m - the pack's bay, wider than the 2.7 ParkingLayout marks out
        /// for the city's own lots because this is the pack's paint and not ours.</summary>
        public const float BayWidth = TileFrontage / BaysPerTile;

        /// <summary>
        /// One row band: the longest car the pass will park, plus 0.4 m of daylight at
        /// each end of it. ParkingLayout.StallDepth is the project's measured car length
        /// (it admits car-tow-truck at 5.43 and rejects the 6.25 lorry) and the parking
        /// pass filters its catalogue by the same number, so the deepest car it can pick
        /// still stands inside the band with room to spare - which is the whole reason
        /// the band is not simply the tile's own five metres.
        /// </summary>
        public const float RowDepth = ParkingLayout.StallDepth + 0.8f;

        /// <summary>Kerb to the front of the first row, and the same again behind the
        /// last: the padding that keeps a bumper off the pavement at one end and off a
        /// wall at the other.</summary>
        public const float Apron = 1.5f;

        /// <summary>What the apron may shrink to on a yard that is only just deep enough.
        /// Still half a metre of daylight, which is what stops the paint reading as if it
        /// had been poured over the kerb.</summary>
        public const float MinApron = 0.6f;

        /// <summary>Between two rows: ParkingLayout's own aisle, which is the room a car
        /// needs to turn ninety degrees out of a bay.</summary>
        public const float Aisle = ParkingLayout.AisleWidth;

        /// <summary>The lot's entrance off the street, cut out of one end of the frontage.
        /// One aisle wide - it IS an aisle, the one that reaches the back row.</summary>
        public const float DriveWidth = ParkingLayout.AisleWidth;

        /// <summary>Two rows and no more. A third would need a second aisle and a yard
        /// nearly thirty metres deep, and no lot pad in the grid has one behind its
        /// buildings.</summary>
        public const int MaxRows = 2;

        /// <summary>
        /// The depth a nose-in strip needs: one row band and the least apron it may keep
        /// at each end of it.
        /// </summary>
        public const float NoseDepth = RowDepth + 2f * MinApron;

        /// <summary>And what two rows with the aisle between them need.</summary>
        public const float TwoRowDepth = 2f * RowDepth + Aisle + 2f * MinApron;

        /// <summary>Widest vehicle in the catalogue is 2.26, so this leaves most of a
        /// metre for the kerb side and the door.</summary>
        public const float ParallelDepth = 3.2f;

        /// <summary>Kerbside slot: the car plus the shunting room to get out of it.</summary>
        public const float ParallelPitch = ParkingLayout.StallDepth + 1.6f;

        /// <summary>One tile is three bays; anything less is a lay-by, not parking.</summary>
        const int MinTiles = 1;

        /// <summary>A single car at a kerb reads as a car, not as parking.</summary>
        const int MinParallel = 2;

        /// <summary>
        /// Where the bays go. Kind.None means nowhere - the yard has no strip deep enough,
        /// which is the ordinary case for a block built out to its own kerb.
        ///
        /// Start and Length are metres ALONG the chosen side, measured from the pad's
        /// minimum corner (west end of a south/north side, south end of a west/east one),
        /// which is the frame Origin and Frame below hand back. They cover the WHOLE lot,
        /// the entrance drive included; <see cref="BayStart"/> and <see cref="BayLength"/>
        /// are the part of it the tiles may use. Depth is the free depth the whole run has
        /// - the shallowest column in it, so nothing placed inside can reach past what was
        /// surveyed.
        /// </summary>
        public readonly struct Plan
        {
            public readonly Kind Kind;
            public readonly Side Side;
            public readonly float Start;
            public readonly float Length;
            public readonly float Depth;

            /// <summary>Painted tiles PER ROW for Kind.Nose, kerbside slots for
            /// Kind.Parallel.</summary>
            public readonly int Units;

            /// <summary>Rows of bays: one, or two with an aisle between them. Always one
            /// for Kind.Parallel.</summary>
            public readonly int Rows;

            /// <summary>Whether one end of the frontage is given up to the lot's entrance.
            /// False on a run too short to spare it, where the row is simply nosed into
            /// straight off the street.</summary>
            public readonly bool HasDrive;

            /// <summary>Which end of the run the drive is at - the end nearer a corner of
            /// the pad, which is where the cross street is.</summary>
            public readonly bool DriveAtStart;

            /// <summary>Kerb to the front of the first row. Between <see cref="MinApron"/>
            /// and <see cref="Apron"/> depending on what the yard could spare; meaningless
            /// for Kind.Parallel, which sets its own setback per car.</summary>
            public readonly float Apron;

            public Plan(Kind kind, Side side, float start, float length, float depth,
                        int units, int rows, bool hasDrive, bool driveAtStart, float apron)
            {
                Kind = kind;
                Side = side;
                Start = start;
                Length = length;
                Depth = depth;
                Units = units;
                Rows = rows;
                HasDrive = hasDrive;
                DriveAtStart = driveAtStart;
                Apron = apron;
            }

            /// <summary>How many cars the lot can hold at most.</summary>
            public int Bays => Kind switch
            {
                Kind.Nose => Rows * Units * BaysPerTile,
                Kind.Parallel => Units,
                _ => 0,
            };

            /// <summary>The frontage the entrance eats, zero when there is none.</summary>
            public float Drive => HasDrive ? DriveWidth : 0f;

            /// <summary>Where the drive begins, along the side.</summary>
            public float DriveStart => DriveAtStart ? Start : Start + Length - Drive;

            /// <summary>The frontage the tiles may use: the run less the drive.</summary>
            public float BayStart => Start + (DriveAtStart ? Drive : 0f);

            public float BayLength => Length - Drive;

            /// <summary>Kerb to the near edge of a row's band.</summary>
            public float RowFront(int row) => Apron + row * (RowDepth + Aisle);

            /// <summary>Kerb to the far edge of a row's band.</summary>
            public float RowBack(int row) => RowFront(row) + RowDepth;

            /// <summary>
            /// Which end of a row's band the HEAD of its bays is at - the closed end, where
            /// the wheel stops stand and a car's nose comes to rest. True means the kerb end.
            ///
            /// It is the aisle a row is entered from that decides this, and that is what
            /// makes the two lots different animals. With one row there is no aisle: the
            /// street is it, a car turns straight off the kerb into the bay, and the head is
            /// at the far end. With two rows the aisle between them serves BOTH - the front
            /// row turned round to face the street, the back row facing away - which is the
            /// ordinary double-loaded aisle every real car park is built of, and the only
            /// arrangement in which the frontage can be fenced: nothing has to cross the
            /// kerb line any more except through the drive.
            /// </summary>
            public bool HeadAtKerb(int row) => Rows > 1 && row == 0;

            /// <summary>Kerb to the head of a row's bays.</summary>
            public float RowHead(int row) => HeadAtKerb(row) ? RowFront(row) : RowBack(row);

            /// <summary>Whether the lot can be closed along the street. Only when its rows
            /// are served by an aisle rather than by the kerb - see
            /// <see cref="HeadAtKerb"/>.</summary>
            public bool FrontCanBeFenced => Kind == Kind.Nose && Rows > 1 && HasDrive;

            /// <summary>How deep the lot itself reaches: the last row's head plus the
            /// apron behind it. Never more than <see cref="Depth"/>.</summary>
            public float Reach => Kind == Kind.Nose ? RowBack(Rows - 1) + Apron : Depth;

            /// <summary>How far in the drive runs: to the head of the last row, which is
            /// as far as anything ever has to drive.</summary>
            public float DriveReach => Kind == Kind.Nose ? RowBack(Rows - 1) : 0f;
        }

        /// <summary>
        /// The one side worth parking on. <paramref name="blocked"/> is the yard's
        /// occupancy grid - true where a building or anything else already stands - and
        /// <paramref name="cell"/> its cell size in metres.
        ///
        /// Nose-in beats kerbside outright rather than on count: bays off the street are
        /// the thing being asked for, and a longer kerbside run on another side is not a
        /// better answer to it.
        /// </summary>
        public static Plan Choose(bool[,] blocked, float cell)
        {
            if (blocked == null || cell <= 0f)
                return default;

            var best = default(Plan);

            foreach (Side side in System.Enum.GetValues(typeof(Side)))
                foreach (var run in Runs(Profile(blocked, side), cell, NoseDepth))
                    best = Better(best, NosePlan(side, run));

            if (best.Kind != Kind.None)
                return best;

            foreach (Side side in System.Enum.GetValues(typeof(Side)))
                foreach (var run in Runs(Profile(blocked, side), cell, ParallelDepth))
                    best = Better(best, KerbPlan(side, run));

            return best;
        }

        /// <summary>
        /// More cars wins. A tie goes to the lot with a way in - two sides of a squarish
        /// free yard hold the same three bays, and the one whose frontage can also spare
        /// the entrance is the better answer - and after that to the deeper strip, because
        /// depth is what keeps a parked car clear of whatever the scatter pass puts behind
        /// it.
        /// </summary>
        static Plan Better(Plan current, Plan candidate)
        {
            if (candidate.Kind == Kind.None)
                return current;
            if (current.Kind == Kind.None)
                return candidate;
            if (candidate.Bays != current.Bays)
                return candidate.Bays > current.Bays ? candidate : current;
            if (candidate.HasDrive != current.HasDrive)
                return candidate.HasDrive ? candidate : current;
            return candidate.Depth > current.Depth ? candidate : current;
        }

        /// <summary>One stretch of free frontage: where it starts along the side, how long
        /// it is, how deep it is EVERYWHERE (the shallowest column in it, because a run is
        /// one rectangle and a rectangle sized off an average has a corner standing in a
        /// building), and how long the side it was found on is.</summary>
        readonly struct Run
        {
            public readonly float Start, Length, Depth, SideLength;

            public Run(float start, float length, float depth, float sideLength)
            {
                Start = start;
                Length = length;
                Depth = depth;
                SideLength = sideLength;
            }

            /// <summary>Whether the run's low end is the one nearer a corner of the pad.
            /// A lot puts its mouth by the cross street, not in the middle of a block.</summary>
            public bool LowEndIsOuter => Start <= SideLength - (Start + Length);
        }

        /// <summary>Every stretch of one side whose free depth never drops below what a
        /// kind of parking needs.</summary>
        static List<Run> Runs(int[] profile, float cell, float needDepth)
        {
            var need = Mathf.CeilToInt(needDepth / cell);
            var runs = new List<Run>();
            var a = 0;

            while (a < profile.Length)
            {
                if (profile[a] < need)
                {
                    a++;
                    continue;
                }

                var from = a;
                var shallowest = profile[a];
                while (a < profile.Length && profile[a] >= need)
                {
                    shallowest = Mathf.Min(shallowest, profile[a]);
                    a++;
                }

                runs.Add(new Run(from * cell, (a - from) * cell, shallowest * cell,
                                 profile.Length * cell));
            }

            return runs;
        }

        /// <summary>
        /// The lot one free run can carry: whole tiles per row, a drive out of one end,
        /// a second row when the yard is deep enough to hold one and the frontage can
        /// still spare the drive that would reach it.
        ///
        /// The drive is dropped rather than the row when the frontage can only hold one
        /// of the two: a single row nosed straight into off the street is a real thing
        /// (every strip mall has one), a row nobody can reach is not.
        /// </summary>
        static Plan NosePlan(Side side, Run run)
        {
            var withDrive = Mathf.FloorToInt((run.Length - DriveWidth) / TileFrontage);
            var hasDrive = withDrive >= MinTiles;

            var tiles = hasDrive ? withDrive : Mathf.FloorToInt(run.Length / TileFrontage);
            if (tiles < MinTiles)
                return default;

            // Two rows need the aisle between them AND the drive that reaches it.
            var rows = hasDrive && run.Depth >= TwoRowDepth ? MaxRows : 1;

            // Whatever depth the rows and the aisle do not want is shared between the
            // apron at the kerb and the one behind the last row - up to the padding they
            // ask for, and never below what they must have.
            var used = rows * RowDepth + (rows - 1) * Aisle;
            var apron = Mathf.Clamp((run.Depth - used) * 0.5f, MinApron, Apron);

            return new Plan(Kind.Nose, side, run.Start, run.Length, run.Depth,
                            tiles, rows, hasDrive, run.LowEndIsOuter, apron);
        }

        /// <summary>Cars along the kerb, for a setback too shallow to nose into. No rows,
        /// no aisle and no drive: the street is the aisle.</summary>
        static Plan KerbPlan(Side side, Run run)
        {
            var slots = Mathf.FloorToInt(run.Length / ParallelPitch);
            if (slots < MinParallel)
                return default;

            return new Plan(Kind.Parallel, side, run.Start, run.Length, run.Depth,
                            slots, 1, false, false, 0f);
        }

        /// <summary>How many free cells each column of a side has before it meets
        /// something, walking in from the kerb.</summary>
        static int[] Profile(bool[,] blocked, Side side)
        {
            var nx = blocked.GetLength(0);
            var nz = blocked.GetLength(1);
            var length = side is Side.South or Side.North ? nx : nz;
            var reach = side is Side.South or Side.North ? nz : nx;

            var profile = new int[length];
            for (var a = 0; a < length; a++)
            {
                var free = 0;
                while (free < reach)
                {
                    Cell(side, a, free, nx, nz, out var i, out var j);
                    if (blocked[i, j])
                        break;
                    free++;
                }
                profile[a] = free;
            }
            return profile;
        }

        /// <summary>
        /// The grid cell <paramref name="deep"/> cells in from a side's kerb at
        /// <paramref name="along"/> cells from its minimum corner. Shared by the survey
        /// and by whoever reserves the ground afterwards, so the strip that was measured
        /// is exactly the strip that gets taken.
        /// </summary>
        public static void Cell(Side side, int along, int deep, int nx, int nz,
                                out int i, out int j)
        {
            switch (side)
            {
                case Side.South:
                    i = along;
                    j = deep;
                    break;
                case Side.North:
                    i = along;
                    j = nz - 1 - deep;
                    break;
                case Side.West:
                    i = deep;
                    j = along;
                    break;
                default:
                    i = nx - 1 - deep;
                    j = along;
                    break;
            }
        }

        /// <summary>The side's own frame: <paramref name="along"/> runs from the minimum
        /// corner, <paramref name="outward"/> points at the street.</summary>
        public static void Frame(Side side, out Vector3 along, out Vector3 outward)
        {
            switch (side)
            {
                case Side.South:
                    along = Vector3.right;
                    outward = Vector3.back;
                    break;
                case Side.North:
                    along = Vector3.right;
                    outward = Vector3.forward;
                    break;
                case Side.West:
                    along = Vector3.forward;
                    outward = Vector3.left;
                    break;
                default:
                    along = Vector3.forward;
                    outward = Vector3.right;
                    break;
            }
        }

        /// <summary>The corner a side's Start is measured from, on the kerb line itself.
        /// <paramref name="min"/> and <paramref name="max"/> are the pad's own corners at
        /// y = 0.</summary>
        public static Vector3 Origin(Side side, Vector3 min, Vector3 max) => side switch
        {
            Side.North => new Vector3(min.x, 0f, max.z),
            Side.East => new Vector3(max.x, 0f, min.z),
            _ => new Vector3(min.x, 0f, min.z),
        };
    }
}
