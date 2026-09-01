using System.Collections.Generic;
using LivingCity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// The screen-edge HUD after dark.
    ///
    /// The design darkens its whole HUD region by re-declaring the --lg-* tokens over
    /// it, so the SAME panels come out dark. This is that pass: a table of tokens in
    /// their two faces, and a register of the graphics wearing them.
    ///
    /// Shared rather than kept privately by each HUD, because the clock strip and the
    /// panels beside it are one bar across the top of the screen. Two tables would let
    /// the two halves of that bar cross at different moments, or - as they did - cross
    /// on different token sets, leaving a segmented control printing near-black type on
    /// a near-black plate because its ink was in one table and not the other.
    ///
    /// Nothing here decides WHEN it is night: <see cref="DemoSky.Nightness"/> does, the
    /// one curve the lamps, the windows, the headlights and the grade all ramp on, so
    /// the paper cools at the moment the street lights come on.
    /// </summary>
    public sealed class HudNight
    {
        /// <summary>
        /// How solid the screen-edge HUD is. The design gives its whole hud region
        /// opacity 0.8 - a fifth of the city shows through every panel, which is what
        /// keeps the paper laid ON the street rather than bolted over it.
        ///
        /// Held here because the bar is made of three canvases - the clock strip, the
        /// panels, the corner plate - and they are one region in the design. Two of
        /// them were fully solid while the third was not, and the top bar read as two
        /// different materials butted together.
        /// </summary>
        public const float Alpha = 0.8f;

        /// <summary>One token, in the two faces the design gives it. Base is the colour
        /// the kit paints with; Day and Night are what the HUD re-declares it to.
        /// </summary>
        readonly struct Token
        {
            public readonly Color Base, Day, Night;

            public Token(int b, int day, int night)
            {
                Base = LedgerV2.Rgb2(b);
                Day = LedgerV2.Rgb2(day);
                Night = LedgerV2.Rgb2(night);
            }
        }

        /// <summary>
        /// The design's night declarations, token for token, plus the four the clock
        /// strip mixes its own paper from. Tokens the design leaves alone are absent on
        /// purpose - the gold of the ledger key and of the hour, the cream on a dark
        /// band, the empty pip - and a colour that is not in this table is never
        /// touched.
        /// </summary>
        static readonly Token[] Tokens =
        {
            new Token(0xf4efe9, 0xf7f2e6, 0x231c17),   // panel
            new Token(0xede7df, 0xefe8d8, 0x2b231d),   // panel band
            new Token(0xe7e0d9, 0xe7e0d9, 0x1d1713),   // panel dark
            new Token(0x1a1512, 0x1a1512, 0xefe6d5),   // ink
            new Token(0x231e1b, 0x231e1b, 0xe6dccb),   // body
            new Token(0x675b53, 0x675b53, 0x9c8f83),   // muted
            new Token(0x6d6059, 0x6d6059, 0x8d8074),   // label
            new Token(0xd8cfc7, 0xd8cfc7, 0x3a312a),   // hair
            new Token(0xc5bcb4, 0xc5bcb4, 0x4a4038),   // rule
            new Token(0xc1b5ab, 0xc1b5ab, 0x5b5047),   // dotted
            new Token(0xa2968c, 0xa2968c, 0x3a312a),   // sheet rule
            new Token(0xebddb9, 0xebddb9, 0x3a2a20),   // picked
            new Token(0xac3031, 0xac3031, 0xd0453f),   // red
            new Token(0x18120f, 0x18120f, 0x120e0c),   // head
            new Token(0xf3ede7, 0xf3ede7, 0xefe6d5),   // head cream
            new Token(0xfdfaf0, 0xfdfaf0, 0x241d18),   // slip stock
            new Token(0xf2ebd8, 0xf2ebd8, 0x1e1814),   // slip stock, low
            new Token(0x3b3226, 0x3b3226, 0xded3c2),   // slip copy
            new Token(0x8a7756, 0x8a7756, 0x9c8f83),   // slip label
            new Token(0x8f2119, 0x8f2119, 0xe0655c),   // red pen

            // The clock strip mixes its paper from its own four, which predate the
            // terminal palette. They cross to the same three faces.
            new Token(0xf4ecd6, 0xf7f2e6, 0x231c17),   // the strip's paper
            new Token(0x2b2418, 0x2b2418, 0xefe6d5),   // the strip's ink
        };

        /// <summary>How near two colours must be to be the same token. Well under the
        /// gap between any two entries above, and well over the rounding a colour takes
        /// on its way through a hex literal.</summary>
        const float Epsilon = 0.004f;

        /// <summary>How finely the crossing is stepped. A smooth ramp would re-ink every
        /// graphic every frame for a colour nobody can see move; sixteen steps across a
        /// twilight is finer than the eye.</summary>
        const int Steps = 16;

        readonly struct Inked
        {
            public readonly Graphic Target;
            public readonly Color Day, Night;

            public Inked(Graphic target, Token token, float alpha)
            {
                Target = target;
                Day = new Color(token.Day.r, token.Day.g, token.Day.b, alpha);
                Night = new Color(token.Night.r, token.Night.g, token.Night.b, alpha);
            }
        }

        readonly List<Inked> _inked = new List<Inked>();
        int _step = -1;

        /// <summary>
        /// Take everything under this root into the palette's keeping.
        ///
        /// The two faces come from the TABLE, never from the colour that was read: the
        /// read colour only says which token this is. Storing what was read is what
        /// broke the clock - a graphic re-registered after a relight had its NIGHT
        /// colour recorded as its day one, and the strip stayed dark through the
        /// following afternoon.
        ///
        /// A graphic that has already been tinted therefore matches nothing and is
        /// skipped, which is exactly right: it is already somebody's.
        /// </summary>
        public void Register(Transform root)
        {
            if (root == null)
                return;

            var found = root.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < found.Length; i++)
            {
                var graphic = found[i];
                var colour = graphic.color;
                for (var t = 0; t < Tokens.Length; t++)
                {
                    var token = Tokens[t];
                    if (Mathf.Abs(colour.r - token.Base.r) > Epsilon ||
                        Mathf.Abs(colour.g - token.Base.g) > Epsilon ||
                        Mathf.Abs(colour.b - token.Base.b) > Epsilon)
                        continue;

                    _inked.Add(new Inked(graphic, token, colour.a));
                    break;
                }
            }
            Relight(force: true);
        }

        /// <summary>Drop the graphics that went down with their panel, so the register
        /// does not grow a dead entry per repaint.</summary>
        public void ForgetDead()
        {
            for (var i = _inked.Count - 1; i >= 0; i--)
                if (_inked[i].Target == null)
                    _inked.RemoveAt(i);
        }

        /// <summary>How dark it is where the sky says, quantised.</summary>
        public static float Nightness()
        {
            var clock = LivingCity.Ambient.DayClock.Current;
            return clock != null ? DemoSky.Nightness(clock.Hour) : 0f;
        }

        /// <summary>Cross everything registered to wherever the sky is now. Only on a
        /// step change: the hour moves every frame and the colour does not.</summary>
        public void Relight(bool force = false)
        {
            var step = Mathf.RoundToInt(Nightness() * Steps);
            if (!force && step == _step)
                return;
            _step = step;

            var night = step / (float)Steps;
            for (var i = 0; i < _inked.Count; i++)
            {
                var ink = _inked[i];
                if (ink.Target != null)
                    ink.Target.color = Color.Lerp(ink.Day, ink.Night, night);
            }
        }
    }
}
