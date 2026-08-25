using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // The line where the port meets the sea, and what a working quay wears along it.
    //
    // The wall itself was always there - coping, bollards, lamps - but nothing hung
    // off the front of it and nothing got you down it. A quay wall with a bare face
    // is a retaining wall; a quay wall with fenders on chains, a ladder every so
    // often, a life ring at the lamps and oil trodden into the concrete behind it is
    // a place ships come alongside.
    //
    // None of it is a harbour piece out of a pack: a fender is the palm city's ball
    // buoy hung on the generic pack's chain, a ladder is the gang pack's, and the oil
    // is the gang pack's puddle decals laid on the same paint plane the tyre marks
    // are laid on. Every one of them is measured off its own prefab.
    public partial class HarborDistrict
    {
        Transform _waterlineRoot;

        /// <summary>How far off the wall's face a fender hangs: nearly the whole of the
        /// gap between the coping and a hull lying alongside, which is what a fender is
        /// for.</summary>
        const float FenderZ = -QuayFace - 0.7f;
        /// <summary>The top of a hanging fender, well clear of the water and against the
        /// belting of anything that comes alongside.</summary>
        const float FenderTop = -0.15f;

        void BuildWaterline()
        {
            _waterlineRoot = Root("Harbor Waterline");
            HangFenders();
            LayLadders();
            DressCoping();
            StainTheConcrete();
            SetTheGulls();
        }

        // ------------------------------------------------------------ fenders

        /// <summary>Ball fenders down the face of the wall, each on its own chain off the
        /// coping - halfway between the bollards, so the two rhythms read as one piece of
        /// furniture rather than a row of dots. Doubled up along the berths, because that
        /// is where a hull touches.</summary>
        void HangFenders()
        {
            var ball = HarborKit.TryLoad(HarborKit.BuoyBall);
            var chain = HarborKit.TryLoad(HarborKit.Chain);
            if (ball == null) return;
            float half = QuayHalf;
            for (float x = -half + 9f; x < half - 4f; x += 9f)
            {
                bool atBerth = false;
                for (int i = 0; i < berths; i++) if (Mathf.Abs(x - BerthX(i)) < 34f) atBerth = true;
                // between the berths only every other one is hung
                if (!atBerth && Mathf.Repeat(x - (-half + 9f), 18f) > 1f) continue;
                var top = new Vector3(x, FenderTop, FenderZ);
                HarborKit.Hang(ball, top, HarborKit.Range(_rng, 0f, 360f), _waterlineRoot, "Fender");
                if (chain != null)
                    HarborKit.Span(chain, new Vector3(x, BollardY - 0.05f, -QuayFace + 0.06f), top,
                                   1f, _waterlineRoot, "FenderChain");
            }
        }

        // ------------------------------------------------------------ ladders

        /// <summary>A ladder down the face at every berth's gangway and every sixty metres
        /// between - recessed against the wall with its rungs to the water, its foot in
        /// the sea and its head a hand's breadth proud of the coping, which is how a man
        /// finds one from a boat.</summary>
        void LayLadders()
        {
            var ladder = HarborKit.TryLoad(HarborKit.Ladder);
            if (ladder == null) return;
            var b = HarborKit.PrefabBounds(ladder);
            float module = Mathf.Max(0.5f, b.size.y);
            float foot = WaterY - 0.4f, head = 0.62f;

            var at = new List<float>();
            for (int i = 0; i < berths; i++) at.Add(BerthX(i) - 18f);
            for (float x = -QuayHalf + 22f; x < QuayHalf - 8f; x += 60f) at.Add(x);
            foreach (float x in at)
            {
                if (Mathf.Abs(x) > QuayHalf - 4f) continue;
                // turned about so the rungs face the sea; the piece then hangs off its
                // pivot on the far side, which is what puts it against the wall
                var go = HarborKit.Prop(ladder, new Vector3(x, foot, -QuayFace), 180f, _waterlineRoot, "QuayLadder");
                var s = go.transform.localScale;
                // stretched to reach the coping, never squashed: a piece already longer
                // than the drop is left at its authored length and its foot simply ends
                // deeper in the water than the waterline
                s.y *= Mathf.Max(1f, (head - foot) / module);
                go.transform.localScale = s;
            }
        }

        // ------------------------------------------------------------ the coping

        /// <summary>What lies on the stone itself: a life ring at every lamp, a coil of
        /// rope beside the odd bollard, and a chained post at the head of each ladder so
        /// nobody drives into the hole.</summary>
        void DressCoping()
        {
            var ring = HarborKit.TryLoad(HarborKit.RescueBuoy);
            var coil = HarborKit.TryLoad(HarborKit.RopeKnot) ?? HarborKit.TryLoad(HarborKit.Rope1);
            float half = QuayHalf;

            if (ring != null)
                for (float x = -half + 13.5f; x < half - 4f; x += 27f)
                    HarborKit.Sit(ring, new Vector3(x + 1.4f, TileTop, 2.6f), HarborKit.Range(_rng, -25f, 25f),
                                  _waterlineRoot, "LifeRing");
            if (coil != null)
                for (float x = -half + 13.5f; x < half - 4f; x += 18f)
                {
                    if (_rng.NextDouble() < 0.45) continue;
                    HarborKit.Sit(coil, new Vector3(x + HarborKit.Range(_rng, -1.5f, 1.5f), TileTop, -0.1f),
                                  HarborKit.Range(_rng, 0f, 360f), _waterlineRoot, "RopeCoil");
                }
        }

        // ------------------------------------------------------------ the concrete

        /// <summary>Oil and standing water where a port makes them: under the gantries'
        /// rails, at the mouths of the forklift aisles, round the drums, and along the
        /// yard road where the lorries stand. Laid on the paint plane, so they lie on
        /// the concrete and the asphalt alike without fighting either for the pixel.</summary>
        void StainTheConcrete()
        {
            var puddles = HarborKit.LoadAll(HarborKit.Puddles, quiet: true);
            if (puddles.Count == 0) return;
            float half = QuayHalf;

            void Spill(Vector3 at, int n, float spread)
            {
                for (int k = 0; k < n; k++)
                {
                    float x = at.x + HarborKit.Range(_rng, -spread, spread);
                    if (Mathf.Abs(x) > half - 3f || InGateLane(x, 2f)) continue;
                    Mark(HarborKit.Pick(_rng, puddles),
                         new Vector3(x, 0f, at.z + HarborKit.Range(_rng, -spread * 0.4f, spread * 0.4f)),
                         HarborKit.Range(_rng, 0f, 360f), "Oil", _waterlineRoot);
                }
            }

            for (int i = 0; i < berths; i++)
            {
                float xb = BerthX(i);
                // the machines stand and drip where they are worked
                Spill(new Vector3(xb, 0f, QuayLaneZ), 4, 26f);
                Spill(new Vector3(AisleX(i), 0f, YardRoadZ0 - 2f), 2, 3f);
                if (IsBoxBerth(i))
                {
                    Spill(new Vector3(xb, 0f, HarborCrane.SeaRailZ), 3, 30f);
                    Spill(new Vector3(xb, 0f, HarborCrane.LandRailZ), 3, 30f);
                }
            }
            // and the length of the shed road, where the lorries idle at the doors
            foreach (var door in _shedDoors) Spill(new Vector3(door.x + 3f, 0f, ShoulderZ), 2, 4f);
        }

        // ------------------------------------------------------------ gulls

        /// <summary>The birds. Over the water off each berth, and thick over a fishing
        /// wall - a quay where fish are landed is the loudest place in any port. Live
        /// objects: a particle system in the static merge comes out the other side as a
        /// mesh of nothing.</summary>
        void SetTheGulls()
        {
            var birds = HarborKit.TryLoad(HarborKit.FxBirds);
            if (birds == null) return;
            for (int i = 0; i < berths; i++)
            {
                int n = Kind(i) == HarborBerthKind.Fishing ? 3 : 1;
                for (int k = 0; k < n; k++)
                {
                    var go = Instantiate(birds, _liveRoot);
                    go.name = "Gulls";
                    go.transform.localPosition = new Vector3(
                        BerthX(i) + HarborKit.Range(_rng, -30f, 30f),
                        WaterY + HarborKit.Range(_rng, 12f, 26f),
                        HarborKit.Range(_rng, -40f, -6f));
                }
            }
        }
    }
}
