using System.Collections.Generic;
using UnityEngine;
using RoadDemo;

namespace LivingCity.Tests
{
    /// <summary>
    /// The two things a car on the demo's street must never be seen doing: standing in
    /// the same metres of road as another car, and standing on the pavement. Both were
    /// shipped - a crew car and a pack car interpenetrating in the middle of a drive-by,
    /// and one wedged half up the kerb - and both are geometry, not driving, so they can
    /// be checked here without a Play mode.
    ///
    /// Same discipline as the rest of this folder: plain statics, no UnityEngine.Object,
    /// nothing logged (Debug.Log is an internal call and throws outside the runtime) -
    /// load the built Assembly-CSharp.dll into a bare .NET host, call <see cref="Run"/>
    /// by reflection, read the returned list. Empty means everything passed.
    /// </summary>
    public static class CarSpaceTests
    {
        sealed class Body : IRoadUser
        {
            public Vector3 P, F = Vector3.right;
            public float V, HL = 2.3f, HW = 0.95f;
            public Vector3 RoadPosition => P;
            public Vector3 RoadForward => F;
            public float RoadSpeed => V;
            public float HalfLength => HL;
            public float HalfWidth => HW;
        }

        public static List<string> Run()
        {
            var fails = new List<string>();
            OverlapSeparates(fails);
            DaylightIsNotOverlap(fails);
            AdvanceStopsShort(fails);
            AdvanceEasesOutOfAWedge(fails);
            AngledCarIsWider(fails);
            UTurnKeepsTheBodyInTheRoad(fails);
            KerbClampHoldsAnAngledCar(fails);
            return fails;
        }

        static void With(params IRoadUser[] users)
        {
            StreetTraffic.Users.Clear();
            foreach (var u in users) StreetTraffic.Users.Add(u);
        }

        // Two boxes on top of one another are seen as such, and the shove that comes
        // back really does take them apart.
        static void OverlapSeparates(List<string> fails)
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(2f, 0f, 0.4f);
            if (!RoadSpace.Overlap(a, Vector3.right, 2.3f, 0.95f, b, Vector3.right, 2.3f, 0.95f, 0.06f, out var push))
            {
                fails.Add("CarSpace: two cars 2 m apart nose to tail are not seen as overlapping");
                return;
            }
            if (RoadSpace.Overlap(a + push, Vector3.right, 2.3f, 0.95f, b, Vector3.right, 2.3f, 0.95f, 0f, out _))
                fails.Add("CarSpace: the shove out of an overlap does not clear it");
        }

        static void DaylightIsNotOverlap(List<string> fails)
        {
            if (RoadSpace.Overlap(Vector3.zero, Vector3.right, 2.3f, 0.95f,
                                  new Vector3(6f, 0f, 0f), Vector3.right, 2.3f, 0.95f, 0.06f, out _))
                fails.Add("CarSpace: two cars 6 m apart are called an overlap");
            if (RoadSpace.Overlap(Vector3.zero, Vector3.right, 2.3f, 0.95f,
                                  new Vector3(0f, 0f, 2.5f), Vector3.right, 2.3f, 0.95f, 0.06f, out _))
                fails.Add("CarSpace: cars in neighbouring lanes are called an overlap");
        }

        // A car driven straight at a stopped one never reaches it: the step is cut
        // short, frame after frame, and the bodies never touch.
        static void AdvanceStopsShort(List<string> fails)
        {
            var self = new Body { P = Vector3.zero };
            var wall = new Body { P = new Vector3(8f, 0f, 0f) };
            With(self, wall);
            for (int i = 0; i < 400; i++)
            {
                var to = self.P + Vector3.right * 0.3f;
                self.P = RoadSpace.Advance(self, self.P, to, Vector3.right, self.HL, self.HW, out _);
                if (RoadSpace.Overlap(self.P, self.F, self.HL, self.HW, wall.P, wall.F, wall.HL, wall.HW, 0f, out _))
                {
                    fails.Add("CarSpace: a car driven at a stopped one ends up inside it");
                    break;
                }
            }
            if (self.P.x < 3f) fails.Add("CarSpace: a car stopped short of another car far too early");
            StreetTraffic.Users.Clear();
        }

        // One that has ended up inside another anyway (a car turned into it) is eased
        // out, and does not stay in it.
        static void AdvanceEasesOutOfAWedge(List<string> fails)
        {
            var self = new Body { P = new Vector3(0.3f, 0f, 0.3f) };
            var other = new Body { P = Vector3.zero };
            With(self, other);
            for (int i = 0; i < 600; i++)
                self.P = RoadSpace.Advance(self, self.P, self.P, Vector3.right, self.HL, self.HW, out _);
            if (RoadSpace.Overlap(self.P, self.F, self.HL, self.HW, other.P, other.F, other.HL, other.HW, 0f, out _))
                fails.Add("CarSpace: a wedged car is never eased out of the car it is in");
            StreetTraffic.Users.Clear();
        }

        // The reason a car climbs the kerb: turned across the street it reaches half a
        // metre further sideways than its flank does.
        static void AngledCarIsWider(List<string> fails)
        {
            float straight = RoadSpace.LateralExtent(Vector3.right, 2.3f, 0.95f);
            float angled = RoadSpace.LateralExtent(new Vector3(1f, 0f, 1f), 2.3f, 0.95f);
            float across = RoadSpace.LateralExtent(Vector3.forward, 2.3f, 0.95f);
            if (Mathf.Abs(straight - 0.95f) > 0.01f) fails.Add("CarSpace: a car along the street is not its own half width across it");
            if (angled < straight + 0.4f) fails.Add("CarSpace: a car at an angle is not measured wider than its flank");
            if (Mathf.Abs(across - 2.3f) > 0.01f) fails.Add("CarSpace: a car square across the street is not its own half length wide");
        }

        // The U-turn is laid inside the carriageway - and it has to hold for the whole
        // body of the car, corners and all, not just the point it is steered by.
        static void UTurnKeepsTheBodyInTheRoad(List<string> fails)
        {
            var road = new StraightStreetModel(0f, -200f, 200f);
            foreach (float dir in new[] { 1f, -1f })
            {
                var path = new PathBuilder(road, 0f).UTurn(0f, road.LaneZ(dir), dir).Build(18f, 4.5f, false);
                foreach (var s in path.Samples)
                {
                    float reach = Mathf.Abs(s.P.z - road.CentreZ) + RoadSpace.LateralExtent(s.Dir, 2.3f, 0.95f);
                    if (reach > road.HalfRoad + 0.5f)
                        fails.Add("CarSpace: the U-turn puts the body " + (reach - road.HalfRoad).ToString("0.00") + " m over the kerb");
                }
            }
        }

        // And the belt under the steering holds a car that IS across the road back off
        // the pavement, where the old one (measured on the flank) let it up.
        static void KerbClampHoldsAnAngledCar(List<string> fails)
        {
            var road = new StraightStreetModel(0f, -200f, 200f);
            var across = new Vector3(1f, 0f, 1f).normalized;
            float extent = RoadSpace.LateralExtent(across, 2.3f, 0.95f);
            float z = road.ClampZ(4.4f, extent);
            if (z + extent > road.HalfRoad + 0.5f)
                fails.Add("CarSpace: an angled car clamped to the road still stands " +
                          (z + extent - road.HalfRoad).ToString("0.00") + " m over the kerb");
            if (road.ClampZ(0f, extent) != 0f) fails.Add("CarSpace: the kerb clamp moves a car that is in the middle of the road");
        }
    }
}
