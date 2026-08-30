using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static RoadDemo.Composer;

namespace RoadDemo
{
    public static partial class ResidentialBlocks
    {
        const string PeopleAnimations = "Assets/Animations/People/";
        const string AmbientIdle = PeopleAnimations + "Breathing Idle.anim";
        const string AmbientTalk = PeopleAnimations + "Standing_Talking.anim";

        // A deliberately small, civilian-only wardrobe. These are block dressing, never
        // pedestrians: no agent, registry entry, collider, rigidbody or gameplay marker.
        static readonly string[] AmbientBodies =
        {
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Male_02.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Female_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Female_02.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Rich_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Rich_Female_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Characters/Character_Male_Jacket.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Characters/Character_Female_Jacket.prefab",
        };

        readonly struct AmbientSite
        {
            public readonly int I, J;
            public readonly ResidentialLot.Use Use;

            public AmbientSite(int i, int j, ResidentialLot.Use use)
            {
                I = i;
                J = j;
                Use = use;
            }
        }

        /// <summary>
        /// Adds tiny frozen street-life tableaux to a composed residential block. They
        /// look occupied (neighbours talking, somebody waiting outside) but are ordinary
        /// prop hierarchy: their animation is sampled once into the bones and their
        /// Animator is then disabled. Nothing ticks, navigates, collides, reacts or joins
        /// PedestrianRegistry, so recycling a block is their complete lifetime.
        /// </summary>
        static void AmbientPeople(ResidentialLot.Plan plan, Transform root, Stood stood)
        {
            if (plan == null || root == null) return;

            var idle = DemoAssetLoad.Load<AnimationClip>(AmbientIdle);
            var talk = DemoAssetLoad.Load<AnimationClip>(AmbientTalk);
            if (idle == null && talk == null) return;

            var rng = new System.Random(unchecked(plan.Seed * 486187739 + 1987));
            var sites = AmbientSites(plan, rng);
            if (sites.Count == 0) return;

            int wanted = plan.Klass switch
            {
                ResidentialLot.Klass.Corner => 1,
                ResidentialLot.Klass.Row => 2,
                ResidentialLot.Klass.Block => 3,
                _ => 4,
            };
            if ((plan.Klass == ResidentialLot.Klass.Block ||
                 plan.Klass == ResidentialLot.Klass.Court) && rng.NextDouble() < 0.35)
                wanted++;

            var pen = new GameObject("Ambient NPC props (decorative)").transform;
            pen.SetParent(root, false);
            var occupied = new List<Vector3>();

            for (int n = 0; n < sites.Count && stood.People < wanted; n++)
            {
                var site = sites[n];
                float x = (site.I + 0.5f) * ResidentialLot.Cell + Between(rng, -0.65f, 0.65f);
                float z = (site.J + 0.5f) * ResidentialLot.Cell + Between(rng, -0.65f, 0.65f);
                var centre = new Vector3(x, Deck, z);
                if (!Apart(occupied, centre, 6f)) continue;

                bool pair = wanted - stood.People >= 2 && talk != null &&
                            (site.Use == ResidentialLot.Use.Court ||
                             site.Use == ResidentialLot.Use.Yard ||
                             rng.NextDouble() < 0.55);
                if (pair)
                {
                    float yaw = 90f * rng.Next(4) + Between(rng, -12f, 12f);
                    var across = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                    var claim = new Rect(x - 1.45f, z - 1.15f, 2.9f, 2.3f);
                    if (!Room(claim)) continue;

                    var a = centre - across * 0.72f;
                    var b = centre + across * 0.72f;
                    if (!Figure(pen, a, YawTowards(b - a), talk, rng, "Neighbours talking") ||
                        !Figure(pen, b, YawTowards(a - b), talk, rng, "Neighbours talking"))
                        continue;

                    Claim(claim);
                    occupied.Add(centre);
                    stood.People += 2;
                    continue;
                }

                var solo = new Rect(x - 0.75f, z - 0.75f, 1.5f, 1.5f);
                if (!Room(solo)) continue;
                float face = StreetFacing(plan, site, rng);
                if (!Figure(pen, centre, face, idle != null ? idle : talk, rng,
                            site.Use == ResidentialLot.Use.Walkway
                                ? "Waiting outside"
                                : "Neighbour taking a break"))
                    continue;

                Claim(solo);
                occupied.Add(centre);
                stood.People++;
            }

            if (stood.People == 0)
                Object.DestroyImmediate(pen.gameObject);
        }

        static List<AmbientSite> AmbientSites(ResidentialLot.Plan plan, System.Random rng)
        {
            var preferred = new List<AmbientSite>();
            var pavement = new List<AmbientSite>();
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    var use = plan.Ground[i, j];
                    if (use == ResidentialLot.Use.Court || use == ResidentialLot.Use.Yard ||
                        use == ResidentialLot.Use.Paved)
                        preferred.Add(new AmbientSite(i, j, use));
                    else if (use == ResidentialLot.Use.Walkway && FrontageNeighbour(plan, i, j))
                        pavement.Add(new AmbientSite(i, j, use));
                }

            Shuffle(preferred, rng);
            Shuffle(pavement, rng);
            preferred.AddRange(pavement);
            return preferred;
        }

        static bool FrontageNeighbour(ResidentialLot.Plan plan, int i, int j)
        {
            for (int side = 0; side < 4; side++)
            {
                int x = i + ResidentialLot.Step[side, 0];
                int y = j + ResidentialLot.Step[side, 1];
                if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                var use = plan.Ground[x, y];
                if (use == ResidentialLot.Use.Building || use == ResidentialLot.Use.Forecourt ||
                    use == ResidentialLot.Use.Cafe || use == ResidentialLot.Use.Paved)
                    return true;
            }
            return false;
        }

        static bool Figure(Transform parent, Vector3 at, float yaw, AnimationClip pose,
                           System.Random rng, string label)
        {
            string path = AmbientBodies[rng.Next(AmbientBodies.Length)];
            var go = Raise(path, parent);
            if (go == null) return false;

            go.name = label + " (prop)";
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));

            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var body in go.GetComponentsInChildren<Rigidbody>(true))
            {
                body.detectCollisions = false;
                body.isKinematic = true;
            }

            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator != null && pose != null)
            {
                var position = go.transform.position;
                var rotation = go.transform.rotation;
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.enabled = true;
                pose.SampleAnimation(go, Between(rng, 0f, Mathf.Max(0.01f, pose.length)));
                go.transform.SetPositionAndRotation(position, rotation);
                animator.enabled = false;
            }

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    skin.updateWhenOffscreen = false;
                    skin.quality = SkinQuality.Bone2;
                }
            }
            return true;
        }

        static bool Apart(List<Vector3> occupied, Vector3 at, float distance)
        {
            float square = distance * distance;
            for (int i = 0; i < occupied.Count; i++)
                if ((occupied[i] - at).sqrMagnitude < square) return false;
            return true;
        }

        static float StreetFacing(ResidentialLot.Plan plan, AmbientSite site, System.Random rng)
        {
            float cx = plan.W * ResidentialLot.Cell * 0.5f;
            float cz = plan.D * ResidentialLot.Cell * 0.5f;
            var fromCentre = new Vector3((site.I + 0.5f) * ResidentialLot.Cell - cx, 0f,
                                         (site.J + 0.5f) * ResidentialLot.Cell - cz);
            if (fromCentre.sqrMagnitude < 0.01f) return 90f * rng.Next(4);
            return YawTowards(fromCentre) + Between(rng, -20f, 20f);
        }

        static float YawTowards(Vector3 direction) =>
            Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int swap = rng.Next(i + 1);
                (list[i], list[swap]) = (list[swap], list[i]);
            }
        }
    }
}
