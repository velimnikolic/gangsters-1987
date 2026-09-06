using System;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace GangstersVerification
{
    /// <summary>Integration fixtures for an explicitly authorized Editor test pass.
    /// Drives the real Move, GraphStepBlocked, CarrySeat and obstacle registration.</summary>
    public static class NpcPropRecoveryTests
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly MethodInfo Move = typeof(PedestrianAgent).GetMethod("Move", Private);
        static readonly MethodInfo Reset = typeof(PedestrianAgent).Assembly
            .GetType("RoadDemo.PedestrianGraphDetour").GetMethod("ResetForPlay", BindingFlags.NonPublic | BindingFlags.Static);

        sealed class Walker : PedestrianAgent
        {
            public int Arrivals;
            public bool BlockSteps;
            protected override bool OnArrived(PedNode node) { Arrivals++; return false; }
            protected override bool GraphStepClear(Vector3 from, Vector3 to) => !BlockSteps && base.GraphStepClear(from, to);
            protected override void GraphStepBlocked(Vector3 wanted) { if (!BlockSteps) base.GraphStepBlocked(wanted); }
            public bool Admitted(PedLink link) => MayEnter(link);
            public void RunGait() => LocomotionPose = PoseJog;
            public void Idle() => SetPose(PoseIdle);
            public bool DetailEnabled => Detailed;
        }

        [MenuItem("Tools/Gangsters/Tests/NPC Prop Recovery")]
        public static void RunMenu()
        {
            var failures = Run();
            if (failures.Count == 0) Debug.Log("NPC prop recovery: PASS (4 integration scenarios)");
            else Debug.LogError(string.Join("\n", failures));
        }

        public static List<string> Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run in Edit mode.");
            var failures = new List<string>();
            foreach (var scenario in new[] { "pavement", "crossing", "streamed overlap" })
            {
                try { Exercise(scenario); }
                catch (Exception e) { failures.Add(scenario + ": " + e.GetBaseException().Message); }
            }
            try { BlockedRunResumes(); }
            catch (Exception e) { failures.Add("blocked run resumes: " + e.GetBaseException().Message); }
            return failures;
        }

        static void BlockedRunResumes()
        {
            Reset.Invoke(null, null);
            var origin = new Vector3(10000f, 0f, 10000f);
            var city = new Rect(origin.x - 10f, origin.z - 10f, 20f, 30f);
            var link = new PedLink { From = new PedNode { Pos = origin },
                To = new PedNode { Pos = origin + Vector3.forward * 12f }, Length = 12f };
            var props = new SidewalkPlan();
            var body = new GameObject("Blocked NPC gait fixture");
            var eye = new GameObject("NPC gait fixture camera");
            var clip = new AnimationClip();
            var walker = new Walker();
            var random = UnityEngine.Random.state;
            bool detail = PedDetail.On;
            float detailRadius = PedDetail.Radius;
            var starts = typeof(CrewKit).GetField("toRun", BindingFlags.NonPublic | BindingFlags.Static);
            var savedStarts = starts.GetValue(null);
            WalkObstacles.City.Add(city);
            try
            {
                PedDetail.On = true;
                PedDetail.Radius = float.MaxValue;
                eye.tag = "MainCamera";
                eye.AddComponent<Camera>();
                eye.transform.position = origin;
                Require(walker.Init(body.transform, new PedClips(), link, 2.3f), "clear init refused");
                typeof(PedestrianAgent).GetField("_lived", Private).SetValue(walker, 1f);
                Require(walker.DetailEnabled, "fixture did not enable authored joins");
                var graph = PlayableGraph.Create("Blocked NPC gait");
                typeof(PedestrianAgent).GetField("_graph", Private).SetValue(walker, graph);
                var poses = (AnimationClipPlayable[])typeof(PedestrianAgent).GetField("_poses", Private).GetValue(walker);
                var mixer = AnimationMixerPlayable.Create(graph, poses.Length);
                typeof(PedestrianAgent).GetField("_mixer", Private).SetValue(walker, mixer);
                foreach (int pose in new[] { PedestrianAgent.PoseWalk, PedestrianAgent.PoseIdle, PedestrianAgent.PoseJog })
                {
                    poses[pose] = AnimationClipPlayable.Create(graph, clip);
                    graph.Connect(poses[pose], 0, mixer, pose);
                }
                // Use a real travelling clip as a deterministic transition input:
                // some shipped start takes report zero root speed and are refused.
                var carries = CrewKit.StockWalk;
                Require(carries != null && carries.length >= .05f && carries.averageSpeed.magnitude >= .3f,
                    "fixture requires a clip that can carry a linear join");
                starts.SetValue(null, new[] { carries, carries, carries, carries, carries });
                walker.RunGait();
                walker.Speed = 3f;
                walker.Idle();
                walker.Tick(1f / 60f);
                Require(walker.Joining, "positive control did not begin a start join");
                // No reverse link: failed recovery must settle to idle even when
                // there is nowhere else to go, then wake up when geometry changes.
                props.Take(SidewalkPlan.Make(new Vector2(origin.x, origin.z + 3f), 0f,
                    new Vector2(4f, .12f), true));
                WalkObstacles.RegisterPlan(props);
                for (int i = 0; i < 360; i++)
                {
                    walker.Tick(1f / 60f);
                    Require(i < 60 || !walker.LegsMoving, "blocked NPC kept a travelling gait");
                }
                Require(mixer.GetInputWeight(PedestrianAgent.PoseIdle) > .99f,
                    "blocked gait did not blend to idle");
                var stopped = body.transform.position;
                WalkObstacles.UnregisterPlan(props);
                for (int i = 0; i < 120; i++)
                {
                    walker.Tick(1f / 60f);
                    Require(!walker.Joining, "recovery restarted a start clip");
                }
                Require(body.transform.position.z > stopped.z + .2f && walker.LegsMoving,
                    "idle animation prevented movement from resuming");
                for (int i = 0; i < 24; i++)
                {
                    walker.BlockSteps = i % 4 < 2;
                    walker.Tick(1f / 60f);
                    Require(!walker.Joining, "intermittent refusal restarted a start clip");
                }
                // A moving take owns its action even when geometry stops the feet.
                Require(walker.PlayTake(carries, true, 1f, 0f, allowMovement: true), "take refused");
                walker.BlockSteps = true;
                walker.Tick(1f / 60f);
                Require(walker.Take == carries, "blocked movement cancelled its owning action");
            }
            finally
            {
                walker.Dispose();
                UnityEngine.Object.DestroyImmediate(body);
                UnityEngine.Object.DestroyImmediate(eye);
                UnityEngine.Object.DestroyImmediate(clip);
                WalkObstacles.UnregisterPlan(props);
                WalkObstacles.City.Remove(city);
                UnityEngine.Random.state = random;
                PedDetail.On = detail;
                PedDetail.Radius = detailRadius;
                starts.SetValue(null, savedStarts);
                Reset.Invoke(null, null);
            }
        }

        static void Exercise(string scenario)
        {
            Reset.Invoke(null, null);
            var origin = new Vector3(10000f, 0f, 10000f);
            var city = new Rect(origin.x - 10f, origin.z - 10f, 20f, 30f);
            var link = new PedLink { From = new PedNode { Pos = origin },
                To = new PedNode { Pos = origin + Vector3.forward * 12f }, Length = 12f,
                Gated = scenario == "crossing", BlocksNorthSouth = true };
            var props = new SidewalkPlan();
            var body = new GameObject("NPC prop recovery fixture");
            var walker = new Walker();
            var random = UnityEngine.Random.state;
            WalkObstacles.City.Add(city);
            try
            {
                Require(walker.Init(body.transform, new PedClips(), link, 2.3f), "Init refused the clear seat");
                walker.Speed = 1.5f;
                foreach (var field in new[] { "_lane", "_lateral" })
                    typeof(PedestrianAgent).GetField(field, Private).SetValue(walker, 0f);
                float start = scenario == "streamed overlap" ? 3f : 2.3f;
                typeof(PedestrianAgent).GetField("_t", Private).SetValue(walker, start);
                body.transform.position = origin + Vector3.forward * start;
                props.Take(SidewalkPlan.Make(new Vector2(origin.x, origin.z + 3f), 0f, Vector2.one * .12f, true));
                WalkObstacles.RegisterPlan(props);
                float red = float.PositiveInfinity;
                if (link.Gated)
                {
                    link.Signal = new TrafficSignal(TrafficSignal.HalfCycle + .01f - RoadCarSimulation.Now);
                    Require(walker.Admitted(link), "test crossing was not admitted on red");
                    red = link.Signal.RedRemaining(true);
                }
                const float dt = 1f / 60f;
                float elapsed = 0f;
                bool recovered = false;
                while (walker.Arrivals == 0 && elapsed < 20f)
                {
                    var before = body.transform.position;
                    bool overlapped = WalkObstacles.Standing(before, WalkObstacles.Radius);
                    Move.Invoke(walker, new object[] { dt });
                    var after = body.transform.position;
                    if (overlapped && after != before)
                    {
                        recovered = true;
                        Require(Vector3.Distance(before, after) <= PedestrianAgent.FrameStepLimit + .0001f,
                            "placement correction exceeded the frame stride limit");
                        var escape = typeof(WalkObstacles).GetMethod("ClearRecoveryStep", BindingFlags.Static | BindingFlags.NonPublic);
                        Require((bool)escape.Invoke(null, new object[] { before, after, WalkObstacles.Radius }),
                            "placement correction went deeper into geometry or entered another solid");
                    }
                    else Require(after == before || !WalkObstacles.BlocksStanding(before, after, WalkObstacles.Radius),
                        "walked through registered geometry");
                    Require(!link.Gated || after.z >= before.z - .0001f, "retreated along an admitted crossing");
                    elapsed += dt;
                }
                Require(walker.Arrivals == 1 && body.transform.position.z >= link.To.Pos.z - .1f,
                    "missing arrival or arrival ahead of the actual feet");
                Require(elapsed < red, "fixture exceeded its crossing admission window");
                Require(scenario != "streamed overlap" || recovered, "invalid seat was not repaired");
            }
            finally
            {
                walker.Dispose();
                UnityEngine.Object.DestroyImmediate(body);
                WalkObstacles.UnregisterPlan(props);
                WalkObstacles.City.Remove(city);
                UnityEngine.Random.state = random;
                Reset.Invoke(null, null);
            }
        }

        static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
