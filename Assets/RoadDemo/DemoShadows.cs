using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadDemo
{
    // Refits the pipeline's shadow range to the demo camera, every frame.
    //
    // Why the demo had no shadows at all: URP culls shadow casters by distance
    // FROM THE CAMERA, and the project's pipeline asset is authored at 50 m -
    // a figure that suits a character standing in the street. The demo camera
    // orbits its pivot from 18 to 520 m out (190 m at start), so the whole
    // cascade volume sat in empty air short of the city and the shadow map came
    // out blank: flat, shadeless roofs and pavements, nothing grounded to
    // anything. The city rig (IsometricCameraController) solves the same problem
    // for the main game; this is the demo's own copy of it, for a PERSPECTIVE
    // camera rather than an orthographic one, so nothing here reaches into
    // LivingCity and nothing here is shared back.
    //
    // Geometry: the camera stands h above the ground and looks down at `pitch`.
    // A ground point seen a degrees below horizontal is at
    // slant range h / sin(a), which projects onto the view axis as
    // h / sin(a) * cos(pitch - a). Feed that the top screen edge (a = pitch - fov/2)
    // for the far end of the visible ground and the bottom edge (a = pitch + fov/2)
    // for the near end.
    //
    // The cascade splits are refitted with the range, because a long boom
    // otherwise spends its near cascades on the dead air between the camera and
    // the ground: split 0 is pushed out to where the ground starts and the
    // remaining three are spread over the band actually on screen.
    [RequireComponent(typeof(Camera))]
    public class DemoShadows : MonoBehaviour
    {
        public DemoCamera rig;

        [Tooltip("Metres of shadow range past the far edge of the visible ground. " +
                 "Enough to keep the towers standing behind that edge - which are " +
                 "still in frame, over the horizon line - shadowing themselves.")]
        public float margin = 90f;

        [Tooltip("Ceiling on the range. Tilted flat the visible ground runs to the " +
                 "horizon, and chasing it would stretch one cascade over a kilometre " +
                 "for shadows the fog has already swallowed - and every caster in that " +
                 "range is drawn once more per cascade, so the ceiling is a frame budget.")]
        public float maxDistance = 420f;

        // Below this the ground plane is nearly edge-on to the view ray and the
        // slant range runs away to infinity; the ceiling above takes over there.
        const float MinGroundAngle = 5f;

        Camera _cam;
        UniversalRenderPipelineAsset _urp;
        bool _adjusted;
        float _authoredDistance;
        int _authoredCascades;
        Vector3 _authoredSplit;

        void Awake() => _cam = GetComponent<Camera>();

        void LateUpdate()
        {
            if (!rig)
                return;

            if (!_urp)
            {
                _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (!_urp)
                    return;

                _authoredDistance = _urp.shadowDistance;
                _authoredCascades = _urp.shadowCascadeCount;
                _authoredSplit = _urp.cascade4Split;
            }

            float pitch = Mathf.Clamp(rig.pitch, 1f, 89f);
            float half = _cam.fieldOfView * 0.5f;
            // measured, not derived from the boom: the pivot is not always on the
            // ground, and the ground is what the shadows land on
            float height = Mathf.Max(1f, transform.position.y);

            float far = GroundDepth(height, pitch, Mathf.Max(pitch - half, MinGroundAngle));
            float near = GroundDepth(height, pitch, Mathf.Min(pitch + half, 89.5f));

            // The last cascade fades out over cascadeBorder of the whole range, so
            // the range has to overshoot the visible ground by that fraction or the
            // fade lands in frame as a shadow cutoff line crawling across the city.
            float border = Mathf.Clamp(_urp.cascadeBorder, 0f, 0.5f);
            float distance = Mathf.Min((far + margin) / (1f - border),
                                       maxDistance, _cam.farClipPlane);

            // Dead range in front of the ground, as a fraction of the whole.
            // Cascade 0 absorbs it; the ceiling keeps every cascade a usable slice.
            float dead = Mathf.Clamp(near / distance, 0f, 0.85f);
            float step = (1f - dead) / 3f;
            var split = new Vector3(dead, dead + step, dead + step * 2f);

            if (!Mathf.Approximately(_urp.shadowDistance, distance))
                _urp.shadowDistance = distance;
            if (_urp.shadowCascadeCount != 4)
                _urp.shadowCascadeCount = 4;
            if ((_urp.cascade4Split - split).sqrMagnitude > 1e-6f)
                _urp.cascade4Split = split;

            _adjusted = true;
        }

        /// <summary>Depth along the view axis of the ground point seen `angle`
        /// degrees below horizontal, from a camera `height` up pitched at `pitch`.</summary>
        static float GroundDepth(float height, float pitch, float angle)
        {
            float slant = height / Mathf.Sin(Mathf.Max(angle, MinGroundAngle) * Mathf.Deg2Rad);
            return slant * Mathf.Cos((pitch - angle) * Mathf.Deg2Rad);
        }

        /// <summary>
        /// The pipeline asset is a project asset, not scene state, so anything
        /// written during Play survives back into the Editor - where the Scene view
        /// camera stands right next to the city and wants the authored range back.
        /// </summary>
        void OnDisable()
        {
            if (!_adjusted || !_urp)
                return;

            _urp.shadowDistance = _authoredDistance;
            _urp.shadowCascadeCount = _authoredCascades;
            _urp.cascade4Split = _authoredSplit;
            _adjusted = false;
        }
    }
}
