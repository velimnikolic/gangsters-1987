using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace GangstersTools
{
    /// <summary>Import and resolver contract for the three loose Mixamo deliveries.
    /// It is deliberately editor-only: the clips remain ordinary referenced assets in
    /// CoverDemo, while this gives the terminal a deterministic answer before Play.</summary>
    public static class MixamoLocomotionAudit
    {
        const string Root = "Assets/Animations/Mixamo/Locomotion";

        [CliCommand("gangsters_cover_locomotion_audit",
            "Validate all 42 CoverDemo Mixamo motions, Humanoid avatars, loops, root pace, sex mapping and pistol directions.",
            MainThreadRequired = true, Tags = new[] { "gangsters", "cover", "animation", "audit" })]
        public static object Run()
        {
            var failures = new List<string>();
            var paths = AssetDatabase.FindAssets("t:Model", new[] { Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            int loops = 0, oneShots = 0, moving = 0;
            if (paths.Length != 42)
                failures.Add($"Expected 42 motion FBXs, found {paths.Length}.");

            foreach (var path in paths)
            {
                string file = Path.GetFileNameWithoutExtension(path);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                {
                    failures.Add(file + ": no ModelImporter.");
                    continue;
                }
                if (importer.animationType != ModelImporterAnimationType.Human ||
                    importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                    failures.Add(file + ": not Humanoid/CreateFromThisModel.");

                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                var avatar = assets.OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                    failures.Add(file + ": missing valid Human Avatar.");

                var clips = assets.OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    .ToArray();
                if (clips.Length != 1)
                {
                    failures.Add($"{file}: expected one motion take, found {clips.Length}.");
                    continue;
                }

                var clip = clips[0];
                if (!string.Equals(clip.name, file, StringComparison.Ordinal))
                    failures.Add($"{file}: imported take is named '{clip.name}'.");

                bool expectedLoop = LivingCity.EditorTools.MixamoLocomotionImportSettings.IsLoop(file);
                var settings = importer.clipAnimations;
                if (settings == null || settings.Length != 1 ||
                    settings[0].loopTime != expectedLoop)
                    failures.Add($"{file}: loop setting is not {expectedLoop}.");
                if (expectedLoop) loops++; else oneShots++;

                string lower = file.ToLowerInvariant();
                bool shouldTravel = lower.Contains("walk") || lower.Contains("run") ||
                                    lower.Contains("strafe");
                var speed = clip.averageSpeed;
                float horizontal = new Vector2(speed.x, speed.z).magnitude;
                if (!Finite(horizontal))
                    failures.Add(file + ": non-finite root pace.");
                else if (shouldTravel)
                {
                    moving++;
                    if (horizontal < 0.15f)
                        failures.Add($"{file}: moving take exposes only {horizontal:F2} m/s root pace.");
                }
            }

            var seed = new AnimationClip { name = "rifle-sentinel" };
            try
            {
                var initial = new PedClips
                {
                    RifleIdle = seed,
                    RifleAim = seed,
                    RifleWalk = seed,
                    RifleJog = seed,
                    AuthoredLongGun = true,
                };
                var female = MixamoLocomotionKit.ForBody(initial, true);
                var male = MixamoLocomotionKit.ForBody(initial, false);

                RequireBody(female, "mixamo-female", "/Female/walking.fbx",
                    "/Female/running.fbx", failures);
                RequireBody(male, "mixamo-male", "/Male/walking.fbx",
                    "/Male/standard run.fbx", failures);
                if (female.RifleIdle != seed || female.RifleAim != seed ||
                    female.RifleWalk != seed || female.RifleJog != seed ||
                    male.RifleIdle != seed || male.RifleAim != seed ||
                    male.RifleWalk != seed || male.RifleJog != seed)
                    failures.Add("Sex-specific basic wardrobe overwrote rifle slots.");

                RequirePistolSet(female.PistolWalks, "walk", failures);
                RequirePistolSet(female.PistolRuns, "run", failures);
                if (!female.AuthoredSidearmLocomotion || !male.AuthoredSidearmLocomotion)
                    failures.Add("Complete pistol delivery did not enable sidearm locomotion.");

                if (!EndsWith(female.TurnLeft, "/Female/left turn.fbx") ||
                    !EndsWith(female.TurnRight, "/Female/right turn.fbx"))
                    failures.Add("Female resolver did not select the root-yaw quarter turns.");
                if (!EndsWith(male.TurnLeft, "/Male/left turn 90.fbx") ||
                    !EndsWith(male.TurnRight, "/Male/right turn 90.fbx"))
                    failures.Add("Male resolver did not select the 90-degree turns.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(seed);
            }

            int resolved = MixamoLocomotionKit.All.Count;
            if (resolved != 42)
                failures.Add($"Runtime resolver reached {resolved}/42 delivered takes.");

            return new
            {
                passed = failures.Count == 0,
                assets = paths.Length,
                resolved,
                loops,
                oneShots,
                moving,
                failures = failures.ToArray(),
            };
        }

        static void RequireBody(PedClips clips, string label, string walkPath,
            string runPath, List<string> failures)
        {
            if (!clips.AuthoredBasicLocomotion || clips.BasicLocomotionLabel != label)
                failures.Add(label + ": basic locomotion flag/label missing.");
            if (!EndsWith(clips.Walk, walkPath) || !EndsWith(clips.Jog, runPath))
                failures.Add(label + ": wrong sex-specific walk/run mapping.");
        }

        static void RequirePistolSet(AnimationClip[] set, string pace,
            List<string> failures)
        {
            if (set == null || set.Length != 8 || set.Any(clip => clip == null))
            {
                failures.Add("Pistol " + pace + ": incomplete eight-way mapping.");
                return;
            }
            var left = set[(int)RifleStep.Left];
            var right = set[(int)RifleStep.Right];
            if (left == right || left.averageSpeed.x >= right.averageSpeed.x - 0.05f)
                failures.Add("Pistol " + pace + ": left/right root directions are not distinct.");
        }

        static bool EndsWith(AnimationClip clip, string suffix) =>
            clip != null && AssetDatabase.GetAssetPath(clip)
                .EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
