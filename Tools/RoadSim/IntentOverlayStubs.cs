// Rendering/input stand-ins for running the actual CombatIntentOverlay offline.
// SetPositions follows Unity's documented positionCount/spare-capacity contract;
// these counters prove submission behavior, not native rendering cost or appearance.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public struct Color : IEquatable<Color>
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1, 1);
        public override int GetHashCode() => HashCode.Combine(r, g, b, a);
        public bool Equals(Color c) => r == c.r && g == c.g && b == c.b && a == c.a;
        public override bool Equals(object other) => other is Color c && Equals(c);
    }
    public partial class Transform { public void SetParent(Transform parent, bool world = true) { } }
    public partial class GameObject
    {
        object component;
        public GameObject() { }
        public GameObject(string label, params Type[] types)
        {
            name = label; transform = new Transform();
            if (types.Length > 0) component = Activator.CreateInstance(types[0]);
        }
        public T GetComponent<T>() where T : class => component as T;
    }
    public class Shader { public static Shader Find(string name) => new Shader(); }
    public class Material
    {
        public string name;
        public Material(Shader shader) { }
        public bool HasProperty(string name) => true;
        public void SetColor(string name, Color colour) { }
    }
    public class LineRenderer
    {
        public bool enabled, useWorldSpace, receiveShadows;
        public float widthMultiplier;
        public int numCapVertices, positionCount, BulkCalls, VertexCalls;
        public Material sharedMaterial;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public Vector3[] Points = Array.Empty<Vector3>();
        void Ensure() { if (Points.Length < positionCount) Points = new Vector3[positionCount]; }
        public void SetPosition(int index, Vector3 point) { Ensure(); Points[index] = point; VertexCalls++; }
        public void SetPositions(Vector3[] points) { Ensure(); Array.Copy(points, Points, positionCount); BulkCalls++; }
    }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } }
namespace UnityEngine.InputSystem
{
    public class Keyboard
    {
        public static Keyboard current;
        public readonly Key iKey = new Key();
        public sealed class Key { public bool wasPressedThisFrame; }
    }
}
namespace LivingCity.UI { public static class PersonnelAlmanac { public static bool IsOpen; } }
namespace RoadDemo
{
    public static class SidewalkPlan { public struct Box { public Vector2 Ax, Az, H, C; public bool Tall; } }
    public static class WalkObstacles { public static void PropsNear(Vector3 at, float reach, List<SidewalkPlan.Box> into) => into.Clear(); }
    public static class CrewOverlay { public static void Announce(string message, float duration, Color colour) { } }
    public class DemoCrews
    {
        public const float PropCoverMinHalf = .4f, PropCoverMaxHalf = 8f;
        public readonly List<Unit> Units = new List<Unit>();
        public readonly List<OverlayCar> Cars = new List<OverlayCar>();
        public sealed class Unit
        {
            public int Faction;
            public CrewWalker Boss;
            public readonly List<CrewWalker> Men = new List<CrewWalker>();
            public List<CrewWalker> All() => Men;
        }
    }
    public class OverlayCar : RoadCar { public DemoCrews.Unit Occupant; }
    public partial class CrewWalker
    {
        public bool Dead, Ducked, Lurking, InCover, Riding, Urgent;
        public Transform Tf;
        public CrewWalker Target;
        public Vector3? HeldCover, CoverSpot;
        public Vector3 OrderDestination;
        public enum Mode { Walking, Homing, Striding, Idle }
        public Mode State;
        public readonly List<Vector3> Path = new List<Vector3>();
        public bool CopyPlannedRoute(List<Vector3> into) { into.Clear(); into.AddRange(Path); return into.Count > 1; }
    }
}
