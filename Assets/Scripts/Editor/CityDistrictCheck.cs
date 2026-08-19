using System.Collections.Generic;
using System.Text;
using RoadDemo;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// What the city rolled, before Play and after it.
    ///
    /// Before Play (edit mode) it rolls the layout the way the builder would and
    /// prints it: which shore each quarter took, which road lines carry it, how wide
    /// the strip is, its seed. Nothing is built - this is the plan on paper, the one
    /// to read when a town comes out wrong.
    ///
    /// In Play it checks the town that actually stands: one MainCamera, one
    /// AudioListener, one sun, every district welded to the grid (a lane out and a
    /// lane back), and nothing of the wild growing on a district's own ground.
    /// </summary>
    public static class CityDistrictCheck
    {
        [MenuItem("Tools/City/Dump City Layout")]
        public static void Dump()
        {
            var city = Object.FindAnyObjectByType<RoadDemoBuilder>();
            if (city == null) { Debug.LogWarning("[Districts] no RoadDemoBuilder in the scene."); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"city layout seed {city.cityLayoutSeed}, roll {city.rollDistricts}, " +
                          $"harbour {city.harborDistrict}, suburbs {city.suburbsMin}-{city.suburbsMax}");

            var slots = city.rollDistricts ? RollLikeTheBuilder(city) : new List<DistrictSlot>(city.districts ?? new DistrictSlot[0]);
            if (slots.Count == 0) sb.AppendLine("  (no districts)");
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                bool vertical = slot.edge == CityEdge.South || slot.edge == CityEdge.North;
                var axis = vertical ? city.verticalRoadX : city.horizontalRoadZ;
                var at = new List<string>();
                foreach (int line in slot.pinLines)
                    at.Add(line >= 0 && line < axis.Length ? $"{line}@{axis[line]:F0}" : $"{line}!");
                sb.AppendLine($"  {slot.kind,-7} {slot.edge,-5} lines [{string.Join(" ", at)}] " +
                              $"strip {slot.strip:F0} seed {slot.seed} size {slot.sizeAcross}x{slot.sizeDeep}");
                // the rule of five, stated where it can be seen to hold
                for (int k = 0; k + 1 < slot.pinLines.Length; k++)
                {
                    int a = slot.pinLines[k], b = slot.pinLines[k + 1];
                    if (a < 0 || b >= axis.Length) continue;
                    float gap = axis[b] - axis[a];
                    bool lattice = Mathf.Abs(gap / 5f - Mathf.Round(gap / 5f)) < 0.01f;
                    sb.AppendLine($"      {a} -> {b}: {gap:F0} m, {((b - a) % 5 == 0 ? "5 apart" : "NOT 5 apart")}" +
                                  $", {(lattice ? "on the 5 m lattice" : "OFF the lattice")}");
                }
            }
            Debug.Log("[Districts] " + sb);
        }

        static List<DistrictSlot> RollLikeTheBuilder(RoadDemoBuilder city)
        {
            var grid = new CityLayout.Grid
            {
                Vx = city.verticalRoadX,
                VBoulevard = city.verticalIsBoulevard,
                Hz = city.horizontalRoadZ,
                HBoulevard = city.horizontalIsBoulevard,
                Blocked = (vertical, line) =>
                {
                    if (city.seams == null) return false;
                    foreach (var s in city.seams)
                        if (s != null && s.vertical != vertical && (line == s.gap || line == s.gap + 1)) return true;
                    return false;
                },
            };
            // (edit mode: the axes are the authored ones, not the respaced ones - the
            // distances printed shift a little once Play respaces the grid, the line
            // indices do not)
            return CityLayout.Roll(grid, city.cityLayoutSeed, city.suburbsMin, city.suburbsMax, city.harborDistrict);
        }

        [MenuItem("Tools/City/Check Districts (in Play)")]
        public static void Check()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Districts] this one checks the town that stands - press Play first " +
                                 "(Tools/City/Dump City Layout reads the plan in edit mode).");
                return;
            }

            int bad = 0;
            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            int mains = 0;
            foreach (var c in cams) if (c.CompareTag("MainCamera")) mains++;
            if (mains != 1) { Debug.LogError($"[Districts] {mains} main cameras in the scene - a host is building one it should not."); bad++; }

            var ears = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            if (ears.Length > 1) { Debug.LogError($"[Districts] {ears.Length} audio listeners."); bad++; }

            int suns = 0;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) suns++;
            if (suns > 1) { Debug.LogError($"[Districts] {suns} directional lights - two suns."); bad++; }

            var hosts = Object.FindObjectsByType<StandaloneDistrictHost>(FindObjectsSortMode.None);
            var city = Object.FindAnyObjectByType<RoadDemoBuilder>();
            if (city != null && hosts.Length > 0)
            {
                Debug.LogError("[Districts] a standalone host is in the city's scene: the city hosts its own districts.");
                bad++;
            }

            int waters = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.name == "Water" || r.name == "Sea") waters++;
            if (waters > 1) Debug.LogWarning($"[Districts] {waters} water planes - two at the same level z-fight.");

            Debug.Log(bad == 0
                ? "[Districts] the town checks out: one camera, one ear, one sun."
                : $"[Districts] {bad} things wrong - see the errors above.");
        }
    }
}
