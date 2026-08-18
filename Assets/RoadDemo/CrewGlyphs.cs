using UnityEngine;

namespace RoadDemo
{
    // What a man is doing, as one glyph that moves: the same sign rides over him
    // on the map and sits in his crew's block on the bar, so the two never say
    // different things. Out of the pack's flat icon cut (DemoUi), tinted in place:
    // an arrow that nudges forward while he walks, crossed blades that flash while
    // he fights, a speech mark that breathes while he talks, the skull when he is
    // down; nothing at all while he simply stands - a bar full of glyphs for men
    // standing still would be noise.
    public static class CrewGlyphs
    {
        public enum Activity { Idle, Moving, Fighting, Chatting, Down }

        static readonly Color Moving = new Color(0.557f, 0.835f, 0.992f); // DemoUi.Accent
        static readonly Color Fighting = new Color(1f, 0.36f, 0.30f);
        static readonly Color Chatting = new Color(0.72f, 0.82f, 0.92f);
        static readonly Color Down = new Color(1f, 0.36f, 0.30f, 0.75f);

        public static Activity Of(CrewWalker man)
        {
            if (man == null) return Activity.Idle;
            if (man.Dead) return Activity.Down;
            if (man.State == CrewWalker.Mode.Engaging) return Activity.Fighting;
            if (man.HasOrder) return Activity.Moving;
            if (man.Chatting) return Activity.Chatting;
            return Activity.Idle;
        }

        public static Sprite SpriteOf(Activity a) => a switch
        {
            Activity.Moving => DemoUi.IconArrow,
            Activity.Fighting => DemoUi.IconCombat,
            Activity.Chatting => DemoUi.IconChat,
            Activity.Down => DemoUi.IconDeath,
            _ => null,
        };

        public static Color TintOf(Activity a) => a switch
        {
            Activity.Moving => Moving,
            Activity.Fighting => Fighting,
            Activity.Chatting => Chatting,
            Activity.Down => Down,
            _ => Color.clear,
        };

        /// <summary>The glyph's motion at time <paramref name="t"/>: a nudge in
        /// reference pixels and an alpha, so bar and map animate identically.</summary>
        public static void Animate(Activity a, float t, float phase, out Vector2 nudge, out float alpha)
        {
            nudge = Vector2.zero;
            alpha = 1f;
            switch (a)
            {
                case Activity.Moving:
                    // marching: forward, back, forward
                    nudge.x = Mathf.Sin(t * 7f + phase) * 2.5f;
                    break;
                case Activity.Fighting:
                    alpha = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(t * 9f + phase));
                    break;
                case Activity.Chatting:
                    alpha = 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(t * 3f + phase));
                    break;
                case Activity.Down:
                    alpha = 0.8f;
                    break;
                default:
                    alpha = 0f;
                    break;
            }
        }

        /// <summary>One line for a popup or a tooltip.</summary>
        public static string Word(Activity a) => a switch
        {
            Activity.Moving => "On the move",
            Activity.Fighting => "In a fight",
            Activity.Chatting => "Talking",
            Activity.Down => "Down",
            _ => "Standing by",
        };
    }
}
