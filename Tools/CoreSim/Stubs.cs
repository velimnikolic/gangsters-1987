// Just enough UnityEngine for CoreLayout and CoreRoads to compile and run without an
// editor: the maths, and hollow objects for the tiles that are never really stood.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k);
        public static Vector2 Min(Vector2 a, Vector2 b) => new Vector2(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        public static Vector2 Max(Vector2 a, Vector2 b) => new Vector2(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        public override string ToString() => $"({x:F1}, {y:F1})";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator *(float k, Vector3 a) => new Vector3(a.x * k, a.y * k, a.z * k);
    }

    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public override bool Equals(object o) => o is Vector2Int v && v.x == x && v.y == y;
        public override int GetHashCode() => x * 73856093 ^ y * 19349663;
    }

    public struct Quaternion
    {
        public float yaw;
        public static Quaternion identity => new Quaternion();
        public static Quaternion Euler(float x, float y, float z) => new Quaternion { yaw = y };

        /// <summary>A turn about Y and nothing else, which is every turn this city makes.</summary>
        public static Vector3 operator *(Quaternion turn, Vector3 by)
        {
            double rad = turn.yaw * Math.PI / 180.0;
            float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
            return new Vector3(by.x * cos + by.z * sin, by.y, -by.x * sin + by.z * cos);
        }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public static Rect zero => new Rect(0f, 0f, 0f, 0f);
        public static Rect MinMaxRect(float xmin, float ymin, float xmax, float ymax) => new Rect(xmin, ymin, xmax - xmin, ymax - ymin);
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public Vector2 center => new Vector2(x + width * 0.5f, y + height * 0.5f);
        public bool Contains(Vector2 p) => p.x >= xMin && p.x < xMax && p.y >= yMin && p.y < yMax;
        public override string ToString() => $"x {xMin:F0}..{xMax:F0} z {yMin:F0}..{yMax:F0}";
    }

    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public Vector3 min => center - size * 0.5f;
        public Vector3 max => center + size * 0.5f;
        public Vector3 extents => size * 0.5f;
        public void Encapsulate(Bounds other)
        {
            var lo = new Vector3(Math.Min(min.x, other.min.x), Math.Min(min.y, other.min.y), Math.Min(min.z, other.min.z));
            var hi = new Vector3(Math.Max(max.x, other.max.x), Math.Max(max.y, other.max.y), Math.Max(max.z, other.max.z));
            center = (lo + hi) * 0.5f;
            size = hi - lo;
        }
    }

    public static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Abs(float a) => Math.Abs(a);
        public static int Abs(int a) => Math.Abs(a);
        public static float Floor(float a) => (float)Math.Floor(a);
        public static float Round(float a) => (float)Math.Round(a, MidpointRounding.ToEven);
        public static int RoundToInt(float a) => (int)Math.Round(a, MidpointRounding.ToEven);
        public static int FloorToInt(float a) => (int)Math.Floor(a);
        public static int CeilToInt(float a) => (int)Math.Ceiling(a);
        public static float Clamp(float v, float lo, float hi) => Math.Clamp(v, lo, hi);
        public static int Clamp(int v, int lo, int hi) => Math.Clamp(v, lo, hi);
    }

    public class Object
    {
        public string name;
        public static implicit operator bool(Object o) => o != null;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();
        public static T Instantiate<T>(T prefab, Transform parent) where T : Object => prefab;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }

    public class Renderer : Component
    {
        public Bounds bounds;
    }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position, localScale = Vector3.one;
        public Quaternion rotation;
        public readonly List<Transform> children = new List<Transform>();
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; }
        public void SetParent(Transform parent, bool keep) { parent?.children.Add(this); }
        public Transform Find(string name) => children.Find(c => c.name == name);
        public IEnumerator GetEnumerator() => children.GetEnumerator();
    }

    public class GameObject : Object
    {
        public readonly Transform transform;
        public GameObject(string name = "") { this.name = name; transform = new Transform { gameObject = this, name = name }; }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => new T[0];
    }

    public static class Debug
    {
        public static void Log(object o) => Console.WriteLine(o);
        public static void LogWarning(object o) => Console.WriteLine("WARN " + o);
        public static void LogError(object o) => Console.WriteLine("ERROR " + o);
    }
}

namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static string GUIDToAssetPath(string guid) => guid;
    }
}

namespace RoadDemo
{
    public static class RoadDemoBuilder
    {
        public static float RoadHalf(bool boulevard) => boulevard ? 17.5f : 7.5f;
    }

    public static class DemoAssetLoad
    {
        public static T Load<T>(string path) where T : UnityEngine.Object => (T)(UnityEngine.Object)new UnityEngine.GameObject(path);
        public static string[] Find(string filter, string[] folders) => new string[0];
    }
}

namespace LivingCity.Gameplay
{
    public static class VehicleCatalog
    {
        public static bool IsBarred(string path) => false;
        public static bool IsMarkedService(string path) => false;
        public static int PoolWeight(string path) => 1;
    }
}
