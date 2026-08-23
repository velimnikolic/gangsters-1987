using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RoadDemo;

static class Program
{
    const float Dt = 1f / 30f;

    // ------------------------------------------------------------ builders

    // A city grid: n x m nodes, streets 2-lane (half 5, lane 2.5) unless boulevard.
    static LaneNet Grid(float[] xs, float[] zs, bool signals, float setback = 5.7f, float nodeHalf = 5f, bool[] blvdX = null)
    {
        var net = new LaneNet();
        var nodes = new RoadNode[xs.Length, zs.Length];
        for (int i = 0; i < xs.Length; i++)
            for (int j = 0; j < zs.Length; j++)
            {
                bool bx = blvdX != null && blvdX[i];
                float hx = bx ? 15f : nodeHalf;
                nodes[i, j] = net.AddNode(xs[i], zs[j], hx, nodeHalf, setback);
                nodes[i, j].I = i; nodes[i, j].J = j;
                if (signals) nodes[i, j].Signal = new TrafficSignal(((i * 31 + j * 17) % 13) / 13f * TrafficSignal.Cycle);
            }
        for (int i = 0; i < xs.Length; i++)
        {
            bool bx = blvdX != null && blvdX[i];
            for (int j = 0; j + 1 < zs.Length; j++)
            {
                var a = nodes[i, j]; var b = nodes[i, j + 1];
                net.AddRoad(new Vector3(xs[i], 0, a.ZMax), new Vector3(xs[i], 0, b.ZMin), bx ? 15f : 5f,
                    bx ? new[] { 7.5f, 12.5f } : new[] { 2.5f }, bx ? 14f : 10f, a, b, true, bx ? 5f : 0f);
            }
        }
        for (int j = 0; j < zs.Length; j++)
            for (int i = 0; i + 1 < xs.Length; i++)
            {
                var a = nodes[i, j]; var b = nodes[i + 1, j];
                net.AddRoad(new Vector3(a.XMax, 0, zs[j]), new Vector3(b.XMin, 0, zs[j]), 5f, new[] { 2.5f }, 10f, a, b, false);
            }
        net.Finish();
        return net;
    }

    // The crew demo: a block with four streets round it, crossroads at the corners,
    // stubs beyond them to dead ends.
    static LaneNet CrewRing(float wx = -45f, float ex = 45f, float sz = -35f, float nz = 35f, float reach = 35f)
    {
        var net = new LaneNet();
        float[] xs = { wx - 5 - reach, wx, ex, ex + 5 + reach };
        float[] zs = { sz - 5 - reach, sz, nz, nz + 5 + reach };
        var nodes = new RoadNode[4, 4];
        // corners
        for (int i = 1; i <= 2; i++)
            for (int j = 1; j <= 2; j++)
                nodes[i, j] = net.AddNode(xs[i], zs[j], 5f, 5f, 1.5f);
        // dead ends
        RoadNode End(float x, float z) => net.AddNode(x, z, 0.5f, 0.5f, 0.5f);
        // horizontal streets (j = 1, 2): west stub, middle, east stub
        for (int j = 1; j <= 2; j++)
        {
            var w = End(xs[0], zs[j]); var e = End(xs[3], zs[j]);
            net.AddRoad(new Vector3(w.XMax, 0, zs[j]), new Vector3(nodes[1, j].XMin, 0, zs[j]), 5f, new[] { 2.5f }, 10f, w, nodes[1, j], false);
            net.AddRoad(new Vector3(nodes[1, j].XMax, 0, zs[j]), new Vector3(nodes[2, j].XMin, 0, zs[j]), 5f, new[] { 2.5f }, 10f, nodes[1, j], nodes[2, j], false);
            net.AddRoad(new Vector3(nodes[2, j].XMax, 0, zs[j]), new Vector3(e.XMin, 0, zs[j]), 5f, new[] { 2.5f }, 10f, nodes[2, j], e, false);
        }
        for (int i = 1; i <= 2; i++)
        {
            var s = End(xs[i], zs[0]); var n = End(xs[i], zs[3]);
            net.AddRoad(new Vector3(xs[i], 0, s.ZMax), new Vector3(xs[i], 0, nodes[i, 1].ZMin), 5f, new[] { 2.5f }, 10f, s, nodes[i, 1], true);
            net.AddRoad(new Vector3(xs[i], 0, nodes[i, 1].ZMax), new Vector3(xs[i], 0, nodes[i, 2].ZMin), 5f, new[] { 2.5f }, 10f, nodes[i, 1], nodes[i, 2], true);
            net.AddRoad(new Vector3(xs[i], 0, nodes[i, 2].ZMax), new Vector3(xs[i], 0, n.ZMin), 5f, new[] { 2.5f }, 10f, nodes[i, 2], n, true);
        }
        net.Finish();
        return net;
    }

    // ------------------------------------------------------------ measurement

    class Stats
    {
        public int Overlaps, Stalls, BeltHits, Frames;
        public float SpeedSum; public int SpeedN;
        public Dictionary<RoadCar, float> StillFor = new Dictionary<RoadCar, float>();
        public HashSet<RoadCar> Stalled = new HashSet<RoadCar>();
        public List<string> Notes = new List<string>();
        public float MaxDepth;
        public Dictionary<RoadCar, List<string>> Hist = new Dictionary<RoadCar, List<string>>();
        public int Traced;
    }

    static void Measure(List<RoadCar> cars, Stats st, float dt, float now)
    {
        var users = StreetTraffic.Users;
        for (int i = 0; i < users.Count; i++)
            for (int j = i + 1; j < users.Count; j++)
            {
                var a = users[i]; var b = users[j];
                if ((a.RoadPosition - b.RoadPosition).sqrMagnitude > 12f * 12f) continue;
                if (RoadSpace.Overlap(a.RoadPosition, a.RoadForward, a.HalfLength, a.HalfWidth,
                                      b.RoadPosition, b.RoadForward, b.HalfLength, b.HalfWidth, 0f, out var push))
                {
                    st.Overlaps++;
                    st.MaxDepth = Math.Max(st.MaxDepth, push.magnitude);
                    if (st.Notes.Count < 12)
                        st.Notes.Add($"t={now:F1} overlap {Name(a)} & {Name(b)} depth {push.magnitude:F2} at {a.RoadPosition}");
                }
            }
        foreach (var c in cars)
        {
            if (c.Parked) continue;
            st.SpeedSum += Math.Abs(c.Speed); st.SpeedN++;
            st.StillFor.TryGetValue(c, out float sf);
            sf = Math.Abs(c.Speed) < 0.1f ? sf + dt : 0f;
            st.StillFor[c] = sf;
            if (!st.Hist.TryGetValue(c, out var h)) st.Hist[c] = h = new List<string>();
            h.Add($"{now:F1}: v={c.Speed:F1} s={c.S:F1} d={c.D:F1} {c.DoingLine} {c.Why} | pass: {c.PassWhy} {ViaInfo(c)}");
            if (h.Count > 110) h.RemoveAt(0);
            if (sf > 45f && !st.Stalled.Contains(c))
            {
                st.Stalled.Add(c); st.Stalls++;
                if (st.Traced < 2) { st.Traced++; for (int k = Math.Max(0, h.Count - 110); k < h.Count; k += 5) st.Notes.Add("      hist car " + c.Id + " " + h[k]); }
                if (st.Notes.Count < 40)
                    st.Notes.Add($"t={now:F1} STALL car {c.Id} {c.Profile.Name} {c.DoingLine} at {c.RoadPosition} road {(c.Road != null ? c.Road.Index : -1)} s={c.S:F1} d={c.D:F1} via={(c.Via != null)} why: {c.Why} {ViaInfo(c)}");
            }
        }
    }

    static string ViaInfo(RoadCar c)
    {
        if (c.Via == null) return "";
        var v = c.Via;
        var inside = v.Node.Inside;
        var mine = inside.FirstOrDefault(o => o.Car == c);
        string others = string.Join(",", inside.Where(o => o.Car != c).Select(o => $"car{o.Car.Id}@{o.Via.From.Road.Index}/{o.Via.From.Heading}->{o.Via.To.Road.Index}/{o.Via.To.Heading}:conf={v.Conflicts[o.Via.Index]}"));
        return $"| via {v.From.Road.Index}/{v.From.Heading}->{v.To.Road.Index}/{v.To.Heading} kind {v.Kind} inside={(mine != null)} others=[{others}]";
    }

    static string Name(IRoadUser u) => u is RoadCar c ? $"car{c.Id}({c.Profile.Name},{c.DoingLine})" : "static";

    static void Run(string title, LaneNet net, List<RoadCar> cars, float seconds, Action<float> each = null)
    {
        var st = new Stats();
        float lateSpeed = 0f; int lateN = 0; int beltNoted = 0;
        int frames = (int)(seconds / Dt);
        Time.deltaTime = Dt;
        RoadCar.BeltHits = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double tickMs = 0;
        for (int f = 0; f < frames; f++)
        {
            Time.time = f * Dt;
            Time.frameCount = f;
            each?.Invoke(Time.time);
            int bh = RoadCar.BeltHits;
            var t0 = sw.Elapsed.TotalMilliseconds;
            foreach (var c in cars) c.Tick(Dt);
            tickMs += sw.Elapsed.TotalMilliseconds - t0;
            if (RoadCar.BeltHits > bh && st.Notes.Count < 40 && beltNoted < 6) { beltNoted++; st.Notes.Add($"t={Time.time:F1} BELT " + RoadCar.LastBeltHit); }
            if (f % 6 == 0) Measure(cars, st, Dt * 6, Time.time);
            if (f == frames - 30 * 60) { lateSpeed = 0f; lateN = 0; }
            if (f >= frames - 30 * 60) foreach (var c in cars) if (!c.Parked) { lateSpeed += Math.Abs(c.Speed); lateN++; }
        }
        int frozen = 0; foreach (var kv in st.StillFor) if (kv.Value > 60f) frozen++;
        float maxStill = 0f; foreach (var kv in st.StillFor) maxStill = Math.Max(maxStill, kv.Value);
        Console.WriteLine($"   longest stand at the end: {maxStill:F0}s");
        if (frozen > 0) foreach (var kv in st.StillFor) if (kv.Value > 60f && st.Notes.Count < 60) st.Notes.Add("   FROZEN car " + kv.Key.Id + " " + kv.Key.Profile.Name + " " + kv.Key.Describe() + " pass: " + kv.Key.PassWhy);
        st.BeltHits = RoadCar.BeltHits;
        Console.WriteLine($"== {title}: {cars.Count} cars, {seconds}s: overlaps={st.Overlaps} (max depth {st.MaxDepth:F2}) stalls={st.Stalls} beltHits={st.BeltHits} avgSpeed={(st.SpeedN > 0 ? st.SpeedSum / st.SpeedN : 0):F1} lastMinuteAvg={(lateN > 0 ? lateSpeed / lateN : 0):F1} frozen>60s={frozen} tick={tickMs / frames:F2}ms/frame");
        foreach (var n in st.Notes) Console.WriteLine("   " + n);
        if (Environment.GetEnvironmentVariable("TRACE") == "1")
            foreach (var kv in st.Hist)
                if (kv.Key.Id.ToString() == (Environment.GetEnvironmentVariable("TRACEID") ?? "1") || cars.Count <= 2)
                    for (int k = Math.Max(0, kv.Value.Count - 60); k < kv.Value.Count; k += 2) Console.WriteLine("      trace car " + kv.Key.Id + " " + kv.Value[k]);
        int shown = 0;
        foreach (var c in cars)
        {
            if (c.Via == null || Math.Abs(c.Speed) > 0.1f || shown >= 8) continue;
            shown++;
            Console.WriteLine($"   END stuck in box: car {c.Id} viaS={c.ViaS:F1}/{c.Via.Length:F1} why: {c.Why} {ViaInfo(c)}");
        }
    }

    static RoadCar Spawn(LaneNet net, RoadEdge e, float s, DriverProfile p = null, float hl = 2.3f, float hw = 0.95f)
    {
        var c = new RoadCar { HalfLen = hl, HalfWide = hw, Net = net, Profile = p ?? DriverProfile.Traffic };
        c.Spawn(e, s);
        StreetTraffic.Users.Add(c);
        return c;
    }

    static void Reset() { StreetTraffic.Users.Clear(); StreetTraffic.Bodies.Clear(); StreetTraffic.Walkers.Clear(); }

    // ------------------------------------------------------------ scenarios

    static void GridTraffic(int count, bool signals, float seconds, bool blvd = false)
    {
        Reset();
        var xs = new float[] { -200, -60, 80, 220 };
        var zs = new float[] { -180, -40, 100, 240 };
        var net = Grid(xs, zs, signals, blvdX: blvd ? new[] { false, true, false, false } : null);
        var cars = new List<RoadCar>();
        int placed = 0;
        for (int round = 0; placed < count && round < 40; round++)
        {
            bool any = false;
            foreach (var e in net.Edges)
            {
                if (placed >= count) break;
                float s = 6f + round * 18f;
                if (s > e.Length - 12f) continue;
                any = true;
                cars.Add(Spawn(net, e, s));
                placed++;
            }
            if (!any) break;
        }
        Run($"grid {(signals ? "signals" : "no signals")}{(blvd ? " +blvd" : "")}", net, cars, seconds);
    }

    static RoadOccupant Prop(LaneNet net, Vector3 pos, Vector3 fwd, float hl = 2.3f, float hw = 0.95f)
    {
        var s = new StaticRoadUser { Position = pos, Forward = fwd, HalfLen = hl, HalfWide = hw };
        StreetTraffic.Users.Add(s);
        return net.AddStatic(s);
    }

    // Crew demo ring: traffic both ways + parked props + a gangster car parking on click side
    static void CrewScenario()
    {
        Reset();
        var net = CrewRing();
        var cars = new List<RoadCar>();
        // traffic on south and north streets
        var south = net.Roads.Where(r => Math.Abs(r.A.z + 35f) < 0.1f && Math.Abs(r.B.z + 35f) < 0.1f).ToList();
        var north = net.Roads.Where(r => Math.Abs(r.A.z - 35f) < 0.1f && Math.Abs(r.B.z - 35f) < 0.1f).ToList();
        foreach (var r in south.Concat(north))
            foreach (var lane in r.Lanes)
                cars.Add(Spawn(net, lane, r.Length * 0.5f));
        // parked props on west/east streets at the kerb
        var west = net.Roads.Where(r => Math.Abs(r.A.x + 45f) < 0.1f && Math.Abs(r.B.x + 45f) < 0.1f && r.Length > 40).First();
        Prop(net, west.Pose(20f, west.KerbD(1, 0.95f)), west.Axis);
        Prop(net, west.Pose(40f, west.KerbD(-1, 0.95f)), -west.Axis);
        // the outfit's car parked at the south kerb, eastbound side, on the middle stretch
        var mid = south.First(r => r.Length > 60);
        var gang = new RoadCar { Net = net, Profile = DriverProfile.Gangster, HalfLen = 2.4f, HalfWide = 0.95f };
        gang.PlaceAt(mid.Pose(20f, mid.KerbD(1, 0.95f)), mid.Axis);
        StreetTraffic.Users.Add(gang);
        cars.Add(gang);
        int phase = 0;
        bool reached = false;
        Run("crew ring: park orders", net, cars, 240f, t =>
        {
            if (phase == 0 && t > 2f) { phase = 1; gang.GoTo(mid.Pose(70f, -4f), park: true); Console.WriteLine("   t=2 order: park on north kerb of south street at s=70 (needs a turn-round)"); }
            if (phase == 1 && gang.Parked && t > 5f) { phase = 2; Console.WriteLine($"   t={t:F1} parked at s={gang.S:F1} d={gang.D:F1} heading {gang.Heading}"); }
            if (phase == 2 && t > 60f) { phase = 3; gang.GoTo(north.First(r => r.Length > 60).Pose(30f, 3f), park: true); Console.WriteLine("   t=60 order: park south kerb of north street"); }
            if (phase == 3 && gang.Parked && t > 65f) { phase = 4; Console.WriteLine($"   t={t:F1} parked on road {gang.Road.Index} s={gang.S:F1} d={gang.D:F1}"); reached = true; }
        });
        Console.WriteLine("   second park reached: " + reached + " gang doing: " + gang.DoingLine + " parked=" + gang.Parked);
    }

    // A stopped car in the lane ahead: traffic waits then passes; gangster passes at once; hot uses the crown
    static void BlockerScenario(DriverProfile p, string title)
    {
        Reset();
        var net = CrewRing();
        var cars = new List<RoadCar>();
        var south = net.Roads.Where(r => Math.Abs(r.A.z + 35f) < 0.1f && Math.Abs(r.B.z + 35f) < 0.1f && r.Length > 60).First();
        var east = south.Lanes.First(l => l.Heading > 0);
        var westL = south.Lanes.First(l => l.Heading < 0);
        // a wreck stood in the eastbound lane at s=50
        Prop(net, south.Pose(50f, 2.5f), south.Axis);
        // oncoming traffic in the west lane, a trickle
        var car = Spawn(net, east, 5f, p);
        cars.Add(car);
        var onc = Spawn(net, westL, south.Length - 5f);
        cars.Add(onc);
        float passedAt = -1f;
        Run(title, net, cars, 90f, t =>
        {
            if (passedAt < 0 && car.Road == south && car.S > 60f) { passedAt = t; Console.WriteLine($"   passed the wreck at t={t:F1} doing {car.DoingLine}"); }
        });
        if (passedAt < 0) Console.WriteLine($"   never passed: car at road {(car.Road?.Index)} s={car.S:F1} d={car.D:F1} doing {car.DoingLine} speed {car.Speed:F1}");
    }

    // Two-way crown: hot car between a queue in its lane and cars in the other
    static void CrownScenario()
    {
        Reset();
        var net = CrewRing();
        var cars = new List<RoadCar>();
        var south = net.Roads.Where(r => Math.Abs(r.A.z + 35f) < 0.1f && Math.Abs(r.B.z + 35f) < 0.1f && r.Length > 60).First();
        var east = south.Lanes.First(l => l.Heading > 0);
        var westL = south.Lanes.First(l => l.Heading < 0);
        // a queue of stopped cars in the east lane 40..70 (props), and parked props at the west kerb
        for (float s = 40f; s <= 70f; s += 7f) Prop(net, south.Pose(s, 2.5f), south.Axis);
        for (float s = 45f; s <= 65f; s += 9f) Prop(net, south.Pose(s, south.KerbD(-1, 0.95f)), -south.Axis);
        var hot = Spawn(net, east, 4f, DriverProfile.Hot);
        cars.Add(hot);
        float passedAt = -1f;
        Run("crown between a queue and parked cars (hot)", net, cars, 60f, t =>
        {
            if (passedAt < 0 && hot.Road == south && hot.S > 78f) { passedAt = t; Console.WriteLine($"   through at t={t:F1} doing {hot.DoingLine}"); }
        });
        if (passedAt < 0) Console.WriteLine($"   never through: s={hot.S:F1} d={hot.D:F1} doing {hot.DoingLine} speed {hot.Speed:F1}");
    }

    // Head on: a gangster car overtaking into the far lane with traffic coming - must wait or pass clean
    static void HeadOnScenario()
    {
        Reset();
        var net = CrewRing();
        var cars = new List<RoadCar>();
        var south = net.Roads.Where(r => Math.Abs(r.A.z + 35f) < 0.1f && Math.Abs(r.B.z + 35f) < 0.1f && r.Length > 60).First();
        var east = south.Lanes.First(l => l.Heading > 0);
        var westL = south.Lanes.First(l => l.Heading < 0);
        // a wreck across most of the east lane out to the crown
        Prop(net, south.Pose(45f, 1.2f), south.Axis);
        var g = Spawn(net, east, 5f, DriverProfile.Gangster);
        cars.Add(g);
        // steady oncoming stream
        for (float s = south.Length - 5f; s > 50f; s -= 16f) cars.Add(Spawn(net, westL, s));
        float passedAt = -1f;
        Run("head-on: pass round a wreck against oncoming", net, cars, 120f, t =>
        {
            if (passedAt < 0 && g.Road == south && g.S > 55f) { passedAt = t; Console.WriteLine($"   passed at t={t:F1} doing {g.DoingLine}"); }
        });
        if (passedAt < 0) Console.WriteLine($"   never passed: s={g.S:F1} d={g.D:F1} doing {g.DoingLine}");
    }

    // Wedged: stopped 1 m behind a wreck with no swing room: reverse then go round
    static void WedgedScenario()
    {
        Reset();
        var net = CrewRing();
        var cars = new List<RoadCar>();
        var south = net.Roads.Where(r => Math.Abs(r.A.z + 35f) < 0.1f && Math.Abs(r.B.z + 35f) < 0.1f && r.Length > 60).First();
        var east = south.Lanes.First(l => l.Heading > 0);
        Prop(net, south.Pose(50f, 2.5f), south.Axis);
        var g = new RoadCar { Net = net, Profile = DriverProfile.Gangster };
        g.PlaceAt(south.Pose(50f - 2.3f - 2.3f - 0.8f, 2.5f), south.Axis);
        StreetTraffic.Users.Add(g);
        cars.Add(g);
        g.GoTo(south.Pose(south.Length - 10f, 4f), park: true);
        float passedAt = -1f;
        Run("wedged behind a wreck: reverse and round", net, cars, 60f, t =>
        {
            if (passedAt < 0 && g.Road == south && g.S > 58f) { passedAt = t; Console.WriteLine($"   passed at t={t:F1}"); }
        });
        if (passedAt < 0) Console.WriteLine($"   never passed: s={g.S:F1} d={g.D:F1} doing {g.DoingLine} parked={g.Parked}");
    }

    // U-turn sweep with a car parked on it: must not turn there
    static void UTurnScenario()
    {
        Reset();
        var net = CrewRing();
        var cars = new List<RoadCar>();
        var south = net.Roads.Where(r => Math.Abs(r.A.z + 35f) < 0.1f && Math.Abs(r.B.z + 35f) < 0.1f && r.Length > 60).First();
        var east = south.Lanes.First(l => l.Heading > 0);
        // parked on the far kerb where a turn from s=30 would sweep
        Prop(net, south.Pose(33f, south.KerbD(-1, 0.95f)), -south.Axis);
        var g = Spawn(net, east, 20f, DriverProfile.Gangster);
        cars.Add(g);
        g.GoTo(south.Pose(10f, -4f), park: true); // behind and across: a turn-round
        Run("u-turn with a car on the sweep, then park across", net, cars, 90f);
        Console.WriteLine($"   result: parked={g.Parked} road={g.Road?.Index} s={g.S:F1} d={g.D:F1} heading={g.Heading} doing={g.DoingLine}");
    }

    // Standoff: traffic car meets a gangster head-on in the same band, both stopped -> traffic yields
    static void StandoffScenario()
    {
        Reset();
        var net = CrewRing();
        var cars = new List<RoadCar>();
        var south = net.Roads.Where(r => Math.Abs(r.A.z + 35f) < 0.1f && Math.Abs(r.B.z + 35f) < 0.1f && r.Length > 60).First();
        var east = south.Lanes.First(l => l.Heading > 0);
        var westL = south.Lanes.First(l => l.Heading < 0);
        // the gangster is on the crown heading east (placed there), a traffic car heading west in its lane
        var g = new RoadCar { Net = net, Profile = DriverProfile.Hot };
        g.PlaceAt(south.Pose(30f, -1.0f), south.Axis);
        StreetTraffic.Users.Add(g); cars.Add(g);
        g.GoTo(south.Pose(south.Length - 10f, 4f), park: true);
        var tcar = Spawn(net, westL, south.Length - 40f);
        cars.Add(tcar);
        Run("standoff on the crown: traffic gives way", net, cars, 60f);
        Console.WriteLine($"   gang: road={g.Road?.Index} s={g.S:F1} parked={g.Parked} doing={g.DoingLine}; traffic: s={tcar.S:F1} d={tcar.D:F1} doing={tcar.DoingLine}");
    }


    // Crab check: a car is a car when the rear axle moves along the heading. Measure the
    // angle between the axle's velocity and fwd, by situation (box / slide / straight) and
    // by heading sign; anything over a couple of degrees is a crab.
    static void CrabScenario()
    {
        Reset();
        var xs = new float[] { -200, -60, 80, 220 };
        var zs = new float[] { -180, -40, 100, 240 };
        var net = Grid(xs, zs, false);
        var cars = new List<RoadCar>();
        foreach (var e in net.Edges) { if (cars.Count >= 24) break; cars.Add(Spawn(net, e, 10f)); }
        var prevAxle = new Dictionary<RoadCar, Vector3>();
        var prevFwd = new Dictionary<RoadCar, Vector3>();
        var acc = new Dictionary<string, (double sum, int n, double max, string where)>();
        int spikes = 0;
        void Note(string key, double ang, string where)
        {
            acc.TryGetValue(key, out var a);
            a.sum += ang; a.n++; if (ang > a.max) { a.max = ang; a.where = where; }
            acc[key] = a;
        }
        // two more stood at the kerbs of one road, one each way, ordered on along the
        // kerb twice: pull-outs and pull-ins on both headings
        var r0 = net.Roads.First(r => r.Length > 100);
        var pk = new List<RoadCar>();
        foreach (int h in new[] { 1, -1 })
        {
            var c = new RoadCar { Net = net, Profile = DriverProfile.Traffic, HalfLen = 2.3f, HalfWide = 0.95f };
            c.PlaceAt(r0.Pose(h > 0 ? 20f : r0.Length - 20f, r0.KerbD(h, 0.95f)), r0.Axis * h);
            StreetTraffic.Users.Add(c); cars.Add(c); pk.Add(c);
        }
        Console.WriteLine("   parkers: " + string.Join(", ", pk.Select(c => $"car{c.Id} h={c.Heading} road={c.Road?.Index} s={c.S:F1} d={c.D:F1}")));
        int ph = 0;
        Run("crab", net, cars, 240f, t =>
        {
            if (ph == 0 && t > 5f) { ph = 1; foreach (var c in pk) c.GoTo(r0.Pose(c.Heading > 0 ? 60f : r0.Length - 60f, r0.KerbD(c.Heading, 0.95f)), park: true); }
            if (ph == 1 && t > 60f) { ph = 2; foreach (var c in pk) c.GoTo(r0.Pose(c.Heading > 0 ? 100f : r0.Length - 100f, r0.KerbD(c.Heading, 0.95f)), park: true); }
            foreach (var c in cars)
            {
                float a = c.HalfLen * 0.6f;
                var axle = c.RoadPosition - c.RoadForward * a;
                if (prevAxle.TryGetValue(c, out var pa) && c.Speed > 1f)
                {
                    var v = axle - pa; v.y = 0f;
                    if (v.magnitude > 0.01f)
                    {
                        double ang = Math.Abs(Vector3.SignedAngle(v.normalized, c.RoadForward, Vector3.up));
                        string sit = c.Via != null ? "box:" + c.Via.Kind : c.Sliding ? "slide" : c.DoingLine.Contains("turn") ? "other" : "straight";
                        string key = sit + " h=" + (c.Via != null ? c.Via.From.Heading : c.Heading);
                        Note(key, ang, $"t={t:F1} car{c.Id} v={c.Speed:F1} {c.DoingLine} viaS={(c.Via != null ? c.ViaS : -1):F1} s={c.S:F1} step={v.magnitude:F2}");
                        if (ang > 3.0 && spikes < 25) { spikes++; Console.WriteLine($"   spike {ang:F1}deg {key} t={t:F1} car{c.Id} v={c.Speed:F1} {c.DoingLine} viaS={(c.Via != null ? c.ViaS : -1):F1}/{(c.Via != null ? c.Via.Length : 0):F1} s={c.S:F1} road={(c.Road != null ? c.Road.Index : -1)} step={v.magnitude:F2} expected={c.Speed * Dt:F2}"); }
                    }
                }
                prevAxle[c] = axle;
                prevFwd[c] = c.RoadForward;
            }
        });
        foreach (var kv in acc.OrderBy(k => k.Key))
            Console.WriteLine($"   {kv.Key,-22} mean={kv.Value.sum / kv.Value.n:F2}deg max={kv.Value.max:F2}deg n={kv.Value.n} at {kv.Value.where}");
    }

    // Two-wheelers in with the cars. What is being asked is not how a bike LOOKS -
    // no stub here draws anything - but the one thing about it that touches the road
    // core: a body a third the width of a car and half its length on the same lanes,
    // in the same junction boxes, at the same kerbs. A narrow body that claims too
    // little would let a car through itself; one that claims too much is a car with a
    // motorcycle painted on it. Zero overlaps and zero belt hits, exactly as for cars.
    static void BikeScenario(int cars, int bikes, float seconds)
    {
        Reset();
        var xs = new float[] { -200, -60, 80, 220 };
        var zs = new float[] { -180, -40, 100, 240 };
        var net = Grid(xs, zs, signals: true);
        var all = new List<RoadCar>();
        int placed = 0;
        for (int round = 0; placed < cars && round < 40; round++)
        {
            bool any = false;
            foreach (var e in net.Edges)
            {
                if (placed >= cars) break;
                float s = 6f + round * 18f;
                if (s > e.Length - 12f) continue;
                any = true;
                all.Add(Spawn(net, e, s));
                placed++;
            }
            if (!any) break;
        }
        // the bikes, measured off the Palm City motorbike: 2.1 m long, 0.8 m across
        int laid = 0;
        for (int round = 0; laid < bikes && round < 40; round++)
        {
            bool any = false;
            foreach (var e in net.Edges)
            {
                if (laid >= bikes) break;
                float s = 15f + round * 26f;
                if (s > e.Length - 12f) continue;
                any = true;
                all.Add(Spawn(net, e, s, DriverProfile.Traffic, hl: 1.25f, hw: 0.45f));
                laid++;
            }
            if (!any) break;
        }
        Run($"bikes in traffic ({cars} cars, {bikes} bikes)", net, all, seconds);

        // and the lean those corners ask a bike for (RoadBike.LeanFor, which no stub
        // here can hold): the pace against the rate the nose comes round. Read down a
        // column for what one radius feels like as the pace rises; 30 degrees is the
        // cap, and a bike upright in a junction is a bike that has stopped.
        Console.WriteLine("   lean asked for (deg), by corner radius:");
        foreach (float v in new[] { 3f, 5.5f, 8f, 12f })
        {
            var row = new List<string>();
            foreach (float radius in new[] { 8f, 14f, 25f, 60f })
            {
                float yaw = v / radius;                       // rad/s round that circle
                float lean = MathF.Atan(v * yaw / 9.81f) * Mathf.Rad2Deg;
                row.Add($"r{radius:F0}->{Math.Min(30f, lean):F0}");
            }
            Console.WriteLine($"      {v,4:F1} m/s: " + string.Join("  ", row));
        }
    }

    static void Main(string[] args)
    {
        string only = args.Length > 0 ? args[0] : "all";
        if (only == "small") GridTraffic(12, true, 200f);
        if (only == "perf") GridTraffic(200, true, 120f);
        if (only == "all" || only == "grid") { GridTraffic(100, true, 600f); GridTraffic(60, false, 300f); GridTraffic(120, true, 300f, blvd: true); }
        if (only == "all" || only == "crew") CrewScenario();
        if (only == "all" || only == "block") { BlockerScenario(DriverProfile.Traffic, "wreck in lane: traffic"); BlockerScenario(DriverProfile.Gangster, "wreck in lane: gangster"); BlockerScenario(DriverProfile.Hot, "wreck in lane: hot"); }
        if (only == "all" || only == "crown") CrownScenario();
        if (only == "all" || only == "headon") HeadOnScenario();
        if (only == "all" || only == "wedged") WedgedScenario();
        if (only == "all" || only == "uturn") UTurnScenario();
        if (only == "all" || only == "turnround") TurnRound.Run();
        if (only == "all" || only == "redlight") RedLight.Run();
        if (only == "all" || only == "standoff") StandoffScenario();
        if (only == "all" || only == "crab") CrabScenario();
        if (only == "all" || only == "bikes") BikeScenario(60, 20, 300f);
    }
}
