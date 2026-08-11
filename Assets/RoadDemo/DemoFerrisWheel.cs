using UnityEngine;

namespace RoadDemo
{
    // Spin for the ferris wheel baked into PalmBlock_07. Attached by the builder
    // to the prop's own rotate pivot (Synty models the moving part as a child
    // named "*_Rotate_*" with the twelve gondolas parented under it), so turning
    // this transform orbits the gondolas for free; each frame they are then set
    // back to the world orientation they were baked with, which is exactly
    // "hanging level" however far the wheel has turned.
    public class DemoFerrisWheel : MonoBehaviour
    {
        // about 1.3 rpm - fairground pace, slow enough to read from street level
        const float DegreesPerSecond = 8f;

        Transform[] _gondolas;
        Quaternion[] _level;

        void Start()
        {
            var found = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in transform)
                if (child.name.Contains("Compartment"))
                    found.Add(child);
            _gondolas = found.ToArray();

            // world-space at bake: the block's yaw is already in here, so the
            // gondola stays both level and facing the way the wheel was placed
            _level = new Quaternion[_gondolas.Length];
            for (int i = 0; i < _gondolas.Length; i++)
                _level[i] = _gondolas[i].rotation;
        }

        void Update()
        {
            // the pivot's spin axis is its local Z - the convention Synty's own
            // (unwired) demo controller for this prop relies on
            transform.Rotate(0f, 0f, -DegreesPerSecond * Time.deltaTime, Space.Self);

            for (int i = 0; i < _gondolas.Length; i++)
                _gondolas[i].rotation = _level[i];
        }
    }
}
