using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // The work of one berth: while a ship lies alongside her deck boxes come off one
    // by one - lifted, carried over the bulwark, set down in the berth's own stack
    // on the quay - and when the deck is bare a few from the stack go back aboard as
    // the outward cargo, so the stack never overflows and both flows are seen. Plain
    // class ticked by the shipping; the forklifts and the men read Working off the berth.
    //
    // Given a Hook (the berth's crane), the handler waits on the gear rather than the
    // clock: it names the box it wants next and holds still until the spreader is down
    // on it, then flies the box while the crane keeps station overhead. With no hook
    // the flights happen as they always did, unattended.
    public sealed class HarborCargo
    {
        enum Stage { Idle, Unloading, Loading, Done }
        enum Flight { None, Lift, Carry, Lower }

        const float LiftHeight = 7f;
        const float LiftTime = 2.4f, CarryTime = 4.2f, LowerTime = 2.2f;

        readonly Transform _yard;
        readonly List<Vector3> _quaySlots;          // world, bottom centre, low layer first
        readonly GameObject[] _onQuay;              // what stands in each slot
        readonly List<GameObject> _boxPrefabs;
        readonly System.Random _rng;

        HarborShip _ship;
        List<Vector3> _deckSlots;                   // ship-local
        int _deckBase;                              // the first slots are her through cargo - not ours
        readonly List<GameObject> _onDeck = new List<GameObject>();   // in slot order
        Stage _stage = Stage.Idle;
        int _toLoad;
        float _pause;

        // the box the hook is going for, before its flight begins
        GameObject _wanted;
        Vector3 _wantedTo;
        bool _wantedToDeck;
        int _wantedSlot;

        // the box in flight
        Flight _flight = Flight.None;
        GameObject _box;
        Vector3 _from, _to, _fromTop, _toTop;
        float _t;
        bool _toDeck;
        int _slotIndex;

        public HarborCargo(Transform yard, List<Vector3> quaySlots, List<GameObject> boxPrefabs, System.Random rng)
        {
            _yard = yard;
            _quaySlots = quaySlots;
            _onQuay = new GameObject[quaySlots.Count];
            _boxPrefabs = boxPrefabs;
            _rng = rng;
        }

        /// <summary>How a box lies on the quay: along it (its own +Z is its length).</summary>
        public static readonly Quaternion QuayRot = Quaternion.Euler(0f, 90f, 0f);

        /// <summary>Where the port stands in the world. The stack slots are the port's
        /// own - they are laid out and compared in its frame, and the boxes seeded with
        /// them are put down before the port is moved onto its shore - so a box that
        /// FLIES (from a deck to the stack) goes through this, and so does the way it
        /// comes to rest, which must be square with the quay wherever the quay faces.</summary>
        public RoadDemo.DistrictFrame Frame = RoadDemo.DistrictFrame.Identity;

        Quaternion Stacked => Frame.Rotation * QuayRot;

        /// <summary>The gear that moves the boxes, if the berth has any.</summary>
        public IHarborHook Hook;

        public bool Busy => _flight != Flight.None || _wanted != null;
        public bool Finished => _stage == Stage.Done || _stage == Stage.Idle;
        public int OnQuay { get { int n = 0; foreach (var g in _onQuay) if (g != null) n++; return n; } }

        /// <summary>The stack as the scene opens: a few boxes already landed.</summary>
        public void SeedQuay(int count)
        {
            for (int i = 0; i < _quaySlots.Count && count > 0; i++)
            {
                if (!SlotFree(i)) continue;
                _onQuay[i] = Spawn(_quaySlots[i], QuayRot, _yard);   // before the port is moved: its own frame
                count--;
            }
        }

        bool SlotFree(int i)
        {
            if (_onQuay[i] != null) return false;
            // the upper layer needs the box under it (the layout lists the low layer first)
            int lower = LowerOf(i);
            return lower < 0 || _onQuay[lower] != null;
        }

        int LowerOf(int i)
        {
            for (int j = 0; j < i; j++)
                if (Mathf.Abs(_quaySlots[j].x - _quaySlots[i].x) < 0.1f && Mathf.Abs(_quaySlots[j].z - _quaySlots[i].z) < 0.1f
                    && _quaySlots[j].y < _quaySlots[i].y - 0.1f) return j;
            return -1;
        }

        bool SlotFreeToTake(int i)
        {
            if (_onQuay[i] == null) return false;
            for (int j = i + 1; j < _quaySlots.Count; j++)
                if (_onQuay[j] != null && LowerOf(j) == i) return false;   // something stands on it
            return true;
        }

        GameObject Spawn(Vector3 at, Quaternion rot, Transform parent)
        {
            var prefab = HarborKit.Pick(_rng, _boxPrefabs);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, at, rot, parent);
            go.name = "Container";
            return go;
        }

        /// <summary>A ship has made fast: her deck cargo appears (what she brought),
        /// sized to what the stack can take, and the unloading begins.</summary>
        public void Begin(HarborShip ship, bool midStay)
        {
            _ship = ship;
            _onDeck.Clear();
            _deckSlots = ship.Spec != null ? ship.Spec.DeckSlots() : new List<Vector3>();
            _deckBase = Mathf.Clamp(ship.DeckBase, 0, _deckSlots.Count);
            int free = 0;
            for (int i = 0; i < _quaySlots.Count; i++) if (_onQuay[i] == null) free++;
            int most = Mathf.Min(_deckSlots.Count - _deckBase, Mathf.Max(0, free - 1));
            int load = most <= 0 ? 0 : _rng.Next(Mathf.Min(3, most), Mathf.Min(most, 9) + 1);
            if (midStay) load = load / 2;
            for (int i = 0; i < load; i++)
            {
                var box = Spawn(Vector3.zero, Quaternion.identity, ship.Model);
                if (box == null) break;
                box.transform.localPosition = _deckSlots[_deckBase + i];
                box.transform.localRotation = Quaternion.identity;
                _onDeck.Add(box);
            }
            _stage = Stage.Unloading;
            _pause = HarborKit.Range(_rng, 3f, 6f);
        }

        /// <summary>The ship is leaving: whatever is aboard goes with her, the flight
        /// in progress having been allowed to land first (see Busy).</summary>
        public void End()
        {
            _ship = null;
            _onDeck.Clear();
            _stage = Stage.Idle;
            _wanted = null;
            Hook?.Release();
        }

        public void Tick(float dt)
        {
            if (_flight != Flight.None) { TickFlight(dt); return; }
            if (_wanted != null) { TickReach(); return; }
            if (_ship == null || _ship.Root == null) return;
            if (_stage != Stage.Unloading && _stage != Stage.Loading) return;
            _pause -= dt;
            if (_pause > 0f) return;

            if (_stage == Stage.Unloading)
            {
                if (_onDeck.Count == 0)
                {
                    // the deck is bare: a few from the stack go aboard as outward cargo
                    _toLoad = OnQuay <= 1 ? 0 : _rng.Next(1, Mathf.Max(1, Mathf.Min(OnQuay - 1, _deckSlots.Count - _deckBase)) + 1);
                    _stage = _toLoad > 0 ? Stage.Loading : Stage.Done;
                    _pause = 4f;
                    return;
                }
                // the last-stacked box comes off first, to the first free stack slot
                int slot = -1;
                for (int i = 0; i < _quaySlots.Count; i++) if (SlotFree(i)) { slot = i; break; }
                if (slot < 0) { _stage = Stage.Done; return; }
                var box = _onDeck[_onDeck.Count - 1];
                _onDeck.RemoveAt(_onDeck.Count - 1);
                Want(box, Frame.ToWorld(_quaySlots[slot]), toDeck: false, slot);
            }
            else
            {
                if (_toLoad <= 0 || _deckBase + _onDeck.Count >= _deckSlots.Count) { _stage = Stage.Done; return; }
                int slot = -1;
                for (int i = _quaySlots.Count - 1; i >= 0; i--) if (SlotFreeToTake(i)) { slot = i; break; }
                if (slot < 0) { _stage = Stage.Done; return; }
                var box = _onQuay[slot];
                _onQuay[slot] = null;
                var deckLocal = _deckSlots[_deckBase + _onDeck.Count];
                _toLoad--;
                Want(box, _ship.Model.TransformPoint(deckLocal), toDeck: true, -1);
            }
        }

        /// <summary>The next box named to the hook. With no hook the lift begins at
        /// once, as it used to.</summary>
        void Want(GameObject box, Vector3 to, bool toDeck, int slot)
        {
            if (box == null) return;
            if (Hook == null)
            {
                StartFlight(box, box.transform.position, to, toDeck, slot);
                return;
            }
            _wanted = box;
            _wantedTo = to;
            _wantedToDeck = toDeck;
            _wantedSlot = slot;
        }

        /// <summary>Waiting on the gear: a box on a heaving deck moves under the hook,
        /// so where it stands is re-read every frame until the spreader is on it.</summary>
        void TickReach()
        {
            if (_wanted == null) return;
            var from = _wanted.transform.position;
            if (!Hook.Reach(from)) return;
            var box = _wanted;
            _wanted = null;
            StartFlight(box, from, _wantedTo, _wantedToDeck, _wantedSlot);
        }

        void StartFlight(GameObject box, Vector3 from, Vector3 to, bool toDeck, int slot)
        {
            _box = box;
            _from = from;
            _to = to;
            float top = Mathf.Max(from.y, to.y) + LiftHeight;
            _fromTop = new Vector3(from.x, top, from.z);
            _toTop = new Vector3(to.x, top, to.z);
            _toDeck = toDeck;
            _slotIndex = slot;
            _t = 0f;
            _flight = Flight.Lift;
            // in flight the box is the yard's, not the ship's - the bob lets go of it
            box.transform.SetParent(_yard, true);
        }

        void TickFlight(float dt)
        {
            if (_box == null) { _flight = Flight.None; Hook?.Release(); return; }
            _t += dt;
            Hook?.Carry(_box.transform.position);
            switch (_flight)
            {
                case Flight.Lift:
                    _box.transform.position = Vector3.Lerp(_from, _fromTop, Mathf.SmoothStep(0f, 1f, _t / LiftTime));
                    if (_t >= LiftTime) { _t = 0f; _flight = Flight.Carry; }
                    break;
                case Flight.Carry:
                    // the destination may be a deck that heaves: re-read it as we go
                    if (_toDeck && _ship != null && _ship.Model != null)
                    {
                        _to = _ship.Model.TransformPoint(_deckSlots[_deckBase + _onDeck.Count]);
                        _toTop = new Vector3(_to.x, _toTop.y, _to.z);
                    }
                    _box.transform.position = Vector3.Lerp(_fromTop, _toTop, Mathf.SmoothStep(0f, 1f, _t / CarryTime));
                    if (_toDeck && _ship != null)
                        _box.transform.rotation = Quaternion.Slerp(_box.transform.rotation, _ship.Model.rotation, dt * 1.5f);
                    else
                        _box.transform.rotation = Quaternion.Slerp(_box.transform.rotation, Stacked, dt * 1.5f);
                    if (_t >= CarryTime) { _t = 0f; _flight = Flight.Lower; }
                    break;
                case Flight.Lower:
                    if (_toDeck && _ship != null && _ship.Model != null)
                        _to = _ship.Model.TransformPoint(_deckSlots[_deckBase + _onDeck.Count]);
                    _box.transform.position = Vector3.Lerp(_toTop, _to, Mathf.SmoothStep(0f, 1f, _t / LowerTime));
                    if (_t >= LowerTime)
                    {
                        _flight = Flight.None;
                        if (_toDeck && _ship != null && _ship.Model != null)
                        {
                            _box.transform.SetParent(_ship.Model, true);
                            _box.transform.localPosition = _deckSlots[_deckBase + _onDeck.Count];
                            _box.transform.localRotation = Quaternion.identity;
                            _onDeck.Add(_box);
                        }
                        else if (_toDeck)
                        {
                            Object.Destroy(_box);   // the ship left mid-flight - into the water with it
                        }
                        else
                        {
                            _box.transform.position = _to;
                            _box.transform.rotation = Stacked;
                            if (_slotIndex >= 0) _onQuay[_slotIndex] = _box;
                        }
                        _box = null;
                        Hook?.Release();
                        _pause = HarborKit.Range(_rng, 6f, 11f);
                    }
                    break;
            }
        }
    }
}
