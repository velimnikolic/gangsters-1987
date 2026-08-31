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

        /// <summary>The men picked for the next job. Character ids.</summary>
        readonly List<int> blockCardCrew = new List<int>();

        /// <summary>The angle the block is standing at. Held on the sheet rather than on
        /// the view, so turning it survives the repaint a pick causes.</summary>
        float blockCardYaw = -35f;
        float blockCardTilt = BlockFilmView.IsoTilt;

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

        /// <summary>Where a premise stands with us. Read off the deed
        /// (BusinessMarker.GangId) and the racket ledger, never stored here.</summary>
        enum BlockTenure
        {
            Ours,
            Paying,
            Rival,
            Open,
        }

        /// <summary>One premise on the block, as the city answers for it.</summary>
        struct BlockTrade
        {
            public TerritoryBusinessId Id;
            public string Name;
            public string Trade;
            public BlockTenure Tenure;
            public string RivalName;

            /// <summary>What it puts in the books in one day: a tenth of the till over a
            /// week when it is ours, its protection over a week when it pays us, and
            /// nothing at all when it is neither.</summary>
            public int TakePerDay;

            public int BuyPrice;

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
            blockCardCrew.Clear();
            organizationBlockMenu = default;
            dirty = true;
        }

        void PickTrade(TerritoryBusinessId businessId)
        {
            blockCardPick = blockCardPick == businessId ? default : businessId;
            dirty = true;
        }

        void ToggleJobMan(int characterId)
        {
            if (!blockCardCrew.Remove(characterId))
                blockCardCrew.Add(characterId);
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
            var business = BusinessRuntime.Instance;
            var racket = runtime.Racket;
            var us = new TerritoryGangId(GangCatalog.PlayerGangId);
            var rows = CityBusinesses.All;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.CanonicalBlockId != blockCardId)
                    continue;

                var trade = new BlockTrade
                {
                    Id = row.Id,
                    Name = row.Name,
                    Trade = "PREMISES",
                    Tenure = BlockTenure.Open,
                };

                var archetype = BusinessArchetypeId.Grocer;
                var priced = false;
                if (business != null && business.Populated &&
                    business.Directory.TryGet(row.Id, out var record))
                {
                    archetype = record.Archetype;
                    priced = true;
                    trade.Trade = TradeWord(record.Archetype);
                    if (record.DisplayName.Length > 0)
                        trade.Name = record.DisplayName;
                }

                // The DEED first - BusinessMarker.GangId is the single source of who owns
                // ground - and only then what the racket says about a door nobody owns.
                var marker = row.Marker;
                if (marker == null)
                    BusinessViewBindings.TryGet(row.Id, out marker);

                if (marker != null && marker.GangId == GangCatalog.PlayerGangId)
                {
                    trade.Tenure = BlockTenure.Ours;
                }
                else if (marker != null && marker.GangId >= 0)
                {
                    trade.Tenure = BlockTenure.Rival;
                    trade.RivalName = GangName(marker.GangId);
                }
                else if (racket != null && racket.TryGetProtector(row.Id, out var protector))
                {
                    trade.Tenure = protector == us ? BlockTenure.Paying : BlockTenure.Rival;
                    if (trade.Tenure == BlockTenure.Rival)
                        trade.RivalName = GangName(protector.Value);
                }

                if (priced)
                {
                    trade.BuyPrice = Outfit.EconomyPrices.BuyPrice(archetype);
                    trade.TakePerDay = trade.Tenure switch
                    {
                        BlockTenure.Ours => Outfit.EconomyPrices.NetPerDay(archetype),
                        BlockTenure.Paying =>
                            Outfit.EconomyPrices.ProtectionPerWeek(archetype) / 7,
                        _ => 0,
                    };
                }

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

        static string GangName(int gangId)
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
        static string TradeWord(BusinessArchetypeId archetype)
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

        // ------------------------------------------------------------------ the card

        static readonly Color TenureOurs = LedgerV2.Rgb2(0xaf3c3a);
        static readonly Color TenurePaying = LedgerV2.Rgb2(0x59985b);
        static readonly Color TenureRival = LedgerV2.Rgb2(0x674f8d);
        static readonly Color TenureOpen = LedgerV2.Rgb2(0xb4a99e);
        static readonly Color RivalInk = LedgerV2.Rgb2(0x60438d);

        static readonly Color ModelPlate = LedgerV2.Rgb2(0x241e1a);
        static readonly Color ModelTip = LedgerV2.Rgb2(0x110c09);
        static readonly Color ModelCaption = LedgerV2.Rgb2(0x998c84);
        static readonly Color ModelHint = LedgerV2.Rgb2(0x72665e);
        static readonly Color ModelLegend = LedgerV2.Rgb2(0xa3958d);
        static readonly Color ModelChip = LedgerV2.Rgb2(0x564a43);

        static Color TenureColour(BlockTenure tenure) => tenure switch
        {
            BlockTenure.Ours => TenureOurs,
            BlockTenure.Paying => TenurePaying,
            BlockTenure.Rival => TenureRival,
            _ => TenureOpen,
        };

        static string TenureWord(BlockTenure tenure) => tenure switch
        {
            BlockTenure.Ours => "OURS",
            BlockTenure.Paying => "PAYS US",
            BlockTenure.Rival => "THEIRS",
            _ => "OPEN",
        };

        /// <summary>
        /// The file itself. Answers the cursor below it, so the ledger above and the
        /// filed orders below are laid out by the same running y as every other section.
        /// </summary>
        float BuildBlockFile(float cursor)
        {
            if (!blockCardId.IsValid)
                return cursor;

            // The file stands under a ROW. Lose the row - the name was struck off and the
            // street took the ground back - and the file closes with it rather than
            // hanging under a ledger that no longer mentions the block.
            if (!organizationBlockRows.Contains(blockCardId))
            {
                blockCardId = default;
                StopBlockFilm();
                return cursor;
            }

            ReadBlockFile();

            var card = NewRect("Block file", organizationColumn);
            PlaceTopLeft(card, 0f, -cursor, organizationW, 1f);
            Fill(card, LedgerV2.Panel);

            var y = BuildBlockFileHead(card);
            y -= BuildBlockModel(card, -y);
            y -= BuildBlockColumns(card, -y);

            var height = -y + 14f;
            card.sizeDelta = new Vector2(organizationW, height);
            return cursor + height + 18f;
        }

        float BuildBlockFileHead(RectTransform card)
        {
            const float bandH = 38f;
            var band = NewRect("File head", card);
            PlaceTopLeft(band, 0f, 0f, organizationW, bandH);
            Fill(band, LedgerV2.Head);

            Caps(band, 16f, -8f, 90f, "BLOCK FILE", 9f, LedgerV2.HeadDim, 8f)
                .font = LedgerStyle.Mono;

            var name = BlockName(blockCardId);
            var title = LedgerV2.Name(band, 16f, -18f, organizationW * 0.4f, name, 19f,
                LedgerV2.HeadCream);
            title.overflowMode = TextOverflowModes.Ellipsis;

            var wardX = 16f + Mathf.Min(organizationW * 0.4f, title.preferredWidth) + 14f;
            LedgerV2.Mono(band, wardX, -18f, organizationW * 0.28f,
                NeighborhoodOf(blockCardId), 10f, LedgerV2.HeadDim, 2f);

            var control = ControlOf(blockCardId);
            var colour = ControlColour(control);
            const float closeW = 74f;
            const float streetW = 190f;
            LedgerV2.StreetMark(band, organizationW - closeW - streetW - 26f, -20f, colour, 10f);
            LedgerV2.Figure(band, organizationW - closeW - streetW - 10f, -19f, streetW - 6f,
                ControlWord(control), 11.5f, colour, TextAlignmentOptions.MidlineRight);

            LedgerV2.Button(band, "CLOSE", organizationW - closeW - 14f, -6f, closeW, 26f,
                () => OpenBlockCard(blockCardId), LedgerV2.Key.Ghost, 9f);

            return -bandH;
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
            var plateH = Mathf.Clamp(organizationW * 0.24f, 220f, 330f);
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

            var film = BlockFilm.Get();
            var view = NewRect("Model", plate);
            Stretch(view);
            view.gameObject.AddComponent<CanvasRenderer>();
            blockCardModel = view.gameObject.AddComponent<BlockFilmView>();
            blockCardModel.raycastTarget = true;
            blockCardModel.color = Color.white;

            // The film is cut to the plate's real pixel size, so a wide window gets a
            // wide negative rather than a square one stretched across it.
            var scale = view.lossyScale.x <= 0f ? 1f : view.lossyScale.x;
            blockCardModel.texture = film.Reel(
                Mathf.RoundToInt(organizationW * scale), Mathf.RoundToInt(plateH * scale));
            film.Look(blockCardGround, blockCardGroundY, blockCardYaw, blockCardTilt,
                blockCardRise);

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

            blockCardModel.Watch(film, blockCardDoors, blockCardYaw, blockCardTilt);
            blockCardModel.Turned = (yaw, tilt) =>
            {
                blockCardYaw = yaw;
                blockCardTilt = tilt;
                BlockFilm.Get().Look(blockCardGround, blockCardGroundY, yaw, tilt,
                    blockCardRise);
            };
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

            BuildBlockModelAngles(plate);
            BuildBlockModelNote(plate, plateH);
            BuildBlockModelKey(plate, plateH);
            return plateH;
        }

        /// <summary>Puts the second lens away and lets the city have the ground back.
        /// Called whenever the file stops standing open - a closed card, a shut book.
        /// </summary>
        void StopBlockFilm()
        {
            RoadDemo.CityBlockRecycler.Release();
            BlockFilm.StopIfRunning();
        }

        /// <summary>The three named angles. They turn the live view and never repaint the
        /// sheet - the model under the pointer must not be destroyed to change its
        /// angle.</summary>
        void BuildBlockModelAngles(RectTransform plate)
        {
            var words = new[] { "ISO", "PLAN", "STREET" };
            var tilts = new[]
            {
                BlockFilmView.IsoTilt, BlockFilmView.PlanTilt, BlockFilmView.StreetTilt,
            };
            const float chipW = 62f;
            const float gap = 6f;
            var x = organizationW - 18f - chipW * words.Length - gap * (words.Length - 1);

            for (var i = 0; i < words.Length; i++)
            {
                var tilt = tilts[i];
                var on = blockCardModel != null && blockCardModel.Angle == words[i];
                var chip = NewRect("Angle " + words[i], plate);
                PlaceTopLeft(chip, x, -14f, chipW, 24f);
                Fill(chip, on ? LedgerV2.Rgb2(0xa51e24) : new Color(0f, 0f, 0f, 0f));
                Frame(chip, 1f, ModelChip);
                var chipFace = ClickSurface(chip);
                var word = Text("Label", chip, LedgerStyle.MonoBold, 9f,
                    on ? LedgerV2.HeadCream : ModelLegend, TextAlignmentOptions.Center);
                Stretch(word.rectTransform);
                word.characterSpacing = 6f;
                word.text = words[i];

                var button = chip.gameObject.AddComponent<Button>();
                button.targetGraphic = chipFace;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() =>
                {
                    blockCardYaw = -35f;
                    blockCardTilt = tilt;
                    if (blockCardModel != null)
                        blockCardModel.Turn(blockCardYaw, blockCardTilt);
                    dirty = true;
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
                    : "");
            blockCardHoverLine.color = TenureColour(trade.Tenure);
            note.gameObject.SetActive(true);
        }

        void BuildBlockModelKey(RectTransform plate, float plateH)
        {
            var words = new[] { "OURS", "PAYS US", "ANOTHER HOUSE", "NOBODY LEANS" };
            var inks = new[] { TenureOurs, TenurePaying, TenureRival, TenureOpen };
            var x = 18f;
            for (var i = 0; i < words.Length; i++)
            {
                LedgerV2.StreetMark(plate, x, -(plateH - 26f), inks[i], 9f);
                var label = LedgerV2.Mono(plate, x + 14f, -(plateH - 27f), 130f, words[i],
                    9f, ModelLegend, 2f);
                x += 14f + Mathf.Min(130f, label.preferredWidth) + 18f;
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
            y += Gauge(card, x, y, width, "TAKE A DAY",
                LedgerText.Cash(blockCardTake),
                (float)blockCardTake / takeCap,
                blockCardTake > 0 ? LedgerV2.Green : LedgerV2.Red,
                blockCardTake > 0
                    ? "counted into the books at midnight"
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
            for (var i = 0; i < shown; i++)
                y += TradeRow(card, x, y, width, blockCardTrades[i]);

            if (blockCardTrades.Count > shown)
            {
                LedgerV2.Button(card, "SHOW ALL " + blockCardTrades.Count + " DOORS",
                    x, -y, Mathf.Min(width, 210f), 24f,
                    () => { blockCardTradesOpen = true; dirty = true; },
                    LedgerV2.Key.Ghost, 9f);
                y += 30f;
            }

            var pick = -1;
            for (var i = 0; i < blockCardTrades.Count; i++)
                if (blockCardPick.IsValid && blockCardTrades[i].Id == blockCardPick)
                    pick = i;
            if (pick >= 0)
                y += BuildTradeOrders(card, x, y, width, blockCardTrades[pick]);

            return y - top;
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
            var textW = width - 16f - figureW - 8f;
            var name = LedgerV2.Name(row, 16f, -6f, textW, trade.Name, 13.5f, LedgerV2.Ink);
            name.overflowMode = TextOverflowModes.Ellipsis;
            var under = LedgerV2.Mono(row, 16f, -22f, textW,
                trade.Trade.ToLowerInvariant() + " · " + TenureLine(trade), 9f,
                LedgerV2.Label, 1f);
            under.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.Figure(row, width - figureW, -6f, figureW,
                trade.TakePerDay > 0 ? LedgerText.Cash(trade.TakePerDay) : "—", 12.5f,
                trade.TakePerDay > 0 ? LedgerV2.Ink : LedgerV2.Muted);
            LedgerV2.Mono(row, width - figureW, -23f, figureW, TenureWord(trade.Tenure),
                9f, ink, 3f, TextAlignmentOptions.MidlineRight);
            return rowH;
        }

        static string TenureLine(BlockTrade trade) => trade.Tenure switch
        {
            BlockTenure.Ours => "on our paper",
            BlockTenure.Paying => "pays us for peace",
            BlockTenure.Rival => (trade.RivalName ?? "another house") + " holds it",
            _ => "nobody leans on it",
        };

        /// <summary>
        /// What can be asked of one door: who goes, and what they do when they get there.
        /// A job is not instant - the men leave, they travel, they put the hours in, and
        /// the outfit's record says afterwards how it went and who came back hurt.
        /// </summary>
        float BuildTradeOrders(RectTransform card, float x, float top, float width,
            BlockTrade trade)
        {
            var panel = NewRect("Door orders", card);
            PlaceTopLeft(panel, x, -top, width, 1f);
            Fill(panel, LedgerV2.Head);

            var y = 10f;
            var name = LedgerV2.Name(panel, 12f, -y, width - 24f, trade.Name, 15f,
                LedgerV2.HeadCream);
            name.overflowMode = TextOverflowModes.Ellipsis;
            y += 20f;
            Caps(panel, 12f, -y, width - 24f,
                trade.Trade + " · " + TenureWord(trade.Tenure), 9f, LedgerV2.HeadDim, 6f)
                .font = LedgerStyle.Mono;
            y += 18f;

            var note = LedgerV2.Mono(panel, 12f, -y, width - 24f, DoorNote(trade), 10f,
                LedgerV2.HeadDim, 0.5f);
            note.overflowMode = TextOverflowModes.Ellipsis;
            y += 22f;

            y += CrewPicker(panel, 12f, y, width - 24f);
            y += JobKeys(panel, 12f, y, width - 24f, trade);
            y += 12f;

            panel.sizeDelta = new Vector2(width, y);
            return y + 10f;
        }

        string DoorNote(BlockTrade trade) => trade.Tenure switch
        {
            BlockTenure.Ours =>
                "Ours outright. " + LedgerText.Cash(trade.TakePerDay) +
                " a day and it shows clean on the books.",
            BlockTenure.Paying =>
                "Pays for peace. " + LedgerText.Cash(trade.TakePerDay) +
                " a day, collected by whoever stands the block.",
            BlockTenure.Rival =>
                (trade.RivalName ?? "Another house") +
                " holds this door. Taking it means their men answer for it.",
            _ =>
                trade.BuyPrice > 0
                    ? "Nobody leans on it. " + LedgerText.Cash(trade.BuyPrice) +
                      " buys the premises outright."
                    : "Nobody leans on it. A quiet door and an open one.",
        };

        /// <summary>The men who can go: the ones already standing on the block first,
        /// then whoever is idle under the outfit. The pip says whether he is carrying.
        /// </summary>
        float CrewPicker(RectTransform panel, float x, float top, float width)
        {
            Caps(panel, x, -top, width,
                blockCardCrew.Count > 0
                    ? "SEND A CREW · " + blockCardCrew.Count + " PICKED"
                    : "SEND A CREW · PICK MEN, THEN A JOB",
                9f, LedgerV2.HeadDim, 10f).font = LedgerStyle.Mono;
            var y = top + 18f;

            blockCardOffer.Clear();
            for (var i = 0; i < blockCardHands.Count; i++)
                blockCardOffer.Add(blockCardHands[i].Id);
            for (var i = 0; i < organizationPeople.Count && blockCardOffer.Count < 12; i++)
            {
                var person = organizationPeople[i];
                if (!person.IsUnassigned || !person.IsAvailable ||
                    blockCardOffer.Contains(person.Id))
                    continue;
                blockCardOffer.Add(person.Id);
            }

            if (blockCardOffer.Count == 0)
            {
                Line(panel, LedgerStyle.MonoItalic, 10.5f, LedgerV2.Boss, x, -y, width, 18f,
                    "No man of ours is here or idle.");
                return y + 24f - top;
            }

            var roster = director != null ? director.Roster : null;
            const float chipH = 24f;
            const float gap = 5f;
            var cursorX = x;
            for (var i = 0; i < blockCardOffer.Count; i++)
            {
                var manId = blockCardOffer[i];
                var member = roster != null ? roster.Find(manId) : null;
                var label = member != null ? ShortName(member.FirstName, member.Surname)
                    : "#" + manId;
                var armed = IsArmed(roster, manId);
                var on = blockCardCrew.Contains(manId);
                var chipW = Mathf.Min(width, 26f + label.Length * 7.2f);

                if (cursorX > x && cursorX + chipW > x + width)
                {
                    cursorX = x;
                    y += chipH + gap;
                }

                var chip = NewRect("Man " + label, panel);
                PlaceTopLeft(chip, cursorX, -y, chipW, chipH);
                Fill(chip, on ? LedgerV2.Red : LedgerV2.DarkPlate);
                RowButton(chip, ClickSurface(chip), () => ToggleJobMan(manId));
                LedgerV2.StreetMark(chip, 7f, -9f, armed ? TenurePaying : LedgerV2.Boss, 6f);
                Line(chip, LedgerStyle.MonoBold, 10f,
                    on ? LedgerV2.HeadCream : LedgerV2.HeadInk,
                    17f, -5f, chipW - 22f, 14f, label);
                cursorX += chipW + gap;
            }

            y += chipH + 8f;
            LedgerV2.Mono(panel, x, -y, width,
                "the pip says he is carrying · the rest go bare-handed", 9f,
                LedgerV2.HeadDim, 0.5f);
            return y + 16f - top;
        }

        readonly List<int> blockCardOffer = new List<int>();

        bool IsArmed(Roster roster, int memberId)
        {
            if (roster == null)
                return false;
            roster.HeldBy(memberId, blockCardKit);
            for (var i = 0; i < blockCardKit.Count; i++)
                if (RosterOps.IsWeapon(blockCardKit[i].Kind))
                    return true;
            return false;
        }

        static string ShortName(string first, string surname)
        {
            var initial = string.IsNullOrEmpty(surname) ? "" : " " + surname[0] + ".";
            return (string.IsNullOrEmpty(first) ? surname : first) + initial;
        }

        /// <summary>The three jobs, and under them the deed the outfit can strike on the
        /// premises itself. Every one of them is filed, not done.</summary>
        float JobKeys(RectTransform panel, float x, float top, float width, BlockTrade trade)
        {
            var y = top;
            var keyW = Mathf.Min(BlockCardKeyMax, (width - 12f) / 3f);
            var jobs = new[] { "SHAKE IT DOWN", "ROB IT", "SIT ON IT" };
            var types = new[]
            {
                Outfit.OrderType.Extort, Outfit.OrderType.Raid, Outfit.OrderType.Guard,
            };

            var cursorX = x;
            for (var i = 0; i < jobs.Length; i++)
            {
                var type = types[i];
                var target = trade;
                if (cursorX > x && cursorX + keyW > x + width)
                {
                    cursorX = x;
                    y += 32f;
                }
                LedgerV2.Button(panel, jobs[i], cursorX, -y, keyW, 26f,
                    () => FileStreetJob(target, type), LedgerV2.Key.Red, 9f);
                cursorX += keyW + 6f;
            }
            y += 34f;

            if (trade.Tenure == BlockTenure.Open || trade.Tenure == BlockTenure.Paying)
            {
                var target = trade;
                var deedW = Mathf.Min(BlockCardKeyMax, width);
                LedgerV2.Button(panel, "BUY IT OUTRIGHT · " + LedgerText.Cash(trade.BuyPrice),
                    x, -y, deedW, 26f, () => FileBuyPremises(target),
                    LedgerV2.Key.Outline, 9f).color = LedgerV2.HeadCream;
                y += 32f;
            }

            if (trade.HasDoor)
            {
                var target = trade;
                var doorW = Mathf.Min(BlockCardKeyMax, width);
                LedgerV2.Button(panel, "SEND THEM TO THE DOOR", x, -y, doorW, 26f,
                    () => SendToDoor(target), LedgerV2.Key.Outline, 9f)
                    .color = LedgerV2.HeadCream;
                y += 32f;
            }

            return y - top;
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

            var seeAll = LedgerV2.Button(card, "SHOW IT ON THE MAP", x, -y, keyW, 28f,
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
                hand.Duty + (hand.Wage > 0 ? " · " + LedgerText.Cash(hand.Wage) + "/day" : ""),
                9f, LedgerV2.Label, 1f);
            duty.overflowMode = TextOverflowModes.Ellipsis;

            var manId = hand.Id;
            LedgerV2.Button(row, "PULL", width - pullW, -5f, pullW, 24f,
                () => FileHoodRecall(manId), LedgerV2.Key.Ghost, 9f);
            return rowH;
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

        // -------------------------------------------------------------- filing a job

        /// <summary>
        /// Sends the picked men to a door. The job goes into the lieutenant's book like
        /// any other: the men travel, they put the hours in, and the outfit's record says
        /// afterwards what came of it and who came back hurt. Nothing happens at the
        /// click, and a request the outfit cannot honour comes back refused with the
        /// reason on it.
        /// </summary>
        void FileStreetJob(BlockTrade trade, Outfit.OrderType type)
        {
            var word = LedgerText.OrderLabel(type);
            var men = new List<int>(blockCardCrew);
            FileOrder(word + " at " + trade.Name + " asked for.", () =>
            {
                if (men.Count == 0)
                    return Outfit.FilingRuling.Refuse("no men picked for the job");
                if (outfit == null)
                    return Outfit.FilingRuling.Refuse(
                        "the outfit's order book is not open in this scene");

                var roster = director != null ? director.Roster : null;
                if (roster == null)
                    return Outfit.FilingRuling.Refuse("the roster is unavailable");

                // A job belongs to ONE lieutenant's book. Men picked out of two branches
                // is not a crew, and the office will not file it as one.
                var crew = roster.CrewOf(men[0]);
                if (crew == null)
                    return Outfit.FilingRuling.Refuse(
                        "he is under nobody · put him on a branch first");
                for (var i = 1; i < men.Count; i++)
                {
                    var other = roster.CrewOf(men[i]);
                    if (other == null || other.Id != crew.Id)
                        return Outfit.FilingRuling.Refuse(
                            "men from two branches do not ride together");
                }

                var job = new Outfit.Job
                {
                    CrewId = crew.Id,
                    Type = type,
                    Men = men.Count,
                    TargetBlockId = LegacyBlockOf(blockCardId),
                    TargetX = trade.HasDoor ? trade.Door.x : 0f,
                    TargetZ = trade.HasDoor ? trade.Door.z : 0f,
                    TargetLabel = trade.Name,
                };

                var result = outfit.IssueOrder(job);
                return result.Ok
                    ? Outfit.FilingRuling.Grant(
                        men.Count + (men.Count == 1 ? " man goes" : " men go") +
                        " · they leave as soon as they are free")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
            blockCardCrew.Clear();
        }

        /// <summary>Buys the premises outright. The asking price is the one the economy
        /// table gives that trade, so a barber and a casino are not the same money.
        /// </summary>
        void FileBuyPremises(BlockTrade trade)
        {
            var men = new List<int>(blockCardCrew);
            FileOrder(trade.Name + " bought outright. " +
                      LedgerText.Cash(trade.BuyPrice) + " committed.", () =>
            {
                if (outfit == null)
                    return Outfit.FilingRuling.Refuse(
                        "the outfit's order book is not open in this scene");
                var roster = director != null ? director.Roster : null;
                var crew = roster != null && men.Count > 0 ? roster.CrewOf(men[0]) : null;
                if (crew == null && roster != null && roster.Crews.Count > 0)
                    crew = roster.Crews[0];
                if (crew == null)
                    return Outfit.FilingRuling.Refuse("no crew to send about it");
                if (outfit.Accounts.Safe < trade.BuyPrice)
                    return Outfit.FilingRuling.Refuse(
                        "the safe does not cover " + LedgerText.Cash(trade.BuyPrice));

                var job = new Outfit.Job
                {
                    CrewId = crew.Id,
                    Type = Outfit.OrderType.BuyPremises,
                    Men = Mathf.Max(1, men.Count),
                    TargetBlockId = LegacyBlockOf(blockCardId),
                    TargetX = trade.HasDoor ? trade.Door.x : 0f,
                    TargetZ = trade.HasDoor ? trade.Door.z : 0f,
                    TargetLabel = trade.Name,
                    TargetWorth = trade.BuyPrice,
                };

                var result = outfit.IssueOrder(job);
                return result.Ok
                    ? Outfit.FilingRuling.Grant("the paperwork is with them")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
            blockCardCrew.Clear();
        }

        /// <summary>Walks a crew to the premises' own doorstep. The demand follows when
        /// they arrive - a click from across the city is an intent, not an interaction,
        /// and the racket layer says so.</summary>
        void SendToDoor(BlockTrade trade)
        {
            var runtime = TerritoryRuntime.Instance;
            var roster = director != null ? director.Roster : null;
            if (runtime?.Commands == null || roster == null)
            {
                organizationNote = "the territory command gateway is unavailable";
                dirty = true;
                return;
            }

            var crew = blockCardCrew.Count > 0 ? roster.CrewOf(blockCardCrew[0]) : null;
            if (crew == null && roster.Crews.Count > 0)
                crew = roster.Crews[0];
            if (crew == null || !runtime.TryGetCrewNode(crew.Id, out var node))
            {
                organizationNote = "no crew of ours is on the street to send";
                dirty = true;
                return;
            }

            var sent = runtime.Commands.Submit(
                new ApproachBusinessCommand(node, trade.Id));
            organizationNote = sent.Status == TerritoryCommandStatus.Rejected
                ? sent.Reason
                : "they are on their way to the door";
            dirty = true;
        }

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
                if (crew == null && blockCardCrew.Count > 0)
                    crew = roster.CrewOf(blockCardCrew[0]);
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

        int LegacyBlockOf(TerritoryBlockId blockId)
        {
            var geography = TerritoryRuntime.Instance?.Geography;
            return geography != null && geography.TryGetBlock(blockId, out var block)
                ? block.LegacyBlockId
                : -1;
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
