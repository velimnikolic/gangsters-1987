using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.City
{
    /// <summary>
    /// Spins an amusement prop's wheel and keeps its gondolas hanging level. Synty models the
    /// moving part as a child named "*_Rotate_*" with the compartments parented under it, so the
    /// pivot orbits them for free; each frame the compartments are then set back to the world
    /// orientation they spawned with, which is exactly "hanging level" however far the wheel has
    /// turned. Attached by ParkDresser to the Carousel station; disables itself on a prop with
    /// no rotate pivot, so the palette can grow non-spinning amusements without a special case.
    /// </summary>
    public class FerrisWheelRotator : MonoBehaviour
    {
        /// <summary>Wheel speed in degrees per second - 8 is about 1.3 rpm, fairground pace.</summary>
        public float degreesPerSecond = 8f;

        Transform wheel;
        Transform[] compartments;
        Quaternion[] levelRotations;

        void Start()
        {
            foreach (var child in GetComponentsInChildren<Transform>())
                if (child.name.Contains("_Rotate"))
                {
                    wheel = child;
                    break;
                }

            if (!wheel)
            {
                enabled = false;
                return;
            }

            var found = new List<Transform>();
            foreach (Transform child in wheel)
                if (child.name.Contains("Compartment"))
                    found.Add(child);
            compartments = found.ToArray();

            // World-space at spawn: the prop's yaw is already in here, so restoring it later
            // keeps the gondola both level AND facing the way the wheel was built.
            levelRotations = new Quaternion[compartments.Length];
            for (int i = 0; i < compartments.Length; i++)
                levelRotations[i] = compartments[i].rotation;
        }

        void Update()
        {
            // The rotate pivot's spin axis is its local Z - the convention Synty's own demo
            // controller for this prop relies on.
            wheel.Rotate(0f, 0f, -degreesPerSecond * Time.deltaTime, Space.Self);

            for (int i = 0; i < compartments.Length; i++)
                compartments[i].rotation = levelRotations[i];
        }
    }
}
