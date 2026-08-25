using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // The port's other trade, laid out as three things a player can find and use
    // rather than as a story anybody has to be told:
    //
    //   - ONE BOX, standing by itself in a back lot behind the sheds, chained,
    //     barriered off, under a camera, with a man walking round it. Everything else
    //     in the yard is stacked five rows deep and nobody watches any of it.
    //   - A HOLE IN THE WIRE, well away from either gate, with the panel gone, the
    //     razor coil pulled down beside it and a path worn through the grass on both
    //     sides. The way in for anybody who does not fancy the weighbridge.
    //   - A SHED ON OFFER: the store next to that lot stands empty with a board on the
    //     front of it. The one building in the port that is for hire.
    //
    // None of it does anything by itself; it is a place set for something to happen
    // in. The mission that uses it is not this file's business, and the guard walks
    // his round whether anybody ever comes or not.
    public partial class HarborDistrict
    {
        /// <summary>How wide the panel that came out of the wire was: half-span, so the
        /// gap is twice this. A man's width and a bit - not a lorry's.</summary>
        const float WireCutHalf = 1.8f;

        /// <summary>Where the wire is cut, in the port's own x. Rolled with the berths so
        /// the fence can be laid round it (FenceLine); zero if the port is honest.</summary>
        float _wireCutX;
        /// <summary>The back lot left empty for the watched box, x by z.</summary>
        Rect _contrabandLot;

        Transform _crimeRoot;

        /// <summary>Where the wire is cut - the far side of the fence, the way a man
        /// comes in. Rolled with the berth kinds because the fence is laid before
        /// anything here runs.</summary>
        void PlanContraband()
        {
            _wireCutX = 0f;
            if (!contraband) return;
            float half = QuayHalf;
            for (int tries = 0; tries < 24; tries++)
            {
                float x = HarborKit.Range(_rng, -half + 20f, half - 20f);
                if (InGateLane(x, 22f)) continue;       // never at a gate: that is what a gate is
                _wireCutX = x;
                return;
            }
        }

        void BuildContraband()
        {
            if (!contraband) return;
            _crimeRoot = Root("Harbor Contraband");
            CutTheWire();
            StowTheBox();
            OfferTheShed();
        }

        // ------------------------------------------------------------ the wire

        /// <summary>The hole: the panel gone (FenceLine left the gap), the razor coil
        /// that crowned it pulled down and lying beside the posts, and the path worn
        /// through - out over the verge toward the street on one side and in across the
        /// yard on the other. The path is what makes it read as used rather than as a
        /// fence the builder forgot to finish.</summary>
        void CutTheWire()
        {
            if (Mathf.Approximately(_wireCutX, 0f)) return;
            float y = TileTop, x = _wireCutX, z = _fenceZ;
            var coil = HarborKit.TryLoad(HarborKit.BarbedWire);
            var panel = HarborKit.TryLoad(HarborKit.FencePanel);
            var dirt = HarborKit.LoadAll(HarborKit.DirtPatches, quiet: true);

            // the cut coil, dragged clear and left lying on its side inside the wire
            if (coil != null)
                for (int k = 0; k < 2; k++)
                {
                    var go = HarborKit.Sit(coil, new Vector3(x + (k == 0 ? -2.6f : 2.4f), y, z - 2.2f - k * 0.9f),
                                           HarborKit.Range(_rng, -35f, 35f), _crimeRoot, "CutWire");
                    if (go != null) go.transform.Rotate(HarborKit.Range(_rng, 60f, 100f), 0f, 0f, Space.Self);
                }
            // and the panel itself, laid flat in the grass on the far side
            if (panel != null)
            {
                var go = HarborKit.PlaceRun(panel, new Vector3(x - WireCutHalf, LandY, z + 3.2f),
                                            new Vector3(x + WireCutHalf, LandY, z + 3.6f), _crimeRoot, fit: false, "DroppedPanel");
                if (go != null) go.transform.Rotate(82f, 0f, 0f, Space.Self);
            }
            // the path: patches of bare ground stepping out over the verge and in across
            // the concrete, thinning as they go
            if (dirt.Count > 0)
                for (int k = -4; k <= 3; k++)
                {
                    if (k == 0) continue;
                    float pz = z + k * 1.9f;
                    // never on the service carriageway: bare earth painted over working
                    // asphalt reads as a hole in the road, not as a path
                    if (pz > _serviceRoadZ0 - 0.5f && pz < _serviceRoadZ1 + 0.5f) continue;
                    bool outside = pz > z;
                    var at = new Vector3(x + HarborKit.Range(_rng, -0.9f, 0.9f), 0f, pz);
                    var go = Mark(HarborKit.Pick(_rng, dirt), at, HarborKit.Range(_rng, 0f, 360f), "WornPath", _crimeRoot);
                    if (go == null) continue;
                    // outside the wire the ground is the island's, not the apron's
                    if (outside)
                    {
                        var p = go.transform.position;
                        go.transform.position = new Vector3(p.x, LandY + PaintY, p.z);
                    }
                    float k2 = 1f - Mathf.Abs(k) * 0.14f;
                    go.transform.localScale = new Vector3(k2, 1f, k2) * HarborKit.Range(_rng, 0.7f, 1.1f);
                }
        }

        // ------------------------------------------------------------ the box

        /// <summary>The one box in the port that is worth watching: on its own in the
        /// back lot the yard pass kept empty, chained, penned in with barriers, a camera
        /// on a pole over it and a man walking round it. A pallet and a couple of drums
        /// beside it, because a box standing on bare concrete with a guard on it and
        /// nothing else looks staged.</summary>
        void StowTheBox()
        {
            if (_contrabandLot.width < 6f || _boxPrefabs.Count == 0) return;
            float y = TileTop;
            float cx = _contrabandLot.center.x;
            float cz = _contrabandLot.yMax - 5.5f;

            // the rusted one if the bake made one: a box that has been standing a while
            var prefab = _boxPrefabs[0];
            foreach (var p in _boxPrefabs)
                if (p.name.IndexOf("rust", System.StringComparison.OrdinalIgnoreCase) >= 0) prefab = p;
            var box = HarborKit.Prop(prefab, new Vector3(cx, y, cz), 90f + HarborKit.Range(_rng, -1.5f, 1.5f),
                                     _crimeRoot, "Sealed Container");
            var bb = HarborKit.BoundsOf(box);

            // the chain and its hook across the doors, on the end that faces the alley
            var chain = HarborKit.TryLoad(HarborKit.Chain);
            var anchor = HarborKit.TryLoad(HarborKit.ChainAnchor);
            float endX = bb.max.x;
            if (chain != null)
                HarborKit.Span(chain, new Vector3(endX + 0.05f, y + 1.1f, bb.min.z + 0.4f),
                               new Vector3(endX + 0.05f, y + 1.1f, bb.max.z - 0.4f), 1f, _crimeRoot, "Seal");
            if (anchor != null)
                HarborKit.Prop(anchor, new Vector3(endX + 0.1f, y + 1.1f, bb.center.z), -90f, _crimeRoot, "Padlock");

            // the pen: barriers across the mouth of the lot, and a cone at each corner
            var barrier = HarborKit.TryLoad(HarborKit.ConcreteBlock);
            var cone = HarborKit.TryLoad(HarborKit.Cone);
            if (barrier != null)
            {
                var kb = HarborKit.PrefabBounds(barrier);
                float step = Mathf.Max(1f, Mathf.Max(kb.size.x, kb.size.z));
                for (float px = bb.min.x - 1.5f; px <= bb.max.x + 1.5f; px += step)
                    HarborKit.Sit(barrier, new Vector3(px, y, bb.min.z - 2.4f), 0f, _crimeRoot, "Barrier");
            }
            if (cone != null)
                foreach (var corner in new[] { new Vector2(bb.min.x - 1f, bb.min.z - 1f), new Vector2(bb.max.x + 1f, bb.min.z - 1f) })
                    HarborKit.Sit(cone, new Vector3(corner.x, y, corner.y), 0f, _crimeRoot, "Cone");

            // a camera over it, on the fence pole the yard already puts along this line
            var pole = HarborKit.TryLoad(HarborKit.Powerpole);
            var camera = HarborKit.TryLoad(HarborKit.SecurityCamera);
            if (pole != null && camera != null)
            {
                var post = HarborKit.Sit(pole, new Vector3(bb.max.x + 3f, y, bb.max.z + 1f), 180f, _crimeRoot, "CameraPole");
                if (post != null)
                {
                    var pb = HarborKit.BoundsOf(post);
                    HarborKit.Prop(camera, new Vector3(bb.max.x + 2.7f, pb.max.y - 0.6f, bb.max.z + 0.6f), 215f, _crimeRoot, "Camera");
                }
            }

            // the working clutter, so it is a corner of a yard and not an exhibit
            var pallet = HarborKit.TryLoad(HarborKit.Pallet);
            var drum = HarborKit.TryLoad(HarborKit.BarrelMetal);
            if (pallet != null) HarborKit.Sit(pallet, new Vector3(bb.min.x - 2.2f, y, bb.center.z + 1f), 25f, _crimeRoot, "Pallet");
            if (drum != null)
                for (int k = 0; k < 3; k++)
                    HarborKit.Sit(drum, new Vector3(bb.min.x - 2.6f + (k % 2) * 0.9f, y, bb.center.z - 1.4f - k * 0.4f),
                                  HarborKit.Range(_rng, 0f, 360f), _crimeRoot, "Drum");

            // and the man on it: a tight round of the box, not a beat round the port
            LoadBodies();
            if (_workerBodies.Count > 0)
            {
                var round = new List<Vector3>
                {
                    new Vector3(bb.min.x - 3.4f, y, bb.min.z - 1.2f),
                    new Vector3(bb.max.x + 3.4f, y, bb.min.z - 1.6f),
                    new Vector3(bb.max.x + 3.6f, y, bb.max.z + 1.4f),
                    new Vector3(bb.center.x, y, bb.min.z - 3f),
                };
                var man = Man(HarborKit.Pick(_rng, _workerBodies), _liveRoot, round[0], 1.05f, WorldPoints(round), null);
                if (man != null) man.DwellRange = new Vector2(4f, 12f);
            }
            _namedWorks.Add((box.transform, "Sealed Container"));
        }

        // ------------------------------------------------------------ the shed

        /// <summary>The store beside that lot, standing empty with a board on it. Renamed
        /// rather than rebuilt: the shed line's own buildings are what the map puts cards
        /// on (BlockTheYard reads the transform's name), so the name IS the offer.</summary>
        void OfferTheShed()
        {
            if (_contrabandLot.width < 6f || _warehouseRoot == null) return;
            float want = _contrabandLot.center.x;
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (Transform t in _warehouseRoot)
            {
                var b = HarborKit.BoundsOf(t.gameObject);
                if (b.size.y < 3f) continue;
                float d = Mathf.Abs(t.position.x - want);
                if (d < bestD) { bestD = d; best = t; }
            }
            if (best == null) return;
            best.name = "Bonded Store (To Let)";

            var board = HarborKit.TryLoad(HarborKit.HireSign);
            if (board == null) return;
            var bb = HarborKit.BoundsOf(best.gameObject);
            HarborKit.Sit(board, new Vector3(bb.center.x + 4f, TileTop, bb.min.z - 1.6f), 0f, _crimeRoot, "ToLetBoard");
        }
    }
}
