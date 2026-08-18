using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The assets the crews need beyond the crowd's walk and idle - the pistol set
    // out of the Universal Animation Library FBX (Pistol_Idle_Loop, Pistol_Aim_Neutral,
    // Pistol_Shoot, Hit_Chest, Death01), the Gang Warfare muzzle flash, the blood,
    // the shot. Editor-only loads through the AssetDatabase, the same contract as the
    // rest of the road demo: nothing here ships, so nothing needs Resources.
    public static class CrewKit
    {
        const string UalPath = "Assets/Animations/UAL1_Standard.fbx";
        const string PeopleDir = "Assets/Animations/People/";
        const string FlashPath = "Assets/Synty/PolygonGangWarfare/Prefabs/FX/FX_Gunshot_01.prefab";
        const string BloodPath = "Assets/Synty/PolygonParticleFX/Prefabs/FX_BloodSplat_Small_01.prefab";
        const string ImpactPath = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Impact_Small_01.prefab";
        const string ShotPath = "Assets/ci/400 Sounds Pack/Weapons/shot_muffled.wav";
        const string CrackPath = "Assets/ci/400 Sounds Pack/Combat and Gore/slap.wav";

        /// <summary>The crowd's walk and idle plus the gun wardrobe. Missing pieces stay
        /// null and simply switch their behaviour off, the crowd's own rule.</summary>
        public static PedClips Clips()
        {
            var clips = new PedClips
            {
                Walk = PeopleClip("Standard Walk"),
                Idle = PeopleClip("Breathing Idle"),
                Talk = PeopleClip("Standing_Talking"),
                SitLoop = PeopleClip("Sitting_Bench_Idle"), // the seat in the car
                Shout = PeopleClip("Standing_Shouting"),
                PistolIdle = UalClip("Pistol_Idle_Loop"),
                Aim = UalClip("Pistol_Aim_Neutral"),
                Shoot = UalClip("Pistol_Shoot"),
                Hit = UalClip("Hit_Chest"),
                // the Mixamo fall (3.6 s, the man crumples and lies) over the library's
                // brisker Death01 - a death has to read, and the longer one does
                Death = PeopleClip("Death") ?? UalClip("Death01"),
                Jog = UalClip("Jog_Fwd_Loop"),
            };
            if (clips.PistolIdle == null || clips.Aim == null || clips.Shoot == null)
                Debug.LogWarning("[RoadDemo] Pistol clips missing from " + UalPath +
                                 " - armed men will hold their guns but not raise them.");
            return clips;
        }

        /// <summary>Adds the gun wardrobe (and the jog) to a crowd wardrobe the caller
        /// already has; the crowd's own talk clips stay if it brought them.</summary>
        public static PedClips WithArms(PedClips crowd)
        {
            var arms = Clips();
            crowd.PistolIdle = arms.PistolIdle;
            crowd.Aim = arms.Aim;
            crowd.Shoot = arms.Shoot;
            crowd.Hit = arms.Hit;
            crowd.Death = arms.Death;
            crowd.Jog = arms.Jog;
            if (crowd.Talk == null) crowd.Talk = arms.Talk;
            if (crowd.SitLoop == null) crowd.SitLoop = arms.SitLoop;
            if (crowd.Shout == null) crowd.Shout = arms.Shout;
            return crowd;
        }

        // ------------------------------------------------------------- the variety
        //
        // Sixty men do not walk one walk or die one death. Each man draws his own
        // gait, his own fall and his own flinch out of these at spawn (DemoCrews),
        // so a line of hoods is a line of people. Loaded once, kept.

        static List<AnimationClip> walks, deaths, hits, runs;

        public static IReadOnlyList<AnimationClip> Walks =>
            walks ??= Gather(PeopleClip("Standard Walk"), UalClip("Walk_Loop"), UalClip("Walk_Formal_Loop"));

        /// <summary>The runs a man may break into - the library's jog and its flat-out
        /// sprint; the jog listed twice so it is the commoner draw. A crew that runs is
        /// then a crew of different runners, not one runner copied.</summary>
        public static IReadOnlyList<AnimationClip> Runs =>
            runs ??= Gather(UalClip("Jog_Fwd_Loop"), UalClip("Jog_Fwd_Loop"), UalClip("Sprint_Loop"));

        public static IReadOnlyList<AnimationClip> Deaths =>
            deaths ??= Gather(PeopleClip("Death"), UalClip("Death01"));

        public static IReadOnlyList<AnimationClip> Hits =>
            hits ??= Gather(UalClip("Hit_Chest"), UalClip("Hit_Head"));

        /// <summary>The wardrobe with one gait, fall and flinch drawn for one man.</summary>
        public static PedClips Draw(PedClips shared, System.Random rng)
        {
            var clips = shared;
            if (Walks.Count > 0) clips.Walk = Walks[rng.Next(Walks.Count)];
            if (Deaths.Count > 0) clips.Death = Deaths[rng.Next(Deaths.Count)];
            if (Hits.Count > 0) clips.Hit = Hits[rng.Next(Hits.Count)];
            if (Runs.Count > 0) clips.Jog = Runs[rng.Next(Runs.Count)];
            return clips;
        }

        static List<AnimationClip> Gather(params AnimationClip[] candidates)
        {
            var list = new List<AnimationClip>();
            foreach (var c in candidates) if (c != null) list.Add(c);
            return list;
        }

        public static GameObject MuzzleFlash => Load<GameObject>(FlashPath);
        public static GameObject Blood => Load<GameObject>(BloodPath);
        public static GameObject Impact => Load<GameObject>(ImpactPath);
        public static AudioClip Gunshot => Load<AudioClip>(ShotPath);
        public static AudioClip Crack => Load<AudioClip>(CrackPath);

        /// <summary>A Gang Warfare gun by prefab name - the ledger's baked cast first,
        /// the pack folder when the cast has not been baked yet.</summary>
        public static GameObject Weapon(string name) =>
            LivingCity.UI.LedgerModelSet.WeaponNamed(name) ??
            Load<GameObject>("Assets/Synty/PolygonGangWarfare/Prefabs/Weapons/" + name + ".prefab");

        public static AnimationClip PeopleClip(string name) =>
            Load<AnimationClip>(PeopleDir + name + ".anim");

        /// <summary>A take out of the UAL FBX by name - the clips live inside the model
        /// asset, so they are found among its representations, not by path.</summary>
        public static AnimationClip UalClip(string name)
        {
#if UNITY_EDITOR
            // Blender-exported takes arrive as "Armature|Pistol_Shoot": the take name
            // is what follows the bar. Preview clips (__preview__...) are not the take.
            foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(UalPath))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
                if (clip.name == name || clip.name.EndsWith("|" + name))
                    return clip;
            }
#endif
            return null;
        }

        static T Load<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
#else
            return null;
#endif
        }
    }
}
