using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // What the city's pavements carry, and how much of them is left to walk on.
    //
    // Every prop the builder lays claims its measured footprint in _plan; the
    // sidewalk dressing asks that plan before it stands anything, and refuses
    // whatever would overlap what is already there or narrow the walk past a
    // passing width. Once the pedestrian graph exists, the same plan is read
    // back into each stretch as the free lateral slots along it, so the walkers
    // step round a palm or a bin instead of through it.
    public partial class RoadDemoBuilder
    {
        readonly SidewalkPlan _plan = new SidewalkPlan();
        SidewalkDressing _dressing;

        void PrepareDressing()
        {
            _dressing = new SidewalkDressing
            {
                Plan = _plan,
                Geometry = _geometry,
                Flora = _flora,
                BenchLaid = (pos, yaw) => _pendingBenches.Add((pos, yaw)),
            };

            // the turning room on every corner and the mouth of every crossing:
            // ground no prop may take, whatever else the dressing wants to do. The
            // walk line runs down the middle of the pavement (BuildPedGraph's Off),
            // and the corner slab is as deep as the pavement is wide.
            float Off = Sidewalk * 0.5f;
            float k = Sidewalk / 5f;
            foreach (var n in _nodes)
                foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, 1f), (-1f, -1f), (1f, -1f) })
                {
                    float bx = sx > 0f ? n.XMax : n.XMin;
                    float bz = sz > 0f ? n.ZMax : n.ZMin;
                    Vector3 C(float ox, float oz) => new Vector3(bx + sx * ox, 0.1f, bz + sz * oz);
                    _plan.Reserve(C(1.4f * k, Off), 0f, new Vector2(1.6f * k, 1.05f * k));
                    _plan.Reserve(C(Off, 1.4f * k), 0f, new Vector2(1.05f * k, 1.6f * k));
                }

            // and the ground in front of every door a civilian walks in at: a
            // hedge run laid across a threshold is a hedge people walk through
            foreach (var (pos, outward) in _pendingDoors)
                _plan.Reserve(pos + outward * 1.6f, SidewalkDressing.YawOf(outward),
                    new Vector2(1.2f, 1.8f));
        }

        StreetProps _streetProps;

        StreetProps Vocabulary()
        {
            return _streetProps ?? (_streetProps = new StreetProps
            {
                Grates = _grates, Palms = _palms, Lamps = _lamps,
                KerbBins = _bins, WallBins = _wallBins, Benches = _benches, Planters = _planters,
                Powerboxes = _powerboxes,
                Chairs = _chairs, Tables = _tables, Umbrellas = _umbrellas,
                TreeCage = _treeCage, Banner = _banner,
                Bag = _bag, BagOpen = _bagOpen, Mailbox = _mailbox, Newsstand = _newsstand,
                BikeStand = _bikeStand, Hydrant = _hydrant,
                Meter = _meter, PayPhone = _payPhone, MenuStand = _menuStand,
            });
        }

        // --------------------------------------------------------------- the cafe fronts

        // A sidewalk cafe only in front of a cafe. The storefronts the blocks were
        // rolled with (BlockFabric's commerce) stand inside the bakes under their
        // kit names; the ones that serve food are looked up once and each stretch
        // asks which of them front it - facade on the building line, facing the
        // street - so the dressing can lay the tables there and nowhere else.
        struct CafeFront
        {
            public Bounds Box;       // world
            public Vector3 Facing;   // the facade's outward normal, world
        }

        static readonly string[] FoodFronts =
        {
            "building-cafe", "building-coffeeshop", "building-restaurant", "building-burger-joint",
        };

        List<CafeFront> _cafeFronts;
        readonly Dictionary<string, FacadeFinder.Side> _facadeSides = new Dictionary<string, FacadeFinder.Side>();

        List<CafeFront> CafeFronts()
        {
            if (_cafeFronts != null) return _cafeFronts;
            _cafeFronts = new List<CafeFront>();
            if (_blocks == null) return _cafeFronts;
            foreach (var t in _blocks.GetComponentsInChildren<Transform>())
            {
                string kind = null;
                foreach (var name in FoodFronts)
                    if (t.name.StartsWith(name)) { kind = name; break; }
                if (kind == null) continue;

                // which side the shop's front is on, measured once per kit prefab:
                // the kit says +Z, but the coffee shop was baked the other way round
                if (!_facadeSides.TryGetValue(kind, out var side))
                    _facadeSides[kind] = side = FacadeFinder.FrontOf(t.gameObject, out _);
                var local = side switch
                {
                    FacadeFinder.Side.PlusX => Vector3.right,
                    FacadeFinder.Side.MinusZ => Vector3.back,
                    FacadeFinder.Side.MinusX => Vector3.left,
                    _ => Vector3.forward,
                };
                _cafeFronts.Add(new CafeFront
                {
                    Box = BoundsOf(t.gameObject),
                    Facing = t.TransformDirection(local).normalized,
                });
            }
            return _cafeFronts;
        }

        /// <summary>The cafe fronts along one side of one stretch, as the metres of
        /// it their facades span. Null when there are none.</summary>
        List<SidewalkDressing.Terrace> TerracesAlong(Vector3 start, Vector3 dir, Vector3 outward,
            float len, float half)
        {
            var fronts = CafeFronts();
            if (fronts.Count == 0) return null;
            List<SidewalkDressing.Terrace> found = null;
            float wall = half + SidewalkDressing.Width;
            foreach (var f in fronts)
            {
                // it faces this street ...
                if (Vector3.Dot(f.Facing, -outward) < 0.7f) continue;
                // ... its front stands on the building line (an awning may hang a
                // little over the pavement, a stoop may set the wall back a yard) ...
                var min = f.Box.min; var max = f.Box.max;
                float latA = Vector3.Dot(min - start, outward), latB = Vector3.Dot(max - start, outward);
                float near = Mathf.Min(latA, latB);
                if (near < wall - 1.5f || near > wall + 3.5f) continue;
                // ... and it spans enough of this stretch to sit outside
                float tA = Vector3.Dot(min - start, dir), tB = Vector3.Dot(max - start, dir);
                float from = Mathf.Max(0f, Mathf.Min(tA, tB)), to = Mathf.Min(len, Mathf.Max(tA, tB));
                if (to - from < 4f) continue;
                (found ??= new List<SidewalkDressing.Terrace>()).Add(
                    new SidewalkDressing.Terrace { From = from, To = to });
            }
            return found;
        }

        // ------------------------------------------------------- what is left to walk on

        /// <summary>Read the laid props back into the pedestrian graph: for every
        /// stretch, which lateral slots a walker can hold at each station along it.
        /// Sampled once here so the crowd pays nothing for it at runtime.</summary>
        void BuildWalkClearance()
        {
            foreach (var link in _pedLinks)
                link.SampleClearance(_plan, SidewalkDressing.WalkRadius);
            // and the same plan to whoever leaves the graph - a crew crossing the road
            // to a fight, the police walking up from the car - so they walk round the
            // furniture too, and not through it
            if (!WalkObstacles.Props.Contains(_plan)) WalkObstacles.Props.Add(_plan);
        }
    }
}
