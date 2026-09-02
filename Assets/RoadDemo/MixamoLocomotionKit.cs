using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>The three Mixamo locomotion deliveries used by CoverDemo: a basic
    /// masculine set, a basic feminine set, and the weapon-specific pistol set.
    ///
    /// This class only deals a presentation wardrobe. Movement speed, routing,
    /// obstacle clearance and destinations remain CrewWalker/WalkRoute authority.
    /// DemoCrews opts into it per scene and asks again per body, so a feminine prefab
    /// can never inherit the masculine walk merely because it shares a crew.</summary>
    public static class MixamoLocomotionKit
    {
        const string Root = "Assets/Animations/Mixamo/Locomotion/";
        const string FemaleDir = Root + "Female/";
        const string MaleDir = Root + "Male/";
        const string PistolDir = Root + "Pistol/";

        static readonly Dictionary<string, AnimationClip> Cache =
            new Dictionary<string, AnimationClip>();

        static AnimationClip[] pistolWalks, pistolRuns;

        /// <summary>Deal the body-sex set first, then add the pistol wardrobe. Long-gun
        /// slots already present in <paramref name="clips"/> are deliberately untouched.</summary>
        public static PedClips ForBody(PedClips clips, bool female)
        {
            string directory = female ? FemaleDir : MaleDir;
            var bodyWalk = Clip(directory, "walking");
            var bodyIdle = Clip(directory, "idle");
            var bodyRun = Clip(directory, female ? "running" : "standard run");
            clips.Walk = bodyWalk ?? clips.Walk;
            clips.Idle = bodyIdle ?? clips.Idle;
            clips.Jog = bodyRun ?? clips.Jog;
            clips.TurnLeft = BodyTurn(directory, female, true) ?? clips.TurnLeft;
            clips.TurnRight = BodyTurn(directory, female, false) ?? clips.TurnRight;
            clips.AuthoredBasicLocomotion = bodyWalk != null && bodyIdle != null && bodyRun != null;
            if (clips.AuthoredBasicLocomotion)
                clips.BasicLocomotionLabel = female ? "mixamo-female" : "mixamo-male";

            var pistolIdle = Clip(PistolDir, "pistol idle");
            clips.PistolIdle = pistolIdle ?? clips.PistolIdle;
            clips.PistolCrouch = Clip(PistolDir, "pistol kneeling idle") ?? clips.PistolCrouch;
            clips.PistolWalks = PistolWalks;
            clips.PistolRuns = PistolRuns;
            clips.AuthoredSidearmLocomotion = pistolIdle != null &&
                                              Forward(clips.PistolWalks) != null &&
                                              Forward(clips.PistolRuns) != null;
            return clips;
        }

        static AnimationClip Forward(AnimationClip[] set) =>
            set != null && set.Length > (int)RifleStep.Forward
                ? set[(int)RifleStep.Forward] : null;

        /// <summary>Eight directions in RifleStep order. Pairs downloaded under the
        /// same Mixamo title acquire “(2)” file names; their root-velocity sign, not
        /// download order, decides left and right.</summary>
        public static AnimationClip[] PistolWalks => pistolWalks ??= PistolGaits(false);
        public static AnimationClip[] PistolRuns => pistolRuns ??= PistolGaits(true);

        static AnimationClip[] PistolGaits(bool run)
        {
            var result = new AnimationClip[8];
            string pace = run ? "run" : "walk";
            result[(int)RifleStep.Forward] = Clip(PistolDir, "pistol " + pace);
            result[(int)RifleStep.Backward] = Clip(PistolDir, "pistol " + pace + " backward");

            PutPair(result, RifleStep.ForwardLeft, RifleStep.ForwardRight,
                Clip(PistolDir, "pistol " + pace + " arc"),
                Clip(PistolDir, "pistol " + pace + " arc (2)"));
            PutPair(result, RifleStep.BackwardLeft, RifleStep.BackwardRight,
                Clip(PistolDir, "pistol " + pace + " backward arc"),
                Clip(PistolDir, "pistol " + pace + " backward arc (2)"));

            // The delivery has one pair of strafes rather than separate walk/run
            // strafes. They are still the correct side-step at either pace; playback
            // is geared to the actual metres covered by the walker.
            PutPair(result, RifleStep.Left, RifleStep.Right,
                Clip(PistolDir, "pistol strafe"),
                Clip(PistolDir, "pistol strafe (2)"));
            FillMissing(result);
            return result;
        }

        static void PutPair(AnimationClip[] into, RifleStep left, RifleStep right,
            AnimationClip a, AnimationClip b)
        {
            if (a == null && b == null) return;
            if (a == null) { into[(int)left] = into[(int)right] = b; return; }
            if (b == null) { into[(int)left] = into[(int)right] = a; return; }

            float ax = a.averageSpeed.x;
            float bx = b.averageSpeed.x;
            if (Mathf.Abs(ax - bx) > 0.05f)
            {
                into[(int)left] = ax < bx ? a : b;
                into[(int)right] = ax < bx ? b : a;
            }
            else
            {
                // Root translation may have been stripped by the source export. Keep
                // a stable fallback; the locomotion audit names this ambiguity.
                into[(int)left] = a;
                into[(int)right] = b;
            }
        }

        static void FillMissing(AnimationClip[] set)
        {
            var forward = set[(int)RifleStep.Forward];
            for (int i = 0; i < set.Length; i++)
                if (set[i] == null) set[i] = forward;
        }

        static AnimationClip BodyTurn(string directory, bool female, bool left)
        {
            if (!female)
                return Clip(directory, left ? "left turn 90" : "right turn 90");

            var a = Clip(directory, left ? "left turn" : "right turn");
            var b = Clip(directory, left ? "left turn (2)" : "right turn (2)");
            if (a == null) return b;
            if (b == null) return a;
            float aa = TurnDegrees(a), bb = TurnDegrees(b);
            return Mathf.Abs(aa - 90f) <= Mathf.Abs(bb - 90f) ? a : b;
        }

        static float TurnDegrees(AnimationClip clip) =>
            Mathf.Abs(clip.averageAngularSpeed) * clip.length * Mathf.Rad2Deg;

        /// <summary>Every delivered take, including jumps and kneel transitions whose
        /// gameplay states do not yet exist. Used by the editor audit to make an import
        /// omission loud without pretending an unused jump is a live movement state.</summary>
        public static IReadOnlyList<AnimationClip> All
        {
            get
            {
                var all = new List<AnimationClip>();
                Add(all, FemaleDir,
                    "left strafe", "right strafe", "running", "jump", "idle",
                    "left strafe walk", "left turn", "right strafe walk", "right turn",
                    "left turn (2)", "walking", "right turn (2)");
                Add(all, MaleDir,
                    "left strafe walking", "right strafe walking", "left strafe",
                    "right strafe", "idle", "jump", "walking", "right turn 90",
                    "left turn 90", "standard run");
                Add(all, PistolDir,
                    "pistol idle", "pistol jump", "pistol run", "pistol jump (2)",
                    "pistol walk", "pistol run backward", "pistol walk backward",
                    "pistol strafe", "pistol strafe (2)", "pistol walk arc",
                    "pistol walk arc (2)", "pistol run arc", "pistol run arc (2)",
                    "pistol run backward arc", "pistol run backward arc (2)",
                    "pistol stand to kneel", "pistol kneeling idle", "pistol kneel to stand",
                    "pistol walk backward arc", "pistol walk backward arc (2)");
                return all;
            }
        }

        static void Add(List<AnimationClip> into, string directory, params string[] files)
        {
            foreach (var file in files)
            {
                var clip = Clip(directory, file);
                if (clip != null) into.Add(clip);
            }
        }

        static AnimationClip Clip(string directory, string file)
        {
            string key = directory + file;
            if (Cache.TryGetValue(key, out var held)) return held;
            AnimationClip found = null;
#if UNITY_EDITOR
            foreach (var asset in UnityEditor.AssetDatabase
                         .LoadAllAssetRepresentationsAtPath(directory + file + ".fbx"))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    found = clip;
                    break;
                }
#endif
            Cache[key] = found;
            return found;
        }
    }
}
