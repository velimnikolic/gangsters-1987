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
        // The Synty animation packs: locomotion (walk/run/idle/crouch) and the idle
        // fidgets. RootMotion takes are used for the gaits ON PURPOSE - nobody applies
        // root motion (the walker moves the transform himself), but the take carries
        // its true metres-a-second in clip.averageSpeed, which is what keys the feet
        // to the ground (PedestrianAgent.ClipPace). The in-place takes read as 0 and
        // would fall back to a guessed pace.
        const string LocoDir = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/";
        const string IdleDir = "Assets/Synty/AnimationIdles/Animations/Polygon/";
        const string FlashPath = "Assets/Synty/PolygonGangWarfare/Prefabs/FX/FX_Gunshot_01.prefab";
        const string BloodPath = "Assets/Synty/PolygonParticleFX/Prefabs/FX_BloodSplat_Small_01.prefab";
        const string ImpactPath = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Impact_Small_01.prefab";
        const string FirePath = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Fire_Big_01.prefab";
        const string FireBurstPath = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Fire_Explosion_01.prefab";
        const string ExhaustPath = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Smoke_White_Small_01.prefab";
        const string EngineSmokePath = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Smoke_Black_Small_01.prefab";
        const string ShotDir = "Assets/Audio/Weapons/";

        /// <summary>The crowd's walk and idle plus the gun wardrobe. Missing pieces stay
        /// null and simply switch their behaviour off, the crowd's own rule.</summary>
        public static PedClips Clips()
        {
            var clips = new PedClips
            {
                Walk = StockWalk,
                Idle = StockIdle,
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
                Jog = StockRun,
                Sprint = StockSprint,
                Crouch = Crouch,
                CrouchWalk = CrouchWalk,
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
            crowd.Sprint = arms.Sprint;
            crowd.Crouch = arms.Crouch;
            crowd.CrouchWalk = arms.CrouchWalk;
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

        static List<AnimationClip> walks, deaths, hits, runs, idles;

        /// <summary>The crowd's stock gait and stand - the Synty locomotion pack's,
        /// with the old Mixamo clips as the net if the pack is ever missing.</summary>
        public static AnimationClip StockWalk => stockWalk != null ? stockWalk
            : stockWalk = Loco("Masculine/Locomotion/Walk/A_Walk_F_RootMotion_Masc")
                          ?? PeopleClip("Standard Walk");
        static AnimationClip stockWalk;

        public static AnimationClip StockIdle => stockIdle != null ? stockIdle
            : stockIdle = Loco("Masculine/Idle/A_Idle_Standing_Masc")
                          ?? PeopleClip("Breathing Idle");
        static AnimationClip stockIdle;

        public static AnimationClip StockRun => stockRun != null ? stockRun
            : stockRun = Loco("Masculine/Locomotion/Run/A_Run_F_RootMotion_Masc")
                         ?? UalClip("Jog_Fwd_Loop");
        static AnimationClip stockRun;

        /// <summary>The gaits a crew man may draw. The pack ships ONE masculine walk,
        /// so the pool is that walk alone - a line of hoods stays out of lockstep by
        /// per-man cadence and phase (WalkCadence, ScatterPhase), not by wardrobe.
        /// The feminine walk is deliberately NOT in here: this pool is dealt only to
        /// hoods (Draw), and a mob man with the pack's swaying gait reads wrong.</summary>
        public static IReadOnlyList<AnimationClip> Walks =>
            walks ??= Gather(StockWalk);

        /// <summary>The stands a man at ease may be dealt. Five hoods holding a corner
        /// all breathing one clip is the same fault as five hoods walking one walk, and
        /// it is worse to look at because they are not going anywhere: the eye has time
        /// to find the pair that are in step. The plain stand is listed twice so it
        /// stays the commoner one and a corner still reads as this town's corner; the
        /// idle pack's folded arms and hands-on-hips are the rarer draws.</summary>
        public static IReadOnlyList<AnimationClip> Idles =>
            idles ??= Gather(StockIdle, StockIdle,
                             IdleClip("Masculine/Base/Stances/A_POLY_IDL_Base_Masc"),
                             IdleClip("Masculine/ArmsFolded/Stances/A_POLY_IDL_ArmsFolded_Casual_Loop_Masc"),
                             IdleClip("Masculine/HandsOnHips/Stances/A_POLY_IDL_HandsOnHips_Base_Loop_Masc"));

        /// <summary>The run a man breaks into. One run, and ONLY one run.
        ///
        /// Sprint_Loop was in here as the rarer draw and it was a mistake, because a run
        /// clip is not just a look - the pace is read off the clip's own root motion and
        /// the man is then MOVED at it (CrewWalker.SetJog, CivilianAgent.TieRun). The
        /// sprint covers about twice the ground a jog does, so the one man in three who
        /// drew it ran at nearly twice his crew's speed, in a flat-out sprint clip,
        /// down a pavement. Whatever variety that bought is not worth one man of a crew
        /// leaving the rest of it behind at a dead run.
        ///
        /// The variety a run wants is a stride of its own and a rate of its own, and
        /// those are dealt per man anyway (CrewWalker.BreakIntoRun).</summary>
        public static IReadOnlyList<AnimationClip> Runs =>
            runs ??= Gather(StockRun);

        /// <summary>The crowd's cower - down behind whatever is nearest, head in.</summary>
        public static AnimationClip Crouch => crouch != null ? crouch
            : crouch = Loco("Masculine/Idle/A_Idle_Crouching_Masc") ?? UalClip("Crouch_Idle_Loop");
        static AnimationClip crouch;

        /// <summary>The flat-out sprint - the pace a man truly running for his life
        /// covers ground at. Kept OUT of the Runs pool on purpose (see Runs): as a
        /// dealt gait it sent one man of a crew off at twice his mates' speed. As a
        /// POSE of its own it is safe - only the flee reaches for it, and a fleeing
        /// man answers to nobody's formation.</summary>
        public static AnimationClip StockSprint => stockSprint != null ? stockSprint
            : stockSprint = Loco("Masculine/Locomotion/Sprint/A_Sprint_F_RootMotion_Masc");
        static AnimationClip stockSprint;

        // ------------------------------------------------------- the transitions
        //
        // The foot-planted joins between the gaits: set off, pull up, turn on the
        // spot. THE ROOTMOTION TAKES, for the same reason the gaits use them and one
        // more besides. The pace reason is the gaits': the take carries its true
        // metres a second, and a start clip that says how fast it gets going is what
        // lets the man's ground speed ease in under his own feet.
        //
        // The turn reason is this layer's own, and it is the whole trick. A turn
        // clip's rotation is either in the ROOT (the RootMotion take) or in the HIPS
        // (the in-place take). Nobody applies root motion here - the walker turns his
        // own transform - so a RootMotion take's turn is extracted and thrown away,
        // leaving stepping feet and a body that does not turn itself, and the code is
        // free to turn the whole man by exactly the angle it meant. The in-place take
        // would turn him TWICE: once in the hips, once on the transform, and then
        // snap him back when the clip ended.

        const string TransDir = "Masculine/Transitions/";

        static AnimationClip[] toWalk, toRun, walkStop, runStop, turns;

        /// <summary>Setting off toward a heading this many signed degrees off his
        /// facing (negative left): the pack's F / 90L / 90R / 180L / 180R starts.
        /// Null when the pack is missing - the caller falls back to the plain
        /// crossfade, which is what every walker in the town had before.</summary>
        public static AnimationClip StartMove(bool walk, float signedAngle)
        {
            var set = walk
                ? toWalk ??= new[]
                {
                    Loco(TransDir + "Idle_ToWalk/A_Idle_ToWalkFRootMotion_Masc"),
                    Loco(TransDir + "Idle_ToWalk/A_Idle_ToWalk90LRootMotion_Masc"),
                    Loco(TransDir + "Idle_ToWalk/A_Idle_ToWalk90RRootMotion_Masc"),
                    Loco(TransDir + "Idle_ToWalk/A_Idle_ToWalk180LRootMotion_Masc"),
                    Loco(TransDir + "Idle_ToWalk/A_Idle_ToWalk180RRootMotion_Masc"),
                }
                : toRun ??= new[]
                {
                    Loco(TransDir + "Idle_ToRun/A_Idle_ToRunFRootMotion_Masc"),
                    Loco(TransDir + "Idle_ToRun/A_Idle_ToRun90LRootMotion_Masc"),
                    Loco(TransDir + "Idle_ToRun/A_Idle_ToRun90RRootMotion_Masc"),
                    Loco(TransDir + "Idle_ToRun/A_Idle_ToRun180LRootMotion_Masc"),
                    Loco(TransDir + "Idle_ToRun/A_Idle_ToRun180RRootMotion_Masc"),
                };
            return set[Quadrant(signedAngle)] ?? set[0];
        }

        /// <summary>Pulling up out of the gait onto a stand - either foot's plant,
        /// dealt at random so a queue does not halt on one leg together.</summary>
        public static AnimationClip StopMove(bool walk)
        {
            var set = walk
                ? walkStop ??= new[]
                {
                    Loco(TransDir + "Walk_ToIdle/A_Walk_ToIdleF_LFootRootMotion_Masc"),
                    Loco(TransDir + "Walk_ToIdle/A_Walk_ToIdleF_RFootRootMotion_Masc"),
                }
                : runStop ??= new[]
                {
                    Loco(TransDir + "Run_ToIdle/A_Run_ToIdleF_LFootRootMotion_Masc"),
                    Loco(TransDir + "Run_ToIdle/A_Run_ToIdleF_RFootRootMotion_Masc"),
                };
            return set[Random.value < 0.5f ? 0 : 1] ?? set[0];
        }

        /// <summary>Turning on the spot, this many signed degrees (negative left):
        /// stepping feet under a turn the code is already making. 90 or 180.</summary>
        public static AnimationClip TurnOnSpot(float signedAngle)
        {
            turns ??= new[]
            {
                null,   // the F slot a start has; a turn of nothing is not a turn
                Loco("Masculine/Locomotion/Turn/A_Turn_Standing_90LRootMotion_Masc"),
                Loco("Masculine/Locomotion/Turn/A_Turn_Standing_90RRootMotion_Masc"),
                Loco("Masculine/Locomotion/Turn/A_Turn_Standing_180LRootMotion_Masc"),
                Loco("Masculine/Locomotion/Turn/A_Turn_Standing_180RRootMotion_Masc"),
            };
            return turns[Quadrant(signedAngle)];
        }

        /// <summary>Which of the five authored angles a wanted turn is nearest:
        /// 0 straight on, 1/2 a quarter turn left/right, 3/4 a half turn either way.
        /// The clip is stretched onto the true angle by whoever plays it, so the
        /// bands only have to pick the nearest shape.</summary>
        static int Quadrant(float signedAngle) =>
            Mathf.Abs(signedAngle) < 50f ? 0
            : Mathf.Abs(signedAngle) < 130f ? (signedAngle < 0f ? 1 : 2)
            : (signedAngle < 0f ? 3 : 4);

        /// <summary>A step to the side, taken from a standstill: the pack's standing
        /// shuffles, one per direction, in the man's OWN frame (his forward is +Z).
        /// About 0.62 metres a second over two thirds of a second, which is what a
        /// person does when somebody crowds him - not a walk, a shift of the feet.</summary>
        public static AnimationClip Shuffle(Vector3 localDir)
        {
            shuffles ??= new[]
            {
                Loco("Masculine/Locomotion/Shuffle/A_Shuffle_Standing_F_RootMotion_Masc"),
                Loco("Masculine/Locomotion/Shuffle/A_Shuffle_Standing_B_RootMotion_Masc"),
                Loco("Masculine/Locomotion/Shuffle/A_Shuffle_Standing_L_RootMotion_Masc"),
                Loco("Masculine/Locomotion/Shuffle/A_Shuffle_Standing_R_RootMotion_Masc"),
            };
            int pick = Mathf.Abs(localDir.z) >= Mathf.Abs(localDir.x)
                ? (localDir.z >= 0f ? 0 : 1)
                : (localDir.x < 0f ? 2 : 3);
            return shuffles[pick];
        }
        static AnimationClip[] shuffles;

        /// <summary>Moving while he is down behind something - the pack's crouched
        /// forward strafe. What a man crosses the last few metres to a bin on with
        /// rounds in the air, instead of strolling up to it at full height.</summary>
        public static AnimationClip CrouchWalk => crouchWalk != null ? crouchWalk
            : crouchWalk = Loco("Masculine/Locomotion/Crouch/A_Crouch_FwdStrafeF_RootMotion_Masc");
        static AnimationClip crouchWalk;

        // ------------------------------------------------------- the little life
        //
        // One-shots and stance loops out of the idle pack: the fidgets a man stood
        // waiting spends, the gestures of a chat, the scared looks, the doorstep
        // snack. Loaded once each; a missing pack leaves the pools empty and every
        // behaviour that draws on them simply never fires - the crowd's own rule.

        const string ActDir = "Masculine/";

        static List<AnimationClip> fidgets, crewFidgets, speakGestures, listenGestures,
            scaredLooks, backLooks, leanLoops, snacks;

        /// <summary>What a citizen does with a wait: taps a foot, checks the watch,
        /// yawns, stretches, shifts his weight, has a look up the street.</summary>
        public static IReadOnlyList<AnimationClip> Fidgets =>
            fidgets ??= Gather(
                IdleClip(ActDir + "Bored/Actions/A_POLY_IDL_Bored_FootTap_Masc"),
                IdleClip(ActDir + "Bored/Actions/A_POLY_IDL_Bored_SwingArms_Masc"),
                IdleClip(ActDir + "CheckWatch/Actions/A_POLY_IDL_CheckWatch_Masc"),
                IdleClip(ActDir + "Yawn/Actions/A_POLY_IDL_Yawn_Masc"),
                IdleClip(ActDir + "Stretch/Actions/A_POLY_IDL_Stretch_Arms_Masc"),
                IdleClip(ActDir + "Stretch/Actions/A_POLY_IDL_Stretch_Shoulders_Masc"),
                IdleClip(ActDir + "WeightShift/Actions/A_POLY_IDL_WeightShift_L_Masc"),
                IdleClip(ActDir + "WeightShift/Actions/A_POLY_IDL_WeightShift_R_Masc"),
                IdleClip(ActDir + "Look/Actions/A_POLY_IDL_Look_L_Masc"),
                IdleClip(ActDir + "Look/Actions/A_POLY_IDL_Look_R_Masc"));

        /// <summary>A hood's version of the same wait: the citizen's fidgets plus a
        /// kick at the ground and a swig off the flask.</summary>
        public static IReadOnlyList<AnimationClip> CrewFidgets
        {
            get
            {
                if (crewFidgets == null)
                {
                    crewFidgets = new List<AnimationClip>(Fidgets);
                    var extra = Gather(
                        IdleClip(ActDir + "Bored/Actions/A_POLY_IDL_Bored_KickGround_Masc"),
                        IdleClip(ActDir + "Drink/Actions/A_POLY_IDL_Drink_Swig_Masc"));
                    crewFidgets.AddRange(extra);
                }
                return crewFidgets;
            }
        }

        /// <summary>The hands of a man with the floor: a point up the street, a
        /// thumb over the shoulder, a shake of the head at all of it.</summary>
        public static IReadOnlyList<AnimationClip> SpeakGestures =>
            speakGestures ??= Gather(
                IdleClip(ActDir + "PointHand/Actions/A_POLY_IDL_PointHand_Index_F_Small_Masc"),
                IdleClip(ActDir + "PointHand/Actions/A_POLY_IDL_PointHand_Thumb_L_Masc"),
                IdleClip(ActDir + "PointHand/Actions/A_POLY_IDL_PointHand_Thumb_R_Masc"),
                IdleClip(ActDir + "HeadShake/Actions/A_POLY_IDL_HeadShake_Large_Masc"));

        /// <summary>The man listening: nods along, or does not think much of it.</summary>
        public static IReadOnlyList<AnimationClip> ListenGestures =>
            listenGestures ??= Gather(
                IdleClip(ActDir + "HeadNod/Actions/A_POLY_IDL_HeadNod_Small_Masc"),
                IdleClip(ActDir + "HeadNod/Actions/A_POLY_IDL_HeadNod_Large_Masc"),
                IdleClip(ActDir + "HeadShake/Actions/A_POLY_IDL_HeadShake_Small_Masc"),
                IdleClip(ActDir + "HeadShake/Actions/A_POLY_IDL_HeadShake_Disappointed_Masc"));

        /// <summary>Two who stop for a word open with it.</summary>
        public static AnimationClip Greet => greet != null ? greet
            : greet = IdleClip(ActDir + "HeadNod/Actions/A_POLY_IDL_HeadNod_Greet_Masc");
        static AnimationClip greet;

        /// <summary>And close it with one of these. The pack authors a wave as an
        /// Enter/Loop/Exit stance; the loop alone, run once, is a wave - which is all
        /// a parting needs.</summary>
        public static IReadOnlyList<AnimationClip> Waves =>
            waves ??= Gather(
                IdleClip(ActDir + "Wave/Stances/A_POLY_IDL_Wave_Small_Loop_Masc"),
                IdleClip(ActDir + "HeadNod/Actions/A_POLY_IDL_HeadNod_Thanks_Masc"));
        static List<AnimationClip> waves;

        /// <summary>A frightened look thrown to one side - the beat of taking in
        /// where the bang came from.</summary>
        public static IReadOnlyList<AnimationClip> ScaredLooks =>
            scaredLooks ??= Gather(
                IdleClip(ActDir + "Look/Actions/A_POLY_IDL_Look_L_Scared_Masc"),
                IdleClip(ActDir + "Look/Actions/A_POLY_IDL_Look_R_Scared_Masc"));

        /// <summary>A look thrown over the shoulder - the man who has just stopped
        /// running checking what is still behind him.</summary>
        public static IReadOnlyList<AnimationClip> BackLooks =>
            backLooks ??= Gather(
                IdleClip(ActDir + "Look/Actions/A_POLY_IDL_Look_L_Behind_Masc"),
                IdleClip(ActDir + "Look/Actions/A_POLY_IDL_Look_R_Behind_Masc"));

        /// <summary>Hands up, don't: the beat before a bystander breaks.</summary>
        public static AnimationClip Plead => plead != null ? plead
            : plead = IdleClip(ActDir + "Plead/Actions/A_POLY_IDL_Plead_F_Masc");
        static AnimationClip plead;

        /// <summary>Down on the knees with the hands together - what a share of the
        /// crowd does under fire instead of the crouch.</summary>
        public static AnimationClip PrayKneel => prayKneel != null ? prayKneel
            : prayKneel = IdleClip(ActDir + "Pray/Stances/A_POLY_IDL_Pray_Kneeling_Loop_Masc");
        static AnimationClip prayKneel;

        /// <summary>Squared right up in somebody's face - the arguing half of a
        /// shouting match.</summary>
        public static AnimationClip AggressiveLoop => aggressive != null ? aggressive
            : aggressive = IdleClip(ActDir + "Posture/Stances/A_POLY_IDL_Posture_Aggressive_Loop_Masc");
        static AnimationClip aggressive;

        /// <summary>The sway of a man who has had a few - worn on a doorstep now
        /// and then before the walk home.</summary>
        public static AnimationClip DrunkSway => drunkSway != null ? drunkSway
            : drunkSway = IdleClip(ActDir + "Sway/Stances/A_POLY_IDL_Sway_Drunk_Loop_Masc");
        static AnimationClip drunkSway;

        /// <summary>Back against the wall: the loops a hood wears holding a corner
        /// with something solid behind him.</summary>
        public static IReadOnlyList<AnimationClip> LeanLoops =>
            leanLoops ??= Gather(
                IdleClip(ActDir + "Lean/Stances/Base/A_POLY_IDL_Lean_B_Base_Loop_Masc"),
                IdleClip(ActDir + "Lean/Stances/ArmsFolded/A_POLY_IDL_Lean_B_ArmsFolded_Loop_Masc"),
                IdleClip(ActDir + "Lean/Stances/ArmsFolded/A_POLY_IDL_Lean_B_ArmsFoldedLegUp_Loop_Masc"));

        /// <summary>Something from the counter, finished on the doorstep.</summary>
        public static IReadOnlyList<AnimationClip> Snacks =>
            snacks ??= Gather(
                IdleClip(ActDir + "Eat/Actions/A_POLY_IDL_Eat_Small_Masc"),
                IdleClip(ActDir + "Eat/Actions/A_POLY_IDL_Eat_Large_Masc"),
                IdleClip(ActDir + "Drink/Actions/A_POLY_IDL_Drink_Sip_Masc"));

        /// <summary>Astride, hands out in front of him: the library's Driving_Loop, which
        /// was made for a steering wheel and does for a handlebar because BikePose writes
        /// the arms and the legs over it anyway. What is wanted from the clip is the one
        /// thing the pose does not touch - a pelvis that is sitting down, and a man who
        /// goes on breathing while he sits. The bench sit is the stand-in.</summary>
        public static AnimationClip Ride => ride != null ? ride
            : ride = UalClip("Driving_Loop") ?? PeopleClip("Sitting_Bench_Idle");
        static AnimationClip ride;

        /// <summary>Off a machine and through the air: the locomotion pack's long fall,
        /// arms out, which is what a man thrown off a motorcycle is doing until the road
        /// arrives. There is no thrown-off-a-bike take anywhere in the project and there
        /// does not need to be - the flight itself is the transform's (RiderSpill), and
        /// the clip only has to stop him looking like he is standing up in mid-air.</summary>
        public static AnimationClip Fall => fall != null ? fall
            : fall = Loco("Masculine/InAir/A_InAir_FallLarge_Masc")
                     ?? Loco("Masculine/InAir/A_InAir_FallShort_Masc");
        static AnimationClip fall;

        /// <summary>Hitting the ground and getting up off it. The pack's hard landing:
        /// it takes a man down onto his hands and stands him back up, which is the
        /// nearest thing the project owns to picking yourself up out of the road, and
        /// it is what the two who walk away from a spill play (RiderSpill).</summary>
        public static AnimationClip Land => land != null ? land
            : land = Loco("Masculine/InAir/A_Land_IdleHard_Masc")
                     ?? Loco("Masculine/InAir/A_Land_IdleMedium_Masc");
        static AnimationClip land;

        /// <summary>A machine burning where it lies, and the sparks it throws while it
        /// is still sliding (BikeSpill).</summary>
        public static GameObject Fire => _fire != null ? _fire : _fire = Load<GameObject>(FirePath);
        public static GameObject FireBurst => _fireBurst != null ? _fireBurst : _fireBurst = Load<GameObject>(FireBurstPath);
        static GameObject _fire, _fireBurst;

        /// <summary>The two smokes a car makes. Both are the pack's mesh puffs, whose
        /// mesh measures 0.19m across, so every size in CarSmoke is read against that
        /// and not against a metre - the pack's own "Small" preset starts them at three
        /// to six, which is a bonfire, not a tailpipe.
        ///
        /// White for what a running engine puts out of the back, black for what a shot
        /// one puts out of the front.</summary>
        public static GameObject Exhaust => _exhaust != null ? _exhaust : _exhaust = Load<GameObject>(ExhaustPath);
        public static GameObject EngineSmoke => _engineSmoke != null ? _engineSmoke : _engineSmoke = Load<GameObject>(EngineSmokePath);
        static GameObject _exhaust, _engineSmoke;

        /// <summary>The civilian wardrobe: the crowd's own clips plus a run, a flinch,
        /// a fall and the cower - so a bystander can bolt, be hit, and go down.</summary>
        public static PedClips ForCrowd(PedClips crowd, System.Random rng)
        {
            var clips = crowd;
            if (Runs.Count > 0) clips.Jog = Runs[rng.Next(Runs.Count)];
            if (Hits.Count > 0) clips.Hit = Hits[rng.Next(Hits.Count)];
            if (Deaths.Count > 0) clips.Death = Deaths[rng.Next(Deaths.Count)];
            // the flat-out run a bystander with a gun going off beside him uses. Safe
            // to hand to everybody because a civilian answers to no formation: what
            // makes the sprint a hazard is a CREW, one of whose men draws it and
            // leaves the rest behind (see Runs).
            clips.Sprint = StockSprint;
            clips.Crouch = Crouch;
            return clips;
        }

        public static IReadOnlyList<AnimationClip> Deaths =>
            deaths ??= Gather(PeopleClip("Death"), UalClip("Death01"));

        public static IReadOnlyList<AnimationClip> Hits =>
            hits ??= Gather(UalClip("Hit_Chest"), UalClip("Hit_Head"));

        /// <summary>The wardrobe with one gait, stand, fall and flinch drawn for one man.</summary>
        public static PedClips Draw(PedClips shared, System.Random rng)
        {
            var clips = shared;
            if (Walks.Count > 0) clips.Walk = Walks[rng.Next(Walks.Count)];
            if (Idles.Count > 0) clips.Idle = Idles[rng.Next(Idles.Count)];
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

        // each an AssetDatabase read per shot before they were held: the flash, the
        // blood and the impact are asked for by every round fired
        public static GameObject MuzzleFlash => _muzzleFlash != null ? _muzzleFlash : _muzzleFlash = Load<GameObject>(FlashPath);
        public static GameObject Blood => _blood != null ? _blood : _blood = Load<GameObject>(BloodPath);
        public static GameObject Impact => _impact != null ? _impact : _impact = Load<GameObject>(ImpactPath);
        static GameObject _muzzleFlash, _blood, _impact;

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
        public static AudioClip Crack => _crack != null ? _crack : _crack = Load<AudioClip>(ShotDir + "bullet_crack.wav");
        static AudioClip _crack;

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

        /// <summary>A locomotion-pack take by its path under the Polygon rig folder.</summary>
        static AnimationClip Loco(string rel) => FbxClip(LocoDir + rel + ".fbx");

        /// <summary>An idle-pack take by its path under the Polygon rig folder.</summary>
        static AnimationClip IdleClip(string rel) => FbxClip(IdleDir + rel + ".fbx");

        /// <summary>The one take a Synty animation FBX holds - found among the model
        /// asset's representations like the UAL takes, skipping the editor preview.</summary>
        static AnimationClip FbxClip(string path)
        {
#if UNITY_EDITOR
            foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
#endif
            return null;
        }

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
