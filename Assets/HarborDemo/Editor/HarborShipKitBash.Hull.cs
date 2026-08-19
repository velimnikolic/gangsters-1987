using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo.EditorTools
{
    /// <summary>
    /// The hull, lofted rather than walled. Wall modules can only stand a flat panel on
    /// a straight edge, so a hull built of them is a box however many edges it is given;
    /// this builds her the way a shipwright reads a lines plan instead - a handful of
    /// levels from the flat of the bottom up to the top of the bulwark, each one a plan
    /// curve (rounded aft, parallel through the middle body, tapering into the stem),
    /// and the skin is the quads lofted from one level to the next.
    ///
    /// What the levels are for: the lower ones are narrower and pulled in at the ends,
    /// which gives the turn of the bilge and a cut-up forefoot; the upper ones are wider
    /// and reach further forward, which gives topsides that flare and a stem that rakes
    /// out ahead of the deck line; the after ends run from a fine bottom to a full deck,
    /// which rounds the stern into a counter. The bulwark's top carries the sheer - up
    /// to the bow, a little up to the stern - so the line along her side is a curve
    /// rather than a ruler, and it steps at the forecastle break.
    ///
    /// The bands are levels too, not decals: the boot-top is the strip between the
    /// waterline and the level above it, the rubbing strake the strip between its own
    /// pair. That way there is nothing to z-fight and the paint follows the curve.
    ///
    /// Everything else - hatches, house, gear, fittings - stands on the flat deck and is
    /// unchanged by this; only the shell is lofted. HarborShipSpec.Beam is still the
    /// beam amidships, so the shipping's berthing numbers hold.
    /// </summary>
    public static partial class HarborShipKitBash
    {
        const int Stations = 34;            // sections a side, cosine-spaced towards the ends

        /// <summary>One level of the lines: how wide she is there, how far in her ends
        /// come, and how fine the entry is.</summary>
        sealed class Level
        {
            public System.Func<float, float> Y;
            public float BeamK = 1f;
            /// <summary>How far the level's after end is forward of the transom, and how
            /// far its stem is aft of the bow (negative reaches ahead - a raked stem).</summary>
            public float SternIn, BowIn;
            /// <summary>Half width at the after end, as a fraction of the level's beam.</summary>
            public float TransomK = 0.72f;
            /// <summary>The bow taper: its length in bow-lengths, and its exponent -
            /// bigger is finer, under one is full and flaring.</summary>
            public float EntryK = 1f, Entry = 1f;
            /// <summary>The length of the stern's rounding, in beams.</summary>
            public float AftRound = 0.5f;
        }

        /// <summary>The deck line, kept so the decks and the fittings can be cut to it.</summary>
        sealed class Rings
        {
            public Vector3[] DeckStar, DeckPort;
            public int Break;                // last station still abaft the forecastle break
        }

        // ------------------------------------------------------------ the lines

        static List<Level> Lines(HarborShipSpec s)
        {
            float strake = s.DeckY * 0.5f;
            float strakeTop = strake + Mathf.Min(0.34f, s.DeckY * 0.09f);
            float sheerFore = 0.013f * s.Length, sheerAft = 0.0055f * s.Length;
            float lowest = 0.34f;            // where the sheer is at its lowest, along her length

            float Sheer(float z)
            {
                float u = Mathf.InverseLerp(s.SternZ, s.BowZ, z);
                return u >= lowest
                    ? sheerFore * Mathf.Pow((u - lowest) / (1f - lowest), 2f)
                    : sheerAft * Mathf.Pow((lowest - u) / lowest, 2f);
            }

            float Bulwark(float z) =>
                (z > s.ForecastleZ ? s.ForecastleY : s.DeckY) + HarborShipSpec.HalfCourse + Sheer(z);

            return new List<Level>
            {
                // the flat of the bottom: narrow, and well in at both ends
                new Level { Y = _ => -HarborShipSpec.Course - 0.6f, BeamK = 0.44f, SternIn = 2.4f, BowIn = 3.6f,
                            TransomK = 0.3f, EntryK = 1.4f, Entry = 1.7f, AftRound = 0.65f },
                // the turn of the bilge, kept well down: she rides high enough in the
                // demo's water that anything tucked in higher would show as a hull
                // standing out of the sea in dry dock
                new Level { Y = _ => -2.4f, BeamK = 0.9f, SternIn = 1f, BowIn = 2f,
                            TransomK = 0.5f, EntryK = 1.2f, Entry = 1.45f, AftRound = 0.58f },
                // the waterline
                new Level { Y = _ => 0.25f, BeamK = 0.97f, SternIn = 0.15f, BowIn = 0.8f,
                            TransomK = 0.63f, EntryK = 1.1f, Entry = 1.3f },
                // the top of the boot-top band
                new Level { Y = _ => 0.95f, BeamK = 0.985f, SternIn = 0.05f, BowIn = 0.5f,
                            TransomK = 0.66f, EntryK = 1.05f, Entry = 1.22f },
                // the strake band, bottom and top
                new Level { Y = _ => strake, BeamK = 0.995f, BowIn = 0.25f, TransomK = 0.7f, Entry = 1.1f },
                new Level { Y = _ => strakeTop, BeamK = 1f, BowIn = 0.2f, TransomK = 0.71f, Entry = 1.06f },
                // the deck line: her full beam, the stem already ahead of the waterline
                // and the counter overhanging the rudder by as much, which also keeps
                // her ends even - the bake pivots on the footprint, and an overhang at
                // one end alone would walk the pivot off the spec's origin
                new Level { Y = _ => s.DeckY, BeamK = 1f, SternIn = -0.25f, BowIn = -0.25f, TransomK = 0.74f, Entry = 0.92f },
                // the top of the bulwark, carrying the sheer and the forecastle's step.
                // Its ends are the deck's, so that a station maps to the same z on both
                // and the step in the sheer lands exactly on the break wall; the flare
                // above the deck comes from the fuller entry instead of a longer reach.
                new Level { Y = Bulwark, BeamK = 0.985f, SternIn = -0.25f, BowIn = -0.25f, TransomK = 0.78f, Entry = 0.82f },
            };
        }

        /// <summary>Half the beam of one level at a station: her transom rounded into the
        /// counter, the parallel middle body, then the taper into the stem.</summary>
        static float LevelHalf(HarborShipSpec s, Level lv, float z)
        {
            float hb = s.Beam * 0.5f * lv.BeamK;
            float zs = s.SternZ + lv.SternIn, zb = s.BowZ - lv.BowIn;
            if (z <= zs) return hb * lv.TransomK;
            float round = Mathf.Min(s.Beam * lv.AftRound, (zb - zs) * 0.35f);
            if (z < zs + round)
                return hb * Mathf.Lerp(lv.TransomK, 1f, Mathf.Sqrt((z - zs) / round));
            float entry = Mathf.Min(s.BowLength * lv.EntryK, (zb - zs) * 0.55f);
            float z0 = zb - entry;
            if (z <= z0) return hb;
            return hb * Mathf.Pow(Mathf.Clamp01(1f - (z - z0) / entry), lv.Entry);
        }

        // The lines of the ship being built, worked out once: the fittings and the deck
        // plates all ask the shell where it is, and the bake is one ship at a time.
        static HarborShipSpec _linesFor;
        static List<Level> _lines;

        static List<Level> LinesOf(HarborShipSpec s)
        {
            if (!ReferenceEquals(_linesFor, s)) { _linesFor = s; _lines = Lines(s); }
            return _lines;
        }

        /// <summary>Half the deck's width at a station, read off the shell's own deck
        /// line - what the fittings are set by, so nothing stands out over the water
        /// where the hull has already begun to taper.</summary>
        static float DeckHalf(HarborShipSpec s, float z)
        {
            var levels = LinesOf(s);
            return LevelHalf(s, levels[levels.Count - 2], z);
        }

        /// <summary>And half her width at any height - what a deck laid above the deck
        /// line (the forecastle) has to be cut to if it is to meet the plating.</summary>
        static float ShellHalf(HarborShipSpec s, float y, float z)
        {
            SurfacePoint(s, LinesOf(s), y, z, 1, out var pos, out _);
            return pos.x;
        }

        /// <summary>Where the sections stand, as fractions of each level's own length:
        /// cosine-spaced so the ends - where the curvature is - get the sections, with a
        /// pair a hair apart at the forecastle break so the step in the sheer is sharp.</summary>
        static float[] Fractions(HarborShipSpec s)
        {
            var list = new List<float>();
            for (int i = 0; i <= Stations; i++) list.Add(0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / Stations));
            if (s.ForecastleRise > 0.1f)
            {
                float f = Mathf.InverseLerp(s.SternZ, s.BowZ, s.ForecastleZ);
                list.Add(f - 0.0035f);
                list.Add(f + 0.0035f);
            }
            list.Sort();
            return list.ToArray();
        }

        static Vector3[] SideOf(HarborShipSpec s, Level lv, float[] fr, int side)
        {
            var pts = new Vector3[fr.Length];
            float zs = s.SternZ + lv.SternIn, zb = s.BowZ - lv.BowIn;
            for (int i = 0; i < fr.Length; i++)
            {
                float z = Mathf.Lerp(zs, zb, fr[i]);
                pts[i] = new Vector3(side * LevelHalf(s, lv, z), lv.Y(z), z);
            }
            return pts;
        }

        /// <summary>One level as a closed loop: up the starboard side to the stem, back
        /// down the port side, the last leg closing across the transom.</summary>
        static Vector3[] Loop(Vector3[] star, Vector3[] port)
        {
            int n = star.Length;
            var loop = new Vector3[2 * n - 1];
            for (int i = 0; i < n; i++) loop[i] = star[i];
            for (int i = n - 2; i >= 0; i--) loop[2 * n - 2 - i] = port[i];
            return loop;
        }

        /// <summary>The outward horizontal normal at every point of a loop, off its own
        /// neighbours - good at the stem too, where a side normal has nothing to say.</summary>
        static Vector3[] Normals(Vector3[] loop, Vector3 centre)
        {
            var normals = new Vector3[loop.Length];
            for (int i = 0; i < loop.Length; i++)
            {
                var prev = loop[(i - 1 + loop.Length) % loop.Length];
                var next = loop[(i + 1) % loop.Length];
                var run = next - prev; run.y = 0f;
                var n = new Vector3(run.z, 0f, -run.x);
                var away = loop[i] - centre; away.y = 0f;
                if (Vector3.Dot(n, away) < 0f) n = -n;
                normals[i] = n.sqrMagnitude > 1e-6f ? n.normalized : away.normalized;
            }
            return normals;
        }

        /// <summary>The skin between two loops, wound to face outward - or inward, for
        /// the side of the bulwark the deck sees. A gap in z leaves that stretch open,
        /// which is how the working deck gets a railing instead of a bulwark.</summary>
        static void Skin(Facets f, Vector3[] lower, Vector3[] upper, Vector3 centre, float vScale,
                         float facing = 1f, float gapZ0 = 0f, float gapZ1 = 0f)
        {
            float u = 0f;
            for (int i = 0; i < lower.Length; i++)
            {
                int j = (i + 1) % lower.Length;
                var a = lower[i]; var b = lower[j]; var c = upper[j]; var d = upper[i];
                float run = (b - a).magnitude;
                var mid = (a + b + c + d) * 0.25f;
                var outward = (mid - centre) * facing;
                bool open = gapZ1 > gapZ0 && mid.z > gapZ0 && mid.z < gapZ1;
                if (!open && ((b - a).sqrMagnitude > 1e-6f || (c - d).sqrMagnitude > 1e-6f))
                    f.Quad(a, b, c, d, outward.normalized, u * 0.16f, (u + run) * 0.16f, 0f, vScale);
                u += run;
            }
        }

        /// <summary>A loop pulled in along its own normals - the inboard face of the
        /// bulwark, and the inboard edge of the capping rail. Near the stem, where the
        /// loop closes to a point, the pull is cut back to what is left of the half
        /// beam there: offsetting a sharp nose by its full width folds it inside out.</summary>
        static Vector3[] Inset(Vector3[] loop, Vector3[] normals, float d)
        {
            var inset = new Vector3[loop.Length];
            for (int i = 0; i < loop.Length; i++)
                inset[i] = loop[i] - normals[i] * Mathf.Min(d, Mathf.Abs(loop[i].x) * 0.8f + 0.04f);
            return inset;
        }

        // ------------------------------------------------------------ the shell

        static Rings Hull(Transform t, HarborShipSpec s, Paints p)
        {
            var levels = LinesOf(s);
            var fr = Fractions(s);
            var centre = new Vector3(0f, s.DeckY * 0.45f, 0f);

            var loops = new Vector3[levels.Count][];
            Vector3[] deckStar = null, deckPort = null;
            for (int i = 0; i < levels.Count; i++)
            {
                var star = SideOf(s, levels[i], fr, 1);
                var port = SideOf(s, levels[i], fr, -1);
                loops[i] = Loop(star, port);
                if (i == levels.Count - 2) { deckStar = star; deckPort = port; }
            }

            // the skin, band by band: antifoul to the waterline, the boot-top, the
            // topsides with the strake through them, and the bulwark above the deck
            var skins = new[]
            {
                p.HullLower, p.HullLower, p.Boot, p.HullUpper, p.Strake, p.HullUpper, p.HullUpper,
            };
            for (int i = 0; i + 1 < levels.Count; i++)
            {
                var facets = new Facets();
                bool bulwark = i == levels.Count - 2;
                Skin(facets, loops[i], loops[i + 1], centre,
                     Mathf.Max(0.12f, (levels[i + 1].Y(0f) - levels[i].Y(0f)) * 0.16f),
                     1f, bulwark ? s.RailZ0 : 0f, bulwark ? s.RailZ1 : 0f);
                facets.Emit(t, "skin", skins[Mathf.Min(i, skins.Length - 1)]);
            }

            // her bottom, so nothing sees through her from below
            var floor = new List<Vector2>();
            foreach (var v in loops[0]) floor.Add(new Vector2(v.x, v.z));
            Plate(t, "bottom", floor, levels[0].Y(0f), p.HullLower, up: false);

            // the bulwark from the deck's side: the shell is one skin, and a man on
            // deck would look straight through it, so its inboard face is lofted too
            var deckLoop = loops[levels.Count - 2];
            var top = loops[levels.Count - 1];
            var normals = Normals(top, centre);
            var innerDeck = Inset(deckLoop, Normals(deckLoop, centre), 0.2f);
            var innerTop = Inset(top, normals, 0.2f);
            var inboard = new Facets();
            Skin(inboard, innerDeck, innerTop, centre, 0.24f, -1f, s.RailZ0, s.RailZ1);
            inboard.Emit(t, "bulwark-inboard", p.HullUpper);

            // the capping rail: a white ribbon laid along the top of the bulwark, which
            // also closes the plating's open edge
            var rail = new Facets();
            var inner = Inset(top, normals, 0.44f);
            for (int i = 0; i < top.Length; i++)
            {
                int j = (i + 1) % top.Length;
                var mid = (top[i] + top[j]) * 0.5f;
                if (mid.z > s.RailZ0 && mid.z < s.RailZ1) continue;
                rail.Quad(top[i], top[j], inner[j], inner[i], Vector3.up);
            }
            rail.Emit(t, "caprail", p.Trim);

            OpenRail(t, s, p, deckLoop, top);

            Frames(t, s, p, levels);
            HullMarks(t, s, p, levels);
            Streaks(t, s, p, levels);
            return new Rings { DeckStar = deckStar, DeckPort = deckPort, Break = BreakStation(s, deckStar) };
        }

        /// <summary>The first station forward of the forecastle break - where the deck
        /// is cut in two and the break wall stands, so plating and wall meet exactly.</summary>
        static int BreakStation(HarborShipSpec s, Vector3[] deckStar)
        {
            for (int i = 0; i < deckStar.Length; i++)
                if (deckStar[i].z > s.ForecastleZ) return i;
            return deckStar.Length - 1;
        }

        /// <summary>Where the bulwark gives way, an open railing on the deck edge, and a
        /// stanchion at either end of it to close the plating off. It is not only how a
        /// working deck is fenced - it is what lets the hands on it be seen at all, since
        /// a man behind a metre and a half of bulwark is a cap and nothing else.</summary>
        static void OpenRail(Transform t, HarborShipSpec s, Paints p, Vector3[] deck, Vector3[] top)
        {
            if (s.RailZ1 <= s.RailZ0) return;
            var centre = new Vector3(0f, s.DeckY, 0f);
            var normals = Normals(deck, centre);
            var line = Inset(deck, normals, 0.16f);
            for (int i = 0; i < deck.Length; i++)
            {
                int j = (i + 1) % deck.Length;
                int prev = (i - 1 + deck.Length) % deck.Length;
                var mid = (deck[i] + deck[j]) * 0.5f;
                var back = (deck[prev] + deck[i]) * 0.5f;
                bool open = Open(s, mid.z), wasOpen = Open(s, back.z);
                // and a gate in it where the gangway comes aboard
                bool gate = Gate(s, mid.z), wasGate = Gate(s, back.z);
                if (open && !gate) RailRun(t, line[i], line[j], p.Trim);
                if (open != wasOpen)
                    Post(t, new Vector3(deck[i].x, s.DeckY, deck[i].z), 0.42f, top[i].y - s.DeckY, p.HullUpper);
                else if (open && gate != wasGate)
                    Post(t, line[i], 0.26f, 1.05f, p.Trim);
            }
        }

        static bool Open(HarborShipSpec s, float z) => z > s.RailZ0 && z < s.RailZ1;
        static bool Gate(HarborShipSpec s, float z) => Mathf.Abs(z - s.GangwayZ) < 1.7f;

        /// <summary>Frames down the topsides: a shade of the strake standing a hair proud
        /// of the plating, turned to lie flat on the curve.</summary>
        static void Frames(Transform t, HarborShipSpec s, Paints p, List<Level> levels)
        {
            float z0 = s.SternZ + s.Beam * 0.35f, z1 = s.ForecastleZ - 1f;
            int n = Mathf.Max(2, Mathf.RoundToInt((z1 - z0) / 6.5f));
            for (int k = 1; k < n; k++)
            {
                float z = Mathf.Lerp(z0, z1, k / (float)n);
                for (int side = -1; side <= 1; side += 2)
                {
                    SurfacePoint(s, levels, s.DeckY * 0.75f, z, side, out var at, out var outward);
                    float yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;
                    var foot = new Vector3(at.x, 1.05f, at.z) + outward * 0.04f;
                    Block(t, foot, new Vector3(0.36f, s.DeckY - 1.1f, 0.22f), p.Strake, yaw);
                }
            }
        }

        /// <summary>Rust weeping down her side: short marks under the sheer, a long one
        /// out of each hawse pipe. It is the cheapest thing on the ship and the one that
        /// tells a hull that works for a living from a model of one. Scattered off the
        /// golden ratio rather than a random, so two bakes come out the same.</summary>
        static void Streaks(Transform t, HarborShipSpec s, Paints p, List<Level> levels)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(s.Length / 8f), 4, 12);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k < n; k++)
                {
                    float f = (k * 0.618034f + (side > 0 ? 0.317f : 0f)) % 1f;
                    float z = Mathf.Lerp(s.SternZ + s.Beam * 0.4f, s.ForecastleZ - 1.5f, f);
                    float len = s.DeckY * (0.24f + ((k * 7) % 5) * 0.06f);
                    SurfacePoint(s, levels, s.DeckY - 0.45f - len * 0.5f, z, side, out var at, out var face);
                    Mark(t, "rust", at + face * 0.1f, face, 0.26f, len, p.Rust);
                }
                // the long weep from the hawse, which every ship has
                SurfacePoint(s, levels, s.DeckY * 0.28f, s.BowZ - s.BowLength * 0.34f, side, out var bow, out var bowFace);
                Mark(t, "rust", bow + bowFace * 0.1f, bowFace, 0.44f, s.DeckY * 0.5f, p.Rust);
            }
        }

        // ------------------------------------------------------------ her markings

        /// <summary>Where a point of the shell is, and which way it faces: the level pair
        /// bracketing that height, read at that station and a little either side of it.</summary>
        static void SurfacePoint(HarborShipSpec s, List<Level> levels, float y, float z, int side,
                                 out Vector3 pos, out Vector3 outward)
        {
            int hi = levels.Count - 1;
            for (int i = 1; i < levels.Count; i++)
                if (levels[i].Y(z) >= y) { hi = i; break; }
            var lo = levels[Mathf.Max(0, hi - 1)];
            float y0 = lo.Y(z), y1 = levels[hi].Y(z);
            float k = Mathf.Abs(y1 - y0) < 1e-3f ? 0f : Mathf.Clamp01((y - y0) / (y1 - y0));

            float Half(float at) => Mathf.Lerp(LevelHalf(s, lo, at), LevelHalf(s, levels[hi], at), k);
            const float d = 0.4f;
            var tangent = new Vector3(side * (Half(z + d) - Half(z - d)), 0f, 2f * d).normalized;
            outward = new Vector3(tangent.z, 0f, -tangent.x) * side;
            pos = new Vector3(side * Half(z), y, z);
        }

        /// <summary>The hawse pipes with their anchors stowed in them, the draft marks at
        /// both ends, and her name on the bows and across the transom - flat work laid on
        /// the curve, each piece taking its own point and facing off the shell.</summary>
        static void HullMarks(Transform t, HarborShipSpec s, Paints p, List<Level> levels)
        {
            float free = s.DeckY;
            float nameY = free - 0.95f, nameH = Mathf.Min(0.6f, free * 0.16f);
            float hawse = Mathf.Min(1.5f, free * 0.42f), hawseY = free * 0.55f;
            float step = Mathf.Min(0.5f, free * 0.16f);
            int marks = Mathf.Clamp(Mathf.FloorToInt((hawseY - 0.9f) / step), 2, 6);

            for (int side = -1; side <= 1; side += 2)
            {
                SurfacePoint(s, levels, hawseY, s.BowZ - s.BowLength * 0.34f, side, out var at, out var face);
                Mark(t, "hawse", at + face * 0.12f, face, hawse, hawse, p.Steel);
                Mark(t, "anchor", at + face * 0.2f + Vector3.down * (hawse * 0.1f), face, hawse * 0.7f, hawse * 0.9f, p.Strake);
                for (int k = 0; k < marks; k++)
                {
                    SurfacePoint(s, levels, 0.95f + k * step, s.BowZ - s.BowLength * 0.86f, side, out var m, out var mf);
                    Mark(t, "draft", m + mf * 0.12f, mf, 0.34f, 0.2f, p.Trim);
                    SurfacePoint(s, levels, 0.95f + k * step, s.SternZ + s.Beam * 0.2f, side, out var a, out var af);
                    Mark(t, "draft", a + af * 0.12f, af, 0.34f, 0.2f, p.Trim);
                }
                NameOnBow(t, s, levels, side, nameY, nameH, p.Trim);
            }
            NameOnTransom(t, s, levels, nameY, nameH, p.Trim);
        }

        static readonly float[] LetterWidths = { 0.42f, 0.3f, 0.38f, 0.24f, 0.4f, 0.34f };

        /// <summary>Her name up the bow, block by block along the curve - lettering the
        /// kit has no texture for, but which reads as a name at any distance the demo is
        /// played from.</summary>
        static void NameOnBow(Transform t, HarborShipSpec s, List<Level> levels, int side, float y, float height, Material mat)
        {
            float z = s.BowZ - s.BowLength * 0.72f;
            foreach (float w in LetterWidths)
            {
                SurfacePoint(s, levels, y, z, side, out var at, out var face);
                Mark(t, "name", at + face * 0.14f, face, w, height, mat);
                z += (w + 0.24f) * 0.85f;         // along the bow, which is running away from us
            }
        }

        /// <summary>And across the transom, where the port of registry goes.</summary>
        static void NameOnTransom(Transform t, HarborShipSpec s, List<Level> levels, float y, float height, Material mat)
        {
            var face = new Vector3(0f, 0f, -1f);
            float total = 0f;
            foreach (float w in LetterWidths) total += w + 0.24f;
            float x = -total * 0.5f;
            float z = s.SternZ - 0.08f;
            foreach (float w in LetterWidths)
            {
                Mark(t, "name", new Vector3(x + w * 0.5f, y, z), face, w, height, mat);
                x += w + 0.24f;
            }
        }
    }
}
