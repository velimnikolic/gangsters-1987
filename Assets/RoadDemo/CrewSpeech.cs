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

        static CrewWalker Standing(DemoCrews.Unit unit)
        {
            foreach (var man in unit.All())
                if (man != null && !man.Dead)
                    return man;
            return null;
        }
    }
}
