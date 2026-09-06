using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using LivingCity.Business;
using LivingCity.Entities;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Territory;
using RoadDemo;
using static LivingCity.UI.LedgerKit;
using DoorTenure = LivingCity.Outfit.DoorTenure;

namespace LivingCity.UI
{
    /// <summary>Who the ledger is allowed to put behind a door order.</summary>
    public enum DoorDispatch
    {
        /// <summary>The floating command menu: an explicitly chosen crew, then the only
        /// crew currently out on the street.</summary>
        PickedOrStreet,

        /// <summary>The block file: only the responsible leader's crew when assigned.
        /// Otherwise an explicit crew choice, then the one crew standing there.</summary>
        BlockResponsibility,
    }

    /// <summary>
    /// THE DOOR'S MENU - the one panel that says what may be done to a shop, who goes,
    /// and what it costs.
    ///
    /// It is painted in TWO places and written ONCE: under the block file in the ledger,
    /// beside the row of the premise that was picked, and over the turf map, beside the
    /// premise that was clicked. Both surfaces call <see cref="Open"/>, both share the
    /// same chosen crew and the same last ruling, so a row added here appears on both and
    /// an order given from either is literally the same order.
    ///
    /// Nothing here decides a rule. The rows come from TerritoryRacketOrders, the money
    /// from Outfit.EconomyPrices, the deed from Gameplay.DoorHolder, the demand's terms
    /// from the racket itself. This class draws them and files what the reader presses.
    /// </summary>
    public static class DoorMenu
    {
        // ------------------------------------------------------------------ the door

        /// <summary>One shop, as anything offering a menu over it needs to see it.</summary>
        public struct Door
        {
            public TerritoryBusinessId Id;

            /// <summary>The ground it stands on - what a collection round walks.</summary>
            public TerritoryBlockId Block;

            public string Name;
            public string Trade;

            /// <summary>The word painted on the pavement outside it, when the outfit
            /// knows about the front; empty for an ordinary shop.</summary>
            public string Role;

            /// <summary>Whose word that is, for the badge's colour; -1 when none.</summary>
            public int RoleGang;

            public string RivalName;
            public DoorTenure Tenure;

            /// <summary>Is this the one address a running man makes for (GAN-235)?</summary>
            public bool IsHideout;
            public TerritoryProtectionState Standing;
            public bool InGoodStanding;
            public int TakePerDay;
            public int BuyPrice;
            public TerritoryDoorClosure Closure;

            // ------------------------------------------------------------ the gazda

            /// <summary>Whose deed it is, in words. Empty where the scene has neither a
            /// business directory nor a marker to ask.</summary>
            public string OwnerName;

            /// <summary>A man, a firm, or the city - a deed is not always a person.</summary>
            public BusinessOwnerKind OwnerKind;

            /// <summary>The seed his face is dealt from. His own, off the deed
            /// (BusinessOwners), so the same city shows the same man twice.</summary>
            public int PortraitSeed;

            public int OwnerGeneration;

            /// <summary>The model to photograph when the owner is a man the game already
            /// dresses - a house's boss. Empty for a shopkeeper, whose face is dealt off
            /// the seed above.</summary>
            public string OwnerFace;

            /// <summary>What kind of man he is (ECON-002) - the trait the racket weighs
            /// him by, never a word chosen for the page.</summary>
            public TerritoryOwnerTrait Trait;

            public bool IsValid => Id.IsValid;
        }

        /// <summary>
        /// One door read off the city - deed, trade, standing and price - out of the same
        /// sources the block ledger reads its whole column from. This is the ONE reading:
        /// the ledger fills its own rows through it too, so the sheet and the map can
        /// never disagree about who holds a shop or what it is worth.
        /// </summary>
        public static bool TryRead(TerritoryBusinessId id, out Door door)
        {
            door = default;
            if (!id.IsValid)
                return false;

            var rows = CityBusinesses.All;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Id != id)
                    continue;
                door = Read(rows[i]);
                return true;
            }

            // A shop the city's business list does not carry is still a door. The core
            // benches keep their premises only in the TERRITORY GEOGRAPHY - the legacy
            // marker sweep behind CityBusinesses has no id on them at all - and the map
            // picks a shop out of exactly that geography, so a menu that could only be
            // opened off the list could not be opened on those scenes at all.
            var runtime = TerritoryRuntime.Instance;
            if (runtime?.Geography == null ||
                !runtime.Geography.TryGetBusinessBlock(id, out var block))
                return false;

            var name = runtime.TryGetBusinessView(id, out var view)
                ? view.BusinessName
                : id.Value;
            door = Read(id, name, block, null);
            return true;
        }

        /// <summary>The same reading, off a row a caller already has in hand.</summary>
        public static Door Read(CityBusinessRow row) =>
            Read(row.Id, row.Name, row.CanonicalBlockId, row.Marker);

        /// <summary>The reading itself, off the four things a door is known by.</summary>
        public static Door Read(TerritoryBusinessId id, string name, TerritoryBlockId block,
            BusinessMarker view)
        {
            var runtime = TerritoryRuntime.Instance;
            var racket = runtime != null ? runtime.Racket : null;
            var us = new TerritoryGangId(GangCatalog.PlayerGangId);
            var business = BusinessRuntime.Instance;

            var door = new Door
            {
                Id = id,
                Block = block,
                Name = name,
                Trade = "PREMISES",
                Tenure = DoorTenure.Open,
                RoleGang = -1,
                Standing = racket != null
                    ? racket.StateOf(id, us)
                    : TerritoryProtectionState.Unaffiliated,
                Closure = ClosureOf(id),
                IsHideout = TerritoryHideout.Is(id),
            };

            var archetype = BusinessArchetypeId.Grocer;
            var priced = false;
            if (business != null && business.Populated &&
                business.Directory.TryGet(id, out var record))
            {
                archetype = record.Archetype;
                priced = true;
                door.Trade = TradeWord(record.Archetype);
                if (record.DisplayName.Length > 0)
                    door.Name = record.DisplayName;

                // The deed names a man and carries the seed his face is dealt from. The
                // menu prints what the deed says and deals nothing of its own.
                if (business.Directory.TryGetOwner(record.OwnerId, out var deed))
                {
                    door.OwnerName = deed.DisplayName;
                    door.OwnerKind = deed.Kind;
                    door.PortraitSeed = deed.PortraitSeed;
                }
                door.OwnerGeneration = business.OwnerGenerationOf(id);
            }

            // What kind of man he is: the profile the demand is already weighed against
            // (TerritoryOwnerProfile), read rather than chosen.
            if (runtime != null)
            {
                door.Trait = runtime.OwnerProfileOf(id).Trait;
                door.InGoodStanding = runtime.DoorInGoodStanding(id, us);
            }

            // The DEED first - the deed book is the record and the marker only a view of
            // it, so a street that is streamed out still reads as held.
            var marker = view;
            if (marker == null)
                BusinessViewBindings.TryGet(id, out marker);
            door.Tenure = Gameplay.DoorHolder.Read(id, marker, out var holder);
            door.RivalName = holder >= 0 ? GangName(holder) : null;

            // The older generated-city scenes keep the gazda on the marker rather than in
            // a directory. The deed wins where there is one; this is the fallback.
            if (string.IsNullOrEmpty(door.OwnerName) && marker != null)
                door.OwnerName = marker.Owner != null
                    ? marker.Owner.DisplayName
                    : marker.OwnerName;

            var front = FrontOn(id);
            if (front != null)
            {
                door.Role = front.Role;
                door.RoleGang = front.GangId;
                // A HOUSE'S OWN PREMISES is not a shopkeeper's. The deed on a front is
                // the family's and the man on it is their boss, so the civilian gazda the
                // generator dealt this site is not who answers here.
                ReadFront(ref door);
            }

            if (priced)
            {
                door.BuyPrice = Outfit.EconomyPrices.BuyPrice(archetype);
                door.TakePerDay = door.Tenure switch
                {
                    DoorTenure.Ours => Outfit.EconomyPrices.NetPerDay(archetype),
                    DoorTenure.Paying when !door.Closure.Shut =>
                        Outfit.EconomyPrices.ProtectionPerWeek(archetype) / 7,
                    _ => 0,
                };
            }

            return door;
        }

        public static TerritoryDoorClosure ClosureOf(TerritoryBusinessId businessId)
        {
            var business = BusinessRuntime.Instance;
            if (business == null ||
                !business.TryGetShutdown(businessId, out var shutdown))
                return default;

            var ours = BusinessDeeds.GangOf(businessId) == GangCatalog.PlayerGangId;
            var outfit = Gameplay.OutfitDirector.Instance;
            var affordable = ours && outfit != null &&
                             outfit.Accounts.Safe >= shutdown.RepairPrice;
            return new TerritoryDoorClosure(
                true,
                BusinessShutdownText.Line(shutdown),
                repairVisible: ours && shutdown.RepairPrice > 0,
                repairAvailable: affordable,
                repairPrice: shutdown.RepairPrice,
                cause: shutdown.Cause);
        }

        /// <summary>
        /// The man on a front's deed: ours is the Don on the roster, a rival's is the man
        /// the outfit knows at the head of that family. Both are named where the game
        /// already names them (GangCatalog, GangRegistry) - nothing is invented here, and
        /// a family with no named man keeps only its own name.
        /// </summary>
        static void ReadFront(ref Door door)
        {
            door.OwnerKind = BusinessOwnerKind.Individual;

            if (door.RoleGang == GangCatalog.PlayerGangId)
            {
                var roster = Book();
                var boss = roster != null ? roster.FindBoss() : null;
                door.OwnerName = boss != null ? boss.FullName : GangCatalog.BossName;
                door.OwnerFace = boss != null
                    ? GangLooks.LookFor(boss, roster)
                    : GangCatalog.BossModel;
                return;
            }

            var gangs = GangRegistry.Gangs;
            for (var i = 0; i < gangs.Count; i++)
            {
                if (gangs[i] == null || gangs[i].Id != door.RoleGang)
                    continue;
                for (var m = 0; m < gangs[i].Members.Count; m++)
                    if (gangs[i].Members[m].Lieutenant)
                    {
                        door.OwnerName = gangs[i].Members[m].FullName;
                        break;
                    }
                break;
            }

            if (string.IsNullOrEmpty(door.OwnerName))
                door.OwnerName = GangName(door.RoleGang);
            if (door.RoleGang >= 0 && door.RoleGang < GangCatalog.LieutenantModels.Length)
                door.OwnerFace = GangCatalog.LieutenantModels[door.RoleGang];
        }

        /// <summary>
        /// The family's premises standing on this business, when the outfit knows about
        /// it. Ours needs no finding - a man knows where he works. A rival's door stays
        /// out of the book until a crew of ours has stood outside it
        /// (<see cref="RoadDemo.TurfKnowledge"/>).
        /// </summary>
        public static GangFront FrontOn(TerritoryBusinessId id)
        {
            if (!id.IsValid)
                return null;

            var all = GangFront.All;
            for (var i = 0; i < all.Count; i++)
            {
                var front = all[i];
                if (front == null || front.BusinessId != id)
                    continue;
                return TurfKnowledge.IsKnown(front) ? front : null;
            }

            return null;
        }

        public static string GangName(int gangId)
        {
            var gangs = GangRegistry.Gangs;
            for (var i = 0; i < gangs.Count; i++)
                if (gangs[i] != null && gangs[i].Id == gangId)
                    return gangs[i].Name;
            return "another house";
        }

        /// <summary>The archetype's own name, spaced out for the sheet: PortCompany is a
        /// PORT COMPANY. Derived from the enum so a trade added to the catalogue prints
        /// itself without a table here to forget to update.</summary>
        public static string TradeWord(BusinessArchetypeId archetype)
        {
            var name = archetype.ToString();
            var word = new System.Text.StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    word.Append(' ');
                word.Append(char.ToUpperInvariant(name[i]));
            }
            return word.ToString();
        }

        // --------------------------------------------------------------- the tenure

        public static readonly Color TenureOurs = LedgerV2.Rgb2(0xaf3c3a);
        public static readonly Color TenurePaying = LedgerV2.Rgb2(0x59985b);
        public static readonly Color TenureRival = LedgerV2.Rgb2(0x674f8d);
        public static readonly Color TenureOpen = LedgerV2.Rgb2(0xb4a99e);

        public static Color TenureColour(DoorTenure tenure) => tenure switch
        {
            DoorTenure.Ours => TenureOurs,
            DoorTenure.Paying => TenurePaying,
            DoorTenure.Rival => TenureRival,
            _ => TenureOpen,
        };

        public static string TenureWord(DoorTenure tenure) => tenure switch
        {
            DoorTenure.Ours => "OURS",
            DoorTenure.Paying => "PAYS US",
            DoorTenure.Rival => "THEIRS",
            _ => "OPEN",
        };

        // ---------------------------------------------------------------- the state

        /// <summary>One crew identity shared by the block band and door menu.</summary>
        public static int SelectedCrewId { get; private set; } = -1;
        public static int SelectedPersonId { get; private set; } = -1;

        /// <summary>What the office last said about an order given here. Painted at the
        /// foot of the menu wherever it stands.</summary>
        public static string Note { get; private set; } = "";

        /// <summary>Moves whenever anything the menu draws itself from changes, so a
        /// surface holding the panel open knows to repaint it.</summary>
        public static int Version { get; private set; }

        /// <summary>Drops the pick and the last ruling - a different door, or a menu
        /// shut.</summary>
        public static void Forget()
        {
            SelectedCrewId = SelectedPersonId = -1;
            Note = "";
            Version++;
        }

        public static void Say(string note)
        {
            Note = note ?? "";
            Version++;
        }

        public static void ToggleCrew(int crewId)
        {
            SelectedCrewId = SelectedCrewId == crewId ? -1 : crewId;
            SelectedPersonId = -1;
            Version++;
        }

        public static void TogglePerson(int personId)
        {
            SelectedPersonId = SelectedPersonId == personId ? -1 : personId;
            SelectedCrewId = -1;
            Version++;
        }

        public static void ConstrainToBlock(Roster roster, TerritoryBlockId block,
            IReadOnlyList<TacticalPersonnelMapping> physical = null)
        {
            var staleCrew = SelectedCrewId >= 0 && BlockMissionChoice.Refusal(
                roster, block, SelectedCrewId, true) != null;
            var staleMan = SelectedPersonId >= 0 && BlockMissionChoice.PersonRefusal(
                roster, block, SelectedPersonId, true, physical) != null;
            if (!staleCrew && !staleMan) return;
            SelectedCrewId = SelectedPersonId = -1;
            Version++;
        }

        static Roster Book()
        {
            var director = Gameplay.PersonnelDirector.Instance;
            return director != null ? director.Roster : null;
        }

        // ----------------------------------------------------------------- the paper

        /// <summary>The menu never opens wider than this, so a wide sheet does not hand
        /// the reader a panel the width of a page for eight short keys.</summary>
        public const float MaxWidth = 380f;

        /// <summary>A key never stretches past this.</summary>
        const float KeyMax = 300f;

        /// <summary>
        /// Paints the whole menu as a card under <paramref name="parent"/> and answers it,
        /// sized. The caller places it: the ledger level with its row, the map beside the
        /// shop that was clicked. <paramref name="changed"/> is called when the menu's own
        /// state moves and the surface must repaint; <paramref name="close"/> is the X in
        /// its corner, and no X is drawn when there is nothing to close.
        /// </summary>
        public static RectTransform Open(Transform parent, Door door, float width,
            Action changed, Action close,
            DoorDispatch dispatch = DoorDispatch.PickedOrStreet,
            bool showCommands = true)
        {
            var infoOnly = !showCommands;

            var panel = LedgerV2.Card("Door menu", parent, 0f, 0f, width, 1f, LedgerV2.Head);
            // It is laid OVER whatever it opened on, so it must also stop the clicks the
            // rows beneath it would otherwise answer.
            ClickSurface(panel);

            var y = 10f;
            const float closeW = 24f;
            var headW = width - 24f - (close != null ? closeW + 6f : 0f);
            var name = LedgerV2.Name(panel, 12f, -y, headW, door.Name, 15f,
                LedgerV2.HeadCream);
            name.overflowMode = TextOverflowModes.Ellipsis;
            if (close != null)
            {
                var shut = LedgerV2.Button(panel, "X", width - 12f - closeW, -(y - 2f),
                    closeW, closeW, () => close(), LedgerV2.Key.Outline, 11f);
                shut.color = LedgerV2.HeadCream;
                LedgerV2.KeyFrame(shut, LedgerV2.HeadDim);
            }
            y += 20f;

            Caps(panel, 12f, -y, headW,
                (string.IsNullOrEmpty(door.Role) ? "" : door.Role + " · ") +
                door.Trade + " · " + TenureWord(door.Tenure),
                infoOnly ? 11f : 9f, LedgerV2.HeadDim, 6f)
                .font = LedgerStyle.Mono;
            y += infoOnly ? 25f : 22f;

            y += Gazda(panel, 12f, y, width - 24f, door, infoOnly);

            // The door's own sentence WRAPS. It carries the demand's terms - what we are
            // worth to this man against what he wants - and a line cut off at the panel's
            // edge is exactly the half the reader needs.
            var noteMax = infoOnly ? 72f : 48f;
            var note = Paragraph(panel, LedgerStyle.Mono, infoOnly ? 12f : 10f,
                LedgerV2.HeadDim, 12f, -y, width - 24f, noteMax, Sentence(door), 2f);
            y += Mathf.Clamp(note.preferredHeight, infoOnly ? 18f : 14f, noteMax) + 6f;

            if (showCommands)
            {
                y += Hands(panel, 12f, y, width - 24f, door, changed, dispatch);
                y += Keys(panel, 12f, y, width - 24f, door, changed, dispatch);
            }

            if (showCommands && !string.IsNullOrEmpty(Note))
            {
                var ruling = Paragraph(panel, LedgerStyle.MonoItalic, 10f,
                    LedgerV2.HeadCream, 12f, -y, width - 24f, 42f, Note, 2f);
                y += Mathf.Clamp(ruling.preferredHeight, 14f, 42f) + 4f;
            }

            y += 12f;
            PlaceTopLeft(panel, 0f, 0f, width, y);
            return panel;
        }

        // ------------------------------------------------------------ floating over

        /// <summary>
        /// The menu as a PANEL THAT FLOATS: one host per surface that can open it over
        /// something - the turf plate, the street - each holding a rect on its own canvas
        /// and the same <see cref="Open"/> inside it. The ledger needs none: it paints the
        /// panel straight onto its sheet, where there is nothing to float over.
        ///
        /// The host owns placement and dismissal and nothing else. What the menu SAYS and
        /// what its keys DO is the one implementation above, so a surface cannot drift.
        /// </summary>
        public sealed class Host
        {
            /// <summary>Over the turf plate (60) and the street overlay (1), under the
            /// book (110) and the street's own paperwork (115) - the menu floats over a
            /// surface, it does not float over the page a reader opened on purpose.
            /// </summary>
            const int SortingOrder = 100;

            /// <summary>How far off the shop the panel stands, canvas units.</summary>
            const float Gap = 14f;

            /// <summary>The breath kept between the panel and the window's edge.</summary>
            const float Margin = 12f;

            Canvas canvas;
            RectTransform root;
            TerritoryBusinessId shown;
            Vector2 at;
            bool commands = true;
            int stamp = -1;

            public bool IsOpen => root != null && root.gameObject.activeSelf;

            /// <summary>True when the menu is up and something it draws itself from has
            /// moved - the surface repaints it on its own beat.</summary>
            public bool Stale => IsOpen && stamp != Version;

            /// <summary>Whether the pointer is on the panel. A surface that polls the
            /// mouse itself has to stand aside for it.</summary>
            public bool Contains(Vector2 screen) =>
                IsOpen && RectTransformUtility.RectangleContainsScreenPoint(root, screen);

            /// <summary>
            /// Opens the menu over this door, beside the point that was clicked. A
            /// different door is a different job, so the men picked at the last one do not
            /// follow the pointer across the city. False when the point names no door.
            /// </summary>
            public bool Show(TerritoryBusinessId id, Vector2 screen)
                => Show(id, screen, true);

            /// <summary>Opens the business dossier only. A left click in the 3D city is
            /// inspection, never an order surface; the right-click card owns commands.</summary>
            public bool ShowInfo(TerritoryBusinessId id, Vector2 screen)
                => Show(id, screen, false);

            bool Show(TerritoryBusinessId id, Vector2 screen, bool showCommands)
            {
                if (!id.IsValid || !TryRead(id, out _))
                    return false;

                if (shown != id || commands != showCommands)
                    Forget();
                shown = id;
                at = screen;
                commands = showCommands;
                Paint();
                return true;
            }

            public void Close()
            {
                if (!shown.IsValid && !IsOpen)
                    return;
                shown = default;
                Forget();
                if (root != null)
                    root.gameObject.SetActive(false);
            }

            public void Paint()
            {
                if (!shown.IsValid || !TryRead(shown, out var door))
                {
                    Close();
                    return;
                }

                Build();

                // Destroy is deferred to the end of the frame, so the old panel is put out
                // of sight at once - a dead key that still answers a click is worse than a
                // flicker.
                for (var i = root.childCount - 1; i >= 0; i--)
                {
                    var stale = root.GetChild(i).gameObject;
                    stale.SetActive(false);
                    UnityEngine.Object.Destroy(stale);
                }

                var panel = Open(root, door, MaxWidth, Paint, Close,
                    DoorDispatch.PickedOrStreet, commands);
                var w = panel.sizeDelta.x;
                var h = panel.sizeDelta.y;
                root.sizeDelta = new Vector2(w, h);
                stamp = Version;

                // The click arrives in real pixels and the panel is measured in canvas
                // units, so the reading comes onto the canvas's own ladder first.
                var scale = Mathf.Max(0.01f, canvas.scaleFactor);
                var point = at / scale;
                var screenW = Screen.width / scale;
                var screenH = Screen.height / scale;

                // BESIDE THE SHOP, LEVEL WITH IT: left of the point where there is room,
                // right of it where there is not, and centred on the point rather than
                // hung from it - a panel this tall pinned by its top edge fell straight
                // off the bottom of the window and was clamped into the corner, which is
                // what made it read as "anywhere but at the shop".
                var x = point.x - Gap - w;
                if (x < Margin)
                {
                    var toTheRight = point.x + Gap;
                    x = toTheRight + w <= screenW - Margin ? toTheRight : Margin;
                }
                x = Mathf.Clamp(x, Margin, Mathf.Max(Margin, screenW - w - Margin));

                // The rect hangs from its TOP edge, so half the panel above the point is
                // what centres it on the point.
                var top = point.y + h * 0.5f;
                var lowest = Mathf.Min(h + Margin, screenH - Margin);
                top = Mathf.Clamp(top, lowest, Mathf.Max(lowest, screenH - Margin));

                root.anchorMin = root.anchorMax = Vector2.zero;
                root.pivot = new Vector2(0f, 1f);
                root.anchoredPosition = new Vector2(x, top);
                root.gameObject.SetActive(true);
            }

            /// <summary>
            /// The menu gets a CANVAS OF ITS OWN, with its own raycaster. The surfaces it
            /// opens over do not all have one - the street overlay is hit-tested by hand
            /// on purpose - and a panel of real keys cannot be pressed on a canvas nothing
            /// raycasts. Its own canvas also puts every surface's copy at the same height
            /// over the same things.
            /// </summary>
            void Build()
            {
                if (canvas != null && root != null)
                    return;

                if (EventSystem.current == null)
                {
                    var events = new GameObject("EventSystem");
                    events.AddComponent<EventSystem>();
                    events.AddComponent<InputSystemUIInputModule>();
                }

                var host = new GameObject("Door Menu", typeof(RectTransform));
                canvas = host.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;

                var scaler = host.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;
                host.AddComponent<GraphicRaycaster>();

                root = NewRect("Panel", host.transform);
            }
        }

        // ------------------------------------------------------------------ the gazda

        /// <summary>
        /// THE MAN BEHIND THE COUNTER: his face, his name, what the deed makes him, and
        /// what kind of man he is. Every one of the four is the city's own - the deed
        /// (BusinessOwners) names him and carries the seed his face is dealt from, the
        /// economy (TerritoryOwnerProfile) says what he is like - and the menu only
        /// prints them. Nothing is spawned for him: a deed is a name, not an actor.
        ///
        /// A firm and City Hall get no photograph, because neither is a man; their plate
        /// keeps its initials. Answers the height the block took.
        /// </summary>
        static float Gazda(RectTransform panel, float x, float top, float width, Door door,
            bool largeCopy = false)
        {
            if (string.IsNullOrEmpty(door.OwnerName))
                return 0f;

            const float plateW = 44f;
            const float plateH = 52f;
            var textX = x + plateW + 10f;
            var textW = width - plateW - 10f;

            var house = door.RoleGang >= 0;
            var plate = LedgerV2.PortraitPlate(panel, x, -top, plateW, plateH,
                Initials(door.OwnerName), LedgerV2.DarkPlate, LedgerV2.HeadDim);
            if (!string.IsNullOrEmpty(door.OwnerFace))
                PortraitStudio.Request(PortraitStudio.FindPeoplePrefab(door.OwnerFace),
                    PortraitStudio.Framing.Bust, plate);
            else if (door.OwnerKind == BusinessOwnerKind.Individual)
                PortraitStudio.Request(
                    PortraitStudio.CivilianPrefab(door.PortraitSeed, FemaleDeed(door.OwnerName)),
                    PortraitStudio.Framing.Bust, plate);

            var name = LedgerV2.Name(panel, textX, -(top + 4f), textW, door.OwnerName,
                largeCopy ? 15.5f : 13.5f, LedgerV2.HeadCream);
            name.overflowMode = TextOverflowModes.Ellipsis;

            // A house's man is read by his house, not by how easily he folds: nobody
            // leans on a family's own front, so the shopkeeper's trait says nothing here.
            Caps(panel, textX, -(top + 22f), textW,
                house
                    ? "BOSS · " + GangName(door.RoleGang)
                    : KindWord(door.OwnerKind) + " · " + TraitWord(door.Trait),
                largeCopy ? 10.5f : 9f, LedgerV2.HeadDim, 6f).font = LedgerStyle.Mono;

            LedgerV2.Mono(panel, textX, -(top + (largeCopy ? 37f : 36f)), textW,
                house ? "the house's own premises" : TraitLine(door.Trait),
                largeCopy ? 11f : 9f,
                LedgerV2.HeadDim, 0.5f);

            if (!house && door.OwnerGeneration > 0)
                LedgerV2.Mono(panel, x, -(top + plateH + 4f), width,
                    BusinessSuccession.MemoryLine,
                    largeCopy ? 10.5f : 9f, LedgerStyle.RedPen, 0.5f);

            return plateH + (largeCopy ? 13f : 10f) +
                   (!house && door.OwnerGeneration > 0 ? 24f : 0f);
        }

        /// <summary>What the deed makes him.</summary>
        static string KindWord(BusinessOwnerKind kind) => kind switch
        {
            BusinessOwnerKind.Company => "COMPANY",
            BusinessOwnerKind.Civic => "CITY HALL",
            _ => "PROPRIETOR",
        };

        /// <summary>His trait, in the economy's own word.</summary>
        static string TraitWord(TerritoryOwnerTrait trait) =>
            trait.ToString().ToUpperInvariant();

        /// <summary>What that trait MEANS at his door, in the terms the racket weighs him
        /// in (TerritoryOwnerProfile) - never a figure invented for the page.</summary>
        static string TraitLine(TerritoryOwnerTrait trait) => trait switch
        {
            TerritoryOwnerTrait.Cowardly => "folds a step early · little fear moves him",
            TerritoryOwnerTrait.Proud => "costs an extra lean · he does not like asking",
            TerritoryOwnerTrait.Greedy => "parting with the cut hurts him",
            TerritoryOwnerTrait.Connected => "leaning on him draws the precinct",
            TerritoryOwnerTrait.Stubborn => "takes a war · he will not be talked round",
            _ => "no reading either way",
        };

        static string Initials(string name)
        {
            var parts = (name ?? "").Split(' ');
            var head = parts.Length > 0 && parts[0].Length > 0 ? parts[0][0].ToString() : "";
            var tail = parts.Length > 1 && parts[parts.Length - 1].Length > 0
                ? parts[parts.Length - 1][0].ToString()
                : "";
            return head + tail;
        }

        /// <summary>Whether the deed names a woman, asked of the SAME table the deed was
        /// drawn from (PedestrianIdentity). Nothing new is stored on the owner for this:
        /// his given name already carries the answer.</summary>
        static bool FemaleDeed(string ownerName)
        {
            if (string.IsNullOrEmpty(ownerName))
                return false;
            var space = ownerName.IndexOf(' ');
            var first = space > 0 ? ownerName.Substring(0, space) : ownerName;
            var names = Entities.PedestrianIdentity.AllFemaleNames;
            for (var i = 0; i < names.Count; i++)
                if (string.Equals(names[i], first, StringComparison.Ordinal))
                    return true;
            return false;
        }

        // ------------------------------------------------------------- what it reads

        public static string Sentence(Door door)
        {
            var tenure = door.Tenure switch
            {
                DoorTenure.Ours => OursLine(door),
                DoorTenure.Paying => PayingLine(door),
                DoorTenure.Rival =>
                    (door.RivalName ?? "Another house") +
                    " holds this door. Taking it means their men answer for it.",
                _ => OpenLine(door),
            };
            return door.Closure.Shut ? door.Closure.Note + ". " + tenure : tenure;
        }

        /// <summary>
        /// A door on our own paper: what it nets, and - first, because it is the fact the
        /// reader came for - whether it is THE HIDEOUT.
        /// </summary>
        static string OursLine(Door door)
        {
            var line = "Ours outright. " + LedgerText.Cash(door.TakePerDay) +
                       " a day and it shows clean on the books.";
            if (!door.IsHideout)
                return line;
            return line + " THE HIDEOUT" +
                   (RoadDemo.CrewQuarters.AnyoneInside(door.Id)
                       ? " - our men are in it."
                       : " - a man who shakes a pursuit runs here.");
        }

        /// <summary>
        /// An open door's line carries THE LAST WORD SPOKEN AT IT: asked and refused,
        /// asked and wavering, leaned on. The racket has remembered the state and the hour
        /// all along; the menu reads it back. Nothing new is stored.
        /// </summary>
        static string OpenLine(Door door)
        {
            var buyLine = door.BuyPrice > 0
                ? " " + LedgerText.Cash(door.BuyPrice) + " buys the premises outright."
                : "";

            var racket = TerritoryRuntime.Instance?.Racket;
            var us = new TerritoryGangId(GangCatalog.PlayerGangId);
            if (racket != null && racket.TryGetRelationship(door.Id, us, out var word))
            {
                var day = " on day " + (int)(word.LastInteraction / 24.0);
                switch (word.State)
                {
                    case TerritoryProtectionState.Approached:
                        return "Our men stood at his door" + day +
                               " - nothing has been asked of him yet." + Price(door.Id);
                    case TerritoryProtectionState.Hesitant:
                        return "Asked" + day + " - the owner is wavering." + Price(door.Id);
                    case TerritoryProtectionState.Defiant:
                        return "Asked" + day + " - the owner refused." + Price(door.Id);
                    case TerritoryProtectionState.Intimidated:
                        return "Leaned on" + day + " - the owner is shaken." + Price(door.Id);
                }
            }

            return door.BuyPrice > 0
                ? "Nobody leans on it." + buyLine
                : "Nobody leans on it. A quiet door and an open one.";
        }

        /// <summary>
        /// WHAT THIS MAN'S YES COSTS, in the same points the racket weighs him in - an
        /// owner four points short and one twenty points short must not read identically.
        /// The bar is HIS: the table's, plus his own nerve, plus what his kind of place is
        /// worth (ECON-002/007), which is why a shop can read 34 and still refuse.
        /// </summary>
        static string Price(TerritoryBusinessId id)
        {
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null || !id.IsValid || !runtime.TryExplainDemand(
                    id, new TerritoryGangId(GangCatalog.PlayerGangId), out var terms))
                return "";

            if (terms.Short <= 0f)
                return " We are worth " + Mathf.RoundToInt(terms.Score) +
                       " to him against the " + Mathf.RoundToInt(terms.AcceptAt) +
                       " he wants - ASK HIM NOW.";

            return " We are worth " + Mathf.RoundToInt(terms.Score) + " to him and he " +
                   "wants " + Mathf.RoundToInt(terms.AcceptAt) + ": " +
                   Mathf.RoundToInt(terms.Short) + " short.";
        }

        /// <summary>What a paying door's line says: the dues meter, read off the ledger
        /// (ECON-001/008) - what it owes right now and when it last paid, never an
        /// invented figure.</summary>
        static string PayingLine(Door door)
        {
            var runtime = TerritoryRuntime.Instance;
            if (runtime != null && runtime.TryGetDues(door.Id, out var owed, out var lastPaid))
                return "Pays for peace. Owes " + LedgerText.Cash(owed) +
                       (lastPaid >= 0 ? " · last paid day " + lastPaid : " · never collected") +
                       " · send a round.";
            return "Pays for peace. " + LedgerText.Cash(door.TakePerDay) +
                   " a day accrues to the dues ledger.";
        }

        // ---------------------------------------------------------------- who goes

        static readonly List<TerritoryActorObservation> actors =
            new List<TerritoryActorObservation>();

        static float Hands(RectTransform panel, float x, float top, float width,
            Door door, Action changed, DoorDispatch dispatch)
        {
            var roster = Book();
            var restricted = dispatch == DoorDispatch.BlockResponsibility;
            if (restricted) ConstrainToBlock(roster, door.Block, CrewMissionPicker.Physical());
            var going = CrewToSend(door.Block, dispatch, out _, out _, out _);
            return CrewMissionPicker.Draw(panel, x, top, width, roster, door.Block,
                restricted, SelectedCrewId, SelectedPersonId, going?.Id ?? -1,
                crewId => { ToggleCrew(crewId); changed?.Invoke(); },
                manId => { TogglePerson(manId); changed?.Invoke(); }, dark: true);
        }

        static string ShortName(string first, string surname)
        {
            var initial = string.IsNullOrEmpty(surname) ? "" : " " + surname[0] + ".";
            return (string.IsNullOrEmpty(first) ? surname : first) + initial;
        }

        // -------------------------------------------------------- what may be done

        static readonly List<TerritoryRacketOrder> orders = new List<TerritoryRacketOrder>();

        /// <summary>
        /// What can actually be done to THIS door, and only that - every row of it out of
        /// the one shared list, so the ledger, the street card and the paper map offer the
        /// SAME menu against the same shop. Red for the doorstep chain (the men walk there
        /// and the demand or the threat happens when they arrive), outline for the work
        /// filed with the outfit's book. A row that cannot be given stands, greyed, rather
        /// than vanishing.
        /// </summary>
        static float Keys(RectTransform panel, float x, float top, float width,
            Door door, Action changed, DoorDispatch dispatch)
        {
            var y = top;
            var keyW = Mathf.Min(KeyMax, (width - 12f) / 3f);
            var cursorX = x;

            // WHO WOULD GO, asked before a single key is drawn. A key that files an
            // order no crew can carry is refused by the office a second and a half
            // later, on a line nothing on this panel ever showed - so the row stands
            // faded instead, which is what the shared table's hasCrew is for.
            var going = CrewToSend(door.Block, dispatch, out _, out _, out _);
            var solo = SelectedPersonId >= 0;
            var soloReason = solo ? SoloRefusal(door, dispatch) : null;
            var hasCrew = going != null || (solo && soloReason == null);

            TerritoryRacketOrders.For(
                door.Standing, door.Tenure, racketable: true, hasCrew,
                MenAtDoor(door.Id), door.BuyPrice, orders,
                closure: door.Closure,
                quarters: RoadDemo.CrewQuarters.State(StreetUnit(going), door.Id),
                isHideout: door.IsHideout,
                inGoodStanding: door.InGoodStanding);

            for (var i = 0; i < orders.Count; i++)
            {
                var row = orders[i];
                var red = row.Kind == TerritoryDoorRowKind.Racket;
                // The money the row commits belongs on the key, and it is the SHARED row's
                // figure - the street card prints the same word beside the same sum.
                var word = row.Cash > 0
                    ? row.Label + " · " + LedgerText.Cash(row.Cash)
                    : row.Label;
                var keyWidth = row.Cash > 0 ? Mathf.Min(KeyMax, width) : keyW;

                if (cursorX > x && cursorX + keyWidth > x + width)
                {
                    cursorX = x;
                    y += 32f;
                }

                var press = Press(door, row, changed, dispatch);
                var label = LedgerV2.Button(panel, word, cursorX, -y, keyWidth, 26f,
                    () => press(), red ? LedgerV2.Key.Red : LedgerV2.Key.Outline, 9f);
                // THE OUTLINE KEY RESTATED IN THE DARK PALETTE. It is drawn for paper -
                // dark ink inside a warm grey box on a cream sheet - and this panel is
                // the near-black head fill, where the box vanishes and the word is left
                // floating. Both halves move: a cream word inside a box in the panel's
                // own dim grey, which is what makes it read as a key at all.
                if (!red)
                {
                    label.color = LedgerV2.HeadCream;
                    LedgerV2.KeyFrame(label, LedgerV2.HeadDim);
                }
                // And the dead key goes dim rather than into the paper greys, which are
                // bright over black - that is what made every key on the panel read alike.
                var soloOrder = row.Kind == TerritoryDoorRowKind.Racket &&
                    (row.Intent == TerritoryRacketIntent.Demand || row.Intent == TerritoryRacketIntent.Threaten);
                LedgerV2.KeyEnabled(label, row.Available && (!solo || (soloOrder && soloReason == null)),
                    LedgerV2.HeadDim, LedgerV2.DarkPlate);
                cursorX += keyWidth + 6f;
            }

            return y + 32f - top;
        }

        /// <summary>Where a shared row goes when it is pressed: the doorstep chain through
        /// the territory gateway, the rest into the outfit's order book.</summary>
        static Action Press(Door door, TerritoryRacketOrder row, Action changed,
            DoorDispatch dispatch)
        {
            if (row.Kind == TerritoryDoorRowKind.Racket)
            {
                var intent = row.Intent;
                return () => { FileRacketIntent(door, intent, dispatch); changed?.Invoke(); };
            }

            if (row.Kind == TerritoryDoorRowKind.Repair)
                return () => { Repair(door); changed?.Invoke(); };

            // Men into one of our own buildings, or out of it. Not filed with the office:
            // the crew walks there and goes in, and the street plays the whole of it
            // (RoadDemo.CrewQuarters) - the same act the street card's row carries.
            if (row.Kind == TerritoryDoorRowKind.Quarters)
            {
                var move = row.Move;
                return () => { MoveQuarters(door, move, dispatch); changed?.Invoke(); };
            }

            // Naming the hideout is a line in the family's book, not an errand: nobody
            // walks anywhere, the safe is not touched, and it takes effect the moment it
            // is pressed (GAN-235).
            if (row.Kind == TerritoryDoorRowKind.Hideout)
            {
                var move = row.HideoutMove;
                return () => { NameHideout(door, move); changed?.Invoke(); };
            }

            var type = row.Job;
            if (type == Outfit.OrderType.BuyPremises)
                return () => { FileBuyPremises(door, dispatch); changed?.Invoke(); };
            return () => { FileStreetJob(door, type, dispatch); changed?.Invoke(); };
        }

        /// <summary>
        /// MAKE THIS THE HIDEOUT, or give it up. One address, so naming a second moves it
        /// rather than adding one; and it is only offered against a door on our own paper,
        /// which the shared row table has already tested.
        /// </summary>
        public static void NameHideout(Door door, TerritoryHideoutMove move)
        {
            if (move == TerritoryHideoutMove.Give)
            {
                TerritoryHideout.Clear();
                Say("no hideout named · the men fall back on the nearest door we hold.");
                Gameplay.CrewVoice.Office(LivingCity.Data.VoiceLines.RktHideoutOff);
                return;
            }

            if (BusinessDeeds.GangOf(door.Id) != GangCatalog.PlayerGangId)
            {
                Say("the hideout has to be on our own paper");
                return;
            }

            // and it has to be a door a man can actually get through
            var closure = ClosureOf(door.Id);
            if (closure.Shut)
            {
                Say(closure.Note);
                return;
            }

            var moved = TerritoryHideout.Any && !TerritoryHideout.Is(door.Id);
            TerritoryHideout.Designate(door.Id);
            Gameplay.CrewVoice.Office(LivingCity.Data.VoiceLines.RktHideoutOn);
            Say(moved
                ? "the hideout moves to " + door.Name + "."
                : door.Name + " is the hideout.");
        }

        /// <summary>Immediate owner repair shared by the street, map and ledger menus.</summary>
        public static void Repair(Door door)
        {
            var outfit = Gameplay.OutfitDirector.Instance;
            if (outfit == null)
            {
                Say("the outfit's accounts are not open in this scene");
                return;
            }

            var result = outfit.RepairBusiness(door.Id);
            if (result.Ok)
                Gameplay.CrewVoice.Office(LivingCity.Data.VoiceLines.RktRepair);
            Say(result.Ok
                ? door.Name + " repaired and open for future collections"
                : result.Reason);
        }

        /// <summary>The bodies on the street behind a crew on the books, or null when
        /// that branch has nobody standing in this scene (the ledger opened on its
        /// own).</summary>
        static DemoCrews.Unit StreetUnit(Crew crew) =>
            crew != null && DemoCrews.Active != null
                ? DemoCrews.Active.UnitOfCrew(crew.Id)
                : null;

        /// <summary>
        /// Men into one of our own premises, or back out of it. The crew is the one the
        /// rest of this menu would send, and the act is the street's - it is carried out
        /// where the player can watch it, not filed with the office.
        /// </summary>
        static void MoveQuarters(
            Door door, TerritoryQuartersMove move, DoorDispatch dispatch)
        {
            var crew = CrewToSend(door.Block, dispatch, out _, out var refusal, out _);
            var unit = StreetUnit(crew);
            if (unit == null)
            {
                Say(string.IsNullOrEmpty(refusal)
                    ? "no crew of ours is on the street to send"
                    : refusal);
                return;
            }

            if (move == TerritoryQuartersMove.Out)
            {
                RoadDemo.CrewQuarters.BringOut(unit);
                Say("out of " + door.Name + " and back on the street.");
                return;
            }

            Say(RoadDemo.CrewQuarters.Station(DemoCrews.Active, unit, door.Id)
                ? "the men are walking to " + door.Name + " to move in."
                : "there is no way into " + door.Name + " from the street.");
        }

        /// <summary>Whether men of ours are already standing at this door - the same
        /// question the street card asks, so the same rows fade on both.</summary>
        public static bool MenAtDoor(TerritoryBusinessId id)
        {
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null || !id.IsValid ||
                !runtime.TryGetBusinessApproach(id, out var door))
                return false;
            var slack = runtime.Racket != null
                ? runtime.Racket.Config.ApproachRadiusMetres
                : 14f;
            return runtime.HasManAt(
                new TerritoryGangId(GangCatalog.PlayerGangId), door, slack);
        }

        // ------------------------------------------------------------ filing a job

        /// <summary>Files the chosen crew's job and rechecks its block responsibility.</summary>
        static void FileStreetJob(Door door, Outfit.OrderType type, DoorDispatch dispatch)
        {
            var word = LedgerText.OrderLabel(type);
            File(word + " at " + door.Name + " asked for.", () =>
            {
                var outfit = Gameplay.OutfitDirector.Instance;
                if (outfit == null)
                    return Outfit.FilingRuling.Refuse("the outfit's order book is not open in this scene");
                var crew = CrewToSend(door.Block, dispatch, out var going, out var why, out _);
                if (crew == null) return Outfit.FilingRuling.Refuse(why);
                if (!Gameplay.DoorJobs.TryBuild(door.Id, type, crew.Id, Mathf.Max(1, going),
                        out var job, out var refusal))
                    return Outfit.FilingRuling.Refuse(refusal);
                var result = outfit.IssueOrder(job);
                return result.Ok
                    ? Outfit.FilingRuling.Grant(BlockMissionChoice.Label(Book(), crew) + " will go")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
        }

        /// <summary>Buys the premises outright. The asking price is the one the economy
        /// table gives that trade, so a barber and a casino are not the same money.
        /// </summary>
        static void FileBuyPremises(Door door, DoorDispatch dispatch)
        {
            File(door.Name + " bought outright. " +
                 LedgerText.Cash(door.BuyPrice) + " committed.", () =>
            {
                var outfit = Gameplay.OutfitDirector.Instance;
                if (outfit == null)
                    return Outfit.FilingRuling.Refuse(
                        "the outfit's order book is not open in this scene");
                var crew = CrewToSend(door.Block, dispatch, out var going, out var why, out _);
                if (crew == null)
                    return Outfit.FilingRuling.Refuse(why);
                if (outfit.Accounts.Safe < door.BuyPrice)
                    return Outfit.FilingRuling.Refuse(
                        "the safe does not cover " + LedgerText.Cash(door.BuyPrice));

                if (!Gameplay.DoorJobs.TryBuild(
                        door.Id, Outfit.OrderType.BuyPremises, crew.Id, going,
                        out var job, out var refusal))
                    return Outfit.FilingRuling.Refuse(refusal);

                var result = outfit.IssueOrder(job);
                return result.Ok
                    ? Outfit.FilingRuling.Grant("the paperwork is with them")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
        }

        /// <summary>
        /// The racket chain: the men walk to the premises' own doorstep and the demand or
        /// the threat happens WHEN THEY ARRIVE - the approach command carries the intent,
        /// so an order given off the menu is one order, not a walk and a second click on
        /// the street. The note names the crew that goes, because a crew the reader did
        /// not pick must never be sent silently.
        /// </summary>
        static void FileRacketIntent(
            Door door, TerritoryRacketIntent intent, DoorDispatch dispatch)
        {
            var runtime = TerritoryRuntime.Instance;
            var roster = Book();
            if (runtime?.Commands == null || roster == null)
            {
                Say("the territory command gateway is unavailable");
                return;
            }

            var solo = SelectedPersonId >= 0;
            var who = "";
            TerritoryCommandNodeId node;
            if (solo)
            {
                var reason = SoloRefusal(door, dispatch);
                if (reason != null) { Say(reason); return; }
                node = TerritoryCommandNodeId.Character(SelectedPersonId);
                who = roster.Find(SelectedPersonId).FullName;
            }
            else
            {
                if (!TryPickStreetCrew(runtime, door, dispatch, out var crew, out node, out var refusal))
                { Say(refusal); return; }
                who = BlockMissionChoice.Label(roster, crew);
            }
            var approach = new ApproachBusinessCommand(node, door.Id, intent,
                door.Block, dispatch == DoorDispatch.BlockResponsibility);
            var sent = intent == TerritoryRacketIntent.Collect && !solo
                ? runtime.Commands.Submit(Gameplay.PlayerCommands.Stamp(new CollectDuesCommand(node, door.Block)))
                : runtime.Commands.Submit(Gameplay.PlayerCommands.Stamp(approach));
            if (sent.Status == TerritoryCommandStatus.Rejected) { Say(sent.Reason); return; }

            Say(intent switch
            {
                TerritoryRacketIntent.Demand =>
                    who + " walks to " + door.Name + " · the demand follows at the door",
                TerritoryRacketIntent.Threaten =>
                    who + " walks to " + door.Name + " · they lean on him at the door",
                TerritoryRacketIntent.Collect =>
                    who + " walks the round · the take banks at the front",
                _ => who + " is on its way to the door",
            });
        }

        static string SoloRefusal(Door door, DoorDispatch dispatch)
        {
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null) return "the territory command gateway is unavailable";
            return runtime.SoloRefusal(Gameplay.PlayerCommands.House, door.Block,
                SelectedPersonId, dispatch == DoorDispatch.BlockResponsibility);
        }

        /// <summary>Resolve the same crew shown by the picker at the moment of dispatch.</summary>
        internal static Crew CrewToSend(TerritoryBlockId block, DoorDispatch dispatch, out int men,
            out string refusal, out string source)
        {
            men = 0;
            refusal = "";
            source = "";
            var roster = Book();
            if (roster == null)
            {
                refusal = "the roster is unavailable";
                return null;
            }

            if (SelectedPersonId >= 0)
            {
                refusal = "one man is selected · choose a door to demand or threaten";
                return null;
            }
            if (SelectedCrewId >= 0)
            {
                refusal = BlockMissionChoice.Refusal(roster, block, SelectedCrewId,
                    dispatch == DoorDispatch.BlockResponsibility) ?? "";
                if (refusal.Length > 0) return null;
                var chosen = roster.FindCrew(SelectedCrewId);
                men = Outfit.CrewKit.MenOf(roster, chosen);
                source = " · PICKED FOR THIS ORDER";
                return chosen;
            }

            if (dispatch == DoorDispatch.BlockResponsibility)
                return BlockCrewToSend(roster, block, out men, out refusal, out source);

            var runtime = TerritoryRuntime.Instance;
            Crew onStreet = null;
            var streetCrews = 0;
            var detail = Bodyguards.DetailOf(roster);
            for (var i = 0; runtime != null && i < roster.Crews.Count; i++)
            {
                // THE DON IS NOT THE MAN THE MENU PICKS FOR YOU. He stands on the street
                // with his detail like any lieutenant now, and this fallback exists to
                // save the player naming the obvious crew when only one is out - the
                // Boss's own detail is never that obvious crew. He goes where he is sent
                // by name, off the picker or off the street card.
                if (detail != null && roster.Crews[i].Id == detail.Id)
                    continue;
                if (!runtime.TryGetCrewNode(roster.Crews[i].Id, out _))
                    continue;
                streetCrews++;
                onStreet = roster.Crews[i];
            }

            if (streetCrews == 1)
            {
                men = Outfit.CrewKit.MenOf(roster, onStreet);
                return onStreet;
            }

            refusal = streetCrews > 1
                ? "several crews are out · choose the crew to send"
                : "no crew of ours is on the street to send";
            return null;
        }

        /// <summary>The block file's dispatch rule. Paper wins: if a lieutenant is
        /// responsible for the block, his branch gets the order. With no name on the
        /// block, the one branch physically standing there gets it. Several branches
        /// require the player to name who answers instead of silently choosing one.</summary>
        static Crew BlockCrewToSend(Roster roster, TerritoryBlockId block,
            out int men, out string refusal, out string source)
        {
            men = 0;
            refusal = "";
            source = "";

            if (BlockMissionChoice.ResponsibleLeader(roster, block) >= 0)
            {
                var assigned = BlockMissionChoice.ResponsibleCrew(roster, block);
                refusal = BlockMissionChoice.Refusal(roster, block, assigned?.Id ?? -1, true) ?? "";
                if (refusal.Length > 0) return null;
                men = Outfit.CrewKit.MenOf(roster, assigned);
                source = "'S CREW · RESPONSIBLE FOR THIS BLOCK";
                return assigned;
            }

            var runtime = TerritoryRuntime.Instance;
            if (runtime == null || !block.IsValid)
            {
                refusal = "nobody is responsible for this block";
                return null;
            }

            actors.Clear();
            runtime.CollectActors(block, actors);
            Crew local = null;
            for (var i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                if (!actor.GangId.IsValid ||
                    actor.GangId.Value != GangCatalog.PlayerGangId ||
                    !actor.CharacterId.IsValid)
                    continue;
                var crew = roster.CrewOf(actor.CharacterId.Value);
                var man = roster.Find(actor.CharacterId.Value);
                if (man == null || man.Duty == Duty.Collector || man.Duty == Duty.Escort ||
                    roster.DoorOrders.Find(man.Id) != null ||
                    BlockMissionChoice.Refusal(roster, block, crew?.Id ?? -1, true) != null)
                    continue;
                if (local != null && local.Id != crew.Id)
                {
                    refusal = "several crews stand here · name a lieutenant for the block";
                    return null;
                }
                local = crew;
            }

            if (local == null)
            {
                refusal = "nobody is responsible or standing on this block";
                return null;
            }

            men = Outfit.CrewKit.MenOf(roster, local);
            source = "'S CREW · STANDING ON THIS BLOCK";
            return local;
        }

        /// <summary>The same crew, with the gateway node a doorstep order is submitted
        /// through - which is the one thing a racket row needs on top of the men.</summary>
        static bool TryPickStreetCrew(TerritoryRuntime runtime, Door door,
            DoorDispatch dispatch,
            out Crew crew, out TerritoryCommandNodeId node, out string refusal)
        {
            crew = CrewToSend(door.Block, dispatch, out _, out refusal, out _);
            if (crew == null || !runtime.TryGetCrewNode(crew.Id, out node))
            {
                node = default;
                if (string.IsNullOrEmpty(refusal))
                    refusal = "no crew of ours is on the street to send";
                return false;
            }

            refusal = "";
            return true;
        }

        /// <summary>The office. The order takes effect at the click and the menu prints
        /// what came of it; with no outfit in the scene the same resolver runs
        /// unrecorded, so a demo scene still says what happened.</summary>
        static void File(string text, Func<Outfit.FilingRuling> resolver)
        {
            var outfit = Gameplay.OutfitDirector.Instance;
            Say(outfit != null
                ? outfit.Filings.File(Stamp(outfit), text, resolver).Ruling
                : resolver().Ruling);
        }

        static Ambient.CityClock clock;

        static string Stamp(Gameplay.OutfitDirector outfit)
        {
            var day = outfit ? outfit.Campaign.Day : 1;
            if (clock == null)
                clock = UnityEngine.Object.FindAnyObjectByType<Ambient.CityClock>();
            if (clock == null)
                return "D" + day;
            return "D" + day + " " +
                   Mathf.FloorToInt(clock.Hour).ToString("00") + ":" +
                   Mathf.FloorToInt(clock.Hour % 1f * 60f).ToString("00");
        }
    }
}
