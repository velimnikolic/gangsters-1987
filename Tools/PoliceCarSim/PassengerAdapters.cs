// Scene, navigation and campaign stand-ins for the linked PrisonerCarriage.
// Passenger seating/restoration and carriage stage rules remain production code.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public class AnimationClip { }
    public partial class Transform
    {
        public Transform() { gameObject.transform = this; }
        public Transform parent;
        public Vector3 localScale = new(1f, 1f, 1f);
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Renderer[] Renderers = Array.Empty<Renderer>();
        public T[] GetComponentsInChildren<T>(bool inactive) => Renderers as T[] ?? Array.Empty<T>();
        public void SetParent(Transform target, bool keepWorld) => parent = target;
    }
    public partial class GameObject
    {
        public int layer;
        public bool activeInHierarchy => activeSelf;
    }
    public partial class Renderer { public bool enabled = true; }
}

namespace RoadDemo
{
    public partial class CrewWalker
    {
        public Transform Tf = new();
        public GameObject SourcePrefab;
        public int CharacterId;
        public bool Dead, Riding, Surrendered, Urgent, HasOrder, RoutedLegStalled;
        public CrewWalker GunpointTarget;
        public void SetRiding(bool riding) => Riding = riding;
        public void Disengage() { HasOrder = false; }
        public void Disarm() { }
        public void HoldAtGunpoint(CrewWalker man) => GunpointTarget = man;
        public void LowerGunpoint() => GunpointTarget = null;
        public void OrderToPoint(Vector3 point) { HasOrder = true; }
        public bool OrderAcross(Vector3 point) { HasOrder = true; return true; }
    }
    public sealed class DemoCrews
    {
        public sealed class Unit
        {
            public readonly List<CrewWalker> Men = new();
            public Unit TargetUnit;
            public bool Wiped, IsPolice;
            public int Faction;
            public float ProvokedAt, PoliceFightOrderedAt;
            public Vector3 Position;
            public IEnumerable<CrewWalker> All() => Men;
        }
        public readonly List<Unit> Units = new();
        public void Sic(Unit unit, Unit target) { }
        public void MarchTo(Unit unit, Vector3 target, bool run = false,
            bool keepOffRoad = false, bool allowCustody = false) { }
        public void SendToVehicleDoor(CrewWalker man, Vector3 door, object graph = null) => man.OrderToPoint(door);
    }
    public sealed class PoliceCruiser : IPoliceUnit
    {
        public RoadCar Car;
        public Transform Tf => Car?.Tf;
        public Vector3 Position => Car?.Position ?? Vector3.zero;
        public bool Available => false;
        public bool OnScene => false;
        public bool Carries => true;
        public int Precinct => 0;
        public void RouteTo(Vector3 scene, float standOff) { }
        public void Release() { }
    }
    public static class WalkObstacles
    {
        public const float CrewTravelRadius = .35f, Radius = .35f;
        public static Vector3 ClearSpot(Vector3 point, float radius, float reach) => point;
        public static bool TryClearSpot(Vector3 point, float radius, out Vector3 spot, float reach)
        { spot = point; return true; }
    }
    public static class DoorBeat
    {
        public static void SendOut(CrewWalker man) { }
        public static bool Held(CrewWalker man) => false;
        public static bool Active(CrewWalker man) => false;
        public static void MoveIn(CrewWalker man, Vector3 door) => man.OrderToPoint(door);
    }
}
namespace LivingCity.Gameplay
{
    public sealed class PersonnelDirector
    {
        public static PersonnelDirector Instance;
        public void Touch() { }
    }
}
namespace LivingCity.Outfit
{
    public sealed class Underworld
    {
        public static Underworld Current;
        public House Of(int faction) => null;
    }
    public sealed class House { public LivingCity.Personnel.Roster Roster; }
}
namespace LivingCity.Police
{
    public static class PrisonPipeline
    {
        public static void ConfiscateWeapons(LivingCity.Personnel.Roster roster, int id) { }
    }
}
