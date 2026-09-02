using System.Collections.Generic;
using LivingCity.Data;
using LivingCity.Entities;
using LivingCity.Generation;
using LivingCity.UI;
using RoadDemo;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PushUpDemo
{
    /// <summary>
    /// Empty animation-review scene: one adult drawn from the live city's crowd catalogue,
    /// retargeted onto the Push Up take. No gameplay, scenery, UI or demo-only cast list.
    /// </summary>
    public sealed class PushUpDemoBuilder : MonoBehaviour
    {
        const string PushUpPath = "Assets/Push Up.fbx";
        const string RuntimeRootName = "Push Up Animation Review";

        readonly List<GameObject> _fallbackCast = new List<GameObject>();

        Transform _runtime;
        GameObject _actor;
        AnimationClip _pushUp;
        AnimationClipPlayable _playable;
        PlayableGraph _graph;
        PedestrianPicker _cityPicker;
        System.Random _rng;
        Vector3 _actorAnchorPosition;
        Quaternion _actorAnchorRotation;

        void Awake()
        {
            EnsureBuilt();
        }

        void OnEnable()
        {
            // With domain reload disabled, Unity keeps the scene objects but not this
            // non-serialized PlayableGraph wrapper. Rebuild the tiny review setup.
            if (Application.isPlaying && (_runtime == null || !_graph.IsValid()))
                EnsureBuilt();
        }

        void EnsureBuilt()
        {
            if (_runtime != null && _graph.IsValid())
                return;

            if (_graph.IsValid())
                _graph.Destroy();

            var stale = _runtime != null ? _runtime : transform.Find(RuntimeRootName);
            if (stale != null)
            {
                stale.gameObject.SetActive(false);
                Destroy(stale.gameObject);
            }

            _runtime = new GameObject(RuntimeRootName).transform;
            _runtime.SetParent(transform, false);
            _rng = new System.Random(unchecked(
                System.Environment.TickCount * 397 ^ (int)System.DateTime.UtcNow.Ticks));

            PrepareCityCast();
            _pushUp = LoadPushUpClip();
            if (_pushUp == null)
            {
                Debug.LogError("[PushUpDemo] Assets/Push Up.fbx has no imported animation clip.", this);
                return;
            }

            var prefab = DrawAdultCityCharacter();
            if (prefab == null)
            {
                Debug.LogError("[PushUpDemo] The city cast has no adult Humanoid character.", this);
                return;
            }

            BuildActor(prefab);
        }

        void PrepareCityCast()
        {
            _fallbackCast.Clear();
            _cityPicker = null;

            var set = LedgerModelSet.Instance;
            if (set == null)
                return;

            var database = set.database;
            if (database != null && database.pedestrianGroups != null)
            {
                var picker = new PedestrianPicker(database.pedestrianGroups, _rng);
                if (!picker.IsEmpty)
                    _cityPicker = picker;
            }

            if (set.people == null)
                return;

            foreach (var prefab in set.people)
                if (IsAdultHumanoid(prefab, null) && !_fallbackCast.Contains(prefab))
                    _fallbackCast.Add(prefab);
        }

        GameObject DrawAdultCityCharacter()
        {
            // Use the weighted crowd mix first. The Resources-backed city cast is the
            // fallback for a checkout whose editor bootstrap has not filled those slots yet.
            if (_cityPicker != null)
            {
                for (var attempt = 0; attempt < 48; attempt++)
                {
                    var candidate = _cityPicker.Next(out var group);
                    if (IsAdultHumanoid(candidate, group))
                        return candidate;
                }
            }

            return _fallbackCast.Count > 0
                ? _fallbackCast[_rng.Next(_fallbackCast.Count)]
                : null;
        }

        static bool IsAdultHumanoid(GameObject prefab, string group)
        {
            if (prefab == null ||
                PedestrianAnthropometry.CohortFor(group, prefab.name) != PedestrianAgeCohort.Adult)
                return false;

            // The older family pack names its child bodies Son/Daughter rather than Child.
            var name = prefab.name;
            if (name.IndexOf("Daughter", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("_Son_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Kid", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            var animator = prefab.GetComponentInChildren<Animator>(true);
            return animator != null && animator.avatar != null && animator.avatar.isHuman;
        }

        void BuildActor(GameObject prefab)
        {
            _actor = Instantiate(prefab, _runtime);
            _actor.name = prefab.name + " - Push Up";
            _actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var animator = _actor.GetComponentInChildren<Animator>(true);
            StripCityRuntime(_actor, animator);

            PedestrianAnthropometry.Apply(
                _actor,
                _rng.Next(),
                PedestrianIdentity.IsFemale(prefab.name),
                PedestrianAgeCohort.Adult,
                prefab.name);
            _actorAnchorPosition = _actor.transform.position;
            _actorAnchorRotation = _actor.transform.rotation;

            animator.enabled = true;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();

            _graph = PlayableGraph.Create("Push Up Demo");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _playable = AnimationClipPlayable.Create(_graph, _pushUp);
            _playable.SetApplyFootIK(false);
            var output = AnimationPlayableOutput.Create(_graph, "Push Up", animator);
            output.SetSourcePlayable(_playable);
            _graph.Play();
            _graph.Evaluate(0f);

            BuildCameraAndLight(BoundsOf(_actor));
        }

        static void StripCityRuntime(GameObject root, Animator keepAnimator)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                Destroy(body);
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
                if (behaviour != keepAnimator)
                    Destroy(behaviour);
        }

        void BuildCameraAndLight(Bounds actorBounds)
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.60f, 0.64f);

            var lightObject = new GameObject("Review light");
            lightObject.transform.SetParent(_runtime, false);
            lightObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.94f, 0.86f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;

            var cameraObject = new GameObject("Review camera");
            cameraObject.transform.SetParent(_runtime, false);
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.135f, 0.16f);
            camera.fieldOfView = 36f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 50f;
            camera.allowHDR = true;
            camera.GetUniversalAdditionalCameraData().antialiasing =
                AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraObject.AddComponent<AudioListener>();

            var extent = Mathf.Max(0.8f, actorBounds.extents.x, actorBounds.extents.y,
                actorBounds.extents.z);
            var target = actorBounds.center;
            var rig = cameraObject.AddComponent<DemoCamera>();
            rig.pivot = target;
            rig.distance = extent * 4.4f;
            rig.yaw = 217f;
            rig.pitch = 28f;
            rig.minDistance = Mathf.Max(0.5f, extent * 1.25f);
            rig.mapCeiling = Mathf.Max(rig.distance, extent * 12f);
            rig.mapAt = 10000f;
            rig.mapTransition = false;
            rig.showHint = false;

            // Start in the same pose DemoCamera will maintain from LateUpdate onward.
            var rotation = Quaternion.Euler(rig.pitch, rig.yaw, 0f);
            cameraObject.transform.SetPositionAndRotation(
                rig.pivot + rotation * new Vector3(0f, 0f, -rig.distance), rotation);
        }

        static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var found = false;
            var bounds = default(Bounds);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else bounds.Encapsulate(renderer.bounds);
            }

            return found
                ? bounds
                : new Bounds(Vector3.up * 0.8f, new Vector3(1.5f, 1.8f, 1.5f));
        }

        void Update()
        {
            // FBX takes are often imported as one-shot clips. The review scene owns the
            // repetition explicitly, so it loops without changing the user's source asset.
            if (!_playable.IsValid() || _pushUp == null || _pushUp.length <= 0f)
                return;

            var time = _playable.GetTime();
            if (time < _pushUp.length)
                return;

            _playable.SetTime(time % _pushUp.length);
            _playable.SetDone(false);
            _playable.SetSpeed(1d);
        }

        void LateUpdate()
        {
            // Animation review is pose-only: even a future clip carrying root curves may
            // never move the subject. Navigation belongs exclusively to DemoCamera.
            if (_actor != null)
                _actor.transform.SetPositionAndRotation(_actorAnchorPosition, _actorAnchorRotation);
        }

        static AnimationClip LoadPushUpClip()
        {
#if UNITY_EDITOR
            foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(PushUpPath))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
#endif
            return null;
        }

        void OnDestroy()
        {
            if (_graph.IsValid())
                _graph.Destroy();
        }
    }
}
