using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The interaction beat the street was missing, in two acts. First the WORD: the
    /// man stands at the door, faced to it, and makes his point with his free hand
    /// (ArmBeat.Talk - a derived gesture, no clip). Then he STEPS INSIDE for a moment
    /// instead of the state flipping while he stands on the kerb - the same trick the
    /// civilians use (CivilianAgent.Mode.Inside): the body is switched off at the door
    /// and comes back out of it a few seconds later; no interior exists and none is
    /// needed. A caller in a hurry - a robbery - passes talk 0 and goes straight in.
    ///
    /// Deliberately refused while the man is in a fight: a beat that hid a man from the
    /// bullets aimed at him would be an exploit, not a flourish - and a fight that
    /// starts MID-WORD cancels the visit the same way.
    /// </summary>
    public sealed class DoorBeat : MonoBehaviour
    {
        /// <summary>How long the doorstep call takes inside, wall-clock seconds.</summary>
        public const float InsideSeconds = 2.6f;

        /// <summary>How long the word at the door runs before he goes in.</summary>
        public const float TalkSeconds = 1.7f;

        struct Call
        {
            public CrewWalker Man;
            public Vector3 Door;

            /// <summary>Still standing at the door making his point; hidden inside once
            /// the word is done.</summary>
            public bool Inside;

            public float NextAt;

            /// <summary>The wall-clock backstop. Sim time can crawl (a low timescale,
            /// a hitch) and a man the sim forgot indoors is a man lost to the player -
            /// whatever happens, he is back on the street inside a few real seconds.</summary>
            public float RealNextAt;
        }

        static DoorBeat instance;
        readonly List<Call> calls = new List<Call>();

        static bool UnderFire(CrewWalker man) =>
            Time.time - StreetAlarm.LastShotAt < 8f &&
            (StreetAlarm.LastShotPos - man.Tf.position).sqrMagnitude < 60f * 60f;

        public static void Visit(CrewWalker man, Vector3 door, float talk = TalkSeconds)
        {
            if (man == null || man.Dead || man.Tf == null ||
                !man.Tf.gameObject.activeInHierarchy)
                return;
            // A man under fire does not pop indoors for a chat.
            if (UnderFire(man))
                return;

            if (instance == null)
            {
                var go = new GameObject("Door Beat") { hideFlags = HideFlags.DontSave };
                instance = go.AddComponent<DoorBeat>();
            }

            // one visit per man at a time - the second caller's beat is already playing
            for (var i = 0; i < instance.calls.Count; i++)
                if (instance.calls[i].Man == man)
                    return;

            var call = new Call { Man = man, Door = door };
            if (talk > 0f)
            {
                ArmBeat.Talk(man, door, talk);
                call.Inside = false;
                call.NextAt = Time.time + talk;
                call.RealNextAt = Time.unscaledTime + talk * 4f;
            }
            else
            {
                man.Tf.gameObject.SetActive(false);
                call.Inside = true;
                call.NextAt = Time.time + InsideSeconds;
                call.RealNextAt = Time.unscaledTime + InsideSeconds * 4f;
            }

            instance.calls.Add(call);
        }

        void Update()
        {
            for (var i = calls.Count - 1; i >= 0; i--)
            {
                var call = calls[i];

                // A man gone from the street mid-word - died, despawned, retasked into a
                // car - takes his visit with him. (Once INSIDE he is inactive by design,
                // so this test belongs to the talking phase only.)
                if (!call.Inside &&
                    (call.Man == null || call.Man.Tf == null || call.Man.Dead ||
                     !call.Man.Tf.gameObject.activeInHierarchy))
                {
                    calls.RemoveAt(i);
                    continue;
                }

                // Sim time says this phase is over - or the wall clock does, while the
                // game is not paused. A pause holds the beat: bodies changing in a
                // frozen city read as a glitch, and unpausing moves it on at once.
                if (Time.time < call.NextAt &&
                    (Time.unscaledTime < call.RealNextAt || Time.timeScale <= 0.001f))
                    continue;

                if (!call.Inside)
                {
                    // The word is done: in he goes - unless the street caught fire
                    // around him meanwhile, when the visit is simply off.
                    calls.RemoveAt(i);
                    if (UnderFire(call.Man))
                        continue;
                    call.Man.Tf.gameObject.SetActive(false);
                    call.Inside = true;
                    call.NextAt = Time.time + InsideSeconds;
                    call.RealNextAt = Time.unscaledTime + InsideSeconds * 4f;
                    calls.Add(call);
                    continue;
                }

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
                if (calls[i].Inside && calls[i].Man?.Tf != null)
                    calls[i].Man.Tf.gameObject.SetActive(true);
            calls.Clear();
            if (instance == this)
                instance = null;
        }
    }
}
