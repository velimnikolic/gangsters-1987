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
    /// BLOCK FILE - what is TRUE about the open block, and the parts the drawer beside
    /// the ledger is built out of: the filmed ground, the doors that trade on it, the
    /// men standing among them, and the verbs that are filed against it.
    ///
    /// How those parts are ARRANGED is not here: the shell, the roles strip and the tabs
    /// are <see cref="BuildBlockDrawer"/>'s, and the three tab bodies and the pickers
    /// are their own files. This one reads the city and prints the pieces.
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

        /// <summary>The responsible crew's own men, listed so the boss can put one of
        /// them on the bag or hand the choice back to the lieutenant (GAN-262).</summary>
        readonly List<CrewHandView> blockCardCrewHands = new List<CrewHandView>();

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

        /// <summary>The apartment buildings standing on this block, in plan order. They
        /// are NOT premises and never join <see cref="blockCardTrades"/>: that list is
        /// what the PREMISES count, the racket standings and the door menu are keyed on.
        /// A building is reached by its own mast and its own header instead.</summary>
        readonly List<LivingCity.Property.ApartmentBuilding> blockCardBuildings =
            new List<LivingCity.Property.ApartmentBuilding>();

        readonly List<BlockFilmView.Mast> blockCardMasts = new List<BlockFilmView.Mast>();
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
        /// order key that has fired and not yet come back to idle - AND THE BLOCK IT
        /// WENT OUT ON. Without the block a round sent on one street printed its OUT
        /// line over the same order on the next street a reader opened, which reports an
        /// order nobody gave and hides a key that could have gone.</summary>
        string blockRacketOutKey = "";
        string blockRacketOutLine = "";
        TerritoryBlockId blockRacketOutBlock;

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

        /// <summary>Opens or closes the drawer against a ledger row. Opening a different
        /// block drops the premise, the men picked for a job, whatever picker was down
        /// and how far the tab body was scrolled - the drawer is about ONE block and
        /// never carries the last one's state over. The TAB it was left on is kept: a
        /// boss reading the doors of one block is reading the doors of the next.
        /// </summary>
        void OpenBlockCard(TerritoryBlockId blockId)
        {
            blockCardId = blockCardId == blockId ? default : blockId;
            if (!blockCardId.IsValid)
                StopBlockFilm();
            blockCardPick = default;
            blockCardSheet = BlockSheet.None;
            blockCardMenOpen = false;
            blockCardTradesOpen = false;
            blockRacketSaidUntil = 0f;
            blockTabScroll = 0f;
            blockSheetScroll = 0f;
            DoorMenu.Forget();
            blocksMenu = default;
            dirty = true;
        }

        /// <summary>Open a premises popup, retaining this block's chosen men while
        /// clearing any pending confirmation from the previous premises.</summary>
        void PickTrade(TerritoryBusinessId businessId)
        {
            if (blockCardPick == businessId)
            {
                CloseTradePopup();
                return;
            }
            if (!DoorMenu.TryRead(businessId, out _))
                return;
            blockCardPick = businessId;
            tradePopupScroll = 0f;
            DoorMenu.ResetCard();
            dirty = true;
            RebuildTradePopup();
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
            ReadBlockBuildings();
            ReadBlockHands(runtime);
            ReadBlockPressure(runtime);
            ReadBlockRacket();
        }

        /// <summary>The block's apartment buildings, off the PLAN - so a building the
        /// recycler has pooled is still listed, still masted and still openable.</summary>
        void ReadBlockBuildings()
        {
            blockCardBuildings.Clear();
            if (!blockCardId.IsValid)
                return;
            var standing = LivingCity.Property.ApartmentBuildings.OnBlock(blockCardId);
            for (var i = 0; i < standing.Count; i++)
                blockCardBuildings.Add(standing[i]);
            blockCardBuildings.Sort((a, b) => string.CompareOrdinal(a.Address, b.Address));
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
                blockRacketOutBlock = default;
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
                    Wage = member != null ? Outfit.Wages.WageFor(member, RosterDay) : 0,
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
            var wanted = Outfit.HouseMindConfig.Default.DemandPresence;
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

        // ------------------------------------------------------------------ the head


        /// <summary>The filmed block's own height. It is not a share of the drawer - a
        /// block filmed taller on a wide window and shorter on a narrow one is a
        /// different picture of the same ground on two screens - but a window too short
        /// to hold the whole shell takes the difference off the plate rather than off
        /// the tab body, which is where the words are.</summary>
        const float BlockPlateH = 330f;

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
        float BuildBlockModel(RectTransform card, float top, float plateH)
        {
            var plate = NewRect("Block model", card);
            PlaceTopLeft(plate, 0f, -top, blockCardW, plateH);
            Fill(plate, ModelPlate);

            if (blockCardGround.width <= 0f)
            {
                Line(plate, LedgerStyle.MonoItalic, 12f, ModelCaption,
                    18f, -(plateH * 0.5f - 10f), blockCardW - 36f, 20f,
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
            PlaceTopLeft(view, 0f, 0f, blockCardW, plateH);
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

            BuildBlockMasts();
            blockCardModel.Watch(film, blockCardDoors, blockCardMasts, blockCardYaw);
            blockCardModel.Turned = yaw =>
            {
                blockCardYaw = yaw;
                BlockFilm.Get().Look(blockCardGround, blockCardGroundY, yaw,
                    blockCardRise);
            };
            // Turning changes only the cached film frame. It is not a reason to rebuild
            // the paper around the model.
            blockCardModel.Settled = null;
            blockCardModel.Picked = key =>
            {
                // A building answers on its own key range: the mast is the only way to
                // click a block of flats, because the walls belong to the shop in its
                // ground floor (BlockFilmView.Mast says why).
                if (TryBuildingKey(key, out var building))
                {
                    OpenBlueprint(building.Id);
                    return;
                }
                PickTrade(key >= 0 && key < blockCardTrades.Count
                    ? blockCardTrades[key].Id
                    : default);
            };
            blockCardModel.Hovered = ShowBlockModelNote;

            var marked = blockCardDoors.Count;
            Caps(plate, 16f, -14f, blockCardW * 0.5f,
                "THE BLOCK · " + blockCardTrades.Count + " PREMISES",
                9.5f, ModelCaption, 14f).font = LedgerStyle.Mono;
            LedgerV2.Mono(plate, 16f, -31f, blockCardW * 0.5f,
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

        /// <summary>
        /// A mast per apartment building: a line from the middle of its footprint to a
        /// head over its roof, and the head is what opens the blueprint.
        ///
        /// The head must clear the building AT EVERY YAW. The lens turns round the block
        /// at the city's own pitch, so the far roof edge climbs the screen as the block
        /// turns: a head only just above the roof is swallowed from behind. At pitch θ a
        /// point h above the roof clears the far edge when h &gt; d·tanθ, where d is the
        /// footprint's half-depth - so the clearance is MEASURED off the building's own
        /// rectangle and the film's own pitch, never a constant.
        /// </summary>
        void BuildBlockMasts()
        {
            blockCardMasts.Clear();
            if (blockCardBuildings.Count == 0)
                return;

            var pitch = Mathf.Clamp(BlockFilm.CityPitch, 5f, 85f);
            var clear = Mathf.Tan(pitch * Mathf.Deg2Rad);
            var gang = GangCatalog.PlayerGangId;

            for (var i = 0; i < blockCardBuildings.Count; i++)
            {
                var building = blockCardBuildings[i];
                var rect = building.WorldRect;
                var half = Mathf.Max(rect.width, rect.height) * 0.5f;
                var head = building.Rise + half * clear + MastClearance;

                blockCardMasts.Add(new BlockFilmView.Mast
                {
                    Key = BuildingKey(i),
                    Base = new Vector3(rect.center.x, blockCardGroundY, rect.center.y),
                    Head = new Vector3(rect.center.x, blockCardGroundY + head, rect.center.y),
                    Ink = FlatsInk(building, gang),
                    Picked = blueprintBuilding == building.Id &&
                             currentPage == LedgerPage.Blueprint,
                });
            }
        }

        /// <summary>Air over the computed clearance, so the head never sits ON the ridge
        /// it is meant to stand above.</summary>
        const float MastClearance = 4f;

        /// <summary>Buildings answer on their own key range: -1 is the bare street and
        /// every index from 0 up is a premise, so a building is -2 and down.</summary>
        static int BuildingKey(int index) => -2 - index;

        bool TryBuildingKey(int key, out LivingCity.Property.ApartmentBuilding building)
        {
            building = null;
            if (key > -2)
                return false;
            var index = -2 - key;
            if (index < 0 || index >= blockCardBuildings.Count)
                return false;
            building = blockCardBuildings[index];
            return true;
        }

        /// <summary>The FIFTH word in the film's key. The four it had are racket states -
        /// OURS, PAYS US, ANOTHER HOUSE, NOBODY LEANS - and a building we hold two rooms
        /// in is none of them.</summary>
        static Color FlatsInk(LivingCity.Property.ApartmentBuilding building, int gang) =>
            LivingCity.Property.Apartments.CountIn(building.Id, gang) > 0
                ? LedgerV2.Green
                : LedgerV2.PaperBlue;

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
            var x = blockCardW - 16f - chipW * words.Length - gap * (words.Length - 1);

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

            // A mast under the pointer names its building, not a shop.
            if (TryBuildingKey(key, out var building))
            {
                var held = LivingCity.Property.Apartments.CountIn(
                    building.Id, GangCatalog.PlayerGangId);
                blockCardHoverName.text = building.Address;
                blockCardHoverLine.text = building.Flats + " flats · " +
                    (held > 0 ? held + " on our deed" : "not one door is ours") +
                    " · click for the blueprint";
                note.gameObject.SetActive(true);
                return;
            }

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
            var cellW = (blockCardW - margin * 2f) / words.Length;
            for (var i = 0; i < words.Length; i++)
            {
                var x = margin + i * cellW;
                LedgerV2.StreetMark(plate, x,
                    LedgerV2.MarkY(-(plateH - 27f), LineBox(9f), 9f), inks[i], 9f);
                LedgerV2.Mono(plate, x + 14f, -(plateH - 27f), cellW - 18f, words[i],
                    9f, ModelLegend, 2f);
            }
        }

        // ----------------------------------------------------------------- the columns

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
            var shown = blockCardTradesOpen
                ? blockCardTrades.Count
                : Mathf.Min(blockCardTrades.Count, BlockCardTradeShown);

            // The aside says how much of the block is on the page, and says instead how
            // many doors are asking for an answer where any are: a count of unread rows
            // is the less urgent of the two facts.
            y += SectionBar(card, x, y, width, "WHAT TRADES HERE",
                needing > 0
                    ? needing + (needing == 1 ? " DOOR NEEDS AN ANSWER"
                        : " DOORS NEED AN ANSWER")
                    : shown + " OF " + blockCardTrades.Count + " SHOWN",
                needing > 0 ? LedgerV2.Red : (Color?)null);
            y += 9f;

            if (blockCardTrades.Count == 0)
            {
                Line(card, LedgerStyle.MonoItalic, 12.8f, LedgerV2.Muted, x, -y, width,
                    20f, "Nothing trades on this block.");
                return y + 26f - top;
            }

            var frameTop = y;

            // THE COLUMN IS GROUPED BY BUILDING (the user, 2026-09-03). A flat alphabet
            // of shop names cannot say which of them share a stairwell, and a block of
            // flats with no shop in it could never appear in a list of trades at all.
            // The header is the second way into the blueprint, and the one that needs no
            // camera - which is what makes the sheet reviewable in the city-less
            // Ledger.unity, where the film has nothing to photograph.
            GroupTradesByBuilding();
            var printed = 0;

            for (var g = 0; g < blockTradeGroups.Count && printed < shown; g++)
            {
                var group = blockTradeGroups[g];
                if (group.Building != null)
                    y += BuildingHeaderRow(card, x, y, width, group.Building);
                for (var i = 0; i < group.Trades.Count && printed < shown; i++)
                {
                    y += TradeRow(card, x, y, width,
                        blockCardTrades[group.Trades[i]]);
                    printed++;
                }
            }

            // The list is closed with one hairline round the whole of it, drawn AFTER
            // the rows so the opaque row fills cannot cover its own edges.
            var frame = NewRect("Doors", card);
            PlaceTopLeft(frame, x, -frameTop, width, y - frameTop);
            Frame(frame, 1f, LedgerV2.Rule);

            if (blockCardTrades.Count > shown)
            {
                y += 9f;
                var word = "SHOW ALL " + blockCardTrades.Count + " DOORS ›";
                var link = LedgerV2.Mono(card, x + 2f, -y, width - 4f, word, 12.0f,
                    LedgerV2.Red, 14f);
                link.font = LedgerStyle.MonoBold;
                WordButton(card, link,
                    () => { blockCardTradesOpen = true; dirty = true; });
                y += 20f;
            }

            return y - top;
        }

        /// <summary>
        /// OUR FLATS for ONE block. Not printed on the file itself - the sheet's own
        /// column carries the city's rooms under the word from the blocks (the user,
        /// 2026-09-03), and the same list twice on one screen is one list too many. Kept
        /// because a per-block reading is what the file will want the day it grows a
        /// second page.
        /// </summary>
        float BuildBlockFlats(RectTransform card, float x, float top, float width)
        {
            var gang = GangCatalog.PlayerGangId;
            var held = 0;
            for (var i = 0; i < blockCardBuildings.Count; i++)
                held += LivingCity.Property.Apartments.CountIn(
                    blockCardBuildings[i].Id, gang);

            // A block with no rooms of ours on it says nothing at all: an empty heading is
            // a heading the reader has to read before learning there is nothing under it.
            if (held == 0)
                return 0f;

            var y = top;
            y += Head(card, x, y, width, "OUR FLATS",
                held + (held == 1 ? " ROOM" : " ROOMS"), LedgerV2.Green);

            var day = RosterDay;
            for (var i = 0; i < blockCardBuildings.Count; i++)
            {
                var building = blockCardBuildings[i];
                LivingCity.Property.Apartments.OwnedIn(building.Id, gang, blockFlatScratch);
                if (blockFlatScratch.Count == 0)
                    continue;

                y += BuildingHeaderRow(card, x, y, width, building);
                for (var f = 0; f < blockFlatScratch.Count; f++)
                    y += BlockFlatRow(card, x, y, width, blockFlatScratch[f], day);
            }

            return y - top + 18f;
        }

        readonly List<LivingCity.Property.ApartmentRecord> blockFlatScratch =
            new List<LivingCity.Property.ApartmentRecord>();

        /// <summary>One room of ours, under its building: the door, what runs out of it,
        /// who keeps it, and how it reads today.</summary>
        float BlockFlatRow(RectTransform card, float x, float top, float width,
            LivingCity.Property.ApartmentRecord record, int day)
        {
            const float rowH = 30f;
            var unit = record.Unit;
            var state = StateOfFlat(unit, day);
            var spec = LivingCity.Property.UnitRoles.Of(record.Role);

            var row = NewRect("Flat " + unit.Door, card);
            PlaceTopLeft(row, x, -top, width, rowH);
            var surface = ClickSurface(row);
            RowButton(row, surface, () =>
            {
                OpenBlueprint(unit.Building);
                OpenFlatForm(unit);
            });
            Rule(row, 0f, -(rowH - 1f), width, LedgerV2.Hair);

            var badge = NewRect("Door", row);
            var badgeW = unit.Door.Length * 7.8f + 12f;
            PlaceTopLeft(badge, 8f, -(rowH - 17f) * 0.5f, badgeW, 17f);
            Fill(badge, LedgerV2.DarkPlate);
            Line(badge, LedgerStyle.Mono, 10.5f, LedgerV2.HeadCream, 6f, -1f,
                badgeW - 12f, 15f, unit.Door).characterSpacing = 4f;

            var textX = 8f + badgeW + 10f;
            var stateW = 96f;
            var keeperW = 118f;
            var nameW = Mathf.Max(60f, width - textX - stateW - keeperW - 16f);

            var name = LedgerV2.Name(row, textX, -(rowH - 18f) * 0.5f, nameW,
                string.IsNullOrEmpty(record.Name)
                    ? LivingCity.Property.UnitRoles.Label(record.Role)
                    : record.Name,
                12.5f, record.Role == LivingCity.Property.UnitRole.Empty
                    ? LedgerV2.Red : LedgerV2.Ink);
            name.overflowMode = TextOverflowModes.Ellipsis;

            var keeper = record.KeeperId >= 0 && director != null && director.Roster != null
                ? director.Roster.Find(record.KeeperId)
                : null;
            var keeperLine = LedgerV2.Mono(row, textX + nameW, -(rowH - 16f) * 0.5f,
                keeperW, keeper != null ? keeper.FullName : "NO KEEPER", 9f,
                keeper != null ? LedgerV2.Label : LedgerV2.Red, 1f);
            keeperLine.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.StreetMark(row, width - stateW - 4f, -(rowH * 0.5f),
                StateInk(state), 9f);
            LedgerV2.Mono(row, width - stateW + 10f, -(rowH - 16f) * 0.5f, stateW - 14f,
                LivingCity.Property.Apartments.Word(state), 9f, StateInk(state), 3f,
                TextAlignmentOptions.MidlineRight);

            return rowH;
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

        /// <summary>One building and the doors that trade out of its ground floor. A
        /// building with no shop in it still gets a group, with nothing under it: that is
        /// how a block of flats becomes visible on a sheet that has only ever listed
        /// trades.</summary>
        sealed class BlockTradeGroup
        {
            public LivingCity.Property.ApartmentBuilding Building;
            public readonly List<int> Trades = new List<int>();
        }

        readonly List<BlockTradeGroup> blockTradeGroups = new List<BlockTradeGroup>();

        /// <summary>
        /// Groups the doors under the building they stand in, keeping the SEVERITY sort
        /// inside a group - the column's whole reason for sorting is that a door needing
        /// an answer must not be thirty rows down, and grouping must not undo that.
        ///
        /// Which building a door is in is read off the business id itself, which carries
        /// the plan and the spot it was minted from; no geometry, no second index.
        /// </summary>
        void GroupTradesByBuilding()
        {
            blockTradeGroups.Clear();
            var byBuilding = new Dictionary<LivingCity.Property.ApartmentBuildingId,
                BlockTradeGroup>();

            for (var i = 0; i < blockCardBuildings.Count; i++)
            {
                var group = new BlockTradeGroup { Building = blockCardBuildings[i] };
                blockTradeGroups.Add(group);
                byBuilding[blockCardBuildings[i].Id] = group;
            }

            BlockTradeGroup loose = null;
            for (var i = 0; i < blockCardTrades.Count; i++)
            {
                if (LivingCity.Property.ApartmentBuildings.TryBuildingOf(
                        blockCardTrades[i].Id, out var id) &&
                    byBuilding.TryGetValue(id, out var group))
                {
                    group.Trades.Add(i);
                    continue;
                }
                // A venue, a compound, a downtown prefab: no plan-level building owns it,
                // so it stands on the block on its own.
                loose ??= new BlockTradeGroup();
                loose.Trades.Add(i);
            }

            // A building whose shops are all elsewhere is still listed; an empty group is
            // only dropped when the building has no flats either.
            for (var i = blockTradeGroups.Count - 1; i >= 0; i--)
                if (blockTradeGroups[i].Trades.Count == 0 &&
                    blockTradeGroups[i].Building != null &&
                    blockTradeGroups[i].Building.Flats <= 0)
                    blockTradeGroups.RemoveAt(i);

            if (loose != null)
                blockTradeGroups.Add(loose);
        }

        /// <summary>The building's own row: its address, what we hold in it, and the way
        /// into its blueprint.</summary>
        float BuildingHeaderRow(RectTransform card, float x, float top, float width,
            LivingCity.Property.ApartmentBuilding building)
        {
            const float rowH = 30f;
            var gang = GangCatalog.PlayerGangId;
            var held = LivingCity.Property.Apartments.CountIn(building.Id, gang);

            var row = NewRect("Building " + building.Address, card);
            PlaceTopLeft(row, x, -top, width, rowH);
            Fill(row, LedgerV2.PanelDark);
            var surface = ClickSurface(row);
            var id = building.Id;
            RowButton(row, surface, () => OpenBlueprint(id));
            Rule(row, 0f, -(rowH - 1f), width, LedgerV2.Rule);

            // Inset like the door rows under it: the list is closed with a hairline
            // now, and a mark flush at x=0 sits on that hairline.
            LedgerV2.StreetMark(row, 10f, -(rowH - 10f) * 0.5f, FlatsInk(building, gang),
                10f);
            var name = LedgerV2.Name(row, 29f, -4f, width - 230f, building.Address, 12.5f,
                LedgerV2.Ink);
            name.overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Mono(row, width - 200f, -4f, 180f,
                held > 0
                    ? held + " OF " + building.Flats + " FLATS OURS"
                    : building.Flats + " FLATS · NONE OURS",
                9f, held > 0 ? LedgerV2.Green : LedgerV2.Label, 2f,
                TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(row, width - 16f, -4f, 14f, "›", 12f, LedgerV2.Muted, 0f);
            return rowH;
        }

        /// <summary>
        /// One door: the mark that says what KIND of fact its standing is, the name and
        /// where it stands with us in the simulation's own words, and the figure it is
        /// worth a day over that standing in one word.
        ///
        /// The mark is HATCHED where the only thing true about the door is on paper - it
        /// is unvisited, it is another house's, it is shut - and SOLID where the standing
        /// is one our own men made at it. That is the book's oldest distinction and it
        /// belongs on a door exactly as much as on a block.
        /// </summary>
        float TradeRow(RectTransform card, float x, float top, float width, BlockTrade trade)
        {
            const float rowH = 46f;
            var picked = blockCardPick.IsValid && trade.Id == blockCardPick;
            var id = trade.Id;

            var row = NewRect("Trade " + trade.Name, card);
            PlaceTopLeft(row, x, -top, width, rowH);
            Fill(row, picked ? LedgerV2.Picked : LedgerV2.Panel);
            RowButton(row, ClickSurface(row), () => PickTrade(id));
            Rule(row, 0f, -(rowH - 1f), width, LedgerV2.Hair);

            // The square takes the STANDING's ink where the racket has one, and the
            // tenure's where it does not: a door that refused us is red whoever holds
            // the deed, because the refusal is the thing the reader has to act on.
            var hasStanding = blockStandings.TryGetValue(trade.Id, out var standing);
            var ink = hasStanding
                ? StandingInk(standing.Kind)
                : TenureColour(trade.Tenure);
            // The two lines under it run from -6 to about -37, so the square takes the
            // centre of the PAIR and not the centre of the row it happens to sit in.
            if (hasStanding && OnTheStreet(standing.Kind))
                LedgerV2.StreetMark(row, 10f, -(rowH - 10f) * 0.5f, ink, 10f);
            else
                LedgerV2.PaperMark(row, 10f, -(rowH - 10f) * 0.5f, ink, 10f);

            const float figureW = 92f;
            var badgeW = BadgeWidth(trade.Role);
            var textW = width - 29f - figureW - 9f - (badgeW > 0f ? badgeW + 6f : 0f);
            var name = LedgerV2.Name(row, 29f, -6f, textW, trade.Name, 16.2f, LedgerV2.Ink);
            name.overflowMode = TextOverflowModes.Ellipsis;
            if (badgeW > 0f)
                RoleBadge(row, 29f + textW + 6f, -12f, badgeW, trade);

            // The second line is WHERE THIS DOOR STANDS WITH US, in the simulation's own
            // words. Only where the racket has nothing to say about it does the row fall
            // back to the tenure sentence it printed before there was a racket at all.
            var under = LedgerV2.Mono(row, 29f, -26f, textW,
                hasStanding
                    ? standing.Line +
                      (trade.Menu.Closure.Shut ? " · " + trade.Menu.Closure.Note : "")
                    : trade.Trade.ToLowerInvariant() + " · " + TenureLine(trade) +
                      (trade.Menu.Closure.Shut ? " · " + trade.Menu.Closure.Note : ""),
                10.8f, hasStanding && standing.Severity > 0 ? ink : LedgerV2.Muted, 10f);
            under.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.Figure(row, width - 10f - figureW, -8f, figureW,
                trade.TakePerDay > 0 ? LedgerText.Cash(trade.TakePerDay) : "—", 13.2f,
                trade.TakePerDay > 0 ? ink : LedgerV2.Faint);
            LedgerV2.Mono(row, width - 10f - figureW, -26f, figureW,
                hasStanding ? StandingWord(standing.Kind) : TenureWord(trade.Tenure),
                9.6f, LedgerV2.Faint, 10f, TextAlignmentOptions.MidlineRight);
            return rowH;
        }

        /// <summary>Whether this standing is a thing our own men made at the door - as
        /// against a thing that is only true on paper until somebody walks it.</summary>
        static bool OnTheStreet(DoorStandingKind kind) =>
            kind == DoorStandingKind.Paying || kind == DoorStandingKind.Refused ||
            kind == DoorStandingKind.Late || kind == DoorStandingKind.Short ||
            kind == DoorStandingKind.Wavering;

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

        /// <summary>Dismiss only the premises popup and its pending selection.</summary>
        void CloseTradePopup()
        {
            ClearTradePopupView();
            if (!blockCardPick.IsValid)
                return;
            blockCardPick = default;
            tradePopupScroll = 0f;
            DoorMenu.ResetCard();
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
            // ONE line: the heading, the headcount and who has the bag among them. The
            // bag is named because a man carrying it is a man out of the street line,
            // and the strip above cannot say WHICH of the men down here it took.
            var bagWord = carrying == 0
                ? "NO BAG MAN ON SITE"
                : blockRacketOk && blockRacket.CollectorName.Length > 0
                    ? blockRacket.CollectorName.ToUpperInvariant() + " ON THE BAG"
                    : carrying + " ON THE BAG";
            y += SectionBar(card, x, y, width,
                "WHO STANDS HERE · " + blockCardHands.Count +
                (blockCardHands.Count == 1 ? " MAN" : " MEN"), bagWord);
            y += 9f;

            if (blockCardHands.Count == 0)
            {
                LedgerV2.Mono(card, x, -y, width, "Nobody stands on this block.",
                    12.0f, LedgerV2.Red, 0.5f);
                return y + 22f - top;
            }

            var shown = blockCardMenOpen
                ? blockCardHands.Count
                : Mathf.Min(blockCardHands.Count, BlockCardMenShown);

            // ONE man to a line. The drawer is a column and a second man beside the
            // first would leave neither of them room for what he is doing.
            var rollTop = y;
            for (var i = 0; i < shown; i++)
                y += HandRow(card, x, y, width, blockCardHands[i]);

            var frame = NewRect("Men", card);
            PlaceTopLeft(frame, x, -rollTop, width, y - rollTop);
            Frame(frame, 1f, LedgerV2.Rule);

            if (blockCardHands.Count > shown)
            {
                y += 9f;
                var link = LedgerV2.Mono(card, x + 2f, -y, width - 4f,
                    "SHOW ALL " + blockCardHands.Count + " MEN ›", 12.0f, LedgerV2.Red,
                    14f);
                link.font = LedgerStyle.MonoBold;
                WordButton(card, link,
                    () => { blockCardMenOpen = true; dirty = true; });
                y += 20f;
            }

            return y - top;
        }

        // ------------------------------------------------------- what you can do

        int WalkingCrewId()
        {
            DoorMenu.ConstrainToBlock(director != null ? director.Roster : null, blockCardId, CrewMissionPicker.Physical());
            return DoorMenu.CrewToSend(blockCardId, DoorDispatch.BlockResponsibility,
                out _, out _, out _)?.Id ?? -1;
        }

        /// <summary>Fires one racket order through the seam and keeps the key OUT while
        /// the office has it. An order that was never accepted says why instead.</summary>
        void FireRacketOrder(string key, int crewId, System.Func<TerritoryCommandResult> run)
        {
            var roster = director != null ? director.Roster : null;
            var refusal = key == "round"
                ? (BlockMissionChoice.ResponsibleCrew(roster, blockCardId)?.Id == crewId
                    ? null : "this crew does not answer for this block")
                : BlockMissionChoice.Refusal(roster, blockCardId, crewId, true);
            var result = refusal == null ? run()
                : new TerritoryCommandResult(0, TerritoryCommandStatus.Rejected, refusal);
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
            blockRacketOutBlock = blockCardId;
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

        /// <summary>A man's row: a 24-unit portrait, his name and what he is doing, the
        /// chip that says he has the bag, and the one word that takes him off the block.
        /// Who CARRIES the bag is no longer set from here - that is the roles strip's
        /// one job, and two surfaces for one assignment is one too many.</summary>
        const float HandRowH = 44f;

        float HandRow(RectTransform card, float x, float top, float width, BlockHand hand)
        {
            const float rowH = HandRowH;
            var row = NewRect("Hand " + hand.Name, card);
            PlaceTopLeft(row, x, -top, width, rowH);
            Fill(row, LedgerV2.Panel);
            Rule(row, 0f, -(rowH - 1f), width, LedgerV2.Hair);

            var roster = director != null ? director.Roster : null;
            var member = roster != null ? roster.Find(hand.Id) : null;
            Face(row, 10f, -(rowH - 24f) * 0.5f, 24f, 24f, member,
                member != null ? Initials(member.FirstName, member.Surname) : "");

            var source = BlockRacketSeam.SourceOrStub;
            var carries = source.IsCollector(hand.Id);
            var onARound = source.TryGetRoundOf(hand.Id, out var roundBlock);

            const float pullW = 40f;
            var chipW = carries
                ? Mathf.Ceil(LedgerV2.MonoWidth("On the bag", 10.5f, 6f)) + 20f
                : 0f;
            var textW = width - 44f - pullW - 10f - (chipW > 0f ? chipW + 9f : 0f);
            var name = LedgerV2.Name(row, 44f, -5f, textW, hand.Name, 16.2f,
                LedgerV2.Ink);
            name.overflowMode = TextOverflowModes.Ellipsis;

            // A man on a round is not standing here in any sense the reader can use him
            // in - he is walking somebody's doors. That is what his line says, and it is
            // greyed, because there is nothing to be done with him until he is back.
            var duty = LedgerV2.Mono(row, 44f, -24f, textW,
                onARound
                    ? "on the round · " + BlockName(roundBlock)
                    : hand.Duty +
                      (hand.Wage > 0 ? " · " + LedgerText.Cash(hand.Wage) + "/day" : "") +
                      (hand.Known.Length > 0 ? " · " + hand.Known + " here" : ""),
                10.8f, onARound ? LedgerV2.Muted : LedgerV2.Muted, 10f);
            duty.overflowMode = TextOverflowModes.Ellipsis;

            if (carries)
                LedgerV2.Status(row, width - 10f - pullW - 9f - chipW,
                    -(rowH - 20f) * 0.5f, chipW, 20f, "On the bag", LedgerV2.Red, 10.5f);

            // A word, not a key: a man's row is a line of a roll and not a strip of
            // buttons.
            var manId = hand.Id;
            var pull = LedgerV2.Mono(row, width - 10f - pullW, -12f, pullW, "PULL",
                10.8f, LedgerV2.Red, 12f, TextAlignmentOptions.MidlineRight);
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

        /// <summary>The lieutenant's surname for a line about his own choice, or a
        /// word that stands in where the block has no name on it.</summary>
        static string ShortLeaderWord(OrganizationPerson leader) =>
            leader.IsValid && !string.IsNullOrEmpty(leader.Name)
                ? leader.Name.Substring(leader.Name.LastIndexOf(' ') + 1).ToUpperInvariant()
                : "HIS LIEUTENANT";

        // ------------------------------------------------- putting men on the block

        /// <summary>Marches the crew that answers for the block onto it. This is the
        /// physical act, not a filing about it: the men walk there, and Presence follows
        /// them.</summary>
        void FileMenOntoBlock()
        {
            var blockId = blockCardId;
            var crewId = WalkingCrewId();
            FileOrder("Men put on " + BlockName(blockId) + ".", () =>
            {
                var runtime = TerritoryRuntime.Instance;
                var roster = director != null ? director.Roster : null;
                if (runtime?.Commands == null || roster == null)
                    return Outfit.FilingRuling.Refuse(
                        "the territory command gateway is unavailable");

                var refusal = BlockMissionChoice.Refusal(roster, blockId, crewId, true);
                if (refusal != null) return Outfit.FilingRuling.Refuse(refusal);
                var crew = roster.FindCrew(crewId);

                var lieutenant = roster.Find(crew.LieutenantId);
                if (!runtime.TryGetCrewNode(crew.Id, out var node))
                    return Outfit.FilingRuling.Refuse(
                        "that crew is not on the street to be moved");

                var result = runtime.Commands.Submit(
                    Gameplay.PlayerCommands.Stamp(
                        new OperateInBlockCommand(node, blockId)));
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
