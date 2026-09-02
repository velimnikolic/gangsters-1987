using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Business;
using LivingCity.Entities;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Territory;
using RoadDemo;
using static LivingCity.UI.LedgerKit;
using BlockTenure = LivingCity.Outfit.DoorTenure;

namespace LivingCity.UI
{
    /// <summary>
    /// BLOCK FILE - the sheet that opens under a row of the block ledger and answers, in
    /// this order: do we actually hold this ground, what does it earn and cost, what
    /// trades on it, who is standing on it, and what can be done about any of that today.
    ///
    /// The whole point of the top half is that PAPER and STREET are two different facts.
    /// A lieutenant's name against a block he has no premise on is not a holding, it is a
    /// filing error, and the sheet says so in words rather than leaving two columns for
    /// the reader to compare.
    ///
    /// Every figure on it is the game's own. Money comes off Outfit.EconomyPrices - what
    /// a premise of that trade pays for peace, or nets when it is ours - never a number
    /// invented for a display. Heat is the fear ledger's police attention on that block
    /// against its own cap. What a block is short of is measured against the racket's
    /// published threshold for leaning on a street, not against a figure chosen here.
    /// Where the city cannot answer, the sheet prints nothing and says why.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        /// <summary>The block whose file is open under the ledger, if any.</summary>
        TerritoryBlockId blockCardId;

        /// <summary>The premise picked out of the model or the list.</summary>
        TerritoryBusinessId blockCardPick;

        bool blockCardAssignOpen;
        bool blockCardMenOpen;
        bool blockCardTradesOpen;

        /// <summary>How far round the block the reader has walked. Held on the sheet
        /// rather than on the view, so turning it survives the repaint a pick causes.
        /// There is no second angle: the block stands at the city's own street pitch.
        /// </summary>
        float blockCardYaw = -35f;

        BlockFilmView blockCardModel;
        TextMeshProUGUI blockCardHoverName;
        TextMeshProUGUI blockCardHoverLine;

        /// <summary>How many men are printed before the column offers the rest.</summary>
        const int BlockCardMenShown = 6;

        /// <summary>How many premises are listed before the column offers the rest.</summary>
        const int BlockCardTradeShown = 8;

        /// <summary>A key never stretches past this, so a column left alone on a row does
        /// not hand the reader a button the width of the sheet.</summary>
        const float BlockCardKeyMax = 300f;

        /// <summary>How far off the list the door's menu stands when it opens beside a
        /// row. How WIDE it opens is the menu's own business.</summary>
        const float BlockCardPopupGap = 12f;

        /// <summary>Where the picked shop's row sits down the card, so the menu can open
        /// LEVEL with it. Written while the list is printed, read when the popup is laid
        /// over the sheet afterwards.</summary>
        float blockCardPickY;

        /// <summary>
        /// One premise on the block. WHAT it is - name, trade, deed, standing, price - is
        /// the shared door reading (<see cref="DoorMenu.Door"/>), the same one the menu
        /// beside it and the turf map are drawn from; the sheet's own rows only read it
        /// back. What the film needs of it is this struct's own, because a photograph of
        /// a block is not something a map or a street card has.
        /// </summary>
        struct BlockTrade
        {
            /// <summary>The door itself, read off the city once.</summary>
            public DoorMenu.Door Menu;

            public TerritoryBusinessId Id => Menu.Id;
            public string Name => Menu.Name;
            public string Trade => Menu.Trade;
            public BlockTenure Tenure => Menu.Tenure;
            public string RivalName => Menu.RivalName;
            public TerritoryProtectionState Standing => Menu.Standing;
            public string Role => Menu.Role;
            public int RoleGang => Menu.RoleGang;
            public int TakePerDay => Menu.TakePerDay;
            public int BuyPrice => Menu.BuyPrice;

            /// <summary>The building's rise, measured off what is standing there. Zero
            /// when the block is not composed in front of the camera. Only the lens uses
            /// it, to know how far back to stand.</summary>
            public float Rise;

            /// <summary>The thing standing on the site, when the city has it composed.
            /// A click on any part of it is a click on this premise.</summary>
            public Transform View;

            public Vector3 Door;
            public bool HasDoor;
        }

        /// <summary>One of our men standing on the block right now.</summary>
        struct BlockHand
        {
            public int Id;
            public string Name;
            public string Duty;
            public bool Armed;
            public int Wage;

            /// <summary>What THIS quarter makes of him (ECON-006), in words - empty
            /// where the street has never heard of him. His name is his own and it
            /// counts only where he is standing, so the block file is where it belongs.
            /// </summary>
            public string Known;
        }

        readonly List<BlockTrade> blockCardTrades = new List<BlockTrade>();
        readonly List<BlockHand> blockCardHands = new List<BlockHand>();
        readonly List<TerritoryActorObservation> blockCardActors =
            new List<TerritoryActorObservation>();
        readonly List<RosterEquipment> blockCardKit = new List<RosterEquipment>();
        readonly List<BlockFilmView.Door> blockCardDoors =
            new List<BlockFilmView.Door>();

        /// <summary>The block's own rectangle in WORLD metres - where the lens goes.</summary>
        Rect blockCardGround;
        float blockCardGroundY;
        float blockCardRise;
        int blockCardRisen;
        int blockCardTake;
        int blockCardWages;
        int blockCardShort;
        float blockCardHeat;
        float blockCardHeatCap;

        // ------------------------------------------------------------------ opening

        /// <summary>Opens or closes the file under a ledger row. Opening a different
        /// block drops the premise, the men picked for a job and the menus with it - the
        /// file is about ONE block and never carries the last one's state over.</summary>
        void OpenBlockCard(TerritoryBlockId blockId)
        {
            blockCardId = blockCardId == blockId ? default : blockId;
            if (!blockCardId.IsValid)
                StopBlockFilm();
            blockCardPick = default;
            blockCardAssignOpen = false;
            blockCardMenOpen = false;
            blockCardTradesOpen = false;
            DoorMenu.Forget();
            organizationBlockMenu = default;
            dirty = true;
        }

        /// <summary>Opens the menu beside a premise, or shuts the one that is open. A
        /// different door is a different job, so the men picked at the last one do not
        /// follow the pointer down the column - the same rule the map keeps.</summary>
        void PickTrade(TerritoryBusinessId businessId)
        {
            blockCardPick = blockCardPick == businessId ? default : businessId;
            DoorMenu.Forget();
            dirty = true;
        }

        // ------------------------------------------------------------- what is true

        /// <summary>Reads the whole block off the city once per repaint: its rectangle,
        /// every premise on it with the deed and the money, and every man of ours
        /// standing there. Nothing below this line queries the city again.</summary>
        void ReadBlockFile()
        {
            blockCardTrades.Clear();
            blockCardHands.Clear();
            blockCardGround = new Rect();
            blockCardRisen = 0;
            blockCardTake = 0;
            blockCardWages = 0;
            blockCardShort = 0;
            blockCardHeat = 0f;
            blockCardHeatCap = 0f;

            var runtime = TerritoryRuntime.Instance;
            if (runtime == null || !blockCardId.IsValid)
                return;

            var geography = runtime.Geography;
            if (geography == null || !geography.TryGetBlock(blockCardId, out var block))
                return;

            var bounds = block.WorldBounds;
            blockCardGround = Rect.MinMaxRect(
                bounds.XMin, bounds.ZMin, bounds.XMax, bounds.ZMax);
            blockCardGroundY = 0f;
            blockCardRise = 0f;

            ReadBlockTrades(runtime);
            ReadBlockHands(runtime);
            ReadBlockPressure(runtime);
        }

        void ReadBlockTrades(TerritoryRuntime runtime)
        {
            var rows = CityBusinesses.All;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.CanonicalBlockId != blockCardId)
                    continue;

                // WHAT this door is - name, trade, deed, standing, price - is read by the
                // shared menu's own reader, so the sheet, the popup beside it and the
                // paper map can never answer a question about a shop differently.
                var trade = new BlockTrade { Menu = DoorMenu.Read(row) };

                var marker = row.Marker;
                if (marker == null)
                    BusinessViewBindings.TryGet(row.Id, out marker);

                trade.View = marker != null ? marker.transform : null;
                trade.Rise = RiseOf(marker);
                if (trade.Rise > 0f)
                    blockCardRisen++;

                if (CityBusinesses.TryApproachPoint(row.Id, out var door))
                {
                    trade.Door = door;
                    trade.HasDoor = true;
                }

                if (trade.HasDoor)
                    blockCardGroundY = trade.Door.y;
                blockCardRise = Mathf.Max(blockCardRise, trade.Rise);

                blockCardTake += trade.TakePerDay;
                blockCardTrades.Add(trade);
            }
        }

        /// <summary>The rise of the thing actually standing on the site. A block the
        /// camera has left is not composed, so there is nothing to measure and the model
        /// draws its footprint flat rather than inventing a storey count for it.</summary>
        static float RiseOf(BusinessMarker marker)
        {
            if (marker == null)
                return 0f;
            var renderers = marker.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return 0f;

            var found = false;
            Bounds total = default;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || !renderers[i].enabled)
                    continue;
                if (!found)
                {
                    total = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    total.Encapsulate(renderers[i].bounds);
                }
            }
            return found ? Mathf.Max(0f, total.size.y) : 0f;
        }

        void ReadBlockHands(TerritoryRuntime runtime)
        {
            blockCardActors.Clear();
            runtime.CollectActors(blockCardId, blockCardActors);
            var roster = director != null ? director.Roster : null;

            // The quarter this street belongs to - which is the ground a man's name is
            // kept on. Read once for the whole card rather than per man.
            var quarter = "";
            if (runtime.Geography != null &&
                runtime.Geography.TryGetBlock(blockCardId, out var here))
                quarter = here.NeighborhoodName;

            for (var i = 0; i < blockCardActors.Count; i++)
            {
                var actor = blockCardActors[i];
                if (!actor.GangId.IsValid || actor.GangId.Value != GangCatalog.PlayerGangId)
                    continue;
                if (!actor.CharacterId.IsValid)
                    continue;

                var member = roster != null ? roster.Find(actor.CharacterId.Value) : null;
                var hand = new BlockHand
                {
                    Id = actor.CharacterId.Value,
                    Name = member != null ? member.FullName : actor.DisplayName,
                    Duty = ActivityWord(actor.Activity),
                    Wage = member != null ? Outfit.Wages.WageFor(member) : 0,
                    Known = KnownHere(runtime, actor.CharacterId.Value, quarter),
                };

                if (roster != null && member != null)
                {
                    roster.HeldBy(member.Id, blockCardKit);
                    for (var k = 0; k < blockCardKit.Count && !hand.Armed; k++)
                        hand.Armed = RosterOps.IsWeapon(blockCardKit[k].Kind);
                }

                blockCardWages += hand.Wage;
                blockCardHands.Add(hand);
            }
        }

        /// <summary>What the street is under: the police attention this ground carries,
        /// and how many more men it would take before its shops would listen to us. Both
        /// are the simulation's own thresholds, read rather than chosen.</summary>
        void ReadBlockPressure(TerritoryRuntime runtime)
        {
            var fear = runtime.Fear;
            if (fear != null)
            {
                blockCardHeat = fear.PoliceAttention(blockCardId, runtime.GameHour);
                blockCardHeatCap = fear.Config != null ? fear.Config.PoliceAttentionCap : 0f;
            }

            var presence = runtime.Presence;
            var racket = runtime.Racket;
            if (presence == null || racket == null ||
                presence.Config == null || racket.Config == null)
                return;

            var standing = presence.TotalOf(
                blockCardId, new TerritoryGangId(GangCatalog.PlayerGangId));
            var wanted = racket.Config.RivalDemandPresence;
            var perMan = presence.Config.ContributionOf(
                TerritoryRank.Hood, TerritoryActorActivity.Stationed);
            if (perMan <= 0f || standing >= wanted)
                return;
            blockCardShort = Mathf.CeilToInt((wanted - standing) / perMan);
        }

        static string ActivityWord(TerritoryActorActivity activity) => activity switch
        {
            TerritoryActorActivity.Stationed => "standing it",
            TerritoryActorActivity.Moving => "on foot, crossing it",
            TerritoryActorActivity.Transit => "passing through",
            _ => "on the block",
        };

        // ------------------------------------------------------------------ the card

        // The deed's four colours and its four words belong to the door, not to this
        // sheet - the menu beside a row and the map paint them too.
        static readonly Color TenureOurs = DoorMenu.TenureOurs;
        static readonly Color TenurePaying = DoorMenu.TenurePaying;
        static readonly Color TenureRival = DoorMenu.TenureRival;
        static readonly Color TenureOpen = DoorMenu.TenureOpen;
        static readonly Color RivalInk = LedgerV2.Rgb2(0x60438d);

        // The film grades its clear colour into this warm brown. Carry that same brown
        // across the whole plate so the block does not sit on a separate dark rectangle.
        static readonly Color ModelPlate = LedgerV2.FilmPlate;
        static readonly Color ModelTip = LedgerV2.Rgb2(0x110c09);
        static readonly Color ModelCaption = LedgerV2.Rgb2(0x998c84);
        static readonly Color ModelHint = LedgerV2.Rgb2(0x72665e);
        static readonly Color ModelLegend = LedgerV2.Rgb2(0xa3958d);
        static readonly Color ModelChip = LedgerV2.Rgb2(0x564a43);

        static Color TenureColour(BlockTenure tenure) => DoorMenu.TenureColour(tenure);

        static string TenureWord(BlockTenure tenure) => DoorMenu.TenureWord(tenure);

        /// <summary>
        /// The file itself, opened INSIDE the block ledger directly under the row that
        /// was clicked. It carries no head of its own: the row over it already gives the
        /// block's name, its ward, who answers for it and what the street says, and a
        /// second copy of all four in a band would only push the block itself down the
        /// page. Clicking the same row again shuts the file.
        ///
        /// Answers the cursor below it, so the rest of the ledger is laid out by the same
        /// running y as every other row.
        /// </summary>
        float BuildBlockFile(float cursor)
        {
            if (!blockCardId.IsValid)
                return cursor;

            ReadBlockFile();

            var card = NewRect("Block file", organizationColumn);
            PlaceTopLeft(card, 0f, -cursor, organizationW, 1f);
            Fill(card, LedgerV2.Panel);

            var y = -6f;
            y -= BuildBlockModel(card, -y);
            y -= BuildBlockColumns(card, -y);

            var height = -y + 14f;
            card.sizeDelta = new Vector2(organizationW, height);
            // The open row carries a red mark down its left edge; the file continues it,
            // so the sheet shows at a glance which row this ground belongs to.
            Block("Open mark", card, 0f, 0f, 3f, height, LedgerV2.Red);
            return cursor + height + 10f;
        }

        // ------------------------------------------------------------------- the model

        /// <summary>
        /// The block itself, filmed. A second lens is put over the real ground the file
        /// is about and the frame it exposes is what the plate shows: the pavement, the
        /// buildings, the people on it and the hour of the day, as the street has them.
        /// Nothing here is a drawing of a block.
        ///
        /// The city puts distant ground away to keep its frame rate, so the file HOLDS
        /// this block up for as long as it is open and lets go the moment it closes.
        /// </summary>
        float BuildBlockModel(RectTransform card, float top)
        {
            // The block is the thing the file is FOR, so it gets the room: near half the
            // column's width in height rather than a quarter of it. At the sheet's own
            // floor that is a 430-unit plate instead of a 224-unit strip.
            var plateH = Mathf.Clamp(organizationW * 0.46f, 260f, 460f);
            var plate = NewRect("Block model", card);
            PlaceTopLeft(plate, 0f, -top, organizationW, plateH);
            Fill(plate, ModelPlate);

            if (blockCardGround.width <= 0f)
            {
                Line(plate, LedgerStyle.MonoItalic, 12f, ModelCaption,
                    18f, -(plateH * 0.5f - 10f), organizationW - 36f, 20f,
                    "This block is not on the canonical geography. Nothing to film.");
                return plateH;
            }

            RoadDemo.CityBlockRecycler.Hold(blockCardGround);

            // The film covers the WHOLE band. Its camera contains the block at the same
            // size the old fitted rectangle did, leaving only the empty stage around it.
            // That makes the camera's grade and vignette one continuous treatment across
            // the plate instead of a post-processed rectangle between two flat UI fills.
            var film = BlockFilm.Get();
            RectTransform view;
            if (blockCardModel != null)
            {
                // Ordinary page refreshes keep the live plate. Only its paper parent and
                // geometry move; the RawImage, texture and interaction state stay alive.
                view = blockCardModel.rectTransform;
                view.SetParent(plate, false);
                view.gameObject.name = "Model";
            }
            else
            {
                view = NewRect("Model", plate);
                view.gameObject.AddComponent<CanvasRenderer>();
                blockCardModel = view.gameObject.AddComponent<BlockFilmView>();
                blockCardModel.raycastTarget = true;
                blockCardModel.color = Color.white;
            }
            PlaceTopLeft(view, 0f, 0f, organizationW, plateH);
            view.SetAsFirstSibling();

            // Render exactly as many pixels as this rectangle shows on screen. The film
            // used to expose at twice this size, which made an open file pay for four
            // times as many pixels while the reader was trying to scroll the sheet.
            var scale = view.lossyScale;
            var pixelScaleX = Mathf.Abs(scale.x) <= 0f ? 1f : Mathf.Abs(scale.x);
            var pixelScaleY = Mathf.Abs(scale.y) <= 0f ? 1f : Mathf.Abs(scale.y);
            blockCardModel.texture = film.Reel(
                Mathf.RoundToInt(view.rect.width * pixelScaleX),
                Mathf.RoundToInt(view.rect.height * pixelScaleY));
            film.Look(blockCardGround, blockCardGroundY, blockCardYaw, blockCardRise);

            blockCardDoors.Clear();
            for (var i = 0; i < blockCardTrades.Count; i++)
            {
                var trade = blockCardTrades[i];
                if (!trade.HasDoor && trade.View == null)
                    continue;
                blockCardDoors.Add(new BlockFilmView.Door
                {
                    Key = i,
                    World = trade.HasDoor
                        ? trade.Door + Vector3.up * 2.2f
                        : trade.View.position + Vector3.up * 2.2f,
                    Ink = TenureColour(trade.Tenure),
                    Picked = blockCardPick.IsValid && trade.Id == blockCardPick,
                    View = trade.View,
                });
            }

            blockCardModel.Watch(film, blockCardDoors, blockCardYaw);
            blockCardModel.Turned = yaw =>
            {
                blockCardYaw = yaw;
                BlockFilm.Get().Look(blockCardGround, blockCardGroundY, yaw,
                    blockCardRise);
            };
            // Turning changes only the cached film frame. It is not a reason to rebuild
            // the paper around the model.
            blockCardModel.Settled = null;
            blockCardModel.Picked = key => PickTrade(
                key >= 0 && key < blockCardTrades.Count
                    ? blockCardTrades[key].Id
                    : default);
            blockCardModel.Hovered = ShowBlockModelNote;

            var marked = blockCardDoors.Count;
            Caps(plate, 18f, -14f, organizationW * 0.5f,
                "THE BLOCK · " + blockCardTrades.Count + " PREMISES",
                9f, ModelCaption, 12f).font = LedgerStyle.Mono;
            LedgerV2.Mono(plate, 18f, -30f, organizationW * 0.5f,
                marked == 0
                    ? "the city has no door on this ground to point at"
                    : blockCardRisen == 0
                        ? "drag to turn it · click a door · the street is coming up"
                        : "drag to turn it · click a door",
                9f, ModelHint, 1f);

            BuildBlockModelTurn(plate);
            BuildBlockModelNote(plate, plateH);
            BuildBlockModelKey(plate, plateH);
            return plateH;
        }

        /// <summary>Puts the second lens away and lets the city have the ground back.
        /// Called whenever the file stops standing open - a closed card, a shut book.
        /// </summary>
        void StopBlockFilm()
        {
            if (blockCardModel != null)
            {
                var old = blockCardModel;
                blockCardModel = null;
                Destroy(old.gameObject);
            }
            blockCardHoverName = null;
            blockCardHoverLine = null;
            RoadDemo.CityBlockRecycler.Release();
            BlockFilm.StopIfRunning();
        }

        /// <summary>Moves the persistent live plate out of the old block-file hierarchy
        /// before Organization destroys and rebuilds that hierarchy.</summary>
        void ParkBlockModelForRebuild()
        {
            if (blockCardModel == null || organizationContent == null)
                return;
            var view = blockCardModel.rectTransform;
            view.SetParent(organizationContent, false);
            view.SetAsFirstSibling();
        }

        /// <summary>If no rebuilt block file claimed the parked plate, the file really did
        /// close or disappear; dispose it instead of leaving the last block on the page.</summary>
        void FinishBlockModelRebuild()
        {
            if (blockCardModel != null &&
                blockCardModel.transform.parent == organizationContent)
                StopBlockFilm();
        }

        /// <summary>The two turns. There is no angle to pick any more: the block stands
        /// at the city's own isometric, and all the reader may do is walk round it. They
        /// turn the live view and never repaint the sheet - the model under the pointer
        /// must not be destroyed to change where it is standing.</summary>
        void BuildBlockModelTurn(RectTransform plate)
        {
            var words = new[] { "<< TURN", "TURN >>" };
            var steps = new[] { -45f, 45f };
            const float chipW = 74f;
            const float gap = 6f;
            var x = organizationW - 18f - chipW * words.Length - gap * (words.Length - 1);

            for (var i = 0; i < words.Length; i++)
            {
                var step = steps[i];
                var chip = NewRect("Turn " + i, plate);
                PlaceTopLeft(chip, x, -14f, chipW, 24f);
                Fill(chip, new Color(0f, 0f, 0f, 0f));
                Frame(chip, 1f, ModelChip);
                var chipFace = ClickSurface(chip);
                var word = Text("Label", chip, LedgerStyle.MonoBold, 9f, ModelLegend,
                    TextAlignmentOptions.Center);
                Stretch(word.rectTransform);
                word.characterSpacing = 4f;
                word.text = words[i];

                var button = chip.gameObject.AddComponent<Button>();
                button.targetGraphic = chipFace;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() =>
                {
                    if (blockCardModel == null)
                        return;
                    blockCardModel.Turn(blockCardModel.Yaw + step);
                });
                x += chipW + gap;
            }
        }

        void BuildBlockModelNote(RectTransform plate, float plateH)
        {
            var note = NewRect("Model note", plate);
            PlaceTopLeft(note, 18f, -(plateH - 92f), 320f, 44f);
            Fill(note, ModelTip);
            blockCardHoverName = LedgerV2.Name(note, 12f, -6f, 296f, "", 15f,
                LedgerV2.HeadCream);
            blockCardHoverLine = LedgerV2.Mono(note, 12f, -26f, 296f, "", 9.5f,
                ModelLegend, 1f);
            note.gameObject.SetActive(false);
        }

        void ShowBlockModelNote(int key)
        {
            if (blockCardHoverName == null || blockCardHoverLine == null)
                return;
            var note = blockCardHoverName.transform.parent;
            if (key < 0 || key >= blockCardTrades.Count)
            {
                note.gameObject.SetActive(false);
                return;
            }

            var trade = blockCardTrades[key];
            blockCardHoverName.text = trade.Name;
            blockCardHoverLine.text = trade.Trade + " · " + TenureWord(trade.Tenure) +
                (trade.TakePerDay > 0
                    ? " · " + LedgerText.Cash(trade.TakePerDay) + " a day"
                    : "") +
                (trade.Menu.Closure.Shut
                    ? " · " + trade.Menu.Closure.Note
                    : "");
            blockCardHoverLine.color = TenureColour(trade.Tenure);
            note.gameObject.SetActive(true);
        }

        void BuildBlockModelKey(RectTransform plate, float plateH)
        {
            var words = new[] { "OURS", "PAYS US", "ANOTHER HOUSE", "NOBODY LEANS" };
            var inks = new[] { TenureOurs, TenurePaying, TenureRival, TenureOpen };
            const float margin = 18f;
            var cellW = (organizationW - margin * 2f) / words.Length;
            for (var i = 0; i < words.Length; i++)
            {
                var x = margin + i * cellW;
                LedgerV2.StreetMark(plate, x, -(plateH - 26f), inks[i], 9f);
                LedgerV2.Mono(plate, x + 14f, -(plateH - 27f), cellW - 18f, words[i],
                    9f, ModelLegend, 2f);
            }
        }

        // ----------------------------------------------------------------- the columns

        /// <summary>
        /// The three readings, side by side where the sheet is wide enough and stacked
        /// where it is not. Nothing is cut off and no column is left as a stub: the sheet
        /// takes three columns over 1000 units, two over 660 and one below that.
        /// </summary>
        float BuildBlockColumns(RectTransform card, float top)
        {
            var columns = organizationW >= 1000f ? 3 : organizationW >= 660f ? 2 : 1;
            const float gutter = 22f;
            var width = (organizationW - 28f - gutter * (columns - 1)) / columns;

            var heights = new float[3];
            var x = new float[3];
            var y = new float[3];
            for (var i = 0; i < 3; i++)
            {
                var column = i % columns;
                var row = i / columns;
                x[i] = 14f + column * (width + gutter);
                y[i] = top + 14f;
                for (var back = i - columns; back >= 0; back -= columns)
                    y[i] += heights[back] + 20f;
                heights[i] = i == 0
                    ? BuildBlockReading(card, x[i], y[i], width)
                    : i == 1
                        ? BuildBlockTrades(card, x[i], y[i], width)
                        : BuildBlockHands(card, x[i], y[i], width);
            }

            // The card closes under the deepest column of the LAST row, whichever that
            // is - a two-column sheet ends under the pair, not under the first of them.
            var deepest = 0f;
            for (var i = 0; i < 3; i++)
                deepest = Mathf.Max(deepest, y[i] - top + heights[i]);

            // The picked door's menu is laid over the sheet LAST, so it stands over every
            // column rather than under one of them. The card only grows for it where the
            // menu is deeper than the whole spread.
            var pick = PickedTrade();
            if (pick >= 0)
            {
                var bottom = BuildTradePopup(card, x[1], width, top + 14f, top + deepest,
                    blockCardTrades[pick]);
                deepest = Mathf.Max(deepest, bottom - top);
            }
            return deepest + 14f;
        }

        /// <summary>What the block is worth and what it costs, and then the one sentence
        /// that says what that adds up to.</summary>
        float BuildBlockReading(RectTransform card, float x, float top, float width)
        {
            var y = top;
            y += Head(card, x, y, width, "WHAT IT COMES TO");

            var standing = blockCardHands.Count;
            var wanted = standing + blockCardShort;
            y += Gauge(card, x, y, width, "MEN STANDING ON IT",
                standing + (blockCardShort > 0 ? " / " + wanted : ""),
                wanted > 0 ? (float)standing / wanted : 1f,
                blockCardShort > 0 ? LedgerV2.Amber : LedgerV2.Green,
                blockCardShort > 0
                    ? blockCardShort + (blockCardShort == 1 ? " man" : " men") +
                      " short of leaning on this street"
                    : standing == 0
                        ? "nobody stands on this block"
                        : "heavy enough for the shops to listen");

            var takeCap = Mathf.Max(1, blockCardTake, blockCardWages);
            // True since the daily settlement (OutfitDirector.SettleBusinessDay): these
            // dollars land in the safe at midnight, on the same price table.
            y += Gauge(card, x, y, width, "TAKE A DAY",
                LedgerText.Cash(blockCardTake),
                (float)blockCardTake / takeCap,
                blockCardTake > 0 ? LedgerV2.Green : LedgerV2.Red,
                blockCardTake > 0
                    ? "deeds settle at midnight · dues bank when a round walks"
                    : "no door here pays us · it earns nothing");

            if (blockCardHeatCap > 0f)
            {
                var share = Mathf.Clamp01(blockCardHeat / blockCardHeatCap);
                y += Gauge(card, x, y, width, "HEAT ON THIS GROUND",
                    Mathf.RoundToInt(share * 10f) + " / 10", share,
                    share > 0.5f ? LedgerV2.Red : share > 0.2f ? LedgerV2.Amber : LedgerV2.Green,
                    share > 0.5f
                        ? "the precinct walks this block"
                        : "quiet enough for now");
            }

            y += 6f;
            Rule(card, x, -y, width, LedgerV2.Hair);
            y += 8f;

            var leaderId = organizationPaper.TryGetValue(blockCardId, out var id) ? id : -1;
            var leader = Leader(leaderId);
            y += Fact(card, x, y, width, "RESPONSIBLE · PAPER",
                leader.IsValid ? leader.Name : "NOBODY NAMED",
                leader.IsValid ? LedgerV2.PaperBlue : LedgerV2.Red);
            y += Fact(card, x, y, width, "PREMISES ON IT",
                blockCardTrades.Count.ToString(), LedgerV2.Ink);
            y += Fact(card, x, y, width, "WAGES STANDING HERE",
                LedgerText.Cash(blockCardWages) + " / day", LedgerV2.Ink);
            var net = blockCardTake - blockCardWages;
            y += Fact(card, x, y, width, "NET OFF THIS BLOCK",
                LedgerText.Cash(net) + " / day",
                net < 0 ? LedgerV2.Red : LedgerV2.Green);

            y += 8f;
            var verdict = BlockVerdict(leader, out var ink);
            var copy = LedgerV2.Copytext(card, x, -y, width, 66f, verdict, 13f, ink,
                italic: true);
            y += Mathf.Max(24f, copy.preferredHeight) + 4f;
            return y - top;
        }

        /// <summary>The block in one sentence a man can act on. Paper against street
        /// first, because a name on ground we do not hold is a different problem from
        /// ground we hold with nobody's name on it.</summary>
        string BlockVerdict(OrganizationPerson leader, out Color ink)
        {
            var control = ControlOf(blockCardId);
            var ours = control == BlockControl.Held || control == BlockControl.Contested;

            if (leader.IsValid && !ours)
            {
                ink = LedgerV2.Red;
                return leader.Name + " answers for ground we do not hold. " +
                       "Paper is not a block.";
            }
            if (!leader.IsValid && control == BlockControl.Held)
            {
                ink = LedgerV2.Red;
                return "We hold it and nobody answers for it. " +
                       "Name a man before somebody else does.";
            }
            if (control == BlockControl.Contested)
            {
                ink = LedgerV2.Amber;
                return "Another house is pushing on it. Men here earn less and bleed more.";
            }
            if (blockCardHands.Count == 0)
            {
                ink = ours ? LedgerV2.Red : LedgerV2.Muted;
                return ours
                    ? "Held on paper and on the deeds, and nobody stands on this block."
                    : "Not ours, nobody named, nobody standing. Nothing to answer for.";
            }
            if (blockCardShort > 0)
            {
                ink = LedgerV2.Amber;
                return "Held, but thin — " + blockCardShort +
                       (blockCardShort == 1 ? " man" : " men") +
                       " short of standing it properly.";
            }
            ink = LedgerV2.Green;
            return "Paper and street agree. Nothing to fix here today.";
        }

        // -------------------------------------------------------------- what trades

        float BuildBlockTrades(RectTransform card, float x, float top, float width)
        {
            var y = top;
            y += Head(card, x, y, width, "WHAT TRADES HERE");

            if (blockCardTrades.Count == 0)
            {
                Line(card, LedgerStyle.MonoItalic, 11.5f, LedgerV2.Muted, x, -y, width, 20f,
                    "Nothing trades on this block.");
                return y + 26f - top;
            }

            var shown = blockCardTradesOpen
                ? blockCardTrades.Count
                : Mathf.Min(blockCardTrades.Count, BlockCardTradeShown);
            blockCardPickY = y;
            for (var i = 0; i < shown; i++)
            {
                // Where the picked row lands is where its menu opens beside it.
                if (blockCardPick.IsValid && blockCardTrades[i].Id == blockCardPick)
                    blockCardPickY = y;
                y += TradeRow(card, x, y, width, blockCardTrades[i]);
            }

            if (blockCardTrades.Count > shown)
            {
                LedgerV2.Button(card, "SHOW ALL " + blockCardTrades.Count + " DOORS",
                    x, -y, Mathf.Min(width, 210f), 24f,
                    () => { blockCardTradesOpen = true; dirty = true; },
                    LedgerV2.Key.Ghost, 9f);
                y += 30f;
            }

            return y - top;
        }

        /// <summary>Which premise on this block is picked, or -1.</summary>
        int PickedTrade()
        {
            if (!blockCardPick.IsValid)
                return -1;
            for (var i = 0; i < blockCardTrades.Count; i++)
                if (blockCardTrades[i].Id == blockCardPick)
                    return i;
            return -1;
        }

        float TradeRow(RectTransform card, float x, float top, float width, BlockTrade trade)
        {
            const float rowH = 38f;
            var picked = blockCardPick.IsValid && trade.Id == blockCardPick;
            var id = trade.Id;

            var row = NewRect("Trade " + trade.Name, card);
            PlaceTopLeft(row, x, -top, width, rowH);
            var surface = ClickSurface(row);
            RowButton(row, surface, () => PickTrade(id));
            if (picked)
                Highlight(row, LedgerV2.Picked);
            Rule(row, 0f, -(rowH - 1f), width, LedgerV2.Hair);

            var ink = TenureColour(trade.Tenure);
            LedgerV2.StreetMark(row, 0f, -14f, ink, 10f);

            const float figureW = 92f;
            var badgeW = BadgeWidth(trade.Role);
            var textW = width - 16f - figureW - 8f - (badgeW > 0f ? badgeW + 6f : 0f);
            var name = LedgerV2.Name(row, 16f, -6f, textW, trade.Name, 13.5f, LedgerV2.Ink);
            name.overflowMode = TextOverflowModes.Ellipsis;
            if (badgeW > 0f)
                RoleBadge(row, 16f + textW + 6f, -5f, badgeW, trade);
            var under = LedgerV2.Mono(row, 16f, -22f, textW,
                trade.Trade.ToLowerInvariant() + " · " + TenureLine(trade) +
                (trade.Menu.Closure.Shut
                    ? " · " + trade.Menu.Closure.Note
                    : ""), 9f,
                LedgerV2.Label, 1f);
            under.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.Figure(row, width - figureW, -6f, figureW,
                trade.TakePerDay > 0 ? LedgerText.Cash(trade.TakePerDay) : "—", 12.5f,
                trade.TakePerDay > 0 ? LedgerV2.Ink : LedgerV2.Muted);
            LedgerV2.Mono(row, width - figureW, -23f, figureW, TenureWord(trade.Tenure),
                9f, ink, 3f, TextAlignmentOptions.MidlineRight);
            return rowH;
        }

        /// <summary>How wide the badge needs to be for its word. Two letters ("HQ") is
        /// a chip; a longer word gets the room it needs and no more, because the name
        /// beside it is the thing the reader is actually looking for.</summary>
        static float BadgeWidth(string role) =>
            string.IsNullOrEmpty(role) ? 0f : Mathf.Clamp(role.Length * 8f + 14f, 30f, 86f);

        /// <summary>
        /// The word painted on the pavement outside this door, printed in the book in
        /// the same colour it is painted in - so a glance down the column finds a
        /// house's own premises without reading a line of it.
        /// </summary>
        void RoleBadge(RectTransform row, float x, float y, float w, BlockTrade trade)
        {
            var tint = GangPalette.Of(trade.RoleGang);
            var chip = NewRect("Role " + trade.Role, row);
            PlaceTopLeft(chip, x, y, w, 16f);
            Fill(chip, tint);
            // Dark ink on a bright house, cream on a dark one - half the palette is a
            // deep colour and a badge nobody can read is worse than no badge.
            var lit = tint.r * 0.299f + tint.g * 0.587f + tint.b * 0.114f;
            Caps(chip, 0f, 0f, w, trade.Role, 9f,
                lit > 0.55f ? LedgerV2.Head : LedgerV2.HeadCream, 2f,
                TextAlignmentOptions.Center);
        }

        static string TenureLine(BlockTrade trade) => trade.Tenure switch
        {
            BlockTenure.Ours => "on our paper",
            BlockTenure.Paying => "pays us for peace",
            BlockTenure.Rival => (trade.RivalName ?? "another house") + " holds it",
            _ => "nobody leans on it",
        };

        /// <summary>
        /// The picked door's menu, opened BESIDE its row: level with the shop, to the left
        /// of the list, with a key that shuts it. It used to open inside the column under
        /// the row, which pushed every other door on the block half a page down the moment
        /// a reader looked at one of them.
        ///
        /// The panel itself is not this sheet's - it is <see cref="DoorMenu"/>, the same
        /// menu the turf map opens over a shop, so the two can never offer different rows
        /// or send different men. The sheet only says WHERE it opens.
        ///
        /// Answers the card-space y its bottom edge reaches.
        /// </summary>
        float BuildTradePopup(RectTransform card, float columnX, float columnW,
            float ceiling, float floor, BlockTrade trade)
        {
            var width = Mathf.Min(columnW, DoorMenu.MaxWidth);
            // Left of the list where the sheet is wide enough to hold it, right of it
            // where the list itself sits against the left edge, and over the list on a
            // one-column sheet, which has room for neither.
            var x = columnX - BlockCardPopupGap - width;
            if (x < 14f)
            {
                var right = columnX + columnW + BlockCardPopupGap;
                x = right + width <= organizationW - 14f ? right : 14f;
            }

            var panel = DoorMenu.Open(card, trade.Menu, width,
                () => dirty = true, CloseTradePopup,
                DoorDispatch.BlockResponsibility);
            var height = panel.sizeDelta.y;

            // Level with its row where the spread has the room, slid up where the row is
            // near the foot of the sheet, and never above the first column's head.
            var top = Mathf.Clamp(blockCardPickY, ceiling,
                Mathf.Max(ceiling, floor - height));
            PlaceTopLeft(panel, x, -top, width, height);
            return top + height;
        }

        /// <summary>Shuts the door's menu without touching the block's file. The men
        /// picked for a job go with it - a pick belongs to the door it was made at.</summary>
        void CloseTradePopup()
        {
            blockCardPick = default;
            DoorMenu.Forget();
            dirty = true;
        }

        // ------------------------------------------------------------- who stands here

        float BuildBlockHands(RectTransform card, float x, float top, float width)
        {
            var y = top;
            y += Head(card, x, y, width, "WHO STANDS HERE");

            if (blockCardHands.Count == 0)
            {
                Line(card, LedgerStyle.MonoItalic, 11.5f, LedgerV2.Red, x, -y, width, 20f,
                    "Nobody stands on this block.");
                y += 26f;
            }
            else
            {
                var shown = blockCardMenOpen
                    ? blockCardHands.Count
                    : Mathf.Min(blockCardHands.Count, BlockCardMenShown);
                for (var i = 0; i < shown; i++)
                    y += HandRow(card, x, y, width, blockCardHands[i]);

                if (blockCardHands.Count > shown)
                {
                    LedgerV2.Button(card,
                        "SHOW ALL " + blockCardHands.Count + " MEN",
                        x, -y, Mathf.Min(width, 200f), 24f,
                        () => { blockCardMenOpen = true; dirty = true; },
                        LedgerV2.Key.Ghost, 9f);
                    y += 30f;
                }
            }

            y += 10f;
            y += Head(card, x, y, width, "WHAT YOU CAN DO");

            var leaderId = organizationPaper.TryGetValue(blockCardId, out var id) ? id : -1;
            var leader = Leader(leaderId);
            var keyW = Mathf.Min(BlockCardKeyMax, width);

            LedgerV2.Button(card, "PUT MEN ON IT", x, -y, keyW, 28f,
                FileMenOntoBlock, LedgerV2.Key.Outline, 9.5f);
            y += 34f;

            LedgerV2.Button(card,
                leader.IsValid ? "CHANGE WHO ANSWERS" : "NAME SOMEONE",
                x, -y, keyW, 28f,
                () => { blockCardAssignOpen = !blockCardAssignOpen; dirty = true; },
                blockCardAssignOpen ? LedgerV2.Key.Dark : LedgerV2.Key.Outline, 9.5f);
            y += 34f;

            // Honest label: this opens the city-wide block PICKER (the same one the
            // ledger's own key opens) - it does not fly the camera to this block.
            var seeAll = LedgerV2.Button(card, "FIND A BLOCK ON THE MAP", x, -y, keyW, 28f,
                BeginBlockTargeting, LedgerV2.Key.Outline, 9.5f);
            SetActionEnabled(seeAll,
                MapTargeting.Available && TerritoryRuntime.Instance?.Commands != null);
            y += 34f;

            if (blockCardAssignOpen)
                y += BuildBlockCardAssign(card, x, y, Mathf.Min(width, 340f), leaderId);

            return y - top;
        }

        float HandRow(RectTransform card, float x, float top, float width, BlockHand hand)
        {
            const float rowH = 34f;
            var row = NewRect("Hand " + hand.Name, card);
            PlaceTopLeft(row, x, -top, width, rowH);
            Rule(row, 0f, -(rowH - 1f), width, LedgerV2.Hair);

            var roster = director != null ? director.Roster : null;
            var member = roster != null ? roster.Find(hand.Id) : null;
            Face(row, 0f, -3f, 22f, 28f, member,
                member != null ? Initials(member.FirstName, member.Surname) : "");
            Block("Arm", row, 0f, -31f, 22f, 2f,
                hand.Armed ? TenurePaying : LedgerV2.Red);

            const float pullW = 44f;
            var textW = width - 30f - pullW - 8f;
            var name = LedgerV2.Name(row, 30f, -4f, textW, hand.Name, 13f, LedgerV2.Ink);
            name.overflowMode = TextOverflowModes.Ellipsis;
            var duty = LedgerV2.Mono(row, 30f, -20f, textW,
                hand.Duty + (hand.Wage > 0 ? " · " + LedgerText.Cash(hand.Wage) + "/day" : "") +
                (hand.Known.Length > 0 ? " · " + hand.Known + " here" : ""),
                9f, LedgerV2.Label, 1f);
            duty.overflowMode = TextOverflowModes.Ellipsis;

            var manId = hand.Id;
            LedgerV2.Button(row, "PULL", width - pullW, -5f, pullW, 24f,
                () => FileHoodRecall(manId), LedgerV2.Key.Ghost, 9f);
            return rowH;
        }

        /// <summary>What this quarter makes of one man, in words, or nothing where it
        /// has never heard of him (ECON-006).</summary>
        static string KnownHere(TerritoryRuntime runtime, int characterId, string quarter)
        {
            if (runtime == null || runtime.Reputation == null || quarter.Length == 0)
                return "";
            var name = runtime.Reputation.Of(characterId, quarter, runtime.GameHour);
            return name < Territory.TerritoryReputationLedger.Faint
                ? ""
                : Territory.TerritoryReputationLedger.Word(name);
        }

        static string Initials(string first, string surname)
        {
            var a = string.IsNullOrEmpty(first) ? "" : first.Substring(0, 1);
            var b = string.IsNullOrEmpty(surname) ? "" : surname.Substring(0, 1);
            return (a + b).ToUpperInvariant();
        }

        float BuildBlockCardAssign(RectTransform card, float x, float top, float width,
            int leaderId)
        {
            var options = organizationLeaders.Count + (leaderId >= 0 ? 1 : 0);
            var height = 28f + options * 30f;
            var menu = NewRect("Block file menu", card);
            PlaceTopLeft(menu, x, -top, width, height);
            Fill(menu, LedgerV2.Head);
            Caps(menu, 12f, -8f, width - 24f, "WHO ANSWERS FOR IT", 9f,
                LedgerV2.HeadDim, 4f).font = LedgerStyle.Mono;

            var y = 28f;
            var query = director != null ? director.Organization : null;
            for (var i = 0; i < organizationLeaders.Count; i++)
            {
                var leader = organizationLeaders[i];
                var isBoss = leader.Rank == Rank.Boss;
                var target = leader.Id;
                var option = NewRect("Option " + leader.Name, menu);
                PlaceTopLeft(option, 0f, -y, width, 30f);
                Rule(option, 0f, 0f, width, LedgerV2.HeadDim);
                Line(option, LedgerStyle.Condensed, 13f,
                    isBoss ? LedgerV2.Amber : LedgerV2.HeadCream,
                    12f, -6f, width - 130f, 18f,
                    isBoss ? leader.Name + " · YOU" : leader.Name);

                if (query != null)
                {
                    var blocks = query.CapacityOf(leader.Id).Blocks;
                    var full = blocks.Current >= blocks.Maximum;
                    Caps(option, width - 118f, -7f, 106f,
                        blocks.Current + " / " + blocks.Maximum + (full ? " · FULL" : ""),
                        9f, full ? LedgerV2.Red : LedgerV2.HeadDim, 2f,
                        TextAlignmentOptions.MidlineRight);
                }

                RowButton(option, ClickSurface(option), () =>
                {
                    blockCardAssignOpen = false;
                    FileBlockResponsibility(blockCardId, target);
                });
                y += 30f;
            }

            if (leaderId >= 0)
            {
                var strike = NewRect("Option strike", menu);
                PlaceTopLeft(strike, 0f, -y, width, 30f);
                Rule(strike, 0f, 0f, width, LedgerV2.HeadDim);
                Line(strike, LedgerStyle.Condensed, 13f, LedgerV2.Red,
                    12f, -6f, width - 24f, 18f, "Nobody · strike the name off");
                RowButton(strike, ClickSurface(strike), () =>
                {
                    blockCardAssignOpen = false;
                    FileBlockRemoval(blockCardId, leaderId);
                });
            }

            return height + 8f;
        }

        // ------------------------------------------------- putting men on the block

        /// <summary>Marches the crew that answers for the block onto it. This is the
        /// physical act, not a filing about it: the men walk there, and Presence follows
        /// them.</summary>
        void FileMenOntoBlock()
        {
            var blockId = blockCardId;
            FileOrder("Men put on " + BlockName(blockId) + ".", () =>
            {
                var runtime = TerritoryRuntime.Instance;
                var roster = director != null ? director.Roster : null;
                if (runtime?.Commands == null || roster == null)
                    return Outfit.FilingRuling.Refuse(
                        "the territory command gateway is unavailable");

                // The lieutenant whose name is on the block goes first; failing that,
                // whoever the reader picked; failing that, the first crew on the books.
                Crew crew = null;
                if (organizationPaper.TryGetValue(blockId, out var leaderId))
                    for (var i = 0; i < roster.Crews.Count && crew == null; i++)
                        if (roster.Crews[i].LieutenantId == leaderId)
                            crew = roster.Crews[i];
                if (crew == null && DoorMenu.Picked.Count > 0)
                    crew = roster.CrewOf(DoorMenu.Picked[0]);
                if (crew == null && roster.Crews.Count > 0)
                    crew = roster.Crews[0];
                if (crew == null)
                    return Outfit.FilingRuling.Refuse("there is no crew to send");

                var lieutenant = roster.Find(crew.LieutenantId);
                if (!runtime.TryGetCrewNode(crew.Id, out var node))
                    return Outfit.FilingRuling.Refuse(
                        "that crew is not on the street to be moved");

                var result = runtime.Commands.Submit(
                    new OperateInBlockCommand(node, blockId));
                if (result.Status == TerritoryCommandStatus.Rejected)
                    return Outfit.FilingRuling.Refuse(result.Reason);
                return Outfit.FilingRuling.Grant(
                    (lieutenant != null ? lieutenant.Surname + "'s crew" : "the crew") +
                    " is walking onto it");
            });
        }

        // --------------------------------------------------------------------- pieces

        /// <summary>A column head: mono caps over a hairline.</summary>
        float Head(RectTransform card, float x, float top, float width, string label)
        {
            Caps(card, x, -top, width, label, 9f, LedgerV2.Label, 12f)
                .font = LedgerStyle.Mono;
            Rule(card, x, -(top + 14f), width, LedgerV2.Hair);
            return 24f;
        }

        /// <summary>A reading with a bar under it and the plain sentence that says what
        /// the reading MEANS. The sentence is the point - a ratio nobody can act on is
        /// not a readout.</summary>
        float Gauge(RectTransform card, float x, float top, float width, string label,
            string figure, float fraction, Color ink, string note)
        {
            LedgerV2.Mono(card, x, -top, width - 110f, label, 10f, LedgerV2.Muted, 5f);
            LedgerV2.Figure(card, x + width - 110f, -top, 110f, figure, 14f, ink);

            var trough = NewRect("Trough", card);
            PlaceTopLeft(trough, x, -(top + 18f), width, 7f);
            Fill(trough, LedgerV2.Trough);
            var fill = NewRect("Fill", trough);
            PlaceTopLeft(fill, 0f, 0f, width * Mathf.Clamp01(fraction), 7f);
            Fill(fill, ink);

            LedgerV2.Mono(card, x, -(top + 28f), width, note, 9.5f, LedgerV2.Muted, 0.5f);
            return 46f;
        }

        /// <summary>A labelled figure over a dotted leader - the sheet's own way of
        /// setting a fact.</summary>
        float Fact(RectTransform card, float x, float top, float width, string label,
            string value, Color ink)
        {
            LedgerV2.Mono(card, x, -top, width - 150f, label, 9.5f, LedgerV2.Label, 4f);
            LedgerV2.Figure(card, x + width - 150f, -top, 150f, value, 12f, ink);
            DottedRule(card, x, -(top + 16f), width, LedgerV2.Dotted);
            return 22f;
        }
    }
}
