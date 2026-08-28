using UnityEngine;
using UnityEngine.Rendering.Universal;
using static RoadDemo.Composer;

namespace RoadDemo
{
    public static partial class ResidentialBlocks
    {
        const string Decals = "Assets/Synty/PolygonPalmCity/Materials/Decals/";
        const string RoadPatch = CityEnv + "SM_Env_Road_Patch_01.prefab";
        const string ManholeA = CityProps + "SM_Prop_Manhole_01.prefab";
        const string ManholeB = CityProps + "SM_Prop_Manhole_02.prefab";
        const string GrateA = CityEnv + "SM_Env_Sidewalk_Grate_01.prefab";
        const string GrateB = CityEnv + "SM_Env_Sidewalk_Grate_02.prefab";

        static readonly string[] Papers =
        {
            CityProps + "SM_Prop_Paper_01.prefab",
            CityProps + "SM_Prop_Paper_02.prefab",
            CityProps + "SM_Prop_Paper_03.prefab",
            CityProps + "SM_Prop_Paper_04.prefab",
            CityProps + "SM_Prop_Paper_05.prefab",
        };

        static readonly string[] Newspapers =
        {
            CityProps + "SM_Prop_Newspaper_01.prefab",
            CityProps + "SM_Prop_Newspaper_02.prefab",
        };

        /// <summary>Stands the sparse surface plan without claiming Composer ground. Every
        /// physical piece is decorative and stripped of collision, while wear is a shallow
        /// downward projector with a short draw distance.</summary>
        static void SurfaceDetails(ResidentialLot.Plan lot, Transform root, Stood stood)
        {
            var details = ResidentialSurface.Lay(lot);
            stood.SurfaceProfile = details.Wear.ToString();
            var under = new GameObject($"Surface details ({details.Wear})").transform;
            under.SetParent(root, false);

            int nth = 0;
            foreach (var mark in details.Marks)
            {
                float x = (mark.I + 0.5f) * ResidentialLot.Cell + mark.OffsetX;
                float z = (mark.J + 0.5f) * ResidentialLot.Cell + mark.OffsetZ;
                bool made = mark.Kind switch
                {
                    ResidentialSurface.Kind.CrackA => Project(Decals + "Decal_Crack_01.mat", under, x, z, mark),
                    ResidentialSurface.Kind.CrackB => Project(Decals + "Decal_Crack_02.mat", under, x, z, mark),
                    ResidentialSurface.Kind.Grunge => Project(Decals + "Decal_Grunge_01.mat", under, x, z, mark),
                    ResidentialSurface.Kind.RoadPatch => Mesh(RoadPatch, under, x, z, mark.Yaw, mark.Scale),
                    ResidentialSurface.Kind.Manhole => Mesh((nth++ & 1) == 0 ? ManholeA : ManholeB,
                                                            under, x, z, mark.Yaw, mark.Scale),
                    ResidentialSurface.Kind.Grate => Mesh((nth++ & 1) == 0 ? GrateA : GrateB,
                                                          under, x, z, mark.Yaw, mark.Scale),
                    ResidentialSurface.Kind.Newspaper => Cluster(Newspapers, under, x, z, mark),
                    _ => Cluster(Papers, under, x, z, mark),
                };
                if (!made) stood.SurfaceMissing++;
                else if (mark.Flush) stood.SurfaceFlush++;
                else stood.SurfaceClusters++;
            }
        }

        static bool Project(string path, Transform parent, float x, float z, ResidentialSurface.Mark mark)
        {
            var material = DemoAssetLoad.Load<Material>(path);
            if (material == null) return false;
            var go = new GameObject(mark.Kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(x, 0.22f, z),
                                                Quaternion.Euler(90f, mark.Yaw, 0f));
            var decal = go.AddComponent<DecalProjector>();
            decal.material = material;
            float size = (mark.Kind == ResidentialSurface.Kind.Grunge ? 3.4f : 4.1f) * mark.Scale;
            decal.size = new Vector3(size, size, 0.35f);
            decal.pivot = new Vector3(0f, 0f, 0.08f);
            decal.drawDistance = 85f;
            decal.fadeScale = 0.8f;
            return true;
        }

        static bool Mesh(string path, Transform parent, float x, float z, float yaw, float scale)
        {
            var go = Sit(path, parent, x, z, yaw, Deck + 0.008f);
            if (go == null) return false;
            go.transform.localScale = Vector3.Scale(go.transform.localScale,
                                                    new Vector3(scale, 1f, scale));
            Decorative(go);
            return true;
        }

        static bool Cluster(string[] paths, Transform parent, float x, float z, ResidentialSurface.Mark mark)
        {
            bool made = false;
            int seed = unchecked(mark.I * 73856093 ^ mark.J * 19349663 ^ mark.Yaw * 83492791);
            var rng = new System.Random(seed);
            for (int n = 0; n < mark.Count; n++)
            {
                string path = paths[rng.Next(paths.Length)];
                float dx = n == 0 ? 0f : Between(rng, -0.55f, 0.55f);
                float dz = n == 0 ? 0f : Between(rng, -0.55f, 0.55f);
                var go = Sit(path, parent, x + dx, z + dz, mark.Yaw + rng.Next(4) * 90f, Deck + 0.01f);
                if (go == null) continue;
                Decorative(go);
                made = true;
            }
            return made;
        }

        static void Decorative(GameObject go)
        {
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (var body in go.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(body);
            go.isStatic = true;
        }
    }
}
