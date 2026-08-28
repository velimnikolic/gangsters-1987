using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// PalmCity power poles and spans for the generated core demo.
    /// </summary>
    public static class CorePowerlines
    {
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string PowerPolePath = PalmProps + "SM_Prop_Powerpole_01.prefab";
        static readonly string[] PowerWirePaths =
        {
            PalmProps + "SM_Prop_Powerline_02.prefab",
            PalmProps + "SM_Prop_Powerline_03.prefab",
        };

        struct PowerRun
        {
            public bool Vertical;
            public float Crown;
            public int Width;
            public int Side;
            public float From, To;
        }

        public static void Stand(CoreLayout.Plan plan, CoreRoads.Raster raster, Transform parent, int seed,
                                 System.Func<GameObject, Transform, GameObject> stand = null)
        {
            if (raster == null || parent == null) return;

            var pole = DemoAssetLoad.Load<GameObject>(PowerPolePath);
            if (pole == null)
            {
                Debug.LogWarning("[Core] PalmCity power pole is missing; powerlines skipped.");
                return;
            }

            var wires = new List<GameObject>();
            foreach (string path in PowerWirePaths)
            {
                var wire = DemoAssetLoad.Load<GameObject>(path);
                if (wire != null) wires.Add(wire);
            }
            if (wires.Count == 0)
            {
                Debug.LogWarning("[Core] PalmCity powerline spans are missing; powerlines skipped.");
                return;
            }

            var root = new GameObject("Powerlines").transform;
            root.SetParent(parent, false);
            int attempt = plan != null ? plan.Attempt : 0;
            var dice = new System.Random(unchecked(seed * 92821 + attempt * 16127 + 17));

            int poles = 0, spans = 0;
            foreach (var run in PowerRuns(raster))
                PoleRun(run, root, pole, wires, dice, stand, ref poles, ref spans);

            if (poles == 0 && spans == 0) DestroyNow(root.gameObject);
            else Debug.Log($"[Core] powerlines: {poles} poles, {spans} spans.");
        }

        static List<PowerRun> PowerRuns(CoreRoads.Raster raster)
        {
            var runs = new List<PowerRun>();
            foreach (var stretch in raster.Stretches)
            {
                if (stretch.Width <= 0) continue;
                AddRun(stretch, stretch.Vertical ? +1 : -1, stretch.From, stretch.To, runs);
            }

            runs.Sort((a, b) =>
            {
                int by = b.Vertical.CompareTo(a.Vertical);
                if (by == 0) by = a.Crown.CompareTo(b.Crown);
                if (by == 0) by = a.Width.CompareTo(b.Width);
                if (by == 0) by = a.Side.CompareTo(b.Side);
                return by == 0 ? a.From.CompareTo(b.From) : by;
            });

            const float MergeGap = 24f;
            var merged = new List<PowerRun>();
            foreach (var run in runs)
            {
                if (run.To - run.From < 1f) continue;
                if (merged.Count > 0)
                {
                    var last = merged[merged.Count - 1];
                    if (last.Vertical == run.Vertical && last.Side == run.Side &&
                        last.Width == run.Width && Mathf.Abs(last.Crown - run.Crown) < 0.1f &&
                        run.From <= last.To + MergeGap)
                    {
                        last.To = Mathf.Max(last.To, run.To);
                        merged[merged.Count - 1] = last;
                        continue;
                    }
                }
                merged.Add(run);
            }
            return merged;
        }

        static void AddRun(CoreRoads.Stretch stretch, int side, float from, float to, List<PowerRun> into)
        {
            if (to - from < CoreRoads.Cell) return;
            into.Add(new PowerRun
            {
                Vertical = stretch.Vertical,
                Crown = stretch.Crown,
                Width = stretch.Width,
                Side = side,
                From = from,
                To = to,
            });
        }

        /// <summary>
        /// The pole line sits through the middle of the core's five-metre pavement.
        /// Do not use the wider city frontage setback here: that puts the crossarm and
        /// the outer cable strand through the buildings behind the pavement.
        /// </summary>
        static float PowerlineLateral(int width) =>
            width * CoreRoads.Cell * 0.5f + CoreRoads.Cell * 0.5f;

        static void PoleRun(PowerRun run, Transform parent, GameObject pole, List<GameObject> wires,
                            System.Random dice, System.Func<GameObject, Transform, GameObject> stand,
                            ref int poles, ref int spans)
        {
            const float WireLen = 7.696f;
            const float WireY = 8.33f;
            float[] strand = { -0.85f, 0f, 0.85f };
            float yaw = run.Vertical ? 0f : 90f;
            float lateral = run.Crown + run.Side * PowerlineLateral(run.Width);

            Vector3 At(float along, float side) => run.Vertical
                ? new Vector3(lateral + run.Side * side, 0.1f, along)
                : new Vector3(along, 0.1f, lateral + run.Side * side);

            var spots = PoleSpots(run.From + 2f, run.To - 2f);
            foreach (float along in spots)
            {
                var go = Spawn(pole, parent, stand);
                go.transform.SetPositionAndRotation(At(along, 0f), Quaternion.Euler(0f, yaw, 0f));
                poles++;
            }

            for (int k = 0; k + 1 < spots.Count; k++)
                foreach (float off in strand)
                {
                    var wire = Spawn(wires[dice.Next(wires.Count)], parent, stand);
                    var seat = At(spots[k], off);
                    wire.transform.SetPositionAndRotation(new Vector3(seat.x, WireY, seat.z),
                                                          Quaternion.Euler(0f, yaw, 0f));
                    wire.transform.localScale = new Vector3(1f, 1f, (spots[k + 1] - spots[k]) / WireLen);
                    spans++;
                }
        }

        static GameObject Spawn(GameObject prefab, Transform parent,
                                System.Func<GameObject, Transform, GameObject> stand)
        {
            return stand != null ? stand(prefab, parent) : Object.Instantiate(prefab, parent);
        }

        static List<float> PoleSpots(float from, float to)
        {
            var spots = new List<float>();
            for (float p = from; p < to; p += 21f) spots.Add(p);
            return spots;
        }

        static void DestroyNow(GameObject go)
        {
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }
    }
}
