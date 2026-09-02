using RoadDemo;
using UnityEngine;

namespace BlockDemo
{
    /// <summary>
    /// THE SACEKUSA, with nobody at the mouse (EPIC 28, COVER-006).
    ///
    /// The lab plays the click the player would make: it finds something to get behind
    /// between the outfit and a mob, right-clicks it (DemoCrews.OrderAmbush), waits for
    /// the men to be down behind their flanks with their guns out, and then walks the
    /// mob into them. What is on trial is the whole of the ambush and nothing else:
    ///
    ///   noambush     - the street had nothing the crew could be put behind
    ///   nolurk       - the men were dealt flanks and never got down behind them
    ///   seenfirst    - the mob had its guns on us before we had ours on it (the
    ///                  surprise range is not holding: DemoCrews.Concealed)
    ///   openambush   - the ambush sprang and the men were NOT behind anything when it
    ///                  did, which is the fire-then-cover fault this epic exists to end
    ///   nospring     - the mob walked up and nothing happened
    ///
    /// It is deliberately not a fight to the death: the run is done the moment the
    /// ambush has sprung correctly. Who wins the exchange afterwards is the ordinary
    /// combat soak's business.
    /// </summary>
    public partial class BlockDemoMission
    {
        [Tooltip("THE AMBUSH. The crew is put behind a bin (or a parked car) between " +
                 "itself and a mob, left to lie in wait, and the mob is then walked into " +
                 "it. What is on trial is that the men are DOWN and QUIET before anybody " +
                 "arrives, that the mob does not see them coming, and that the first " +
                 "rounds leave from behind something.")]
        public bool ambush;

        [Tooltip("Ambush: metres up the line toward the mob to look for something to get " +
                 "behind. Well outside the range a mob opens fire at (24 m), so the men " +
                 "are in place long before anybody is near them.")]
        [Min(10f)] public float ambushAhead = 40f;

        [Tooltip("Ambush: metres round that point the lab will take an anchor from. A " +
                 "pavement is six and a half metres wide, so this stays on it.")]
        [Min(4f)] public float ambushLookWithin = 10f;

        [Tooltip("Ambush: seconds allowed for the men to walk to their flanks and get " +
                 "down behind them before the wait is written off.")]
        [Min(10f)] public float ambushSettle = 90f;

        [Tooltip("Ambush: seconds allowed for the mob to walk into it once the men are " +
                 "in place.")]
        [Min(10f)] public float ambushPatience = 180f;

        /// <summary>Metres BEYOND the anchor the mob is sent, so that it walks past the
        /// ambush rather than up to it.</summary>
        const float WalkPast = 25f;

        enum AmbushStep { Settling, Baiting }

        AmbushStep _ambushStep;
        DemoCrews.CoverAnchor _ambushAnchor;
        float _ambushStepAt;
        int _ambushMen;
        float AmbushInStep => Now - _ambushStepAt;

        void StartAmbush()
        {
            _quarry = null;
            float best = float.MaxValue;
            foreach (var unit in _crews.Units)
            {
                if (unit.Faction == 0 || unit.IsPolice || unit.Wiped) continue;
                float d = (unit.Position - _ours.Position).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                _quarry = unit;
            }
            if (_quarry == null) { Give("there is no rival crew in the quarter"); return; }

            // somewhere between us and them, well outside the range they open up at:
            // the men have to be down and quiet before anybody is near them
            var toward = _quarry.Position - _ours.Position;
            toward.y = 0f;
            float gap = toward.magnitude;
            var look = _ours.Position +
                       (gap > 1f ? toward.normalized : Vector3.forward) * Mathf.Min(ambushAhead, gap * 0.5f);
            // OFF THE ASPHALT, ON THE CREW'S OWN SIDE. Two crews on opposite pavements
            // put the midpoint between them in the middle of the carriageway, and the
            // nearest thing to get behind there is a heap in a live lane - five men
            // lying in wait in the traffic. Where a mob comes down the street is the
            // road, so the men want the PAVEMENT side of something, which is what the
            // order's own threat direction already assumes.
            look = OffTheRoad(look, _ours.Position);
            if (!DemoCrews.AnchorNear(look, ambushLookWithin, out _ambushAnchor))
            {
                Fault("noambush", $"nothing to get behind within {ambushLookWithin:F0} m of the line");
                Give("the street had nothing to lie in wait behind");
                return;
            }

            _crews.Select(_ours);
            if (!_crews.OrderAmbush(_ours, _ambushAnchor))
            {
                Fault("noambush", _crews.AmbushRefusal ?? "the order was refused");
                Give("the crew would not lie in wait there");
                return;
            }

            _ambushStep = AmbushStep.Settling;
            _ambushStepAt = Now;
            State = Phase.Marching;
            _phaseAt = Now;
            Note($"Ambush: {_ours.GangName} put behind " +
                 (_ambushAnchor.IsCar ? "a car" : "a prop") +
                 $" {Vector3.Distance(_ours.Position, _ambushAnchor.At):F0} m off, " +
                 $"{_quarry.GangName} {Vector3.Distance(_ambushAnchor.At, _quarry.Position):F0} m up the street");
        }

        /// <summary>The nearest pavement to a point, on the side <paramref name="ourSide"/>
        /// stands. Off the lane net entirely, the point is already somewhere a man may
        /// stand and is left alone.</summary>
        static Vector3 OffTheRoad(Vector3 at, Vector3 ourSide)
        {
            var net = LaneNet.Active;
            if (net == null) return at;
            var road = net.Locate(at, out float s, out float d, 20f);
            if (road == null || Mathf.Abs(d) > road.HalfRoad + 2f) return at;
            var middle = road.A + road.Axis * Mathf.Clamp(s, 0f, road.Length);
            float side = Vector3.Dot(ourSide - middle, road.Right) >= 0f ? 1f : -1f;
            var off = middle + road.Right * side * (road.HalfRoad + 3f);
            off.y = at.y;
            return off;
        }

        void TickAmbush()
        {
            if (_ours == null || _ours.Wiped) { Give("the crew lying in wait was wiped out"); return; }
            if (_quarry == null || _quarry.Wiped)
            { Go(Phase.Done, "the mob was gone before it ever walked in"); return; }

            switch (_ambushStep)
            {
                case AmbushStep.Settling: TickAmbushSettle(); break;
                case AmbushStep.Baiting: TickAmbushBait(); break;
            }
        }

        /// <summary>Down behind them, guns out, and nobody stood up in the street.</summary>
        void TickAmbushSettle()
        {
            int lurking = 0, held = 0, standing = 0;
            foreach (var man in _ours.All())
            {
                if (man == null || man.Dead) continue;
                standing++;
                if (man.HeldCover.HasValue) held++;
                if (man.Lurking) lurking++;
            }
            _ambushMen = lurking;

            if (lurking > 0 && lurking >= held)
            {
                _ambushStep = AmbushStep.Baiting;
                _ambushStepAt = Now;
                // and now they are walked into it. A mob does not come looking on its
                // own - it stands at its frontage - so the lab marches it, which is the
                // only thing here the player would not have done himself.
                //
                // PAST the thing and not AT it, and off the road: an ambush is somebody
                // walking down a street, not somebody walking up to a bin. Marched at
                // the anchor itself - which is as often as not a car at the kerb - they
                // walk down the carriageway to reach it, which is its own fault
                // (CrewAudit.roadwalk) and reads as a mob queueing for the ambush.
                var past = _ambushAnchor.At - _quarry.Position;
                past.y = 0f;
                var walkTo = _ambushAnchor.At +
                             (past.sqrMagnitude > 1f ? past.normalized : Vector3.forward) * WalkPast;
                _crews.MarchTo(_quarry, walkTo, run: false, keepOffRoad: true);
                Note($"lying in wait: {lurking} of {standing} down behind something, " +
                     $"{_quarry.GangName} walked in");
                return;
            }

            if (AmbushInStep < ambushSettle) return;
            Fault("nolurk", $"{lurking} of {standing} down behind something after " +
                            $"{ambushSettle:F0} s ({held} dealt a flank)");
            Give("the men never got down behind their flanks");
        }

        /// <summary>The mob walks in. Two things are watched and nothing else: who laid
        /// eyes on whom first, and whether our first rounds left from behind
        /// something.</summary>
        void TickAmbushBait()
        {
            // THE SURPRISE. Their crew must not have a mark before ours does: that is
            // the whole of the concealment rule (DemoCrews.Concealed, LurkSeen).
            if (_quarry.TargetUnit != null && _ours.TargetUnit == null)
            {
                Fault("seenfirst", $"{_quarry.GangName} had its guns on us first, " +
                                   $"{Vector3.Distance(_ours.Position, _quarry.Position):F0} m out");
                Give("the mob saw the ambush before it sprang");
                return;
            }

            if (_ours.TargetUnit == null)
            {
                if (AmbushInStep < ambushPatience) return;
                Fault("nospring", $"{ambushPatience:F0} s and the mob never walked into it " +
                                  $"({Vector3.Distance(_ours.Position, _quarry.Position):F0} m out)");
                Give("the ambush never sprang");
                return;
            }

            // IT SPRANG. Every man of ours who has a mark should be behind something
            // when it does - he was already there, which is the whole point of the
            // order. A man on his feet in the open here is the fire-then-cover fault.
            int fighting = 0, covered = 0;
            foreach (var man in _ours.All())
            {
                if (man == null || man.Dead || man.Target == null) continue;
                fighting++;
                if (man.InCover || man.CoverSpot.HasValue) covered++;
            }
            if (fighting == 0) return;   // the frame the order lands, before anybody has ticked

            if (covered * 2 < fighting)
                Fault("openambush", $"the ambush sprang with {covered} of {fighting} behind something");
            Go(Phase.Done, $"sprung on {_quarry.GangName}: {covered} of {fighting} firing from cover " +
                           $"({_ambushMen} men had been lying in wait)");
        }
    }
}
