using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CarBehavior = LivingCity.City.CarBehavior;
using PathFinding = LivingCity.City.PathFinding;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Converts Synty vehicle models into traffic-ready _AI prefabs under
    /// Assets/Configs/Vehicles - the AuthorPedestrians deal, for cars: the pack's model with
    /// OUR driving contract bolted on, everything copied off a measured reference so the
    /// values stay in one place.
    ///
    /// What CarBehavior actually needs (read off car-passenger_AI, the reference):
    ///  - front/rear wheel transform lists and the two mid-point markers (steering pose),
    ///  - a sibling PathFinding,
    ///  - a NON-trigger body BoxCollider (also what CarHeadlights hangs its lights off),
    ///  - a forward TRIGGER box - the driving sensor that meets TrafficLight/Crosswalk
    ///    triggers (1.2 x 2.49 x 2.17, centred ~1.15 m past the nose on the reference),
    ///  - a kinematic-style Rigidbody.
    ///
    /// Wheels are found by name ("Wheel" anywhere in a child transform) and split
    /// front/rear POSITIONALLY - by z against the wheel-set centre - because the two Synty
    /// packs use different suffix conventions (Wheel_LF vs FBX-internal names) and the
    /// polyperfect one used a third. Synty vehicles face +Z at identity, the pack-wide
    /// convention.
    /// </summary>
    public static class AuthorVehicles
    {
        public const string OutputDir = "Assets/Configs/Vehicles";

        const string ReferenceAiCar =
            "Assets/polyperfect/Low Poly Epic City/T/- Prefabs_T/Vehicles_T/Cars_AI_T/car-passenger_AI.prefab";

        /// <summary>Every Synty vehicle the traffic tables draw as a driver.</summary>
        static readonly string[] Sources =
        {
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01_Preset_Taxi.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01_Preset_Food.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Suv_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Pickup_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Pickup_01_Preset_Construction.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Van_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Limousine_01.prefab",
            "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/SM_Veh_Car_01.prefab",
        };

        public static void AuthorAll(List<string> missing)
        {
            var reference = AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceAiCar);
            CarBehavior refCar = null;
            BoxCollider refSensor = null;
            Rigidbody refBody = null;
            if (reference)
            {
                refCar = reference.GetComponent<CarBehavior>();
                refBody = reference.GetComponent<Rigidbody>();
                refSensor = reference.GetComponents<BoxCollider>().FirstOrDefault(b => b.isTrigger);
            }

            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/Configs", "Vehicles");

            foreach (var path in Sources)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!source)
                {
                    missing.Add(path);
                    continue;
                }
                Author(source, refCar, refSensor, refBody);
            }
        }

        static void Author(GameObject source, CarBehavior refCar, BoxCollider refSensor, Rigidbody refBody)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                                   InteractionMode.AutomatedAction);
                instance.name = source.name + "_AI";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                // Strip whatever the pack shipped - Synty vehicle prefabs carry a collider or
                // two and nothing else; the driving contract is rebuilt from scratch.
                foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb) Object.DestroyImmediate(mb);
                foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                    if (col) Object.DestroyImmediate(col);

                // Wheels, split by position about the wheel-set centre.
                var wheels = instance.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.Contains("Wheel") && t.GetComponentInChildren<MeshFilter>())
                    .ToList();
                var front = new List<Transform>();
                var rear = new List<Transform>();
                if (wheels.Count >= 2)
                {
                    var midZ = wheels.Average(w => w.position.z);
                    foreach (var w in wheels)
                        (w.position.z >= midZ ? front : rear).Add(w);
                }
                else
                {
                    Debug.LogWarning($"AuthorVehicles: '{source.name}' has {wheels.Count} wheel " +
                                     "transform(s) - traffic will slide it un-steered.");
                }

                Transform Marker(string name, List<Transform> set)
                {
                    var t = new GameObject(name).transform;
                    t.SetParent(instance.transform, false);
                    if (set.Count > 0)
                    {
                        var centre = Vector3.zero;
                        foreach (var w in set) centre += w.position;
                        t.position = centre / set.Count;
                    }
                    return t;
                }

                var frontMid = Marker("Front Wheels Middle Point", front);
                var rearMid = Marker("Rear Wheels Middle Point", rear);

                // Body box from the renderers; sensor box copied from the reference and pushed
                // to ~1.15 m past this body's own nose.
                var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);

                var body = instance.AddComponent<BoxCollider>();
                body.center = bounds.center - instance.transform.position;
                body.size = bounds.size;

                var sensor = instance.AddComponent<BoxCollider>();
                sensor.isTrigger = true;
                if (refSensor)
                {
                    sensor.size = refSensor.size;
                    sensor.center = new Vector3(0f, refSensor.center.y,
                                                bounds.max.z - instance.transform.position.z + 1.15f);
                }
                else
                {
                    sensor.size = new Vector3(1.2f, 2.49f, 2.17f);
                    sensor.center = new Vector3(0f, 0.84f, bounds.size.z / 2f + 1.15f);
                }

                var rb = instance.AddComponent<Rigidbody>();
                if (refBody)
                {
                    rb.mass = refBody.mass;
                    rb.useGravity = refBody.useGravity;
                    rb.isKinematic = refBody.isKinematic;
                    rb.interpolation = refBody.interpolation;
                    rb.collisionDetectionMode = refBody.collisionDetectionMode;
                    rb.constraints = refBody.constraints;
                }

                var car = instance.AddComponent<CarBehavior>();
                instance.AddComponent<PathFinding>();
                car.FrontWheels = front;
                car.RearWheels = rear;
                car.frontWheelsMiddlePoint = frontMid;
                car.rearWheelsMiddlePoint = rearMid;
                if (refCar)
                {
                    car.maxspeed = refCar.maxspeed;
                    car.maxTurnAngle = refCar.maxTurnAngle;
                    car.acceleration = refCar.acceleration;
                    car.minDistance = refCar.minDistance;
                }
                // The polyperfect fleet ships with randomDestination ON and VehicleSpawner
                // (like PedestrianSpawner) also asserts it per instance. Baking FALSE here
                // routed every spawned car into the checkpoint branch with no checkpoints -
                // an EMPTY route - and traffic died on frame one.
                car.randomDestination = refCar ? refCar.randomDestination : true;

                if (refCar)
                    instance.tag = refCar.gameObject.tag;

                PrefabUtility.SaveAsPrefabAsset(instance, $"{OutputDir}/{instance.name}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
