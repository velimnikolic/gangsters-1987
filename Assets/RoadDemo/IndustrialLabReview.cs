using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadDemo
{
    /// <summary>Only review navigation; blocks themselves come from IndustrialBlocks.</summary>
    [RequireComponent(typeof(DemoCamera))]
    public sealed class IndustrialLabReview : MonoBehaviour
    {
        public Transform[] candidates;
        RenderPipelineAsset _previousPipeline;
        UniversalRenderPipelineAsset _reviewPipeline;

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            _previousPipeline = QualitySettings.renderPipeline;
            var source = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!source) return;
            // The city's 50 m shadow limit is too short for an entire industrial parcel.
            // Use a disposable review copy, then restore the user's quality asset.
            _reviewPipeline = Instantiate(source);
            _reviewPipeline.name = "Industrial lab lighting (temporary)";
            _reviewPipeline.shadowDistance = 240f;
            _reviewPipeline.shadowCascadeCount = 4;
            _reviewPipeline.mainLightShadowmapResolution = 4096;
            QualitySettings.renderPipeline = _reviewPipeline;
            var data = GetComponent<Camera>().GetUniversalAdditionalCameraData();
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }

        void OnDisable()
        {
            if (!_reviewPipeline) return;
            if (QualitySettings.renderPipeline == _reviewPipeline) QualitySettings.renderPipeline = _previousPipeline;
            Destroy(_reviewPipeline);
        }

        public void Focus(int index)
        {
            if (candidates == null || index < 0 || index >= candidates.Length || !candidates[index]) return;
            Bounds bounds = default;
            bool any = false;
            foreach (var renderer in candidates[index].GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.GetComponent<TextMesh>()) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!any) return;
            var camera = GetComponent<DemoCamera>();
            camera.pivot = new Vector3(bounds.center.x, 2f, bounds.center.z);
            camera.distance = Mathf.Max(bounds.size.x, bounds.size.z) * 1.65f;
            var rotation = Quaternion.Euler(camera.pitch, camera.yaw, 0f);
            camera.transform.SetPositionAndRotation(camera.pivot - rotation * Vector3.forward * camera.distance, rotation);
        }

        void Update()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.digit1Key.wasPressedThisFrame) Focus(0);
            if (keys.digit2Key.wasPressedThisFrame) Focus(1);
            if (keys.digit3Key.wasPressedThisFrame) Focus(2);
            if (keys.digit4Key.wasPressedThisFrame) Focus(3);
        }
    }
}
