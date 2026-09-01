// Minimal UnityEngine stand-ins so the road core compiles and runs headless.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 left => new Vector3(-1, 0, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator *(float k, Vector3 a) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator /(Vector3 a, float k) => new Vector3(a.x / k, a.y / k, a.z / k);
        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = magnitude; return m > 1e-8f ? this / m : zero; } }
        public void Normalize() { float m = magnitude; if (m > 1e-8f) { x /= m; y /= m; z /= m; } }
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(MathF.Min(a.x, b.x), MathF.Min(a.y, b.y), MathF.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(MathF.Max(a.x, b.x), MathF.Max(a.y, b.y), MathF.Max(a.z, b.z));
        // Good enough for a headless sim: both callers feed unit-ish directions, and a
        // normalized lerp bends the same way at the angles the driving ever uses.
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
        {
            t = Mathf.Clamp01(t);
            var m = Mathf.Lerp(a.magnitude, b.magnitude, t);
            var d = (a.normalized * (1f - t) + b.normalized * t);
            return d.sqrMagnitude > 1e-10f ? d.normalized * m : a;
        }
        public static float Angle(Vector3 a, Vector3 b)
        {
            float d = Dot(a.normalized, b.normalized);
            return Mathf.Rad2Deg * MathF.Acos(Mathf.Clamp(d, -1f, 1f));
        }
        public static float SignedAngle(Vector3 a, Vector3 b, Vector3 axis)
        {
            float ang = Angle(a, b);
            float s = Mathf.Sign(Dot(axis, Cross(a, b)));
            return ang * s;
        }
        public static Vector3 RotateTowards(Vector3 from, Vector3 to, float maxRad, float maxMag)
        {
            float ang = Angle(from, to) * Mathf.Deg2Rad;
            if (ang < 1e-5f) return to.normalized * from.magnitude;
            float t = Mathf.Min(1f, maxRad / ang);
            var f = from.normalized; var g = to.normalized;
            var r = (f * (1 - t) + g * t).normalized;
            return r * from.magnitude;
        }
        public override string ToString() => $"({x:F2},{y:F2},{z:F2})";
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 right => new Vector2(1, 0);
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k);
        public float magnitude => MathF.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized { get { float m = magnitude; return m > 1e-8f ? new Vector2(x / m, y / m) : zero; } }
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
    }

    public struct Vector2Int { public int x, y; public Vector2Int(int x, int y) { this.x = x; this.y = y; } }

    public struct Quaternion
    {
        public Vector3 fwd;
        public static Quaternion identity => new Quaternion { fwd = Vector3.forward };
        public static Quaternion LookRotation(Vector3 f) => new Quaternion { fwd = f.normalized };
        public static Quaternion LookRotation(Vector3 f, Vector3 up) => new Quaternion { fwd = f.normalized };
        public static Quaternion Euler(float x, float y, float z) => new Quaternion { fwd = new Vector3(MathF.Sin(y * Mathf.Deg2Rad), 0, MathF.Cos(y * Mathf.Deg2Rad)) };
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }

    public static class Mathf
    {
        public const float PI = MathF.PI;
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-5f;
        public static float Abs(float v) => MathF.Abs(v);
        public static int Abs(int v) => Math.Abs(v);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Min(float a, float b, float c) => MathF.Min(a, MathF.Min(b, c));
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Sqrt(float v) => MathF.Sqrt(v);
        public static float Sin(float v) => MathF.Sin(v);
        public static float Cos(float v) => MathF.Cos(v);
        public static float Tan(float v) => MathF.Tan(v);
        public static float Atan(float v) => MathF.Atan(v);
        public static float Atan2(float a, float b) => MathF.Atan2(a, b);
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float SmoothStep(float a, float b, float t) { t = Clamp01(t); t = t * t * (3f - 2f * t); return a + (b - a) * t; }
        public static float MoveTowards(float a, float b, float d) => Abs(b - a) <= d ? b : a + Sign(b - a) * d;
        public static float Sign(float v) => v >= 0 ? 1f : -1f;
        public static int CeilToInt(float v) => (int)MathF.Ceiling(v);
        public static int FloorToInt(float v) => (int)MathF.Floor(v);
        public static int RoundToInt(float v) => (int)MathF.Round(v);
        public static float Floor(float v) => MathF.Floor(v);
        public static float Repeat(float t, float len) => t - MathF.Floor(t / len) * len;
    }

    public static class Random
    {
        public static System.Random R = new System.Random(int.TryParse(Environment.GetEnvironmentVariable("SEED"), out var sd) ? sd : 1);
        public static float value => (float)R.NextDouble();
        public static float Range(float a, float b) => a + (float)R.NextDouble() * (b - a);
        public static int Range(int a, int b) => a + R.Next(Math.Max(0, b - a));
    }

    public static class Time
    {
        public static float time;
        public static float deltaTime;
        public static int frameCount;
    }

    public class Transform
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 forward => rotation.fwd;
        public Vector3 right => Vector3.Cross(Vector3.up, rotation.fwd);
        public string name;
        public GameObject gameObject = new GameObject();
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; }
    }

    public class GameObject
    {
        public string name;
    }

    public class Object
    {
        public static void Destroy(object o) { }
    }
    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }
    public class MeshRenderer : Component { }
    public class Renderer : Component { }
    public class AudioClip : Object { }

    public static class Debug
    {
        public static void Log(object o) => Console.WriteLine(o);
        public static void LogWarning(object o) => Console.WriteLine("WARN " + o);
        public static void LogError(object o) => Console.WriteLine("ERR " + o);
    }

    public enum RuntimeInitializeLoadType { SubsystemRegistration, BeforeSceneLoad, AfterSceneLoad }
    [AttributeUsage(AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }
}

namespace RoadDemo
{
    // the registries the cars read (the real StreetTraffic is a MonoBehaviour spawner)
    public static class StreetTraffic
    {
        // A body in the road carries the faction it belongs to now, not just a point -
        // the real one grew the field and this stub did not, so the harness would not
        // build at all ("the type name 'Body' does not exist in the type
        // 'StreetTraffic'", RoadCar.Behind).
        public readonly struct Body
        {
            public readonly UnityEngine.Vector3 At;
            public readonly int Faction;
            public Body(UnityEngine.Vector3 at, int faction) { At = at; Faction = faction; }
        }

        public static readonly List<IRoadUser> Users = new List<IRoadUser>();
        public static readonly List<Body> Bodies = new List<Body>();
        public static readonly List<UnityEngine.Vector3> Walkers = new List<UnityEngine.Vector3>();

        // The quiet zone a shootout opens (the real one is fed by StreetAlarm): the
        // headless sim never has one, so the wandering test drivers never detour.
        public static UnityEngine.Vector3 QuietAt => default;
        public static bool QuietOpen => false;
        public static bool CrossesQuiet(UnityEngine.Vector3 a, UnityEngine.Vector3 b) => false;
    }

    public class CrewWalker { }

    // The toll plaza is scene furniture (TollPlaza is a MonoBehaviour that stands the
    // booths up); the sim only ever asks the gate one question, so the stub answers it.
    public sealed class TollGate
    {
        public bool MayPass(RoadCar car) => true;
    }

    public static class StreetAlarm
    {
        public static float LastShotAt = -1000f;
        public static UnityEngine.Vector3 LastShotPos;
    }

    public static class DemoAudio { public static void At(object clip, UnityEngine.Vector3 p, float vol, float pitch) { } }
    public static class DemoSounds { public static object Horns; public static float HornVolume; public static object Pick(object o) => null; }

    public class TrafficSignal
    {
        public const float Green = 9f, Yellow = 2.5f, AllRed = 1.5f;
        public const float HalfCycle = Green + Yellow + AllRed;
        public const float Cycle = HalfCycle * 2f;
        readonly float _offset;
        public TrafficSignal(float offset) { _offset = offset; }
        float AxisTime(bool ns)
        {
            float t = (UnityEngine.Time.time + _offset) % Cycle;
            return ns ? t : (t + HalfCycle) % Cycle;
        }
        public bool GreenFor(bool ns) => AxisTime(ns) < Green;
        public bool YellowFor(bool ns) { float t = AxisTime(ns); return t >= Green && t < Green + Yellow; }
    }
}
