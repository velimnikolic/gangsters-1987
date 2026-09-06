using System;
using Numerics = System.Numerics;

namespace UnityEngine
{
    public sealed class HeaderAttribute(string text) : Attribute { }
    public sealed class TooltipAttribute(string text) : Attribute { }
    public sealed class MinAttribute(float value) : Attribute { }
    public static class Time { public static float deltaTime; }
    public static class Mathf
    {
        public const float PI = MathF.PI;
        public const float Deg2Rad = PI / 180f;
        public static float Sin(float a) => MathF.Sin(a);
        public static float Exp(float a) => MathF.Exp(a);
        public static float Abs(float a) => MathF.Abs(a);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Clamp(float a, float min, float max) => Math.Clamp(a, min, max);
        public static float Repeat(float a, float length) => Clamp(a - MathF.Floor(a / length) * length, 0f, length);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp(t, 0f, 1f);
        public static float InverseLerp(float a, float b, float v) => Clamp((v - a) / (b - a), 0f, 1f);
        public static float SmoothStep(float a, float b, float t)
        { t = Clamp(t, 0f, 1f); return Lerp(a, b, t * t * (3f - 2f * t)); }
    }
    public readonly struct Quaternion(Numerics.Quaternion value)
    {
        public Numerics.Quaternion Value { get; } = value;
        public static Quaternion Euler(float x, float y, float z) => new(
            Numerics.Quaternion.CreateFromYawPitchRoll(y * Mathf.Deg2Rad, x * Mathf.Deg2Rad, z * Mathf.Deg2Rad));
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => new(
            Numerics.Quaternion.Slerp(a.Value, b.Value, Mathf.Clamp(t, 0f, 1f)));
    }
    public readonly struct Color(float r, float g, float b)
    {
        public static Color Lerp(Color a, Color b, float t) => a;
    }
    public sealed class Transform
    {
        Quaternion pose = Quaternion.Euler(0, 0, 0);
        public int Writes;
        public Quaternion rotation { get => pose; set { pose = value; Writes++; } }
    }
    public enum LightShadows { Soft }
    public sealed class Light
    {
        public Transform transform = new();
        public Color color;
        public float intensity;
        public LightShadows shadows;
        public bool enabled;
        public static implicit operator bool(Light light) => light != null;
    }
}
namespace LivingCity.Ambient
{
    public sealed class CityClock
    {
        public const float HoursPerDay = 24f;
        public bool Running = true, isActiveAndEnabled = true;
        public float SecondsPerHour = 60f, Hour = 10f;
        public static implicit operator bool(CityClock clock) => clock != null;
    }
}
namespace RoadDemo
{
    public partial class DemoSky
    {
        public LivingCity.Ambient.CityClock clock = new();
        public UnityEngine.Light sun = new();
        public void Frame(float dt)
        {
            UnityEngine.Time.deltaTime = dt;
            if (clock != null && clock.Running && clock.isActiveAndEnabled)
                clock.Hour = UnityEngine.Mathf.Repeat(clock.Hour + dt / clock.SecondsPerHour, 24f);
            ApplySun(clock != null ? clock.Hour : 15f, 0f);
        }
        public UnityEngine.Quaternion ClockPose() => SunRotation(clock.Hour, out _);
    }
}
