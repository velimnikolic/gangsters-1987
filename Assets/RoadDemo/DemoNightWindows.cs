using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // Lit windows after dark. Self-contained (the demo does not use the city's
    // NightWindows, and its own clock/night curve are the ones that drive this).
    //
    // The pack did the hard part: the PolygonCity atlas ships an emission map,
    // "Emissive_01", in which the artist painted exactly which texels are windows
    // and signage. What it does not ship is any reason for that map to be used -
    // every building material sits at _Enable_Emission 0 with a black emission
    // colour, so nothing on a facade ever lights and the city after sunset is a
    // field of silhouettes lit only by the street lamps.
    //
    // Turning that map on wholesale is the wrong fix: every window of every
    // building and every sign in the city lights at once, at the same second and
    // the same brightness, which reads as a switch being thrown rather than as a
    // city. So the map is used as the artist's *list of candidates*, and the
    // lighting picks from it:
    //
    //  - a quarter of the buildings never light at all - they keep the pack's own
    //    materials untouched and stay dark all night;
    //  - the rest are dealt one of Buckets lots. Each lot carries its own copy of
    //    the emission map with a random share of the painted cells blacked out, so
    //    only some of the windows and signs on that building are live;
    //  - each lot also comes on at its own point in the dusk ramp, and some of
    //    them go dark again in the small hours.
    //
    // This is not real illumination - a window casts nothing onto the pavement
    // opposite; DemoStreetLamps does that. What it gives is a city that reads as
    // inhabited after dark.
    public class DemoNightWindows : MonoBehaviour
    {
        public DemoClock clock;

        // Synty's Generic_Basic shader graph exposes emission as a plain Boolean
        // property plus a colour and a map - no keyword, so a SetFloat is all the
        // toggle needs. The Unity-standard names are written too: the pack's
        // materials carry both sets and other kits may only have the standard one.
        static readonly int EnableEmission = Shader.PropertyToID("_Enable_Emission");
        static readonly int SyntyEmissionColor = Shader.PropertyToID("_Emission_Color");
        static readonly int SyntyEmissionMap = Shader.PropertyToID("_Emission_Map");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

        // Warm tungsten. The emission map already decides WHICH texels light up,
        // this only tints and scales them; a shade over 1 pushes into the bloom.
        static readonly Color LitColour = new Color(1f, 0.87f, 0.62f);
        const float Intensity = 1.5f;

        const int Buckets = 5;

        // Share of buildings that stay dark all night. With KillShare below this
        // leaves roughly 40% of the painted windows in the city burning at once -
        // enough to read as occupied, far from a christmas tree.
        const float DarkShare = 0.25f;

        // Share of the emission map's cells blacked out per bucket, and the size
        // of a cell in texels. 32 on the pack's 1024 map is about one window
        // cluster: coarse enough to put out a whole row of a facade, fine enough
        // that two buildings on the same bucket still differ from each other.
        const float KillShare = 0.45f;
        const int MaskCell = 32;

        // Ceiling on a mask's resolution. The city's own atlas is 1024 and is
        // copied texel for texel, but several packs ship 4096 emission maps and
        // Buckets copies of one of those would be a third of a gigabyte. Anything
        // larger is shrunk to this first, which costs the masks about 20 MB per
        // source atlas - beside the 67 MB Unity already holds for a 4096 original.
        const int MaxMaskSize = 1024;

        // A building's night starts somewhere in the dusk ramp, not at the top of
        // it, and the last of them only lights once it is properly dark.
        static readonly float[] Onset = { 0f, 0.12f, 0.28f, 0.45f, 0.62f };

        // Hour at which a bucket turns in, or 0 for the ones that burn till dawn -
        // stairwells, signage, the people who never switch anything off.
        static readonly float[] SleepHour = { 0f, 1.4f, 2.1f, 0f, 3.2f };

        // The hour DemoSky's night curve finally reaches zero - its sunrise plus
        // its twilight. The sleep fade applies below this and nowhere else, and it
        // has to reach exactly this far: cut it off at sunrise itself and a window
        // that went dark at three snaps back on for the last of the twilight,
        // because the night it is being scaled by has not run out yet.
        const float SmallHoursEnd = 7.25f;

        readonly List<Material>[] _bucketMats = new List<Material>[Buckets];
        readonly float[] _applied = new float[Buckets];
        readonly List<Texture2D> _masks = new List<Texture2D>();

        void Start()
        {
            for (int i = 0; i < Buckets; i++)
            {
                _bucketMats[i] = new List<Material>();
                _applied[i] = -1f;
            }

            // one masked set of the map per source texture, made once and shared
            var maskSets = new Dictionary<Texture, Texture2D[]>();
            // one clone per (pack material, bucket) - buildings on the same bucket
            // share it, so the whole city costs a couple of dozen materials
            var clones = new Dictionary<(Material, int), Material>();

            int litBuildings = 0, darkBuildings = 0;

            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var materials = renderer.sharedMaterials;

                // does this renderer carry anything the artist painted windows on?
                bool candidate = false;
                for (int i = 0; i < materials.Length && !candidate; i++)
                    candidate = materials[i] && UnlitEmissionMap(materials[i]);
                if (!candidate)
                    continue;

                int bucket = BucketOf(renderer.transform.position);
                if (bucket < 0)
                {
                    // left on the pack's own materials: never lights, costs nothing
                    darkBuildings++;
                    continue;
                }

                bool touched = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var original = materials[i];
                    if (!original)
                        continue;

                    var map = UnlitEmissionMap(original);
                    if (!map)
                        continue;

                    if (!clones.TryGetValue((original, bucket), out var clone))
                    {
                        if (!maskSets.TryGetValue(map, out var masks))
                        {
                            masks = BuildMasks(map);
                            maskSets[map] = masks;
                        }

                        clone = MakeNightMaterial(original, masks?[bucket]);
                        clones[(original, bucket)] = clone;
                        _bucketMats[bucket].Add(clone);
                    }

                    materials[i] = clone;
                    touched = true;
                }

                if (touched)
                {
                    // never the shared array in place - assigning it back is what
                    // actually swaps the materials on the renderer
                    renderer.sharedMaterials = materials;
                    litBuildings++;
                }
            }

            if (litBuildings == 0)
            {
                enabled = false;
                Debug.LogWarning("[RoadDemo] No unlit emissive materials found - facades will " +
                                 "stay as the pack left them.", this);
                return;
            }

            Debug.Log($"[RoadDemo] Night windows: {litBuildings} facades on {clones.Count} " +
                      $"materials across {Buckets} lots, {darkBuildings} left dark.", this);
        }

        /// <summary>
        /// The emission map of a material that carries one but does not currently
        /// use it, or null.
        ///
        /// Two things have to be kept out. Anything already burning - the PalmCity
        /// props and vehicles, the police station's security panels - is the pack
        /// looking as intended and is not ours to drive. And several of the pack's
        /// own materials have a NORMAL map sitting in the emission slot (every
        /// PolygonCity _B and _C variant does); lighting those paints the facade in
        /// normal-map lilac, so the texture has to actually be an emissive one.
        /// </summary>
        static Texture UnlitEmissionMap(Material material)
        {
            if (material.HasProperty(EnableEmission) && material.GetFloat(EnableEmission) > 0.5f)
                return null;

            Texture map = null;
            if (material.HasProperty(SyntyEmissionMap))
                map = material.GetTexture(SyntyEmissionMap);
            if (!map && material.HasProperty(EmissionMap))
                map = material.GetTexture(EmissionMap);
            if (!map)
                return null;

            if (map.name.IndexOf("Emissive", System.StringComparison.OrdinalIgnoreCase) < 0)
                return null;

            // a material with no Synty toggle but an already-bright standard colour
            // is emissive by the other convention - leave it alone too
            if (!material.HasProperty(EnableEmission) && material.HasProperty(EmissionColor))
            {
                var c = material.GetColor(EmissionColor);
                if (c.maxColorComponent > 0.01f)
                    return null;
            }

            return map;
        }

        Material MakeNightMaterial(Material original, Texture2D mask)
        {
            // a clone, never the asset: writing emission onto a shared material
            // edits the file on disk the moment this runs in the editor, and leaves
            // every window in the project glowing long after the demo stopped
            var clone = new Material(original) { name = original.name + " (night)" };

            if (clone.HasProperty(EnableEmission))
                clone.SetFloat(EnableEmission, 1f);
            clone.EnableKeyword("_EMISSION");
            clone.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            if (mask)
            {
                if (clone.HasProperty(SyntyEmissionMap))
                    clone.SetTexture(SyntyEmissionMap, mask);
                if (clone.HasProperty(EmissionMap))
                    clone.SetTexture(EmissionMap, mask);
            }

            // dark until the clock says otherwise - Start can run at noon
            if (clone.HasProperty(SyntyEmissionColor))
                clone.SetColor(SyntyEmissionColor, Color.black);
            if (clone.HasProperty(EmissionColor))
                clone.SetColor(EmissionColor, Color.black);

            return clone;
        }

        /// <summary>
        /// One copy of the emission map per bucket, each with a different random
        /// share of its cells blacked out, so a facade shows only some of the
        /// windows and signs the artist painted on it.
        ///
        /// The source is re-decoded from the PNG rather than read off the imported
        /// texture: the pack ships these without Read/Write enabled, and flipping
        /// that on 100-odd textures to run a demo is not a trade worth making.
        /// </summary>
        Texture2D[] BuildMasks(Texture source)
        {
            var readable = Decode(source);
            if (!readable)
            {
                Debug.LogWarning($"[RoadDemo] Could not read {source.name}; its buildings light " +
                                 "every painted window instead of a share of them.", this);
                return null;
            }

            int w = readable.width, h = readable.height;
            var painted = readable.GetPixels32();
            Destroy(readable); // the pixels are ours now; a 4096 decode is 67 MB

            int factor = Mathf.Max(Mathf.CeilToInt(w / (float)MaxMaskSize),
                                   Mathf.CeilToInt(h / (float)MaxMaskSize));
            if (factor > 1)
                painted = Shrink(painted, ref w, ref h, factor);

            var masks = new Texture2D[Buckets];

            for (int b = 0; b < Buckets; b++)
            {
                var pixels = (Color32[])painted.Clone();
                // seeded per bucket, so a run looks the same twice and a bucket's
                // pattern can be recognised across the city
                var rng = new System.Random(9187 + b * 31);

                for (int cy = 0; cy < h; cy += MaskCell)
                {
                    for (int cx = 0; cx < w; cx += MaskCell)
                    {
                        if (rng.NextDouble() >= KillShare)
                            continue;

                        int xEnd = Mathf.Min(cx + MaskCell, w);
                        int yEnd = Mathf.Min(cy + MaskCell, h);
                        for (int y = cy; y < yEnd; y++)
                        {
                            int row = y * w;
                            for (int x = cx; x < xEnd; x++)
                                pixels[row + x] = new Color32(0, 0, 0, 255);
                        }
                    }
                }

                var mask = new Texture2D(w, h, TextureFormat.RGBA32, true)
                {
                    name = source.name + $" (night {b})",
                    wrapMode = source.wrapMode,
                    filterMode = source.filterMode,
                    anisoLevel = source.anisoLevel,
                };
                mask.SetPixels32(pixels);
                mask.Apply(true, true); // mips, then off the CPU - nothing reads it again
                masks[b] = mask;
                _masks.Add(mask);
            }

            return masks;
        }

        /// <summary>
        /// Box-shrinks an emission map by taking the BRIGHTEST texel of each block
        /// rather than their average.
        ///
        /// Averaging is the wrong filter here: a painted window is a handful of lit
        /// texels in a field of black, and the mean of a 4x4 block that holds one
        /// of them is a sixteenth as bright - the windows would survive the shrink
        /// as a smear. Taking the maximum keeps every painted patch at the artist's
        /// own brightness and only costs it a little precision at its edge.
        /// </summary>
        static Color32[] Shrink(Color32[] source, ref int width, ref int height, int factor)
        {
            int w = Mathf.Max(1, width / factor);
            int h = Mathf.Max(1, height / factor);
            var shrunk = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                int sy0 = y * factor, sy1 = Mathf.Min(sy0 + factor, height);
                int row = y * w;

                for (int x = 0; x < w; x++)
                {
                    int sx0 = x * factor, sx1 = Mathf.Min(sx0 + factor, width);
                    Color32 brightest = default;
                    int best = -1;

                    for (int sy = sy0; sy < sy1; sy++)
                    {
                        int sourceRow = sy * width;
                        for (int sx = sx0; sx < sx1; sx++)
                        {
                            var texel = source[sourceRow + sx];
                            int sum = texel.r + texel.g + texel.b;
                            if (sum > best)
                            {
                                best = sum;
                                brightest = texel;
                            }
                        }
                    }

                    shrunk[row + x] = brightest;
                }
            }

            width = w;
            height = h;
            return shrunk;
        }

        // LoadImage gives a readable texture whatever the importer was told, which
        // is the only way at the source pixels without re-importing the pack.
        static Texture2D Decode(Texture source)
        {
#if UNITY_EDITOR
            var path = UnityEditor.AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(System.IO.File.ReadAllBytes(path)))
                    return texture;
                Destroy(texture);
            }
#endif
            return null;
        }

        /// <summary>
        /// Which lot a building draws, or -1 for one that stays dark. Hashed off
        /// its position so it is stable: the same building lights the same way
        /// every run, and nothing has to be stored per renderer.
        /// </summary>
        static int BucketOf(Vector3 position)
        {
            unchecked
            {
                uint h = (uint)(Mathf.RoundToInt(position.x) * 73856093
                              ^ Mathf.RoundToInt(position.y) * 83492791
                              ^ Mathf.RoundToInt(position.z) * 19349663);
                h ^= h >> 13;
                h *= 2654435761u;
                h ^= h >> 16;

                float r = (h & 0xFFFFFF) / (float)0x1000000;
                if (r < DarkShare)
                    return -1;

                int bucket = (int)((r - DarkShare) / (1f - DarkShare) * Buckets);
                return Mathf.Clamp(bucket, 0, Buckets - 1);
            }
        }

        void LateUpdate()
        {
            float hour = clock ? clock.Hour : 12f;
            float night = DemoSky.Nightness(hour);

            for (int b = 0; b < Buckets; b++)
            {
                var materials = _bucketMats[b];
                if (materials.Count == 0)
                    continue;

                float lit = BucketNight(b, hour, night);

                // a colour write per material per frame is cheap, but it also
                // dirties them and there is no reason to do it while the sun is
                // high and nothing is changing
                if (Mathf.Approximately(lit, _applied[b]))
                    continue;

                _applied[b] = lit;
                var emission = LitColour * (Intensity * lit);

                foreach (var material in materials)
                {
                    if (!material)
                        continue;
                    if (material.HasProperty(SyntyEmissionColor))
                        material.SetColor(SyntyEmissionColor, emission);
                    if (material.HasProperty(EmissionColor))
                        material.SetColor(EmissionColor, emission);
                }
            }
        }

        /// <summary>
        /// How lit this bucket is: the demo's one night curve, entered late by its
        /// own onset, and given up again at its own bedtime.
        /// </summary>
        static float BucketNight(int bucket, float hour, float night)
        {
            float onset = Onset[bucket];
            float lit = onset <= 0f ? night : Mathf.Clamp01((night - onset) / (1f - onset));

            float sleep = SleepHour[bucket];
            if (sleep > 0f && hour < SmallHoursEnd)
                lit *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(sleep, sleep + 0.8f, hour));

            return lit;
        }

        void OnDestroy()
        {
            foreach (var materials in _bucketMats)
            {
                if (materials == null)
                    continue;
                foreach (var material in materials)
                    if (material)
                        Destroy(material);
                materials.Clear();
            }

            foreach (var mask in _masks)
                if (mask)
                    Destroy(mask);
            _masks.Clear();
        }
    }
}
