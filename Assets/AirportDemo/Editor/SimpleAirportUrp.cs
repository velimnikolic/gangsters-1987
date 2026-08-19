using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AirportDemo.EditorTools
{
    /// <summary>
    /// The Simple Airport pack ships its materials on the built-in pipeline's Standard
    /// shader. This project is URP, which has no pass for it, so every aeroplane in
    /// the pack renders magenta. This walks the pack's materials once and moves them
    /// onto URP/Lit, carrying the albedo map, the tint, the normal map, the emission
    /// and the metallic/smoothness across, and turning on alpha clipping for the few
    /// that were cut-out.
    ///
    /// It edits the pack's own materials rather than making copies, because a magenta
    /// package is broken for anything that touches it - the pack's own demo scene
    /// included - and because the Synty packs in this project are already URP, so
    /// this only brings the newcomer into line. Re-importing the package puts the
    /// Standard shader back; run the menu item again if that happens.
    ///
    /// Only the shader is touched. Which prefabs the project actually uses out of
    /// this pack is a separate rule, and a narrow one: the aircraft, and nothing else
    /// (see the note at the top of AirportKit).
    /// </summary>
    public static class SimpleAirportUrp
    {
        const string PackDir = "Assets/SimpleAirport";
        const string MarkerPath = "Assets/CityKit/Airport/SimpleAirportUrpVersion.txt";
        const int Version = 1;

        [MenuItem("Tools/City/Catalog/Convert Simple Airport To URP", priority = 7)]
        public static void ForceConvert()
        {
            AssetDatabase.DeleteAsset(MarkerPath);
            ConvertIfStale();
        }

        public static bool IsFresh()
        {
            var marker = AssetDatabase.LoadAssetAtPath<TextAsset>(MarkerPath);
            return marker && marker.text.Trim() == Version.ToString();
        }

        public static void ConvertIfStale()
        {
            if (IsFresh()) return;
            if (!AssetDatabase.IsValidFolder(PackDir)) return;   // the pack is not installed
            Convert();
        }

        static void Convert()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (lit == null)
            {
                Debug.LogWarning("[SimpleAirportUrp] no URP/Lit shader in this project - nothing converted.");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Material", new[] { PackDir });
            int moved = 0, already = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                var name = mat.shader.name;
                if (name.StartsWith("Universal Render Pipeline") || name.StartsWith("Shader Graphs"))
                {
                    already++;
                    continue;
                }

                // read what the built-in material was wearing, before the shader goes
                var albedo = Tex(mat, "_MainTex");
                var normal = Tex(mat, "_BumpMap");
                var emissionMap = Tex(mat, "_EmissionMap");
                var tint = Col(mat, "_Color", Color.white);
                var emission = Col(mat, "_EmissionColor", Color.black);
                float metallic = Flt(mat, "_Metallic", 0f);
                float smoothness = Flt(mat, "_Glossiness", Flt(mat, "_Smoothness", 0.1f));
                float cutoff = Flt(mat, "_Cutoff", 0.5f);
                // Standard's rendering mode: 0 opaque, 1 cutout, 2 fade, 3 transparent
                int mode = Mathf.RoundToInt(Flt(mat, "_Mode", 0f));
                bool cutout = mode == 1 || path.IndexOf("Alpha", System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool blended = mode >= 2;
                bool wasUnlit = name.IndexOf("Unlit", System.StringComparison.OrdinalIgnoreCase) >= 0;

                mat.shader = wasUnlit && unlit != null ? unlit : lit;

                Set(mat, "_BaseMap", albedo);
                Set(mat, "_MainTex", albedo);        // URP keeps this as an alias
                SetCol(mat, "_BaseColor", tint);
                SetCol(mat, "_Color", tint);
                if (normal != null)
                {
                    Set(mat, "_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                }
                SetFlt(mat, "_Metallic", metallic);
                SetFlt(mat, "_Smoothness", smoothness);
                if (emission.maxColorComponent > 0.001f || emissionMap != null)
                {
                    Set(mat, "_EmissionMap", emissionMap);
                    SetCol(mat, "_EmissionColor", emission);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }

                if (cutout)
                {
                    // URP surface type opaque, alpha clipping on
                    SetFlt(mat, "_Surface", 0f);
                    SetFlt(mat, "_AlphaClip", 1f);
                    SetFlt(mat, "_Cutoff", cutoff);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                }
                else if (blended)
                {
                    SetFlt(mat, "_Surface", 1f);
                    SetFlt(mat, "_Blend", 0f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                else
                {
                    SetFlt(mat, "_Surface", 0f);
                    SetFlt(mat, "_AlphaClip", 0f);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = -1;
                }

                EditorUtility.SetDirty(mat);
                moved++;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath));
            if (!AssetDatabase.IsValidFolder("Assets/CityKit")) AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder("Assets/CityKit/Airport")) AssetDatabase.CreateFolder("Assets/CityKit", "Airport");
            File.WriteAllText(MarkerPath, Version.ToString());
            AssetDatabase.ImportAsset(MarkerPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SimpleAirportUrp] moved {moved} Simple Airport materials onto URP ({already} were already there) - " +
                      "the aeroplanes were magenta because the pack ships on the built-in pipeline's Standard shader.");
        }

        static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
        static Color Col(Material m, string p, Color fallback) => m.HasProperty(p) ? m.GetColor(p) : fallback;
        static float Flt(Material m, string p, float fallback) => m.HasProperty(p) ? m.GetFloat(p) : fallback;
        static void Set(Material m, string p, Texture t) { if (t != null && m.HasProperty(p)) m.SetTexture(p, t); }
        static void SetCol(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
        static void SetFlt(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
    }
}
