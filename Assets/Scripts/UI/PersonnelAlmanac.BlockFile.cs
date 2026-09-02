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

        /// <summary>The man NAMED to walk this block's doors, and through him the crew
        /// that walks them. The design names men rather than crews because that is what
        /// a boss points at; the orders take the crew he belongs to.</summary>
        int blockCardWalkerId = -1;

        /// <summary>How many chips the band offers before it stops.</summary>
        const int BlockCardWalkersShown = 8;

        /// <summary>Whether the six orders under the block are open. Closed by default:
        /// the design shuts them behind one bar so the block and its figures are not
        /// three hundred units apart.</summary>
        bool blockCardOrdersOpen;
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

        /// <summary>The racket's own reading of this block, off the seam - the round,
        /// the money and the collector. Read once per repaint like everything else on
        /// this card; <see cref="blockRacketOk"/> is false where the seam has nothing to
        /// say about the block at all.</summary>
        BlockRacketView blockRacket;
        bool blockRacketOk;

        /// <summary>Where each door stands with us, by BusinessId. A door with no entry
        /// falls back to today's tenure line, which is what a scene with no racket
        /// behind it has always printed.</summary>
        readonly Dictionary<TerritoryBusinessId, DoorStanding> blockStandings =
            new Dictionary<TerritoryBusinessId, DoorStanding>();
        readonly List<DoorStanding> blockStandingScratch = new List<DoorStanding>();

        /// <summary>The key that is OUT and what the office said when it went - the one
        /// order key that has fired and not yet come back to idle.</summary>
        string blockRacketOutKey = "";
        string blockRacketOutLine = "";

        /// <summary>What the last refused order said, and how long it stands.</summary>
        string blockRacketSaid = "";
        float blockRacketSaidUntil;

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
            blockCardWalkerId = -1;
            DoorMenu.Forget();
            blocksMenu = default;
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
            ReadBlockRacket();
        }

        /// <summary>
        /// What the racket makes of this block, off the seam and nowhere else. The page
        /// composes none of it: the standings arrive as finished lines in the
        /// simulation's own vocabulary, and the figures arrive derived.
        ///
        /// The doors are then SORTED by what needs answering - red, then amber, then the
        /// rest, and by name inside each - because a column of thirty doors in whatever
        /// order the geography happened to hand them over is a column nobody reads.
        /// </summary>
        void ReadBlockRacket()
        {
            blockStandings.Clear();
            blockRacketOk = false;
            blockRacket = default;
            if (!blockCardId.IsValid)
                return;

            var source = BlockRacketSeam.SourceOrStub;
            blockRacketOk = source.TryGetBlock(blockCardId, out blockRacket);

            // A key stands OUT until the seam says the world has moved under it. That is
            // the seam's own version and nothing else: a repaint is not an answer.
            if (blockRacketOutKey.Length > 0 && source.Version != blockRacketOutVersion)
            {
                blockRacketOutKey = "";
                blockRacketOutLine = "";
            }

            source.CollectDoorStandings(blockCardId, blockStandingScratch);
            for (var i = 0; i < blockStandingScratch.Count; i++)
                blockStandings[blockStandingScratch[i].BusinessId] = blockStandingScratch[i];

            // The stub keeps no door list of its own - it cannot, it has never seen this
            // city - so it answers per door instead, in a rotation that puts every state
            // on any block with nine doors or more.
            if (BlockRacketSeam.IsStub)
                for (var i = 0; i < blockCardTrades.Count; i++)
                    blockStandings[blockCardTrades[i].Id] =
                        StubBlockRacket.Instance.StandingFor(blockCardTrades[i].Id, i);

            blockCardTrades.Sort((a, b) =>
            {
                var sa = SeverityOf(a.Id);
                var sb = SeverityOf(b.Id);
                return sa != sb
                    ? sb.CompareTo(sa)
                    : string.CompareOrdinal(a.Name, b.Name);
            });
        }

        int SeverityOf(TerritoryBusinessId id) =>
            blockStandings.TryGetValue(id, out var standing) ? standing.Severity : 0;

        /// <summary>How many of this block's doors are asking for an answer. Counted off
        /// the standings the page actually has, so it can never disagree with the column
        /// under it.</summary>
        int DoorsNeedingAnswer()
        {
            var needing = 0;
            for (var i = 0; i < blockCardTrades.Count; i++)
                if (SeverityOf(blockCardTrades[i].Id) > 0)
                    needing++;
            return needing;
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

            var card = NewRect("Block file", blocksColumn);
            PlaceTopLeft(card, 0f, -cursor, blocksW, 1f);
            Fill(card, LedgerV2.Panel);

            var y = 0f;
            y -= BuildBlockHeader(card, -y);
            y -= BuildBlockModel(card, -y);
            y -= BuildBlockOrders(card, -y);
            y -= BuildBlockColumns(card, -y);

            var height = -y;
            card.sizeDelta = new Vector2(blocksW, height);
            return cursor + height + 10f;
        }

        // ------------------------------------------------------------------ the head

        /// <summary>The dark band the file opens with: what the sheet is (BLOCK FILE),
        /// which block, its ward, what the street says about it, and the way out. The
        /// design's own head, and the reason the card reads as a document rather than as
        /// a photograph with figures under it.</summary>
        const float BlockHeadH = 67f;

        /// <summary>The filmed block's own height, from the design. Fixed, not a share
        /// of the column.</summary>
        const float BlockPlateH = 330f;

        float BuildBlockHeader(RectTransform card, float top)
        {
            var band = NewRect("Block head", card);
            PlaceTopLeft(band, 0f, -top, blocksW, BlockHeadH);
            Fill(band, LedgerV2.Head);

            var control = ControlOf(blockCardId);
            var word = ControlWord(control);
            var ink = ControlColour(control);
            var stateW = word.Length * 7.2f + 30f;
            var titleW = blocksW - 32f - stateW - 24f - 12f;

            Caps(band, 16f, -10f, titleW, "BLOCK FILE", 8.5f, LedgerV2.HeadDim, 16f)
                .font = LedgerStyle.Mono;
            Line(band, LedgerStyle.Condensed, 19f, LedgerV2.HeadCream,
                16f, -21f, titleW, 24f, BlockName(blockCardId))
                .overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Mono(band, 16f, -45f, titleW, NeighborhoodOf(blockCardId), 10f,
                LedgerV2.HeadDim, 1f).overflowMode = TextOverflowModes.Ellipsis;

            var stateX = blocksW - 16f - 24f - 12f - stateW;
            Block("Street", band, stateX, -14f, 9f, 9f, ink);
            Caps(band, stateX + 16f, -13f, stateW - 16f, word, 11f, ink, 4f)
                .font = LedgerStyle.MonoBold;

            // The way out is the same click the row is: a file is shut by the block it
            // belongs to, and this is that block.
            var shut = NewRect("Shut", band);
            PlaceTopLeft(shut, blocksW - 16f - 24f, -11f, 24f, 24f);
            Fill(shut, new Color(0f, 0f, 0f, 0f));
            Frame(shut, 1f, LedgerV2.HeadDim);
            var open = blockCardId;
            RowButton(shut, ClickSurface(shut), () => OpenBlockCard(open));
            Caps(shut, 0f, -7f, 24f, "X", 10f, LedgerV2.HeadCream, 0f,
                TextAlignmentOptions.Center).font = LedgerStyle.MonoBold;

            return BlockHeadH;
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
            // The design's own plate: 330 units, fixed. It is not a share of the column
            // - a block filmed taller on a wide window and shorter on a narrow one is a
            // different picture of the same ground on two screens.
            const float plateH = BlockPlateH;
            var plate = NewRect("Block model", card);
            PlaceTopLeft(plate, 0f, -top, blocksW, plateH);
            Fill(plate, ModelPlate);

            if (blockCardGround.width <= 0f)
            {
                Line(plate, LedgerStyle.MonoItalic, 12f, ModelCaption,
                    18f, -(plateH * 0.5f - 10f), blocksW - 36f, 20f,
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
            PlaceTopLeft(view, 0f, 0f, blocksW, plateH);
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
            Caps(plate, 16f, -14f, blocksW * 0.5f,
                "THE BLOCK · " + blockCardTrades.Count + " PREMISES",
                9.5f, ModelCaption, 14f).font = LedgerStyle.Mono;
            LedgerV2.Mono(plate, 16f, -31f, blocksW * 0.5f,
                marked == 0
                    ? "the city has no door on this ground to point at"
                    : blockCardRisen == 0
                        ? "drag to turn it · click a door · the street is coming up"
                        : "drag to turn it · click a door",
                9.5f, ModelHint, 1f);

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
            if (blockCardModel == null || blocksContent == null)
                return;
            var view = blockCardModel.rectTransform;
            view.SetParent(blocksContent, false);
            view.SetAsFirstSibling();
        }

        /// <summary>If no rebuilt block file claimed the parked plate, the file really did
        /// close or disappear; dispose it instead of leaving the last block on the page.</summary>
        void FinishBlockModelRebuild()
        {
            if (blockCardModel != null &&
                blockCardModel.transform.parent == blocksContent)
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
            const float gap = 7f;
            var x = blocksW - 16f - chipW * words.Length - gap * (words.Length - 1);

            for (var i = 0; i < words.Length; i++)
            {
                var step = steps[i];
                var chip = NewRect("Turn " + i, plate);
                PlaceTopLeft(chip, x, -14f, chipW, 23f);
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
            var cellW = (blocksW - margin * 2f) / words.Length;
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
        /// TWO columns under the block, and always the same two: the ARRANGEMENT on the
        /// left - what it is worth, what it costs, and the sentence that says what that
        /// comes to - and on the right the DOORS with the MEN under them. They are the
        /// two questions a boss asks of a block, and they never swap sides.
        ///
        /// A column narrower than the design's own minimum drops the pair into one
        /// measure rather than cutting either of them in half.
        /// </summary>
        float BuildBlockColumns(RectTransform card, float top)
        {
            const float pad = 18f;
            const float lip = 16f;
            var split = blocksW - pad * 2f >= 620f;
            var width = split ? (blocksW - pad * 3f) * 0.5f : blocksW - pad * 2f;

            var leftX = pad;
            var rightX = split ? pad * 2f + width : pad;
            var leftY = top + lip;
            var rightY = split ? top + lip : 0f;

            var left = BuildBlockReading(card, leftX, leftY, width);
            if (!split)
                rightY = leftY + left + 20f;

            var right = BuildBlockTrades(card, rightX, rightY, width);
            right += 18f;
            right += BuildBlockHands(card, rightX, rightY + right, width);

            // The hairline between the pair, drawn to the deeper of the two so it closes
            // the block rather than stopping in the middle of it.
            var deepest = Mathf.Max(leftY - top + left, rightY - top + right);
            if (split)
                Block("Column rule", card, pad + width + pad * 0.5f, -(top + lip),
                    1f, deepest - lip, LedgerV2.Hair);

            // The picked door's menu is laid over the sheet LAST, so it stands over both
            // columns rather than under one of them. The card only grows for it where the
            // menu is deeper than the whole spread.
            var pick = PickedTrade();
            if (pick >= 0)
            {
                var bottom = BuildTradePopup(card, rightX, width, top + lip, top + deepest,
                    blockCardTrades[pick]);
                deepest = Mathf.Max(deepest, bottom - top);
            }
            return deepest + lip;
        }

        /// <summary>What the block is worth and what it costs, and then the one sentence
        /// that says what that adds up to.</summary>
        float BuildBlockReading(RectTransform card, float x, float top, float width)
        {
            var y = top;
            y += Head(card, x, y, width, "THE ARRANGEMENT");

            // The arrangement itself, first and in one line: who answers for this ground,
            // the day his men walk it and how he runs them. Everything under it is the
            // arithmetic of that sentence.
            var paperId = organizationPaper.TryGetValue(blockCardId, out var paper)
                ? paper
                : -1;
            var responsible = Leader(paperId);
            LedgerV2.Mono(card, x, -y, width, ResponsibleLine(responsible), 11.5f,
                responsible.IsValid ? LedgerV2.Ink : LedgerV2.Red, 1f);
            y += 18f;
            if (blockRacketOk && responsible.IsValid && blockRacket.CollectsWeekday < 0)
            {
                LedgerV2.Mono(card, x, -y, width,
                    "nobody here carries the bag - a door's take is not money until a " +
                    "man walks it home", 10f, LedgerV2.Red, 0.5f);
                y += 16f;
            }
            if (blockRacketOk && blockRacket.LastRoundDay > 0)
            {
                LedgerV2.Mono(card, x, -y, width,
                    "last round day " + blockRacket.LastRoundDay + " · banked " +
                    LedgerText.Cash(blockRacket.LastRoundBanked) + " · " +
                    blockRacket.LastRoundShort + " short", 10f, LedgerV2.Muted, 0.5f);
                y += 16f;
            }
            y += 6f;

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
            y += Gauge(card, x, y, width, "TAKE A DAY",
                LedgerText.Cash(blockCardTake),
                (float)blockCardTake / takeCap,
                blockCardTake > 0 ? LedgerV2.Green : LedgerV2.Red,
                blockCardTake > 0
                    ? "what the doors here are worth a day, standing"
                    : "no door here pays us · it earns nothing");

            if (blockRacketOk && blockRacket.RoundOut && blockRacket.RoundStops > 0)
                y += Gauge(card, x, y, width, "ROUND OUT",
                    blockRacket.RoundCursor + " / " + blockRacket.RoundStops,
                    (float)blockRacket.RoundCursor / blockRacket.RoundStops,
                    LedgerV2.Amber,
                    blockRacket.RoundCursor + " of " + blockRacket.RoundStops +
                    " doors · " + LedgerText.Cash(blockRacket.RoundCarried) +
                    " in the bag · " + blockRacket.RoundCollectorName);

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

            // MONEY WALKS, and the three figures are set in the order it travels so
            // nobody can read a door's take as money in the safe: owed at the doors,
            // carried in the bag, banked. IN THE BAG is a dash unless there is a bag.
            if (blockRacketOk)
            {
                y += Fact(card, x, y, width, "OWED AT THE DOORS",
                    LedgerText.Cash(blockRacket.Owed),
                    blockRacket.Owed > 0 ? LedgerV2.Red : LedgerV2.Muted);
                y += Fact(card, x, y, width, "IN THE BAG",
                    blockRacket.RoundOut ? LedgerText.Cash(blockRacket.InTheBag) : "—",
                    blockRacket.RoundOut ? LedgerV2.Amber : LedgerV2.Muted);
                y += Fact(card, x, y, width, "BANKED THIS WEEK",
                    LedgerText.Cash(blockRacket.BankedThisWeek),
                    blockRacket.BankedThisWeek > 0 ? LedgerV2.Green : LedgerV2.Muted);

                // A block with a lieutenant on it and nobody carrying the bag earns
                // nothing at all, and the sheet says so in red rather than printing a
                // blank where a weekday should be.
                var noCollector = responsible.IsValid && blockRacket.CollectsWeekday < 0;
                y += Fact(card, x, y, width, "COLLECTS",
                    noCollector ? "NOBODY ON THE BAG"
                        : blockRacket.CollectsWord.Length > 0
                            ? blockRacket.CollectsWord
                            : "nothing to collect yet",
                    noCollector ? LedgerV2.Red : LedgerV2.Ink);
            }

            y += Fact(card, x, y, width, "PREMISES ON IT",
                blockCardTrades.Count.ToString(), LedgerV2.Ink);
            y += Fact(card, x, y, width, "WAGES STANDING HERE",
                LedgerText.Cash(blockCardWages) + " / day", LedgerV2.Ink);

            // NET is banked against wages, both over the SAME week - not a day's take
            // against a day's wages, because the take is not money until it is banked.
            var wagesWeek = blockCardWages * 7;
            var net = (blockRacketOk ? blockRacket.BankedThisWeek : 0) - wagesWeek;
            y += Fact(card, x, y, width, "NET OFF THIS BLOCK",
                LedgerText.Cash(net) + " / week",
                net < 0 ? LedgerV2.Red : LedgerV2.Green);

            y += 8f;
            var verdict = BlockVerdict(responsible, out var ink);
            var needing = DoorsNeedingAnswer();
            if (needing == 1)
                verdict += " One door needs an answer.";
            else if (needing > 1)
                verdict += " " + needing + " doors need an answer.";
            var copy = LedgerV2.Copytext(card, x, -y, width, 80f, verdict, 13f, ink,
                italic: true);
            y += Mathf.Max(24f, copy.preferredHeight) + 4f;

            // Invented money must never be read as the city's. The stub says so on the
            // card itself, in the same breath as the sentence above it.
            if (BlockRacketSeam.IsStub)
            {
                LedgerV2.Mono(card, x, -y, width,
                    "(stub figures · no racket is running in this scene)", 9.5f,
                    LedgerV2.Muted, 0.5f);
                y += 16f;
            }
            return y - top;
        }

        /// <summary>Who answers for the block, and on what terms - the name, the day his
        /// men walk it and how he runs them. One line, because it is one fact.</summary>
        string ResponsibleLine(OrganizationPerson leader)
        {
            if (!leader.IsValid)
                return "NOBODY NAMED";
            if (!blockRacketOk)
                return leader.Name;
            if (blockRacket.CollectsWeekday < 0)
                return leader.Name + " · nobody on the bag";
            return leader.Name + " · " + blockRacket.CollectsWord.ToLowerInvariant() +
                   " · " + blockRacket.Policy.ToString().ToUpperInvariant();
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
            var needing = DoorsNeedingAnswer();
            y += Head(card, x, y, width, "WHAT TRADES HERE",
                needing > 0
                    ? needing + (needing == 1 ? " DOOR NEEDS AN ANSWER"
                        : " DOORS NEED AN ANSWER")
                    : "",
                LedgerV2.Red);

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
            const float rowH = 39f;
            var picked = blockCardPick.IsValid && trade.Id == blockCardPick;
            var id = trade.Id;

            var row = NewRect("Trade " + trade.Name, card);
            PlaceTopLeft(row, x, -top, width, rowH);
            var surface = ClickSurface(row);
            RowButton(row, surface, () => PickTrade(id));
            if (picked)
                Highlight(row, LedgerV2.Picked);
            Rule(row, 0f, -(rowH - 1f), width, LedgerV2.Hair);

            // The square takes the STANDING's ink where the racket has one, and the
            // tenure's where it does not: a door that refused us is red whoever holds
            // the deed, because the refusal is the thing the reader has to act on.
            var hasStanding = blockStandings.TryGetValue(trade.Id, out var standing);
            var ink = hasStanding
                ? StandingInk(standing.Kind)
                : TenureColour(trade.Tenure);
            LedgerV2.StreetMark(row, 0f, -15f, ink, 10f);

            const float figureW = 92f;
            const float chevronW = 14f;
            var badgeW = BadgeWidth(trade.Role);
            var textW = width - 19f - figureW - chevronW - 9f -
                        (badgeW > 0f ? badgeW + 6f : 0f);
            var name = LedgerV2.Name(row, 19f, -6f, textW, trade.Name, 13.5f, LedgerV2.Ink);
            name.overflowMode = TextOverflowModes.Ellipsis;
            if (badgeW > 0f)
                RoleBadge(row, 19f + textW + 6f, -5f, badgeW, trade);

            // The second line is WHERE THIS DOOR STANDS WITH US, in the simulation's own
            // words. Only where the racket has nothing to say about it does the row fall
            // back to the tenure sentence it printed before there was a racket at all.
            var under = LedgerV2.Mono(row, 19f, -23f, textW,
                hasStanding
                    ? standing.Line +
                      (trade.Menu.Closure.Shut ? " · " + trade.Menu.Closure.Note : "")
                    : trade.Trade.ToLowerInvariant() + " · " + TenureLine(trade) +
                      (trade.Menu.Closure.Shut ? " · " + trade.Menu.Closure.Note : ""),
                9f, hasStanding && standing.Severity > 0 ? ink : LedgerV2.Label, 1f);
            under.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.Figure(row, width - figureW - chevronW, -5f, figureW,
                trade.TakePerDay > 0 ? LedgerText.Cash(trade.TakePerDay) : "—", 12.5f,
                trade.TakePerDay > 0 ? LedgerV2.Ink : LedgerV2.Muted);
            LedgerV2.Mono(row, width - figureW - chevronW, -23f, figureW,
                hasStanding ? StandingWord(standing.Kind) : TenureWord(trade.Tenure),
                9f, ink, 6f, TextAlignmentOptions.MidlineRight);

            // The row is a control and has to look like one: the door's menu opens beside
            // it, and nothing else on this card says so.
            LedgerV2.Mono(row, width - chevronW, -13f, chevronW, "›", 11f,
                picked ? LedgerV2.Ink : LedgerV2.Label, 0f,
                TextAlignmentOptions.MidlineRight);
            return rowH;
        }

        /// <summary>The ink a standing is read by, before a word of it is read. Red is
        /// what has to be answered today, amber what will have to be answered soon.
        /// </summary>
        static Color StandingInk(DoorStandingKind kind) => kind switch
        {
            DoorStandingKind.Refused => LedgerV2.Red,
            DoorStandingKind.Late => LedgerV2.Red,
            DoorStandingKind.Wavering => LedgerV2.Amber,
            DoorStandingKind.Short => LedgerV2.Amber,
            DoorStandingKind.Paying => TenurePaying,
            DoorStandingKind.Rival => TenureRival,
            _ => LedgerV2.Label,
        };

        /// <summary>The standing in the one word that stands under the figure.</summary>
        static string StandingWord(DoorStandingKind kind) => kind switch
        {
            DoorStandingKind.Refused => "REFUSED",
            DoorStandingKind.Late => "LATE",
            DoorStandingKind.Wavering => "WAVERING",
            DoorStandingKind.Short => "SHORT",
            DoorStandingKind.Paying => "PAYS US",
            DoorStandingKind.Rival => "THEIRS",
            DoorStandingKind.Unvisited => "UNVISITED",
            DoorStandingKind.Shut => "SHUT",
            _ => "ON THE BOOKS",
        };

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
                x = right + width <= blocksW - 14f ? right : 14f;
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
            var source = BlockRacketSeam.SourceOrStub;
            var carrying = 0;
            for (var i = 0; i < blockCardHands.Count; i++)
                if (source.IsCollector(blockCardHands[i].Id))
                    carrying++;
            // ONE line, the design's: the heading, the headcount and how many of them
            // carry the bag, all in the same mono caps.
            y += Head(card, x, y, width,
                "WHO STANDS HERE · " + blockCardHands.Count +
                (blockCardHands.Count == 1 ? " MAN · " : " MEN · ") +
                (carrying == 0 ? "NO COLLECTOR"
                    : carrying == 1 ? "1 COLLECTOR"
                    : carrying + " COLLECTORS"));

            if (blockCardHands.Count == 0)
            {
                LedgerV2.Mono(card, x, -(y + 6f), width, "Nobody stands on this block.",
                    11.5f, LedgerV2.Red, 0.5f);
                y += 26f;
            }
            else
            {
                var shown = blockCardMenOpen
                    ? blockCardHands.Count
                    : Mathf.Min(blockCardHands.Count, BlockCardMenShown);

                // Two men to a line where the column can hold them, which is the design's
                // own grid: a roll of six down one measure is a column of air beside it.
                const float gutter = 16f;
                var men = width >= 376f ? 2 : 1;
                var cell = (width - gutter * (men - 1)) / men;
                for (var i = 0; i < shown; i++)
                    HandRow(card, x + i % men * (cell + gutter),
                        y + i / men * HandRowH, cell, blockCardHands[i]);
                y += (shown + men - 1) / men * HandRowH;

                if (blockCardHands.Count > shown)
                {
                    LedgerV2.Button(card,
                        "SHOW ALL " + blockCardHands.Count + " MEN →",
                        x, -(y + 7f), Mathf.Min(width, 200f), 22f,
                        () => { blockCardMenOpen = true; dirty = true; },
                        LedgerV2.Key.Ghost, 10f);
                    y += 29f;
                }
            }

            return y - top;
        }

        // ------------------------------------------------------- what you can do

        /// <summary>
        /// THE ORDERS BAND, straight under the filmed block: one dark bar that opens the
        /// six things that can be done to this ground, the policy his crew runs them by,
        /// and the menu that changes whose paper it is.
        ///
        /// It is a DROPDOWN and not six keys down the column because six keys with a line
        /// of explanation each is three hundred units of page between the block and the
        /// figures about it - the design closes it and says how many orders are inside.
        /// </summary>
        float BuildBlockOrders(RectTransform card, float top)
        {
            const float x = 18f;
            var width = blocksW - 36f;
            var y = top + 14f;
            var barW = Mathf.Min(width, 470f);

            var bar = NewRect("Orders bar", card);
            PlaceTopLeft(bar, x, -y, barW, 31f);
            Fill(bar, LedgerV2.Head);
            RowButton(bar, ClickSurface(bar),
                () => { blockCardOrdersOpen = !blockCardOrdersOpen; dirty = true; });
            Caps(bar, 13f, -10f, barW * 0.6f,
                blockCardOrdersOpen ? "WHAT YOU CAN DO ▴" : "WHAT YOU CAN DO ▾",
                10.5f, LedgerV2.HeadCream, 8f).font = LedgerStyle.MonoBold;
            if (!blockCardOrdersOpen)
                Caps(bar, barW * 0.4f, -11f, barW * 0.6f - 13f,
                    "SIX ORDERS ON THIS BLOCK", 9f, LedgerV2.HeadDim, 4f,
                    TextAlignmentOptions.MidlineRight).font = LedgerStyle.Mono;
            y += 31f;

            var leaderId = organizationPaper.TryGetValue(blockCardId, out var id) ? id : -1;
            var leader = Leader(leaderId);
            // The crew that walks these doors: the man the boss named, or failing that
            // the lieutenant whose paper the block is on.
            var crewId = WalkingCrewId();
            var block = blockCardId;
            var source = BlockRacketSeam.SourceOrStub;
            var actions = BlockRacketSeam.ActionsOrStub;

            if (blockCardOrdersOpen)
            {
                var panel = NewRect("Orders", card);
                PlaceTopLeft(panel, x, -y, barW, 1f);
                Fill(panel, LedgerV2.Money);
                var inner = 0f;

                // The LABEL and the NOTE come off the shared order table, never a
                // literal here: the door menu prints the same rows, and two surfaces
                // that word one order differently are two surfaces describing two
                // different orders (TerritoryRacketOrders).
                inner += OrderRow(panel, barW, inner,
                    LivingCity.Territory.TerritoryRacketOrders.ShakeDownLabel,
                    LivingCity.Territory.TerritoryRacketOrders.ShakeDownNote,
                    source.Refusal("shakedown", crewId, block),
                    () => FireRacketOrder("shakedown",
                        () => actions.ShakeDown(crewId, block)));

                inner += OrderRow(panel, barW, inner, "SEND THE ROUND NOW",
                    LivingCity.Territory.TerritoryRacketOrders.RoundNote,
                    source.Refusal("round", crewId, block),
                    () => FireRacketOrder("round",
                        () => actions.SendRound(crewId, block)));

                inner += OrderRow(panel, barW, inner,
                    LivingCity.Territory.TerritoryRacketOrders.LeanLabel,
                    LivingCity.Territory.TerritoryRacketOrders.LeanNote,
                    source.Refusal("lean", crewId, block),
                    () => FireRacketOrder("lean",
                        () => actions.LeanOnHoldouts(crewId, block)));

                inner += OrderRow(panel, barW, inner, "PUT A MAN ON IT",
                    "one more of ours stands on this block · presence, not paper",
                    "", FileMenOntoBlock);

                inner += OrderRow(panel, barW, inner,
                    leader.IsValid ? "CHANGE WHO ANSWERS" : "NAME SOMEONE",
                    "name the lieutenant whose paper this block is on · he collects and " +
                    "answers for it",
                    "",
                    () => { blockCardAssignOpen = !blockCardAssignOpen; dirty = true; });

                // Honest label: this opens the city-wide block PICKER (the same one the
                // ledger's own key opens) - it does not fly the camera to this block.
                inner += OrderRow(panel, barW, inner, "MARK IT ON THE MAP",
                    "find this block on the turf map",
                    MapTargeting.Available && TerritoryRuntime.Instance?.Commands != null
                        ? ""
                        : "no map in this scene to pick one off",
                    BeginBlockTargeting);

                panel.sizeDelta = new Vector2(barW, inner);
                y += inner;
            }

            if (blockRacketOk && blockRacket.HasResponsible)
                y += BuildPolicyBar(card, x, y + 16f, Mathf.Min(width, 400f), crewId) + 16f;

            y += BuildWalkers(card, x, y + 16f, width) + 16f;

            if (blockCardAssignOpen)
                y += BuildBlockCardAssign(card, x, y + 8f, Mathf.Min(width, 400f),
                    leaderId) + 8f;

            var saying = BlockCardSaying;
            if (saying.Length > 0)
            {
                LedgerV2.Mono(card, x, -(y + 8f), width, saying, 10.5f, LedgerV2.Red, 0.5f);
                y += 24f;
            }

            y += 18f;
            Rule(card, 0f, -y, blocksW, LedgerV2.Rule);
            return y - top;
        }

        /// <summary>The crew this block's orders are given to: the crew of the man the
        /// boss named on the chips, or the lieutenant's whose paper it is on.</summary>
        int WalkingCrewId()
        {
            var roster = director != null ? director.Roster : null;
            if (roster != null && blockCardWalkerId >= 0)
            {
                var crew = roster.CrewOf(blockCardWalkerId);
                if (crew != null)
                    return crew.Id;
                for (var i = 0; i < roster.Crews.Count; i++)
                    if (roster.Crews[i].LieutenantId == blockCardWalkerId)
                        return roster.Crews[i].Id;
            }
            return blockRacketOk ? blockRacket.ResponsibleCrewId : -1;
        }

        /// <summary>
        /// WHO WALKS THE DOORS. The men who could be sent at this block, nearest first:
        /// the ones standing on it, then the lieutenant's own branch, then whoever is
        /// spare. Naming one names his CREW - the orders above are given to a crew, and
        /// a boss points at a man.
        /// </summary>
        float BuildWalkers(RectTransform card, float x, float top, float width)
        {
            var roster = director != null ? director.Roster : null;
            if (roster == null)
                return 0f;

            blockCardWalkers.Clear();
            for (var i = 0; i < blockCardHands.Count &&
                            blockCardWalkers.Count < BlockCardWalkersShown; i++)
                blockCardWalkers.Add(blockCardHands[i].Id);

            var responsible = organizationPaper.TryGetValue(blockCardId, out var paper)
                ? paper
                : -1;
            var branch = responsible >= 0 ? roster.CrewOf(responsible) : null;
            if (branch == null && responsible >= 0)
                for (var i = 0; i < roster.Crews.Count && branch == null; i++)
                    if (roster.Crews[i].LieutenantId == responsible)
                        branch = roster.Crews[i];
            if (branch != null)
            {
                if (!blockCardWalkers.Contains(branch.LieutenantId) &&
                    blockCardWalkers.Count < BlockCardWalkersShown)
                    blockCardWalkers.Add(branch.LieutenantId);
                for (var i = 0; i < branch.HoodIds.Count &&
                                blockCardWalkers.Count < BlockCardWalkersShown; i++)
                    if (!blockCardWalkers.Contains(branch.HoodIds[i]))
                        blockCardWalkers.Add(branch.HoodIds[i]);
            }
            for (var i = 0; i < roster.Crews.Count &&
                            blockCardWalkers.Count < BlockCardWalkersShown; i++)
                if (!blockCardWalkers.Contains(roster.Crews[i].LieutenantId))
                    blockCardWalkers.Add(roster.Crews[i].LieutenantId);

            if (blockCardWalkers.Count == 0)
                return 0f;

            var named = blockCardWalkerId >= 0 &&
                        blockCardWalkers.Contains(blockCardWalkerId);
            Caps(card, x, -top, width,
                    "WHO WALKS THE DOORS · " +
                    (named ? "1 MAN NAMED" : "NAME THE MEN WHO WALK THE DOORS"),
                    9.5f, LedgerV2.Label, 14f)
                .font = LedgerStyle.Mono;

            var y = top + 19f;
            var chipX = 0f;
            var line = 0;
            for (var i = 0; i < blockCardWalkers.Count; i++)
            {
                var manId = blockCardWalkers[i];
                var man = roster.Find(manId);
                if (man == null)
                    continue;

                var word = ShortName(man);
                var chipW = word.Length * 6.6f + 34f;
                if (chipX + chipW > width && chipX > 0f)
                {
                    chipX = 0f;
                    line++;
                }

                var picked = manId == blockCardWalkerId;
                var chip = NewRect("Walker " + manId, card);
                PlaceTopLeft(chip, x + chipX, -(y + line * 28f), chipW, 22f);
                Fill(chip, picked ? LedgerV2.Red : LedgerV2.PanelDark);
                if (!picked)
                    Frame(chip, 1f, LedgerV2.Rule);
                RowButton(chip, ClickSurface(chip), () =>
                {
                    blockCardWalkerId = blockCardWalkerId == manId ? -1 : manId;
                    dirty = true;
                });
                Block("Armed", chip, 10f, -8f, 6f, 6f,
                    man.Gone ? LedgerV2.Red : TenurePaying);
                LedgerV2.Mono(chip, 22f, -5f, chipW - 30f, word, 10.5f,
                    picked ? LedgerV2.HeadCream : LedgerV2.Ink, 1f)
                    .font = LedgerStyle.MonoBold;
                chipX += chipW + 6f;
            }

            return y - top + line * 28f + 22f;
        }

        readonly List<int> blockCardWalkers = new List<int>();

        /// <summary>"Dutch K." - the chips have room for a first name and an initial.
        /// </summary>
        static string ShortName(Character man) =>
            string.IsNullOrEmpty(man.Surname)
                ? man.FirstName
                : man.FirstName + " " + man.Surname.Substring(0, 1) + ".";

        /// <summary>
        /// One order inside the band: what it is, the mark that says whether it can fire,
        /// and the line under it that says what it actually DOES. Every order carries one
        /// (the user, 2026-09-02: "nije mi jasno šta koja akcija radi"), and an order that
        /// cannot fire REPLACES that line with the reason, so the reader is never told
        /// what a dead key would have done without being told why it is dead.
        ///
        /// An order that HAS fired stands as a status line until the seam's version moves
        /// and it can be given again.
        /// </summary>
        float OrderRow(RectTransform panel, float width, float top, string label,
            string note, string refusal, UnityEngine.Events.UnityAction run)
        {
            var key = label.ToLowerInvariant();
            if (blockRacketOutKey == key)
            {
                var strip = NewRect("Order out", panel);
                PlaceTopLeft(strip, 0f, -top, width, 34f);
                Rule(strip, 0f, 0f, width, LedgerV2.Rule);
                Caps(strip, 13f, -11f, width - 26f,
                    "OUT · " + blockRacketOutLine, 10f, LedgerV2.Amber, 4f)
                    .font = LedgerStyle.Mono;
                return 34f;
            }

            var can = string.IsNullOrEmpty(refusal);

            var row = NewRect("Order " + label, panel);
            PlaceTopLeft(row, 0f, -top, width, 1f);
            Rule(row, 0f, 0f, width, LedgerV2.Hair);
            if (can)
                RowButton(row, ClickSurface(row), run);

            Caps(row, 13f, -9f, width - 50f, label, 10.5f,
                can ? LedgerV2.Ink : LedgerV2.Muted, 5f).font = LedgerStyle.MonoBold;
            LedgerV2.Mono(row, width - 26f, -9f, 13f, can ? "›" : "—", 10f,
                LedgerV2.Label, 0f, TextAlignmentOptions.MidlineRight);

            // The note says what the order DOES and stands whether the order can fire or
            // not; the reason stands UNDER it in red. The design keeps both, because a
            // reader who cannot fire a key still has to learn what it was for.
            var copy = LedgerV2.Copytext(row, 13f, -24f, width - 26f, 40f, note, 9.5f,
                LedgerV2.Muted);
            var height = 24f + Mathf.Max(13f, copy.preferredHeight) + 3f;
            if (!can)
            {
                LedgerV2.Mono(row, 13f, -height, width - 26f, refusal, 9.5f,
                    LedgerV2.Red, 0.5f).font = LedgerStyle.MonoBold;
                height += 14f;
            }
            height += 6f;
            row.sizeDelta = new Vector2(width, height);
            return height;
        }

        /// <summary>Fires one racket order through the seam and keeps the key OUT while
        /// the office has it. An order that was never accepted says why instead.</summary>
        void FireRacketOrder(string key, System.Func<TerritoryCommandResult> run)
        {
            var result = run();
            if (result.Status == TerritoryCommandStatus.Rejected ||
                result.Status == TerritoryCommandStatus.Failed)
            {
                SayOnTheBlockCard(string.IsNullOrEmpty(result.Reason)
                    ? "the order was not taken"
                    : result.Reason);
                dirty = true;
                return;
            }

            blockRacketOutKey = key;
            blockRacketOutLine = string.IsNullOrEmpty(result.Reason)
                ? "the men are on their way"
                : result.Reason;
            blockRacketOutVersion = BlockRacketSeam.SourceOrStub.Version;
            dirty = true;
        }

        /// <summary>What the seam read when a key went out. The key comes back to idle
        /// when this moves - which is the seam saying the world has changed since.
        /// </summary>
        int blockRacketOutVersion = -1;

        /// <summary>
        /// LENIENT / NORMAL / STRICT / BRUTAL - how the lieutenant's crew handles a short
        /// or a no. One segmented bar, the same family the clock's speed rungs are, and
        /// the setting is the crew's, not the block's: a man runs his doors one way.
        /// </summary>
        float BuildPolicyBar(RectTransform card, float x, float top, float width,
            int crewId)
        {
            Caps(card, x, -top, width * 1.6f,
                    "POLICY · HOW HIS CREW HANDLES A SHORT OR A NO", 9.5f,
                    LedgerV2.Label, 14f)
                .font = LedgerStyle.Mono;

            var y = top + 19f;
            var values = System.Enum.GetValues(typeof(CrewPolicy));
            var cell = width / values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                var policy = (CrewPolicy)values.GetValue(i);
                var picked = blockRacket.Policy == policy;
                var rung = LedgerV2.Button(card, policy.ToString().ToUpperInvariant(),
                    x + i * cell, -y, cell, 25f,
                    () =>
                    {
                        var refusal =
                            BlockRacketSeam.ActionsOrStub.SetPolicy(crewId, policy);
                        if (!string.IsNullOrEmpty(refusal))
                            SayOnTheBlockCard(refusal);
                        dirty = true;
                    },
                    picked ? LedgerV2.Key.Dark : LedgerV2.Key.Ghost, 9f);
                SetActionEnabled(rung, crewId >= 0);
            }
            return y - top + 25f;
        }

        /// <summary>The design's own man row: a 22x28 portrait, his name and duty, and
        /// the two words that move him. 5 units of air either side of the plate.</summary>
        const float HandRowH = 38f;

        float HandRow(RectTransform card, float x, float top, float width, BlockHand hand)
        {
            const float rowH = HandRowH;
            var row = NewRect("Hand " + hand.Name, card);
            PlaceTopLeft(row, x, -top, width, rowH);
            Rule(row, 0f, -(rowH - 1f), width, LedgerV2.Hair);

            var roster = director != null ? director.Roster : null;
            var member = roster != null ? roster.Find(hand.Id) : null;
            Face(row, 0f, -5f, 22f, 28f, member,
                member != null ? Initials(member.FirstName, member.Surname) : "");

            var source = BlockRacketSeam.SourceOrStub;
            var carries = source.IsCollector(hand.Id);
            var onARound = source.TryGetRoundOf(hand.Id, out var roundBlock);
            // The stripe under the plate is the design's dot: blue for a man on the bag,
            // green for a man with a post, red for a man with neither.
            Block("Arm", row, 0f, -31f, 22f, 2f,
                carries ? LedgerV2.PaperBlue : hand.Armed ? TenurePaying : LedgerV2.Red);

            const float pullW = 36f;
            var bagW = 58f;
            var textW = width - 30f - pullW - bagW - 18f;
            var tagW = carries ? 60f : 0f;
            var name = LedgerV2.Name(row, 30f, -5f, textW - tagW, hand.Name, 13f,
                LedgerV2.Ink);
            name.overflowMode = TextOverflowModes.Ellipsis;
            if (carries)
                LedgerV2.Mono(row, 30f + textW - tagW + 4f, -7f, tagW - 4f, "COLLECTOR",
                    8f, LedgerV2.PaperBlue, 10f);

            // A man on a round is not standing here in any sense the reader can use him
            // in - he is walking somebody's doors. That is what his line says, and it is
            // greyed, because there is nothing to be done with him until he is back.
            var duty = LedgerV2.Mono(row, 30f, -21f, textW,
                onARound
                    ? "on the round · " + BlockName(roundBlock)
                    : hand.Duty +
                      (hand.Wage > 0 ? " · " + LedgerText.Cash(hand.Wage) + "/day" : "") +
                      (hand.Known.Length > 0 ? " · " + hand.Known + " here" : ""),
                9f, onARound ? LedgerV2.Muted : LedgerV2.Label, 1f);
            duty.overflowMode = TextOverflowModes.Ellipsis;

            // Two words, not two keys: the design sets them as plain type on the row,
            // because a man's row is a line of a roll and not a strip of buttons.
            var manId = hand.Id;
            var bag = LedgerV2.Mono(row, width - pullW - bagW - 9f, -12f, bagW,
                carries ? "OFF THE BAG" : "ON THE BAG", 9f,
                carries ? LedgerV2.Muted : LedgerV2.PaperBlue, 1f,
                TextAlignmentOptions.MidlineRight);
            bag.font = LedgerStyle.MonoBold;
            WordButton(row, bag, () => SetCollector(manId, !carries));

            var pull = LedgerV2.Mono(row, width - pullW, -12f, pullW, "PULL", 9.5f,
                LedgerV2.Red, 1f, TextAlignmentOptions.MidlineRight);
            pull.font = LedgerStyle.MonoBold;
            WordButton(row, pull, () => FileHoodRecall(manId));
            return rowH;
        }

        /// <summary>A WORD that is a control - the design's own way of putting a verb on
        /// a roll's row: no border, no fill, just the type, with a click target the size
        /// of the word.</summary>
        void WordButton(RectTransform row, TMP_Text word, UnityEngine.Events.UnityAction run)
        {
            var target = NewRect("Hit", row);
            var rect = word.rectTransform;
            PlaceTopLeft(target, rect.anchoredPosition.x, rect.anchoredPosition.y,
                rect.sizeDelta.x, rect.sizeDelta.y);
            RowButton(target, ClickSurface(target), run);
        }

        /// <summary>Puts a man on the bag or takes him off it. The seam does the whole
        /// of it; a refusal comes back as words and stands on the card for a moment,
        /// which is the same way every other refusal on this page is printed.</summary>
        void SetCollector(int characterId, bool on)
        {
            var refusal = BlockRacketSeam.ActionsOrStub.SetCollector(characterId, on);
            if (!string.IsNullOrEmpty(refusal))
                SayOnTheBlockCard(refusal);
            dirty = true;
        }

        /// <summary>A word the block card holds up for a couple of seconds - a refused
        /// order, in the words the system that refused it used.</summary>
        void SayOnTheBlockCard(string words)
        {
            blockRacketSaid = words ?? "";
            blockRacketSaidUntil = Time.unscaledTime + 2.5f;
        }

        string BlockCardSaying =>
            Time.unscaledTime < blockRacketSaidUntil ? blockRacketSaid : "";

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

        /// <summary>A column head: mono caps over a hairline, with an optional count
        /// held to the right margin of the same line - the design's "3 DOORS NEED AN
        /// ANSWER" beside the heading it belongs to.</summary>
        float Head(RectTransform card, float x, float top, float width, string label,
            string aside = "", Color? asideInk = null)
        {
            Caps(card, x, -top, aside.Length > 0 ? width * 0.58f : width, label, 9.5f,
                    LedgerV2.Label, 14f)
                .font = LedgerStyle.Mono;
            if (aside.Length > 0)
                Caps(card, x + width * 0.42f, -top, width * 0.58f, aside, 9f,
                        asideInk ?? LedgerV2.Label, 8f,
                        TextAlignmentOptions.MidlineRight)
                    .font = LedgerStyle.Mono;
            return 20f;
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
