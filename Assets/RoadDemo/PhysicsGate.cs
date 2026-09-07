using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// PHYSICS RUNS ONLY WHEN THERE IS A BODY TO MOVE. The city has 130,000 colliders
    /// and, nearly always, no Rigidbody at all - the cars and the people are driven by
    /// hand - yet PhysX simulated every fixed step for nothing (0.6 ms of every frame,
    /// 2026-09-07). The scene is switched to scripted simulation: the query structures
    /// are still kept in step with the transforms every frame, so raycasts, sphere
    /// casts and overlaps answer as before, and a fixed step is simulated only while a
    /// Rigidbody exists (a shattered window, a bomb, a rider thrown off a bike). The
    /// look-up is a type-indexed engine call, a microsecond or two.
    /// </summary>
    [DefaultExecutionOrder(-20000)]
    public sealed class PhysicsGate : MonoBehaviour
    {
        SimulationMode _before;
        bool _armed;

        void OnEnable()
        {
            _before = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            _armed = true;
        }

        void OnDisable()
        {
            if (_armed) Physics.simulationMode = _before;
            _armed = false;
        }

        void FixedUpdate()
        {
            if (FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length > 0)
                Physics.Simulate(Time.fixedDeltaTime);
        }

        void Update() => Physics.SyncTransforms();
    }
}
