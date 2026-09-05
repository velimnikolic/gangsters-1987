using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OwnerBeatingDemo
{
    /// <summary>Replay controls only. The sequence itself lives in RoadDemo.</summary>
    public sealed class OwnerBeatingDemo : MonoBehaviour
    {
        public OwnerBeatingSequence sequence;
        public Transform gangster, owner, shop, inside, outside, street;
        public bool autoPlay = true;
        CrewWalker _gangster, _owner;
        Vector3 _gangsterStart, _ownerStart;
        Quaternion _gangsterRotation, _ownerRotation;
        float _savedTimeScale;
        bool _paused, _slow;
        GUIStyle _label;

        void Start()
        {
            _savedTimeScale = Time.timeScale;
            _gangsterStart = gangster.position; _ownerStart = owner.position;
            _gangsterRotation = gangster.rotation; _ownerRotation = owner.rotation;
            ResetActors();
            if (autoPlay) Play();
        }
        void ResetActors()
        {
            sequence.Cancel();
            CrewGore.Forget(_gangster); CrewGore.Forget(_owner);
            _gangster?.Dispose(); _owner?.Dispose();
            Clean(gangster); Clean(owner);
            var clips = new PedClips { Walk = sequence.walk, Idle = sequence.idle,
                Hit = sequence.hitBody, Death = sequence.fall };
            _gangster = new CrewWalker { DisplayName = "ENFORCER", RoamsAlone = false };
            _owner = new CrewWalker { DisplayName = "SHOP OWNER", RoamsAlone = false };
            _gangster.InitAt(gangster, clips, _gangsterStart, _gangsterRotation);
            _owner.InitAt(owner, clips, _ownerStart, _ownerRotation);
        }
        static void Clean(Transform actor)
        {
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>()) renderer.SetPropertyBlock(null);
            foreach (var tf in actor.GetComponentsInChildren<Transform>())
                if (tf.name == "Stain") Destroy(tf.gameObject);
        }
        public void Replay() { ResetActors(); Play(); }
        void Play()
        {
            if (!sequence.Begin(_gangster, _owner, shop, inside.position, outside.position, street.position))
                Debug.LogError("[OwnerBeatingDemo] Cannot start: check actors, anchors and animation references.");
        }
        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.rKey.wasPressedThisFrame) Replay();
                if (kb.spaceKey.wasPressedThisFrame) { _paused = !_paused; ApplySpeed(); }
                if (kb.digit1Key.wasPressedThisFrame) { _slow = false; ApplySpeed(); }
                if (kb.digit2Key.wasPressedThisFrame) { _slow = true; ApplySpeed(); }
            }
            _gangster?.TickCrew(Time.deltaTime);
            _owner?.TickCrew(Time.deltaTime);
        }
        void ApplySpeed() { Time.timeScale = _paused ? 0f : _slow ? 0.35f : 1f; }
        void OnGUI()
        {
            var previousMatrix = GUI.matrix;
            float scale = Mathf.Max(0.75f, Screen.height / 900f);
            float height = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.98f, 0.88f, 0.7f) } };
            GUI.Box(new Rect(16, height - 94, 440, 78), GUIContent.none);
            GUI.Label(new Rect(30, height - 88, 415, 30), "THE VISIT  /  " + Caption(), _label);
            if (GUI.Button(new Rect(30, height - 50, 100, 26), "Replay [R]")) Replay();
            if (GUI.Button(new Rect(140, height - 50, 130, 26), _paused ? "Resume [Space]" : "Pause [Space]"))
            { _paused = !_paused; ApplySpeed(); }
            if (GUI.Button(new Rect(280, height - 50, 155, 26), _slow ? "Normal speed [1]" : "Slow motion [2]"))
            { _slow = !_slow; ApplySpeed(); }
            GUI.matrix = previousMatrix;
        }
        string Caption() => sequence.Phase switch
        {
            OwnerBeatingSequence.BeatPhase.Approach => "AT THE DOOR",
            OwnerBeatingSequence.BeatPhase.Open or OwnerBeatingSequence.BeatPhase.Enter => "INSIDE THE SHOP",
            OwnerBeatingSequence.BeatPhase.Address or OwnerBeatingSequence.BeatPhase.Threat or OwnerBeatingSequence.BeatPhase.ClearDoor or OwnerBeatingSequence.BeatPhase.Extract => "OUTSIDE. NOW.",
            OwnerBeatingSequence.BeatPhase.SquareUp or OwnerBeatingSequence.BeatPhase.Combo or OwnerBeatingSequence.BeatPhase.Punch => "A MESSAGE DELIVERED",
            OwnerBeatingSequence.BeatPhase.GroinStrike => "THE LAST WORD",
            OwnerBeatingSequence.BeatPhase.Down or OwnerBeatingSequence.BeatPhase.Recover => "STILL BREATHING",
            OwnerBeatingSequence.BeatPhase.Return or OwnerBeatingSequence.BeatPhase.Close => "BACK THROUGH THE DOOR",
            OwnerBeatingSequence.BeatPhase.Complete => "THE VISIT IS OVER",
            OwnerBeatingSequence.BeatPhase.Cancelled => "INTERRUPTED",
            _ => "READY"
        };
        void OnDestroy()
        {
            sequence?.Cancel();
            CrewGore.Forget(_gangster); CrewGore.Forget(_owner);
            _gangster?.Dispose(); _owner?.Dispose();
            Time.timeScale = _savedTimeScale;
        }
    }
}
