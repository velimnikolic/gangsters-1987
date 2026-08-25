using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarborDemo
{
    /// <summary>What a berth is FOR. A port that works nothing but boxes is a box
    /// terminal, and a box terminal is one picture repeated down the length of the
    /// quay; a port has a corner that does something else, and it is the something
    /// else that a player remembers a place by.</summary>
    public enum HarborBerthKind
    {
        /// <summary>The gantry, the live row, the standing blocks behind. What the
        /// port was made of before it had any other kind of berth.</summary>
        Container,
        /// <summary>Grabbed out of a hold onto heaps on the apron: sand, coal, and the
        /// conveyor gallery that carries it back off the quay.</summary>
        Bulk,
        /// <summary>Cars off a ship and into ranks on the concrete, waiting for the
        /// transporters. 1987: the import quay.</summary>
        RoRo,
        /// <summary>The working end of the harbour: small boats along the wall, the
        /// tackle, the ice store, and every gull in the port.</summary>
        Fishing,
    }

    // Which berth does what, and everything that stands behind a berth BECAUSE of what
    // it does. The kinds are rolled off the port's own seed, at most one of each odd
    // kind to a port (the landmark rule: a thing seen twice is furniture), and always
    // at least one box berth - the gantries, the live rows and the standing blocks are
    // the port's spine and something has to carry it.
    //
    // A berth's kind reaches four other places, and nowhere else:
    //   - the standing blocks are not laid behind a berth that has no boxes (Yard);
    //   - only a box berth is given a gantry, a live row and a cargo handler (Life);
    //   - a berth that is not a box berth is given the coaster rather than a freighter
    //     (HarborShipping.AddBerth's `small`);
    //   - and this file dresses the apron behind each one.
    public partial class HarborDistrict
    {
        HarborBerthKind[] _berthKinds;
        /// <summary>The stretch of quay kept clear of standing blocks for the port
        /// office, at the far end past the outermost gantry's travel.</summary>
        Vector2 _officeSpan;
        /// <summary>Where the grab's dust flies, and which berth's work turns it on.</summary>
        readonly List<(int berth, Transform fx)> _bulkQuays = new List<(int, Transform)>();
        /// <summary>Buildings the port wants the MAP to know by name - the office, the
        /// ice store, the bonded shed on offer. Kept as TRANSFORMS and measured in
        /// BlockTheYard, which runs after the port has been carried onto its shore: a
        /// box measured here would be a box in the port's own coordinates, and the card
        /// would sit on empty water.</summary>
        readonly List<(Transform at, string what)> _namedWorks = new List<(Transform, string)>();

        public HarborBerthKind Kind(int berth) =>
            _berthKinds != null && berth >= 0 && berth < _berthKinds.Length ? _berthKinds[berth] : HarborBerthKind.Container;

        /// <summary>Whether this berth works boxes - the one question the yard, the
        /// gantries and the cargo handlers ask.</summary>
        public bool IsBoxBerth(int berth) => Kind(berth) == HarborBerthKind.Container;

        /// <summary>The kinds, and the office's stretch of quay. Rolled after the sheds
        /// (which fix the gates) and before the yard is laid, because the yard reads
        /// both.</summary>
        void PlanBerthKinds()
        {
            _berthKinds = new HarborBerthKind[Mathf.Max(1, berths)];
            for (int i = 0; i < _berthKinds.Length; i++) _berthKinds[i] = HarborBerthKind.Container;

            // how many berths may be something else: never all of them, never more than
            // one of each kind, and a small port keeps its odd berth more often than a
            // big one loses its spine
            var odd = new List<HarborBerthKind>
            {
                HarborBerthKind.Bulk, HarborBerthKind.RoRo, HarborBerthKind.Fishing,
            };
            Shuffle(odd);
            int room = mixedBerths ? Mathf.Clamp(_berthKinds.Length - 1, 0, odd.Count) : 0;
            int taken = 0;
            for (int k = 0; k < room; k++)
            {
                // the first odd berth is near certain, the second even money, the third rare
                double chance = k == 0 ? 0.85 : k == 1 ? 0.5 : 0.2;
                if (_rng.NextDouble() > chance) continue;
                taken++;
            }
            if (taken > 0)
            {
                // the odd berths go on the ENDS of the quay, working inward: a box
                // terminal is worked from the middle, and its awkward neighbours - the
                // heaps, the car park, the fishing wall - are what a port pushes to the
                // ends of its own water
                var ends = new List<int>();
                for (int lo = 0, hi = _berthKinds.Length - 1; lo <= hi; lo++, hi--)
                {
                    if (hi != lo) ends.Add(_rng.Next(2) == 0 ? lo : hi);
                    if (hi != lo) ends.Add(ends[ends.Count - 1] == lo ? hi : lo);
                    else ends.Add(lo);
                }
                for (int k = 0; k < taken && k < ends.Count && k < odd.Count; k++)
                    _berthKinds[ends[k]] = odd[k];
            }

            // the port office: whatever stretch of quay no berth's gear reaches into.
            // A hut inside a gantry's run is a hut a gantry drives through.
            _officeSpan = FindOfficeSpan();

            PlanContraband();

            var said = new List<string>();
            for (int i = 0; i < _berthKinds.Length; i++) said.Add(_berthKinds[i].ToString());
            Debug.Log($"[Harbor] berths: {string.Join(", ", said)}" +
                      (_officeSpan == Vector2.zero ? ", no office" : ", office at x " + _officeSpan.x.ToString("F0")));
        }

        /// <summary>Where the harbourmaster's hut can stand on the quay: the widest
        /// stretch that no berth and no gate corridor wants.
        ///
        /// What is looked for is a GAP: the slack past the outermost berth, or the run
        /// between one berth's reach and the next's. A gantry's reach is its RAIL, which
        /// overhangs its travel by its own legs and a yard either side; a berth with no
        /// gantry is as wide as its own dressing (the ranks of imports, the heaps, the
        /// tackle). Zero when the quay is full, and the port has no office on the water.
        ///
        /// The gate corridors are NOT counted, and deliberately: what a gate corridor
        /// keeps clear is the shed line and the gate road, which begin at the yard road
        /// (z 54) - down here on the apron at the coping the corridor is ordinary
        /// concrete. Counting it swallowed both ends of the quay and left the stock
        /// three-berth port with nowhere at all to put an office.</summary>
        Vector2 FindOfficeSpan()
        {
            float half = QuayHalf;
            var taken = new List<Vector2>();
            for (int i = 0; i < _berthKinds.Length; i++)
            {
                float reach = BerthReach(i);
                taken.Add(new Vector2(BerthX(i) - reach, BerthX(i) + reach));
            }
            taken.Sort((a, b) => a.x.CompareTo(b.x));

            var best = Vector2.zero;
            float cursor = -half + 1f;
            void Offer(float to)
            {
                if (to - cursor > best.y - best.x) best = new Vector2(cursor, to);
            }
            foreach (var span in taken)
            {
                if (span.x > cursor) Offer(span.x);
                cursor = Mathf.Max(cursor, span.y);
            }
            Offer(half - 1f);
            // eight and a half is the yard shed plus a stride; BuildHarbourmaster checks
            // the piece it actually loaded against the span before it seats it
            return best.y - best.x >= 8.5f ? best : Vector2.zero;
        }

        /// <summary>How far either side of its centre a berth's own gear and dressing
        /// reaches along the quay. A gantry berth is measured by its LEGS at the end of
        /// their travel and not by its rail: the rail is a twelve-centimetre strip and a
        /// hut standing over the end of one is nothing, while a hut standing where a
        /// portal frame travels is a hut a gantry drives through.</summary>
        float BerthReach(int berth)
        {
            switch (Kind(berth))
            {
                case HarborBerthKind.Bulk: return berthPitch * 0.44f + 2f;      // the gallery and the flank
                case HarborBerthKind.RoRo: return berthPitch * 0.36f + 2.5f;    // the compound and its cones
                case HarborBerthKind.Fishing: return berthPitch * 0.38f + 2f;   // the boats and the tackle
                default:
                    return quayCranes
                        ? HarborCrane.TravelHalf + HarborCrane.LegHalfX + 0.55f + 1f
                        : berthPitch * 0.30f + 2f;
            }
        }

        /// <summary>Stretches of the quay no standing block may be laid on: the forklift
        /// aisles, the port office, and the whole of a berth that works no boxes.</summary>
        bool YardClosed(float x)
        {
            if (InAisle(x)) return true;
            if (_officeSpan != Vector2.zero && x > _officeSpan.x - 3f && x < _officeSpan.y + 3f) return true;
            for (int i = 0; i < berths; i++)
                if (!IsBoxBerth(i) && Mathf.Abs(x - BerthX(i)) < berthPitch * 0.48f) return true;
            return false;
        }

        // ------------------------------------------------------------ the dressing

        Transform _berthRoot;
        Material _coalMat, _steelMat;

        /// <summary>The band of apron a berth's own dressing may use: north of the men's
        /// way along the quay (LayFootways lays that the WHOLE length of the port, box
        /// berth or not, so a heap or a rank of cars started at the quay lane would bury
        /// it) and short of the yard road's kerb.</summary>
        static float BerthWorkZ0 => QuayWalkZ + WalkWidth * 0.5f + 2.2f;
        static float BerthWorkZ1 => YardRoadZ0 - 4f;

        Material CoalMaterial() => _coalMat ??= HarborKit.Flat("Harbor Coal", new Color(0.14f, 0.14f, 0.15f), 0.06f);
        Material SteelMaterial() => _steelMat ??= HarborKit.Flat("Harbor Works Steel", new Color(0.46f, 0.48f, 0.5f), 0.35f);

        /// <summary>What stands behind each berth because of what the berth is. Run
        /// after the yard and the back lots, so it fills ground the boxes were told to
        /// leave alone.</summary>
        void BuildBerthWorks()
        {
            _berthRoot = Root("Harbor Berths");
            for (int i = 0; i < berths; i++)
            {
                switch (Kind(i))
                {
                    case HarborBerthKind.Bulk: DressBulkBerth(i); break;
                    case HarborBerthKind.RoRo: DressRoRoBerth(i); break;
                    case HarborBerthKind.Fishing: DressFishingBerth(i); break;
                }
            }
        }

        // ------------------------------------------------------------ bulk

        /// <summary>Sand and coal: three heaps on the concrete between the quay lane and
        /// the yard road, a conveyor gallery from the quay over the top of them, drums
        /// and pipe on the flank, and the bulldozed skirt every heap wears.</summary>
        void DressBulkBerth(int berth)
        {
            float xb = BerthX(berth), y = TileTop;
            float z0 = BerthWorkZ0, z1 = BerthWorkZ1 - 2f;
            float mid = (z0 + z1) * 0.5f;
            float room = (z1 - z0) * 0.5f - 1f;          // a heap may not spill onto the walk or the road
            var dirt = HarborKit.LoadAll(HarborKit.DirtPatches, quiet: true);

            // Everything below is laid inside the berth's OWN half-pitch and inside the
            // apron: the odd berths always land on the ENDS of the quay (PlanBerthKinds),
            // so a piece set a berth-and-a-half out from the centre goes over the end of
            // the concrete onto the beach at one hand and into the neighbouring box
            // berth's standing stacks at the other.
            float ownHalf = berthPitch * 0.44f;
            float edge = QuayHalf - 8f;
            // NOT asked about the gate corridors: what a corridor keeps clear is the
            // shed line and the gate road, and both of those begin at the yard road -
            // down here on the apron between the quay lane and the stacks the corridor
            // is ordinary concrete. Asking swallowed the whole belt gallery on any
            // berth that happened to lie near a gate.
            bool Room(float at) => Mathf.Abs(at - xb) <= ownHalf + 0.01f && Mathf.Abs(at) <= edge;

            // the heaps: two of one cargo and one of the other, so the berth reads as
            // working two trades rather than as one tidy pile
            bool coalFirst = _rng.NextDouble() < 0.5;
            float span = berthPitch * 0.27f;
            for (int k = 0; k < 3; k++)
            {
                float hx = xb + (k - 1) * span;
                if (!Room(hx)) continue;
                bool coal = (k == 1) != coalFirst;
                // and no wider than the room between it and its neighbour or the edge
                float r = Mathf.Min(HarborKit.Range(_rng, 8f, 11f), Mathf.Min(room, span - 2f));
                if (r < 4f) continue;
                // steep: a tipped stockpile stands near its angle of repose, and a heap
                // half as high as it is wide reads from the yard road as a puddle
                float h = r * HarborKit.Range(_rng, 0.68f, 0.82f);
                var at = new Vector3(hx, y, mid + HarborKit.Range(_rng, -1.2f, 1.2f));
                Heap($"Heap {(coal ? "Coal" : "Sand")}", at, r, h,
                     coal ? CoalMaterial() : SandMaterial(), _berthRoot);
                // the skirt the loader pushes out round the foot
                if (dirt.Count > 0)
                    for (int s = 0; s < 3; s++)
                    {
                        float a = HarborKit.Range(_rng, 0f, 360f);
                        var off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (r * HarborKit.Range(_rng, 0.95f, 1.25f));
                        Mark(HarborKit.Pick(_rng, dirt), at + off, a, "HeapSkirt", _berthRoot);
                    }
            }

            // the gallery: a belt on legs from the quay, over the heaps, to the yard road,
            // stood outboard of the last heap and still inside the berth
            float gx = xb + ownHalf - 3f;
            if (Room(gx))
            {
                var pillar = HarborKit.TryLoad(HarborKit.GenBase + "SM_Bld_Base_Pillar_01.prefab");
                if (pillar != null)
                {
                    var foot = new Vector3(gx, y + 2.2f, QuayLaneZ + 1.5f);
                    var head = new Vector3(gx, y + 11f, z1 + 3f);
                    foreach (float side in new[] { -1.1f, 1.1f })
                        Paint(HarborKit.Span(pillar, foot + Vector3.right * side, head + Vector3.right * side,
                                             0.55f, _berthRoot, "BeltGirder"), SteelMaterial());
                    // the ties between the girders, and the legs down to the concrete
                    for (float t = 0.08f; t < 1f; t += 0.16f)
                    {
                        var on = Vector3.Lerp(foot, head, t);
                        Paint(HarborKit.Span(pillar, on + Vector3.left * 1.4f, on + Vector3.right * 1.4f,
                                             0.28f, _berthRoot, "BeltTie"), SteelMaterial());
                        if (t > 0.2f && t < 0.9f)
                            Paint(HarborKit.Span(pillar, new Vector3(on.x, y, on.z), on, 0.45f, _berthRoot, "BeltLeg"),
                                  SteelMaterial());
                    }
                    // the transfer tower at the top and the chute over the quay
                    Paint(HarborKit.Span(pillar, new Vector3(head.x, y, head.z), head + Vector3.up * 2f,
                                         1.6f, _berthRoot, "TransferTower"), SteelMaterial());
                }
            }

            // the flank: drums, pipe, a barrow and the cones that keep lorries off the sand
            var drum = HarborKit.TryLoad(HarborKit.BarrelMetal);
            var pipes = HarborKit.TryLoad(HarborKit.PipeStack);
            var barrow = HarborKit.TryLoad(HarborKit.Wheelbarrow);
            var cone = HarborKit.TryLoad(HarborKit.Cone);
            float fx = xb - ownHalf + 1f;
            if (Room(fx) && Room(fx + 4f))
            {
                if (drum != null)
                    for (int r = 0; r < 2; r++)
                        for (int c = 0; c < 4; c++)
                            HarborKit.Sit(drum, new Vector3(fx + c * 1.1f, y, z0 + 1f + r * 1.1f),
                                          HarborKit.Range(_rng, 0f, 360f), _berthRoot, "Drum");
                if (pipes != null) HarborKit.Sit(pipes, new Vector3(fx + 1.5f, y, z0 + 6f), 90f, _berthRoot, "Pipes");
                if (barrow != null) HarborKit.Sit(barrow, new Vector3(fx - 2f, y, z0 + 3f), 40f, _berthRoot, "Barrow");
            }
            if (cone != null)
                for (float cx = xb - ownHalf; cx <= xb + ownHalf; cx += 7f)
                {
                    if (!Room(cx)) continue;
                    HarborKit.Sit(cone, new Vector3(cx, y, BerthWorkZ0 - 1.5f), 0f, _berthRoot, "Cone");
                }

            // where the grab spills its dust while she is worked (switched by HarborNight's
            // sister, the port's routine - see TickBerthWorks)
            var dust = new GameObject("BulkDust").transform;
            dust.SetParent(_liveRoot, false);
            dust.localPosition = new Vector3(xb, y + 1.5f, QuayLaneZ - 2f);
            var fxDust = HarborKit.TryLoad(HarborKit.FxDust);
            if (fxDust != null)
            {
                var go = Instantiate(fxDust, dust);
                go.transform.localPosition = Vector3.zero;
                go.transform.localScale = Vector3.one * 3f;
            }
            dust.gameObject.SetActive(false);
            _bulkQuays.Add((berth, dust));
        }

        /// <summary>A heap of loose cargo: a ring at the foot, a shoulder, and a rounded
        /// top - not a cone, which is what a tipped load looks like for about an hour
        /// before a loader has been over it. The radius wanders a little round the ring
        /// so no two heaps are the same lump.</summary>
        GameObject Heap(string name, Vector3 at, float radius, float height, Material mat, Transform parent)
        {
            const int Seg = 20;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            // three rings and an apex; each ring its own wobble
            float[] rings = { 1.04f, 0.72f, 0.34f };
            float[] highs = { 0f, 0.45f, 0.80f };
            var wob = new float[Seg];
            for (int s = 0; s < Seg; s++)
                wob[s] = 1f + (Mathf.PerlinNoise(at.x * 0.05f + s * 0.31f, at.z * 0.05f) - 0.5f) * 0.22f;
            for (int r = 0; r < rings.Length; r++)
                for (int s = 0; s < Seg; s++)
                {
                    float a = s / (float)Seg * Mathf.PI * 2f;
                    float rr = radius * rings[r] * wob[s];
                    verts.Add(new Vector3(Mathf.Cos(a) * rr, height * highs[r], Mathf.Sin(a) * rr));
                }
            int apex = verts.Count;
            verts.Add(new Vector3(0f, height, 0f));
            for (int r = 0; r + 1 < rings.Length; r++)
                for (int s = 0; s < Seg; s++)
                {
                    int a0 = r * Seg + s, a1 = r * Seg + (s + 1) % Seg;
                    int b0 = a0 + Seg, b1 = a1 + Seg;
                    tris.Add(a0); tris.Add(b0); tris.Add(a1);
                    tris.Add(a1); tris.Add(b0); tris.Add(b1);
                }
            int top = (rings.Length - 1) * Seg;
            for (int s = 0; s < Seg; s++)
            {
                tris.Add(top + s); tris.Add(apex); tris.Add(top + (s + 1) % Seg);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            go.isStatic = true;             // one mesh already: the merge leaves it alone
            return go;
        }

        // ------------------------------------------------------------ roll-on

        /// <summary>The import quay: ranks of cars on the concrete between the quay lane
        /// and the yard road, nose to tail with a walking lane between ranks, the rows
        /// marked out with the palm city's parking dividers, and the whole compound
        /// coned off from the yard road. 1987 - so whatever the scan turns up, none of
        /// it is a squad car and none of it is barred (HarborKit.ScanCars).</summary>
        void DressRoRoBerth(int berth)
        {
            var cars = HarborKit.ScanCars();
            if (cars.Count == 0)
            {
                Debug.LogWarning("[HarborDemo] no car bodies for the roll-on berth - it stands empty.");
                return;
            }
            float xb = BerthX(berth), y = TileTop;
            float z0 = BerthWorkZ0, z1 = BerthWorkZ1;
            const float RankPitch = 7.2f;      // a car's length and room to open a door
            const float BayPitch = 2.5f;       // and the width of a bay
            // how many bodies the compound may hold. A rank of imports is the point of
            // the berth, but the ground behind it would take a hundred and fifty cars,
            // and a hundred and fifty car meshes is a bigger bill than the whole of the
            // rest of the port put together
            const int Budget = 54;

            var divider = HarborKit.TryLoad(HarborKit.ParkingDivider);
            var cone = HarborKit.TryLoad(HarborKit.Cone);
            int ranks = Mathf.Clamp(Mathf.FloorToInt((z1 - z0) / RankPitch), 1, 3);
            float x0 = xb - berthPitch * 0.36f, x1 = xb + berthPitch * 0.36f;
            int bays = Mathf.Max(1, Mathf.FloorToInt((x1 - x0) / BayPitch));
            // the ship is still discharging: the far rank is filling and is half empty
            int filling = _rng.Next(ranks);
            int parked = 0;

            for (int r = 0; r < ranks; r++)
            {
                float z = z0 + (r + 0.5f) * RankPitch;
                // every second rank faces the other way, the way a marshalling yard is run
                float yaw = (r & 1) == 0 ? 0f : 180f;
                for (int b = 0; b < bays; b++)
                {
                    float x = x0 + (b + 0.5f) * BayPitch;
                    if (divider != null && (b % 2) == 0)
                        HarborKit.Sit(divider, new Vector3(x - BayPitch * 0.5f, y, z), 90f, _berthRoot, "Divider");
                    if (r == filling && _rng.NextDouble() < 0.55) continue;      // the rank being filled
                    if (_rng.NextDouble() < 0.08) continue;                      // and the odd gap elsewhere
                    if (++parked > Budget) continue;
                    var go = HarborKit.Prop(HarborKit.Pick(_rng, cars), Vector3.zero,
                                            yaw + HarborKit.Range(_rng, -1.5f, 1.5f), _berthRoot, "Import");
                    HarborKit.StripBehaviours(go, keepAnimator: false);
                    var bb = HarborKit.BoundsOf(go);
                    var p = go.transform.position;
                    go.transform.position = new Vector3(p.x + (x - bb.center.x), y - bb.min.y + p.y, p.z + (z - bb.center.z));
                }
            }
            // the compound's mouth on the yard road, coned down to one way in
            if (cone != null)
                for (float cx = x0; cx <= x1; cx += 5f)
                {
                    if (Mathf.Abs(cx - AisleX(berth)) < 4f) continue;    // the way in
                    HarborKit.Sit(cone, new Vector3(cx, y, z1 + 1.2f), 0f, _berthRoot, "Cone");
                }
        }

        // ------------------------------------------------------------ fishing

        /// <summary>The working end: small boats lying along the wall inside the berth,
        /// the tackle on the coping - crates of fish, kegs, buckets, coils of rope - an
        /// ice store at the back of the apron, and a board with a crab on it. No gantry,
        /// no stacks, and every gull in the port (BuildWaterline reads the kind).</summary>
        void DressFishingBerth(int berth)
        {
            float xb = BerthX(berth), y = TileTop;
            var boats = HarborKit.LoadAll(HarborKit.SmallBoats, quiet: true);
            var crate = HarborKit.TryLoad(HarborKit.Crate1);
            var crate2 = HarborKit.TryLoad(HarborKit.Crate2);
            var keg = HarborKit.TryLoad(HarborKit.Keg);
            var bucket = HarborKit.TryLoad(HarborKit.Bucket);
            var tackle = HarborKit.TryLoad(HarborKit.TackleBox);
            var rod = HarborKit.TryLoad(HarborKit.FishingRod);
            var rope = HarborKit.TryLoad(HarborKit.RopeKnot) ?? HarborKit.TryLoad(HarborKit.Rope1);
            var cooler = HarborKit.TryLoad(HarborKit.DrinksCooler);
            var sign = HarborKit.TryLoad(HarborKit.CrabSign);
            var pallet = HarborKit.TryLoad(HarborKit.Pallet);

            // the boats: alongside the wall, bows all one way, a fender's width off it -
            // and CLEAR OF THE SHIP'S BOX either side. A berth that works no boxes is
            // given the coaster (HarborShipping.AddBerth's `small`), she lies at
            // -(QuayFace + 1.5 + Beam/2) with her length along the quay, and boats moored
            // in the middle of the berth are boats moored inside her hull.
            if (boats.Count > 0)
            {
                float bz = -(QuayFace + 2.6f);
                float clear = HarborShipSpec.Coaster.Length * 0.5f + 5f;
                for (int k = 0; k < 4; k++)
                {
                    // two off each end of where she lies, nose to tail
                    float side = k < 2 ? -1f : 1f;
                    float bx = xb + side * (clear + (k % 2) * 11f);
                    if (Mathf.Abs(bx) > QuayHalf - 6f) continue;
                    var prefab = HarborKit.Pick(_rng, boats);
                    var go = HarborKit.Prop(prefab, Vector3.zero, 90f + HarborKit.Range(_rng, -3f, 3f), _berthRoot, "Boat");
                    HarborKit.StripBehaviours(go, keepAnimator: false);
                    var bb = HarborKit.BoundsOf(go);
                    var p = go.transform.position;
                    // her waterline on the water, not her keel: a boat sits IN the sea
                    go.transform.position = new Vector3(p.x + (bx - bb.center.x),
                                                        p.y + (WaterY - 0.15f - (bb.min.y + bb.size.y * 0.32f)),
                                                        p.z + (bz - bb.center.z));
                }
            }

            // the tackle on the coping, in the strip between the wall and the men's way
            for (int k = 0; k < 9; k++)
            {
                float tx = xb - 30f + k * 7.5f + HarborKit.Range(_rng, -1.2f, 1.2f);
                if (Mathf.Abs(tx) > QuayHalf - 4f) continue;
                float tz = HarborKit.Range(_rng, 2.6f, 5.4f);
                switch (k % 5)
                {
                    case 0:
                        if (pallet != null)
                        {
                            var pb = HarborKit.PrefabBounds(pallet);
                            HarborKit.Sit(pallet, new Vector3(tx, y, tz), HarborKit.Range(_rng, -12f, 12f), _berthRoot, "Pallet");
                            for (int c = 0; c < 3 && crate2 != null; c++)
                                HarborKit.Sit(crate2, new Vector3(tx + HarborKit.Range(_rng, -0.4f, 0.4f), y + pb.size.y + c * 0.88f, tz),
                                              HarborKit.Range(_rng, -8f, 8f), _berthRoot, "FishCrate");
                        }
                        break;
                    case 1:
                        if (keg != null)
                            for (int c = 0; c < 3; c++)
                                HarborKit.Sit(keg, new Vector3(tx + c * 0.8f, y, tz + (c & 1) * 0.7f),
                                              HarborKit.Range(_rng, 0f, 360f), _berthRoot, "Keg");
                        break;
                    case 2:
                        if (rope != null)
                            for (int c = 0; c < 2; c++)
                                HarborKit.Sit(rope, new Vector3(tx + c * 1.1f, y, tz), HarborKit.Range(_rng, 0f, 360f), _berthRoot, "RopeCoil");
                        if (bucket != null) HarborKit.Sit(bucket, new Vector3(tx - 1f, y, tz + 0.8f), 20f, _berthRoot, "Bucket");
                        break;
                    case 3:
                        if (crate != null)
                            for (int c = 0; c < 4; c++)
                                HarborKit.Sit(crate, new Vector3(tx + (c % 2) * 1.2f, y + (c / 2) * 0.95f, tz + (c % 2) * 0.3f),
                                              HarborKit.Range(_rng, -15f, 15f), _berthRoot, "FishCrate");
                        break;
                    default:
                        if (tackle != null) HarborKit.Sit(tackle, new Vector3(tx, y, tz), HarborKit.Range(_rng, 0f, 360f), _berthRoot, "TackleBox");
                        if (rod != null) HarborKit.Sit(rod, new Vector3(tx + 0.9f, y, tz + 0.4f), HarborKit.Range(_rng, 0f, 360f), _berthRoot, "Rod");
                        break;
                }
            }
            // clear of the pallet pile the yard dressing stands at xb + 8
            if (cooler != null) HarborKit.Sit(cooler, new Vector3(xb + 12f, y, QuayLaneZ + 4.2f), 15f, _berthRoot, "Cooler");

            // the ice store: a hut at the back of the apron with its own board
            var shed = HarborKit.TryLoad(HarborKit.YardShed);
            if (shed != null)
            {
                var sb = HarborKit.PrefabBounds(shed);
                float sx = xb - sb.size.x * 0.5f;
                if (!InGateLane(sx, sb.size.x))
                {
                    float front = YardRoadZ0 - 3f - sb.size.z;
                    var go = Instantiate(shed, Vector3.zero, Quaternion.Euler(0f, 180f, 0f), _berthRoot);
                    go.name = "Ice Store";
                    HarborKit.StripBehaviours(go, keepAnimator: false);
                    var b = HarborKit.BoundsOf(go);
                    var p = go.transform.position;
                    go.transform.position = new Vector3(p.x + (sx - b.min.x), TileTop + ShedLift, p.z + (front - b.min.z));
                    b = HarborKit.BoundsOf(go);
                    if (sign != null) HarborKit.Sit(sign, new Vector3(b.center.x + 4f, y, b.min.z - 1.2f), 0f, _berthRoot, "CrabBoard");
                    _namedWorks.Add((go.transform, "Ice Store"));
                }
            }
        }

        // ------------------------------------------------------------ helpers

        /// <summary>Every renderer of an instance onto one material - what turns a
        /// stretched Base-kit pillar into a girder rather than a lump of concrete, the
        /// way HarborCrane paints its own members.</summary>
        static void Paint(GameObject go, Material mat)
        {
            if (go == null || mat == null) return;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) mr.sharedMaterial = mat;
        }

        /// <summary>A flat piece laid centred on a point of a named root, its underside on
        /// the paint plane - <see cref="Mark(GameObject,Vector3,float,string)"/> for the
        /// files that keep their own root.</summary>
        GameObject Mark(GameObject prefab, Vector3 at, float yaw, string name, Transform parent)
        {
            if (prefab == null) return null;
            var b = HarborKit.PrefabBounds(prefab);
            var offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(b.center.x, 0f, b.center.z);
            return HarborKit.Prop(prefab, new Vector3(at.x - offset.x, TileTop + PaintY - b.min.y, at.z - offset.z),
                                  yaw, parent, name);
        }

    }
}
