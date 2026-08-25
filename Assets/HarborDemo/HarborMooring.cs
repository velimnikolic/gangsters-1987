using UnityEngine;

namespace HarborDemo
{
    // What actually holds a ship against a quay, and the one thing the port had none
    // of: the lines. A freighter lying alongside with nothing between her and the
    // bollards reads as a model parked next to a wall; four lines - a head line and a
    // stern line leading away from her ends, and a spring at each end leading back
    // along her - and she is made fast.
    //
    // Each line is the generic pack's rope (three metres of it standing up its own
    // +Y, four centimetres thick) stretched into three lengths through a shallow sag,
    // so the line hangs rather than pointing. The lot are children of the ship's MODEL
    // - the transform HarborBob heaves and rolls - so a line stays on its fairlead
    // while she works; the shore end wanders by the heave, which is five centimetres.
    //
    // Made fast when she does (HarborShipping.MakeFast) and let go when she casts off
    // (CastOff), because a ship that sails with her lines still on her drags four ropes
    // over the horizon.
    public static class HarborMooring
    {
        /// <summary>Sag in the middle of a line, as a share of its span. A mooring line
        /// is set up hard, so this is small - enough to read as rope, not as cable.</summary>
        const float Sag = 0.055f;
        /// <summary>How thick the line is drawn, as a multiple of the rope piece's own
        /// four centimetres.</summary>
        const float Thickness = 2.6f;
        /// <summary>Lengths a line is drawn in: three is enough for the eye to read a
        /// curve and cheap enough to hang four off every ship in the port.</summary>
        const int Lengths = 3;

        /// <summary>The four lines for a ship lying at a berth, as children of her model.
        /// <paramref name="bollardY"/> and <paramref name="bollardZ"/> are the coping's
        /// own - where BuildQuay stood the bollards - and <paramref name="quayHalf"/>
        /// keeps a line from being made fast to a bollard that is not there.</summary>
        public static GameObject Make(HarborShip ship, HarborShipSpec spec, Transform live,
                                      float berthX, float quayHalf, float bollardY, float bollardZ)
        {
            var rope = HarborKit.TryLoad(HarborKit.Rope1);
            if (rope == null || ship == null || ship.Model == null || spec == null || live == null) return null;

            var root = new GameObject("Mooring");
            root.transform.SetParent(ship.Model, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            // her quay side is her own -X: the shipping lays her heading east, which
            // turns her local +Z onto the coast and her -X onto the wall
            float side = -(spec.Beam * 0.5f - 0.4f);
            var bow = new Vector3(side, spec.ForecastleY + 0.4f, spec.BowZ - 3.5f);
            var stern = new Vector3(side, spec.DeckY + 0.9f, spec.SternZ + 3.5f);

            // where each line is made fast ashore, along the coping either side of her
            float bowX = berthX + spec.BowZ, sternX = berthX + spec.SternZ;
            Line(rope, root.transform, live, ship, bow, Bollard(bowX + 14f, quayHalf), bollardY, bollardZ, "HeadLine");
            Line(rope, root.transform, live, ship, bow, Bollard(bowX - 26f, quayHalf), bollardY, bollardZ, "ForeSpring");
            Line(rope, root.transform, live, ship, stern, Bollard(sternX + 26f, quayHalf), bollardY, bollardZ, "AftSpring");
            Line(rope, root.transform, live, ship, stern, Bollard(sternX - 14f, quayHalf), bollardY, bollardZ, "SternLine");
            return root;
        }

        /// <summary>The nearest bollard the coping actually carries: BuildQuay stands
        /// them every nine metres from four and a half in off the west end, and a line
        /// leading to open air past the end of the wall is worse than no line.</summary>
        static float Bollard(float want, float quayHalf)
        {
            float first = -quayHalf + 4.5f;
            // the LAST one the coping actually carries: BuildQuay's loop stops at
            // x < quayHalf - 4, so clamping to quayHalf - 4.5 lands off the grid and
            // makes a line fast to bare stone at either end of the wall
            int steps = Mathf.Max(0, Mathf.FloorToInt((quayHalf - 4f - 0.001f - first) / 9f));
            float at = first + Mathf.Round((want - first) / 9f) * 9f;
            return Mathf.Clamp(at, first, first + steps * 9f);
        }

        /// <summary>One line from a point on her (her own frame) to a point on the coping
        /// (the port's), in three sagging lengths.</summary>
        static void Line(GameObject rope, Transform parent, Transform live, HarborShip ship,
                         Vector3 shipLocal, float bollardX, float bollardY, float bollardZ, string name)
        {
            // both ends into the world the ship stands in, so the piece may be laid
            // whatever frame the port itself ended up on
            var a = ship.Model.TransformPoint(shipLocal);
            var b = live.TransformPoint(new Vector3(bollardX, bollardY, bollardZ));
            float span = Vector3.Distance(a, b);
            float sag = span * Sag;
            for (int i = 0; i < Lengths; i++)
            {
                var p0 = Hang(a, b, i / (float)Lengths, sag);
                var p1 = Hang(a, b, (i + 1) / (float)Lengths, sag);
                HarborKit.Span(rope, p0, p1, Thickness, parent, name);
            }
        }

        /// <summary>A point along a line that hangs: the straight run with a parabola
        /// taken out of it, deepest in the middle.</summary>
        static Vector3 Hang(Vector3 a, Vector3 b, float t, float sag)
            => Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
    }
}
