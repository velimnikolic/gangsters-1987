using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The promenade's plan: one stretch of it between two bridges (or a bridge and the end
    /// of the line), as a raster of 5 m cells, cast with what stands on it.
    ///
    /// A park is a graph of paths through a field, cut into rooms (<see cref="ParkWalk"/>).
    /// A promenade is simpler and stricter: it is a STRIP along a line. Across it, west to
    /// east, three bands never change - the kerb along the quay street, the rooms, and the
    /// WALK along the wall, two cells of paving with the railing on its far edge, which is
    /// the promenade proper and is kept clear from one end of the line to the other. Along
    /// it, the streets that arrive at the quay street set the rhythm: each one's crossing
    /// goes on as a paved LANE across the strip to the walk, and the rooms are what lies
    /// between the lanes - the same rhythm the blocks across the street have. A room longer
    /// than a block is cut with a lane of its own.
    ///
    /// Rooms are cast the way the park casts its own: a programme takes the smallest room it
    /// fits, and lawn is what is left. What the strip is asked for - the one fairground, the
    /// landing, the plaza at the boulevard's mouth - comes from the line as a whole
    /// (<see cref="Cast"/>): a stretch does not know it is the end of the line, or that the
    /// stretch across the bridge already has the wheel.
    ///
    /// Nothing here stands anything. <c>QuayBlocks</c> composes what the plan says, and
    /// measures what it stood; <see cref="Report"/> judges the plan itself
    /// (Docs/river-plan.md).
    /// </summary>
    public static class QuayWalk
    {
        public const float Cell = CoreBlockMetrics.Cell;
        /// <summary>The kerbside band along the quay street: the same ten metres used by
        /// every generated CoreDemo block.</summary>
        public const int Band = CoreBlockMetrics.PavementTiles;
        /// <summary>The walk along the wall, in cells: a metre of railing and lamps, five
        /// of clear way, and the benches facing the water.</summary>
        public const int WalkDeep = 2;
        /// <summary>The narrowest promenade that has a room in it at all.</summary>
        public const int DeepMin = Band + WalkDeep + 2;
        /// <summary>A room longer than this is cut: a block's width is the rhythm.</summary>
        public const int RoomMax = 16;
        /// <summary>A room shorter than this is paving with nothing on it.</summary>
        public const int RoomMin = 3;

        public enum Ground : byte
        {
            None,
            Kerb,       // the pavement along the quay street, with the street's furniture
            Plaza,      // paving
            Grass,
            Walk,       // the promenade proper, along the wall
            Landing,    // walk with the stairs down to the water: a gap in the railing
            Lane,       // a street's crossing carried on across the strip
            Yard,       // the fairground's tarmac
        }

        /// <summary>What a room is for. The sizes are the smallest room each fits, in cells
        /// along the line and across the whole strip - measured off the pieces
        /// (Docs/river-plan.md 2.3), and the composer refuses what does not fit rather than
        /// scale it.</summary>
        public enum Programme { Lawn, Plaza, Fountain, Terrace, Grove, Landing, Fair, Diner }

        /// <summary>What a stretch's end meets: the end of the line, a street's bridge, or
        /// the boulevard's. The boulevard's mouth is where the plaza goes.</summary>
        public enum End { Line, Bridge, Boulevard }

        public sealed class Room
        {
            public int Z0, Z1;           // along the line, cells, Z1 exclusive
            public Programme Programme;
            public int Length => Z1 - Z0;
            /// <summary>Where the stairs go down, for a landing: two cells of the walk.</summary>
            public int LandingZ = -1;
        }

        /// <summary>A street arriving at the kerb: the cells of the strip its crossing faces.</summary>
        public struct Mouth
        {
            public int Z0, Z1;
            public Mouth(int z0, int z1) { Z0 = z0; Z1 = z1; }
        }

        /// <summary>What the line asks of one stretch.</summary>
        public struct Wants
        {
            public bool Fair;           // the one fairground on the line, at the end away from the bridge
            public bool FairAtStart;    // at z = 0 rather than z = Length
            public bool Landing;        // the stairs down to the boats
            public bool Diner;          // the catalog's diner, its front to the water
            public int Terraces;        // how many cafes at most
        }

        public sealed class Plan
        {
            public int Depth, Length;    // cells: x across the strip (0 at the kerb), z along it
            public End South, North;     // what the z = 0 and z = Length ends meet
            public Ground[,] Cells;      // [x, z]
            public readonly List<Room> Rooms = new List<Room>();
            public readonly List<Mouth> Mouths = new List<Mouth>();
            public Wants Wanted;
            public string Map;
            /// <summary>Notes on what was asked for and did not fit: not faults, but said.</summary>
            public readonly List<string> Refused = new List<string>();

            public Ground At(int x, int z) => x < 0 || z < 0 || x >= Depth || z >= Length ? Ground.None : Cells[x, z];
            public bool Paved(int x, int z)
            {
                var g = At(x, z);
                return g == Ground.Kerb || g == Ground.Plaza || g == Ground.Walk || g == Ground.Landing ||
                       g == Ground.Lane || g == Ground.Yard;
            }
            /// <summary>The first cell of the walk, across the strip.</summary>
            public int WalkX => Depth - WalkDeep;
        }

        // ------------------------------------------------------------------ the sizes

        /// <summary>The smallest room a programme takes: cells along the line, and the
        /// strip's whole depth in cells. Measured, not guessed: a terrace is a kiosk of
        /// 4.5 x 3.3 m by the kerb and two rows of tables under 3.25 m umbrellas before the
        /// walk; PalmBlock_07 is 37.7 x 45.4 m and keeps the demo's complete fairground.</summary>
        static void Least(Programme what, out int along, out int deep)
        {
            switch (what)
            {
                case Programme.Fair: along = 10; deep = 10 + Band; break;
                case Programme.Diner: along = 5; deep = 6 + Band; break; // 16.3 x 8.8 m, and its tables
                case Programme.Fountain: along = 4; deep = DeepMin; break;
                case Programme.Terrace: along = 4; deep = 5 + Band; break;
                case Programme.Landing: along = 4; deep = DeepMin; break;
                case Programme.Grove: along = 3; deep = DeepMin; break;
                default: along = 0; deep = 0; break;
            }
        }

        static bool Fits(Programme what, Room room, int depth)
        {
            Least(what, out int along, out int deep);
            return room.Length >= along && depth >= deep;
        }

        // ---------------------------------------------------------------------- the lay

        /// <summary>
        /// Lays one stretch: the bands, a lane across from every mouth, the rooms between
        /// the lanes, and the rooms cast. Same dice, same plan.
        /// </summary>
        public static Plan Lay(int depth, int length, IList<Mouth> mouths, End south, End north, Wants wants,
                               System.Random rng)
        {
            depth = Mathf.Max(DeepMin, depth);
            length = Mathf.Max(1, length);
            var plan = new Plan { Depth = depth, Length = length, South = south, North = north, Wanted = wants };
            plan.Cells = new Ground[depth, length];
            for (int x = 0; x < depth; x++)
                for (int z = 0; z < length; z++)
                    plan.Cells[x, z] = x < Band ? Ground.Kerb : x >= plan.WalkX ? Ground.Walk : Ground.Plaza;

            // an end on a bridge is a pavement along the bridge's approach road, kerb and
            // all - a row of it, the same band the quay street gets. An end on the line
            // itself has no kerb: the promenade goes on from there
            var cut = new bool[length];
            if (south != End.Line && length > Band)
                for (int z = 0; z < Band; z++) { cut[z] = true; for (int x = 0; x < depth; x++) plan.Cells[x, z] = Ground.Kerb; }
            if (north != End.Line && length > 2 * Band)
                for (int z = length - Band; z < length; z++) { cut[z] = true; for (int x = 0; x < depth; x++) plan.Cells[x, z] = Ground.Kerb; }

            // the mouths: a street's crossing goes on as a lane to the walk. A mouth at the
            // very end of the stretch is the bridge's approach, not a street - the stretch
            // ends there
            if (mouths != null)
                foreach (var m in mouths)
                {
                    int z0 = Mathf.Max(0, m.Z0), z1 = Mathf.Min(length, m.Z1);
                    if (z1 <= z0) continue;
                    plan.Mouths.Add(new Mouth(z0, z1));
                    for (int z = z0; z < z1; z++)
                    {
                        if (plan.Cells[Band, z] == Ground.Kerb) continue;
                        cut[z] = true;
                        for (int x = Band; x < plan.WalkX; x++) plan.Cells[x, z] = Ground.Lane;
                    }
                }

            // the rooms: whatever lies between two lanes. A room longer than a block is cut
            // in two with a lane of its own, and again until it is not
            var rooms = new List<Room>();
            for (int z = 0; z < length;)
            {
                if (cut[z]) { z++; continue; }
                int z0 = z;
                while (z < length && !cut[z]) z++;
                rooms.Add(new Room { Z0 = z0, Z1 = z });
            }
            for (int r = 0; r < rooms.Count; r++)
            {
                var room = rooms[r];
                if (room.Length <= RoomMax) continue;
                int mid = room.Z0 + room.Length / 2;
                for (int x = Band; x < plan.WalkX; x++) plan.Cells[x, mid] = Ground.Lane;
                rooms[r] = new Room { Z0 = room.Z0, Z1 = mid };
                rooms.Insert(r + 1, new Room { Z0 = mid + 1, Z1 = room.Z1 });
                r--;
            }
            plan.Rooms.AddRange(rooms);

            Cast(plan, rng);
            Floor(plan);
            plan.Map = Draw(plan);
            return plan;
        }

        /// <summary>
        /// Casts the rooms. Order is what the line insists on first: the fairground at the
        /// end it was asked for, the plaza at the boulevard's mouth, the landing in the
        /// longest room left, the terraces in the smallest rooms they fit (never two next
        /// to each other: a promenade of cafes end to end is a food court), then a grove or
        /// a lawn for whatever is left that is big enough to be either, and paving for the
        /// rest. A programme that fits nowhere is refused and said, never squeezed.
        /// </summary>
        static void Cast(Plan plan, System.Random rng)
        {
            var rooms = plan.Rooms;
            if (rooms.Count == 0) return;
            foreach (var room in rooms) room.Programme = room.Length >= RoomMin ? Programme.Lawn : Programme.Plaza;
            var taken = new HashSet<Room>();

            if (plan.Wanted.Fair)
            {
                var end = plan.Wanted.FairAtStart ? rooms[0] : rooms[rooms.Count - 1];
                if (Fits(Programme.Fair, end, plan.Depth)) { end.Programme = Programme.Fair; taken.Add(end); }
                else plan.Refused.Add($"no room for the fair: the end room is {end.Length} cells long and the strip {plan.Depth} deep, it wants 10 x 11");
            }

            foreach (var (boulevard, room) in new[] { (plan.South == End.Boulevard, rooms[0]), (plan.North == End.Boulevard, rooms[rooms.Count - 1]) })
            {
                if (!boulevard || taken.Contains(room)) continue;
                if (Fits(Programme.Fountain, room, plan.Depth)) { room.Programme = Programme.Fountain; taken.Add(room); }
                else plan.Refused.Add($"no room for the plaza at the boulevard: the room there is {room.Length} cells long, it wants 4");
            }

            if (plan.Wanted.Landing)
            {
                Room best = null;
                foreach (var room in rooms)
                    if (!taken.Contains(room) && Fits(Programme.Landing, room, plan.Depth) && (best == null || room.Length > best.Length)) best = room;
                if (best != null)
                {
                    best.Programme = Programme.Landing;
                    best.LandingZ = best.Z0 + best.Length / 2 - 1;
                    taken.Add(best);
                }
                else plan.Refused.Add("no room for the landing: no room of 4 cells is free");
            }

            if (plan.Wanted.Diner)
            {
                Room best = null;
                foreach (var room in rooms)
                    if (!taken.Contains(room) && Fits(Programme.Diner, room, plan.Depth) && (best == null || room.Length > best.Length)) best = room;
                if (best != null) { best.Programme = Programme.Diner; taken.Add(best); }
                else plan.Refused.Add("no room for the diner: no room of 5 cells is free in a strip 7 deep");
            }

            for (int n = 0; n < plan.Wanted.Terraces; n++)
            {
                Room best = null;
                for (int r = 0; r < rooms.Count; r++)
                {
                    var room = rooms[r];
                    if (taken.Contains(room) || !Fits(Programme.Terrace, room, plan.Depth)) continue;
                    if (r > 0 && rooms[r - 1].Programme == Programme.Terrace) continue;
                    if (r + 1 < rooms.Count && rooms[r + 1].Programme == Programme.Terrace) continue;
                    if (best == null || room.Length < best.Length) best = room;
                }
                if (best == null) break;
                best.Programme = Programme.Terrace;
                taken.Add(best);
            }

            foreach (var room in rooms)
            {
                if (taken.Contains(room)) continue;
                if (Fits(Programme.Grove, room, plan.Depth) && rng.NextDouble() < GroveOdds) room.Programme = Programme.Grove;
            }
        }

        /// <summary>How often a room nobody asked for anything of is trees rather than lawn.</summary>
        const double GroveOdds = 0.5;

        /// <summary>A cafe's or a diner's paved apron, either side of the room's middle;
        /// the landing's, either side of its stairs. The rest of such a room is grass: a
        /// promenade that is all paving reads as a car park without the cars.</summary>
        public const float ApronHalf = 12f, LandingApron = 10f, FountainApron = 15f;

        /// <summary>The ground each room's programme stands on: grass for a lawn or a grove,
        /// tarmac for the fair, paving for the plaza, an apron of paving in the grass for a
        /// cafe, a diner or a landing, the walk cut for the landing's stairs.</summary>
        static void Floor(Plan plan)
        {
            foreach (var room in plan.Rooms)
            {
                Ground floor = room.Programme == Programme.Lawn || room.Programme == Programme.Grove ? Ground.Grass
                             : room.Programme == Programme.Fair ? Ground.Yard : Ground.Plaza;
                float middle = (room.Z0 + room.Z1) * 0.5f * Cell, apron = 0f;
                if (room.Programme == Programme.Terrace || room.Programme == Programme.Diner) apron = ApronHalf;
                if (room.Programme == Programme.Fountain) apron = FountainApron;
                if (room.Programme == Programme.Landing && room.LandingZ >= 0) { middle = (room.LandingZ + 1) * Cell; apron = LandingApron; }
                for (int z = room.Z0; z < room.Z1; z++)
                {
                    bool grass = apron > 0f && Mathf.Abs((z + 0.5f) * Cell - middle) > apron;
                    for (int x = Band; x < plan.WalkX; x++)
                        plan.Cells[x, z] = grass ? Ground.Grass : floor;
                }
                if (room.Programme == Programme.Landing && room.LandingZ >= 0)
                    for (int z = room.LandingZ; z < room.LandingZ + 2 && z < room.Z1; z++)
                        for (int x = plan.WalkX; x < plan.Depth; x++)
                            plan.Cells[x, z] = Ground.Landing;
            }
        }

        // ------------------------------------------------------------------ the line

        /// <summary>
        /// One stretch of the core's promenade, read off the core's plan: how deep, how long,
        /// which streets arrive at its kerb, what its two ends meet - and cast with what the
        /// line asks of it.
        /// </summary>
        public static Plan ForQuay(CoreLayout.Plan core, CoreLayout.Block quay, Wants wants, System.Random rng)
        {
            var box = quay.Box;
            var line = core.River;
            int depth = Mathf.RoundToInt(box.width / Cell);
            int length = Mathf.RoundToInt(box.height / Cell);
            var mouths = new List<Mouth>();
            foreach (var band in core.Bands)
            {
                // an east-west street that ends on the quay street: its crossing faces the
                // strip. The boulevard and the bridges go on over the water - the stretch
                // ends at those - and the two north-south bands are the quay street and the
                // far road themselves
                if (band.height > band.width) continue;
                float end = line.East ? band.xMax : band.xMin;
                if (Mathf.Abs(end - line.QuayWater) > 0.01f) continue;
                int z0 = Mathf.RoundToInt((band.yMin - box.yMin) / Cell), z1 = Mathf.RoundToInt((band.yMax - box.yMin) / Cell);
                if (z1 <= 0 || z0 >= length) continue;
                mouths.Add(new Mouth(z0, z1));
            }
            End south = EndAt(core, box.yMin, false), north = EndAt(core, box.yMax, true);
            if (!line.East)
            {
                // a river on the west: the strip is composed with its kerb at x = 0 and its
                // wall at its far x, and turned about to stand; so what the plan calls the
                // start is the north end, and the mouths are read from that end
                for (int m = 0; m < mouths.Count; m++) mouths[m] = new Mouth(length - mouths[m].Z1, length - mouths[m].Z0);
                (south, north) = (north, south);
                wants.FairAtStart = !wants.FairAtStart;
            }
            return Lay(depth, length, mouths, south, north, wants, rng);
        }

        /// <summary>How long the room at one end of a stretch comes out, in cells: from the
        /// line's end (past the street that runs along it) to the next street's edge.</summary>
        static int EndRoom(CoreLayout.Plan core, CoreLayout.Block quay, bool atStart)
        {
            var box = quay.Box;
            float edge = atStart ? box.yMin : box.yMax;
            float first = float.MaxValue, second = float.MaxValue;
            foreach (var band in core.Bands)
            {
                if (band.height > band.width) continue;
                float near = atStart ? band.yMin - edge : edge - band.yMax;
                float far = atStart ? band.yMax - edge : edge - band.yMin;
                if (far <= 0f) continue;
                // the street along the line's end lies at the edge itself; the next one
                // is where the end room stops
                if (near <= 0.01f) { second = Mathf.Min(second, far); continue; }
                first = Mathf.Min(first, near);
            }
            float from = second == float.MaxValue ? 0f : second;
            float to = first == float.MaxValue ? box.height : first;
            return Mathf.Max(0, Mathf.RoundToInt((to - from) / Cell));
        }

        static End EndAt(CoreLayout.Plan core, float z, bool north)
        {
            foreach (var bridge in core.Bridges)
            {
                float edge = north ? bridge.Band.yMin : bridge.Band.yMax;
                if (Mathf.Abs(edge - z) < 0.01f) return bridge.Boulevard ? End.Boulevard : End.Bridge;
            }
            return End.Line;
        }

        /// <summary>
        /// What the line asks of each of its stretches, in the order the core lists them
        /// (south to north): the fairground once, at whichever end of the line has the
        /// longer end room - the room between the line's end and the first street, which
        /// is as long as the belt park beside it is deep; the landing once, on the longest
        /// stretch; a cafe or two on every stretch long enough to want one.
        /// </summary>
        public static Wants[] Cast(CoreLayout.Plan core)
        {
            int n = core.Quays.Count;
            var wants = new Wants[n];
            if (n == 0) return wants;
            int longest = 0;
            for (int q = 1; q < n; q++)
                if (core.Quays[q].Box.height > core.Quays[longest].Box.height) longest = q;
            int fair = EndRoom(core, core.Quays[0], true) >= EndRoom(core, core.Quays[n - 1], false) ? 0 : n - 1;
            for (int q = 0; q < n; q++)
            {
                int length = Mathf.RoundToInt(core.Quays[q].Box.height / Cell);
                // a cafe every forty metres or so (the user, 2026-08-26: "dodaj kafice,
                // dinere, propsa dosta"), the diner where the landing is - the longest
                // stretch has the rooms for both
                wants[q] = new Wants
                {
                    Fair = q == fair,
                    FairAtStart = q == 0,
                    Landing = q == longest,
                    Diner = q == longest,
                    Terraces = Mathf.Max(length >= 6 ? 1 : 0, length / 8),
                };
            }
            return wants;
        }

        // --------------------------------------------------------------- the verdict

        /// <summary>
        /// What the plan came to, and what is wrong with it. Faults: a cell of no ground; the
        /// walk broken between the two ends; a mouth with no paved way to the walk; a room
        /// longer than a block; a programme in a room it does not fit; two fairgrounds. What
        /// was asked for and refused is said, not counted.
        /// </summary>
        public static string Report(Plan plan, out int faults)
        {
            faults = 0;
            var sb = new StringBuilder();
            var cast = new Dictionary<Programme, int>();
            foreach (var room in plan.Rooms) cast[room.Programme] = cast.TryGetValue(room.Programme, out var c) ? c + 1 : 1;
            sb.Append($"{plan.Depth * Cell:F0} m deep, {plan.Length * Cell:F0} m long, {plan.Mouths.Count} mouths, {plan.Rooms.Count} rooms:");
            // in the enum's order, not the dictionary's, so two runs of one seed read the same
            foreach (Programme what in System.Enum.GetValues(typeof(Programme)))
                if (cast.TryGetValue(what, out var many)) sb.Append($" {many} {what.ToString().ToLowerInvariant()}");

            int none = 0;
            for (int x = 0; x < plan.Depth; x++)
                for (int z = 0; z < plan.Length; z++)
                    if (plan.Cells[x, z] == Ground.None) none++;
            if (none > 0) { faults++; sb.Append($"; WARNING: {none} cell(s) with no ground"); }

            // the walk, end to end, over its own paving
            int broken = 0;
            for (int z = 0; z < plan.Length; z++)
            {
                bool any = false;
                for (int x = plan.WalkX; x < plan.Depth; x++) if (plan.Paved(x, z)) any = true;
                if (!any) broken++;
            }
            // and every mouth has its lane, unless the mouth is the kerb row at a bridge
            foreach (var mouth in plan.Mouths)
            {
                bool laned = false;
                for (int z = mouth.Z0; z < mouth.Z1; z++)
                    if (plan.At(Band, z) == Ground.Lane || plan.At(Band, z) == Ground.Kerb) laned = true;
                if (!laned) { faults++; sb.Append($"; WARNING: the mouth at z {mouth.Z0} has no lane across the strip"); }
            }
            if (broken > 0) { faults++; sb.Append($"; WARNING: the walk is broken at {broken} cell(s)"); }

            // every mouth reaches the walk over paving
            var reached = Reach(plan);
            int lost = 0;
            foreach (var mouth in plan.Mouths)
            {
                bool any = false;
                for (int z = mouth.Z0; z < mouth.Z1; z++) if (reached[Band, z]) any = true;
                if (!any) lost++;
            }
            if (lost > 0) { faults++; sb.Append($"; WARNING: {lost} mouth(s) with no paved way to the walk"); }

            int fairs = 0;
            foreach (var room in plan.Rooms)
            {
                if (room.Length > RoomMax) { faults++; sb.Append($"; WARNING: a room of {room.Length} cells at z {room.Z0}, longer than a block"); }
                if (room.Programme == Programme.Fair) fairs++;
                if (!Fits(room.Programme, room, plan.Depth))
                {
                    faults++;
                    sb.Append($"; WARNING: a {room.Programme.ToString().ToLowerInvariant()} in a room of {room.Length} cells at z {room.Z0} it does not fit");
                }
            }
            if (fairs > 1) { faults++; sb.Append($"; WARNING: {fairs} fairgrounds"); }
            // everything above is Lay's own construction checked against itself; what CAN
            // fail is the cast against the wants - the deal asked this stretch for a fair,
            // a landing, a diner, and the rooms the mouths left did not hold it
            bool landing = false, diner = false;
            foreach (var room in plan.Rooms)
            {
                if (room.Programme == Programme.Landing) landing = true;
                if (room.Programme == Programme.Diner) diner = true;
            }
            if (plan.Wanted.Fair && fairs == 0) { faults++; sb.Append("; WARNING: the fair was wanted and there is no room for it"); }
            if (plan.Wanted.Landing && !landing) { faults++; sb.Append("; WARNING: the landing was wanted and there is no room for it"); }
            if (plan.Wanted.Diner && !diner) { faults++; sb.Append("; WARNING: the diner was wanted and there is no room for it"); }
            foreach (var refused in plan.Refused) sb.Append("; ").Append(refused);
            return sb.ToString();
        }

        /// <summary>Paved cells reachable from the walk over paving.</summary>
        static bool[,] Reach(Plan plan)
        {
            var seen = new bool[plan.Depth, plan.Length];
            var todo = new Queue<(int x, int z)>();
            for (int z = 0; z < plan.Length; z++)
                for (int x = plan.WalkX; x < plan.Depth; x++)
                    if (plan.Paved(x, z)) { seen[x, z] = true; todo.Enqueue((x, z)); }
            while (todo.Count > 0)
            {
                var (x, z) = todo.Dequeue();
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= plan.Depth || nz >= plan.Length) continue;
                    if (seen[nx, nz] || !plan.Paved(nx, nz)) continue;
                    seen[nx, nz] = true;
                    todo.Enqueue((nx, nz));
                }
            }
            return seen;
        }

        /// <summary>The strip drawn north up, the kerb on the left and the water on the
        /// right: ':' kerb, '+' lane, '#' paving, '.' grass, 'W' walk, 'd' landing, 'F'
        /// fairground; the room's programme in its margin.</summary>
        static string Draw(Plan plan)
        {
            var sb = new StringBuilder();
            for (int z = plan.Length - 1; z >= 0; z--)
            {
                sb.Append((z * (int)Cell).ToString().PadLeft(5)).Append(' ');
                for (int x = 0; x < plan.Depth; x++)
                    sb.Append(plan.Cells[x, z] switch
                    {
                        Ground.Kerb => ':', Ground.Lane => '+', Ground.Plaza => '#', Ground.Grass => '.',
                        Ground.Walk => 'W', Ground.Landing => 'd', Ground.Yard => 'F', _ => '?',
                    });
                sb.Append("~ ");
                foreach (var room in plan.Rooms)
                    if (z == room.Z0 + room.Length / 2) sb.Append(room.Programme.ToString().ToLowerInvariant());
                sb.Append((char)10);
            }
            return sb.ToString();
        }
    }
}
