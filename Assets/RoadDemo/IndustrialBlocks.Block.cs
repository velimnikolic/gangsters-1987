using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadDemo
{
    public static partial class IndustrialBlocks
    {
        // --------------------------------------------------------------------- the block

        /// <summary>
        /// One parcel under composition: which cells it holds, what is on the floor of each,
        /// what stands on them, and which of its four sides is kerb and which a shared fence.
        /// </summary>
        public sealed class Block
        {
            public readonly Transform Root;
            public readonly int W, D, NX, NZ;
            public readonly System.Random Rng;

            /// <summary>South, north, west, east - in the block's OWN frame, which always
            /// faces south. <see cref="IndustrialLayout.Parcel.Local"/> turns the quarter's
            /// compass into this one.</summary>
            readonly IndustrialLayout.Edge[] _sides;

            readonly bool[] _held;
            readonly bool[] _laid;      // something has already floored the cell
            readonly bool[] _drive;     // the pavement gives way to road here: the way in
            readonly bool[] _corridor;  // the drive itself, which nothing else may surface
            readonly Surface[] _floor;
            readonly List<Rect> _taken = new List<Rect>();
            readonly List<Rect> _footprints = new List<Rect>();
            readonly Dictionary<string, int> _refused = new Dictionary<string, int>();

            Vector2 _way;

            /// <summary>What the yard is floored with where nothing else has claimed it.</summary>
            public Surface Ground = Surface.Asphalt;

            /// <summary>Brick unless the block is a yard; none at all for a forecourt, which
            /// is open to the road by definition.</summary>
            public Wall Wall = Wall.Brick;

            // ---- the rectangle a building may stand in, per side

            int Ring(IndustrialLayout.Side side) =>
                _sides[(int)side].Rim == IndustrialLayout.Rim.Kerb ? 1 : 0;

            float Back(IndustrialLayout.Side side) =>
                _sides[(int)side].Rim == IndustrialLayout.Rim.Kerb ? Setback : Party;

            public float In => Ring(IndustrialLayout.Side.West) * Cell + Back(IndustrialLayout.Side.West);
            public float Out => W - Ring(IndustrialLayout.Side.East) * Cell - Back(IndustrialLayout.Side.East);
            public float Near => Ring(IndustrialLayout.Side.South) * Cell + Back(IndustrialLayout.Side.South);
            public float Far => D - Ring(IndustrialLayout.Side.North) * Cell - Back(IndustrialLayout.Side.North);

            /// <summary>The ground inside the perimeter, <paramref name="inset"/> metres in
            /// from the wall line on every side. The wall stands on the kerb ring's inner
            /// edge where there is a kerb and on the boundary itself where the side is a
            /// shared fence, so this is not the same rectangle as In/Near/Out/Far - those
            /// are where a BUILDING may stand, a setback behind the wall.</summary>
            public Rect Yard(float inset) =>
                Rect.MinMaxRect(Ring(IndustrialLayout.Side.West) * Cell + inset,
                                Ring(IndustrialLayout.Side.South) * Cell + inset,
                                W - Ring(IndustrialLayout.Side.East) * Cell - inset,
                                D - Ring(IndustrialLayout.Side.North) * Cell - inset);

            public Block(Transform root, int w, int d, IndustrialLayout.Edge[] sides, System.Random rng)
            {
                Root = root;
                W = w;
                D = d;
                Rng = rng;
                _sides = sides;
                NX = Mathf.Max(3, w / (int)Cell);
                NZ = Mathf.Max(3, d / (int)Cell);
                _held = new bool[NX * NZ];
                _laid = new bool[NX * NZ];
                _drive = new bool[NX * NZ];
                _corridor = new bool[NX * NZ];
                _floor = new Surface[NX * NZ];
                for (int k = 0; k < _held.Length; k++)
                {
                    _held[k] = true;
                    _floor[k] = Surface.Asphalt;
                }
            }

            int At(int i, int j) => j * NX + i;

            bool Held(int i, int j) => i >= 0 && j >= 0 && i < NX && j < NZ && _held[At(i, j)];

            /// <summary>Which side of the block a step off (i, j) leaves by. Only asked of a
            /// step that leaves the block's own rectangle.</summary>
            static IndustrialLayout.Side Leaving(int di, int dj) =>
                di < 0 ? IndustrialLayout.Side.West : di > 0 ? IndustrialLayout.Side.East
                       : dj < 0 ? IndustrialLayout.Side.South : IndustrialLayout.Side.North;

            /// <summary>Does the block's PAVEMENT run through this cell? True on an outer
            /// edge, false where the block simply meets its neighbour: there the fence is
            /// the boundary and the ground up to it is yard.</summary>
            bool Kerbed(int i, int j)
            {
                if (!Held(i, j)) return false;
                foreach (var step in Steps)
                {
                    int ni = i + step.x, nj = j + step.y;
                    if (Held(ni, nj)) continue;
                    // a bite out of the middle of the block is the street's, so its edge is
                    // kerb whatever the sides say
                    if (ni >= 0 && nj >= 0 && ni < NX && nj < NZ) return true;
                    if (_sides[(int)Leaving(step.x, step.y)].Rim == IndustrialLayout.Rim.Kerb) return true;
                }
                return false;
            }

            /// <summary>
            /// Ground a building stands on, whole cells and part cells alike.
            ///
            /// This is the difference between the rule and the bug. <c>_laid</c> is set by
            /// <see cref="Claim"/> only for cells a footprint covers ENTIRELY, because that
            /// is the right test for flooring - a cell lapped halfway still wants its tile.
            /// It is the wrong test for everything else: the drive and the gateway both read
            /// it, so both were free to run road straight through the half of a cell a
            /// building was standing on, and the building came out with its front on
            /// pavement and its flank on tarmac.
            /// </summary>
            bool Apron(int i, int j)
            {
                var cell = new Rect(i * Cell, j * Cell, Cell, Cell);
                foreach (var foot in _footprints)
                    if (foot.Overlaps(cell)) return true;
                return false;
            }

            /// <summary>Is this cell the block's PAVEMENT - a plate, or a kerb tile of the
            /// outer ring? The drive is not: it is road cut through the pavement, and the
            /// pavement it passes wants a kerb against it like any other edge.</summary>
            bool Pave(int i, int j)
            {
                if (!Held(i, j)) return false;
                if (_drive[At(i, j)] || _corridor[At(i, j)]) return false;
                return Kerbed(i, j) || _floor[At(i, j)] == Surface.Plate;
            }

            /// <summary>The block's own working ground: held, and not pavement. What an
            /// inside kerb faces.</summary>
            bool Bare(int i, int j) => Held(i, j) && !Pave(i, j);

            /// <summary>The block's outline, kerb or fence alike.</summary>
            bool Rim(int i, int j)
            {
                if (!Held(i, j)) return false;
                foreach (var step in Steps)
                    if (!Held(i + step.x, j + step.y)) return true;
                return false;
            }

            static readonly Vector2Int[] Steps =
            {
                new Vector2Int(-1, 0), new Vector2Int(1, 0),
                new Vector2Int(0, -1), new Vector2Int(0, 1),
            };

            /// <summary>Takes a bite out of the block. The street will make it a car park;
            /// the block simply stops there and its kerb turns the corner.</summary>
            public void Bite(int i0, int j0, int ni, int nj)
            {
                for (int i = i0; i < i0 + ni; i++)
                    for (int j = j0; j < j0 + nj; j++)
                        if (i >= 0 && j >= 0 && i < NX && j < NZ) _held[At(i, j)] = false;
            }

            // ---- the way in

            /// <summary>
            /// The way in, as a span in metres along the south kerb - and a block is always
            /// composed facing south, so this is always the street it fronts.
            ///
            /// Setting it lays the DRIVE at the same time: road surface from the kerb
            /// straight in until something is standing in the way, and that ground booked so
            /// nothing is set down on it afterwards.
            /// </summary>
            public Vector2 Way
            {
                get => _way;
                set { _way = value; Corridor(); Drive(_way); }
            }

            void Corridor()
            {
                if (_way.y <= _way.x) return;
                for (int i = 0; i < NX; i++)
                {
                    float a = i * Cell, b = a + Cell;
                    if (Mathf.Min(b, _way.y) - Mathf.Max(a, _way.x) < Cell * 0.4f) continue;

                    int first = -1;
                    for (int j = 0; j < NZ; j++) if (Held(i, j)) { first = j; break; }
                    if (first < 0 || !Rim(i, first)) continue;

                    int last = first;
                    for (int j = first + 1; j < NZ; j++)
                    {
                        // it stops at the far boundary, kerb or fence: a drive that runs
                        // into the neighbour's yard is not a drive. And it stops at a
                        // building's ground, ANY part of it - not just the cells a building
                        // fills whole, which is what let it take half a cell out from under
                        // a shed and leave it standing on two surfaces
                        if (!Held(i, j) || Rim(i, j) || _laid[At(i, j)] || Apron(i, j)) break;
                        _floor[At(i, j)] = Surface.Asphalt;
                        _corridor[At(i, j)] = true;
                        last = j;
                    }
                    if (last > first)
                        _taken.Add(new Rect(a, (first + 1) * Cell, Cell, (last - first) * Cell));
                }
            }

            // ---- the wall's accounting

            float _wallWanted, _wallLaid;

            /// <summary>
            /// What the wall misses, measured against the GROUND it is meant to ring rather
            /// than against what the run-builder happened to ask for. Asking the builder is
            /// no test at all: a side it never worked out is a side it never wanted, and the
            /// hole goes unreported.
            ///
            /// A shared fence the NEIGHBOUR lays is not this block's to miss, so that
            /// stretch is left out of the reckoning on both counts.
            /// </summary>
            public float WallGap
            {
                get
                {
                    float should = 0f;
                    for (int j = 0; j < NZ; j++)
                        for (int i = 0; i < NX; i++)
                        {
                            if (!Held(i, j) || Kerbed(i, j)) continue;
                            foreach (var step in Steps)
                            {
                                int ni = i + step.x, nj = j + step.y;
                                if (Inside(ni, nj)) continue;
                                bool leaves = ni < 0 || nj < 0 || ni >= NX || nj >= NZ;
                                if (leaves)
                                {
                                    // a shared line the neighbour is down to build is not
                                    // this block's to miss
                                    var side = _sides[(int)Leaving(step.x, step.y)];
                                    if (!side.Lays) continue;
                                    if (Wall == Wall.None && side.Rim == IndustrialLayout.Rim.Kerb) continue;
                                }
                                // a forecourt owes no wall along its own pavement either,
                                // and must not be charged for one: the street side is where
                                // it is deliberately open
                                else if (Wall == Wall.None && Kerbed(ni, nj)) continue;
                                should += Cell;
                            }
                        }
                    if (Way.y > Way.x) should -= Way.y - Way.x;
                    return Mathf.Max(0f, Mathf.Max(should, _wallWanted) - _wallLaid);
                }
            }

            bool Inside(int i, int j) => Held(i, j) && !Kerbed(i, j);

            /// <summary>Wall pieces standing inside a building, which is the fault that keeps
            /// coming back and cannot be seen from above: a panel, a pillar or a gate leaf
            /// reaching into a wall reads as the building bursting through the perimeter.
            /// Counted rather than eyeballed, and reported with the block.</summary>
            public int WallInBuilding()
            {
                int through = 0;
                foreach (Transform piece in Root)
                {
                    if (!piece.name.StartsWith("SM_Bld_Fence")) continue;
                    if (!WorldBox(piece.gameObject, out var box)) continue;
                    // the box is measured in WORLD space and the footprints are in the
                    // block's own. Half the parcels in a quarter are turned about, so
                    // subtracting the root's POSITION is not the same as leaving its frame -
                    // and done that way this check quietly passed every parcel that faces
                    // north, which is the half of them nobody would think to look at.
                    var lo = Root.InverseTransformPoint(box.min);
                    var hi = Root.InverseTransformPoint(box.max);
                    var stood = Rect.MinMaxRect(Mathf.Min(lo.x, hi.x) + 0.1f, Mathf.Min(lo.z, hi.z) + 0.1f,
                                                Mathf.Max(lo.x, hi.x) - 0.1f, Mathf.Max(lo.z, hi.z) - 0.1f);
                    foreach (var foot in _footprints)
                        if (foot.Overlaps(stood)) { through++; break; }
                }
                return through;
            }

            // ---- booking ground

            /// <summary>Books ground. Only cells covered WHOLE count as floored - a cell a
            /// building laps half of still wants its tile, or there is a sliver of nothing
            /// showing along the wall.</summary>
            void Claim(Rect metres)
            {
                _taken.Add(metres);
                int i0 = Mathf.CeilToInt(metres.xMin / Cell);
                int i1 = Mathf.FloorToInt(metres.xMax / Cell) - 1;
                int j0 = Mathf.CeilToInt(metres.yMin / Cell);
                int j1 = Mathf.FloorToInt(metres.yMax / Cell) - 1;
                for (int i = Mathf.Max(0, i0); i <= Mathf.Min(NX - 1, i1); i++)
                    for (int j = Mathf.Max(0, j0); j <= Mathf.Min(NZ - 1, j1); j++)
                        _laid[At(i, j)] = true;
            }

            /// <summary>Is there room here: inside the block, off the kerb ring, and clear of
            /// everything already standing. A cell against a SHARED fence is ordinary yard
            /// and may be used; a cell of pavement may not.</summary>
            public bool Room(Rect want)
            {
                foreach (var taken in _taken)
                    if (taken.Overlaps(want)) return false;

                return OnYard(want);
            }

            /// <summary>
            /// Ground that belongs to the block and is not the kerb ring - the half of
            /// <see cref="Room"/> that asks about the GROUND rather than about what is already
            /// standing on it.
            ///
            /// Tested at the four corners of a slightly shrunk rectangle, so a piece laid
            /// flush against a cell line is not refused by the last bit of a float.
            /// </summary>
            bool OnYard(Rect want)
            {
                if (want.width <= 0f || want.height <= 0f) return false;

                var inset = new Rect(want.xMin + 0.15f, want.yMin + 0.15f,
                                     Mathf.Max(0.1f, want.width - 0.3f),
                                     Mathf.Max(0.1f, want.height - 0.3f));
                var corners = new[]
                {
                    new Vector2(inset.xMin, inset.yMin), new Vector2(inset.xMax, inset.yMin),
                    new Vector2(inset.xMin, inset.yMax), new Vector2(inset.xMax, inset.yMax),
                };
                foreach (var corner in corners)
                {
                    int i = Mathf.FloorToInt(corner.x / Cell);
                    int j = Mathf.FloorToInt(corner.y / Cell);
                    if (!Held(i, j) || Kerbed(i, j)) return false;
                }
                return true;
            }

            /// <summary>What the block has built on it, so the passes that dress the ground
            /// can keep off the brick and hug the foot of it.</summary>
            public IReadOnlyList<Rect> Built => _footprints;

            // ---- what stands

            /// <summary>A building, seated with the near corner of its footprint where it is
            /// asked for. Returns what it covers - an empty rectangle if the piece is missing
            /// from the project, or if it will not fit where it was asked for.</summary>
            public Rect Put(string path, float minX, float minZ, float yaw)
            {
                var foot = Foot(path, yaw);
                var where = new Rect(minX, minZ, foot.x, foot.y);
                if (!Room(where)) return new Rect();
                var go = IndustrialBlocks.Stand(path, Root, where.center.x, where.center.y, yaw, Deck);
                if (go == null) return new Rect();
                _footprints.Add(where);
                Claim(where);
                return where;
            }

            /// <summary>A prop, if there is room for it. Refused rather than crammed in: a
            /// yard reads as a yard because things are set down where they fit.</summary>
            public GameObject Prop(string path, float x, float z, float yaw, float lift = 0f)
            {
                var foot = Foot(path, yaw);
                var where = new Rect(x - foot.x * 0.5f, z - foot.y * 0.5f, foot.x, foot.y);
                if (!Room(where))
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    _refused[name] = _refused.TryGetValue(name, out var seen) ? seen + 1 : 1;
                    return null;
                }

                var go = Sit(path, Root, x, z, yaw, Deck + lift);
                if (go != null) _taken.Add(where);
                return go;
            }

            /// <summary>
            /// What was asked for and would not fit, worst first.
            ///
            /// Reported with the block because it is the one fault this pipeline could not
            /// see. Every refusal returns null and says nothing, so a recipe whose hand-picked
            /// coordinates went stale - a building grew, a rank got longer - comes out as a
            /// yard that is quietly half empty and a summary that says everything is fine.
            /// </summary>
            public string Refused()
            {
                if (_refused.Count == 0) return "";

                // ties broken by name: a dictionary's own order is not the same twice, and
                // a report that reads differently for the same seed is a report nobody can
                // diff
                var worst = _refused.OrderByDescending(one => one.Value)
                                    .ThenBy(one => one.Key, StringComparer.Ordinal).Take(5)
                                    .Select(one => $"{one.Key} x{one.Value}");
                return string.Join(", ", worst);
            }

            /// <summary>
            /// A flat overlay laid straight on the yard: a worn patch, a puddle, weed through
            /// a crack, litter.
            ///
            /// It books no ground and is refused by nothing standing, which is the whole
            /// point. A puddle under a pallet is still a puddle, and weathering that gave way
            /// wherever the yard is actually USED would be weathering everywhere except where
            /// anyone is looking. The one thing it will not do is lie inside a building or out
            /// on the kerb ring, because there it is not weathering, it is a mistake.
            /// </summary>
            public GameObject Decal(string path, float x, float z, float yaw, float lift = 0f)
            {
                var foot = Foot(path, yaw);
                var where = new Rect(x - foot.x * 0.5f, z - foot.y * 0.5f, foot.x, foot.y);
                if (!OnYard(where)) return null;

                foreach (var built in _footprints)
                    if (built.Overlaps(where)) return null;

                return Sit(path, Root, x, z, yaw, Deck + lift);
            }

            /// <summary>
            /// A thing set down exactly where it is asked for, booking nothing and refused by
            /// nothing.
            ///
            /// For the few pieces whose position IS the point and where a fit test against
            /// ground already booked could only ever say no: a bollard on the cheek of a gate,
            /// a car in a bay that has just been painted, the second container of a stack.
            /// </summary>
            public GameObject Fix(string path, float x, float z, float yaw, float lift = 0f) =>
                Sit(path, Root, x, z, yaw, Deck + lift);

            /// <summary>A flat thing that lies ON the ground and books nothing: a puddle, a
            /// tuft of weed through a crack. It is a decal, and a yard where nothing may be
            /// set down beside a puddle is a yard with puddles for furniture.</summary>
            public GameObject Mark(string path, float x, float z, float yaw) =>
                Decal(path, x, z, yaw, 0.01f);

            /// <summary>A thing that goes on top of another - the second container of a stack
            /// - which has no ground of its own to book.</summary>
            public GameObject Atop(string path, float x, float z, float yaw, float lift) =>
                Fix(path, x, z, yaw, lift);

            /// <summary>
            /// Painted bays, the pack's own ten metres by five, laid on the grid so they take
            /// the floor of the cells they cover - and, where one is asked for, something
            /// standing in one of the two.
            ///
            /// A yard full of freshly painted empty bays is a car park nobody uses. One car in
            /// two is what the pack's own demo does and it is what a works looks like at any
            /// hour somebody is inside it.
            /// </summary>
            public void Bay(float minX, float minZ, float yaw, string parked = null)
            {
                float sizeX = Turned(yaw) ? Cell : Cell * 2f;
                float sizeZ = Turned(yaw) ? Cell * 2f : Cell;
                var where = new Rect(minX, minZ, sizeX, sizeZ);
                if (!Room(where)) return;
                if (Way.y > Way.x && where.yMin < Cell * 4f &&
                    where.xMax > Way.x - 1f && where.xMin < Way.y + 1f) return;   // not across the gate
                IndustrialBlocks.Lay(PaintedBays, Root, minX, minZ, sizeX, sizeZ, yaw);
                Claim(where);

                if (string.IsNullOrEmpty(parked)) return;

                // The bay pair is two 5 m stalls side by side; the car takes the near one and
                // stands nose-in, which on a bay laid at yaw 0 is along z.
                var stall = Turned(yaw)
                    ? new Vector2(where.center.x, where.yMin + Cell * 0.5f)
                    : new Vector2(where.xMin + Cell * 0.5f, where.center.y);
                Fix(parked, stall.x, stall.y, yaw);
            }

            public void Scatter(string path, int count, Rect area, float spread)
            {
                var foot = Foot(path, 0f);
                for (int stood = 0, guard = 0; stood < count && guard < count * 12; guard++)
                {
                    float x = Half(area.xMin + (float)Rng.NextDouble() * Mathf.Max(0f, area.width));
                    float z = Half(area.yMin + (float)Rng.NextDouble() * Mathf.Max(0f, area.height));
                    float yaw = 90f * Rng.Next(4) + ((float)Rng.NextDouble() * 2f - 1f) * spread;
                    var probe = new Rect(x - foot.x * 0.6f, z - foot.y * 0.6f,
                                         foot.x * 1.2f, foot.y * 1.2f);
                    if (!Room(probe)) continue;
                    if (Prop(path, x, z, yaw) != null) stood++;
                }
            }

            /// <summary>Decals strewn over a rectangle. They book nothing, so this is a
            /// count and not an attempt.</summary>
            public void Strew(string[] paths, int count, Rect area)
            {
                for (int k = 0; k < count; k++)
                {
                    float x = area.xMin + (float)Rng.NextDouble() * Mathf.Max(0f, area.width);
                    float z = area.yMin + (float)Rng.NextDouble() * Mathf.Max(0f, area.height);
                    Mark(paths[Rng.Next(paths.Length)], x, z, 90f * Rng.Next(4));
                }
            }

            internal static float Half(float v) => Mathf.Round(v * 2f) * 0.5f;

            // ---- the works stack

            /// <summary>
            /// A works stack: a tall round shaft that TAPERS, with a crown at the top.
            ///
            /// No pack here ships one, and both ways of faking it out of pack pieces were
            /// worse than this. Stacked villa flues are a metre across with a chimney cap
            /// every two and a half metres - a pile of crates. A square shaft of brick wall
            /// panels is a box, and cannot taper at all without scaling the panels below the
            /// size they were drawn at, which this project does not do.
            /// </summary>
            public void Chimney(float x, float z, float height)
            {
                const float Foot = 2.3f, Head = 1.15f, Drum = 2.2f;
                var ground = new Rect(x - Foot, z - Foot, Foot * 2f, Foot * 2f);
                if (!Room(ground))
                {
                    Debug.LogWarning($"[Industrial] no room for the stack at ({x:F1}, {z:F1}) - " +
                                     "something is standing there, most likely the drive.");
                    return;
                }

                var paint = StackPaint();
                int drums = Mathf.Max(4, Mathf.RoundToInt(height / Drum));
                float course = height / drums;
                for (int k = 0; k < drums; k++)
                {
                    float along = (k + 0.5f) / drums;
                    Barrel(x, z, Deck + k * course, course * 1.02f,
                           Mathf.Lerp(Foot, Head, along), paint, "chimney");
                }
                Barrel(x, z, Deck + height, 0.9f, Head * 1.25f, paint, "chimney crown");

                float mouth = Deck + height + 0.9f;
                var plume = Raise(Smoke, Root);
                if (plume != null)
                {
                    plume.transform.SetPositionAndRotation(
                        new Vector3(x, mouth, z), Quaternion.identity);
                    plume.transform.localScale *= 2.6f;
                    var smoke = plume.GetComponentInChildren<ParticleSystem>();
                    LivingCity.Ambient.FireSmokeFx.TintSmoke(
                        smoke, LivingCity.Ambient.FireSmokeFx.ChimneySmoke);
                    if (smoke != null)
                    {
                        var main = smoke.main;
                        main.simulationSpace = ParticleSystemSimulationSpace.World;
                    }
                }
                Claim(ground);
            }

            /// <summary>One drum of the shaft. Unity's cylinder is two units tall and one
            /// across, so the scale is half the height and twice the radius.</summary>
            void Barrel(float x, float z, float bottom, float height, float radius,
                        Material paint, string name)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = name;
                go.transform.SetParent(Root, false);
                go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
                go.transform.position = new Vector3(x, bottom + height * 0.5f, z);
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer) renderer.sharedMaterial = paint;
                var collider = go.GetComponent<Collider>();
                if (collider) UnityEngine.Object.DestroyImmediate(collider);
            }

            /// <summary>
            /// A storage tank: the pack's own oil drum blown up until it is a tank.
            ///
            /// No pack in this project has a tank or a silo, and the harbour settled this
            /// question first - its tank farm is <c>SM_Prop_Barrel_Metal_01</c> scaled to
            /// four metres and up (HarborKit.TankBody). A drum is the right shape already:
            /// a cylinder with two hoop rims, which is what a bunded tank looks like from
            /// across a fence.
            /// </summary>
            public GameObject Tank(float x, float z, float across, float tall)
            {
                var ground = new Rect(x - across * 0.5f, z - across * 0.5f, across, across);
                if (!Room(ground)) return null;
                var go = Sit(BarrelMetal, Root, x, z, 0f, Deck);
                if (go == null) return null;
                var own = Box(BarrelMetal).size;
                if (own.x < 0.01f || own.y < 0.01f) return go;
                go.transform.localScale = Vector3.Scale(go.transform.localScale,
                    new Vector3(across / own.x, tall / own.y, across / own.z));
                if (WorldBox(go, out var box))
                    go.transform.position += new Vector3(x - box.center.x, Deck - box.min.y, z - box.center.z);
                go.name = "tank";
                Claim(ground);
                return go;
            }

            /// <summary>Steam off a vent, stood on a coordinate rather than seated: a
            /// particle renderer reports whatever bounds it is holding until it plays, so
            /// sitting it on them would carry it anywhere at all.</summary>
            public void Vent(float x, float y, float z, float size)
            {
                var puff = Raise(Steam, Root);
                if (puff == null) return;
                puff.transform.position = new Vector3(x, y, z);
                puff.transform.localScale *= size;
            }

            // ---- the fence

            /// <summary>
            /// The wall round the yard, laid on whichever sides are this block's to lay.
            ///
            /// The crown is not a fence and never was - it is 77 cm of razor wire pivoted
            /// three metres up - so it goes as a run of its own, at whatever height puts its
            /// underside on the panel's top.
            /// </summary>
            public void Fence()
            {
                var panel = Box(Panel);
                float panelY = Deck - panel.min.y;
                float crownY = Wall == Wall.Wire
                    ? panelY + panel.max.y - Box(FenceCrown).min.y
                    : 0f;

                Side(true, false, IndustrialLayout.Side.South, panelY, crownY);
                Side(true, true, IndustrialLayout.Side.North, panelY, crownY);
                Side(false, false, IndustrialLayout.Side.West, panelY, crownY);
                Side(false, true, IndustrialLayout.Side.East, panelY, crownY);
            }

            /// <summary>
            /// What a wall-less block still fences.
            ///
            /// <see cref="Wall.None"/> means open TO THE ROAD, which is what a forecourt is.
            /// It does not mean the neighbour goes without the fence this parcel owes them:
            /// a shared line has exactly one builder, and a truck stop that declined to
            /// build left a full-depth hole down the middle of its island - which
            /// <see cref="WallGap"/> then declined to report. So a wall-less block skips its
            /// kerb sides and wires its shared ones.
            /// </summary>
            Wall Facing(bool kerb) => Wall == Wall.None ? Wall.Wire : Wall;

            string Panel => PanelOf(Wall == Wall.None ? Wall.Wire : Wall);

            static string PanelOf(Wall wall) => wall == Wall.Wire ? FencePanel : BrickPanel;

            static string UprightOf(Wall wall) => wall == Wall.Wire ? FencePole : BrickPillar;

            /// <summary>One side of the yard. <paramref name="alongX"/> is a run east-west,
            /// <paramref name="far"/> picks the north or the east one of the pair. The line
            /// it runs on is read off the mask column by column, so a bitten block fences
            /// round its own bite instead of straight across it.</summary>
            void Side(bool alongX, bool far, IndustrialLayout.Side which, float panelY, float crownY)
            {
                if (!_sides[(int)which].Lays) return;
                bool kerb = _sides[(int)which].Rim == IndustrialLayout.Rim.Kerb;
                if (Wall == Wall.None && kerb) return;    // open to the road, fenced from the neighbour
                var wall = Facing(kerb);

                int outer = alongX ? NX : NZ;
                int inner = alongX ? NZ : NX;
                var wanted = new bool[outer];
                var line = new float[outer];
                var ring = new int[outer];

                for (int a = 0; a < outer; a++)
                    for (int b = 0; b < inner; b++)
                    {
                        int step = far ? inner - 1 - b : b;
                        int i = alongX ? a : step;
                        int j = alongX ? step : a;
                        if (!Held(i, j)) continue;
                        wanted[a] = true;
                        // against a kerb the wall stands just INSIDE the pavement cell;
                        // against a shared fence there is no pavement, and the line is the
                        // boundary itself
                        line[a] = far ? (kerb ? step : step + 1) * Cell
                                      : (kerb ? step + 1 : step) * Cell;
                        ring[a] = step;
                        break;
                    }

                int from = -1;
                for (int a = 0; a <= outer; a++)
                {
                    bool joins = a < outer && wanted[a] &&
                                 (from < 0 || Mathf.Abs(line[a] - line[from]) < 0.01f);
                    if (joins)
                    {
                        if (from < 0) from = a;
                        continue;
                    }
                    if (from >= 0)
                    {
                        WireUp(wall, Corner(from, a - 1, ring, alongX, out bool startsFree, out bool endsFree),
                               line[from], alongX, far, panelY, crownY, startsFree, endsFree);
                        from = -1;
                    }
                    if (a < outer && wanted[a]) from = a;
                }
            }

            /// <summary>
            /// The stretch a run of wall covers, which is not the whole stretch of block it
            /// belongs to.
            ///
            /// A column at the very end of a side is a CORNER cell - the one the wall along
            /// the next side has to turn through - so the run stops a cell short of it and
            /// the two sides MEET there instead of crossing. Without this every block wears
            /// two panels through each other at all four corners.
            ///
            /// Unless that end is a SHARED FENCE, and then it is no corner at all: the wall
            /// carries straight on into the neighbour's, and stopping short of it leaves a
            /// ten metre hole in the middle of an island's frontage.
            /// </summary>
            Vector2 Corner(int first, int last, int[] ring, bool alongX,
                           out bool startsFree, out bool endsFree)
            {
                float from = first * Cell, to = (last + 1) * Cell;
                bool openBefore = alongX ? !Held(first - 1, ring[first])
                                         : !Held(ring[first], first - 1);
                bool openAfter = alongX ? !Held(last + 1, ring[last])
                                        : !Held(ring[last], last + 1);

                var before = alongX ? IndustrialLayout.Side.West : IndustrialLayout.Side.South;
                var after = alongX ? IndustrialLayout.Side.East : IndustrialLayout.Side.North;
                bool partyBefore = _sides[(int)before].Rim == IndustrialLayout.Rim.Party;
                bool partyAfter = _sides[(int)after].Rim == IndustrialLayout.Rim.Party;

                // an end where the block STOPS is a corner the next side turns through, so
                // the run stops a cell short of it; an end where the block carries on at a
                // different depth is the INSIDE corner of a notch, and there the two runs do
                // not meet at all but pass each other five metres apart, so that one is
                // reached out to instead
                from += openBefore ? (partyBefore ? 0f : Cell) : -Cell;
                to += openAfter ? (partyAfter ? 0f : -Cell) : Cell;
                startsFree = openBefore && partyBefore;
                endsFree = openAfter && partyAfter;
                return new Vector2(from, to);
            }

            /// <summary>Wall along one straight stretch, cut only where the way in is: the
            /// wall goes all the way round and the gate is the only thing that breaks it.
            /// Buildings are set back behind it rather than being it.</summary>
            void WireUp(Wall wall, Vector2 span, float line, bool alongX, bool far, float panelY,
                        float crownY, bool startsFree, bool endsFree)
            {
                var free = new List<Vector2> { span };

                // the gate hangs in the ONE run its opening falls in. Judged per SIDE, a
                // bitten block hung a second pair of leaves on every other run of its south
                // side, standing free in no opening at all
                float middle = (Way.x + Way.y) * 0.5f;
                bool wayIn = alongX && !far && Way.y > Way.x &&
                             middle > span.x - 0.01f && middle < span.y + 0.01f;
                if (wayIn) Cut(free, Way);

                foreach (var piece in free) _wallWanted += piece.y - piece.x;

                foreach (var piece in free)
                {
                    if (piece.y - piece.x < 1.2f) continue;
                    _wallLaid += Run(PanelOf(wall), piece, line, alongX, far, panelY);
                    if (wall == Wall.Wire) Run(FenceCrown, piece, line, alongX, far, crownY);
                    // a post where a run ends against nothing, and none where it runs on
                    // into the neighbour's fence: there the neighbour's own end post stands
                    if (!(startsFree && Mathf.Abs(piece.x - span.x) < 0.01f)) Pillar(wall, piece.x, line, alongX, far);
                    if (!(endsFree && Mathf.Abs(piece.y - span.y) < 0.01f)) Pillar(wall, piece.y, line, alongX, far);
                    StreetPlate(piece, line, alongX, far);
                }

                if (!wayIn) return;

                // both leaves stand INSIDE the opening and swung back into the yard, so the
                // way in is open. Thrown the other way each leaf reaches over whatever the
                // gate is cut between - and a gate is usually cut between two buildings.
                var gate = Foot(FenceGate, 90f);
                IndustrialBlocks.Lay(FenceGate, Root, Way.x, line, gate.x, gate.y, 90f, panelY);
                IndustrialBlocks.Lay(FenceGate, Root, Way.y - gate.x, line, gate.x, gate.y, 90f, panelY);
            }

            static void Cut(List<Vector2> spans, Vector2 bite)
            {
                for (int k = spans.Count - 1; k >= 0; k--)
                {
                    var span = spans[k];
                    if (bite.y <= span.x || bite.x >= span.y) continue;
                    spans.RemoveAt(k);
                    if (bite.y < span.y) spans.Insert(k, new Vector2(bite.y, span.y));
                    if (bite.x > span.x) spans.Insert(k, new Vector2(span.x, bite.x));
                }
            }

            /// <summary>Lays a run and returns the metres it covered. The run divides into
            /// whole modules and STRETCHES them; rounded up, a module would come out shorter
            /// than the piece it is - a prefab scaled below the size it was drawn at, which
            /// this project does not do.</summary>
            float Run(string path, Vector2 span, float line, bool alongX, bool far, float y)
            {
                float length = span.y - span.x;
                if (length < 0.5f) return 0f;

                float yaw = alongX ? 0f : 90f;
                var foot = Foot(path, yaw);
                float module = alongX ? foot.x : foot.y;
                float thick = alongX ? foot.y : foot.x;
                if (module < 0.2f) return 0f;

                if (length < module)
                {
                    float over = (module - length) * 0.5f;
                    span = new Vector2(span.x - over, span.y + over);
                    length = module;
                }
                int panels = Mathf.Max(1, Mathf.FloorToInt(length / module + 0.01f));
                float step = length / panels;
                // only what actually STOOD counts. Returned whole, this would report a
                // perimeter with no fence in it as a perimeter with no holes in it, which
                // is precisely the reading WallGap exists to make impossible
                float laid = 0f;
                for (int k = 0; k < panels; k++)
                {
                    float a = span.x + k * step;
                    var piece = alongX
                        ? IndustrialBlocks.Lay(path, Root, a, far ? line - thick : line, step, thick, yaw, y)
                        : IndustrialBlocks.Lay(path, Root, far ? line - thick : line, a, thick, step, yaw, y);
                    if (piece != null) laid += step;
                }
                return laid;
            }

            /// <summary>
            /// A keep-out plate on the STREET face of a run of wall - the one thing somebody
            /// walking past a blank perimeter can actually read off it, and a fitting the
            /// pack's own demo hangs ten of round a single compound.
            ///
            /// The outer face of a wall is at <paramref name="line"/> whichever side of the
            /// block it is on: a run laid near occupies [line, line + thick] and one laid far
            /// occupies [line - thick, line], so both put their street side exactly there. The
            /// only thing that changes between the four sides is which way the plate looks,
            /// which is the yaw - and the three centimetres it stands proud, which is the sign.
            ///
            /// Only where there IS a street. A shared fence has the neighbour's yard on its
            /// other side, and a plate hung there faces nobody but their forklift.
            /// </summary>
            void StreetPlate(Vector2 span, float line, bool alongX, bool far)
            {
                var side = alongX ? (far ? IndustrialLayout.Side.North : IndustrialLayout.Side.South)
                                  : (far ? IndustrialLayout.Side.East : IndustrialLayout.Side.West);
                if (_sides[(int)side].Rim != IndustrialLayout.Rim.Kerb) return;
                if (span.y - span.x < 4f || !Chance(Rng, 0.6)) return;

                var at = Between(Rng, span.x + 1.5f, span.y - 1.5f);
                var off = far ? 0.03f : -0.03f;
                var yaw = alongX ? (far ? 0f : 180f) : (far ? 90f : 270f);

                // Stood rather than sat: this plate pivots at its own middle, and dropping it
                // onto its underside would hang it a hand's breadth higher than asked for.
                IndustrialBlocks.Stand(KeepOut, Root,
                                       alongX ? at : line + off,
                                       alongX ? line + off : at,
                                       yaw, Deck + 1.9f);
            }

            void Pillar(Wall wall, float at, float line, bool alongX, bool far)
            {
                float inward = far ? -0.25f : 0.25f;
                float x = alongX ? at : line + inward;
                float z = alongX ? line + inward : at;
                Sit(UprightOf(wall), Root, x, z, 0f, Deck);
            }

            /// <summary>Takes the way in out of the pavement. Not a dropped kerb: the
            /// pavement STOPS and the road comes through it, the way a yard gate crosses a
            /// footway anywhere.</summary>
            void Drive(Vector2 span)
            {
                for (int i = 0; i < NX; i++)
                {
                    float a = i * Cell, b = a + Cell;
                    if (Mathf.Min(b, span.y) - Mathf.Max(a, span.x) < Cell * 0.4f) continue;
                    for (int j = 0; j < NZ; j++)
                    {
                        if (!Held(i, j)) continue;
                        if (Kerbed(i, j)) _drive[At(i, j)] = true;
                        break;
                    }
                }
            }

            // ---- the ground

            /// <summary>
            /// What the ground is made of, decided in one place for the whole block.
            ///
            /// A yard is ASPHALT. Pavement is the ground people walk on, and in a works
            /// there are exactly two strips of it: the band inside the wall, and a skirt
            /// round each building. Everything else is where the lorries go.
            /// </summary>
            public void Surfaces()
            {
                for (int k = 0; k < _floor.Length; k++)
                    if (!_corridor[k]) _floor[k] = Ground;

                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Held(i, j) || Kerbed(i, j) || _corridor[At(i, j)]) continue;

                        // the walk inside the wall - which, against a shared fence, is the
                        // boundary cell itself, there being no pavement outside it
                        bool walk = Kerbed(i - 1, j) || Kerbed(i + 1, j) ||
                                    Kerbed(i, j - 1) || Kerbed(i, j + 1) || Rim(i, j);

                        // and the skirt a building stands on. A SKIRT, not a five metre
                        // apron: at a cell to the metre, an apron that wide meets the next
                        // building's and the walk inside the wall, and the yard comes out as
                        // islands of asphalt with pavement running between them
                        if (!walk)
                        {
                            var cell = new Rect(i * Cell, j * Cell, Cell, Cell);
                            foreach (var foot in _footprints)
                            {
                                var skirt = new Rect(foot.xMin - 1.2f, foot.yMin - 1.2f,
                                                     foot.width + 2.4f, foot.height + 2.4f);
                                if (!skirt.Overlaps(cell)) continue;
                                walk = true;
                                break;
                            }
                        }

                        if (walk) _floor[At(i, j)] = Surface.Plate;
                    }

                // and then, over the top of all of it: EVERY cell a building touches is
                // pavement. The skirt above already reaches them, but saying it outright is
                // what makes it a rule rather than a consequence - a building stands on one
                // surface, and it is this one.
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                        if (Held(i, j) && !Kerbed(i, j) && Apron(i, j))
                            _floor[At(i, j)] = Surface.Plate;

                Gateway();
            }

            /// <summary>The way in, cut through whatever the walk and the skirts put in
            /// front of it. It runs from the kerb until it reaches ground that is the yard's
            /// own surface anyway, and stops there - a gateway is the few metres between the
            /// street and the yard, and no more.</summary>
            void Gateway()
            {
                if (Way.y <= Way.x) return;
                for (int i = 0; i < NX; i++)
                {
                    float a = i * Cell, b = a + Cell;
                    if (Mathf.Min(b, Way.y) - Mathf.Max(a, Way.x) < Cell * 0.4f) continue;

                    int first = -1;
                    for (int j = 0; j < NZ; j++) if (Held(i, j)) { first = j; break; }
                    if (first < 0 || !Rim(i, first)) continue;

                    for (int j = first + 1; j < NZ; j++)
                    {
                        if (!Held(i, j) || Rim(i, j)) break;
                        if (_floor[At(i, j)] != Surface.Plate) break;   // the yard: done
                        // a building's ground is NOT the gateway's to take. It used to set
                        // the cell to road and THEN notice the building on it, which is
                        // precisely a building standing half on pavement and half on road
                        if (Apron(i, j)) break;
                        _floor[At(i, j)] = Surface.Asphalt;
                    }
                }
            }

            /// <summary>
            /// The kerb, all the way round whatever shape the mask came out as - and only
            /// where the block's own pavement runs, which is not where a fence is shared.
            ///
            /// Which way a kerb tile faces is not guessed at. The street kit lays a road's
            /// south pavement at yaw 0, which puts the raised stone on the tile's +Z side,
            /// and every other side follows from that; the corner piece carries its stone on
            /// +X and +Z at yaw 0, so a block's north-east corner is 0 and the other three
            /// are quarter turns from it.
            /// </summary>
            public void Kerbs()
            {
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Kerbed(i, j)) continue;

                        if (_drive[At(i, j)])
                        {
                            _laid[At(i, j)] =
                                IndustrialBlocks.Lay(Asphalt, Root, i * Cell, j * Cell, Cell, Cell, 0f) != null;
                            continue;
                        }

                        // which way the pavement turns is read off the OUTER sides only: a
                        // block whose east fence is shared has no corner there, and a corner
                        // tile laid at one would point its stone into the neighbour's yard
                        bool west = Open(i, j, -1, 0), east = Open(i, j, 1, 0);
                        bool south = Open(i, j, 0, -1), north = Open(i, j, 0, 1);
                        if (!west && !east && !south && !north) continue;
                        LayKerb(i, j, west, east, south, north);
                    }

                Inside();
            }

            /// <summary>
            /// The kerb round the pavement INSIDE the block, corners and all.
            ///
            /// The yard is tarmac and the ground a building stands on is concrete, and where
            /// the two meet there is a kerb - the same kerb, turning the same corners, as the
            /// one at the street. Without it the apron was a flat plate butted against the
            /// asphalt with nothing but a change of colour between them, which from a car is
            /// no edge at all.
            ///
            /// It goes wherever pavement meets the block's own working ground, and the ONLY
            /// thing that stops it is a building standing on the stone itself.
            ///
            /// The first rule was "not where a building stands", meaning any cell a footprint
            /// touched - and a footprint usually takes a corner of a cell and stops, so the
            /// very cells that ARE the pavement's edge were the ones ruled out, and half the
            /// aprons came out unkerbed. What matters is not whether the building is in the
            /// cell but whether it is on the 90 cm of it the stone occupies, which is what
            /// <see cref="Clear"/> asks.
            ///
            /// Against a shared fence there is no kerb at all: the thing on the far side is
            /// the neighbour, not a road.
            /// </summary>
            void Inside()
            {
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Held(i, j) || _laid[At(i, j)]) continue;
                        if (_floor[At(i, j)] != Surface.Plate) continue;

                        bool west = Bare(i - 1, j) && Clear(i, j, -1, 0);
                        bool east = Bare(i + 1, j) && Clear(i, j, 1, 0);
                        bool south = Bare(i, j - 1) && Clear(i, j, 0, -1);
                        bool north = Bare(i, j + 1) && Clear(i, j, 0, 1);
                        if (!west && !east && !south && !north) continue;
                        LayKerb(i, j, west, east, south, north);
                    }
            }

            /// <summary>Is the strip of this cell the kerb stone would stand on free of every
            /// building? A kerb under a shed is a kerb nobody can see, and a shed standing on
            /// a step.</summary>
            bool Clear(int i, int j, int di, int dj)
            {
                const float Stone = 0.9f;
                float x = i * Cell, z = j * Cell;
                var strip = di < 0 ? new Rect(x, z, Stone, Cell)
                          : di > 0 ? new Rect(x + Cell - Stone, z, Stone, Cell)
                          : dj < 0 ? new Rect(x, z, Cell, Stone)
                                   : new Rect(x, z + Cell - Stone, Cell, Stone);
                foreach (var foot in _footprints)
                    if (foot.Overlaps(strip)) return false;
                return true;
            }

            /// <summary>
            /// One kerb tile, facing the sides given.
            ///
            /// Which way it faces is not guessed at. The street kit lays a road's south
            /// pavement at yaw 0, which puts the raised stone on the tile's +Z side, and
            /// every other side follows from that; the corner piece carries its stone on +X
            /// and +Z at yaw 0, so a north-east corner is 0 and the other three are quarter
            /// turns from it. The outer ring and the inside edge want the same answer, so
            /// they ask the same question here.
            /// </summary>
            void LayKerb(int i, int j, bool west, bool east, bool south, bool north)
            {
                bool corner = !(west && east) && !(south && north) &&
                              ((east && north) || (east && south) ||
                               (west && south) || (west && north));

                string tile;
                float yaw;
                if (corner)
                {
                    tile = KerbCorner;
                    yaw = KerbYaw.Corner(north, east);
                }
                else
                {
                    tile = Kerb;
                    yaw = south ? 180f : north ? 0f : west ? 270f : 90f;
                }

                _laid[At(i, j)] =
                    IndustrialBlocks.Lay(tile, Root, i * Cell, j * Cell, Cell, Cell, yaw) != null;
            }

            /// <summary>Does the block's pavement face the street in this direction? Off the
            /// block, and not over a shared fence.</summary>
            bool Open(int i, int j, int di, int dj)
            {
                int ni = i + di, nj = j + dj;
                if (Held(ni, nj)) return false;
                if (ni >= 0 && nj >= 0 && ni < NX && nj < NZ) return true;    // a bite
                return _sides[(int)Leaving(di, dj)].Rim == IndustrialLayout.Rim.Kerb;
            }

            /// <summary>The floor, one tile to a cell and one tile to a surface. Laid last,
            /// so whatever brought ground of its own - the kerb, a painted bay - keeps it
            /// instead of being paved over twice and left to flicker.</summary>
            public void Floor()
            {
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Held(i, j) || _laid[At(i, j)]) continue;
                        string tile = _floor[At(i, j)] == Surface.Plate ? Plate : Asphalt;
                        // a tile that never stood leaves the cell counted as a hole, which
                        // is what it is: a missing prefab is a block you can see through
                        _laid[At(i, j)] =
                            IndustrialBlocks.Lay(tile, Root, i * Cell, j * Cell, Cell, Cell, 0f) != null;
                    }
            }

            /// <summary>
            /// What stands on the block's OWN pavement, facing the street.
            ///
            /// It belongs to the block and not to the road, which is the core's rule too
            /// (CorePavement bakes a block's lamps into the block). The road reader lays
            /// tarmac and markings and knows nothing about what is beside it; the kerb is
            /// the block's, so the lamp on the kerb is the block's.
            ///
            /// Two things and no more, because this is an industrial estate and not a high
            /// street: a lamp every twenty-five metres on every side that has a street, and
            /// - along the FRONT only - a pole line, which is the one piece of furniture
            /// that says "works" from three hundred metres up. No benches, no newspaper
            /// boxes, no planters, no palms.
            /// </summary>
            public void Streetside(System.Random rng)
            {
                Lampposts(rng);
                Poles();
            }

            /// <summary>A lamp every twenty-five metres, and which cell of the five it
            /// stands in - shared with <see cref="Poles"/>, which must not put a pole in the
            /// same one.</summary>
            const int LampPitch = 5, LampAt = 2;

            void Lampposts(System.Random rng)
            {
                for (int j = 0; j < NZ; j++)
                    for (int i = 0; i < NX; i++)
                    {
                        if (!Kerbed(i, j) || _drive[At(i, j)]) continue;
                        // one per side per pitch, counted along the side it faces, and never
                        // on a corner cell - a lamp on a corner is a lamp in the way of both
                        // streets
                        bool west = Open(i, j, -1, 0), east = Open(i, j, 1, 0);
                        bool south = Open(i, j, 0, -1), north = Open(i, j, 0, 1);
                        int faces = (west ? 1 : 0) + (east ? 1 : 0) + (south ? 1 : 0) + (north ? 1 : 0);
                        if (faces != 1) continue;

                        int along = south || north ? i : j;
                        if (along % LampPitch != LampAt) continue;

                        // turned to face OUT, so the arm reaches over the carriageway
                        float yaw = south ? 180f : north ? 0f : west ? 270f : 90f;
                        float x = i * Cell + Cell * 0.5f, z = j * Cell + Cell * 0.5f;
                        // and stood a metre in from the kerb stone, where a lamp goes
                        x += south || north ? 0f : (west ? 1.2f : -1.2f);
                        z += west || east ? 0f : (south ? 1.2f : -1.2f);
                        Sit(StreetLamp, Root, x, z, yaw, Deck);
                        // the hydrant steps ALONG the kerb, not always east: offset in x on
                        // a south or north side it walked out into the yard, through the
                        // wall, on every west and east side in the quarter
                        if (!Chance(rng, 0.12)) continue;
                        float hx = x + (south || north ? 2.5f : 0f);
                        float hz = z + (west || east ? 2.5f : 0f);
                        Sit(Hydrant, Root, hx, hz, yaw, Deck);
                    }
            }

            /// <summary>
            /// The pole line down the frontage: a pole every span of cable, and the cable
            /// between them.
            ///
            /// The spacing is MEASURED off the cable rather than chosen, because a cable is
            /// a modelled catenary of one length: poles set further apart leave it hanging
            /// in mid air, and set closer they overlap. If the pack's cable turns out to be
            /// nothing like a span - anything under 10 m or over 40 - the poles go up alone
            /// at forty metres, which is a pole line as far as anyone can see from a car.
            /// </summary>
            void Poles()
            {
                if (_sides[(int)IndustrialLayout.Side.South].Rim != IndustrialLayout.Rim.Kerb) return;

                float span = Foot(PowerLine, 0f).x;
                bool cabled = span > 10f && span < 40f;
                float pitch = cabled ? span : 40f;
                float z = Cell * 0.5f;

                var cable = Box(PowerLine);
                float cableWide = Foot(PowerLine, 0f).y;
                // the cable's UNDERSIDE goes a little under the pole's head. Lay corrects x
                // and z by the measured box but passes y through as the pivot, so the
                // correction has to be made here - as the wall, the crown and the bund all
                // make it, and as this one alone did not
                float head = Deck + Box(PowerPole).size.y - cable.size.y - 0.4f;
                for (float x = pitch * 0.5f; x < W; x += pitch)
                {
                    if (Way.y > Way.x && x > Way.x - 2f && x < Way.y + 2f) continue;
                    int i = Mathf.FloorToInt(x / Cell);
                    if (!Kerbed(i, 0) || _drive[At(i, 0)]) continue;
                    if (i % LampPitch == LampAt) continue;    // a lamp already has this cell
                    Sit(PowerPole, Root, x, z, 0f, Deck);
                    if (!cabled || x + pitch >= W) continue;
                    IndustrialBlocks.Lay(PowerLine, Root, x, z - cableWide * 0.5f, span, cableWide,
                                         0f, head - cable.min.y);
                }
            }

            /// <summary>
            /// Buildings standing on more than one surface - the fault the drive and the
            /// gateway kept making, and the one a screenshot shows plainly the moment
            /// somebody looks for it: a shed with its front on concrete and its flank on
            /// tarmac, because half a cell of its ground was floored as road.
            ///
            /// Counted rather than eyeballed, and nought is the only passing answer.
            /// </summary>
            public int Straddles()
            {
                int split = 0;
                foreach (var foot in _footprints)
                {
                    bool two = false;
                    int i0 = Mathf.FloorToInt(foot.xMin / Cell), i1 = Mathf.CeilToInt(foot.xMax / Cell) - 1;
                    int j0 = Mathf.FloorToInt(foot.yMin / Cell), j1 = Mathf.CeilToInt(foot.yMax / Cell) - 1;
                    for (int i = Mathf.Max(0, i0); i <= Mathf.Min(NX - 1, i1) && !two; i++)
                        for (int j = Mathf.Max(0, j0); j <= Mathf.Min(NZ - 1, j1) && !two; j++)
                        {
                            if (!Held(i, j) || Kerbed(i, j)) continue;   // the ring is its own tile
                            if (_floor[At(i, j)] != Surface.Plate ||
                                _drive[At(i, j)] || _corridor[At(i, j)]) two = true;
                        }
                    if (two) split++;
                }
                return split;
            }

            /// <summary>Cells of the block with nothing on the floor at all, which a block
            /// dropped into the city would be seen straight through.</summary>
            public int Gaps()
            {
                int gaps = 0;
                for (int k = 0; k < _held.Length; k++)
                    if (_held[k] && !_laid[k]) gaps++;
                return gaps;
            }

            /// <summary>Books a rectangle of ground outright, for a recipe that stands
            /// something this class did not place - the pack's own filling station, which
            /// arrives as a cluster and must still keep the yard off its forecourt.</summary>
            public void Book(Rect area) => Claim(area);

            /// <summary>Every building this block put up, for the walkers and the map.</summary>
            public IReadOnlyList<Rect> Footprints => _footprints;
        }
    }
}
