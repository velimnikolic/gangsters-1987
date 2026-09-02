using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace HarborDemo
{
    // Behind the quay: the live row under the cranes that the ships' cargo comes
    // down into and goes back up out of, the standing blocks behind it - five rows
    // deep, stacked two to four high, broken by the forklift aisles and a cross lane
    // every few bays, the way a box terminal is actually laid - then the yard road,
    // the lorries' apron, the sheds with their doors to the water and loading docks
    // at their backs, the back lots between the sheds (parked lorries, drum stores,
    // an empties depot), the service road, the wire with a gate at either end, and
    // the street beyond.
    public partial class HarborDistrict
    {
        // the yard's z bands, south to north
        public const float QuayLaneZ = 8f;          // where the forklifts run (5..11)
        public const float LiveRowZ = 18f;          // the live stacks, between the cranes' rails (5.6 / 22)
        /// <summary>The lane the devanning gang works in: the strip between the back of
        /// the live row and the gantry's landward rail. A box is opened here and what
        /// comes out of it is shuttled to the sheds (HarborDevanning).</summary>
        public const float DevanZ = 20.6f;
        /// <summary>The standing blocks: their south face clear of the cranes' land rail
        /// and the legs that run on it, five rows of boxes lying along the quay, a
        /// box and a half a row.</summary>
        public const float BlockZ0 = 25f;
        public const int BlockRows = 5;
        public const float RowPitch = 3.5f;
        public static float BlockZ1 => BlockZ0 + BlockRows * RowPitch;      // 42.5
        /// <summary>The yard road: a city street's carriageway, two lanes, ten metres - the
        /// loop's south side - with the sheds hard up against its far kerb.</summary>
        public const float YardRoadZ0 = 44f, YardRoadZ1 = 54f;
        /// <summary>The shed line: a forecourt's width off the yard road's kerb, the
        /// doors opening onto the street. A lorry being worked stands at the kerb
        /// alongside, the way a delivery stands in a city street, and the goods go
        /// across the forecourt; the sheds jog off the line by a metre or two, no
        /// more - a row hard up against a road is a row, a row wandering off it is
        /// a field with huts in it.</summary>
        public const float ShedFrontZ = YardRoadZ1 + 3f;
        /// <summary>The shoulder: where a lorry stands at a door, on the road's north
        /// lane against the kerb, the traffic passing on the south lane.</summary>
        public const float ShoulderZ = YardRoadZ1 - 1.8f;
        public const float BoxPitch = 7f;
        /// <summary>Bays to a standing block before a cross lane is left through it.</summary>
        const int BlockBays = 8;
        const float CrossLane = 5f;

        /// <summary>The middle of the yard road - the spine everything inside drives on.</summary>
        public static float YardLaneZ => (YardRoadZ0 + YardRoadZ1) * 0.5f;
        /// <summary>The middle of the service road, the loop's other long side.</summary>
        float ServiceLaneZ => (_serviceRoadZ0 + _serviceRoadZ1) * 0.5f;

        float _shedBackZ, _serviceRoadZ0, _serviceRoadZ1, _fenceZ, _streetZ;
        float _gateEastX, _gateWestX;
        readonly List<Vector3> _shedDoors = new List<Vector3>();       // where a door opens on the apron
        readonly List<Vector3> _loadingDocks = new List<Vector3>();    // where a truck backs up
        readonly List<GameObject> _boxPrefabs = new List<GameObject>();
        readonly Dictionary<int, List<Vector3>> _liveSlots = new Dictionary<int, List<Vector3>>();
        /// <summary>The x-stretches of the shed line nothing was built on - the back
        /// lots, filled after the sheds and the gates have had their say.</summary>
        readonly List<Vector2> _backLots = new List<Vector2>();

        // ------------------------------------------------------------ sheds

        /// <summary>Two centimetres of daylight between a shed's own floor slab and the
        /// apron concrete under it, so the two planes do not fight for the pixel a door
        /// stands open on.</summary>
        const float ShedLift = 0.02f;

        /// <summary>How far behind the shed line a shed may be set. A port is not built
        /// in one season, and a row of sheds all touching the same line, evenly spaced,
        /// is the one thing that reads as laid out by a machine rather than by a
        /// harbour board over forty years.</summary>
        const float ShedJog = 2.5f;

        /// <summary>The gate: the clear width kept through the shed line so the gate
        /// road runs from the street to the yard road with no wall across it, half the
        /// road's own asphalt, and half the opening left in the wire.</summary>
        public const float GateLaneHalf = 14f, GateRoadHalf = 5f, GateHalf = 5.5f;

        /// <summary>Whether x falls in a gate's corridor - nothing is built there.</summary>
        bool InGateLane(float x, float slack = 0f) =>
            Mathf.Abs(x - _gateWestX) < GateLaneHalf + slack || Mathf.Abs(x - _gateEastX) < GateLaneHalf + slack;

        /// <summary>The kit's industrial sheds set along the shed line - the skylit
        /// warehouses, the depot garage - west to east in blocks of two or three with a
        /// lot's width between blocks, each set back off the line by its own few metres,
        /// one now and then turned broadside so its doors open on the alley. Two
        /// corridors are kept clear for the gate roads, and the little sheds are not in
        /// the row at all: they go into the back lots as stores and beside the gates as
        /// gatehouses. Then where the backs end the service road, the fence and the
        /// street are fixed, and the apron is stretched to reach the fence.
        ///
        /// The bake (SyntyWarehouseKit) leaves every one of them fronting +Z with its
        /// ground at y = 0, so no facade needs measuring here: half a turn faces the
        /// doors south to the berths, a quarter turn faces them along the line.</summary>
        void BuildWarehouses()
        {
            float half = QuayHalf;
            // hung off the city, the gates stand under the road lines the port is
            // pinned to; on its own, twenty metres in from either end of the quay
            if (_links != null && _links.Length > 1)
            {
                _gateWestX = -GateSpanCentre;
                _gateEastX = GateSpanCentre;
            }
            else
            {
                _gateWestX = -half + 20f;
                _gateEastX = half - 20f;
            }

            var big = HarborKit.LoadAll(HarborKit.LineSheds, quiet: true);
            var main = HarborKit.TryLoad(HarborKit.MainShed);
            var loadingDock = HarborKit.TryLoad(HarborKit.LoadingDock);
            float backMax = ShedFrontZ + 18f;
            if (big.Count == 0 && main == null)
                Debug.LogWarning("[HarborDemo] no industrial sheds found under Assets/CityKit/Buildings - " +
                                 "run Tools/City/Catalog/Rebuild Synty Warehouse Kit (Buildings).");

            var bag = new List<GameObject>();
            var taken = new List<Vector2>();        // what stands on the line: sheds and the gate corridors
            taken.Add(new Vector2(_gateWestX - GateLaneHalf, _gateWestX + GateLaneHalf));
            taken.Add(new Vector2(_gateEastX - GateLaneHalf, _gateEastX + GateLaneHalf));
            var blocked = new List<Vector2>(taken);   // what the line's cursor must step over

            // The authority building takes the centre of the port itself. If its catalogue
            // pieces are absent, the old large warehouse remains a safe fallback.
            if (!TryBuildHeadquarters(ShedFrontZ + 8f, ref backMax, taken, blocked) && main != null)
            {
                var mb = HarborKit.PrefabBounds(main);
                var go = Instantiate(main, Vector3.zero, Quaternion.Euler(0f, 180f, 0f), _warehouseRoot);
                go.name = main.name;
                HarborKit.StripBehaviours(go, keepAnimator: false);
                var b = HarborKit.BoundsOf(go);
                var p = go.transform.position;
                go.transform.position = new Vector3(p.x + (0f - b.center.x), TileTop + ShedLift, p.z + (ShedFrontZ - b.min.z));
                b = HarborKit.BoundsOf(go);
                backMax = Mathf.Max(backMax, b.max.z);
                _shedDoors.Add(new Vector3(b.center.x, TileTop, ShedFrontZ - 1.5f));
                if (loadingDock != null) BackDocks(loadingDock, b);
                var span = new Vector2(b.min.x - 8f, b.max.x + 8f);   // a lorry's width of alley either side
                taken.Add(new Vector2(b.min.x, b.max.x));
                blocked.Add(span);
            }

            float x = -half + 6f, blockBase = 0f;
            int inBlock = 0, blockSize = _rng.Next(2, 4), placed = 0, guard = 0;

            while (big.Count > 0 && x < half - 22f && guard++ < 64)
            {
                if (bag.Count == 0) { bag.AddRange(big); Shuffle(bag); }
                var prefab = bag[bag.Count - 1];
                bag.RemoveAt(bag.Count - 1);
                var pb = HarborKit.PrefabBounds(prefab);
                if (pb.size.sqrMagnitude < 1f) continue;
                // a shed shallow enough to be turned broadside stands so now and then,
                // its doors on the alley - a transit shed at right angles to the row
                bool cross = pb.size.z < 20f && pb.size.x < 30f && _rng.NextDouble() < 0.25;
                float yaw = cross ? (_rng.NextDouble() < 0.5 ? 90f : -90f) : 180f;
                float width = cross ? pb.size.z : pb.size.x;

                // the gate corridors and the main building are not built in: a shed that
                // would stand in one waits until the cursor is past it
                foreach (var span in blocked)
                    if (x + width > span.x && x < span.y)
                    {
                        x = span.y;
                        inBlock = 0;
                    }
                if (x + width > half - 6f) break;

                if (inBlock == 0) blockBase = HarborKit.Range(_rng, 0f, ShedJog);
                float front = ShedFrontZ + Mathf.Clamp(blockBase + HarborKit.Range(_rng, -2f, 2f), 0f, ShedJog);

                var go = Instantiate(prefab, Vector3.zero, Quaternion.Euler(0f, yaw, 0f), _warehouseRoot);
                go.name = prefab.name;
                HarborKit.StripBehaviours(go, keepAnimator: false);
                var b = HarborKit.BoundsOf(go);
                if (b.size.sqrMagnitude < 1f) { Destroy(go); continue; }
                var p = go.transform.position;
                go.transform.position = new Vector3(p.x + (x - b.min.x), TileTop + ShedLift, p.z + (front - b.min.z));
                b = HarborKit.BoundsOf(go);
                backMax = Mathf.Max(backMax, b.max.z);
                placed++;
                taken.Add(new Vector2(b.min.x, b.max.x));
                // only a shed facing the water has a door on the apron; a broadside one
                // opens on the alley and is left out of the lorries' and forklifts' rounds
                if (!cross) _shedDoors.Add(new Vector3(b.center.x, TileTop, front - 1.5f));
                if (!cross && loadingDock != null) BackDocks(loadingDock, b);

                // within a block the sheds stand a lorry's width apart; between blocks
                // a lot is left - wide enough to park on or store in, not so wide that
                // the row falls apart into huts on a plain
                float gap;
                if (++inBlock >= blockSize) { inBlock = 0; blockSize = _rng.Next(2, 4); gap = HarborKit.Range(_rng, 16f, 24f); }
                else gap = HarborKit.Range(_rng, 6f, 11f);
                x = b.max.x + gap;
            }
            if (placed == 0 && big.Count > 0)
                Debug.LogWarning("[HarborDemo] the shed line came out empty - the quay may be too short for the stock.");

            _shedBackZ = backMax;
            // the docks stand three metres proud of the backs: the road clears them
            _serviceRoadZ0 = backMax + 4f;
            _serviceRoadZ1 = _serviceRoadZ0 + 10f;   // a city carriageway too: the loop's north side
            _fenceZ = _serviceRoadZ1 + 3.5f;
            _streetZ = _fenceZ + 20f;   // the wire, a verge, then the road's outer kerb
            apronDepth = Mathf.Max(apronDepth, _fenceZ);

            // the back lots: whatever of the line is left between what was built
            taken.Sort((a, c) => a.x.CompareTo(c.x));
            float cursor = -half + 4f;
            foreach (var span in taken)
            {
                if (span.x - cursor >= 8f) _backLots.Add(new Vector2(cursor, span.x));
                cursor = Mathf.Max(cursor, span.y);
            }
            if (half - 4f - cursor >= 8f) _backLots.Add(new Vector2(cursor, half - 4f));
        }

        /// <summary>Where a lorry stands to be worked: at the kerb in front of a door,
        /// nose along the road, a few strides east of the doorway so the men and the
        /// forklift have the door and the forecourt west of her.</summary>
        Vector3 BayAt(Vector3 door) => new Vector3(door.x + 3f, TileTop, ShoulderZ);

        /// <summary>The bays free for a lorry to drive into. The rule the yard is shared
        /// by: the detail pass parks two lorries that never move on the first two odd
        /// doors, and everything else - the even doors, and any odd door past those two
        /// - is a working bay off the approach road.</summary>
        IEnumerable<Vector3> FreeBays()
        {
            for (int i = 0; i < _shedDoors.Count; i++)
                if ((i & 1) == 0 || i >= 5) yield return BayAt(_shedDoors[i]);
        }

        /// <summary>The loading docks at a shed's back, one or two by its width, their
        /// lips to the service road.</summary>
        void BackDocks(GameObject loadingDock, Bounds b)
        {
            var db = HarborKit.PrefabBounds(loadingDock);
            int docks = b.size.x > 26f ? 2 : 1;
            for (int d = 0; d < docks; d++)
            {
                float dx = b.center.x + (docks == 1 ? 0f : (d - 0.5f) * 12f);
                // the platform runs 6 m along its own -X off the pivot; yaw 180 puts
                // its open side north, its length back along +X from the pivot
                var pos = new Vector3(dx - db.size.x * 0.5f, TileTop, b.max.z + 0.1f);
                HarborKit.Prop(loadingDock, pos, 180f, _warehouseRoot, "LoadingDock");
                _loadingDocks.Add(new Vector3(dx, TileTop, b.max.z + 3.3f + 4.5f));
            }
        }

        /// <summary>One building set down with its west edge at x, its south edge at
        /// front, its floor on the tiles.</summary>
        void Seat(GameObject prefab, float x, float front, float yaw)
        {
            var go = Instantiate(prefab, Vector3.zero, Quaternion.Euler(0f, yaw, 0f), _warehouseRoot);
            go.name = prefab.name;
            HarborKit.StripBehaviours(go, keepAnimator: false);
            var b = HarborKit.BoundsOf(go);
            var p = go.transform.position;
            go.transform.position = new Vector3(p.x + (x - b.min.x), TileTop + ShedLift, p.z + (front - b.min.z));
        }

        void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ------------------------------------------------------------ the back lots

        /// <summary>What stands on the shed line where no shed does. Each lot is given
        /// one use by its width: the small yard shed as a store against the back;
        /// lorries backed in along the back in a row, the drivers' end of the yard; a
        /// depot of empties - boxes on the ground, one and two high, waiting for a
        /// ship; a drum and spool store with a pipe stack. The narrowest lots are left
        /// as the alleys they are. Nothing here is on the apron in front of the doors,
        /// where the lorries swing, nor within the gate corridors.</summary>
        void BuildBackLots()
        {
            var small = HarborKit.LoadAll(HarborKit.SmallSheds, quiet: true);
            var trucks = HarborKit.LoadAll(new[] { HarborKit.Truck, HarborKit.TownTruck, HarborKit.TownDelivery }, quiet: true);
            var drum = HarborKit.TryLoad(HarborKit.BarrelMetal);
            var drumP = HarborKit.TryLoad(HarborKit.BarrelPlastic);
            var spool = HarborKit.TryLoad(HarborKit.WireSpool);
            var pipes = HarborKit.TryLoad(HarborKit.PipeStack);
            var pallet = HarborKit.TryLoad(HarborKit.Pallet);
            var crate = HarborKit.TryLoad(HarborKit.Crate1);
            float y = TileTop;
            float lotZ0 = ShedFrontZ + 2f, lotZ1 = _shedBackZ - 1.5f;
            int use = _rng.Next(4);

            // the widest lot in the row is the tank farm's - once to a port, and the
            // widest because a bund wants room round it (HarborDistrict.Works.cs); and
            // one other is kept back empty for the box nobody talks about
            // (HarborDistrict.Contraband.cs)
            int farm = -1, bonded = -1;
            for (int i = 0; i < _backLots.Count; i++)
            {
                if (_backLots[i].y - _backLots[i].x < 18f) continue;
                if (InGateLane((_backLots[i].x + _backLots[i].y) * 0.5f, 6f)) continue;
                if (farm < 0 || _backLots[i].y - _backLots[i].x > _backLots[farm].y - _backLots[farm].x) farm = i;
            }
            if (contraband)
                for (int i = 0; i < _backLots.Count; i++)
                {
                    if (i == farm || _backLots[i].y - _backLots[i].x < 13f) continue;
                    if (InGateLane((_backLots[i].x + _backLots[i].y) * 0.5f, 6f)) continue;
                    bonded = i;
                    break;
                }

            for (int lotIndex = 0; lotIndex < _backLots.Count; lotIndex++)
            {
                var lot = _backLots[lotIndex];
                float w = lot.y - lot.x;
                if (w < 12f) continue;
                float x0 = lot.x + 2.5f, x1 = lot.y - 2.5f;
                if (lotIndex == farm)
                {
                    RaiseTankFarm(x0, x1, lotZ0, lotZ1);
                    continue;
                }
                if (lotIndex == bonded)
                {
                    _contrabandLot = Rect.MinMaxRect(x0, lotZ0, x1, lotZ1);
                    continue;               // left clear: the contraband pass fills it
                }
                // the uses go round in turn from a random start, so two neighbouring
                // lots are never the same thing
                use = (use + 1) & 3;
                switch (use)
                {
                    case 0: // the store shed, against the back, with its clutter beside it
                    {
                        if (small.Count == 0) goto case 1;
                        var prefab = HarborKit.Pick(_rng, small);
                        var sb = HarborKit.PrefabBounds(prefab);
                        bool broadside = sb.size.x > sb.size.z && w < sb.size.x + 8f;
                        float sw = broadside ? sb.size.z : sb.size.x;
                        if (sw + 4f > w) goto case 3;
                        float sx = HarborKit.Range(_rng, x0, x1 - sw);
                        float front = lotZ1 - (broadside ? sb.size.x : sb.size.z) - HarborKit.Range(_rng, 0f, 5f);
                        Seat(prefab, sx, front, broadside ? 90f : 180f);
                        if (pallet != null)
                            for (int k = 0; k < 3; k++)
                                HarborKit.Sit(pallet, new Vector3(sx + sw + 1.5f + (k % 2) * 1.4f, y, front + 1f + k * 1.5f),
                                              HarborKit.Range(_rng, -10f, 10f), _yardRoot, "Pallet");
                        break;
                    }
                    case 1: // the lorry park: backed in against the back, a lorry's width and a half apart
                    {
                        if (trucks.Count == 0) goto case 2;
                        float pitch = 4.6f;
                        int n = Mathf.Clamp(Mathf.FloorToInt((x1 - x0) / pitch), 1, 4);
                        float startX = (x0 + x1) * 0.5f - (n - 1) * pitch * 0.5f;
                        for (int k = 0; k < n; k++)
                        {
                            if (_rng.NextDouble() < 0.25) continue;       // an empty bay or two
                            var prefab = HarborKit.Pick(_rng, trucks);
                            var go = HarborKit.Prop(prefab, Vector3.zero, 180f + HarborKit.Range(_rng, -3f, 3f), _yardRoot, "ParkedLorry");
                            HarborKit.StripBehaviours(go, keepAnimator: false);
                            var b = HarborKit.BoundsOf(go);
                            var p = go.transform.position;
                            go.transform.position = new Vector3(p.x + (startX + k * pitch - b.center.x), y - b.min.y + p.y,
                                                                p.z + (lotZ1 - 1f - b.max.z));
                        }
                        break;
                    }
                    case 2: // the empties: a row of boxes on the ground, one and two high
                    {
                        if (_boxPrefabs.Count == 0) goto case 3;
                        var bb = HarborKit.PrefabBounds(_boxPrefabs[0]);
                        float boxH = Mathf.Max(1f, bb.size.y);
                        int n = Mathf.Clamp(Mathf.FloorToInt((x1 - x0) / BoxPitch), 1, 4);
                        float startX = (x0 + x1) * 0.5f - (n - 1) * BoxPitch * 0.5f;
                        int rows = (lotZ1 - lotZ0) > 12f ? 2 : 1;
                        for (int r = 0; r < rows; r++)
                            for (int k = 0; k < n; k++)
                            {
                                if (_rng.NextDouble() < 0.2) continue;
                                int high = _rng.NextDouble() < 0.4 ? 2 : 1;
                                for (int layer = 0; layer < high; layer++)
                                    HarborKit.Prop(HarborKit.Pick(_rng, _boxPrefabs),
                                        new Vector3(startX + k * BoxPitch + HarborKit.Range(_rng, -0.2f, 0.2f), y + layer * boxH,
                                                    lotZ1 - 2.5f - r * 4.5f),
                                        90f + HarborKit.Range(_rng, -2f, 2f), _yardRoot, "Container");
                            }
                        break;
                    }
                    case 3: // the drum store: drums in ranks, spools, a pipe stack, crates
                    {
                        float cx = (x0 + x1) * 0.5f;
                        if (drum != null)
                        {
                            int cols = Mathf.Clamp(Mathf.FloorToInt((x1 - x0) / 1.1f), 2, 8);
                            for (int r = 0; r < 3; r++)
                                for (int c = 0; c < cols; c++)
                                {
                                    if (_rng.NextDouble() < 0.15) continue;
                                    var prefab = drumP != null && _rng.NextDouble() < 0.4 ? drumP : drum;
                                    HarborKit.Sit(prefab, new Vector3(cx - (cols - 1) * 0.55f + c * 1.1f, y, lotZ1 - 1.2f - r * 1.1f),
                                                  HarborKit.Range(_rng, 0f, 360f), _yardRoot, "Drum");
                                }
                        }
                        if (spool != null)
                            for (int k = 0; k < 3; k++)
                                HarborKit.Sit(spool, new Vector3(cx - 3f + k * 2.2f, y, lotZ1 - 7f + HarborKit.Range(_rng, -0.5f, 0.5f)),
                                              HarborKit.Range(_rng, 0f, 360f), _yardRoot, "Spool");
                        if (pipes != null && x1 - x0 > 14f)
                            HarborKit.Sit(pipes, new Vector3(cx + 4.5f, y, lotZ0 + 4f), 90f, _yardRoot, "Pipes");
                        if (crate != null)
                            for (int k = 0; k < 4; k++)
                                HarborKit.Sit(crate, new Vector3(cx - 5f + HarborKit.Range(_rng, 0f, 2f), y, lotZ0 + 3f + k * 1.6f),
                                              HarborKit.Range(_rng, 0f, 360f), _yardRoot, "Crate");
                        break;
                    }
                }
            }

            BuildGatehouses(small);
        }

        /// <summary>The gatehouses: one inside each gate, east of the road with its door
        /// to it - the west side of every gate road belongs to the men's footway.
        /// Turned broadside, just short of the service road, and only built if it fits
        /// the strip between road and corridor.</summary>
        void BuildGatehouses(List<GameObject> small)
        {
            if (small.Count == 0) return;
            foreach (float gx in new[] { _gateWestX, _gateEastX })
            {
                var prefab = HarborKit.Pick(_rng, small);
                var sb = HarborKit.PrefabBounds(prefab);
                float room = GateLaneHalf - GateRoadHalf - 1.5f;
                if (sb.size.z > room) continue;
                Seat(prefab, gx + GateRoadHalf + 1f, _serviceRoadZ0 - 2f - sb.size.x, -90f);
            }
        }

        // ------------------------------------------------------------ containers

        /// <summary>The kit-bashed boxes: the live row per berth (slots the cargo
        /// handler fills and empties, listed low layer first), and the standing blocks
        /// behind it - five rows of boxes lying along the quay, in blocks of eight bays
        /// with a cross lane between blocks and the forklift aisle kept clear through
        /// them at each berth. A block is one thing: its own height - two, three, four
        /// high - its own colour if it is one shipper's, and its own state, full or
        /// being worked down; the rows ramp off a box at the block's ends and the top
        /// layer is never quite complete, which is how a stack looks from the air.</summary>
        void BuildContainerYard()
        {
            _boxPrefabs.Clear();
            _boxPrefabs.AddRange(HarborKit.LoadAll(HarborKit.Containers, quiet: true));
            if (_boxPrefabs.Count == 0)
            {
                Debug.LogWarning("[HarborDemo] no container prefabs under Assets/CityKit/Ships - run Tools/City/Catalog/Rebuild Harbor Ships.");
                return;
            }
            var box = _boxPrefabs[0];
            var bb = HarborKit.PrefabBounds(box);
            float boxH = Mathf.Max(1f, bb.size.y);

            // the live slots: seven along per berth, two high, boxes lying along the
            // quay - only under a berth that HAS a gantry, because the live row is the
            // gantry's landing ground and a bulk quay or a car park has neither
            for (int i = 0; i < berths; i++)
            {
                if (!IsBoxBerth(i)) continue;
                float xb = BerthX(i);
                var slots = new List<Vector3>();
                for (int layer = 0; layer < 2; layer++)
                    for (int k = 0; k < 7; k++)
                        slots.Add(new Vector3(xb - 21f + k * BoxPitch, TileTop + layer * boxH, LiveRowZ));
                _liveSlots[i] = slots;
            }

            // the standing blocks: bays west to east, a new block at every aisle and
            // every eighth bay
            float half = QuayHalf;
            float boxLen = Mathf.Max(bb.size.x, bb.size.z);
            var bays = new List<float>();           // bay centres of the current block
            void LayBlock()
            {
                if (bays.Count == 0) return;
                LayStandingBlock(bays, boxH);
                bays.Clear();
            }
            for (float x = -half + 6f + BoxPitch * 0.5f; x < half - 6f; x += BoxPitch)
            {
                if (YardClosed(x))
                {
                    LayBlock();
                    continue;
                }
                if (bays.Count >= BlockBays)
                {
                    LayBlock();
                    // the cross lane: box edge to box edge, on top of the bay's own slack
                    x += CrossLane - (BoxPitch - boxLen);
                    if (YardClosed(x) || x >= half - 6f) continue;
                }
                bays.Add(x);
            }
            LayBlock();
        }

        /// <summary>One standing block over the given bay centres: its plan drawn, then
        /// every slot stacked to the plan with the block's own boxes.</summary>
        void LayStandingBlock(List<float> bays, float boxH)
        {
            int n = bays.Count;
            // the block's character
            int baseHigh = _rng.NextDouble() < 0.2 ? 4 : (_rng.NextDouble() < 0.55 ? 3 : 2);
            bool worked = n >= 4 && _rng.NextDouble() < 0.22;            // being run down: half gone
            bool oneShipper = _rng.NextDouble() < 0.35;
            var shipper = HarborKit.Pick(_rng, _boxPrefabs);
            float skew = HarborKit.Range(_rng, -1.2f, 1.2f);             // the whole block a touch off square
            int workedFrom = worked ? _rng.Next(1, Mathf.Max(2, n / 2)) : n;
            bool workedEast = _rng.NextDouble() < 0.5;

            for (int r = 0; r < BlockRows; r++)
            {
                float z = BlockZ0 + RowPitch * 0.5f + r * RowPitch;
                // the outer rows a box lower now and then - the shape a block takes as
                // it is picked from the outside in
                int rowHigh = baseHigh - ((r == 0 || r == BlockRows - 1) && _rng.NextDouble() < 0.4 ? 1 : 0);
                for (int k = 0; k < n; k++)
                {
                    int bay = workedEast ? n - 1 - k : k;
                    int high = rowHigh;
                    if (bay >= workedFrom) high = _rng.NextDouble() < 0.6 ? 0 : 1;  // the worked end: mostly gone
                    else if (bay == 0 || bay == n - 1) high -= _rng.NextDouble() < 0.5 ? 1 : 0;   // the ramp at the ends
                    if (high <= 0) continue;
                    // the top layer is never quite complete
                    if (high >= 2 && _rng.NextDouble() < 0.18) high--;
                    for (int layer = 0; layer < high; layer++)
                    {
                        var prefab = oneShipper && _rng.NextDouble() < 0.85 ? shipper : HarborKit.Pick(_rng, _boxPrefabs);
                        var pos = new Vector3(bays[k] + HarborKit.Range(_rng, -0.12f, 0.12f), TileTop + layer * boxH,
                                              z + HarborKit.Range(_rng, -0.08f, 0.08f));
                        HarborKit.Prop(prefab, pos, 90f + skew + HarborKit.Range(_rng, -0.8f, 0.8f), _yardRoot, "Container");
                    }
                }
            }
        }

        bool InAisle(float x)
        {
            for (int i = 0; i < berths; i++)
                if (Mathf.Abs(x - AisleX(i)) < 4.5f + BoxPitch * 0.5f) return true;
            return false;
        }

        public float BerthX(int i) => (i - (berths - 1) * 0.5f) * berthPitch;
        /// <summary>The forklift line for a berth: east of its centre, up through the rows.</summary>
        public float AisleX(int i) => BerthX(i) + 14f;

        // ------------------------------------------------------------ fence, roads, street

        GameObject _fencePanel, _fenceCrown;
        float _crownDrop;                // how far the crown must come down to sit on a panel

        /// <summary>One length of yard fence: the chain-link panels, and the barbed
        /// crown along their tops. The pack's "wire" piece is only that crown - 0.77 m
        /// of barbed arm pivoted three metres up, meant to ride on a panel - so laid on
        /// its own at ground level it hangs in the air over nothing, which is what the
        /// yard's perimeter used to be. The drop between the two is measured off the
        /// prefabs rather than typed, and both runs are stretched to the same span, so
        /// panel and crown keep step.</summary>
        void FenceRun(Vector3 from, Vector3 to)
        {
            if (_fencePanel != null) HarborKit.LayRun(_fencePanel, from, to, _fenceRoot, "Fence");
            if (_fenceCrown != null)
            {
                var lift = new Vector3(0f, -_crownDrop, 0f);
                HarborKit.LayRun(_fenceCrown, from + lift, to + lift, _fenceRoot, "FenceWire");
            }
        }

        /// <summary>A length of the perimeter, with the hole left in it. Every yard has
        /// one somewhere and it is never at a gate: the cut is decided with the berths
        /// (PlanBerthKinds) so the wire can be built round it and dressed afterwards
        /// (HarborDistrict.Contraband.cs). Handed its ends either way round, because the
        /// gates are not guaranteed to be in west-east order.</summary>
        void FenceLine(float x0, float x1, float y, float z)
        {
            if (x1 < x0) (x0, x1) = (x1, x0);
            if (WireCutHalf > 0f && _wireCutX > x0 + 1f && _wireCutX < x1 - 1f)
            {
                FenceRun(new Vector3(x0, y, z), new Vector3(_wireCutX - WireCutHalf, y, z));
                FenceRun(new Vector3(_wireCutX + WireCutHalf, y, z), new Vector3(x1, y, z));
                return;
            }
            FenceRun(new Vector3(x0, y, z), new Vector3(x1, y, z));
        }

        void BuildFence()
        {
            _fencePanel = HarborKit.TryLoad(HarborKit.FencePanel);
            _fenceCrown = HarborKit.TryLoad(HarborKit.FenceWire);
            var gate = HarborKit.TryLoad(HarborKit.FenceGate);
            _crownDrop = _fencePanel != null && _fenceCrown != null
                ? Mathf.Max(0f, HarborKit.PrefabBounds(_fenceCrown).min.y - HarborKit.PrefabBounds(_fencePanel).max.y)
                : 0f;
            float half = QuayHalf, z = _fenceZ, y = TileTop;
            if (_fencePanel == null && _fenceCrown == null)
            {
                Debug.LogWarning("[HarborDemo] the GangWarfare fence pieces are missing - the yard stands open.");
                return;
            }
            FenceLine(-half, _gateWestX - GateHalf, y, z);
            FenceLine(_gateWestX + GateHalf, _gateEastX - GateHalf, y, z);
            FenceLine(_gateEastX + GateHalf, half, y, z);
            // the ends: down the sides of the yard to the coping
            FenceRun(new Vector3(-half, y, z), new Vector3(-half, y, 3f));
            // The east side steps around the deeper bulk terminal rather than drawing a
            // fence through it. This jog is the port's deliberately irregular outline.
            FenceRun(new Vector3(half, y, z), new Vector3(half, y, BulkTerminalNorth));
            FenceRun(new Vector3(half, y, BulkTerminalNorth),
                     new Vector3(BulkTerminalEast, y, BulkTerminalNorth));
            FenceRun(new Vector3(BulkTerminalEast, y, BulkTerminalNorth),
                     new Vector3(BulkTerminalEast, y, BulkTerminalSouth + 3f));
            // the gates stand open all day - a port's do - each as two leaves swung
            // out flat beside the road, hinged at the posts, a leaf no longer than the
            // verge between wire and pavement (fit: the piece is one leaf's worth)
            if (gate != null)
            {
                float leaf = Mathf.Min(Mathf.Max(3f, HarborKit.RunOf(gate).magnitude), 7f);
                foreach (float gx in new[] { _gateWestX, _gateEastX })
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var post = new Vector3(gx + side * GateHalf, y, z);
                        HarborKit.PlaceRun(gate, post, post + new Vector3(0f, 0f, leaf), _fenceRoot, fit: true, "Gate");
                    }
            }
        }

        /// <summary>What the port drives on: a city street's carriageway, laid in a
        /// loop - the yard road along the shed fronts, the service road along their
        /// backs, a gate road at either end joining the two and running on through the
        /// wire to the street - the road kit's own tiles with their yellow lines, a
        /// junction square wherever two meet, and the forklift aisles and the quay
        /// lane as plain yard asphalt off it. The ends of the yard road past the gate
        /// corners are yard asphalt too, for the forklifts' turn. A lorry can be
        /// followed from the street to a door and out again without leaving a road.</summary>
        void BuildYardRoads()
        {
            float half = QuayHalf, gW = _gateWestX, gE = _gateEastX;
            // the loop, a hair over the concrete like the asphalt strips
            _yardStreet = new StreetKit(_apronRoot, y: TileTop + 0.012f,
                registerWalkPlan: false) { Palms = false };
            bool laid = _yardStreet.LayRoadAlongX(YardLaneZ, gW + 5f, gE - 5f)
                        && _yardStreet.LayRoadAlongX(ServiceLaneZ, gW + 5f, gE - 5f);
            foreach (float gx in new[] { gW, gE })
            {
                _yardStreet.LayRoadAlongZ(gx, YardLaneZ + 5f, ServiceLaneZ - 5f);
                _yardStreet.LayRoadAlongZ(gx, ServiceLaneZ + 5f, _fenceZ);   // to the wire; the street kit takes it on from there
                // yard roads have no kerb to park at: the square is the bare two lanes
                _yardStreet.LayJunction(gx, YardLaneZ, half: StreetKit.RoadHalf);        // the bend
                _yardStreet.LayJunction(gx, ServiceLaneZ, half: StreetKit.RoadHalf);     // the T
            }
            if (!laid)
            {
                Debug.LogWarning("[HarborDemo] the street kit did not load - the loop is plain asphalt.");
                AsphaltStrip(gW - 5f, gE + 5f, YardRoadZ0, YardRoadZ1, _apronRoot);
                AsphaltStrip(gW - 5f, gE + 5f, _serviceRoadZ0, _serviceRoadZ1, _apronRoot);
                foreach (float gx in new[] { gW, gE })
                    AsphaltStrip(gx - GateRoadHalf, gx + GateRoadHalf, YardRoadZ0, _streetZ - StreetKit.OuterHalf + 0.5f, _apronRoot);
            }
            // the yard's own lanes, off the loop
            AsphaltStrip(-half + 1f, gW - 5f, YardRoadZ0, YardRoadZ1, _apronRoot);
            AsphaltStrip(gE + 5f, half - 1f, YardRoadZ0, YardRoadZ1, _apronRoot);
            AsphaltStrip(-half + 1f, half - 1f, QuayLaneZ - 3f, QuayLaneZ + 3f, _apronRoot);
            BuildBulkTerminalRoads();
            // the forklift aisles - only where there ARE forklift aisles. A berth that
            // works no boxes has no stacks to run a lane between, and a strip of tarmac
            // laid up through a sand heap or a rank of parked imports is a road drawn
            // where nothing drives.
            for (int i = 0; i < berths; i++)
                if (IsBoxBerth(i))
                    AsphaltStrip(AisleX(i) - 2.5f, AisleX(i) + 2.5f, QuayLaneZ + 3f, YardRoadZ0, _apronRoot);
        }

        StreetKit _street, _yardStreet;
        readonly List<float> _standaloneBackStreetNorthLinks = new List<float>();

        sealed class BackStreetCrossing
        {
            public float X;
            public bool North;
            public bool South;
        }

        /// <summary>Contract X positions where a neighbouring standalone district's
        /// streets enter the north side of the harbor road.</summary>
        internal void SetStandaloneBackStreetNorthLinks(IEnumerable<float> contractXs)
        {
            _standaloneBackStreetNorthLinks.Clear();
            if (contractXs == null) return;
            foreach (float contractX in contractXs)
            {
                float ownX = contractX - GateSpanCentre;
                bool duplicate = false;
                foreach (float present in _standaloneBackStreetNorthLinks)
                    if (Mathf.Abs(present - ownX) < 0.1f) { duplicate = true; break; }
                if (!duplicate) _standaloneBackStreetNorthLinks.Add(ownX);
            }
            _standaloneBackStreetNorthLinks.Sort();
        }

        /// <summary>The road the port is reached by: a two-way street outside the wire,
        /// running well past both ends of the quay so the lorries come on and go off it
        /// out of sight, dressed the road demo's way but for the palms - nobody plants
        /// a boulevard along a container port. Laid in lengths around every gate and
        /// landward service junction, so all of those roads open through its kerb rather
        /// than being drawn over it.</summary>
        void BuildBackStreet()
        {
            // a hair above the land: the kit's carriageway lies at its own y and the
            // heightfield at LandY, and a road laid level with the grass is under it
            _street = new StreetKit(_streetRoot, y: LandY + 0.03f,
                registerWalkPlan: false) { Palms = false };
            float gW = _gateWestX, gE = _gateEastX;
            var gates = new[] { gW, gE };
            int linked = _links != null ? _links.Length : 0;
            var crossings = new List<BackStreetCrossing>();
            void AddCrossing(float x, bool north, bool south)
            {
                foreach (var present in crossings)
                {
                    if (Mathf.Abs(present.X - x) >= 0.1f) continue;
                    present.North |= north;
                    present.South |= south;
                    return;
                }
                crossings.Add(new BackStreetCrossing { X = x, North = north, South = south });
            }
            for (int k = 0; k < gates.Length; k++) AddCrossing(gates[k], k < linked, true);
            foreach (float x in _standaloneBackStreetNorthLinks) AddCrossing(x, true, false);
            crossings.Sort((one, other) => one.X.CompareTo(other.X));

            // Dressed lengths between crossings; beside each square the sidewalk is
            // omitted only on the side where another road actually arrives.
            const float H = StreetKit.StreetHalf;
            const float C = StreetKit.Cell;
            float cursor = -QuayHalf - 130f;
            float end = QuayHalf + 130f;
            bool ok = true;
            void Along(float from, float to, bool southWalk = true, bool northWalk = true, bool dress = true)
            {
                if (to <= from + 0.05f) return;
                if (!_street.LayAlongX(_streetZ, from, to, southWalk, northWalk, dress)) ok = false;
            }
            foreach (var crossing in crossings)
            {
                if (crossing.X - H <= cursor || crossing.X + H >= end) continue;
                Along(cursor, crossing.X - H - C);
                Along(Mathf.Max(cursor, crossing.X - H - C), crossing.X - H,
                      southWalk: !crossing.South, northWalk: !crossing.North, dress: false);
                Along(crossing.X + H, Mathf.Min(end, crossing.X + H + C),
                      southWalk: !crossing.South, northWalk: !crossing.North, dress: false);
                cursor = Mathf.Max(cursor, crossing.X + H + C);
            }
            Along(cursor, end);
            if (!ok)
            {
                Debug.LogWarning("[HarborDemo] the street kit did not load - no approach road.");
                return;
            }
            foreach (var crossing in crossings)
            {
                _street.LayJunction(crossing.X, _streetZ,
                                    capNorth: !crossing.North,
                                    capSouth: !crossing.South,
                                    splaySouth: crossing.South ? 1 : 0,
                                    splayNorth: crossing.North ? 1 : 0);
            }
            foreach (float gx in gates)
            {
                // the gate road outside the wire, at the street's own level, up to the T
                _street.LayRoadAlongZ(gx, _fenceZ, _streetZ - StreetKit.StreetHalf);
            }
        }

        // ------------------------------------------------------------ dressing

        /// <summary>What a working yard is strewn with: pallets by the gangways, a burn
        /// barrel with the men's stools of crates round it, cones where the aisles meet
        /// the road, dumpsters at the sheds, poles along the fence, lamps down the
        /// yard road; and the back lots between the sheds.</summary>
        void DressYard()
        {
            var pallet = HarborKit.TryLoad(HarborKit.Pallet);
            var burn = HarborKit.TryLoad(HarborKit.BurnBarrel);
            var crate = HarborKit.TryLoad(HarborKit.Crate1);
            var cone = HarborKit.TryLoad(HarborKit.Cone);
            var dumpster = HarborKit.TryLoad(HarborKit.Dumpster);
            var pole = HarborKit.TryLoad(HarborKit.Powerpole);
            float y = TileTop;

            for (int i = 0; i < berths; i++)
            {
                float xb = BerthX(i);
                // the pallet pile the forklift draws from, and a burn barrel with seats
                // - all between the quay lane and the men's footway
                if (pallet != null)
                {
                    var pb = HarborKit.PrefabBounds(pallet);
                    for (int k = 0; k < 3; k++)
                        HarborKit.Prop(pallet, new Vector3(xb + 8f, y + k * pb.size.y, QuayLaneZ + 4f), k * 4f, _yardRoot, "Pallet");
                    HarborKit.Prop(pallet, new Vector3(xb + 10.5f, y, QuayLaneZ + 4.2f), 12f, _yardRoot, "Pallet");
                }
                var drumAt = new Vector3(xb - 30f, y, QuayLaneZ + 4.4f);
                if (burn != null)
                {
                    HarborKit.Prop(burn, drumAt, 0f, _yardRoot, "BurnBarrel");
                    // the mugs on its rim: what says the men round it are on their break
                    var mug = HarborKit.TryLoad(HarborKit.CoffeeCup);
                    if (mug != null)
                    {
                        float rim = drumAt.y + HarborKit.PrefabBounds(burn).max.y;
                        for (int k = 0; k < 2; k++)
                            HarborKit.Sit(mug, new Vector3(drumAt.x - 0.18f + k * 0.34f, rim, drumAt.z + (k == 0 ? 0.1f : -0.14f)),
                                          HarborKit.Range(_rng, 0f, 360f), _yardRoot, "Mug");
                    }
                }
                if (crate != null)
                {
                    // the men's stools. Offered to the break rota as SEATS, so a docker
                    // called off his round sits on the crate that is actually there
                    // rather than on a point beside it (HarborDistrict.Routine.cs)
                    var west = new Vector3(xb - 31.6f, y, QuayLaneZ + 3.7f);
                    var east = new Vector3(xb - 28.6f, y, QuayLaneZ + 3.9f);
                    HarborKit.Prop(crate, west, 30f, _yardRoot, "Crate");
                    HarborKit.Prop(crate, east, -20f, _yardRoot, "Crate");
                    OfferSeat(crate, west, 30f, drumAt);
                    OfferSeat(crate, east, -20f, drumAt);
                }
                if (cone != null)
                {
                    HarborKit.Prop(cone, new Vector3(AisleX(i) - 3.2f, y, YardRoadZ0 - 0.6f), 0f, _yardRoot, "Cone");
                    HarborKit.Prop(cone, new Vector3(AisleX(i) + 3.2f, y, YardRoadZ0 - 0.6f), 0f, _yardRoot, "Cone");
                }
            }
            if (dumpster != null)
                foreach (var door in _shedDoors)
                    HarborKit.Prop(dumpster, new Vector3(door.x - 13.5f, y, door.z + 1f), 0f, _yardRoot, "Dumpster");
            if (pole != null)
                for (float x = -QuayHalf + 20f; x < QuayHalf - 10f; x += 40f)
                {
                    if (InGateLane(x, 1f)) continue;
                    HarborKit.Prop(pole, new Vector3(x, y, _fenceZ - 1.2f), 0f, _yardRoot, "Pole");
                }
            // lamps down the yard road too, on the block side of it
            for (float x = -QuayHalf + 27f; x < QuayHalf - 10f; x += 45f)
                HarborKit.Prop(_pierLamp, new Vector3(x, y, YardRoadZ0 - 0.7f), 0f, _yardRoot);   // its own name: see BuildQuay

            BuildBackLots();
            DressPerimeter();
        }

        /// <summary>The perimeter: razor coils at the foot of the wire, danger boards
        /// either side of each gate, concrete barriers narrowing the gate approach, and
        /// a bin at every shed door. Every one of them is seated on its own measured
        /// underside - the packs pivot their furniture at the middle as readily as at
        /// the feet.</summary>
        void DressPerimeter()
        {
            var coil = HarborKit.TryLoad(HarborKit.BarbedWire);
            var sign = HarborKit.TryLoad(HarborKit.DangerSign);
            var block = HarborKit.TryLoad(HarborKit.ConcreteBlock);
            var bin = HarborKit.TryLoad(HarborKit.YardBin);
            float y = TileTop, half = QuayHalf;

            // coils along the inside foot of the perimeter wire, in broken lengths -
            // a yard never finishes the run in one go
            if (coil != null)
            {
                float step = Mathf.Max(2f, HarborKit.PrefabBounds(coil).size.x);
                for (float x = -half + 8f; x < half - 8f; x += step)
                {
                    if (InGateLane(x, 1f)) continue;
                    if (_rng.NextDouble() < 0.35) continue;
                    HarborKit.Sit(coil, new Vector3(x, y, _fenceZ - 0.9f), 0f, _yardRoot, "RazorWire");
                }
            }

            foreach (float gx in new[] { _gateWestX, _gateEastX })
            {
                // the boards face the street. A board on a post stands on the ground; a
                // plate is bolted to the wire at head height - the prefab's own height
                // says which it is.
                if (sign != null)
                {
                    var sb = HarborKit.PrefabBounds(sign);
                    float sy = sb.size.y > 1.2f ? y - sb.min.y : y + 1.5f - sb.min.y - sb.size.y * 0.5f;
                    foreach (float dx in new[] { -GateHalf - 1.5f, GateHalf + 1.5f })
                        HarborKit.Prop(sign, new Vector3(gx + dx, sy, _fenceZ + 0.35f), 0f, _yardRoot, "DangerSign");
                }
                if (block != null)
                {
                    var kb = HarborKit.PrefabBounds(block);
                    float w = Mathf.Max(1f, Mathf.Max(kb.size.x, kb.size.z));
                    for (int k = 0; k < 2; k++)
                        foreach (float side in new[] { -1f, 1f })
                            HarborKit.Sit(block, new Vector3(gx + side * (GateRoadHalf + 1.5f + k * w), y, _serviceRoadZ1 + 1.4f),
                                          90f, _yardRoot, "ConcreteBlock");
                }
            }

            if (bin != null)
                foreach (var door in _shedDoors)
                    HarborKit.Sit(bin, new Vector3(door.x + 8.5f, y, door.z + 0.1f),
                                  HarborKit.Range(_rng, -10f, 10f), _yardRoot, "Bin");
        }
    }
}
