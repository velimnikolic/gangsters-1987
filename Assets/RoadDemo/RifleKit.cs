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

        /// <summary>The second delivery: the gunplay, the shot and the two turns. A
        /// separate folder because they came down one at a time rather than as the
        /// pack, and each one ships with the preview body baked in - 112 MB apiece,
        /// imported for their motion alone (materials off).</summary>
        const string ShootingDir = "Assets/Animations/Mixamo/Shooting/";

        public static bool Installed => Idle != null;

        /// <summary>Add the coherent Mixamo rifle set without replacing the city gait.
        /// The ordinary Synty Walk/Jog/Sprint remain the movement authority and stay
        /// visible until the weapon is really drawn; CrewWalker then selects these
        /// dedicated rifle slots for presentation only.</summary>
        public static PedClips ForCover(PedClips clips)
        {
            // Do not overwrite the sidearm slots: CoverDemo carries pistols as well
            // as rifles, and a pistol must never inherit a two-handed rifle stand or
            // recoil merely because both weapons share one crew.
            clips.RifleIdle = Idle;
            clips.RifleAim = IdleAiming;
            clips.RifleShoot = ShootRifle;
            clips.RifleCrouch = IdleCrouching;
            clips.RifleWalk = Walk(RifleStep.Forward);
            clips.RifleJog = Run(RifleStep.Forward);
            clips.RifleSprint = Sprint(RifleStep.Forward);
            clips.RifleCrouchWalk = CrouchWalk(RifleStep.Forward);
            clips.RifleWalks = Walks;
            clips.RifleRuns = Runs;
            clips.RifleSprints = Sprints;
            clips.RifleCrouchWalks = CrouchWalks;
            clips.RifleGunplay = GunplayRifle;
            clips.AutomaticShoot = GunplayMachineGun;
            clips.CoverShoot = ShootFromCover;
            clips.LongGunRunUpper = null; // full-body rifle gait already owns both arms
            clips.AuthoredLongGun = clips.RifleIdle != null && clips.RifleAim != null &&
                                    clips.RifleWalk != null && clips.RifleJog != null;
            return clips;
        }

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

        public static AnimationClip[] Walks => walks ??= Gaits("walk");
        public static AnimationClip[] Runs => runs ??= Gaits("run");
        public static AnimationClip[] Sprints => sprints ??= Gaits("sprint");
        public static AnimationClip[] CrouchWalks => crouchWalks ??= Gaits("walk crouching");
        static AnimationClip[] walks, runs, sprints, crouchWalks;

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

        // ------------------------------------------------------------ the shooting

        /// <summary>The long gun worked: the rifle's own gunplay loop and the machine
        /// gun's, the shot, the shot taken from behind cover, and the two turns.</summary>
        public static AnimationClip GunplayRifle => Shot("Gunplay_rifle", ref gunplayRifle);
        static AnimationClip gunplayRifle;
        public static AnimationClip GunplayMachineGun =>
            Shot("Gunplay_machine_gun", ref gunplayMachineGun);
        static AnimationClip gunplayMachineGun;
        public static AnimationClip ShootRifle => Shot("Shoot Rifle", ref shootRifle);
        static AnimationClip shootRifle;
        public static AnimationClip ShootFromCover =>
            Shot("shoot_behind_cover", ref shootFromCover);
        static AnimationClip shootFromCover;
        public static AnimationClip TurnLeftArmed => Shot("Turn Left", ref turnLeftArmed);
        static AnimationClip turnLeftArmed;
        public static AnimationClip TurnRightArmed => Shot("Turn Right", ref turnRightArmed);
        static AnimationClip turnRightArmed;

        // ------------------------------------------------------------ the falling

        /// <summary>The six ways the pack drops a man holding a long gun. Dealt per man
        /// like the crowd's own deaths (CrewKit.Deaths): sixty men do not die one death.</summary>
        public static IReadOnlyList<AnimationClip> Deaths => deaths ??= Gather(
            "death from the front", "death from the back", "death from right",
            "death from front headshot", "death from back headshot",
            "death crouching headshot front");
        static List<AnimationClip> deaths;

        /// <summary>Every take in the pack, in the order a man would want to look
        /// through them: the stands, then the gaits eight ways each, then the turns,
        /// the jump and the falls. This is the roster a bench puts in a list - it is
        /// the file names, so what the list says is what is on disk.</summary>
        public static IReadOnlyList<AnimationClip> All => all ??= Gather(
            "idle", "idle aiming", "idle crouching", "idle crouching aiming",
            "walk forward", "walk forward left", "walk forward right",
            "walk left", "walk right",
            "walk backward", "walk backward left", "walk backward right",
            "run forward", "run forward left", "run forward right",
            "run left", "run right",
            "run backward", "run backward left", "run backward right",
            "sprint forward", "sprint forward left", "sprint forward right",
            "sprint left", "sprint right",
            "sprint backward", "sprint backward left", "sprint backward right",
            "walk crouching forward", "walk crouching forward left", "walk crouching forward right",
            "walk crouching left", "walk crouching right",
            "walk crouching backward", "walk crouching backward left", "walk crouching backward right",
            "turn 90 left", "turn 90 right",
            "crouching turn 90 left", "crouching turn 90 right",
            "jump up", "jump loop", "jump down",
            "death from the front", "death from the back", "death from right",
            "death from front headshot", "death from back headshot",
            "death crouching headshot front",
            "\u0000Gunplay_rifle", "\u0000Gunplay_machine_gun",
            "\u0000Shoot Rifle", "\u0000shoot_behind_cover",
            "\u0000Turn Left", "\u0000Turn Right");
        static List<AnimationClip> all;

        /// <summary>Whether this take is a pose in which the man is actually working
        /// the sights. The distinction belongs to the animation wardrobe, not to a
        /// demo: aiming stands, shots and the eight-way tactical gaits keep their eyes
        /// on the mark; at-ease stands, turns, jumps and falls keep the head authored
        /// by their own clip.
        ///
        /// Mixamo's imported clip is deliberately renamed to its FBX file name (Clip),
        /// so this remains stable across every Humanoid body that wears the take.</summary>
        public static bool IsAimingPose(AnimationClip clip)
        {
            if (clip == null) return false;
            var name = clip.name;
            return StartsWith(name, "walk ") ||
                   StartsWith(name, "run ") ||
                   StartsWith(name, "sprint ") ||
                   Contains(name, "aim") ||
                   Contains(name, "shoot") ||
                   Contains(name, "gunplay");
        }

        /// <summary>Firing takes already author their head motion. The procedural look
        /// is for sustained aiming stands and tactical gaits only; gunplay and shots
        /// still aim the rifle, but do not have their skull overwritten.</summary>
        public static bool TracksTargetWithHead(AnimationClip clip)
        {
            if (!IsAimingPose(clip)) return false;
            var name = clip.name;
            return !Contains(name, "shoot") && !Contains(name, "gunplay");
        }

        static bool StartsWith(string value, string prefix) =>
            value.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase);

        static bool Contains(string value, string part) =>
            value.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        // ------------------------------------------------------------- the loading

        static readonly Dictionary<string, AnimationClip> Cache =
            new Dictionary<string, AnimationClip>();

        static AnimationClip Gait(string gait, RifleStep step) => Clip(gait + " " + Suffix(step));

        static AnimationClip[] Gaits(string gait)
        {
            var clips = new AnimationClip[8];
            for (int i = 0; i < clips.Length; i++)
                clips[i] = Gait(gait, (RifleStep)i);
            return clips;
        }

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

        static AnimationClip Shot(string file, ref AnimationClip held) =>
            held != null ? held : held = Clip(ShootingDir, file);

        /// <summary>The takes in order. A name carrying the shooting mark is looked up
        /// in the second folder - a marker rather than a second list, so the roster
        /// stays one list read top to bottom, which is what a picker shows.</summary>
        const char FromShooting = '\u0000';

        static List<AnimationClip> Gather(params string[] files)
        {
            var list = new List<AnimationClip>();
            foreach (var file in files)
            {
                var shooting = file.Length > 0 && file[0] == FromShooting;
                var clip = shooting ? Clip(ShootingDir, file.Substring(1)) : Clip(file);
                if (clip != null) list.Add(clip);
            }
            return list;
        }

        /// <summary>One take by its file name. Mixamo names every take inside the FBX
        /// "mixamo.com", so the FILE is the only address the pack gives us; the import
        /// pass renames each clip to its file name after the fact, and this reads the
        /// first clip in the file either way.</summary>
        static AnimationClip Clip(string file) => Clip(Dir, file);

        static AnimationClip Clip(string directory, string file)
        {
            var key = directory + file;
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
