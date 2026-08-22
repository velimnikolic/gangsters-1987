using System.Collections.Generic;
using LivingCity.Personnel;
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
        const string ShotDir = "Assets/Audio/Weapons/";

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
                Crouch = UalClip("Crouch_Idle_Loop"),
                Ride = Ride,
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
            crowd.Crouch = arms.Crouch;
            if (crowd.Ride == null) crowd.Ride = arms.Ride;
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

        /// <summary>The crowd's cower - down behind whatever is nearest, head in.</summary>
        public static AnimationClip Crouch => crouch ??= UalClip("Crouch_Idle_Loop");
        static AnimationClip crouch;

        /// <summary>Astride, hands out in front of him: the library's Driving_Loop, which
        /// was made for a steering wheel and does for a handlebar because BikePose writes
        /// the arms and the legs over it anyway. What is wanted from the clip is the one
        /// thing the pose does not touch - a pelvis that is sitting down, and a man who
        /// goes on breathing while he sits. The bench sit is the stand-in.</summary>
        public static AnimationClip Ride => ride != null ? ride
            : ride = UalClip("Driving_Loop") ?? PeopleClip("Sitting_Bench_Idle");
        static AnimationClip ride;

        /// <summary>The civilian wardrobe: the crowd's own clips plus a run, a flinch,
        /// a fall and the cower - so a bystander can bolt, be hit, and go down.</summary>
        public static PedClips ForCrowd(PedClips crowd, System.Random rng)
        {
            var clips = crowd;
            if (Runs.Count > 0) clips.Jog = Runs[rng.Next(Runs.Count)];
            if (Hits.Count > 0) clips.Hit = Hits[rng.Next(Hits.Count)];
            if (Deaths.Count > 0) clips.Death = Deaths[rng.Next(Deaths.Count)];
            clips.Crouch = Crouch;
            return clips;
        }

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
        /// <summary>The reports for one weapon, drawn at random per shot. Real
        /// recordings of the gun the armoury actually sells - a .45 and a .38 for the
        /// pistols, two 12 gauges, a Swedish K, an AK, a PPSh - cut by
        /// Tools/audio/import_sounds.py, which is where the choices are argued.
        ///
        /// Empty for anything that is not a firearm, and empty is not an error: the
        /// crews already treat a missing clip as a silent shot.</summary>
        public static AudioClip[] Gunshots(EquipmentKind kind)
        {
            if (_shots.TryGetValue(kind, out var cached)) return cached;
            var clips = Numbered(ShotPrefix(kind));
            _shots[kind] = clips;
            return clips;
        }

        /// <summary>Every firearm's set at once, in the shape DemoCrews serialises.</summary>
        public static DemoCrews.WeaponSounds[] GunshotSets()
        {
            var sets = new List<DemoCrews.WeaponSounds>();
            foreach (EquipmentKind kind in System.Enum.GetValues(typeof(EquipmentKind)))
            {
                var clips = Gunshots(kind);
                if (clips.Length > 0)
                    sets.Add(new DemoCrews.WeaponSounds { Kind = kind, Clips = clips });
            }
            return sets.ToArray();
        }

        /// <summary>The round going past: a whip crack, which is the same physics.</summary>
        public static AudioClip Crack => Load<AudioClip>(ShotDir + "bullet_crack.wav");

        static readonly Dictionary<EquipmentKind, AudioClip[]> _shots = new();

        static string ShotPrefix(EquipmentKind kind) => kind switch
        {
            // Two pistols in a coat are the same gun twice, so they sound like one.
            EquipmentKind.Pistol or EquipmentKind.TwinPistols => "pistol",
            EquipmentKind.Shotgun => "shotgun",
            EquipmentKind.MachinePistol => "machinepistol",
            EquipmentKind.Rifle => "rifle",
            EquipmentKind.TommyGun => "tommygun",
            _ => null,
        };

        /// <summary>Loads name_1, name_2 ... until one is missing. The counts differ per
        /// weapon and change whenever the bake picks up another usable report, so
        /// counting here rather than listing means a re-bake needs no edit.</summary>
        static AudioClip[] Numbered(string name)
        {
            if (string.IsNullOrEmpty(name)) return System.Array.Empty<AudioClip>();
            var list = new List<AudioClip>();
            for (int i = 1; ; i++)
            {
                var clip = Load<AudioClip>($"{ShotDir}{name}_{i}.wav");
                if (!clip) break;
                list.Add(clip);
            }
            return list.ToArray();
        }

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
            return RoadDemo.DemoAssetLoad.Load<T>(path);
#else
            return null;
#endif
        }
    }
}
