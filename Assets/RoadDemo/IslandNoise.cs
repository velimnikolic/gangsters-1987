using UnityEngine;

namespace RoadDemo
{
    /// <summary>Seeded, view-only terrain noise, independent of the simulation RNG.</summary>
    public static class IslandNoise
    {
        static float Hash(int x, int z, int seed)
        {
            uint h = unchecked((uint)(x * 374761393 + z * 668265263 + seed * 1442695041));
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
        }
        public static float At(float x, float z, int seed)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float u = x - ix, v = z - iz;
            u = u * u * (3f - 2f * u); v = v * v * (3f - 2f * v);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iz, seed), Hash(ix + 1, iz, seed), u),
                Mathf.Lerp(Hash(ix, iz + 1, seed), Hash(ix + 1, iz + 1, seed), u), v);
        }
    }
}
