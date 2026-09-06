using System;
using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>Runs the production recovery planner and stepper without an Editor.
    /// Fixtures supply swept obstacle geometry; no animation or scene is simulated.</summary>
    public static class PedestrianGraphDetourTests
    {
        const float Radius = 0.45f;
        struct Prop
        {
            public float X, Z, HalfX, HalfZ;
            public Prop(float x, float z, float halfX, float halfZ)
            { X = x; Z = z; HalfX = halfX; HalfZ = halfZ; }
        }

        static PedLink Link(bool crossing = false, float length = 12f) => new PedLink
        {
            From = new PedNode { Pos = Vector3.zero },
            To = new PedNode { Pos = new Vector3(0f, 0f, length) },
            Length = length, Gated = crossing
        };

        public static List<string> Run()
        {
            var failures = new List<string>();
            foreach (float dt in new[] { 1f / 60f, 1f / 15f, 0.3f })
            {
                Check(failures, $"post dt={dt}", () => Pass(
                    new[] { new Prop(0f, 3f, .15f, .15f) }, new Vector3(0f, 0f, 2.35f), dt));
                Check(failures, $"bench dt={dt}", () => Pass(
                    new[] { new Prop(0f, 3f, 1f, .25f) }, new Vector3(0f, 0f, 2.25f), dt));
                Check(failures, $"staggered props dt={dt}", () => Pass(
                    new[] { new Prop(-.7f, 3f, .8f, .15f), new Prop(.7f, 5.1f, .8f, .15f) },
                    new Vector3(-.7f, 0f, 2.3f), dt));
                Check(failures, $"crossing mouth dt={dt}", () => Pass(
                    new[] { new Prop(0f, 11.7f, .12f, .12f) }, new Vector3(0f, 0f, 11.05f), dt, true));
            }
            Check(failures, "closed corridor turns back", () => ClosedCorridor());
            Check(failures, "streamed obstacle invalidates path", ChangedGeometry);
            Check(failures, "external move cancels old path", MovedExternally);
            Check(failures, "link change cancels old path", ChangedLink);
            Check(failures, "crowd and gait stop movement", Stopped);
            Check(failures, "search failure is throttled", Throttled);
            Check(failures, "rotated pavement", Rotated);
            Check(failures, "global search budget and queue fairness", GlobalBudget);
            Check(failures, "failed plans shared until geometry changes", FailureMemo);
            Check(failures, "invalid standing placement repaired before routing", PlacementRecovery);
            Check(failures, "version churn cannot reset active give-up clock", VersionChurn);
            Check(failures, "crowd and traffic pauses do not spend give-up time", PauseBeforeInvalidation);
            Check(failures, "closed crossing retains existing safe reversal", () => ClosedCorridor(true));
            Check(failures, "recovery rejects the far face and neighbouring solids", RecoveryGeometry);
            return failures;
        }

        static int Frame;
        sealed class Harness
        {
            readonly PedestrianGraphDetour _detour;
            public bool Pending => _detour.Pending;
            public Harness(Func<Vector3, Vector3, bool> clear) => _detour = new PedestrianGraphDetour(clear);
            public void Begin(PedLink link, Vector3 from, float now) => _detour.Begin(link, from, now, Frame, 0);
            public bool Step(PedLink link, Vector3 from, float budget, float now, out Vector3 at, out bool back) =>
                _detour.Step(link, from, budget, now, out at, out back, ++Frame, 0);
        }

        static void Check(List<string> failures, string name, Action test)
        {
            PedestrianGraphDetour.ResetForPlay(); Frame = 0;
            try { test(); } catch (Exception e) { failures.Add(name + ": " + e.Message); }
        }

        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        static void Pass(Prop[] props, Vector3 start, float dt, bool crossing = false)
        {
            var link = Link(crossing);
            var detour = new Harness((a, b) => Clear(props, a, b));
            Require(!Clear(props, start, start + Vector3.forward), "fixture must obstruct the original line");
            detour.Begin(link, start, 0f);
            var position = start;
            for (int frame = 1; frame <= 600 && detour.Pending; frame++)
            {
                var before = position;
                float cap = Math.Min(1.5f * dt, .75f);
                Require(detour.Step(link, before, cap, frame * dt, out position, out bool back), "path disappeared");
                Require(!back, "turned back despite an open corridor");
                Require(Clear(props, before, position), "walked through a prop");
                Require(!crossing || position.z >= before.z - .0001f, "retreated on an admitted crossing");
                Require(FlatDistance(before, position) <= cap + .0001f, "exceeded the frame stride budget");
                Require(Math.Abs(position.x) <= 1.9001f && position.z <= link.Length + .0001f,
                    "left the sidewalk/crossing or skipped its junction");
            }
            Require(!detour.Pending, "never finished the detour");
            Require(position.z >= Math.Min(link.Length, start.z + 4f) - .001f,
                "finished without physical forward progress");
        }

        static void ClosedCorridor(bool crossing = false)
        {
            var props = new[] { new Prop(0f, 3f, 3f, .1f) };
            var link = Link(crossing);
            var from = new Vector3(0f, 0f, 2.3f);
            var detour = new Harness((a, b) => Clear(props, a, b));
            detour.Begin(link, from, 0f);
            Vector3 at = from; bool back = false;
            for (int frame = 1; frame <= 130 && !back; frame++)
                detour.Step(link, from, .025f, frame / 60f, out at, out back);
            Require(back && at == from && !detour.Pending, "closed corridor did not yield a safe reversal");
        }

        static void ChangedGeometry()
        {
            var props = new List<Prop> { new Prop(0f, 3f, .15f, .15f) };
            var link = Link();
            var position = new Vector3(0f, 0f, 2.3f);
            var detour = new Harness((a, b) => Clear(props, a, b));
            detour.Begin(link, position, 0f);
            props.Add(new Prop(0f, 3f, 3f, .1f));
            bool reversed = false;
            for (int i = 1; i < 600 && detour.Pending; i++)
            {
                var before = position;
                detour.Step(link, before, .025f, i / 60f, out position, out bool back);
                Require(Clear(props, before, position), "committed a segment blocked by a newly streamed prop");
                reversed |= back;
            }
            Require(reversed && !detour.Pending, "invalid path neither replanned nor gave up");
        }

        static void MovedExternally()
        {
            var link = Link();
            var detour = new Harness((a, b) => true);
            detour.Begin(link, Vector3.forward, 0f);
            var moved = new Vector3(1f, 0f, 5f);
            Require(!detour.Step(link, moved, .1f, 1f, out var at, out _) && at == moved && !detour.Pending,
                "a door/bench move retained a stale path");
        }

        static void ChangedLink()
        {
            var detour = new Harness((a, b) => true);
            detour.Begin(Link(), Vector3.forward, 0f);
            Require(!detour.Step(Link(true), Vector3.forward, .1f, 1f, out _, out _) && !detour.Pending,
                "continued a route belonging to a previous link");
        }

        static void Stopped()
        {
            var link = Link();
            var detour = new Harness((a, b) => true);
            detour.Begin(link, Vector3.forward, 0f);
            detour.Step(link, Vector3.forward, 0f, 20f, out var at, out bool back);
            Require(at == Vector3.forward && !back && detour.Pending, "moved during a crowd/gait stop");
        }

        static void Throttled()
        {
            int queries = 0;
            var link = Link();
            var detour = new Harness((a, b) => { queries++; return false; });
            detour.Begin(link, Vector3.forward, 0f);
            int first = queries;
            for (int i = 1; i < 30; i++)
                detour.Step(link, Vector3.forward, .025f, i / 60f, out _, out _);
            Require(queries == first, "repeated the failed search every frame");
        }

        static void Rotated()
        {
            var link = Link();
            link.To.Pos = new Vector3(12f, 0f, 0f);
            var props = new[] { new Prop(0f, 3f, .15f, .15f) };
            Vector3 Local(Vector3 p) => new Vector3(-p.z, p.y, p.x);
            var detour = new Harness((a, b) => Clear(props, Local(a), Local(b)));
            var at = new Vector3(2.3f, 0f, 0f);
            detour.Begin(link, at, 0f);
            for (int i = 1; i < 600 && detour.Pending; i++)
            {
                var before = at;
                detour.Step(link, before, .025f, i / 60f, out at, out bool back);
                Require(!back && Clear(props, Local(before), Local(at)), "rotated detour failed");
            }
            Require(!detour.Pending && at.x >= 6.299f, "rotated detour did not complete");
        }

        static void GlobalBudget()
        {
            var walkers = new List<(PedestrianGraphDetour Detour, PedLink Link, Vector3 From)>();
            int searches = 0;
            for (int i = 0; i < 12; i++)
            {
                var link = Link();
                var from = new Vector3(0f, 0f, 1f);
                bool first = true;
                var detour = new PedestrianGraphDetour((a, b) => { if (first) { searches++; first = false; } return true; });
                detour.Begin(link, from, 0f, 0, 0);
                walkers.Add((detour, link, from));
            }
            Require(searches == 2, "more than two plans ran in one frame");
            for (int frame = 1; frame <= 5; frame++)
            {
                int before = searches;
                foreach (var w in walkers)
                    w.Detour.Step(w.Link, w.From, .01f, frame / 60f, out _, out _, frame, 0);
                Require(searches - before <= 2, "per-frame search ceiling was exceeded");
            }
            Require(searches == 12, "queued walkers starved");
        }

        static void FailureMemo()
        {
            int queries = 0;
            var link = Link();
            var from = Vector3.forward;
            var first = new PedestrianGraphDetour((a, b) => { queries++; return false; });
            first.Begin(link, from, 0f, 0, 0);
            var second = new PedestrianGraphDetour((a, b) => { queries++; return false; });
            second.Begin(link, from, 0f, 1, 0);
            Require(queries == 1, "same failed start was searched twice");
            second.Step(link, from, .1f, .1f, out _, out _, 2, 1);
            Require(queries == 2, "geometry change did not invalidate the failure memo");
        }

        static void PlacementRecovery()
        {
            var link = Link();
            var box = SidewalkPlan.Make(new Vector2(0f, 3f), 0f, Vector2.one * .1f, true);
            var plan = new SidewalkPlan(); plan.Take(box);
            bool ClearBox(Vector3 a, Vector3 b) => !plan.Obstructs(new Vector2(a.x, a.z), new Vector2(b.x, b.z), Radius);
            bool Escape(Vector3 a, Vector3 b) => SidewalkPlan.RecoveryStepClear(box,
                new Vector2(a.x, a.z), new Vector2(b.x, b.z), Radius, out _);
            var from = new Vector3(0f, 0f, 3f);
            var repair = new Vector3(.7f, 0f, 3f);
            int repairs = 0;
            var detour = new PedestrianGraphDetour(ClearBox,
                (l, p) => { repairs++; return repair; }, Escape);
            detour.Begin(link, from, 0f, 0, 0);
            var at = from;
            for (int frame = 1; frame < 600 && detour.Pending; frame++)
            {
                var before = at;
                detour.Step(link, before, .025f, frame / 60f, out at, out bool back, frame, 0);
                Require(FlatDistance(before, at) <= .0251f && !back, "repair snapped or reversed");
                Require(ClearBox(before, at) || Escape(before, at), "repair went deeper into a prop");
            }
            Require(!detour.Pending && at.z >= 6.99f && repairs == 1, "repaired body did not finish");
        }

        static void VersionChurn()
        {
            var link = Link();
            var detour = new PedestrianGraphDetour((a, b) => false);
            var from = Vector3.forward;
            detour.Begin(link, from, 0f, 0, 0);
            bool back = false;
            for (int frame = 1; frame <= 130 && !back; frame++)
                detour.Step(link, from, .025f, frame / 60f, out _, out back, frame, frame);
            Require(back && !detour.Pending, "streaming churn restarted the give-up clock");
        }

        static void PauseBeforeInvalidation()
        {
            var link = Link();
            bool clear = true;
            var detour = new PedestrianGraphDetour((a, b) => clear);
            var from = Vector3.forward;
            detour.Begin(link, from, 0f, 0, 0);
            detour.Step(link, from, 0f, 100f, out _, out _, 1, 0);
            clear = false;
            detour.Step(link, from, .025f, 100.02f, out var at, out bool back, 2, 1);
            Require(!back && at == from && detour.Pending, "traffic wait caused instant reversal");
            detour.Step(link, from, .025f, 200f, out _, out back, 3, 1);
            Require(!back, "time between Move calls counted as blocked walking time");
        }

        static void RecoveryGeometry()
        {
            var box = SidewalkPlan.Make(Vector2.zero, 35f, new Vector2(1f, .2f), true);
            var nearFace = box.Ax * .9f;
            Require(SidewalkPlan.RecoveryStepClear(box, nearFace, box.Ax * 1.6f, Radius, out bool inside) && inside,
                "outward escape from rotated box was rejected");
            Require(!SidewalkPlan.RecoveryStepClear(box, nearFace, box.Ax * -1.6f, Radius, out _),
                "recovery crossed the box to its far face");
            var neighbour = SidewalkPlan.Make(box.Ax * 1.8f, 35f, Vector2.one * .1f, true);
            Require(!SidewalkPlan.RecoveryStepClear(neighbour, nearFace, box.Ax * 1.7f, Radius, out _),
                "recovery entered a neighbouring solid");
        }

        // Independent conservative swept-box oracle. Inflating each fixture by the
        // shoulder radius is stricter at corners than the production swept circle.
        static bool Clear(IEnumerable<Prop> props, Vector3 a, Vector3 b)
        {
            foreach (var p in props)
            {
                float lo = 0f, hi = 1f;
                if (Slab(a.x, b.x - a.x, p.X - p.HalfX - Radius, p.X + p.HalfX + Radius, ref lo, ref hi) &&
                    Slab(a.z, b.z - a.z, p.Z - p.HalfZ - Radius, p.Z + p.HalfZ + Radius, ref lo, ref hi))
                    return false;
            }
            return true;
        }

        static bool Slab(float from, float delta, float min, float max, ref float lo, ref float hi)
        {
            if (Math.Abs(delta) < 1e-8f) return from >= min && from <= max;
            float a = (min - from) / delta, b = (max - from) / delta;
            lo = Math.Max(lo, Math.Min(a, b));
            hi = Math.Min(hi, Math.Max(a, b));
            return lo <= hi;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        { var d = a - b; d.y = 0f; return d.magnitude; }
    }
}
