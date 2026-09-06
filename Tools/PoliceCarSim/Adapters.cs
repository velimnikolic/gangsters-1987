// Only scene presentation and unrelated inventory/custody types are stand-ins.
// Patrol states, parking selection, road motion and police arrival rules are linked sources.
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public interface IPoliceUnit
    {
        Transform Tf { get; }
        Vector3 Position { get; }
        bool Available { get; }
        bool OnScene { get; }
        bool Carries { get; }
        int Precinct { get; }
        void RouteTo(Vector3 scene, float standOff);
        void Release();
    }
    public interface IPatrolMarker
    {
        Transform MarkerTf { get; }
        float MarkerHeight { get; }
        bool MarkerDimmed { get; }
        string MarkerTitle { get; }
        string MarkerLine { get; }
    }
    public static class PatrolInfo
    {
        public static string Heading(Transform tf) => "";
        public static string Toward(Vector3 from, Vector3 to) => "";
    }
    public sealed class CarOccupant
    {
        public GameObject gameObject = new();
        public void Show(bool visible) { }
        public static CarOccupant Seat(Transform car, GameObject prefab, AnimationClip clip,
            Vector3 seat, int layer) => new();
    }
    public static class CarBody
    {
        public static Vector3[] MeasureSeats(Transform tf) => new[] {
            new Vector3(-.5f, 1f, .5f), new Vector3(.5f, 1f, .5f),
            new Vector3(-.5f, 1f, -.5f), new Vector3(.5f, 1f, -.5f), Vector3.zero };
        public static bool MeasureTrafficFootprint(Transform tf, out float length, out float width)
        { length = width = 0f; return false; }
    }
}
namespace LivingCity.Personnel
{
    public enum EquipmentKind { Vehicle }
    public sealed class RosterEquipment
    {
        public EquipmentKind Kind;
        public int HolderId, OwnerId;
        public string DisplayName;
    }
    public sealed class Roster { public readonly List<RosterEquipment> Equipment = new(); }
}
namespace LivingCity.UI
{
    public static class PortraitStudio { public static string VehicleModelFor(string name) => null; }
}
namespace LivingCity.Police
{
    public enum PrisonStage { Held, ForTransfer, InTransit, Sentenced, Serving }
}
