using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Which way a man is travelling relative to the way he is facing. A man
    /// with a long gun keeps the muzzle where he is looking and takes his ground
    /// sideways or backwards, so every gait in the rifle set is authored eight ways
    /// rather than one.</summary>
    public enum RifleStep
    {
        Forward, ForwardLeft, ForwardRight,
        Left, Right,
        Backward, BackwardLeft, BackwardRight
    }

    /// <summary>The long-gun wardrobe: the Mixamo Pro Rifle pack, one FBX a take, all
    /// of it retargeted onto the city's Humanoid rigs.
    ///
    /// THE TAKES CARRY THEIR ROOT MOTION, and that is on purpose, exactly as the Synty
    /// gaits do (see CrewKit). Nobody applies root motion - the walker moves his own
    /// transform - but the take's clip.averageSpeed is the oracle that keys his feet to
    /// the ground (PedestrianAgent.ClipPace). An in-place take reads 0 there and the
    /// man falls back to a guessed pace, which is the skate this project keeps out.
    ///
    /// Measured off the pack: walk 1.92 m/s, crouch walk 2.04, run 4.80, sprint 7.20.
    /// The sprint is well over the flee band the crews were tuned to, so a gait dealt
    /// from here is dealt with its own pace, never with somebody else's.
    ///
    /// Editor-only loads through the AssetDatabase, the same contract as CrewKit:
    /// nothing here ships, so nothing needs Resources. A missing pack leaves every
    /// accessor null and simply switches the behaviour that asked off.</summary>
    public static class RifleKit
    {
        const string Dir = "Assets/Animations/Mixamo/Rifle/";

        public static bool Installed => Idle != null;

        // ------------------------------------------------------------- the stands

        /// <summary>Long gun held ready across the body, at ease.</summary>
        public static AnimationClip Idle => Take("idle", ref idle);
        static AnimationClip idle;

        /// <summary>Long gun up, sighted down the barrel.</summary>
        public static AnimationClip IdleAiming => Take("idle aiming", ref idleAiming);
        static AnimationClip idleAiming;

        public static AnimationClip IdleCrouching => Take("idle crouching", ref idleCrouching);
        static AnimationClip idleCrouching;

        public static AnimationClip IdleCrouchingAiming =>
            Take("idle crouching aiming", ref idleCrouchingAiming);
        static AnimationClip idleCrouchingAiming;

        // -------------------------------------------------------------- the gaits

        public static AnimationClip Walk(RifleStep step) => Gait("walk", step);
        public static AnimationClip Run(RifleStep step) => Gait("run", step);
        public static AnimationClip Sprint(RifleStep step) => Gait("sprint", step);
        public static AnimationClip CrouchWalk(RifleStep step) => Gait("walk crouching", step);

        // ------------------------------------------------- the turns and the jump

        public static AnimationClip TurnLeft => Take("turn 90 left", ref turnLeft);
        static AnimationClip turnLeft;
        public static AnimationClip TurnRight => Take("turn 90 right", ref turnRight);
        static AnimationClip turnRight;
        public static AnimationClip CrouchTurnLeft =>
            Take("crouching turn 90 left", ref crouchTurnLeft);
        static AnimationClip crouchTurnLeft;
        public static AnimationClip CrouchTurnRight =>
            Take("crouching turn 90 right", ref crouchTurnRight);
        static AnimationClip crouchTurnRight;

        public static AnimationClip JumpUp => Take("jump up", ref jumpUp);
        static AnimationClip jumpUp;
        public static AnimationClip JumpLoop => Take("jump loop", ref jumpLoop);
        static AnimationClip jumpLoop;
        public static AnimationClip JumpDown => Take("jump down", ref jumpDown);
        static AnimationClip jumpDown;

        // ------------------------------------------------------------ the falling

        /// <summary>The six ways the pack drops a man holding a long gun. Dealt per man
        /// like the crowd's own deaths (CrewKit.Deaths): sixty men do not die one death.</summary>
        public static IReadOnlyList<AnimationClip> Deaths => deaths ??= Gather(
            "death from the front", "death from the back", "death from right",
            "death from front headshot", "death from back headshot",
            "death crouching headshot front");
        static List<AnimationClip> deaths;

        // ------------------------------------------------------------- the loading

        static readonly Dictionary<string, AnimationClip> Cache =
            new Dictionary<string, AnimationClip>();

        static AnimationClip Gait(string gait, RifleStep step) => Clip(gait + " " + Suffix(step));

        /// <summary>The pack's own file names. "walk left" is the strafe; "walk forward
        /// left" is the diagonal - two different takes, and the pack means both.</summary>
        static string Suffix(RifleStep step) => step switch
        {
            RifleStep.Forward => "forward",
            RifleStep.ForwardLeft => "forward left",
            RifleStep.ForwardRight => "forward right",
            RifleStep.Left => "left",
            RifleStep.Right => "right",
            RifleStep.Backward => "backward",
            RifleStep.BackwardLeft => "backward left",
            _ => "backward right"
        };

        static AnimationClip Take(string file, ref AnimationClip held) =>
            held != null ? held : held = Clip(file);

        static List<AnimationClip> Gather(params string[] files)
        {
            var list = new List<AnimationClip>();
            foreach (var file in files)
            {
                var clip = Clip(file);
                if (clip != null) list.Add(clip);
            }
            return list;
        }

        /// <summary>One take by its file name. Mixamo names every take inside the FBX
        /// "mixamo.com", so the FILE is the only address the pack gives us; the import
        /// pass renames each clip to its file name after the fact, and this reads the
        /// first clip in the file either way.</summary>
        static AnimationClip Clip(string file)
        {
            if (Cache.TryGetValue(file, out var held)) return held;
            AnimationClip found = null;
#if UNITY_EDITOR
            foreach (var asset in UnityEditor.AssetDatabase
                         .LoadAllAssetRepresentationsAtPath(Dir + file + ".fbx"))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    found = clip;
                    break;
                }
#endif
            Cache[file] = found;
            return found;
        }
    }
}
