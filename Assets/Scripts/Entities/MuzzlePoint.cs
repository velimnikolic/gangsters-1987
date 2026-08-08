using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Marks where a shot leaves the barrel, and draws itself so it can be found.
    ///
    /// Exists only to be visible. An empty GameObject has no renderer and no gizmo of its own,
    /// so a muzzle marker parented three levels down a skeleton is a point you are told is there
    /// and cannot see - which is no better than the hard-coded vector it replaced.
    ///
    /// The drawing lives here rather than on WeaponSocket because a gizmo drawn from the socket
    /// only appears while the CHARACTER is selected, and the whole point of the marker is to
    /// select it and drag it. Drawn from the marker, it is on screen exactly when it is useful.
    /// </summary>
    public sealed class MuzzlePoint : MonoBehaviour
    {
        [Tooltip("How far the shot line is drawn in the Scene view. Cosmetic - it does not " +
                 "affect anything that fires.")]
        [SerializeField, Min(0f)] float lineLength = 0.6f;

        static readonly Color Flash = new(1f, 0.8f, 0.3f);

        void OnDrawGizmos()
        {
            var here = transform.position;

            Gizmos.color = Flash;
            Gizmos.DrawSphere(here, 0.008f);

            // A wire sphere around the solid one: the solid dot vanishes at any distance, the
            // wire ring stays legible when the whole character is in frame.
            Gizmos.DrawWireSphere(here, 0.03f);
            Gizmos.DrawRay(here, transform.forward * lineLength);
        }
    }
}
