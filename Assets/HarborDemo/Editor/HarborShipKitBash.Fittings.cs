using UnityEngine;

namespace HarborDemo.EditorTools
{
    /// <summary>
    /// What stands on a hull once the plating is up: the working deck (pipe runs,
    /// bitts, vents, winches), the deckhouse with its wings, funnel, mast, boats and
    /// outside stairs, and the cargo gear - deck cranes on the newer ships, kingposts
    /// and derricks on the older. All of it out of the same stretched pillar and the
    /// pack's railing, and all of it in port-and-starboard pairs so the bake keeps the
    /// pivot on the centreline.
    ///
    /// The gear is stowed steep on purpose. HarborCargo flies a box seven metres over
    /// its slot and carries it across the beam, so a jib cocked at the angle a crane
    /// really works at would stand in the flight path: topped right up, the way a jib
    /// is stowed in port anyway, it is already above the lift wherever it crosses a
    /// stack - and the derrick booms are topped up steep for the same reason.
    /// </summary>
    public static partial class HarborShipKitBash
    {
        const float JibRake = 82f;          // degrees off the horizontal: topped up, and clear of the lift
        const float BitInset = 1.2f;        // how far in from the bulwark the bitts stand

        // ------------------------------------------------------------ the deck

        /// <summary>Where a fitting may stand on the main deck: the clear bands between
        /// the house, the hatches and the forecastle break, minus the reach of the cargo
        /// gear, two spots either side of the centreline in each. A wide ship has room
        /// for a dozen; the coaster, whose hatch takes nearly her whole deck, has none -
        /// and her clutter goes on the forecastle and the poop instead.</summary>
        static System.Collections.Generic.List<Vector3> DeckStands(HarborShipSpec s)
        {
            var stands = new System.Collections.Generic.List<Vector3>();
            float hb = s.Beam * 0.5f;
            var xs = new[] { hb - 2.3f, -(hb - 2.3f), hb - 4.1f, -(hb - 4.1f) };

            var hatches = new System.Collections.Generic.List<Rect>(s.Hatches);
            hatches.Sort((a, b) => a.yMin.CompareTo(b.yMin));
            var bands = new System.Collections.Generic.List<Vector2>();
            float z = s.HouseZ1 + 0.6f;
            foreach (var h in hatches)
            {
                if (h.yMin - 0.6f - z > 2.2f) bands.Add(new Vector2(z, h.yMin - 0.6f));
                z = Mathf.Max(z, h.yMax + 0.6f);
            }
            if (s.ForecastleZ - 0.6f - z > 2.2f) bands.Add(new Vector2(z, s.ForecastleZ - 0.6f));

            foreach (var band in bands)
                for (float at = band.x + 1f; at <= band.y - 1f; at += 1.4f)
                    foreach (float x in xs)
                        if (Mathf.Abs(x) > 0.8f && Mathf.Abs(x) < hb - 1.2f && !GearStands(s, x, at))
                            stands.Add(new Vector3(x, s.DeckY, at));
            return stands;
        }

        /// <summary>Whether the cargo gear has that spot: a crane's pedestal and its
        /// machinery sit on the centreline, a kingpost pair stands wide of it with the
        /// winch house between the two posts.</summary>
        static bool GearStands(HarborShipSpec s, float x, float z)
        {
            foreach (var c in s.Cranes)
                if (Mathf.Abs(z - c.Z) < 2.6f && Mathf.Abs(x) < 2.6f) return true;
            foreach (float k in s.Kingposts)
                if (Mathf.Abs(z - k) < 2.2f && (Mathf.Abs(x) < 2.2f || Mathf.Abs(Mathf.Abs(x) - 3.4f) < 1.1f)) return true;
            return s.ForeMast && Mathf.Abs(z - (s.ForecastleZ - 2.5f)) < 2.6f && Mathf.Abs(x) < 2.2f;
        }

        /// <summary>The working deck: a pipe run on its stools down each side, mooring
        /// bitts at the stations fore and aft, vents and a cable reel wherever the deck
        /// has room for them, the windlass on the forecastle with her cables led out to
        /// the hawse pipes, and the mooring winch aft.</summary>
        static void DeckFittings(Transform t, HarborShipSpec s, Paints p, System.Collections.Generic.List<Vector3> stands)
        {
            float hb = s.Beam * 0.5f;
            float pipeX = hb - 0.62f;
            float z0 = s.HouseZ1 + 1.2f, z1 = s.ForecastleZ - 1f;
            bool forecastle = s.ForecastleRise > 0.1f;

            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * pipeX;
                Bar(t, new Vector3(x, s.DeckY + 0.6f, z0), new Vector3(x, s.DeckY + 0.6f, z1), 0.3f, 0.3f, p.Steel);
                int stools = Mathf.Max(2, Mathf.RoundToInt((z1 - z0) / 6f));
                for (int k = 0; k <= stools; k++)
                    Block(t, new Vector3(x, s.DeckY, Mathf.Lerp(z0, z1, k / (float)stools)), new Vector3(0.24f, 0.6f, 0.24f), p.Steel);
            }

            // the mooring stations: bitts in pairs where the deck is open right across.
            // Forward that deck is already tapering, so the station is set by the width
            // the hull actually has there rather than by the beam.
            Bitts(t, p, Mathf.Min(hb - BitInset, DeckHalf(s, s.SternZ + 1.5f) - 1f), s.DeckY, s.SternZ + 1.5f);
            Bitts(t, p, hb - BitInset, s.DeckY, s.GangwayZ);
            if (forecastle)
            {
                float bz = s.ForecastleZ + s.BowLength * 0.2f;
                Bitts(t, p, Mathf.Max(1.2f, DeckHalf(s, bz) - 1.1f), s.ForecastleY, bz);
            }

            // vents, mushroom heads and a cable reel at the first free stands
            for (int k = 0; k < 2 && k < stands.Count; k++)
                Fit(t, AirVent, stands[k], stands[k].x < 0f ? -90f : 90f, 1.5f, p.House);
            for (int k = 2; k < 4 && k < stands.Count; k++)
            {
                Post(t, stands[k], 0.5f, 1.25f, p.Steel);
                Block(t, stands[k] + Vector3.up * 1.25f, new Vector3(0.9f, 0.3f, 0.9f), p.House);
            }
            if (stands.Count > 4) Fit(t, Wirespool, stands[4], stands[4].x < 0f ? -90f : 90f, 1.1f);

            // the winch aft, the windlass forward, and the cables run out to the hawse
            Winch(t, p, new Vector3(0f, s.DeckY, s.SternZ + 1.8f), 3f);
            if (forecastle)
            {
                float wz = s.ForecastleZ + s.BowLength * 0.28f;
                Winch(t, p, new Vector3(0f, s.ForecastleY, wz), Mathf.Min(3.2f, (DeckHalf(s, wz) - 0.8f) * 2f));
                float cz = s.ForecastleZ + s.BowLength * 0.72f;
                float cx = Mathf.Min(2.2f, DeckHalf(s, cz) * 0.6f);
                for (int side = -1; side <= 1; side += 2)
                {
                    var from = new Vector3(side * 1.1f, s.ForecastleY + 0.15f, wz);
                    var to = new Vector3(side * cx, s.ForecastleY + 0.15f, cz);
                    Bar(t, from, to, 0.22f, 0.22f, p.Steel);
                    Block(t, to, new Vector3(0.5f, 0.45f, 0.6f), p.Steel);      // the chain stopper
                }
            }
        }

        /// <summary>A pair of bitts each side - what a mooring line is turned up on.</summary>
        static void Bitts(Transform t, Paints p, float x, float y, float z)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Block(t, new Vector3(side * x, y, z), new Vector3(1.2f, 0.25f, 0.9f), p.Steel);
                for (int k = -1; k <= 1; k += 2)
                    Post(t, new Vector3(side * x + k * 0.32f, y + 0.25f, z), 0.3f, 0.8f, p.Steel);
            }
        }

        /// <summary>A winch: a bed, two end frames, the drum across them, and its motor.</summary>
        static void Winch(Transform t, Paints p, Vector3 at, float width)
        {
            Block(t, at, new Vector3(width, 0.35f, 1.7f), p.Steel);
            for (int side = -1; side <= 1; side += 2)
                Block(t, at + new Vector3(side * (width * 0.5f - 0.25f), 0.35f, 0f), new Vector3(0.4f, 1f, 1.3f), p.Steel);
            Bar(t, at + new Vector3(-width * 0.5f + 0.3f, 0.95f, 0f), at + new Vector3(width * 0.5f - 0.3f, 0.95f, 0f), 1f, 1f, p.Mast);
            Block(t, at + new Vector3(0f, 0.35f, -1f), new Vector3(1.1f, 0.8f, 0.7f), p.Mast);
        }

        // ------------------------------------------------------------ the house

        /// <summary>The deckhouse aft: window walls forward and to the sides, plain aft
        /// with a door onto the poop, a floor to every storey and a parapet round the
        /// roof - then the wings, the stairs, the boats, the funnel and the mast.</summary>
        static void Deckhouse(Transform t, HarborShipSpec s, Paints p)
        {
            float hw = s.HouseWidth * 0.5f;
            float centreZ = (s.HouseZ0 + s.HouseZ1) * 0.5f;
            for (int storey = 0; storey < s.Storeys; storey++)
            {
                float y = s.DeckY + storey * HarborShipSpec.Course;
                bool bridge = storey == s.Storeys - 1;
                Block(t, new Vector3(0f, y, centreZ), new Vector3(s.HouseWidth, HarborShipSpec.Course - 0.12f, s.HouseLength), p.House);
                Block(t, new Vector3(0f, y + HarborShipSpec.Course - 0.12f, centreZ), new Vector3(s.HouseWidth + 0.45f, 0.12f, s.HouseLength + 0.45f), p.Trim);
                // A wheelhouse has a continuous lookout band, accommodation has small windows.
                int windows = bridge ? 7 : 4;
                for (int k = 0; k < windows; k++)
                {
                    float x = Mathf.Lerp(-hw + 0.9f, hw - 0.9f, (k + 0.5f) / windows);
                    Mark(t, "Forward bridge glazing", new Vector3(x, y + 1.75f, s.HouseZ1 + 0.015f), Vector3.forward,
                        bridge ? s.HouseWidth / windows - 0.2f : 0.65f, bridge ? 1.12f : 0.7f, p.Glass);
                }
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < 4; k++)
                        Mark(t, "Accommodation glazing", new Vector3(side * (hw + 0.015f), y + 1.75f, s.HouseZ0 + 1.2f + k * (s.HouseLength - 2.4f) / 3f),
                            Vector3.right * side, bridge ? 1.25f : 0.65f, bridge ? 1.12f : 0.7f, p.Glass);
                Block(t, new Vector3(0f, y + 2.5f, s.HouseZ1 + 0.35f), new Vector3(s.HouseWidth + 0.5f, 0.12f, 0.7f), p.Trim);
            }

            // the monkey island: railed rather than parapeted, so the mast and funnel
            // stand on an open deck a man can be seen on
            float roof = s.HouseRoofY;
            var r0 = new Vector3(-hw + 0.2f, roof, s.HouseZ1 - 0.2f); var r1 = new Vector3(hw - 0.2f, roof, s.HouseZ1 - 0.2f);
            var r2 = new Vector3(hw - 0.2f, roof, s.HouseZ0 + 0.2f); var r3 = new Vector3(-hw + 0.2f, roof, s.HouseZ0 + 0.2f);
            RailRun(t, r0, r1, p.Trim);
            RailRun(t, r1, r2, p.Trim);
            RailRun(t, r2, r3, p.Trim);
            RailRun(t, r3, r0, p.Trim);
            for (int side = -1; side <= 1; side += 2)
                Post(t, new Vector3(side * (hw - 0.2f), roof, s.HouseZ1 - 0.2f), 0.28f, 1.05f, p.Trim);

            BridgeWings(t, s, p);
            OutsideStairs(t, s, p);
            Boats(t, s, p);
            Funnel(t, s, p);
            RadarMast(t, s, p);
        }

        /// <summary>The bridge wings: a deck out past the ship's side on two struts,
        /// railed, with the nav lights at their ends - red to port, green to starboard.
        /// The captain is put out on one of these at Play (HarborShipSpec.BridgeWing).</summary>
        static void BridgeWings(Transform t, HarborShipSpec s, Paints p)
        {
            if (s.BridgeWingSpan < 0.2f) return;
            float hw = s.HouseWidth * 0.5f, y = s.BridgeY;
            float z0 = s.HouseZ1 - 3.4f, z1 = s.HouseZ1;
            for (int side = -1; side <= 1; side += 2)
            {
                float xIn = side * hw, xOut = side * (hw + s.BridgeWingSpan);
                // a slab, not a plate: the wing is looked up at from the quay
                Block(t, new Vector3((xIn + xOut) * 0.5f, y - 0.24f, (z0 + z1) * 0.5f),
                      new Vector3(s.BridgeWingSpan, 0.24f, z1 - z0), p.House);
                RailRun(t, new Vector3(xOut, y, z0), new Vector3(xOut, y, z1), p.Trim);
                RailRun(t, new Vector3(xIn, y, z0), new Vector3(xOut, y, z0), p.Trim);
                RailRun(t, new Vector3(xIn, y, z1), new Vector3(xOut, y, z1), p.Trim);
                Bar(t, new Vector3(xOut, y - 0.06f, z0 + 0.5f), new Vector3(xIn, y - 2.2f, z0 + 0.5f), 0.22f, 0.22f, p.House);
                Bar(t, new Vector3(xOut, y - 0.06f, z1 - 0.5f), new Vector3(xIn, y - 2.2f, z1 - 0.5f), 0.22f, 0.22f, p.House);
                Block(t, new Vector3(xOut - side * 0.3f, y + 0.95f, z1 - 1.1f), new Vector3(0.42f, 0.55f, 0.42f),
                      side < 0 ? p.NavRed : p.NavGreen);
            }
        }

        /// <summary>The stairs up the aft face, a flight and a landing to a storey, one
        /// each side - how the men get from the poop to the bridge.</summary>
        static void OutsideStairs(Transform t, HarborShipSpec s, Paints p)
        {
            float hw = s.HouseWidth * 0.5f;
            float face = s.HouseZ0 - 0.2f;
            for (int storey = 0; storey < s.Storeys; storey++)
            {
                float y0 = s.DeckY + storey * HarborShipSpec.Course;
                float y1 = y0 + HarborShipSpec.Course;
                for (int side = -1; side <= 1; side += 2)
                {
                    // the flight and its landing have to stay on the poop, which is
                    // only the three metres between the house and the transom
                    float x = side * (hw - 1.6f);
                    var foot = new Vector3(x, y0, face - 2.2f);
                    var head = new Vector3(x, y1, face - 0.2f);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var off = new Vector3(k * 0.5f, 0f, 0f);
                        Bar(t, foot + off, head + off, 0.14f, 0.42f, p.House);
                        Bar(t, foot + off + Vector3.up * 1.05f, head + off + Vector3.up * 1.05f, 0.1f, 0.1f, p.Trim);
                    }
                    for (int k = 0; k < 6; k++)
                    {
                        var step = Vector3.Lerp(foot, head, (k + 0.5f) / 6f);
                        Bar(t, step + new Vector3(-0.5f, 0f, 0f), step + new Vector3(0.5f, 0f, 0f), 0.34f, 0.09f, p.House);
                    }
                    Block(t, new Vector3(x, y1 - 0.2f, face - 1.35f), new Vector3(1.4f, 0.2f, 2.3f), p.House);
                }
            }
        }

        /// <summary>The boats: one a side on the deck below the bridge, sat in a cradle
        /// under davits, the falls hanging off them.</summary>
        static void Boats(Transform t, HarborShipSpec s, Paints p)
        {
            float hw = s.HouseWidth * 0.5f;
            float y = s.BridgeY;
            float z = (s.HouseZ0 + s.HouseZ1) * 0.5f - 1.2f;   // aft of the wing above it
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (hw + 0.9f);
                for (int k = -1; k <= 1; k += 2)
                {
                    var foot = new Vector3(side * (hw - 0.25f), y, z + k * 2.1f);
                    var head = new Vector3(side * (hw + 1.8f), y + 3.1f, z + k * 2.1f);
                    Bar(t, foot, head, 0.24f, 0.24f, p.Mast);
                    Bar(t, head, new Vector3(x, y + 1.6f, z + k * 2.1f), 0.09f, 0.09f, p.Steel);
                }
                Bar(t, new Vector3(x, y + 0.7f, z - 2.5f), new Vector3(x, y + 0.7f, z + 2.5f), 0.6f, 0.25f, p.Steel);
                FitLong(t, Lifeboat, new Vector3(x, y + 0.95f, z), 0f, 5.2f, p.Boat);
            }
        }

        /// <summary>The funnel: a raked casing with the company band round it and the
        /// black top, the exhaust pipes standing out of it, a ladder up the back.</summary>
        static void Funnel(Transform t, HarborShipSpec s, Paints p)
        {
            var fb = s.FunnelBase;
            float h = s.FunnelHeight;
            Block(t, fb, new Vector3(2.7f, h, 3.4f), p.Funnel);
            Block(t, fb + Vector3.up * (h * 0.42f), new Vector3(2.85f, 0.75f, 3.55f), p.Trim);
            Block(t, fb + Vector3.up * (h - 0.8f), new Vector3(2.9f, 0.8f, 3.6f), p.Steel);
            for (int side = -1; side <= 1; side += 2)
                Post(t, fb + new Vector3(side * 0.65f, h, 0f), 0.42f, 1.1f, p.Steel);
            Fit(t, Ladder, fb + new Vector3(0f, 0f, -1.85f), 180f, h - 0.6f, p.Steel);
        }

        /// <summary>The mast on the monkey island: a post with its crosstree and stays,
        /// the scanner turning on top, and a dish either side of it.</summary>
        static void RadarMast(Transform t, HarborShipSpec s, Paints p)
        {
            float roof = s.HouseRoofY;
            float z = s.HouseZ1 - 1.7f;
            Post(t, new Vector3(0f, roof, z), 0.42f, 5.6f, p.Mast);
            Bar(t, new Vector3(-2.4f, roof + 3.6f, z), new Vector3(2.4f, roof + 3.6f, z), 0.24f, 0.24f, p.Mast);
            for (int side = -1; side <= 1; side += 2)
            {
                Bar(t, new Vector3(side * 2.4f, roof + 3.6f, z), new Vector3(0f, roof + 5.3f, z), 0.11f, 0.11f, p.Mast);
                Block(t, new Vector3(side * 2.2f, roof + 3.8f, z), new Vector3(0.34f, 0.45f, 0.34f),
                      side < 0 ? p.NavRed : p.NavGreen);
                Fit(t, SatDish, new Vector3(side * 1.35f, roof + 0.05f, z - 1.4f), side * 20f, 1.3f, p.House);
            }
            Block(t, new Vector3(0f, roof + 5.6f, z), new Vector3(2.7f, 0.3f, 0.55f), p.Trim);
            Block(t, new Vector3(0f, roof + 2.4f, z), new Vector3(0.34f, 0.45f, 0.34f), p.Trim);
        }

        // ------------------------------------------------------------ cargo gear

        static void CargoGear(Transform t, HarborShipSpec s, Paints p)
        {
            foreach (var crane in s.Cranes) DeckCrane(t, s, p, crane);
            foreach (float z in s.Kingposts) KingpostPair(t, s, p, z);
            if (s.ForeMast) ForeMastRig(t, s, p);
        }

        /// <summary>A deck crane: pedestal, machinery house with the cab on the jib
        /// side, the jib raked back off its heel with a stay to hold it, and the fall
        /// hanging short so the hook never dips into the stack.</summary>
        static void DeckCrane(Transform t, HarborShipSpec s, Paints p, HarborShipSpec.DeckCrane c)
        {
            float dir = Mathf.Abs(Mathf.DeltaAngle(c.Yaw, 180f)) < 90f ? -1f : 1f;
            float top = s.DeckY + c.Pedestal;
            Block(t, new Vector3(0f, s.DeckY, c.Z), new Vector3(3.2f, c.Pedestal, 3.2f), p.Mast);
            Block(t, new Vector3(0f, top, c.Z), new Vector3(3.6f, 2.8f, 3.8f), p.Mast);
            Block(t, new Vector3(0f, top + 0.5f, c.Z + dir * 2.2f), new Vector3(1.7f, 1.8f, 1.2f), p.House);
            Mark(t, "cabglass", new Vector3(0f, top + 1.4f, c.Z + dir * 2.82f), new Vector3(0f, 0f, dir), 1.4f, 0.9f, p.Steel);

            float rake = JibRake * Mathf.Deg2Rad;
            var heel = new Vector3(0f, top + 2f, c.Z + dir * 1.5f);
            var tip = heel + new Vector3(0f, c.Reach * Mathf.Sin(rake), dir * c.Reach * Mathf.Cos(rake));
            Bar(t, heel, tip, 0.6f, 0.6f, p.Mast);
            Bar(t, new Vector3(0f, top + 3.2f, c.Z - dir * 0.8f), Vector3.Lerp(heel, tip, 0.6f), 0.16f, 0.16f, p.Steel);
            var hook = tip + Vector3.down * 2.2f;
            Bar(t, tip, hook, 0.09f, 0.09f, p.Steel);
            Block(t, hook, new Vector3(0.55f, 0.8f, 0.55f), p.Steel);
        }

        /// <summary>The older gear: a pair of kingposts with their crosstree, a derrick
        /// boom topped up steep off each - stowed the way they are in port, and clear
        /// of the stack the boxes make - the topping lifts holding them, and the winch
        /// house on the centreline between the posts.</summary>
        static void KingpostPair(Transform t, HarborShipSpec s, Paints p, float z)
        {
            const float x = 3.4f, h = 10.5f;
            float y = s.DeckY;
            for (int side = -1; side <= 1; side += 2)
            {
                Post(t, new Vector3(side * x, y, z), 0.7f, h, p.Mast);
                var heel = new Vector3(side * x, y + 2f, z + 0.5f);
                var tip = new Vector3(side * (x - 0.5f), y + 12.5f, z + 2.2f);
                Bar(t, heel, tip, 0.42f, 0.42f, p.Mast);
                Bar(t, new Vector3(side * x, y + h - 0.7f, z), tip, 0.1f, 0.1f, p.Steel);
                Bar(t, tip, tip + Vector3.down * 1.6f, 0.08f, 0.08f, p.Steel);
                Block(t, tip + Vector3.down * 1.6f, new Vector3(0.45f, 0.6f, 0.45f), p.Steel);
            }
            Bar(t, new Vector3(-x, y + h - 1.5f, z), new Vector3(x, y + h - 1.5f, z), 0.3f, 0.3f, p.Mast);
            Block(t, new Vector3(0f, y + h - 1.4f, z), new Vector3(2f * x, 0.16f, 1.4f), p.Steel);
            Block(t, new Vector3(0f, y, z - 0.6f), new Vector3(3f, 1.1f, 1.3f), p.Steel);
        }

        /// <summary>The coaster's rig: one mast forward of the hatch with a crosstree
        /// and stays, her single derrick over the hold, and the winch ahead of it.</summary>
        static void ForeMastRig(Transform t, HarborShipSpec s, Paints p)
        {
            float mz = s.ForecastleZ - 2.5f;
            float y = s.DeckY;
            Post(t, new Vector3(0f, y, mz), 0.62f, 9.5f, p.Mast);
            Bar(t, new Vector3(-2.6f, y + 7.2f, mz), new Vector3(2.6f, y + 7.2f, mz), 0.24f, 0.24f, p.Mast);
            for (int side = -1; side <= 1; side += 2)
                Bar(t, new Vector3(side * 2.6f, y + 7.2f, mz), new Vector3(0f, y + 9.4f, mz), 0.1f, 0.1f, p.Steel);
            var heel = new Vector3(0f, y + 1.6f, mz - 0.6f);
            var tip = new Vector3(0f, y + 10.5f, mz - 4.2f);   // steep, so it clears her stack
            Bar(t, heel, tip, 0.4f, 0.4f, p.Mast);
            Bar(t, new Vector3(0f, y + 8.8f, mz), tip, 0.09f, 0.09f, p.Steel);
            Bar(t, tip, tip + Vector3.down * 1.5f, 0.08f, 0.08f, p.Steel);
            Block(t, tip + Vector3.down * 1.5f, new Vector3(0.45f, 0.6f, 0.45f), p.Steel);
            Winch(t, p, new Vector3(0f, y, mz + 1.7f), 2.4f);
        }

        // ------------------------------------------------------------ dressing

        /// <summary>The loose gear a working ship is never without: the anchor cable on
        /// the forecastle, ropes, crates and drums at whatever stands the deck has left
        /// over, a couple of drums on the poop, life rings on the house. Nothing stands
        /// on a hatch or in the lane the deckhands walk (HarborShipSpec.DeckWalkX).</summary>
        static void Dressing(Transform t, HarborShipSpec s, Paints p, System.Collections.Generic.List<Vector3> stands)
        {
            float hw = s.HouseWidth * 0.5f;
            float fy = s.ForecastleRise > 0.1f ? s.ForecastleY : s.DeckY;
            float fz = s.ForecastleZ + s.BowLength * 0.5f;

            Prop(t, AnchorChain, new Vector3(0f, fy, fz), 0f);
            Prop(t, Crate, new Vector3(-Mathf.Min(1.6f, DeckHalf(s, fz) - 1f), fy, fz - 1.6f), 25f);
            // the stands the fittings did not want, filled in order: rope, rope, crate,
            // crate, then drums - a wide deck gets the lot, a narrow one what fits
            var loose = new[] { Rope, Rope, Crate, Crate, Barrel, Barrel, Barrel };
            for (int k = 5; k < stands.Count && k - 5 < loose.Length; k++)
                Prop(t, loose[k - 5], stands[k], (k * 37) % 90);
            for (int k = 0; k < 2; k++)
                Prop(t, Barrel, new Vector3(2.1f + k * 0.9f, s.DeckY, s.SternZ + 1f), k * 40f);   // clear of the winch aft
            for (int side = -1; side <= 1; side += 2)
            {
                Prop(t, RescueBuoy, new Vector3(side * (hw + 0.15f), s.DeckY + 1.6f, s.HouseZ1 - 2f), side * 90f);
                Prop(t, RescueBuoy, new Vector3(side * (hw + s.BridgeWingSpan - 0.5f), s.BridgeY + 0.15f, s.HouseZ1 - 2.6f), side * 90f);
            }
        }
    }
}
