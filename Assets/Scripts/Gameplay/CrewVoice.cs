using System.Collections.Generic;
using LivingCity.Data;
using LivingCity.Personnel;
using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The men answering their orders.
    ///
    /// One call - <see cref="Say"/> - takes a KEY (what is said) and the MAN who says it,
    /// and everything else is decided here: which actor he speaks in (VoiceCasting), which
    /// take of the key gets used, whether he is allowed to talk over whatever is already
    /// being said, and where in the world it comes from.
    ///
    /// It is hung off the ORDER METHODS, never off the cards that call them, so the street
    /// card, the paper turf map and the ledger all speak with one voice and no scene has to
    /// wire anything up. The component installs itself the first time somebody speaks, the
    /// way every other runtime layer in this project does.
    ///
    /// Everything about it is allowed to come back empty. No database, no bank, no take for
    /// this key - the game is simply quiet, which is what an unfinished recording tier has
    /// to sound like.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrewVoice : MonoBehaviour
    {
        /// <summary>Who gets to talk over whom. A refusal the player never hears is a
        /// refusal that did not happen, so it outranks the order that provoked it; an
        /// order outranks the click that selected the crew.</summary>
        public enum Priority
        {
            Selection = 0,
            Order = 1,
            Refusal = 2,
        }

        /// <summary>A line cut off before this much of it has been heard reads as a bug
        /// rather than an interruption, so a higher-priority line waits this long.</summary>
        const float MinAudible = 0.25f;

        /// <summary>The same man saying the same thing twice inside this is one order the
        /// player gave twice, not two orders - so he says it once.</summary>
        const float RepeatGuard = 0.6f;

        /// <summary>One in this many of the ORDINARY lines is spoken. Walking, taking
        /// cover, getting in and out of a car and being picked are what the player does all
        /// day; a man answering every one of them turns his own voice into a click sound.
        /// The rare orders - the kill, the grenade, the run for it, the refusals - are not
        /// thinned at all, because each of those is an event the player wants confirmed.
        /// </summary>
        const int SpeakOneRoutineIn = 5;

        /// <summary>The lines that get thinned. Kept as a set here rather than as a flag on
        /// each call site so the rule is one list somebody can read, and so a line added to
        /// the sheet is loud until it is deliberately put in here.</summary>
        static readonly HashSet<string> Routine = new HashSet<string>
        {
            VoiceLines.SelReady, VoiceLines.SelCar, VoiceLines.SelInside,
            VoiceLines.SelHurt, VoiceLines.SelRound, VoiceLines.SelFew,
            VoiceLines.OrdMove, VoiceLines.OrdRun, VoiceLines.OrdCover,
            VoiceLines.OrdBoard, VoiceLines.OrdOut,
            VoiceLines.OrdInside, VoiceLines.OrdOutside,
        };

        /// <summary>Past this the order is somebody else's business - the camera is
        /// looking at another part of town and a crew off the edge of it should not be
        /// heard giving orders.</summary>
        const float Earshot = 140f;

        /// <summary>The men are under the street, not over it. A voice that sits level with
        /// the gunfire turns every order into an announcement; this is one number and it is
        /// the only place to tune how loud the outfit is.</summary>
        const float Volume = 0.5f;

        static CrewVoice instance;

        AudioSource source;

        // what is playing, and how much of it is left to hear
        Priority speakingPriority;
        float speakingUntil;

        // how many ordinary lines have come due, counted over the whole outfit: what wears
        // on the ear is how often ANY of them speaks, so a player working down a row of six
        // crews hears one answer and not six
        int routine;

        // the last take used per (bank, key), so one man never says the same words twice
        // running while another take of them exists
        readonly Dictionary<int, int> lastTake = new Dictionary<int, int>(128);

        // the last thing each man said and when, which is what stops a double right click
        // from being answered twice
        readonly Dictionary<int, float> saidAt = new Dictionary<int, float>(64);
        readonly Dictionary<int, string> saidWhat = new Dictionary<int, string>(64);

        /// <summary>The man speaks. Everything is optional: a null man, an unrecorded key
        /// or no database at all is silence.</summary>
        public static void Say(string key, Character speaker, Vector3 at,
            Priority priority = Priority.Order, Roster roster = null)
        {
            if (string.IsNullOrEmpty(key) || speaker == null || speaker.Gone)
                return;
            var bank = VoiceCasting.BankFor(speaker, roster);
            if (string.IsNullOrEmpty(bank))
                return;
            Speak(key, bank, VoiceCasting.PitchFor(speaker), speaker.Id, at, priority);
        }

        /// <summary>The desk answers - an order filed with the office, or an act that is
        /// money and paper rather than men in a doorway. No man says it and it carries no
        /// position: it is heard at the ear, like the rest of the ledger.</summary>
        public static void Office(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            var db = VoiceDatabase.Instance;
            var bank = db != null ? db.OfficeBankId : null;
            if (string.IsNullOrEmpty(bank))
                return;
            Speak(key, bank, 1f, 0, Vector3.zero, Priority.Order, atEar: true);
        }

        static void Speak(string key, string bank, float pitch, int speakerId, Vector3 at,
            Priority priority, bool atEar = false)
        {
            var self = Ensure();
            if (self == null)
                return;
            self.Play(key, bank, pitch, speakerId, at, priority, atEar);
        }

        void Play(string key, string bank, float pitch, int speakerId, Vector3 at,
            Priority priority, bool atEar)
        {
            var now = Time.unscaledTime;

            // The same man, the same words, a moment ago: one order given twice.
            if (speakerId != 0 &&
                saidAt.TryGetValue(speakerId, out var last) && now - last < RepeatGuard &&
                saidWhat.TryGetValue(speakerId, out var what) && what == key)
                return;

            // One voice at a time. A line already running is talked over only by something
            // that outranks it, and even then not in its first quarter-second.
            if (source != null && source.isPlaying && now < speakingUntil &&
                (priority <= speakingPriority || now < speakingUntil - HeardEnough()))
                return;

            if (!atEar && !InEarshot(at))
                return;

            var db = VoiceDatabase.Instance;
            if (db == null)
                return;
            var takes = db.Takes(bank, key);
            if (takes.Length == 0)
                return;

            var clip = Pick(takes, bank, key);
            if (clip == null)
                return;

            // THE THINNING IS THE LAST GATE, and deliberately so: it is only spent on a
            // line that was otherwise about to be heard. Counting an order that was out of
            // earshot, or one that lost to a refusal talking over it, would leave the
            // player clicking five times for a man who never says anything.
            if (Routine.Contains(key) && ++routine % SpeakOneRoutineIn != 0)
                return;

            source.transform.position = atEar ? EarPosition() : at;
            source.spatialBlend = atEar ? 0f : 0.7f;
            source.pitch = pitch;
            source.clip = clip;
            source.volume = Volume;
            source.Play();

            speakingPriority = priority;
            speakingUntil = now + clip.length;

            if (speakerId == 0)
                return;
            saidAt[speakerId] = now;
            saidWhat[speakerId] = key;
        }

        /// <summary>How much of the running line has to have been heard before anything may
        /// cut it off. Expressed against its end so the check above reads in one line.
        /// </summary>
        float HeardEnough() =>
            source != null && source.clip != null
                ? Mathf.Max(0f, source.clip.length - MinAudible)
                : 0f;

        AudioClip Pick(AudioClip[] takes, string bank, string key)
        {
            if (takes.Length == 1)
                return takes[0];

            var slot = (bank.GetHashCode() * 397) ^ key.GetHashCode();
            lastTake.TryGetValue(slot, out var previous);

            var index = Random.Range(0, takes.Length);
            if (index == previous)
                index = (index + 1) % takes.Length;

            lastTake[slot] = index;
            return takes[index];
        }

        static bool InEarshot(Vector3 at)
        {
            var ear = Ear();
            return ear == null || (ear.transform.position - at).sqrMagnitude <= Earshot * Earshot;
        }

        static Vector3 EarPosition()
        {
            var ear = Ear();
            return ear != null ? ear.transform.position : Vector3.zero;
        }

        static AudioListener ear;

        /// <summary>The ear, cached: a scene has one listener and it does not move between
        /// objects, but a scene load replaces it - so the cache is re-taken whenever the
        /// one it holds has gone.</summary>
        static AudioListener Ear()
        {
            if (ear == null)
                ear = FindAnyObjectByType<AudioListener>();
            return ear;
        }

        static CrewVoice Ensure()
        {
            if (instance != null)
                return instance;
            if (!Application.isPlaying)
                return null;

            var go = new GameObject("CrewVoice");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CrewVoice>();
            return instance;
        }

        void Awake()
        {
            instance = this;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0.7f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 20f;
            source.maxDistance = Earshot;
            source.dopplerLevel = 0f;
        }

        /// <summary>Static state outlives Play when domain reload is off - the same reset
        /// every other static holder in this project makes.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            instance = null;
            ear = null;
        }
    }
}
