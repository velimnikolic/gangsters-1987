using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // The join in the middle of the port's one long piece of work.
    //
    // The gantry lands a box on the quay and the forklift trundles off to the shed
    // with a pallet - but until now those were two machines each doing its own thing
    // on its own clock, and nothing said the pallet had come out of the box. The
    // devanning gang is what says it: the moment a box is set down, this party of
    // pallets is moved to stand BESIDE THAT BOX, the shuttle's pick-up end is moved
    // with it, and the pallets are counted off one a trip until they are gone and the
    // shuttle stands idle again waiting for the next box.
    //
    // So the chain reads end to end: ship -> gantry -> the stack on the quay -> this
    // box, opened -> forklift -> shed door -> the pile at the door -> lorry.
    //
    // Plain class, ticked by nobody: it only answers the cargo handler's Landed and
    // the forklift's Took.
    public sealed class HarborDevanning
    {
        /// <summary>Pallets got out of one box before the gang moves on.</summary>
        public const int PerBox = 4;

        readonly List<GameObject> _pallets = new List<GameObject>();
        readonly Transform _party;
        readonly Vector3 _offset;        // where the gang stands relative to the box, in the port's frame
        readonly RoadDemo.DistrictFrame _frame;
        readonly float _groundY;         // the concrete: a box landed on the upper layer is not stood on

        int _left;

        /// <summary>Where the forklift comes to pick up - beside the last box landed.
        /// Until one has been, it is where the party was first stood.</summary>
        public Vector3 At { get; private set; }

        /// <summary>Whether there is anything left in the box to take.</summary>
        public bool HasWork => _left > 0;

        /// <summary>The gang for one berth: a party of pallets under one root, made once
        /// and moved about, so a box being opened costs no instantiation at all.
        /// <para><paramref name="firstStand"/> is in the PORT'S OWN coordinates and is
        /// laid down before the port has been carried onto its shore - the live root it
        /// hangs off is still at the origin, so a port-local point set as a world one
        /// lands exactly where the root will later take it. This is the same trick the
        /// forklifts and the seeded stacks are built by. After that the port has moved
        /// and everything here is world: <see cref="Opened"/> is handed a box's foot
        /// straight out of the cargo handler.</para></summary>
        public HarborDevanning(Transform live, RoadDemo.DistrictFrame frame, Vector3 firstStand,
                               Vector3 offset, GameObject pallet, IList<GameObject> freight, System.Random rng)
        {
            _frame = frame;
            _offset = offset;
            At = frame.ToWorld(firstStand);
            _groundY = At.y;

            _party = new GameObject("Devanning").transform;
            _party.SetParent(live, false);
            _party.localPosition = firstStand;
            _party.localRotation = Quaternion.identity;
            if (pallet == null) return;

            var pb = HarborKit.PrefabBounds(pallet);
            for (int i = 0; i < PerBox; i++)
            {
                var group = new GameObject("Goods");
                group.transform.SetParent(_party, false);
                // laid out in ONE row along the quay, never two deep. The lane the gang
                // works in is the strip between the back of the live row and the
                // gantry's landward rail, and a second row of pallets reaches into the
                // rail - where the gantry's leg travels straight through it.
                group.transform.localPosition = new Vector3((i - (PerBox - 1) * 0.5f) * 1.75f, 0f, 0f);
                HarborKit.Sit(pallet, group.transform.position, HarborKit.Range(rng, -10f, 10f), group.transform, "Pallet");
                if (freight != null && freight.Count > 0)
                    HarborKit.Sit(HarborKit.Pick(rng, freight), group.transform.position + new Vector3(0f, pb.size.y, 0f),
                                  HarborKit.Range(rng, 0f, 360f), group.transform, "Freight");
                group.SetActive(false);
                _pallets.Add(group);
            }
        }

        /// <summary>A box has been landed: the gang moves to it and opens it.</summary>
        public void Opened(Vector3 boxFoot)
        {
            // beside the box, and on the concrete: a box landed on the second layer has
            // its foot three metres up, and the gang does not work three metres up
            At = boxFoot + _frame.ToWorldDir(_offset);
            At = new Vector3(At.x, _groundY, At.z);
            if (_party != null) _party.position = At;
            _left = _pallets.Count;
            for (int i = 0; i < _pallets.Count; i++) _pallets[i].SetActive(true);
        }

        /// <summary>The shuttle has taken one: the pallet it took goes out.</summary>
        public void Taken()
        {
            if (_left <= 0) return;
            _left--;
            if (_left < _pallets.Count) _pallets[_left].SetActive(false);
        }
    }
}
