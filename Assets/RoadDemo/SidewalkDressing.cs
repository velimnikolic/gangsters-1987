using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>The prop vocabulary a street is dressed from. Any bag may be empty
    /// or any single prop null - the dressing simply never rolls it.
    ///
    /// What belongs on a city pavement, and what does not, was read off the
    /// POLYGON City demo scene (its 2.9 km of kerb) and the Palm City demo's
    /// own streets, prop by prop:
    ///
    ///   KERB, regular      - light poles on a beat, palms in grates (a tree cage
    ///                        round the trunk), nothing else planted
    ///   KERB, furniture    - the public litter bin, the mailbox, the hydrant,
    ///                        parking meters, the pay phone, the newspaper box,
    ///                        a few bike hoops: what a 1987 American street keeps
    ///                        at its kerb, one piece every twenty-odd metres
    ///   FRONTAGE           - nearly bare. A bench now and then with a bin at its
    ///                        end, the building's own bin with its bags, a planter
    ///                        or two, a utility cabinet; the City demo carries
    ///                        under two of those per hundred metres of wall
    ///   TERRACE            - a cafe's tables and chairs, ONLY in front of a cafe,
    ///                        a diner, a restaurant: never on the pavement of an
    ///                        apartment block or a warehouse
    ///
    /// Saplings and small palms (bushes at shoulder height), hedges, dumpsters,
    /// wall-mounted meter boards laid on the ground: not street furniture, not rolled.</summary>
    public sealed class StreetProps
    {
        public List<GameObject> Grates, Palms, Lamps, KerbBins, WallBins, Benches, Planters,
            Powerboxes, Chairs, Tables, Umbrellas;
        public GameObject TreeCage, Banner, Bag, BagOpen, Mailbox, Newsstand, BikeStand, Hydrant,
            Meter, PayPhone, MenuStand;
    }

    /// <summary>How a sidewalk is furnished. A 5 m pavement is read as three bands
    /// and never as one:
    ///
    ///   kerb strip   - palms in their grates, lamp posts, hydrants, bike hoops,
    ///                  a mailbox, parking meters, a pay phone, a news box:
    ///                  everything that belongs to the road, seated with its
    ///                  road-side edge against the kerb
    ///   the walk     - nothing. The middle of the pavement is what people walk
    ///                  down, and every prop is refused the ground that would
    ///                  leave less than a passing width of it
    ///   the frontage - benches with their backs to the wall, the building's bin
    ///                  and bags, a planter, a cabinet: the building's furniture,
    ///                  seated against the building line - and a cafe's terrace
    ///                  where, and only where, there is a cafe behind it
    ///
    /// Where a prop goes is decided by its measured footprint (SidewalkPlan), not
    /// by a guessed offset, so a wide grate and a thin post each sit right.
    ///
    /// Densities follow the POLYGON City demo: a light pole every 26 m, a palm
    /// every 13 m (the Palm City main street), one piece of kerb furniture every
    /// 26 m or so, and a frontage group every 40 m on average. A stretch of
    /// empty pavement reads better than a stretch of scattered junk, and the
    /// groups keep away from the ones they would spoil: nobody sits on a bench
    /// with the rubbish at his elbow.</summary>
    public sealed class SidewalkDressing
    {
        /// Pavement that must stay walkable at every station, measured as the line
        /// a walker's centre can hold: with his shoulders that is a clear 1.8 m.
        public const float Corridor = 0.9f;
        /// Half a walker, shoulder to shoulder - what has to clear a prop, not
        /// the point his root sits on.
        public const float WalkRadius = 0.45f;
        /// Sidewalk, kerb to building line: the kit's 5 m tile and three tenths
        /// more, which is what it took to fit a palm grate, a walk and a bench.
        public const float Width = 6.5f;
        /// Where the kerb furniture is seated, measured from the road edge: the
        /// City kit's tile carries a gutter and a raised kerb stone out to 1.07 m
        /// before the pavement proper begins, and a grate or a post seated any
        /// nearer the road stands in the gutter (which is where they stood).
        public const float KerbSeat = 1.1f;
        /// The trees a little further in than the posts and the bins.
        public const float TreeSeat = KerbSeat + 0.15f;

        /// The kerb's beat: palms on the odd stations (13 m), lamps on every other
        /// even one (26 m), the kerb furniture on the rest.
        public const float KerbStep = 6.5f;
        /// The frontage's stations - most of them left bare on purpose.
        public const float WallStep = 13f;

        public SidewalkPlan Plan;
        public Transform Geometry, Flora;
        /// Called for every bench laid, so the scene can seat people on it.
        public System.Action<Vector3, float> BenchLaid;
        /// Props ride this above the tile plane (the pavement top).
        public float Lift = 0.1f;

        /// <summary>A cafe front on this stretch: the metres along it the shop's
        /// facade spans. Its terrace is laid there and nowhere else.</summary>
        public struct Terrace
        {
            public float From, To;
        }

        StreetProps _p;
        Vector3 _start, _dir, _out;
        float _half, _len, _faceRoad, _alongRoad;

        // ------------------------------------------------------------------ entry

        /// <summary>Dress one side of one stretch of ordinary street. <paramref name="half"/>
        /// is the carriageway half width; the pavement runs from there outward.
        /// <paramref name="terraces"/> are the cafe fronts along it, if any.</summary>
        public void Dress(StreetProps props, Vector3 start, Vector3 dir, Vector3 outward,
            float len, float half, IList<Terrace> terraces = null)
        {
            _p = props;
            _start = start;
            _dir = dir;
            _out = outward;
            _len = len;
            _half = half;
            _faceRoad = YawOf(-outward);
            _alongRoad = YawOf(dir);
            _laid.Clear();
            KerbStrip();
            // the terraces go down before the frontage, so the bins and benches
            // that follow keep their distance from the tables
            if (terraces != null)
                for (int i = 0; i < terraces.Count; i++) LayTerrace(terraces[i]);
            Frontage();
        }

        // ------------------------------------------------------------------ the kerb

        void KerbStrip()
        {
            // stations on the beat, centred on the stretch, none nearer than 5 m
            // to a corner - the corner's own furniture stands there
            int n = Mathf.FloorToInt((_len - 10f) / KerbStep) + 1;
            if (n < 1) return;
            float first = (_len - (n - 1) * KerbStep) * 0.5f;
            // which even stations carry the lamps: rolled per stretch, so the
            // two sides of a street and the next block along are not copies
            int lampPhase = Random.value < 0.5f ? 0 : 2;

            for (int slot = 0; slot < n; slot++)
            {
                float t = first + slot * KerbStep;
                if (slot % 2 == 1)
                {
                    // every other station is a tree, on the beat: a street is
                    // PLANTED, at a spacing you can read down the block, not
                    // scattered - the palm city's main street, a palm every 12.5 m
                    if (Random.value < 0.85f) Tree(t);
                    continue;
                }
                if (slot % 4 == lampPhase) Lamp(t);
                else KerbFurniture(t);
            }
        }

        /// A palm in its pavement grate, with a tree cage round the trunk - the triple
        /// the palm city's own kerbs carry.
        bool Tree(float t)
        {
            if (!Any(_p.Grates) || !Any(_p.Palms)) return false;
            var grate = Pick(_p.Grates);
            float lie = Random.Range(0, 4) * 90f;
            // the grate is the wide half of the pair and it is paving, not an
            // obstacle, so it is what gets seated against the kerb - a little way
            // in from it, clear of the kerb stone
            if (!Seat(grate, lie, t, _half + TreeSeat, false, out var pos)) return false;
            if (!Stand(grate, pos, lie, Geometry)) return false;
            // the palm stands INSIDE the grate - it shares that ground on
            // purpose, which is the whole point of a grate
            var palm = Place(Pick(_p.Palms), pos, Random.value * 360f, Flora, nest: true);
            if (palm == null) return false;
            CorePavement.SizePavementPalm(palm.transform);
            if (_p.TreeCage != null)
                Stand(_p.TreeCage, pos, lie, Geometry, nest: true);
            return true;
        }

        /// The light pole, head out over the road; a pair of banners hung on it
        /// some of the time, and what gathers at a lamp post - a litter bin, the
        /// mailbox, a hydrant - a pace or two along the kerb from it.
        void Lamp(float t)
        {
            if (!Any(_p.Lamps)) return;
            var lamp = Pick(_p.Lamps);
            if (!SeatKerb(lamp, _faceRoad, t, out var pos)) return;
            var post = Place(lamp, pos, _faceRoad, Geometry);
            if (post == null) return;

            if (_p.Banner != null && Random.value < 0.35f)
                Object.Instantiate(_p.Banner, pos + Vector3.up * 3.61f,
                    Quaternion.Euler(0f, _faceRoad + 180f, 0f), post.transform);

            if (Random.value < 0.35f)
            {
                float at = t + (Random.value < 0.5f ? -1.7f : 1.7f);
                float r = Random.value;
                if (r < 0.55f && Any(_p.KerbBins)) Kerb(Pick(_p.KerbBins), _faceRoad + 180f, at, Geometry);
                else if (r < 0.85f && _p.Mailbox != null) Kerb(_p.Mailbox, _faceRoad, at, Geometry);
                else if (_p.Hydrant != null) Kerb(_p.Hydrant, Random.value * 360f, at, Geometry);
            }
        }

        /// What a street keeps at its kerb between the lamps, at the City demo's
        /// rates: a bin a block, a mailbox or a hydrant every few blocks, now and
        /// then a pair of meters, a phone, a news box, a few bike hoops - and as
        /// often as not nothing at all, which is most of any real kerb.
        void KerbFurniture(float t)
        {
            float at = t + Random.Range(-0.8f, 0.8f);
            float r = Random.value;
            if (r < 0.30f && Any(_p.KerbBins))
                Kerb(Pick(_p.KerbBins), _faceRoad + 180f, at, Geometry);
            else if (r < 0.40f && _p.Mailbox != null)
                Kerb(_p.Mailbox, _faceRoad, at, Geometry);
            else if (r < 0.50f && _p.Hydrant != null)
                Kerb(_p.Hydrant, Random.value * 360f, at, Geometry);
            else if (r < 0.58f && _p.Meter != null)
            {
                // one meter a parking bay, the bays 5.2 m long
                Kerb(_p.Meter, _faceRoad, at - 2.6f, Geometry);
                Kerb(_p.Meter, _faceRoad, at + 2.6f, Geometry);
            }
            else if (r < 0.66f && _p.PayPhone != null)
                Kerb(_p.PayPhone, _faceRoad + 180f, at, Geometry);
            else if (r < 0.72f && _p.Newsstand != null)
                Kerb(_p.Newsstand, _faceRoad + 180f, at, Geometry);
            else if (r < 0.78f && _p.BikeStand != null)
            {
                int hoops = Random.Range(2, 4);
                for (int k = 0; k < hoops; k++)
                    Kerb(_p.BikeStand, _alongRoad, at + (k - (hoops - 1) * 0.5f) * 0.9f, Geometry);
            }
            // and otherwise bare kerb
        }

        // ------------------------------------------------------------------ the frontage

        enum Furn { Bench, Refuse, Cafe, Green, Utility }

        readonly List<(Furn kind, float t)> _laid = new List<(Furn, float)>();

        // How far apart two kinds have to stand. The point of the table is the
        // bench: a bench wants clear air on both sides of it, and rubbish wants
        // to be somewhere nobody is sitting or eating.
        static float Apart(Furn a, Furn b)
        {
            if (a > b) { var s = a; a = b; b = s; }
            if (a == b)
                return a == Furn.Green ? 3f : a == Furn.Cafe ? 8f : a == Furn.Bench ? 14f : 7f;
            if (a == Furn.Bench && b == Furn.Refuse) return 11f;
            if (a == Furn.Bench && b == Furn.Cafe) return 5f;
            if (a == Furn.Bench && b == Furn.Utility) return 4f;
            if (a == Furn.Refuse && b == Furn.Cafe) return 14f;
            if (a == Furn.Cafe && b == Furn.Utility) return 6f;
            return 2f;
        }

        bool Welcome(Furn kind, float t)
        {
            for (int i = 0; i < _laid.Count; i++)
                if (Mathf.Abs(_laid[i].t - t) < Apart(kind, _laid[i].kind)) return false;
            return true;
        }

        void Frontage()
        {
            int n = Mathf.FloorToInt((_len - 10f) / WallStep) + 1;
            if (n < 1) return;
            float first = (_len - (n - 1) * WallStep) * 0.5f;
            for (int slot = 0; slot < n; slot++)
            {
                // most of any frontage is bare wall, on purpose
                if (Random.value > 0.33f) continue;
                float at = first + slot * WallStep + Random.Range(-2.5f, 2.5f);
                float r = Random.value;
                Furn kind = r < 0.42f ? Furn.Bench
                    : r < 0.64f ? Furn.Refuse
                    : r < 0.84f ? Furn.Green
                    : Furn.Utility;
                if (!Welcome(kind, at)) continue;
                if (Lay(kind, at)) _laid.Add((kind, at));
            }
        }

        bool Lay(Furn kind, float t)
        {
            switch (kind)
            {
                case Furn.Bench: return LayBench(t);
                case Furn.Refuse: return LayRefuse(t);
                case Furn.Green: return LayGreen(t);
                default: return LayUtility(t);
            }
        }

        // A bench with its back to the building, looking out at the street. A
        // litter bin is allowed at the end of it - three good paces off, and a
        // public bin, never a heap of bags.
        bool LayBench(float t)
        {
            if (!Any(_p.Benches)) return false;
            var bench = Pick(_p.Benches);
            if (!SeatWall(bench, _faceRoad, t, out var pos)) return false;
            if (!Stand(bench, pos, _faceRoad, Geometry)) return false;
            BenchLaid?.Invoke(pos, _faceRoad);
            if (Random.value < 0.45f && Any(_p.KerbBins))
            {
                var bin = Pick(_p.KerbBins);
                float side = Random.value < 0.5f ? -1f : 1f;
                if (SeatWall(bin, _faceRoad, t + side * 3.4f, out var bp))
                    Stand(bin, bp, _faceRoad, Geometry);
            }
            return true;
        }

        // The building's rubbish: its own bin against the wall and a bag or two
        // heaped against it, all of it out of the way of the walk.
        bool LayRefuse(float t)
        {
            bool any = false;
            if (Any(_p.WallBins))
            {
                var bin = Pick(_p.WallBins);
                float yaw = _faceRoad + Random.Range(-20f, 20f);
                if (SeatWall(bin, yaw, t, out var pos))
                    any |= Stand(bin, pos, yaw, Geometry);
            }
            if (_p.Bag != null)
            {
                int bags = Random.Range(1, 3);
                for (int k = 0; k < bags; k++)
                {
                    var bag = Random.value < 0.7f ? _p.Bag : (_p.BagOpen ?? _p.Bag);
                    float yaw = Random.value * 360f;
                    // bags lean on each other and on the bin: a heap, not a row
                    if (SeatWall(bag, yaw, t + Random.Range(0.9f, 2.2f), out var pos))
                        any |= Stand(bag, pos, yaw, Geometry, nest: true);
                }
            }
            return any;
        }

        // Greenery on a pavement is a TREE at the kerb (KerbStrip) and otherwise a
        // planter: a box with a plant in it, standing against the frontage where a
        // shopkeeper would put one. No hedges - a hedge on a city pavement is a
        // wall of leaves across ground that is there to be walked on.
        bool LayGreen(float t)
        {
            if (!Any(_p.Planters)) return false;
            int pieces = Random.Range(1, 3);
            float at = t;
            bool any = false;
            for (int k = 0; k < pieces; k++)
            {
                var piece = Pick(_p.Planters);
                float step = SidewalkPlan.Reach(piece, _alongRoad, _dir, out _) * 2f;
                if (step < 0.2f) step = 1f;
                if (!SeatWall(piece, _alongRoad, at + step * 0.5f, out var pos)) break;
                if (!Stand(piece, pos, _alongRoad, Flora)) break;
                any = true;
                at += step + 0.25f;   // clear of the pad the overlap test allows
            }
            return any;
        }

        // The utility cabinet against the wall - the free-standing kind.
        bool LayUtility(float t)
        {
            if (!Any(_p.Powerboxes)) return false;
            var box = Pick(_p.Powerboxes);
            if (!SeatWall(box, _faceRoad, t, out var pos)) return false;
            return Stand(box, pos, _faceRoad, Geometry);
        }

        // ------------------------------------------------------------------ the terrace

        // A sidewalk cafe up against its own frontage: a table or two along the
        // shop's width with the chairs round the street side of each, an umbrella
        // over most of them, the menu board out at one end for the passers-by.
        void LayTerrace(Terrace terrace)
        {
            float from = Mathf.Max(terrace.From, 3f), to = Mathf.Min(terrace.To, _len - 3f);
            float width = to - from;
            if (width < 4f || !Any(_p.Tables) || !Any(_p.Chairs)) return;
            float centre = (from + to) * 0.5f;
            bool any;
            if (width >= 9f)
            {
                float off = Mathf.Min(width * 0.25f, 4f);
                any = LayCafeTable(centre - off);
                any |= LayCafeTable(centre + off);
            }
            else any = LayCafeTable(centre);
            if (!any) return;
            _laid.Add((Furn.Cafe, centre));

            if (_p.MenuStand != null)
            {
                float end = Random.value < 0.5f ? from + 0.9f : to - 0.9f;
                if (SeatWall(_p.MenuStand, _alongRoad, end, out var mp))
                    Stand(_p.MenuStand, mp, _alongRoad + Random.Range(-15f, 15f), Geometry);
            }
        }

        bool LayCafeTable(float t)
        {
            var table = Pick(_p.Tables);
            // the table's long side runs along the wall, whichever way the model
            // is cut, so it takes the frontage and not the walk
            float yaw = _faceRoad;
            if (SidewalkPlan.Reach(table, yaw, _out, out _) > SidewalkPlan.Reach(table, yaw, _dir, out _) + 0.2f)
                yaw += 90f;
            yaw += Random.Range(-8f, 8f);
            if (!SeatWall(table, yaw, t, out var pos)) return false;
            if (!Stand(table, pos, yaw, Geometry)) return false;

            // chairs on the arc that faces the street, each just off the table's
            // own edge in its direction, so none of them ends up wedged between
            // the table and the wall or stood inside the table top
            int chairs = Random.Range(2, 4);
            for (int k = 0; k < chairs; k++)
            {
                float ang = _faceRoad + Mathf.Lerp(-70f, 70f, chairs == 1 ? 0.5f : k / (chairs - 1f));
                var dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                float radius = SidewalkPlan.Reach(table, yaw, dir, out _) + 0.45f;
                Stand(Pick(_p.Chairs), pos + dir * radius, ang + 180f, Geometry);
            }
            // the umbrella stands THROUGH the table, which is where an umbrella goes
            if (Random.value < 0.7f && Any(_p.Umbrellas))
                Stand(Pick(_p.Umbrellas), pos, Random.value * 360f, Flora, nest: true);
            return true;
        }

        // ------------------------------------------------------------------ seating

        /// The spot that puts a prop's road-side edge just clear of the kerb stone.
        bool SeatKerb(GameObject prefab, float yaw, float t, out Vector3 pos)
            => Seat(prefab, yaw, t, _half + KerbSeat, false, out pos);

        /// The spot that puts a prop's back against the building line.
        bool SeatWall(GameObject prefab, float yaw, float t, out Vector3 pos)
            => Seat(prefab, yaw, t, _half + Width - 0.25f, true, out pos);

        bool Seat(GameObject prefab, float yaw, float t, float edge, bool backToWall, out Vector3 pos)
        {
            pos = default;
            if (prefab == null) return false;
            float reach = SidewalkPlan.Reach(prefab, yaw, _out, out float offset);
            float centre = backToWall ? edge - reach : edge + reach;
            float lat = centre - offset;
            pos = _start + _dir * t + _out * lat + Vector3.up * Lift;
            return true;
        }

        bool Kerb(GameObject prefab, float yaw, float t, Transform parent)
            => SeatKerb(prefab, yaw, t, out var pos) && Stand(prefab, pos, yaw, parent);

        // ------------------------------------------------------------------ standing

        /// <summary>Stand a prop, if the ground is free and enough pavement is left
        /// to walk down once it is there. Refused props simply never appear.
        /// <paramref name="nest"/> is for the pairs that SHARE ground by design -
        /// the palm in its grate, the umbrella over its table, bags heaped against
        /// each other - which would otherwise refuse one another.</summary>
        public bool Stand(GameObject prefab, Vector3 pos, float yaw, Transform parent, bool nest = false)
            => Place(prefab, pos, yaw, parent, nest) != null;

        /// <summary>As <see cref="Stand"/>, handing back what was stood - or null
        /// where the ground refused it.</summary>
        public GameObject Place(GameObject prefab, Vector3 pos, float yaw, Transform parent, bool nest = false)
        {
            if (prefab == null) return null;
            if (!SidewalkPlan.Footprint(prefab, pos, yaw, out var box))
                return Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
            if (!nest && !Plan.Free(box, 0.06f)) return null;
            Plan.Take(box);
            if (box.Solid && !CorridorHolds(pos, box))
            {
                Plan.Pop();
                return null;
            }
            return Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
        }

        // Enough bare pavement to walk down, over the whole length the prop covers.
        bool CorridorHolds(Vector3 pos, in SidewalkPlan.Box box)
        {
            float t = Vector3.Dot(pos - _start, _dir);
            float reach = Mathf.Abs(Vector2.Dot(box.Ax, new Vector2(_dir.x, _dir.z))) * box.H.x +
                          Mathf.Abs(Vector2.Dot(box.Az, new Vector2(_dir.x, _dir.z))) * box.H.y;
            float from = t - reach - 0.3f, to = t + reach + 0.3f;
            float inner = _half + KerbSeat, outer = _half + Width - 0.15f;

            for (float s = from; s <= to + 0.01f; s += 0.9f)
            {
                float best = 0f, run = 0f;
                for (float lat = inner; lat <= outer; lat += 0.2f)
                {
                    var p = _start + _dir * s + _out * lat;
                    if (Plan.Occupied(new Vector2(p.x, p.z), WalkRadius)) run = 0f;
                    else
                    {
                        run += 0.2f;
                        if (run > best) best = run;
                    }
                }
                if (best < Corridor) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ bits

        public static float YawOf(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        static bool Any(List<GameObject> l) => l != null && l.Count > 0;
        static GameObject Pick(List<GameObject> l) => l[Random.Range(0, l.Count)];
    }
}
