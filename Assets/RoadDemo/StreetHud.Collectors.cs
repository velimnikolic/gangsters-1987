using System.Collections.Generic;
using LivingCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace RoadDemo
{
    /// <summary>
    /// THE COLLECTORS, under the clock (GAN-273).
    ///
    /// The bag detail is the one part of the outfit that walks the city on its own
    /// errand: it leaves the house with nothing, calls at door after door and comes
    /// back carrying the week. Every other surface said so somewhere else - the map
    /// draws a diamond, the marker over his head names him, the block file counts him -
    /// and a boss watching the street had no standing place to look for the answer to
    /// "where is my money right now".
    ///
    /// So: one small panel per detail, stacked ACROSS under the clock strip, in the
    /// same crew order the chips above them keep. Each says who carries the bag, what
    /// the detail is doing in the same words the map's tip and his own marker use
    /// (CrewStatus.Bag - one word table, three surfaces), what is in the bag and how
    /// many doors are left on the round. A press puts the camera on him and holds it
    /// there until it is pressed again, which is what "follow him across the city"
    /// means at every zoom level.
    ///
    /// The detail is NOT selectable and this panel does not make it so: the bag man
    /// takes no orders from the street (his lieutenant's book gives him the round), and
    /// a panel that picked him would put a man on the pointer who refuses every order
    /// the pointer can give.
    /// </summary>
    public sealed partial class StreetHud
    {
        /// <summary>Under the chip row, with the strip of air the design leaves between
        /// the bar and whatever hangs off it.</summary>
        const float CollectorTop = BarTall + 4f;
        const float CollectorWide = 148f;
        const float CollectorTall = 40f;
        const float CollectorGap = 3f;

        /// <summary>What the panel's edge says at a glance, and the only thing on it
        /// that is painted rather than written - so a change of class REPAINTS the row
        /// (HudNight remembers the colour a graphic was built with, and a colour poked
        /// in afterwards is undone the moment the street crosses into night).</summary>
        enum BagState { Home, Round, Trouble }

        sealed class CollectorFace
        {
            public DemoCrews.Unit Unit;
            public TextMeshProUGUI Status, Money, Stops;
            public string ShownStatus, ShownMoney, ShownStops;
        }

        RectTransform _collectorRoot;
        readonly List<CollectorFace> _collectors = new List<CollectorFace>();
        readonly List<DemoCrews.Unit> _details = new List<DemoCrews.Unit>();
        int _paintedCollectors = -1;

        /// <summary>The detail the camera is riding, if the player put it there from
        /// this row. Held so a second press lets go, and dropped when the player pans
        /// away and the rig stops riding on its own.</summary>
        DemoCrews.Unit _followed;
        DemoCamera _rig;

        void TendCollectors()
        {
            if (_rig == null)
                _rig = FindAnyObjectByType<DemoCamera>();
            if (_followed != null && (_rig == null || !_rig.Riding))
                _followed = null;

            var stamp = CollectorStamp();
            if (stamp != _paintedCollectors)
            {
                _paintedCollectors = stamp;
                PaintCollectors();
            }
            RefreshCollectors();
        }

        /// <summary>Our own bag details, in crew order - the order the chips above are
        /// dealt in, so the two rows read down the same outfit.</summary>
        List<DemoCrews.Unit> Details()
        {
            _details.Clear();
            if (_crews == null)
                return _details;
            for (var i = 0; i < _crews.Units.Count; i++)
            {
                var unit = _crews.Units[i];
                if (unit == null || !unit.IsDetachment || unit.Wiped ||
                    unit.Faction != LivingCity.Gangs.GangCatalog.PlayerGangId)
                    continue;
                _details.Add(unit);
            }
            _details.Sort((a, b) => a.CrewId.CompareTo(b.CrewId));
            return _details;
        }

        /// <summary>Which details are on the row, in what state, and which one the
        /// camera is on. The words and the figures move without any of this, and are
        /// written into the type that is already standing.</summary>
        int CollectorStamp()
        {
            var stamp = 19;
            var details = Details();
            for (var i = 0; i < details.Count; i++)
            {
                var unit = details[i];
                stamp = stamp * 31 + unit.CrewId;
                stamp = stamp * 31 + unit.Standing();
                stamp = stamp * 31 + (int)StateOf(unit);
                stamp = stamp * 31 + (unit == _followed ? 1 : 0);
            }
            return stamp;
        }

        static BagState StateOf(DemoCrews.Unit unit)
        {
            if (unit == null)
                return BagState.Home;
            if (unit.InCustody || unit.TargetUnit != null || BagDown(unit) != null)
                return BagState.Trouble;
            return TerritoryRuntime.Instance != null &&
                   TerritoryRuntime.Instance.TryGetRound(unit.CrewId, out _, out _, out _)
                ? BagState.Round : BagState.Home;
        }

        /// <summary>The bag this detail dropped, still lying where its man fell. It is
        /// the one thing on this panel the boss has to act on, so it takes the line the
        /// status word would have had.</summary>
        static BagOnGround BagDown(DemoCrews.Unit unit)
        {
            var lying = BagOnGround.All;
            if (unit == null || lying == null)
                return null;
            for (var i = 0; i < lying.Count; i++)
            {
                var bag = lying[i];
                if (bag != null && bag.CrewId == unit.CrewId &&
                    bag.OwnerFaction == unit.Faction)
                    return bag;
            }
            return null;
        }

        void PaintCollectors()
        {
            if (_collectorRoot != null)
                Destroy(_collectorRoot.gameObject);
            _collectors.Clear();
            _night.ForgetDead();

            var details = Details();
            if (details.Count == 0)
            {
                _collectorRoot = null;
                return;
            }

            var root = NewRect("Collectors", _canvas.transform);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(0f, -CollectorTop);
            root.sizeDelta = new Vector2(0f, CollectorTall);
            _collectorRoot = root;

            // The same cap the chip row keeps: the row stops rather than running in
            // under the wire.
            var frame = ((RectTransform)_canvas.transform).rect.width;
            var room = Mathf.Max(0f, frame - BarRightMargin);

            var x = 0f;
            for (var i = 0; i < details.Count; i++)
            {
                if (x + CollectorWide > room)
                    break;
                Panel(root, x, details[i]);
                x += CollectorWide + CollectorGap;
            }

            root.sizeDelta = new Vector2(Mathf.Max(0f, x - CollectorGap), CollectorTall);
            _night.Register(root);
        }

        void Panel(RectTransform row, float x, DemoCrews.Unit unit)
        {
            var followed = unit == _followed;
            var state = StateOf(unit);

            var panel = NewRect("Collector " + unit.CrewId, row);
            PlaceTopLeft(panel, x, 0f, CollectorWide, CollectorTall);
            Fill(panel, followed ? LedgerV2.Picked : LedgerV2.Panel);

            var clicks = ClickSurface(panel).gameObject.AddComponent<ChipClicks>();
            clicks.Pick = () => Follow(unit);
            clicks.Ride = () => Follow(unit);

            // The edge is the state: red while he is out on the round with our money,
            // amber while something has gone wrong with it, and the paper blue of a man
            // who is only on the books while he sits at home.
            Block("Edge", panel, 0f, 0f, 3f, CollectorTall,
                state == BagState.Round ? LedgerV2.Red
                    : state == BagState.Trouble ? LedgerV2.Amber
                    : LedgerV2.PaperBlue);

            var face = new CollectorFace { Unit = unit };
            _collectors.Add(face);

            const float TextX = 9f;
            const float MoneyWide = 50f;
            var nameWide = CollectorWide - TextX - MoneyWide - 6f;

            // Who carries it - the man himself, not the crew: the boss is watching a
            // MAN cross the city, and the crew's own name is already on the chip above.
            var carrier = CrewStatus.Carrier(unit);
            var name = LedgerV2.Name(panel, TextX, -1f, nameWide,
                carrier != null ? carrier.DisplayName
                    : unit.Parent != null ? unit.Parent.Name : unit.Name,
                12.5f);
            name.raycastTarget = false;

            // What is in the bag right now. Not the day's takings and not what the
            // block owes: the figure this man is carrying on his person, which is the
            // figure that is lost if he is put down before he banks it.
            // A line box is taller than the type in it and taller again for larger type,
            // so the figure is dropped by half the difference to sit on the name's own
            // centre line rather than a unit above it.
            face.Money = LedgerV2.Figure(panel, CollectorWide - MoneyWide - 6f,
                -1f - (LineBox(12.5f) - LineBox(11.5f)) * 0.5f,
                MoneyWide, "", 11.5f,
                state == BagState.Trouble ? LedgerV2.Red : LedgerV2.Ink);
            face.Money.raycastTarget = false;

            face.Status = LedgerV2.Mono(panel, TextX, -21f, nameWide, "", 9.2f,
                state == BagState.Trouble ? LedgerV2.Red : LedgerV2.Muted, 8f);
            face.Status.raycastTarget = false;

            face.Stops = LedgerV2.Mono(panel, CollectorWide - MoneyWide - 6f, -21f,
                MoneyWide, "", 9.2f, LedgerV2.Label, 4f,
                TextAlignmentOptions.MidlineRight);
            face.Stops.raycastTarget = false;

            WriteCollector(face);
        }

        /// <summary>Put the camera on this detail - and take it off again when the
        /// panel that put it there is pressed a second time. The rule for WHERE he is
        /// is CrewQuarters.WhereStands, the same one the map draws him at: a detail
        /// behind a door has bodies standing on the pavement it walked in from, and a
        /// camera sent to those bodies looks at the wrong side of the street.</summary>
        void Follow(DemoCrews.Unit unit)
        {
            if (_rig == null)
                _rig = FindAnyObjectByType<DemoCamera>();
            if (_rig == null || unit == null)
                return;

            if (_followed == unit && _rig.Riding)
            {
                _rig.Drop();
                _followed = null;
                return;
            }

            _followed = unit;
            // A detail with nobody left standing has no position of its own - its men
            // are gone and Unit.Position answers the map's origin, which would throw the
            // camera into the corner of the world. What is left to watch in that case is
            // the bag lying where he fell, and after that nothing: the ride ends and the
            // camera stays where it stopped.
            _rig.Ride(() =>
            {
                if (!unit.Wiped)
                    return CrewQuarters.WhereStands(unit);
                var lying = BagDown(unit);
                return lying != null && lying.transform != null
                    ? lying.transform.position : (Vector3?)null;
            });
        }

        void RefreshCollectors()
        {
            for (var i = 0; i < _collectors.Count; i++)
                WriteCollector(_collectors[i]);
        }

        /// <summary>The three things that move without the row being rebuilt: the word,
        /// the money and the doors left. Written into the type that is already standing
        /// - a row torn down and rebuilt at the rate a man walks would flicker.</summary>
        void WriteCollector(CollectorFace face)
        {
            var unit = face != null ? face.Unit : null;
            if (unit == null || face.Status == null)
                return;

            var lying = BagDown(unit);
            var carried = 0;
            var stopsLeft = 0;
            var round = TerritoryRuntime.Instance != null &&
                        TerritoryRuntime.Instance.TryGetRound(
                            unit.CrewId, out carried, out stopsLeft, out _);

            var status = lying != null ? "BAG ON THE GROUND"
                : CrewStatus.Bag(unit) ?? "OUTSIDE";
            var money = lying != null ? LedgerText.Cash(lying.Take)
                : carried > 0 ? LedgerText.Cash(carried) : "";
            var stops = lying != null ? "GO AND GET IT"
                : round && stopsLeft > 0
                    ? stopsLeft + (stopsLeft == 1 ? " DOOR" : " DOORS")
                    : "";

            if (status != face.ShownStatus)
            {
                face.ShownStatus = status;
                face.Status.text = status;
            }
            if (money != face.ShownMoney)
            {
                face.ShownMoney = money;
                face.Money.text = money;
            }
            if (stops != face.ShownStops)
            {
                face.ShownStops = stops;
                face.Stops.text = stops;
            }
        }
    }
}
