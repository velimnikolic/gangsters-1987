using System.Collections.Generic;
using LivingCity.Data;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The street's end of the voice layer: it turns a unit on the road into the man who
    /// answers for it, and hands that man to <see cref="CrewVoice"/>.
    ///
    /// Only OUR men speak. A rival crew and a police squad are units like any other and
    /// pass through the same order methods when the sim drives them, so the faction test
    /// lives here rather than at every call site - the player hears his own outfit answer
    /// and nobody else.
    ///
    /// The lieutenant does the talking. A crew is one voice giving one answer, and four
    /// men saying "on our way" over each other is a crowd, not a crew - the exceptions are
    /// the lines that are a man's own business (his bus out of town, the bag he picked up),
    /// which come through <see cref="SayMan"/> instead.
    /// </summary>
    public static class CrewSpeech
    {
        /// <summary>The crew answers its order, in its lieutenant's voice.</summary>
        public static void Say(DemoCrews.Unit unit, string key,
            CrewVoice.Priority priority = CrewVoice.Priority.Order)
        {
            if (unit == null || unit.Faction != 0 || unit.Wiped)
                return;

            var speaker = unit.Boss != null && !unit.Boss.Dead ? unit.Boss : Standing(unit);
            if (speaker == null)
                return;

            SayMan(speaker, key, priority);
        }

        /// <summary>One named man answers for himself.</summary>
        public static void SayMan(CrewWalker man, string key,
            CrewVoice.Priority priority = CrewVoice.Priority.Order)
        {
            if (man == null || man.Dead || man.Tf == null || string.IsNullOrEmpty(key))
                return;

            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            var member = roster != null && man.CharacterId >= 0 ? roster.Find(man.CharacterId) : null;
            if (member == null)
                return;

            CrewVoice.Say(key, member, man.Tf.position, priority, roster);
        }

        /// <summary>What the crew says when it is picked - the answer depends on where the
        /// men are standing when the player asks, which is the whole point of a selection
        /// line: it reports their state before he gives them anything to do.</summary>
        public static void Selected(DemoCrews.Unit unit)
        {
            if (unit == null || unit.Faction != 0 || unit.Wiped)
                return;

            var key = VoiceLines.SelReady;
            if (unit.Car != null) key = VoiceLines.SelCar;
            else if (CrewQuarters.Inside(unit)) key = VoiceLines.SelInside;
            else if (unit.IsDetachment) key = VoiceLines.SelRound;
            else if (unit.Standing() <= 1) key = VoiceLines.SelFew;
            else if (Hurt(unit)) key = VoiceLines.SelHurt;

            Say(unit, key, CrewVoice.Priority.Selection);
        }

        /// <summary>A crew that has been shot at and is still on its feet. Read off the
        /// men themselves rather than a flag, so it means what it says on the street.
        /// </summary>
        static bool Hurt(DemoCrews.Unit unit)
        {
            var down = 0;
            foreach (var man in unit.All())
                if (man != null && man.Dead)
                    down++;
            return down > 0;
        }

        // ------------------------------------------------------------------- the fight

        // When each man last said each kind of thing. A fight fires its events far faster
        // than a mouth can keep up - a man is hit three times in a second - and the guard
        // in CrewVoice is only there to swallow one click read twice, so the pacing of the
        // shouting is decided here, per man and per kind.
        static readonly Dictionary<long, float> Said = new Dictionary<long, float>(256);

        /// <summary>
        /// ANY man on the street cries out - ours, a rival's, an officer's.
        ///
        /// One of our own speaks in the voice his ledger entry was cast in, so the man who
        /// says "on our way" is the man who screams when he is hit. A man on nobody's books
        /// has a bank dealt off his own number instead: a fight where only our side makes a
        /// sound is half a fight.
        /// </summary>
        public static void Cry(CrewWalker man, string key,
            CrewVoice.Priority priority = CrewVoice.Priority.Combat, float cooldown = 0f)
        {
            if (man == null || man.Tf == null || string.IsNullOrEmpty(key))
                return;

            // A dead man says one thing (his last), so the death cry is exempt from the
            // test that would otherwise silence it - it is spoken AS he goes down.
            if (man.Dead && priority != CrewVoice.Priority.Death)
                return;

            if (cooldown > 0f)
            {
                var slot = ((long)man.GetHashCode() << 20) ^ key.GetHashCode();
                var now = Time.unscaledTime;
                if (Said.TryGetValue(slot, out var last) && now - last < cooldown)
                    return;
                Said[slot] = now;
            }

            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            var member = roster != null && man.CharacterId >= 0
                ? roster.Find(man.CharacterId)
                : null;

            if (member != null)
                CrewVoice.Say(key, member, man.Tf.position, priority, roster);
            else
                CrewVoice.SayUnbooked(key, Seed(man), man.Tf.position, priority);
        }

        /// <summary>
        /// A man has just gone down, and the street answers for him: whoever shot him says
        /// so, and one of his own calls it out.
        ///
        /// Both are Combat and the dying cry is Death, so the order they come in decides
        /// itself - the man's own last sound plays and these two lose to it. That is right:
        /// a brag over the top of the scream is a comedy.
        /// </summary>
        public static void Fell(CrewWalker man, CrewWalker killer)
        {
            if (man == null)
                return;

            var crews = DemoCrews.Active;
            var unit = crews != null ? crews.UnitOf(man) : null;

            // His own: the nearest crewmate still standing shouts. Not the whole crew, and
            // not the man himself - he is past shouting.
            if (unit != null)
            {
                var mate = Nearest(unit, man);
                if (mate != null)
                    Cry(mate, LivingCity.Data.VoiceLines.LossMan, cooldown: 4f);
            }

            if (killer == null || killer.Dead || killer.Faction == man.Faction)
                return;

            // THE LAST MAN OF A CREW IS A DIFFERENT LINE. The brag is for one body; the
            // street being theirs is for the crew being finished, and only one of the two
            // is said.
            Cry(killer,
                unit != null && unit.Wiped ? LivingCity.Data.VoiceLines.WinOver : LivingCity.Data.VoiceLines.DropGot,
                cooldown: 3f);
        }

        /// <summary>
        /// "Grenade!" - shouted by whoever is standing closest to where one is about to
        /// land, from any family but the one that threw it. One voice for the whole throw:
        /// six men shouting the same word at once is a noise, not a warning.
        /// </summary>
        public static void Warn(DemoCrews crews, Vector3 at, int thrownBy)
        {
            crews = crews != null ? crews : DemoCrews.Active;
            if (crews == null)
                return;

            CrewWalker best = null;
            var bestSq = WarnRadius * WarnRadius;
            foreach (var unit in crews.Units)
            {
                if (unit == null || unit.Wiped || unit.Faction == thrownBy)
                    continue;
                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null) continue;
                    var d = (man.Tf.position - at).sqrMagnitude;
                    if (d >= bestSq) continue;
                    bestSq = d;
                    best = man;
                }
            }

            if (best != null)
                Cry(best, LivingCity.Data.VoiceLines.WarnCall, cooldown: 5f);
        }

        /// <summary>How near a grenade has to land for anybody to call it. Wide enough to
        /// cover the men it would actually take, and no wider - a shout from across the
        /// street about a bomb nobody can see is somebody else's film.</summary>
        const float WarnRadius = 22f;

        /// <summary>The nearest man of the unit still on his feet, other than this one.
        /// </summary>
        static CrewWalker Nearest(DemoCrews.Unit unit, CrewWalker not)
        {
            CrewWalker best = null;
            var bestSq = float.MaxValue;
            foreach (var man in unit.All())
            {
                if (man == null || man == not || man.Dead || man.Tf == null) continue;
                var d = (man.Tf.position - not.Tf.position).sqrMagnitude;
                if (d >= bestSq) continue;
                bestSq = d;
                best = man;
            }
            return best;
        }

        /// <summary>The number a body is known by when the books do not know him. His
        /// rival id where there is one (they are negative and their own), and otherwise the
        /// object itself - stable for as long as the body stands, which is all it has to
        /// be.</summary>
        static int Seed(CrewWalker man) =>
            man.CharacterId != 0 ? man.CharacterId : man.GetHashCode();

        /// <summary>Static state outlives Play when domain reload is off.</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Said.Clear();

        static CrewWalker Standing(DemoCrews.Unit unit)
        {
            foreach (var man in unit.All())
                if (man != null && !man.Dead)
                    return man;
            return null;
        }
    }
}
