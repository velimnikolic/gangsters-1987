using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The interaction beat the street was missing: a man STEPS INSIDE the shop for a
    /// moment instead of the state flipping while he stands on the kerb. Same trick the
    /// civilians already use (CivilianAgent.Mode.Inside) - the body is switched off at
    /// the door and comes back out of it a few seconds later; no interior exists and
    /// none is needed.
    ///
    /// Deliberately refused while the man is in a fight: a beat that hid a man from the
    /// bullets aimed at him would be an exploit, not a flourish.
    /// </summary>
    public sealed class DoorBeat : MonoBehaviour
    {
        /// <summary>How long the doorstep call takes, wall-clock seconds.</summary>
        public const float InsideSeconds = 2.6f;

        struct Call
        {
            public CrewWalker Man;
            public Vector3 Door;
            public float OutAt;
        }

        static DoorBeat instance;
        readonly List<Call> calls = new List<Call>();

        public static void Visit(CrewWalker man, Vector3 door)
        {
            if (man == null || man.Dead || man.Tf == null ||
                !man.Tf.gameObject.activeInHierarchy)
                return;
            // A man under fire does not pop indoors for a chat.
            if (Time.time - StreetAlarm.LastShotAt < 8f &&
                (StreetAlarm.LastShotPos - man.Tf.position).sqrMagnitude < 60f * 60f)
                return;

            if (instance == null)
            {
                var go = new GameObject("Door Beat") { hideFlags = HideFlags.DontSave };
                instance = go.AddComponent<DoorBeat>();
            }

            man.Tf.gameObject.SetActive(false);
            instance.calls.Add(new Call
            {
                Man = man,
                Door = door,
                OutAt = Time.time + InsideSeconds,
            });
        }

        void Update()
        {
            for (var i = calls.Count - 1; i >= 0; i--)
            {
                var call = calls[i];
                if (Time.time < call.OutAt)
                    continue;
                calls.RemoveAt(i);

                if (call.Man == null || call.Man.Tf == null)
                    continue;
                // He comes out of the door he went into, whatever the crew did around
                // him meanwhile - and a man who died invisibly (a blast, a purge) is
                // left where the systems put him.
                if (!call.Man.Dead)
                    call.Man.Tf.position = call.Door;
                call.Man.Tf.gameObject.SetActive(true);
            }
        }

        void OnDestroy()
        {
            // Never strand an invisible man: whatever ends the beat runner ends the
            // beats, bodies first.
            for (var i = 0; i < calls.Count; i++)
                if (calls[i].Man?.Tf != null)
                    calls[i].Man.Tf.gameObject.SetActive(true);
            calls.Clear();
            if (instance == this)
                instance = null;
        }
    }
}
