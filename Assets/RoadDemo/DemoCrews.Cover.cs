using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// COVER, AND THE AMBUSH (EPIC 28).
    ///
    /// One oracle, asked from three places and never duplicated:
    ///
    /// * <see cref="CoverNear"/> - what a man in a fight asks, wired onto
    ///   <see cref="CrewWalker.FindCover"/>. Inside his gun's reach it searches round
    ///   HIM, as it always did; out of reach it searches round a point on the fire
    ///   line, so the flank is found BEFORE the first round rather than three seconds
    ///   after it (the closing shot is now only the fallback for an empty street).
    /// * <see cref="FlankAround"/> - a flank round a named place, facing a named
    ///   threat. What the ambush is dealt from, and what a man whose car drove off
    ///   asks for again.
    /// * <see cref="OrderAmbush"/> - the player's right click on a bin or on somebody
    ///   else's parked car: one man takes it, the rest take the furniture round it,
    ///   and they lie in wait.
    ///
    /// The two sources of cover are what they always were - cars stood still
    /// (<see cref="StreetTraffic.Users"/>, RoadSpeed at or under
    /// <see cref="StoodStill"/>: parked NPC cars, traffic held at a light, a rival
    /// crew's motor) and the pavement's furniture (<see cref="WalkObstacles.PropsNear"/>).
    /// A prop has no identity here, only a footprint: the size policy
    /// (<see cref="PropCoverMinHalf"/> / <see cref="PropCoverMaxHalf"/>, no
    /// <see cref="SidewalkPlan.Box.Tall"/>) is the whole of the sorting.
    /// </summary>
    public partial class DemoCrews
    {
        const float CoverReach = 10f;         // the furthest he will go to get behind something
        const float CoverApart = 0.8f;        // two men do not share one flank
        // Slimmer than MinHalf on its short side is a post, not cover; wider than
        // MaxHalf is a wall or a lot, not furniture. CoverDemo reads the same pair.
        internal const float PropCoverMinHalf = 0.22f;
        internal const float PropCoverMaxHalf = 3f;

        /// <summary>A road user this slow is furniture: the flank of a parked car, of
        /// one waiting out a red, of a mob's motor left at the kerb. Above it the tin
        /// is going somewhere and takes the flank with it (COVER-005).</summary>
        internal const float StoodStill = 0.5f;

        /// <summary>Closer than this and a man SHOOTS. He does not walk round his own
        /// enemy to get behind a bin, and he does not turn his back on one at arm's
        /// length to reach a car's flank - at this range the fight is already had.</summary>
        const float PointBlank = 4f;

        /// <summary>How near the mark the flank on the fire line is looked for: the
        /// centre of the search sits this fraction of the gun's reach from the mark,
        /// so the man arrives at a flank he can actually shoot from rather than at one
        /// he has to leave again. The walker's own closing figure, deliberately - the
        /// two are the same idea seen from either end.</summary>
        const float CoverRangeFactor = 0.8f;

        /// <summary>How far round the fire-line centre the search runs. Never less than
        /// this, whatever the gun: a pistol fight still has a street's worth of
        /// furniture in it.</summary>
        const float CoverLineReach = 6f;

        /// <summary>How far round the clicked anchor an ambush is dealt. The men are a
        /// crew lying in wait behind one piece of street, not a picket line.</summary>
        internal const float AmbushSpread = 10f;

        /// <summary>How long an ambush nobody sprang holds before the men stand up and
        /// are their crew's again - the door post's lease (CrewWalker.WatchLease), twice
        /// over, because waiting is the whole of the order.</summary>
        internal const float AmbushLease = 240f;

        /// <summary>How near a man lying in wait has to be before anybody sees him. A
        /// range and not a raycast, deliberately: the sight model is walls only
        /// (WalkObstacles.Sees) and it is not going to learn about bins for this. The
        /// moment his crew has a fight, the ordinary rules apply again.</summary>
        internal const float LurkSeen = 8f;

        // Somewhere for a pressed man to get behind: the far flank of a car stood still,
        // or of a bin, a planter, a phone box - anything of the street's furniture that
        // stands on the far side of him from the man shooting.
        static readonly List<SidewalkPlan.Box> _coverBoxes = new List<SidewalkPlan.Box>();
        static readonly List<Vector3> _claimed = new List<Vector3>();
        readonly List<Vector3> _coverRoute = new List<Vector3>();

        /// <summary>Whose tin the last flank this oracle handed out belongs to, or null
        /// when it was a bin. Read by the caller on the frame it asks and never later:
        /// a man holds it so that a car pulling away takes its flank with it
        /// (COVER-005). A field rather than an out-parameter because the oracle is
        /// reached through a delegate the walker owns, and an audit probe that ignores
        /// it must not have to pass one.</summary>
        internal static IRoadUser LastCoverAnchor { get; private set; }

        // ------------------------------------------------------------ the flank itself

        /// <summary>The far flank of a car stood still: off its side away from
        /// <paramref name="awayFrom"/>, by a shoulder and a bit - clear of the body the
        /// walk keeps out of - and slid along that side toward
        /// <paramref name="from"/>.</summary>
        static Vector3 CarFlank(IRoadUser u, Vector3 from, Vector3 awayFrom, float y)
        {
            var c = u.RoadPosition;
            var f = u.RoadForward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) return new Vector3(float.NaN, y, float.NaN);
            f.Normalize();
            var right = Vector3.Cross(Vector3.up, f);
            float side = Vector3.Dot(c - awayFrom, right) >= 0f ? 1f : -1f;
            float along = Mathf.Clamp(Vector3.Dot(from - c, f), -u.HalfLength + 0.6f, u.HalfLength - 0.6f);
            var spot = c + right * side * (u.HalfWidth + WalkObstacles.Radius + 0.4f) + f * along;
            spot.y = y;
            return spot;
        }

        /// <summary>The same of a prop. A prop is a box on the ground (SidewalkPlan):
        /// take the face pointing away from <paramref name="awayFrom"/>, stand him off
        /// it by a shoulder, and slide him along that face toward where he already is -
        /// the car's `along`, in the box's own frame.</summary>
        static bool BoxFlank(in SidewalkPlan.Box b, Vector3 from, Vector3 awayFrom, float y,
            out Vector3 spot)
        {
            spot = default;
            var away = b.C - new Vector2(awayFrom.x, awayFrom.z);
            if (away.sqrMagnitude < 1e-4f) return false;
            float ax = Vector2.Dot(away, b.Ax), az = Vector2.Dot(away, b.Az);
            Vector2 n, slide;
            float ext, slideHalf;
            if (Mathf.Abs(ax) >= Mathf.Abs(az)) { n = b.Ax * Mathf.Sign(ax); ext = b.H.x; slide = b.Az; slideHalf = b.H.y; }
            else                                { n = b.Az * Mathf.Sign(az); ext = b.H.y; slide = b.Ax; slideHalf = b.H.x; }
            float room = Mathf.Max(0f, slideHalf - 0.2f);
            float along = Mathf.Clamp(Vector2.Dot(new Vector2(from.x, from.z) - b.C, slide), -room, room);
            var s2 = b.C + n * (ext + WalkObstacles.Radius + 0.35f) + slide * along;
            spot = new Vector3(s2.x, y, s2.y);
            return true;
        }

        /// <summary>Big enough to put between himself and a round, small enough to be
        /// furniture. The plan keeps no height, so this is all the sorting there is: a
        /// grate is not solid at all, a lamp post is too slim to hide a man. A TRUNK IS
        /// NOT COVER - a palm's box is the slice of it at knee height, so a man sent to
        /// its far flank ends up a metre from the pivot, under the canopy and inside the
        /// fronds, which is exactly what the player saw.</summary>
        internal static bool CoverSized(in SidewalkPlan.Box b) =>
            !b.Tall &&
            Mathf.Min(b.H.x, b.H.y) >= PropCoverMinHalf &&
            Mathf.Max(b.H.x, b.H.y) <= PropCoverMaxHalf;

        // ------------------------------------------------------------------ the search

        /// <summary>THE ONE SEARCH. Every flank in this game comes out of here.
        ///
        /// <paramref name="centre"/> and <paramref name="reach"/> say WHERE to look;
        /// <paramref name="awayFrom"/> says which face of a thing is its far one;
        /// <paramref name="minShot"/>/<paramref name="maxShot"/> say what the flank has
        /// to be worth once he is stood at it. The winner is the least walk from the
        /// man, and ties (within a stride's quarter) go to the flank nearer the mark.
        ///
        /// A spot is a spot A MAN IS SENT TO STAND AT, so it takes the canopy berth
        /// every other chosen spot in the town takes, it is not another man's already,
        /// and his feet have to be able to get to it.</summary>
        Vector3? SearchCover(CrewWalker man, Vector3 awayFrom, Vector3 centre, float reach,
            float maxWalk, float minShot, float maxShot)
        {
            LastCoverAnchor = null;
            if (man == null || man.Tf == null) return null;
            var p = man.Tf.position;
            Vector3? best = null;
            IRoadUser bestUser = null;
            float bestWalk = maxWalk, bestToMark = float.MaxValue;
            float reach2 = reach * reach;

            GatherClaims(man);

            foreach (var u in StreetTraffic.Users)
            {
                if (u.RoadSpeed > StoodStill) continue;
                var spot = CarFlank(u, p, awayFrom, p.y);
                if (float.IsNaN(spot.x)) continue;
                if ((spot - centre).sqrMagnitude > reach2) continue;
                if (!Take(man, spot, awayFrom, p, minShot, maxShot,
                          ref bestWalk, ref bestToMark)) continue;
                best = spot;
                bestUser = u;
            }

            // and the same of the pavement's furniture
            WalkObstacles.PropsNear(centre, reach, _coverBoxes);
            for (int i = 0; i < _coverBoxes.Count; i++)
            {
                var b = _coverBoxes[i];
                if (!CoverSized(b)) continue;
                if (!BoxFlank(b, p, awayFrom, p.y, out var spot)) continue;
                if ((spot - centre).sqrMagnitude > reach2) continue;
                if (!Take(man, spot, awayFrom, p, minShot, maxShot,
                          ref bestWalk, ref bestToMark)) continue;
                best = spot;
                bestUser = null;
            }

            LastCoverAnchor = best.HasValue ? bestUser : null;
            return best;
        }

        /// <summary>Is this candidate flank both usable and better than the best so
        /// far? The order of the tests is the cost of them: the cheap geometry first,
        /// the ground query next, and the walked route - which is several cells of A* -
        /// only for a flank that has already won.</summary>
        bool Take(CrewWalker man, Vector3 spot, Vector3 awayFrom, Vector3 from,
            float minShot, float maxShot, ref float bestWalk, ref float bestToMark)
        {
            float walk = Vector3.Distance(spot, from);
            if (walk > bestWalk + 0.25f) return false;
            float toMark = Vector3.Distance(spot, awayFrom);
            if (toMark < minShot || toMark > maxShot) return false;
            // least walk wins; a tie goes toward the mark
            bool better = walk < bestWalk - 0.25f ||
                          (walk <= bestWalk + 0.25f && toMark < bestToMark);
            if (!better) return false;
            if (WalkObstacles.Occupied(spot, WalkObstacles.Radius, WalkObstacles.CanopyBerth)) return false;
            if (Claimed(spot)) return false;
            if (!CoverReachable(from, spot)) return false;
            bestWalk = Mathf.Min(bestWalk, walk);
            bestToMark = toMark;
            return true;
        }

        // ------------------------------------------------- what a man in a fight asks

        /// <summary>Somewhere for a pressed man to get behind, against the man he is
        /// fighting. Wired onto <see cref="CrewWalker.FindCover"/>, so the outfit, the
        /// mobs and the police squads all fight the same way.
        ///
        /// A FLANK BEFORE THE FIRST ROUND (COVER-001). It used to look only round the
        /// man himself, within min(CoverReach, dist x 0.9), and only accept a flank
        /// with the mark inside the gun's reach - so a crew sent at a rival forty metres
        /// off was told "nothing", fell into the closing branch, and walked in FIRING ON
        /// THE MOVE; two seconds later the recheck found the bin at its elbow and off it
        /// went. That is the order the player saw: fire, then cover. Out of reach the
        /// search now runs round a point ON THE FIRE LINE - <see cref="CoverRangeFactor"/>
        /// of the gun's reach short of the mark - and the walk to that flank is silent
        /// (TickEngage's approach returns before the fire block). The closing shot
        /// survives only where the street has nothing.</summary>
        Vector3? CoverNear(CrewWalker man, Vector3 target)
        {
            if (man == null || man.Tf == null) return null;
            var p = man.Tf.position;
            float range = Mathf.Max(1f, man.Ballistics.Range);
            var toMark = target - p;
            toMark.y = 0f;
            float dist = toMark.magnitude;

            // POINT BLANK: he shoots. A man does not walk round his own enemy to reach
            // a bin, and one at arm's length is not somebody you take your eyes off.
            if (dist <= PointBlank) return null;

            if (dist <= range)
            {
                // inside his reach: round HIM, exactly as it always was. Never further
                // off than the fight itself - nobody sprints eight metres to a bin with
                // an enemy stood four away.
                float cap = Mathf.Min(CoverReach, Mathf.Max(3f, dist * 0.9f));
                return SearchCover(man, target, p, cap, cap, 3f, range);
            }

            return CoverToward(man, target, range, dist);
        }

        /// <summary>The flank ON THE FIRE LINE: the same oracle, searched from the point
        /// between the man and his mark at <see cref="CoverRangeFactor"/> of the gun's
        /// reach from the mark. A flank qualifies as it always did AND leaves the mark
        /// inside [3, range], so the first round leaves from behind something.
        ///
        /// It is also the leapfrog (COVER-002): a man whose held flank the mark has
        /// walked out of asks this again and takes the NEXT one toward him.</summary>
        Vector3? CoverToward(CrewWalker man, Vector3 target, float range, float dist)
        {
            var p = man.Tf.position;
            var toMan = p - target;
            toMan.y = 0f;
            if (toMan.sqrMagnitude < 1e-4f) return null;
            toMan.Normalize();

            // never past the man himself: the centre walks up the line toward the mark,
            // it does not go looking behind the man's own back
            var centre = target + toMan * Mathf.Min(range * CoverRangeFactor, dist);
            centre.y = p.y;
            float reach = Mathf.Max(CoverLineReach, range * 0.6f);
            float maxWalk = Vector3.Distance(p, centre) + reach;
            return SearchCover(man, target, centre, reach, maxWalk, 3f, range);
        }

        /// <summary>A flank round a named place, facing a named threat, with no gun
        /// range asked of it at all - the ambush's own deal, and what a man whose car
        /// pulled out from under him asks for again. Wired onto
        /// <see cref="CrewWalker.FindFlankAround"/> so the walker can re-ask without
        /// the arena having to watch him.</summary>
        Vector3? FlankAround(CrewWalker man, Vector3 centre, Vector3 threat, float reach)
        {
            if (man == null || man.Tf == null) return null;
            centre.y = man.Tf.position.y;
            float maxWalk = Vector3.Distance(man.Tf.position, centre) + reach;
            return SearchCover(man, threat, centre, reach, maxWalk, 0f, float.MaxValue);
        }

        /// <summary>A free flank is not useful if the man cannot reach it. Cover is
        /// selected only every few seconds, so pay for the same fixed-ground route
        /// proof his feet will use before replacing a valid fighting position with an
        /// unreachable one.</summary>
        bool CoverReachable(Vector3 from, Vector3 spot)
        {
            _coverRoute.Clear();
            return WalkRoute.Plan(from, spot, _coverRoute, false) &&
                   _coverRoute.Count > 0;
        }

        /// <summary>What the rest of the street is already behind: two men crowding one
        /// flank is one man in cover and one stood in the open beside him. A man LYING
        /// IN WAIT holds his flank the same way a man in a fight holds his, so the
        /// ambush's own deal cannot stack two men on one bin.</summary>
        void GatherClaims(CrewWalker except)
        {
            _claimed.Clear();
            foreach (var unit in Units)
                foreach (var m in unit.All())
                {
                    if (m == null || m == except || m.Dead) continue;
                    if (m.CoverSpot.HasValue) _claimed.Add(m.CoverSpot.Value);
                    else if (m.HeldCover.HasValue) _claimed.Add(m.HeldCover.Value);
                }
        }

        /// <summary>Is another man already behind this very flank?</summary>
        static bool Claimed(Vector3 spot)
        {
            for (int i = 0; i < _claimed.Count; i++)
                if ((_claimed[i] - spot).sqrMagnitude < CoverApart * CoverApart) return true;
            return false;
        }

        // --------------------------------------------------------------- the ambush

        /// <summary>The thing the player pointed at when he asked for a sacekusa: a
        /// piece of the street's furniture, or somebody else's car stood at the kerb.
        /// A footprint and nothing else - the anchor has no identity, exactly as a
        /// prop has none inside the oracle.</summary>
        public struct CoverAnchor
        {
            /// <summary>The tin, when it is a car. Null for a prop - and watched
            /// afterwards, because a car that drives off takes its flank with it.</summary>
            public IRoadUser Car;
            /// <summary>The footprint, when it is a prop.</summary>
            public SidewalkPlan.Box Box;
            /// <summary>Its middle, on the ground the men stand on.</summary>
            public Vector3 At;
            public bool IsCar;
            public bool Valid;

            /// <summary>What the card and the announcement call it. There is no name to
            /// be had - a prop is a footprint - so it is called what it is.</summary>
            public string Word => IsCar ? "the car" : "it";
        }

        /// <summary>The anchor under a click: a stood car whose body the pointer went
        /// through, or a prop whose footprint the ground point is inside.
        ///
        /// NPC PARKED CARS ARE FIRST-CLASS ANCHORS (the user's own words, 2026-09-02:
        /// "ne samo rivalskog, no NPC parkiranog auta"). Every road user stood still is
        /// one - the kerbside StoodCars, the forecourt's, a RoadCar parked or held at a
        /// red - and none of them opens a card: they are one click, like the ground.
        /// The outfit's own cars and a rival crew's never reach here; their own picks
        /// answer first (CrewOverlay.PickCarAt).</summary>
        public static bool AnchorUnder(Ray ray, Vector3 ground, out CoverAnchor anchor)
        {
            anchor = default;

            // the tin first: the pointer through the body at bonnet height, because a
            // ground point under a click on a car's roof lands well past the car. The
            // NEAREST such car and not the first in the list - the pointer crosses the
            // bonnet plane of every car on the street, and the one the player meant is
            // the one in front.
            float nearest = float.MaxValue;
            for (int i = 0; i < StreetTraffic.Users.Count; i++)
            {
                var u = StreetTraffic.Users[i];
                if (u == null || u.RoadSpeed > StoodStill) continue;
                var c = u.RoadPosition;
                var f = u.RoadForward;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) continue;
                f.Normalize();
                var bonnet = new Plane(Vector3.up, new Vector3(0f, c.y + 0.8f, 0f));
                if (!bonnet.Raycast(ray, out float enter)) continue;
                if (enter >= nearest) continue;
                var rel = ray.GetPoint(enter) - c;
                rel.y = 0f;
                var right = Vector3.Cross(Vector3.up, f);
                if (Mathf.Abs(Vector3.Dot(rel, f)) > u.HalfLength + 0.3f) continue;
                if (Mathf.Abs(Vector3.Dot(rel, right)) > u.HalfWidth + 0.3f) continue;
                nearest = enter;
                anchor = new CoverAnchor { Car = u, IsCar = true, At = c, Valid = true };
            }
            if (anchor.Valid) return true;

            // and the furniture: the ground point inside a box that IS cover
            WalkObstacles.PropsNear(ground, 1.5f, _coverBoxes);
            var g = new Vector2(ground.x, ground.z);
            float bestArea = float.MaxValue;
            for (int i = 0; i < _coverBoxes.Count; i++)
            {
                var b = _coverBoxes[i];
                if (!CoverSized(b)) continue;
                var d = g - b.C;
                if (Mathf.Abs(Vector2.Dot(d, b.Ax)) > b.H.x + 0.25f) continue;
                if (Mathf.Abs(Vector2.Dot(d, b.Az)) > b.H.y + 0.25f) continue;
                // two boxes over one point: the smaller of them is the thing clicked
                float area = b.H.x * b.H.y;
                if (area >= bestArea) continue;
                bestArea = area;
                anchor = new CoverAnchor
                {
                    Box = b,
                    IsCar = false,
                    At = new Vector3(b.C.x, ground.y, b.C.y),
                    Valid = true,
                };
            }
            return anchor.Valid;
        }

        /// <summary>The nearest thing to get behind within reach of a point - the same
        /// two sources and the same size policy, with no pointer involved. What the lab
        /// asks when it plays the player with nobody at the mouse
        /// (BlockDemoMission.ambush).</summary>
        public static bool AnchorNear(Vector3 at, float reach, out CoverAnchor anchor)
        {
            anchor = default;
            float best = reach * reach;

            for (int i = 0; i < StreetTraffic.Users.Count; i++)
            {
                var u = StreetTraffic.Users[i];
                if (u == null || u.RoadSpeed > StoodStill) continue;
                float d = (u.RoadPosition - at).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                anchor = new CoverAnchor { Car = u, IsCar = true, At = u.RoadPosition, Valid = true };
            }

            WalkObstacles.PropsNear(at, reach, _coverBoxes);
            for (int i = 0; i < _coverBoxes.Count; i++)
            {
                var b = _coverBoxes[i];
                if (!CoverSized(b)) continue;
                var middle = new Vector3(b.C.x, at.y, b.C.y);
                float d = (middle - at).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                anchor = new CoverAnchor { Box = b, IsCar = false, At = middle, Valid = true };
            }
            return anchor.Valid;
        }

        /// <summary>The same anchor made from a car the caller already has in its hand -
        /// the rival crew's motor its own card is open on. Invalid while the thing is
        /// moving: there is no flank on a car that is going somewhere.</summary>
        public static CoverAnchor AnchorOf(IRoadUser car)
        {
            if (car == null || car.RoadSpeed > StoodStill) return default;
            return new CoverAnchor { Car = car, IsCar = true, At = car.RoadPosition, Valid = true };
        }

        /// <summary>Why the crew will not lie in wait there. Null when it will.</summary>
        public string AmbushRefusal { get; private set; }

        /// <summary>THE AMBUSH (COVER-003). One man takes the thing the player pointed
        /// at, the rest take the furniture round it, and they lie in wait.
        ///
        /// Which face is the safe one is the whole of the order, so the threat is
        /// decided first: a rival his crew can see, else the nearest carriageway (they
        /// come down the street, so the men sit on the pavement side of the bin and
        /// watch the road), else the way the crew itself came in. A crew already in a
        /// fight that is clicked onto a prop is being told to fight from THERE - the
        /// live mark is the threat and the fight goes on from the new flanks.
        ///
        /// Single click walks, the double runs, exactly as everywhere.</summary>
        public bool OrderAmbush(Unit unit, CoverAnchor anchor, bool run)
        {
            AmbushRefusal = OrderRefusal = null;
            if (unit == null || unit.Wiped || !anchor.Valid) return false;
            if (unit.Surrendered) { AmbushRefusal = OrderRefusal = HandsUpRefusal; return false; }
            if (unit.Car != null || unit.Boarding != null)
            {
                // a crew in a motor is not a crew behind a bin; get them out first
                Unboard(unit, "an ambush order");
                if (unit.Car != null) Disembark(unit);
            }

            CallOffRaids(unit, "an ambush order");
            NoteRetask(unit);
            unit.Fleeing = false;
            unit.PendingDrive = null;
            unit.PendingAttack = null;
            unit.OrderedAt = Time.time;

            var threatDir = ThreatToward(unit, anchor.At);
            // a point out along the threat, which is all the oracle wants: it asks
            // which face of a thing points AWAY from a place, not how far off it is
            var threatAt = anchor.At + threatDir * 30f;

            // EVERY MAN ON HIS FEET, armed or not: a hood with nothing in his coat still
            // gets behind the bin rather than standing in the street beside it. Only the
            // men who are not on the pavement at all are left out - riding, indoors, or
            // out on a raid of their own.
            _ambushMen.Clear();
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Tf != null &&
                    man.Tf.gameObject.activeInHierarchy &&
                    !IsAboard(man) && !man.Riding && !OnRaid(man) && !man.Panicked)
                    _ambushMen.Add(man);
            if (_ambushMen.Count == 0)
            { AmbushRefusal = OrderRefusal = "Nobody of the crew is on his feet"; return false; }

            // THE MAN NEAREST IT TAKES IT. The clicked thing's own far face, by the same
            // geometry every flank in the game is cut with.
            GatherClaims(null);
            var first = NearestOfList(anchor.At);
            var held = Vector3.zero;
            bool haveHeld = false;
            if (first != null && first.Tf != null)
                haveHeld = anchor.IsCar
                    ? Held(CarFlank(anchor.Car, first.Tf.position, threatAt, anchor.At.y), out held)
                    : (BoxFlank(anchor.Box, first.Tf.position, threatAt, anchor.At.y, out var face) &&
                       Held(face, out held));

            int dealt = 0;
            if (haveHeld)
            {
                var taker = NearestOfList(held) ?? first;
                if (taker != null && taker.HoldCover(held, threatDir, anchor.At, anchor.Car, run))
                {
                    _ambushMen.Remove(taker);
                    dealt++;
                }
            }

            // and the rest round it, by the ordinary oracle - Claimed keeps them off
            // one another's flank, and the men already dealt are claimed because their
            // HeldCover is set (SearchCover reads it)
            for (int i = _ambushMen.Count - 1; i >= 0; i--)
            {
                var man = _ambushMen[i];
                var spot = FlankAround(man, anchor.At, threatAt, AmbushSpread);
                if (!spot.HasValue) continue;
                var user = LastCoverAnchor;
                if (!man.HoldCover(spot.Value, threatDir, anchor.At, user, run)) continue;
                _ambushMen.RemoveAt(i);
                dealt++;
            }

            // NOBODY IS LEFT STANDING IN THE OPEN. A man the street had no flank for
            // crouches beside the nearest taken one, on its safe side.
            for (int i = _ambushMen.Count - 1; i >= 0; i--)
            {
                var man = _ambushMen[i];
                if (!BesideATakenFlank(unit, man, threatDir, out var beside)) continue;
                if (!man.HoldCover(beside, threatDir, anchor.At, null, run)) continue;
                _ambushMen.RemoveAt(i);
                dealt++;
            }

            if (dealt == 0)
            { AmbushRefusal = OrderRefusal = "No way to get behind it"; return false; }

            // A CREW ALREADY FIGHTING KEEPS ITS FIGHT. The click was manual cover, not a
            // ceasefire: Engage keeps the held flank when the mark is in reach of it
            // (CrewWalker.Engage), so the same men shoot the same mark from the new
            // places. A crew with nothing on goes quiet and waits.
            if (unit.TargetUnit == null)
                foreach (var man in unit.All())
                    if (man != null && !man.Dead) man.Disengage();

            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", unit.GangName);
                DriveTrace.Bool(sb, "car", anchor.IsCar);
                DriveTrace.Int(sb, "men", dealt);
                DriveTrace.Int(sb, "open", _ambushMen.Count);
                DriveTrace.Vec(sb, "at", anchor.At);
                DriveTrace.Vec(sb, "threat", anchor.At + threatDir);
                DriveTrace.Row("ambush", sb.ToString());
            }
            return true;
        }

        readonly List<CrewWalker> _ambushMen = new List<CrewWalker>();

        /// <summary>The man of the deal still without a place who stands nearest
        /// here.</summary>
        CrewWalker NearestOfList(Vector3 at)
        {
            CrewWalker best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _ambushMen.Count; i++)
            {
                var man = _ambushMen[i];
                if (man == null || man.Tf == null) continue;
                float d = (man.Tf.position - at).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = man;
            }
            return best;
        }

        /// <summary>A flank a man may actually be sent to: on the ground, clear of the
        /// canopy, and not somebody else's already.</summary>
        static bool Held(Vector3 spot, out Vector3 held)
        {
            held = spot;
            if (float.IsNaN(spot.x)) return false;
            if (WalkObstacles.Occupied(spot, WalkObstacles.Radius, WalkObstacles.CanopyBerth)) return false;
            return !Claimed(spot);
        }

        /// <summary>Where the trouble is coming from, as a heading out of the anchor:
        /// the crew's live mark or the nearest rival it can see, else the nearest
        /// carriageway, else the way the crew itself walked in.</summary>
        Vector3 ThreatToward(Unit unit, Vector3 at)
        {
            Vector3 way = Vector3.zero;

            // the fight it is already in, or one it can see
            var mark = unit.TargetUnit != null ? NearestStanding(unit.TargetUnit, at) : null;
            if (mark == null)
            {
                float bestD = SightRange * SightRange;
                foreach (var other in Units)
                {
                    if (other == unit || other.Wiped || other.IsPolice) continue;
                    if (other.Faction == unit.Faction) continue;
                    foreach (var b in other.All())
                    {
                        if (b == null || b.Dead || b.Tf == null || IsAboard(b)) continue;
                        float d = (b.Tf.position - at).sqrMagnitude;
                        if (d >= bestD) continue;
                        if (!InSight(at, b.Tf.position)) continue;
                        bestD = d;
                        mark = b;
                    }
                }
            }
            if (mark != null && mark.Tf != null) way = mark.Tf.position - at;

            // else the road: they come down the street, so the men sit on the pavement
            // side of the thing and watch the asphalt
            if (way.sqrMagnitude < 1e-4f)
            {
                float s = 0f;
                var road = LaneNet.Active != null ? LaneNet.Active.Locate(at, out s, out _, 40f) : null;
                if (road != null) way = (road.A + road.Axis * Mathf.Clamp(s, 0f, road.Length)) - at;
            }

            // and failing everything, the way they came in
            if (way.sqrMagnitude < 1e-4f) way = unit.Position - at;
            way.y = 0f;
            return way.sqrMagnitude > 1e-4f ? way.normalized : Vector3.forward;
        }

        /// <summary>Beside the nearest flank already taken, on the side of it away from
        /// the threat - the corner a man with no bin of his own gets.</summary>
        bool BesideATakenFlank(Unit unit, CrewWalker man, Vector3 threatDir, out Vector3 beside)
        {
            beside = default;
            GatherClaims(man);
            Vector3 nearest = default;
            float bestD = float.MaxValue;
            foreach (var m in unit.All())
            {
                if (m == null || m == man || m.Dead || !m.HeldCover.HasValue) continue;
                float d = (m.HeldCover.Value - man.Tf.position).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                nearest = m.HeldCover.Value;
            }
            if (bestD == float.MaxValue) return false;

            var side = Vector3.Cross(Vector3.up, threatDir).normalized;
            for (int i = 0; i < 3; i++)
            {
                var try1 = nearest + side * (CoverApart * 1.3f) - threatDir * (0.3f * i);
                var try2 = nearest - side * (CoverApart * 1.3f) - threatDir * (0.3f * i);
                if (Held(try1, out beside) && CoverReachable(man.Tf.position, beside)) return true;
                if (Held(try2, out beside) && CoverReachable(man.Tf.position, beside)) return true;
            }
            var back = nearest - threatDir * (CoverApart * 1.3f);
            if (Held(back, out beside) && CoverReachable(man.Tf.position, beside)) return true;
            beside = default;
            return false;
        }

        // --------------------------------------------------------- lying in wait

        /// <summary>Is anybody of this crew down behind something, waiting?</summary>
        internal static bool AnyLurking(Unit unit)
        {
            if (unit == null) return false;
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Lurking) return true;
            return false;
        }

        /// <summary>Cannot be seen from there: a man lying in wait, until his crew has
        /// FIRED. The one change the ambush makes to sight, and it is a RANGE - the
        /// model is walls only and is not going to learn about bins for this.
        ///
        /// It asks <see cref="CrewWalker.Hidden"/> and not `Lurking` on purpose: a man
        /// who has just been handed a mark has not shown himself, and reading the mark
        /// let the mob see the ambush during the beat between the crew acquiring and
        /// the first round leaving.</summary>
        internal static bool Concealed(CrewWalker man, Vector3 from) =>
            man != null && man.Hidden && man.Tf != null &&
            (man.Tf.position - from).sqrMagnitude > LurkSeen * LurkSeen;

        /// <summary>A round has left this crew: every man of it still holding a flank is
        /// visible again (COVER-004). Asked of the SHOOTER's own crew only - being shot
        /// at does not show a man who has not fired - and only of a shooter who is
        /// himself on a held flank, so an ordinary firefight pays nothing for it.</summary>
        void SpringAmbush(CrewWalker shooter)
        {
            if (shooter == null || !shooter.HeldCover.HasValue) return;
            var unit = UnitOf(shooter);
            if (unit == null) return;
            foreach (var man in unit.All())
                if (man != null && man.HeldCover.HasValue) man.SpringAmbush();
        }

        /// <summary>THE AMBUSH SPRINGS ITSELF. The outfit starts nothing (TickCombat) -
        /// except a crew the player put behind a bin and told to wait, which is the one
        /// fight he asked it to start. A rival family's man inside the crew's best gun
        /// reach and in sight of one of the waiting men, and the whole crew opens up
        /// from where it is lying. Never the law, and never a civilian.</summary>
        Unit LurkQuarry(Unit unit)
        {
            float reach = 0f;
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Carrying)
                    reach = Mathf.Max(reach, man.Ballistics.Range);
            if (reach <= 0f) return null;

            Unit best = null;
            float bestD = reach * reach;
            foreach (var other in Units)
            {
                if (other == unit || other.Wiped || other.IsPolice) continue;
                if (other.Faction == unit.Faction) continue;
                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null || !man.Lurking) continue;
                    foreach (var b in other.All())
                    {
                        if (b == null || b.Dead || b.Tf == null || IsAboard(b)) continue;
                        float d = (b.Tf.position - man.Tf.position).sqrMagnitude;
                        if (d >= bestD) continue;
                        if (!InSight(man.Tf.position, b.Tf.position)) continue;
                        bestD = d;
                        best = other;
                    }
                }
            }
            return best;
        }
    }
}
