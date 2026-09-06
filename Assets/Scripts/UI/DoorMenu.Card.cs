using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Business;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Territory;
using RoadDemo;
using static LivingCity.UI.LedgerKit;
using DoorTenure = LivingCity.Outfit.DoorTenure;
using OrderType = LivingCity.Outfit.OrderType;

namespace LivingCity.UI
{
    /// <summary>
    /// THE PREMISES CARD - what the door menu LOOKS like.
    ///
    /// Built to the "Premises Popup" handoff (Docs/design-briefs/premises-popup-brief.md):
    /// a file band with the day on it, the premises and who holds it, the man behind the
    /// counter with his nerve read off in pips, three leader rows of money and heat, and
    /// then the moves - grouped into dropdowns ordered by consequence, one open at a
    /// time, with a two-step confirm in front of everything that cannot be taken back.
    /// It replaces a flat wall of ten same-weight keys where torching a building sat
    /// beside knocking on its door.
    ///
    /// Nothing here decides a rule. Every row is still TerritoryRacketOrders', every
    /// figure still the economy table's, every refusal still the shared table's word.
    /// This file only says where they stand and what they are set in - which is why it
    /// is one surface and not three: the ledger's block drawer, the turf plate and the
    /// street all paint THIS, so a section added here appears on all of them.
    /// </summary>
    public static partial class DoorMenu
    {
        // ------------------------------------------------------------- the measures

        /// <summary>
        /// The handoff's card is drawn 420 wide and read at 1.12, and its note says to
        /// build it at the read size rather than copy the zoom. So every length below is
        /// the design's own px through <see cref="Px"/>, and every type size is that px
        /// through the face's measured optical (LedgerStyle.FromPx) - the two conversions
        /// the street HUD was built to as well. Nothing here is eyeballed.
        ///
        /// The card is measured against the 1920x1080 ladder, which is the canvas the
        /// menu already owns on every surface it opens on: its own floating host, and
        /// the ledger book's drawer.
        /// </summary>
        const float Zoom = 1.12f;

        static float Px(float px) => px * Zoom;

        /// <summary>A design px set in the mono face.</summary>
        static float MonoPx(float px) =>
            LedgerStyle.FromPx(px * Zoom, LedgerStyle.MonoOptical);

        /// <summary>A design px set in the condensed face.</summary>
        static float CondPx(float px) =>
            LedgerStyle.FromPx(px * Zoom, LedgerStyle.CondensedOptical);

        /// <summary>Whether this card is being painted inside the book, whose small
        /// print is lifted (LedgerKit.BookSize) because it is read further away.</summary>
        static bool inBook;

        /// <summary>The size a size actually prints at, here.</summary>
        static float Print(float size) => inBook ? BookSize(size) : size;

        /// <summary>
        /// THE WIDTH A MONO WORD TAKES, and it has to be the true one: this card lays
        /// out by measurement - the key, then the leader, then the note held to the
        /// right margin - and a width guessed 20% wide drops notes onto a second line
        /// that had room on the first.
        ///
        /// IBM Plex Mono advances 0.6 em, the tracking is em hundredths, and BOTH are
        /// drawn at the face's measured optical (LedgerStyle.MonoOptical), which is what
        /// LedgerV2.MonoWidth leaves out - it was written for the paper sheets, where a
        /// generous reserve costs nothing.
        /// </summary>
        static float Wide(string word, float size, float spacing) =>
            string.IsNullOrEmpty(word)
                ? 0f
                : word.Length * Print(size) * LedgerStyle.MonoOptical *
                  (0.6f + spacing / 100f);

        /// <summary>The card's fixed width - the design's 420 at the size it is read
        /// at. A surface with less room than this clamps it; nothing widens it.</summary>
        public const float MaxWidth = 470f;

        // ------------------------------------------------------------- the sections

        public const string SectionCrew = "crew";
        public const string SectionDoor = "door";
        public const string SectionLean = "lean";
        public const string SectionNoWay = "noway";
        public const string SectionHouse = "house";

        /// <summary>
        /// Which dropdown stands open. ONE at a time: opening a section closes whichever
        /// was open, because a card with four open sections is the wall of keys this
        /// design exists to replace. The door is open by default - it is the first thing
        /// anybody does to a shop.
        /// </summary>
        public static string OpenSection { get; private set; } = SectionDoor;

        /// <summary>Which move is waiting on a second word, or null. Irreversible moves
        /// only; everything else fires on the press.</summary>
        public static string Armed { get; private set; }

        /// <summary>Whether an irreversible move asks twice. The handoff's own switch;
        /// off, they fire on the first press like any other row.</summary>
        public static bool TwoStepConfirm = true;

        public static void ShowSection(string key)
        {
            OpenSection = OpenSection == key ? null : key;
            Version++;
        }

        static void Arm(string key, string verb)
        {
            Armed = key;
            Say(verb + " - waiting on your word");
        }

        static void CallOff()
        {
            Armed = null;
            Say("Called off. Nothing sent.");
        }

        // ------------------------------------------------------------------- the card

        /// <summary>
        /// Paints the whole menu as a card under <paramref name="parent"/> and answers
        /// it, sized. The caller places it: the ledger level with its row, the map beside
        /// the shop that was clicked. <paramref name="changed"/> is called when the
        /// menu's own state moves and the surface must repaint; <paramref name="close"/>
        /// is the X in its corner, and no X is drawn when there is nothing to close.
        /// </summary>
        public static RectTransform Open(Transform parent, Door door, float width,
            Action changed, Action close,
            DoorDispatch dispatch = DoorDispatch.PickedOrStreet,
            bool showCommands = true)
        {
            var panel = LedgerV2.Card("Door menu", parent, 0f, 0f, width, 1f,
                LedgerV2.Head);
            // It is laid OVER whatever it opened on, so it must also stop the clicks the
            // rows beneath it would otherwise answer.
            ClickSurface(panel);

            inBook = parent != null &&
                     parent.GetComponentInParent<PersonnelAlmanac>(true) != null;

            var y = FileBand(panel, width, door, close);
            y = Body(panel, width, y, door, changed, dispatch, showCommands);

            PlaceTopLeft(panel, 0f, 0f, width, y);
            return panel;
        }

        /// <summary>The file band: what this is, which file it is, what day it is, and
        /// the way out.</summary>
        static float FileBand(RectTransform panel, float width, Door door, Action close)
        {
            var h = Px(46f);
            Block("File band", panel, 0f, 0f, width, h, LedgerStyle.Chrome);
            Rule(panel, 0f, -(h - 1f), width, LedgerStyle.ChromeRule);

            var pad = Px(10f);
            var right = width - pad;

            if (close != null)
            {
                var keyW = Px(30f);
                var keyH = Px(30f);
                LedgerV2.Button(panel, "X", right - keyW, -(h - keyH) * 0.5f, keyW, keyH,
                    () => close(), LedgerV2.Key.Ghost, MonoPx(8.7f));
                right -= keyW + Px(6f);
            }

            var outfit = Gameplay.OutfitDirector.Instance;
            var day = "DAY " + (outfit ? outfit.Campaign.Day : 1);
            var dayW = Wide(day, MonoPx(9f), 8f);
            LedgerV2.Cell(panel, right - dayW, 0f, dayW, h, day, MonoPx(9f),
                LedgerStyle.RailLabel, 8f, TextAlignmentOptions.MidlineRight);
            right -= dayW + Px(10f);

            LedgerV2.Cell(panel, pad, 0f, Mathf.Max(40f, right - pad), h,
                "PREMISES · FILE " + FileNumber(door.Id), MonoPx(9.4f),
                LedgerStyle.RailBright, 12f, TextAlignmentOptions.MidlineLeft,
                LedgerStyle.MonoBold);
            return h;
        }

        /// <summary>
        /// THE FILE NUMBER, off the door's own id and nothing else - so the same shop
        /// carries the same number every time it is opened, in every session of the same
        /// city, and two shops never share one. A hash written out here rather than
        /// String.GetHashCode, which is not promised to be stable between runs.
        /// </summary>
        static string FileNumber(TerritoryBusinessId id)
        {
            var word = id.IsValid ? id.Value : "";
            unchecked
            {
                var hash = 2166136261u;
                for (var i = 0; i < word.Length; i++)
                    hash = (hash ^ word[i]) * 16777619u;
                return (hash % 900u + 100u) + "-" +
                       (char)('A' + (int)(hash / 900u % 26u));
            }
        }

        // ------------------------------------------------------------------- the body

        static float Body(RectTransform panel, float width, float top, Door door,
            Action changed, DoorDispatch dispatch, bool showCommands)
        {
            var x = Px(14f);
            var w = width - x * 2f;
            var y = top + Px(12f);

            y = NameRow(panel, x, y, w, door);
            y += Px(11f);
            y = OwnerStrip(panel, x, y, w, door);
            y += Px(10f);
            y = Readings(panel, x, y, w, door);
            y += Px(10f);

            // THE DOOR'S OWN SENTENCE, which the handoff has no slot for and this game
            // cannot do without: it carries what we are worth to this man against what
            // he wants (ECON-002/007), and a card that hides that is a card whose keys
            // are guesses. It wraps, because the half of that line that would be cut is
            // exactly the half the reader came for.
            y = DoorLine(panel, x, y, w, door, showCommands);

            if (showCommands)
            {
                y += Px(12f);
                y = Sections(panel, x, y, w, door, changed, dispatch);
                y = Foot(panel, x, y, w, door, changed, dispatch);
            }

            return y + Px(14f);
        }

        /// <summary>The premises, what trade it is, who it pays, and the chip that says
        /// whose door it is.</summary>
        static float NameRow(RectTransform panel, float x, float y, float w, Door door)
        {
            var chipWord = TenureWord(door.Tenure);
            var chipH = Px(22f);
            var chipW = Wide(chipWord, MonoPx(8.7f), 6f) + Px(20f);
            var nameH = Px(24f);
            var subH = Px(13f);
            var blockH = nameH + subH;

            LedgerV2.Status(panel, x + w - chipW, -(y + (blockH - chipH) * 0.5f),
                chipW, chipH, chipWord, ChipInk(door.Tenure), MonoPx(8.7f));

            var textW = w - chipW - Px(10f);
            LedgerV2.Cell(panel, x, -y, textW, nameH, door.Name.ToUpperInvariant(),
                CondPx(21f), LedgerStyle.RailValue, 2f, TextAlignmentOptions.MidlineLeft,
                LedgerStyle.Condensed);
            LedgerV2.Cell(panel, x, -(y + nameH), textW, subH, SubLine(door),
                MonoPx(9.8f), LedgerStyle.RailLabel, 4.5f);
            return y + blockH;
        }

        /// <summary>What the chip is filled with - the map's own four ownership inks,
        /// except that an unclaimed door takes the design's amber: the map's open grey is
        /// a colour for TEXT and cream on it cannot be read.</summary>
        static Color ChipInk(DoorTenure tenure) => tenure switch
        {
            DoorTenure.Ours => TenureOurs,
            DoorTenure.Paying => TenurePaying,
            DoorTenure.Rival => TenureRival,
            _ => LedgerStyle.RailAmber,
        };

        static string SubLine(Door door)
        {
            var pays = door.Tenure switch
            {
                DoorTenure.Ours => "ON OUR OWN PAPER",
                DoorTenure.Paying => "PAYS US",
                DoorTenure.Rival => "PAYS " + (door.RivalName ?? "ANOTHER HOUSE"),
                _ => "PAYS NOBODY",
            };
            var role = string.IsNullOrEmpty(door.Role) ? "" : door.Role + " · ";
            return (role + door.Trade + " · " + pays).ToUpperInvariant();
        }

        // ------------------------------------------------------------------ the gazda

        /// <summary>
        /// THE MAN BEHIND THE COUNTER: his face flush in the strip's left edge, his name,
        /// what the deed makes him, and the reading the racket actually weighs him by -
        /// his nerve, in pips, with the line that says what that nerve MEANS at his door.
        /// Every one of them is the city's own: the deed (BusinessOwners) names him and
        /// carries the seed his face is dealt from, the economy (TerritoryOwnerProfile)
        /// says what he is like. Nothing is spawned for him - a deed is a name, not an
        /// actor - and a firm or City Hall gets no photograph, because neither is a man.
        /// </summary>
        static float OwnerStrip(RectTransform panel, float x, float y, float w, Door door)
        {
            if (string.IsNullOrEmpty(door.OwnerName))
                return y;

            var h = Px(66f);
            Block("Owner strip", panel, x, -y, w, h, LedgerStyle.Rail);

            var plate = LedgerV2.PortraitPlate(panel, x, -y, h, h,
                Initials(door.OwnerName), LedgerV2.DarkPlate, LedgerV2.DarkPlateInk);
            if (!string.IsNullOrEmpty(door.OwnerFace))
                PortraitStudio.Request(PortraitStudio.FindPeoplePrefab(door.OwnerFace),
                    PortraitStudio.Framing.Bust, plate);
            else if (door.OwnerKind == BusinessOwnerKind.Individual)
                PortraitStudio.Request(
                    PortraitStudio.CivilianPrefab(door.PortraitSeed, FemaleDeed(door.OwnerName)),
                    PortraitStudio.Framing.Bust, plate);

            var house = door.RoleGang >= 0;
            var tx = x + h + Px(11f);
            var tw = w - h - Px(11f) - Px(10f);
            var ty = y + Px(8f);

            LedgerV2.Cell(panel, tx, -ty, tw, Px(19f), door.OwnerName.ToUpperInvariant(),
                CondPx(16f), LedgerStyle.RailValue, 4f, TextAlignmentOptions.MidlineLeft,
                LedgerStyle.Condensed);
            ty += Px(19f) + Px(4f);

            LedgerV2.Cell(panel, tx, -ty, tw, Px(12f),
                house
                    ? "BOSS · " + GangName(door.RoleGang).ToUpperInvariant()
                    : KindWord(door.OwnerKind) + " · " + TraitWord(door.Trait),
                MonoPx(9.8f), LedgerStyle.RailLabel, 4.5f);
            ty += Px(12f) + Px(4f);

            // A house's man is read by his HOUSE, not by how easily he folds: nobody
            // leans on a family's own front, so there is no nerve to take here.
            if (house)
            {
                LedgerV2.Cell(panel, tx, -ty, tw, Px(12f), "THE HOUSE'S OWN PREMISES",
                    MonoPx(9.8f), LedgerStyle.RailNote, 4.5f);
            }
            else
            {
                var labelW = Wide("NERVE", MonoPx(9.8f), 4.5f);
                LedgerV2.Cell(panel, tx, -ty, labelW, Px(12f), "NERVE",
                    MonoPx(9.8f), LedgerStyle.RailLabel, 4.5f);

                var pipsX = tx + labelW + Px(7f);
                LedgerV2.Pips(panel, pipsX, -(ty + Px(6f)), 6,
                    Mathf.RoundToInt(Mathf.Clamp01(door.Nerve) * 6f),
                    LedgerStyle.RailGold, Px(7f), Px(7f), Px(9f), LedgerStyle.RailHair);

                var noteX = pipsX + LedgerV2.PipsWidth(6, Px(7f), Px(9f)) + Px(7f);
                LedgerV2.Cell(panel, noteX, -ty, Mathf.Max(20f, tx + tw - noteX), Px(12f),
                    TraitLine(door.Trait).ToUpperInvariant(),
                    MonoPx(9.8f), LedgerStyle.RailNote, 4.5f);
            }

            y += h;

            // The deed changed hands and the door remembers it (EPIC 38). It is not a
            // reading of the man, so it stands under his strip rather than inside it.
            if (!house && door.OwnerGeneration > 0)
            {
                y += Px(6f);
                LedgerV2.Cell(panel, x, -y, w, Px(13f), BusinessSuccession.MemoryLine,
                    MonoPx(9.8f), LedgerStyle.RedPen, 4.5f);
                y += Px(13f);
            }

            return y;
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
            TerritoryOwnerTrait.Cowardly => "folds a step early",
            TerritoryOwnerTrait.Proud => "costs an extra lean",
            TerritoryOwnerTrait.Greedy => "parting with the cut hurts",
            TerritoryOwnerTrait.Connected => "leaning on him draws the precinct",
            TerritoryOwnerTrait.Stubborn => "takes a war to move",
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

        // --------------------------------------------------------------- the readings

        /// <summary>The three leader rows: what the week is worth, what the ground is
        /// under, and what the deed costs.</summary>
        static float Readings(RectTransform panel, float x, float y, float w, Door door)
        {
            var rowH = Px(15f);
            var gap = Px(6f);

            if (door.TakePerWeek > 0)
            {
                // The same figure means two things and the LABEL is what says which: a
                // door that pays us nothing is not taking anything, it is quoting.
                Leader(panel, x, y, w, rowH,
                    door.PaysUs ? "TAKES, A WEEK" : "WOULD PAY, A WEEK",
                    LedgerText.Cash(door.TakePerWeek), LedgerStyle.RailValue);
                y += rowH + gap;
            }

            LeaderPips(panel, x, y, w, rowH, "HEAT ON THE BLOCK",
                Mathf.RoundToInt(Mathf.Clamp01(door.Heat) * 6f));
            y += rowH + gap;

            if (door.BuyPrice > 0)
            {
                Leader(panel, x, y, w, rowH, "BUYS OUTRIGHT",
                    LedgerText.Cash(door.BuyPrice), LedgerStyle.RailSafeGold);
                y += rowH + gap;
            }

            return y - gap;
        }

        static void Leader(RectTransform panel, float x, float y, float w, float rowH,
            string label, string figure, Color ink)
        {
            var labelW = Wide(label, MonoPx(9.8f), 4.5f);
            var figureW = Wide(figure, MonoPx(11.6f), 0f);
            LedgerV2.Cell(panel, x, -y, labelW, rowH, label, MonoPx(9.8f),
                LedgerStyle.RailLabel, 4.5f);
            Dots(panel, x + labelW + Px(8f), y + rowH * 0.68f,
                w - labelW - figureW - Px(16f));
            LedgerV2.Cell(panel, x + w - figureW, -y, figureW, rowH, figure,
                MonoPx(11.6f), ink, 0f, TextAlignmentOptions.MidlineRight,
                LedgerStyle.MonoBold);
        }

        static void LeaderPips(RectTransform panel, float x, float y, float w, float rowH,
            string label, int filled)
        {
            var labelW = Wide(label, MonoPx(9.8f), 4.5f);
            var pipsW = LedgerV2.PipsWidth(6, Px(7f), Px(9f));
            LedgerV2.Cell(panel, x, -y, labelW, rowH, label, MonoPx(9.8f),
                LedgerStyle.RailLabel, 4.5f);
            Dots(panel, x + labelW + Px(8f), y + rowH * 0.68f,
                w - labelW - pipsW - Px(16f));
            LedgerV2.Pips(panel, x + w - pipsW, -(y + rowH * 0.5f), 6, filled,
                LedgerStyle.RailRed, Px(7f), Px(7f), Px(9f), LedgerStyle.RailHair);
        }

        /// <summary>The dotted leader between a label and the figure that answers it.
        /// A run shorter than a dot is not drawn at all - a one-unit leader reads as a
        /// smudge between two words that were meant to be spaced.</summary>
        static void Dots(RectTransform panel, float x, float y, float w)
        {
            if (w >= Px(6f))
                DottedRule(panel, x, -y, w, LedgerStyle.ChromeRule);
        }

        /// <summary>The door's own sentence, wrapped. In the dossier (a left click in the
        /// city, no commands) it is the whole of what the card has to say, so it is given
        /// more room there.</summary>
        static float DoorLine(RectTransform panel, float x, float y, float w, Door door,
            bool showCommands)
        {
            var size = MonoPx(9.8f);
            var max = showCommands ? LineBox(size, 3) : LineBox(size, 6);
            var line = Paragraph(panel, LedgerStyle.Mono, size, LedgerStyle.RailNote,
                x, -y, w, max, Sentence(door), 2f);
            return y + Mathf.Clamp(line.preferredHeight, LineBox(size), max);
        }

        // --------------------------------------------------------------- the sections

        /// <summary>Which dropdown a shared row belongs under. The GROUPING is the whole
        /// point of the redesign - a reader must not meet "go to the door" and "torch it"
        /// as two keys of equal weight - and it is a reading of the row's own kind and
        /// order type, never a second table of what may be done.</summary>
        enum Bucket
        {
            /// <summary>Knocking, asking, collecting. Nothing burns.</summary>
            Door,

            /// <summary>Fear and damage: a threat, a beating, a wrecked front, a till.</summary>
            Lean,

            /// <summary>What cannot be undone: the building, the man.</summary>
            NoWay,

            /// <summary>Only offered on our own paper - quarters, the hideout, repairs.
            /// The handoff has no section for these because its florist is a stranger's
            /// shop; they exist in this game and a row that vanishes teaches nothing.
            /// </summary>
            House,

            /// <summary>The money moves, which stand under the sections on their own.</summary>
            Foot,
        }

        static Bucket BucketOf(TerritoryRacketOrder row)
        {
            switch (row.Kind)
            {
                case TerritoryDoorRowKind.Racket:
                    return row.Intent == TerritoryRacketIntent.Threaten
                        ? Bucket.Lean
                        : Bucket.Door;
                case TerritoryDoorRowKind.Repair:
                case TerritoryDoorRowKind.Quarters:
                case TerritoryDoorRowKind.Hideout:
                    return Bucket.House;
                default:
                    switch (row.Job)
                    {
                        case OrderType.Beating:
                        case OrderType.SmashUp:
                        case OrderType.Raid:
                            return Bucket.Lean;
                        case OrderType.Torch:
                        case OrderType.KillOwner:
                            return Bucket.NoWay;
                        case OrderType.Guard:
                        case OrderType.BuyPremises:
                            return Bucket.Foot;
                        default:
                            return Bucket.Door;
                    }
            }
        }

        /// <summary>A move that cannot be taken back, and therefore asks twice.</summary>
        static bool Irreversible(TerritoryRacketOrder row) =>
            row.Kind == TerritoryDoorRowKind.Job &&
            (row.Job == OrderType.Beating || row.Job == OrderType.SmashUp ||
             row.Job == OrderType.Raid || row.Job == OrderType.Torch ||
             row.Job == OrderType.KillOwner);

        /// <summary>What the card warns before it takes a second word - the consequence
        /// itself, in the crew's own terms, not a shrug about being careful.</summary>
        static string Warning(TerritoryRacketOrder row) => row.Job switch
        {
            OrderType.Beating => "He will remember the faces.",
            OrderType.SmashUp => "Window, cooler, till. He rebuilds or he pays.",
            OrderType.Raid => "The till, once, and he knows who took it.",
            OrderType.Torch => "There is no premises after this.",
            OrderType.KillOwner => "A body brings the precinct.",
            _ => "This cannot be taken back.",
        };

        /// <summary>The identity an armed move is remembered by across a repaint. It has
        /// to survive the card being torn down and rebuilt, so it is the ROW's own
        /// description and not a reference to anything drawn.</summary>
        static string RowKey(TerritoryRacketOrder row) =>
            row.Kind == TerritoryDoorRowKind.Racket
                ? "racket:" + row.Intent
                : row.Kind + ":" + row.Job + ":" + row.Label;

        static readonly List<TerritoryRacketOrder> orders = new List<TerritoryRacketOrder>();

        /// <summary>
        /// Every dropdown, and under them the confirm strip when a move is armed.
        ///
        /// The rows come from the ONE shared table, asked once. A surface that wrote a
        /// key of its own is the bug that table exists to prevent, and a card that
        /// re-asked per section would let two sections disagree about whether the same
        /// crew is picked.
        /// </summary>
        static float Sections(RectTransform panel, float x, float y, float w, Door door,
            Action changed, DoorDispatch dispatch)
        {
            var roster = Book();
            var restricted = dispatch == DoorDispatch.BlockResponsibility;
            if (restricted)
                ConstrainToBlock(roster, door.Block, CrewMissionPicker.Physical());

            var going = CrewToSend(door.Block, dispatch, out _, out var refusal, out _);
            var solo = SelectedPersonId >= 0;
            var soloReason = solo ? SoloRefusal(door, dispatch) : null;
            var hasCrew = going != null || (solo && soloReason == null);

            TerritoryRacketOrders.For(
                door.Standing, door.Tenure, racketable: true, hasCrew,
                MenAtDoor(door.Id), door.BuyPrice, orders,
                closure: door.Closure,
                quarters: CrewQuarters.State(StreetUnit(going), door.Id),
                isHideout: door.IsHideout,
                inGoodStanding: door.InGoodStanding);

            var gap = Px(5f);

            // WHO GOES first, because every row under it is refused until it is answered.
            y += CrewSection(panel, x, y, w, door, changed, dispatch,
                going, refusal, solo, soloReason) + gap;

            // The armed row is picked up while the sections are drawn, so the strip under
            // them commits the same press the row would have.
            Action commit = null;
            var armedVerb = "";
            var armedWarning = "";

            Step(Bucket.Door, SectionDoor, "AT THE DOOR", "NO HEAT",
                LedgerStyle.RailNote, default);
            Step(Bucket.Lean, SectionLean, "LEAN ON IT", "HEAT",
                LedgerStyle.RailGold, default);
            Step(Bucket.NoWay, SectionNoWay, "NO WAY BACK", "FINAL",
                LedgerStyle.RailRed, LedgerV2.Red);
            Step(Bucket.House, SectionHouse, "OUR OWN DOOR", "OURS",
                LedgerStyle.RailSafeGold, LedgerStyle.RailSafeGold);

            void Step(Bucket bucket, string key, string label, string word, Color ink,
                Color accent)
            {
                var took = MoveSection(panel, x, y, w, bucket, key, label, word, ink,
                    accent, door, changed, dispatch, solo, soloReason,
                    ref commit, ref armedVerb, ref armedWarning);
                // A section with no rows in it is not drawn, and must not leave the air
                // between two sections behind either.
                if (took > 0f)
                    y += took + gap;
            }

            // An armed move whose row is gone - the standing moved under the card, the
            // crew was taken off it - must not leave a strip offering to commit nothing.
            if (Armed != null && commit == null)
                Armed = null;

            if (commit != null)
                y += Px(10f) + Confirm(panel, x, y + Px(10f), w, armedVerb, armedWarning,
                    commit, changed);

            return y;
        }

        /// <summary>
        /// WHO GOES: the crews, then the men who can be sent alone, as rows in a dropdown
        /// whose head carries the answer. The handoff draws three crew rows; this game
        /// deals as many as the roster has, and a single man is a real choice here (a
        /// demand or a threat can be put by one), so the reserve stands under them.
        /// </summary>
        static float CrewSection(RectTransform panel, float x, float y, float w, Door door,
            Action changed, DoorDispatch dispatch, Crew going, string refusal,
            bool solo, string soloReason)
        {
            var roster = Book();
            var restricted = dispatch == DoorDispatch.BlockResponsibility;
            var crews = new List<Crew>();
            var reserve = new List<Character>();
            BlockMissionChoice.Collect(roster, door.Block, restricted, crews, reserve,
                CrewMissionPicker.Physical());

            var picked = solo
                ? (roster?.Find(SelectedPersonId)?.FullName ?? "ONE MAN")
                : going != null
                    ? BlockMissionChoice.Label(roster, going)
                    : "NOBODY PICKED";
            var open = OpenSection == SectionCrew;
            var h = Head(panel, x, y, w, SectionCrew, "WHO GOES", picked.ToUpperInvariant(),
                going != null || solo ? LedgerStyle.RailSafeGold : LedgerStyle.RailRed,
                LedgerStyle.RailSafeGold, open, MonoPx(10.6f));
            if (!open)
                return h;

            var body = y + h;
            var rowH = Px(27f);
            var count = crews.Count + reserve.Count;
            var bodyH = count > 0 ? rowH * count : Px(27f);

            Block("Crew body", panel, x, -body, w, bodyH + 1f, LedgerStyle.Chrome);
            Rule(panel, x, -body, w, LedgerStyle.ChromeRule);
            var ry = body + 1f;

            for (var i = 0; i < crews.Count; i++)
            {
                var crew = crews[i];
                var why = BlockMissionChoice.Refusal(roster, door.Block, crew.Id, restricted);
                var men = Outfit.CrewKit.MenOf(roster, crew);
                CrewRow(panel, x, ry, w, rowH,
                    BlockMissionChoice.Label(roster, crew),
                    why ?? men + (men == 1 ? " MAN" : " MEN"),
                    why == null, crew.Id == SelectedCrewId,
                    () => { ToggleCrew(crew.Id); ShowSection(SectionCrew); changed?.Invoke(); });
                ry += rowH;
            }

            for (var i = 0; i < reserve.Count; i++)
            {
                var man = reserve[i];
                var busy = roster.DoorOrders.Find(man.Id) != null;
                CrewRow(panel, x, ry, w, rowH, man.FullName,
                    busy ? "already on a doorstep errand" : "ONE MAN · NO WITNESSES",
                    !busy, man.Id == SelectedPersonId,
                    () => { TogglePerson(man.Id); ShowSection(SectionCrew); changed?.Invoke(); });
                ry += rowH;
            }

            if (count == 0)
                LedgerV2.Cell(panel, x + Px(24f), -ry, w - Px(34f), Px(27f),
                    string.IsNullOrEmpty(refusal) ? "No men are available to send." : refusal,
                    MonoPx(9.8f), LedgerStyle.RailNote, 4.5f);

            return h + bodyH + 1f;
        }

        static void CrewRow(RectTransform panel, float x, float y, float w, float rowH,
            string label, string note, bool available, bool picked, Action press)
        {
            var row = NewRect("Crew " + label, panel);
            PlaceTopLeft(row, x, -y, w, rowH);
            if (available)
                Pressable(row, LedgerStyle.Chrome, LedgerStyle.Rail, press);
            else
                Fill(row, LedgerStyle.Chrome);

            // The picked crew is struck with the safe gold down its edge, which is the
            // colour the head answers in - a reader must be able to see the answer and
            // where it came from in one look.
            if (picked)
                Block("Picked", row, 0f, 0f, Px(3f), rowH, LedgerStyle.RailSafeGold);

            var lx = Px(24f);
            var labelW = Wide(label, MonoPx(10.6f), 3f);
            var noteW = Wide(note, MonoPx(9.8f), 4.5f);
            labelW = Mathf.Min(labelW, w - lx - Px(20f));
            LedgerV2.Cell(row, lx, 0f, labelW, rowH, label.ToUpperInvariant(),
                MonoPx(10.6f), available ? LedgerStyle.RailValue : LedgerStyle.RailLabel,
                3f);
            noteW = Mathf.Min(noteW, w - lx - labelW - Px(26f));
            if (noteW > Px(10f))
            {
                Dots(panel, x + lx + labelW + Px(8f), y + rowH * 0.68f,
                    w - lx - labelW - noteW - Px(26f));
                LedgerV2.Cell(row, w - Px(10f) - noteW, 0f, noteW, rowH, note,
                    MonoPx(9.8f), LedgerStyle.RailNote, 4.5f,
                    TextAlignmentOptions.MidlineRight);
            }
        }

        /// <summary>One dropdown of moves, and every row in it. Answers the height it
        /// took; a section with no rows in it is not drawn at all.</summary>
        static float MoveSection(RectTransform panel, float x, float y, float w,
            Bucket bucket, string key, string label, string word, Color wordInk,
            Color accent, Door door, Action changed, DoorDispatch dispatch,
            bool solo, string soloReason,
            ref Action commit, ref string armedVerb, ref string armedWarning)
        {
            var count = 0;
            for (var i = 0; i < orders.Count; i++)
                if (BucketOf(orders[i]) == bucket)
                    count++;
            if (count == 0)
                return 0f;

            var open = OpenSection == key;
            var h = Head(panel, x, y, w, key, label,
                count + (count == 1 ? " MOVE · " : " MOVES · ") + word, wordInk, accent,
                open, MonoPx(9.8f));
            if (!open)
                return h;

            var pad = Px(10f);
            var gap = Px(6f);
            var rowH = Px(34f);
            var bodyRows = new List<int>();
            for (var i = 0; i < orders.Count; i++)
                if (BucketOf(orders[i]) == bucket)
                    bodyRows.Add(i);

            var innerW = w - pad * 2f;
            var heights = new float[bodyRows.Count];
            var bodyH = Px(8f) * 2f;
            for (var i = 0; i < bodyRows.Count; i++)
            {
                heights[i] = MoveRowHeight(orders[bodyRows[i]], innerW, rowH);
                bodyH += heights[i] + (i > 0 ? gap : 0f);
            }

            var body = y + h;
            Block("Move body", panel, x, -body, w, bodyH + 1f, LedgerStyle.Chrome);
            Rule(panel, x, -body, w, LedgerStyle.ChromeRule);

            var ry = body + 1f + Px(8f);
            for (var i = 0; i < bodyRows.Count; i++)
            {
                var row = orders[bodyRows[i]];
                MoveRow(panel, x + pad, ry, innerW, heights[i], rowH, row, bucket, door,
                    changed, dispatch, solo, soloReason,
                    ref commit, ref armedVerb, ref armedWarning);
                ry += heights[i] + gap;
            }

            return h + bodyH + 1f;
        }

        /// <summary>
        /// A section head: the caret, the section's name, a leader, and the reading that
        /// says what is under it without opening it. Clicking one opens it and closes
        /// whichever was open.
        /// </summary>
        static float Head(RectTransform panel, float x, float y, float w, string key,
            string label, string word, Color wordInk, Color accent, bool open, float wordSize)
        {
            var h = Px(29f);
            var row = NewRect("Head " + label, panel);
            PlaceTopLeft(row, x, -y, w, h);
            Pressable(row, LedgerStyle.Rail, LedgerStyle.RailTrough, () => ShowSection(key));

            var lx = Px(10f);
            if (accent.a > 0f)
            {
                Block("Accent", row, 0f, 0f, Px(3f), h, accent);
                lx += Px(3f);
            }

            LedgerKit.Caret(row, lx, -h * 0.5f, Px(8.4f), open, LedgerStyle.RailLabel);
            lx += Px(8.4f) + Px(8f);

            var labelW = Wide(label, MonoPx(9.8f), 6f);
            LedgerV2.Cell(row, lx, 0f, labelW, h, label, MonoPx(9.8f),
                LedgerStyle.RailValue, 6f, TextAlignmentOptions.MidlineLeft,
                LedgerStyle.MonoBold);

            var wordW = Mathf.Min(Wide(word, wordSize, 4.5f),
                w - lx - labelW - Px(26f));
            Dots(panel, x + lx + labelW + Px(8f), y + h * 0.68f,
                w - lx - labelW - wordW - Px(26f));
            LedgerV2.Cell(row, w - Px(10f) - wordW, 0f, wordW, h, word, wordSize, wordInk,
                4.5f, TextAlignmentOptions.MidlineRight);
            return h;
        }

        /// <summary>
        /// How tall a move row stands. The handoff sets the key and its cost note side by
        /// side on one line, and its notes are three words long. THIS game's notes are the
        /// shared table's own sentences - they say why a row is refused, which is the one
        /// thing a faded key cannot say for itself - so a note too long to stand beside
        /// the key drops UNDER it rather than being cut off.
        /// </summary>
        static float MoveRowHeight(TerritoryRacketOrder row, float w, float rowH)
        {
            var keyW = KeyWidth(row.Label);
            var noteW = Wide(row.Note, MonoPx(9.8f), 4.5f);
            return noteW <= w - keyW - Px(24f) ? rowH : rowH + Px(13f);
        }

        static float KeyWidth(string label) =>
            Px(15f) * 2f + Wide(label, MonoPx(8.7f), 7f);

        static void MoveRow(RectTransform panel, float x, float y, float w, float h,
            float rowH, TerritoryRacketOrder row, Bucket bucket, Door door,
            Action changed, DoorDispatch dispatch, bool solo, string soloReason,
            ref Action commit, ref string armedVerb, ref string armedWarning)
        {
            // A single man may put a demand or a threat and nothing else - the same rule
            // the flat key wall carried, kept because it is a rule about who can do what
            // and not about how the card is drawn.
            var soloOrder = row.Kind == TerritoryDoorRowKind.Racket &&
                (row.Intent == TerritoryRacketIntent.Demand ||
                 row.Intent == TerritoryRacketIntent.Threaten);
            var live = row.Available && (!solo || (soloOrder && soloReason == null));

            var press = Press(door, row, changed, dispatch);
            var hard = Irreversible(row) && TwoStepConfirm;
            var key = RowKey(row);
            Action act = hard
                ? () => { Arm(key, row.Label); changed?.Invoke(); }
                : () => { Armed = null; press(); };

            if (hard && live && Armed == key)
            {
                commit = () => { Armed = null; press(); };
                armedVerb = row.Label;
                armedWarning = Warning(row);
            }

            var band = NewRect("Move " + row.Label, panel);
            PlaceTopLeft(band, x, -y, w, h);
            if (live)
                Pressable(band, LedgerStyle.Chrome, LedgerStyle.Rail, act);
            else
                Fill(band, LedgerStyle.Chrome);

            var keyW = KeyWidth(row.Label);
            var keyH = Px(30f);
            var keyY = (rowH - keyH) * 0.5f;
            var red = bucket == Bucket.NoWay;

            // A DARK KEY ON A NEAR-BLACK GROUND IS NOT A KEY. The handoff wraps every one
            // of them in a hairline box for exactly that reason, and the box is what makes
            // the word read as something to press rather than something left lying there.
            // The red keys are their own fill and take none.
            if (!red)
                KeyBox(band, 0f, -keyY, keyW, keyH,
                    live ? LedgerStyle.RailLabel : LedgerV2.At(LedgerStyle.RailLabel, 0.4f));

            var label = LedgerV2.Button(band, row.Label, 0f, -keyY, keyW, keyH,
                () => act(), red ? LedgerV2.Key.Red : LedgerV2.Key.Dark, MonoPx(8.7f));
            LedgerV2.KeyEnabled(label, live, LedgerV2.At(LedgerStyle.RailValue, 0.4f));

            var note = row.Note;
            var noteInk = NoteInk(bucket, row);
            var noteW = Wide(note, MonoPx(9.8f), 4.5f);
            if (h > rowH)
            {
                // The note dropped under the key: it is a sentence now, so it is set
                // against the key's own left edge and reads left to right like one.
                LedgerV2.Cell(band, 0f, -rowH, w, Px(13f), note, MonoPx(9.8f), noteInk,
                    4.5f);
                return;
            }

            Dots(panel, x + keyW + Px(9f), y + rowH * 0.6f,
                w - keyW - noteW - Px(18f));
            LedgerV2.Cell(band, w - noteW, 0f, noteW, rowH, note, MonoPx(9.8f), noteInk,
                4.5f, TextAlignmentOptions.MidlineRight);
        }

        /// <summary>What a move's note is struck in: the consequence's own colour. A
        /// refused row keeps it - the faded key is what says it cannot be pressed, and
        /// dimming the note as well drops the reason below reading.</summary>
        static Color NoteInk(Bucket bucket, TerritoryRacketOrder row)
        {
            if (bucket == Bucket.NoWay)
                return LedgerStyle.RailRed;
            if (bucket == Bucket.Lean &&
                (row.Job == OrderType.Beating || row.Job == OrderType.SmashUp))
                return LedgerStyle.RailGold;
            return LedgerStyle.RailNote;
        }

        /// <summary>The hairline box round a dark key.</summary>
        static void KeyBox(RectTransform parent, float x, float y, float w, float h,
            Color edge)
        {
            var box = NewRect("Key box", parent);
            PlaceTopLeft(box, x - 1f, y + 1f, w + 2f, h + 2f);
            Frame(box, 1f, edge);
        }

        // ---------------------------------------------------------------- the confirm

        /// <summary>
        /// SAY IT TWICE OR NOT AT ALL. The strip stands under the sections while a move
        /// that cannot be taken back is armed: the consequence in words, the key that
        /// commits it, and the way out.
        /// </summary>
        static float Confirm(RectTransform panel, float x, float y, float w, string verb,
            string warning, Action commit, Action changed)
        {
            var pad = Px(10f);
            var keyH = Px(30f);
            var commitLabel = "COMMIT · " + verb;
            var commitW = KeyWidth(commitLabel);
            var offW = KeyWidth("CALL IT OFF");

            // The warning is never the thing that gets dropped when the strip is tight -
            // it is the whole reason the strip is there - so it takes a line of its own
            // above the keys rather than sharing one with them.
            var warnW = w - pad * 2f - Px(3f);
            var warnText = warning + " Say it twice or not at all.";
            var lines = Mathf.Clamp(
                Mathf.CeilToInt(Wide(warnText, MonoPx(9.8f), 4.5f) / Mathf.Max(1f, warnW)),
                1, 3);
            var warnH = LineBox(MonoPx(9.8f), lines);
            var h = Px(9f) * 2f + warnH + Px(8f) + keyH;

            Block("Confirm", panel, x, -y, w, h, LedgerV2.Rgb2(0x2b1210));
            Block("Confirm edge", panel, x, -y, Px(3f), h, LedgerV2.Red);

            Paragraph(panel, LedgerStyle.Mono, MonoPx(9.8f), LedgerStyle.RailRed,
                x + pad + Px(3f), -(y + Px(9f)), warnW, warnH, warnText, 2f);

            var keyY = y + Px(9f) + warnH + Px(8f);
            LedgerV2.Button(panel, commitLabel, x + pad + Px(3f), -keyY, commitW, keyH,
                () => { commit(); changed?.Invoke(); }, LedgerV2.Key.Red, MonoPx(8.7f));
            LedgerV2.Button(panel, "CALL IT OFF",
                x + pad + Px(3f) + commitW + Px(9f), -keyY, offW, keyH,
                () => { CallOff(); changed?.Invoke(); }, LedgerV2.Key.Ghost, MonoPx(8.7f));
            return h;
        }

        // ------------------------------------------------------------------- the foot

        /// <summary>
        /// The money moves and the last word. BUY IT OUTRIGHT wears the safe gold edge
        /// that marks a move paid for out of the safe; SIT ON IT is this game's own row -
        /// the handoff's do-nothing exit is a real order here, our men standing on his
        /// door - and it keeps the foot's second place because that is where a reader
        /// looks for the quiet answer.
        /// </summary>
        static float Foot(RectTransform panel, float x, float y, float w, Door door,
            Action changed, DoorDispatch dispatch)
        {
            y += Px(12f);
            Rule(panel, x, -y, w, LedgerStyle.ChromeRule);
            y += Px(10f);

            var keyH = Px(30f);
            var cursor = x;
            var refusals = "";

            for (var i = 0; i < orders.Count; i++)
            {
                var row = orders[i];
                if (BucketOf(row) != Bucket.Foot)
                    continue;

                var money = row.Cash > 0;
                var word = money
                    ? row.Label + " · " + LedgerText.Cash(row.Cash)
                    : row.Label;
                var keyW = KeyWidth(word);
                if (cursor > x && cursor + keyW > x + w)
                {
                    cursor = x;
                    y += keyH + Px(9f);
                }

                var press = Press(door, row, changed, dispatch);
                // The gold edge is what marks the move paid for out of the safe. The
                // quiet key beside it is a ghost and takes no box at all.
                if (money)
                    KeyBox(panel, cursor, -y, keyW, keyH,
                        row.Available
                            ? LedgerStyle.RailSafeGold
                            : LedgerV2.At(LedgerStyle.RailSafeGold, 0.4f));
                var label = LedgerV2.Button(panel, word, cursor, -y, keyW, keyH,
                    () => { Armed = null; press(); },
                    money ? LedgerV2.Key.Dark : LedgerV2.Key.Ghost, MonoPx(8.7f));
                LedgerV2.KeyEnabled(label, row.Available,
                    LedgerV2.At(money ? LedgerStyle.RailValue : LedgerStyle.RailRed, 0.4f));
                cursor += keyW + Px(9f);

                if (!row.Available && row.Note.Length > 0)
                    refusals += (refusals.Length > 0 ? " · " : "") + row.Note;
            }
            y += keyH;

            if (refusals.Length > 0)
            {
                y += Px(8f);
                y += Line(panel, x, y, w, refusals, LedgerStyle.RailNote);
            }

            if (!string.IsNullOrEmpty(Note))
            {
                y += Px(8f);
                y += Line(panel, x, y, w, Note, LedgerStyle.RailBright);
            }

            return y;
        }

        /// <summary>A wrapped line at the foot of the card - a refusal, or the last word
        /// the office had.</summary>
        static float Line(RectTransform panel, float x, float y, float w, string text,
            Color ink)
        {
            var size = MonoPx(9.8f);
            var max = LineBox(size, 3);
            var line = Paragraph(panel, LedgerStyle.Mono, size, ink, x, -y, w, max,
                text, 2f);
            return Mathf.Clamp(line.preferredHeight, LineBox(size), max);
        }

        // ------------------------------------------------------------------ the rows

        /// <summary>
        /// A row that answers a click and lightens under the pointer. The tint is a
        /// MULTIPLIER, which is what Unity's colour transition takes, so the two grounds
        /// are named in the design's own tokens here and the ratio between them worked
        /// out rather than a lighter colour being eyeballed.
        /// </summary>
        static void Pressable(RectTransform rect, Color idle, Color hover, Action press)
        {
            var face = Fill(rect, idle);
            face.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = Tint(idle, hover);
            colours.selectedColor = colours.highlightedColor;
            colours.pressedColor = Tint(idle, hover) * 0.8f;
            colours.disabledColor = Color.white;
            button.colors = colours;
            button.onClick.AddListener(() => press());
        }

        static Color Tint(Color idle, Color hover) => new Color(
            idle.r > 0.002f ? hover.r / idle.r : 1f,
            idle.g > 0.002f ? hover.g / idle.g : 1f,
            idle.b > 0.002f ? hover.b / idle.b : 1f);
    }
}
