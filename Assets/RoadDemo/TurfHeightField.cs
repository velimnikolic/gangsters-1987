using System;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Immutable terrain samples shared by the tactical map and minimap.</summary>
    public sealed class TurfHeightField
    {
        public const int RegionalSampleBudget = 400000;
        public readonly Rect Area;
        public readonly float Step;
        public readonly int Width, Height;
        readonly float[] _samples;
        public int SampleCount => _samples.Length;

        public TurfHeightField(Rect view, Func<float, float, float> sample, int budget = int.MaxValue)
        {
            if (budget < 4) throw new ArgumentOutOfRangeException(nameof(budget));
            Area = new Rect(view.center - view.size * 0.65f, view.size * 1.3f);
            // Keep the existing 3 m survey where it fits. A wide region increases the
            // step, rather than multiplying synchronous terrain queries without bound.
            float step = 3f;
            int width, height;
            while (true)
            {
                width = Math.Max(2, Mathf.CeilToInt(Area.width / step) + 1);
                height = Math.Max(2, Mathf.CeilToInt(Area.height / step) + 1);
                double count = (double)width * height;
                if (count <= budget) break;
                step *= (float)Math.Sqrt(count / budget) * 1.001f;
            }
            Step = step; Width = width; Height = height;
            _samples = new float[width * height];
            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                    _samples[j * width + i] = sample(Area.xMin + i * step, Area.yMin + j * step);
        }

        public float At(float wx, float wz)
        {
            float u = (wx - Area.xMin) / Step, v = (wz - Area.yMin) / Step;
            int x = Mathf.Clamp((int)u, 0, Width - 2), y = Mathf.Clamp((int)v, 0, Height - 2);
            float fx = Mathf.Clamp01(u - x), fy = Mathf.Clamp01(v - y);
            float a = Mathf.Lerp(_samples[y * Width + x], _samples[y * Width + x + 1], fx);
            float b = Mathf.Lerp(_samples[(y + 1) * Width + x], _samples[(y + 1) * Width + x + 1], fx);
            return Mathf.Lerp(a, b, fy);
        }
    }
}
