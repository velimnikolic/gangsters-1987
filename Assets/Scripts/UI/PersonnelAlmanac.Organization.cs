using System.Collections.Generic;
using TMPro;
using UnityEngine;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Territory;
using RoadDemo;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// Shared organization-backed reads and actions used by CHAIN OF COMMAND, BLOCKS
    /// and the personal files. The retired ORGANIZATION page owned these seams first;
    /// they remain here as one authority for transfers, filings and territory readings.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        /// <summary>The line the sheet writes when an order goes in. It is cleared the
        /// moment the office has nothing left to answer, so a stale "not answered yet"
        /// never stands over a page where everything has been ruled on.</summary>
        const string FiledNote = "filed · the outfit has not answered yet";

        string organizationNote = "";

        /// <summary>The man picked out of the pool, waiting for a branch to take him.</summary>
        int organizationPickedHoodId = -1;

        readonly List<OrganizationPerson> organizationLeaders =
            new List<OrganizationPerson>();
        readonly List<OrganizationPerson> organizationPeople =
            new List<OrganizationPerson>();
        readonly List<OrganizationPerson> organizationScratch =
            new List<OrganizationPerson>();
        readonly List<OrganizationBlockResponsibility> organizationResponsibilities =
            new List<OrganizationBlockResponsibility>();
        /// <summary>blockId to the leader who answers for it, rebuilt every repaint.</summary>
        readonly Dictionary<TerritoryBlockId, int> organizationPaper =
            new Dictionary<TerritoryBlockId, int>();

        /// <summary>What the street says about every block, read ONCE per repaint. The
        /// player query projects a fresh presentation object on every call, and this
        /// sheet asks about the same block from four places.</summary>
        readonly Dictionary<TerritoryBlockId, BlockControl> organizationControl =
            new Dictionary<TerritoryBlockId, BlockControl>();
        /// <summary>Hoods the book says could run a crew, gathered for the one line
        /// that answers "who do I promote" against the Boss's span.</summary>
        readonly List<Character> organizationReady = new List<Character>();

        /// <summary>Names on the READY FOR A CREW line before it says "and n more" -
        /// the line is a prompt, not a roll.</summary>
        const int ReadyNamed = 4;

        /// <summary>
        /// Who is on the books and what is written against their names: the leaders in
        /// rank order, the pool, the paper each block is on and what the street says
        /// about it. Read once at the head of a repaint - Every sheet that prints any of
        /// it starts here, because the player query projects a
        /// fresh object on every call and each figure is asked for from several places.
        /// </summary>
        void ReadOrganizationRoll(IOrganizationQuery query)
        {
            if (query == null || !query.TryGetBoss(out var boss))
                return;

            organizationLeaders.Clear();
            organizationLeaders.Add(boss);
            query.CollectLieutenants(organizationScratch);
            organizationLeaders.AddRange(organizationScratch);
            query.CollectHoods(organizationPeople);
            ReadOrganizationPaper(query);
            ReadOrganizationControl();
            if (TerritoryRuntime.Instance != null)
                TerritoryRuntime.Instance.CollectOccupiedBlocks(
                    GangCatalog.PlayerGangId, blocksOurStreets);
            else
                blocksOurStreets.Clear();
        }

        // ------------------------------------------------- what the book says of them

        /// <summary>
        /// The day the sheet is turned to. The campaign's own, because everything on
        /// the ledger that counts days - time in rank, the notability fold, the reason
        /// feed - was written against that clock at the day tick.
        /// </summary>
        int OrganizationDay => outfit ? outfit.Campaign.Day : RosterDay;

        /// <summary>
        /// FOLLOW-004 in a FILE's voice rather than a card's - "173 days · since
        /// 5 JAN 1987 · parked". Every sheet reads
        /// <see cref="Loyalty.TimeInRank"/>, which is the figure the drift is charged
        /// against, and every sheet that prints it calls one of these two rather than
        /// composing a third sentence of its own.
        ///
        /// A man whose rank was never stamped has been what he is since he SIGNED, and
        /// the label says which thing he has been that long - "A HOOD FOR", "A
        /// LIEUTENANT FOR". It read "A HOOD FOR" on every unstamped man, which called
        /// a lieutenant a hood on his own file.
        /// </summary>
        static string TenureLabel(Character man)
        {
            if (man == null)
                return "IN RANK";
            if (man.RankSince > 0)
                return "IN RANK";
            return man.Rank switch
            {
                Rank.Hood => "A HOOD FOR",
                Rank.Lieutenant => "A LIEUTENANT FOR",
                Rank.Boss => "HEAD OF THE FAMILY FOR",
                _ => "IN RANK",
            };
        }

        /// <summary>See <see cref="TenureLabel"/>.</summary>
        static string TenureFigure(Character man, int today)
        {
            if (man == null)
                return "";
            var days = Loyalty.TimeInRank(man, today);
            return (days == 1 ? "1 day" : days + " days") + "  ·  since " +
                   LedgerText.DayStamp(Loyalty.RankSinceDay(man)) +
                   (Loyalty.IsParked(man, today) ? "  ·  parked" : "");
        }

        /// <summary>
        /// FOLLOW-002/003. What a red flag on the man who HOLDS a branch costs, in
        /// words, and how many men it takes with it - the count comes off the defection
        /// arithmetic itself (<see cref="Defection.WouldFollow"/>), never off the mark.
        /// One sentence, called by every sheet that warns about him.
        /// </summary>
        static string BearsWatchingLine(int would) =>
            would <= 0
                ? "BEARS WATCHING · IF HE WALKS HE WALKS ALONE"
                : would == 1
                    ? "BEARS WATCHING · IF HE WALKS ONE OF HIS MEN GOES WITH HIM"
                    : "BEARS WATCHING · IF HE WALKS " + would +
                      " OF HIS MEN GO WITH HIM";

        /// <summary>
        /// FOLLOW-006's sentence: the hoods the book says could run a crew, named
        /// against the span that is the actual constraint on making one. Answers "" when
        /// the book is not pointing at anybody, and says through
        /// <paramref name="hasRoom"/> whether there is a place left to put one.
        ///
        /// It NAMES men and does nothing: a mark informs and never acts.
        /// </summary>
        string ReadyForACrewLine(OrganizationPerson boss, out bool hasRoom)
        {
            hasRoom = false;
            var roster = director != null ? director.Roster : null;
            if (roster == null)
                return "";

            organizationReady.Clear();
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (man == null || man.Gone || man.Rank != Rank.Hood)
                    continue;
                if ((ManFlags.Of(man) & ManFlag.LieutenantMaterial) != 0)
                    organizationReady.Add(man);
            }
            if (organizationReady.Count == 0)
                return "";

            var named = "";
            var show = Mathf.Min(organizationReady.Count, ReadyNamed);
            for (var i = 0; i < show; i++)
                named += (named.Length > 0 ? ", " : "") +
                         organizationReady[i].FullName;
            if (organizationReady.Count > show)
                named += " and " + (organizationReady.Count - show) + " more";

            var member = roster.Find(boss.Id);
            var room = member != null
                ? Command.LieutenantCap(member) - Command.LieutenantsHeld(roster)
                : 0;
            hasRoom = room > 0;
            var against = room > 0
                ? room == 1 ? " · one place left under you"
                    : " · " + room + " places left under you"
                : " · no place left under you";
            return named + against;
        }

        /// <summary>One capacity meter: the figure, the bar, and the plain sentence
        /// saying what the figure means for the next order.</summary>
        /// <summary>
        /// A capacity meter, exactly as the design strikes one: the label on the left of
        /// its own line with the figure held to the right of it, a flat trough under the
        /// pair, and the line of plain English under that. Answers the height it took,
        /// so a caller can stack two without counting.
        /// </summary>
        static float Meter(Transform card, float x, float top, float width, string label,
            CapacityMeasure measure, string unit, string plural, bool dark,
            float labelSize = 10f, float figureSize = 15f)
        {
            var over = measure.IsOverCapacity;
            var full = !over && measure.Current >= measure.Maximum;
            var ink = over ? LedgerV2.Red
                : full ? LedgerV2.Amber
                : dark ? LedgerV2.HeadCream : LedgerV2.Ink;
            var labelInk = over ? ink : dark ? LedgerV2.HeadDim : LedgerV2.Muted;

            const float figureW = 96f;
            var name = Line(card, LedgerStyle.Mono, labelSize, labelInk,
                x, -top, width - figureW - 8f, LineBox(labelSize), label);
            name.characterSpacing = 6f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            Line(card, LedgerStyle.MonoBold, figureSize, ink,
                x + width - figureW, -(top - 1f), figureW, LineBox(figureSize),
                measure.Current + " / " + measure.Maximum)
                .alignment = TextAlignmentOptions.MidlineRight;

            var barY = top + LineBox(labelSize) + 4f;
            MeterBar(card, x, barY, width,
                measure.Maximum <= 0 ? 0f : (float)measure.Current / measure.Maximum,
                ink, dark);

            var room = measure.Maximum - measure.Current;
            var note = over
                ? "OVER BY " + measure.Overage + " · the outfit will not add more"
                : full
                    ? "at the limit · no room for another " + unit
                    : room + " more " + (room == 1 ? unit : plural) + " will fit";
            var noteY = barY + MeterTrackH + 3f;
            var line = Line(card, over ? LedgerStyle.MonoBold : LedgerStyle.Mono, 10f,
                over ? ink : dark ? LedgerV2.HeadDim : LedgerV2.Muted,
                x, -noteY, width, LineBox(10f), note);
            line.overflowMode = TextOverflowModes.Ellipsis;
            return noteY + LineBox(10f) - top;
        }

        /// <summary>The design's trough: seven units, flat, no border, and the fill runs
        /// it edge to edge. A framed track with an inset fill reads as an empty box with
        /// a sliver in it at the fractions this page actually shows - 2 of 50 is four
        /// per cent, and four per cent of a bordered box is the border.</summary>
        /// <summary>What one meter stands, label to note. Independent of where it is
        /// put, so a card can be measured before its column is drawn.</summary>
        static float MeterHeight(float labelSize = 10f) =>
            LineBox(labelSize) + 4f + MeterTrackH + 3f + LineBox(10f);

        const float MeterTrackH = 7f;

        static void MeterBar(Transform card, float x, float top, float width,
            float fraction, Color ink, bool dark)
        {
            var track = NewRect("Meter track", card);
            PlaceTopLeft(track, x, -top, width, MeterTrackH);
            Fill(track, dark ? LedgerStyle.RailTrough : LedgerV2.Trough);

            var filled = Mathf.Clamp01(fraction);
            if (filled <= 0f)
                return;
            var run = NewRect("Meter fill", track);
            // At least a unit of ink: a man on the books is a man on the books, and a
            // meter that prints nothing for him has lost the only thing it was for.
            PlaceTopLeft(run, 0f, 0f, Mathf.Max(1f, width * filled), MeterTrackH);
            Fill(run, ink);
        }

        /// <summary>A man's photograph in a plate, or the hatch when no model resolves.
        /// The face is always the one he wears on the street - never a picked file.</summary>
        static void Face(Transform parent, float x, float y, float w, float h,
            Character member, string initials = "", Color? ground = null,
            Color? ink = null)
        {
            var raw = LedgerV2.PortraitPlate(parent, x, y, w, h, initials, ground, ink);
            if (member != null)
                PortraitStudio.Request(MemberModel(member), PortraitStudio.Framing.Bust, raw);
        }

        void PickHood(int hoodId)
        {
            organizationPickedHoodId = organizationPickedHoodId == hoodId ? -1 : hoodId;
            organizationNote = organizationPickedHoodId >= 0
                ? "pick the lieutenant who should take him"
                : "";
            dirty = true;
        }

        void ToggleBlockMenu(TerritoryBlockId blockId)
        {
            blocksMenu = blocksMenu == blockId
                ? default
                : blockId;
            dirty = true;
        }

        // ------------------------------------------------------------------- filings

        /// <summary>Files one order with the outfit. Where there is no filing office in
        /// the scene the order is carried out at once instead, so a demo rig without the
        /// campaign director still commands the same systems.</summary>
        void FileOrder(string text, System.Func<Outfit.FilingRuling> resolver)
        {
            if (!outfit)
            {
                var immediate = resolver();
                SayOnThisSheet(immediate.Ruling);
                dirty = true;
                return;
            }

            outfit.Filings.File(FilingStamp(), text, resolver);
            SayOnThisSheet(FiledNote);
            dirty = true;
        }

        /// <summary>The sheet the reader is on is the sheet the office's answer belongs
        /// on. CHAIN OF COMMAND and BLOCKS file with the same office and each keeps its own
        /// line, so a ruling never appears on a page the reader was not looking at.
        /// </summary>
        void SayOnThisSheet(string note)
        {
            if (currentPage == LedgerPage.Blocks)
                blocksNote = note;
            else
                organizationNote = note;
        }

        string FilingStamp()
        {
            var day = outfit ? outfit.Campaign.Day : 1;
            if (!cityClock)
                return "D" + day;
            return "D" + day + " " +
                   Mathf.FloorToInt(cityClock.Hour).ToString("00") + ":" +
                   Mathf.FloorToInt(cityClock.Hour % 1f * 60f).ToString("00");
        }

        void FileHoodPlacement(int hoodId, int leaderId)
        {
            var hood = Person(hoodId);
            var leader = Leader(leaderId);
            if (!hood.IsValid || !leader.IsValid)
                return;

            organizationPickedHoodId = -1;
            FileOrder(hood.Name + " put under " + leader.Name + ".", () =>
            {
                var query = director != null ? director.Organization : null;
                var target = Leader(leaderId);
                if (query == null || !target.IsValid)
                    return Outfit.FilingRuling.Refuse(
                        "that command node is no longer on the books");

                // The Boss's own branch is where an idle man LIVES, so a return to it is
                // never refused. Only a branch can be full.
                var manpower = query.CapacityOf(leaderId).Manpower;
                if (target.Rank == Rank.Lieutenant &&
                    !Outfit.OutfitFilingRules.AcceptsAnotherMan(manpower))
                    return Outfit.FilingRuling.Refuse(
                        Outfit.OutfitFilingRules.ManRefusal(target.Name, manpower));

                var result = SubmitHoodAssignment(hoodId, target);
                return result.Ok
                    ? Outfit.FilingRuling.Grant("he reports to " + target.Name + " from today")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
        }

        /// <summary>
        /// Puts one man in front of the Don. Filed like every other posting, and
        /// refused for the same reason a branch refuses one: the detail eats the Boss's
        /// own manpower cap, so a full Boss cannot stand another guard up.
        /// </summary>
        void FileDetailPosting(int hoodId)
        {
            var hood = Person(hoodId);
            if (!hood.IsValid)
                return;

            organizationPickedHoodId = -1;
            FileOrder(hood.Name + " put on the Boss's detail.", () =>
            {
                var query = director != null ? director.Organization : null;
                if (query == null || !query.TryGetBoss(out var boss))
                    return Outfit.FilingRuling.Refuse(LedgerText.ReasonNoBoss);

                var manpower = query.CapacityOf(boss.Id).Manpower;
                if (!Outfit.OutfitFilingRules.AcceptsAnotherMan(manpower))
                    return Outfit.FilingRuling.Refuse(
                        Outfit.OutfitFilingRules.ManRefusal(boss.Name, manpower));

                var result = director.AssignToDetail(hoodId);
                return result.Ok
                    ? Outfit.FilingRuling.Grant("he stands with the Don from today")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
        }

        void FileHoodRecall(int hoodId)
        {
            var hood = Person(hoodId);
            if (!hood.IsValid)
                return;

            FileOrder(hood.Name + " pulled back under the Boss.", () =>
            {
                var boss = organizationLeaders.Count > 0 ? organizationLeaders[0] : default;
                if (!boss.IsValid)
                    return Outfit.FilingRuling.Refuse(LedgerText.ReasonNoBoss);
                var result = SubmitHoodAssignment(hoodId, boss);
                return result.Ok
                    ? Outfit.FilingRuling.Grant("off the branch · idle and drawing pay")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
        }

        /// <summary>
        /// The half every HIRE A MAN order has in common, wherever the key was pressed:
        /// the signing money out of the safe, the man's own name off the roster, and
        /// then the one posting that key stands for.
        ///
        /// <paramref name="place"/> answers empty where the posting held and the
        /// refusal where it did not - a man who cannot be placed is never thrown away,
        /// he reports to the pool and the ruling says why. The branch's own refusals
        /// belong BEFORE this call, while the money is still in the safe.
        /// </summary>
        Outfit.FilingRuling SignAndPlace(System.Func<int, string> place,
            System.Func<string, string> granted)
        {
            var hired = director.RecruitHood(out var newId);
            if (!hired.Ok)
                return Outfit.FilingRuling.Refuse(hired.Reason);

            var recruit = director.Roster != null ? director.Roster.Find(newId) : null;
            var name = recruit != null ? recruit.FullName : "the new man";
            var refusal = place(newId);
            return string.IsNullOrEmpty(refusal)
                ? Outfit.FilingRuling.Grant(granted(name))
                : Outfit.FilingRuling.Grant(
                    name + " reported · " + refusal + ", so he waits in the pool");
        }

        /// <summary>
        /// HIRE A MAN struck on THE DETAIL: the signing money, and then the new man
        /// stands in front of the Don from that day. One order for both halves, so a
        /// guard signed for the detail is never left idle in the reserve.
        ///
        /// The Boss's cap is read BEFORE the money leaves the safe - the detail eats his
        /// own manpower, and a full Boss must not pay a signing to be told so.
        /// </summary>
        void FileRecruitToDetail()
        {
            FileOrder("A man requested for the Boss's own detail. " +
                      LedgerText.Cash(director != null ? director.HoodRecruitmentCost : 0) +
                      " committed.", () =>
            {
                var query = director != null ? director.Organization : null;
                if (query == null || !query.TryGetBoss(out var boss))
                    return Outfit.FilingRuling.Refuse(LedgerText.ReasonNoBoss);

                var manpower = query.CapacityOf(boss.Id).Manpower;
                if (!Outfit.OutfitFilingRules.AcceptsAnotherMan(manpower))
                    return Outfit.FilingRuling.Refuse(
                        Outfit.OutfitFilingRules.ManRefusal(boss.Name, manpower) +
                        " · nobody hired");

                return SignAndPlace(
                    id =>
                    {
                        var placed = director.AssignToDetail(id);
                        return placed.Ok ? "" : placed.Reason;
                    },
                    name => name + " stands with the Don from today");
            });
        }

        void FileRecruit(int leaderId)
        {
            var leader = leaderId >= 0 ? Leader(leaderId) : default;
            var text = leader.IsValid && leader.Rank == Rank.Lieutenant
                ? "A man requested for " + leader.Name + "'s branch. " +
                  LedgerText.Cash(director.HoodRecruitmentCost) + " committed."
                : "A man requested under the Boss directly. " +
                  LedgerText.Cash(director.HoodRecruitmentCost) + " committed.";

            FileOrder(text, () =>
            {
                var query = director != null ? director.Organization : null;
                var target = leaderId >= 0 ? Leader(leaderId) : default;
                if (query == null)
                    return Outfit.FilingRuling.Refuse("the command file is unavailable");

                // Refused BEFORE the money leaves the safe: a branch that cannot hold
                // the man must not cost the outfit fifty dollars to find that out.
                if (target.IsValid && target.Rank == Rank.Lieutenant)
                {
                    var manpower = query.CapacityOf(target.Id).Manpower;
                    if (!Outfit.OutfitFilingRules.AcceptsAnotherMan(manpower))
                        return Outfit.FilingRuling.Refuse(
                            target.Name + " has no room (" + manpower.Current + "/" +
                            manpower.Maximum + ") · nobody hired");
                }

                if (!target.IsValid || target.Rank != Rank.Lieutenant)
                    return SignAndPlace(id => "",
                        name => name + " reported · idle until you place him");

                return SignAndPlace(
                    id =>
                    {
                        var placed = SubmitHoodAssignment(id, target);
                        return placed.Ok ? "" : placed.Reason;
                    },
                    name => name + " reports to " + target.Name);
            });
        }

        void FileBlockResponsibility(TerritoryBlockId blockId, int leaderId)
        {
            var leader = Leader(leaderId);
            if (!leader.IsValid || !blockId.IsValid)
                return;

            blocksMenu = default;
            ClearBlocksPendingBlock();
            var blockName = BlockName(blockId);
            FileOrder("Responsibility for " + blockName + " struck against " +
                      leader.Name + ".", () =>
            {
                var runtime = TerritoryRuntime.Instance;
                var query = director != null ? director.Organization : null;
                var target = Leader(leaderId);
                if (query == null || !target.IsValid || runtime?.Commands == null)
                    return Outfit.FilingRuling.Refuse(
                        "the territory command gateway is unavailable");

                var blocks = query.CapacityOf(target.Id).Blocks;
                query.CollectBlockResponsibilities(target.Id, organizationResponsibilities);
                var alreadyHis = false;
                for (var i = 0; i < organizationResponsibilities.Count; i++)
                    if (organizationResponsibilities[i].BlockId == blockId)
                        alreadyHis = true;
                if (!alreadyHis && target.Rank == Rank.Lieutenant &&
                    !Outfit.OutfitFilingRules.AcceptsAnotherBlock(blocks))
                    return Outfit.FilingRuling.Refuse(
                        Outfit.OutfitFilingRules.BlockRefusal(target.Name, blocks));

                var node = target.Rank == Rank.Boss
                    ? TerritoryCommandNodeId.Boss(target.Id)
                    : TerritoryCommandNodeId.Lieutenant(target.Id);
                var result = runtime.Commands.Submit(
                    Gameplay.PlayerCommands.Stamp(
                        new AssignBlockResponsibilityCommand(
                            blockId,
                            Gameplay.PlayerCommands.House,
                            node,
                            target.Rank == Rank.Boss
                                ? new TerritoryCharacterId(target.Id) : default,
                            target.Rank == Rank.Lieutenant
                                ? new TerritoryCharacterId(target.Id)
                                : default)));
                if (!result.WasAccepted)
                    return Outfit.FilingRuling.Refuse(result.Reason);

                return Outfit.FilingRuling.Grant(
                    !IsOurStreet(ReadControl(blockId))
                        ? "filed as written · the block is not ours"
                        : target.Name + " answers for it from today");
            });
        }

        void FileBlockRemoval(TerritoryBlockId blockId, int leaderId)
        {
            var leader = Leader(leaderId);
            if (!blockId.IsValid)
                return;

            blocksMenu = default;
            var blockName = BlockName(blockId);
            FileOrder("The name struck off " + blockName + ".", () =>
            {
                var runtime = TerritoryRuntime.Instance;
                var result = runtime != null
                    ? runtime.RemoveBlockResponsibility(blockId, leaderId)
                    : OpResult.Fail("the territory command gateway is unavailable");
                return result.Ok
                    ? Outfit.FilingRuling.Grant(
                        "nobody answers for it now · control unchanged")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
            if (!leader.IsValid)
                SayOnThisSheet("the block had no valid name on it");
        }

        OpResult SubmitHoodAssignment(int hoodId, OrganizationPerson leader)
        {
            var commands = TerritoryRuntime.Instance?.Commands;
            if (commands == null)
                return leader.Rank == Rank.Boss
                    ? director.AssignToBoss(hoodId, leader.Id)
                    : director.AssignToLieutenant(hoodId, leader.Id);

            var result = leader.Rank == Rank.Boss
                ? commands.Submit(Gameplay.PlayerCommands.Stamp(
                    new AssignHoodToBossCommand(
                        new TerritoryCharacterId(hoodId),
                        new TerritoryCharacterId(leader.Id))))
                : commands.Submit(Gameplay.PlayerCommands.Stamp(
                    new AssignHoodToLieutenantCommand(
                        new TerritoryCharacterId(hoodId),
                        new TerritoryCharacterId(leader.Id))));
            return result.Status == TerritoryCommandStatus.Succeeded
                ? OpResult.Success
                : OpResult.Fail(string.IsNullOrEmpty(result.Reason)
                    ? "the command was not completed"
                    : result.Reason);
        }

        void DismissOrganizationTransient()
        {
            organizationPickedHoodId = -1;
        }

        // ---------------------------------------------------------------- the reading

        /// <summary>What the street says about a block, from OUR side of it. The
        /// territory layer derives control from the deeds standing on the block
        /// (TerritoryControlDerivation); this is that reading turned into the four words
        /// the sheet prints.</summary>
        enum BlockControl
        {
            NotOurs,
            Theirs,
            Contested,
            Held,
            Unknown,
        }

        void ReadOrganizationPaper(IOrganizationQuery query)
        {
            organizationPaper.Clear();
            for (var i = 0; i < organizationLeaders.Count; i++)
            {
                var leaderId = organizationLeaders[i].Id;
                query.CollectBlockResponsibilities(leaderId, organizationResponsibilities);
                for (var b = 0; b < organizationResponsibilities.Count; b++)
                    organizationPaper[organizationResponsibilities[b].BlockId] = leaderId;
            }
        }

        void ReadOrganizationControl()
        {
            organizationControl.Clear();
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            if (query == null)
                return;
            var ids = query.BlockIds;
            for (var i = 0; i < ids.Count; i++)
                organizationControl[ids[i]] = ReadControl(ids[i]);
        }

        /// <summary>Ground the street already counts as ours in some measure.</summary>
        static bool IsOurStreet(BlockControl control) =>
            control == BlockControl.Held || control == BlockControl.Contested;

        BlockControl ControlOf(TerritoryBlockId blockId) =>
            organizationControl.TryGetValue(blockId, out var control)
                ? control
                : ReadControl(blockId);

        /// <summary>The live read, straight off the player query - what a filing's
        /// resolver has to use, because it runs long after the page was painted.</summary>
        static BlockControl ReadControl(TerritoryBlockId blockId)
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            if (query == null || !query.TryGetBlock(blockId, out var block) || block == null)
                return BlockControl.Unknown;

            var profile = TerritoryPresentationProfile.Default;
            if (block.Control == profile.Uncontrolled)
                return BlockControl.NotOurs;
            // Dominated counts: a street run outright is HELD, not unknown - reading it
            // as unknown dropped the row and slammed the open block file shut the
            // moment a family did too well.
            if (block.Control != profile.Controlled && block.Control != profile.Contested &&
                block.Control != profile.Influenced && block.Control != profile.Dominated)
                return BlockControl.Unknown;

            // Control alone does not say WHOSE. A held block we hold no premise on
            // belongs to another house, and the sheet has to say so rather than print it
            // green. It is the DEEDS that answer this, not who happens to be standing
            // there: a crew passing through a rival street does not make it ours.
            var ours = block.Holding != profile.Holding.NoneLabel &&
                       block.Holding != profile.Holding.UnknownLabel;
            if (!ours)
                return BlockControl.Theirs;
            return block.Control == profile.Controlled ||
                   block.Control == profile.Dominated
                ? BlockControl.Held
                : BlockControl.Contested;
        }

        static string ControlWord(BlockControl control) => control switch
        {
            BlockControl.Held => "HELD BY US",
            BlockControl.Contested => "CONTESTED",
            BlockControl.Theirs => "ANOTHER HOUSE",
            BlockControl.NotOurs => "NOT OURS",
            _ => "NOT KNOWN",
        };

        static Color ControlColour(BlockControl control) => control switch
        {
            BlockControl.Held => LedgerV2.Green,
            BlockControl.Contested => LedgerV2.Amber,
            BlockControl.Theirs => LedgerV2.Red,
            BlockControl.NotOurs => LedgerV2.Red,
            _ => LedgerV2.Label,
        };

        /// <summary>The sentence that says what the paper and the street add up to.
        /// The two can disagree in four ways and each of them is a different problem, so
        /// the sheet writes the problem out rather than leaving the reader to compare two
        /// columns.</summary>
        static string Reading(
            OrganizationPerson leader, BlockControl control, bool manned, out Color colour)
        {
            var ours = control == BlockControl.Held || control == BlockControl.Contested;

            if (leader.IsValid && !ours)
            {
                colour = manned ? LedgerV2.Amber : LedgerV2.Red;
                if (manned)
                    return leader.Name + " has men on it and not a premise to show for it.";
                return control == BlockControl.Theirs
                    ? leader.Name + " answers for another house's ground. Paper only."
                    : leader.Name + " answers for ground we do not hold. Paper only.";
            }
            if (!leader.IsValid && control == BlockControl.Held)
            {
                colour = LedgerV2.Red;
                return "We hold it and nobody answers for it.";
            }
            if (control == BlockControl.Contested)
            {
                colour = LedgerV2.Amber;
                return leader.IsValid
                    ? "His, but another house is pushing on it."
                    : "Contested, and nobody is named for it.";
            }
            if (!leader.IsValid)
            {
                colour = LedgerV2.Muted;
                return manned
                    ? "Our men are standing on it. No premise here is ours yet."
                    : "Nobody named. Nothing to answer for.";
            }
            colour = LedgerV2.Green;
            return "Paper and street agree.";
        }

        static Color StatusColour(Outfit.FilingStatus status) => status switch
        {
            Outfit.FilingStatus.Granted => LedgerV2.Green,
            Outfit.FilingStatus.Refused => LedgerV2.Red,
            _ => LedgerV2.Filed,
        };

        static string StatusWord(Outfit.FilingStatus status) => status switch
        {
            Outfit.FilingStatus.Granted => "GRANTED",
            Outfit.FilingStatus.Refused => "REFUSED",
            _ => "FILED",
        };

        int CountHeldBlocks()
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            if (query == null)
                return 0;
            var held = 0;
            var ids = query.BlockIds;
            for (var i = 0; i < ids.Count; i++)
                if (ControlOf(ids[i]) == BlockControl.Held)
                    held++;
            return held;
        }

        int CountPaperOnly()
        {
            var count = 0;
            foreach (var pair in organizationPaper)
                if (ControlOf(pair.Key) == BlockControl.NotOurs)
                    count++;
            return count;
        }

        string BranchSummary(List<OrganizationPerson> men)
        {
            if (men.Count == 0)
                return "NO HOODS UNDER HIM";
            var posted = 0;
            for (var i = 0; i < men.Count; i++)
                if (HasPost(men[i]))
                    posted++;
            return posted + " POSTED · " + (men.Count - posted) + " WITHOUT A POST";
        }

        static bool HasPost(OrganizationPerson person) =>
            person.IsAvailable && person.Rank == Rank.Hood &&
            person.Assignment != AssignmentKind.Pool;

        /// <summary>Is he one of the men standing in front of the Don?</summary>
        bool IsOnTheDetail(int id)
        {
            var detail = director != null ? director.BodyguardDetail() : null;
            return detail != null && detail.HoodIds.Contains(id);
        }

        /// <summary>What a man is actually doing, in the sheet's own words.</summary>
        string HoodDuty(OrganizationPerson person)
        {
            if (!person.IsAvailable)
                return person.Status.ToString().ToLowerInvariant() + " · off the books";
            if (person.Rank != Rank.Hood)
                return "commands a branch";
            var roster = director != null ? director.Roster : null;
            var member = roster != null ? roster.Find(person.Id) : null;
            if (member != null && member.Duty == Duty.Collector)
            {
                if (BlockRacketSeam.SourceOrStub.TryGetRoundOf(
                        person.Id, out var roundBlock))
                    return "on the round · " + BlockName(roundBlock);
                var crew = roster.CrewOf(person.Id);
                var leader = crew != null ? roster.Find(crew.LieutenantId) : null;
                return "carries the bag for " +
                       (leader != null ? leader.Surname + "'s ground" : "the outfit");
            }
            if (member != null && member.Duty == Duty.Escort)
                return "guards the bag";
            if (person.Assignment == AssignmentKind.Pool)
                return "no post · earning nothing";
            if (IsOnTheDetail(person.Id))
                return "stands with the Don";
            if (TryObservedBlock(person.Id, out var blockName))
                return "on the street · " + blockName;
            return person.Assignment == AssignmentKind.Front
                ? "front post"
                : "on his lieutenant's crew";
        }

        string NeighborhoodOf(TerritoryBlockId blockId)
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            return query != null && query.TryGetBlock(blockId, out var block) &&
                   block != null && block.NeighborhoodName.Length > 0
                ? block.NeighborhoodName
                : "ward unrecorded";
        }

        string BlockName(TerritoryBlockId blockId)
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            return query != null && query.TryGetBlock(blockId, out var block) && block != null
                ? block.BlockName
                : blockId.Value;
        }

        bool TryObservedBlock(int characterId, out string blockName)
        {
            var truth = TerritoryRuntime.Instance?.DebugTruth;
            if (truth != null)
                for (var i = 0; i < truth.BlockIds.Count; i++)
                    if (truth.TryGetBlock(truth.BlockIds[i], out var block) && block != null)
                        for (var a = 0; a < block.Actors.Count; a++)
                            if (block.Actors[a].GangId.IsValid &&
                                block.Actors[a].GangId.Value == GangCatalog.PlayerGangId &&
                                block.Actors[a].CharacterId.IsValid &&
                                block.Actors[a].CharacterId.Value == characterId)
                            {
                                blockName = block.Definition.DisplayName;
                                return true;
                            }
            blockName = "";
            return false;
        }

        OrganizationPerson Leader(int id)
        {
            for (var i = 0; i < organizationLeaders.Count; i++)
                if (organizationLeaders[i].Id == id)
                    return organizationLeaders[i];
            return default;
        }

        OrganizationPerson Person(int id)
        {
            for (var i = 0; i < organizationPeople.Count; i++)
                if (organizationPeople[i].Id == id)
                    return organizationPeople[i];
            return Leader(id);
        }

        bool IsPooled(int id)
        {
            var person = Person(id);
            return person.IsValid && person.IsUnassigned && person.IsAvailable;
        }

        static string FirstName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return "HIM";
            var space = fullName.IndexOf(' ');
            return space > 0 ? fullName.Substring(0, space) : fullName;
        }

        static string OutfitTitle(string bossName)
        {
            if (string.IsNullOrWhiteSpace(bossName))
                return "THE OUTFIT";
            var words = bossName.Split(' ');
            var surname = words.Length > 0 ? words[words.Length - 1] : "";
            return "THE " + surname.ToUpperInvariant() + " OUTFIT";
        }

        static void SetActionEnabled(TMP_Text label, bool enabled) =>
            LedgerV2.KeyEnabled(label, enabled);
    }
}
