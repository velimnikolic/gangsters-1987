using System.Collections.Generic;
using TMPro;
using UnityEngine;
using LivingCity.Outfit;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE DOING HALF OF THE TABLE. What a word points at - the money, one of our
    /// streets, a third house, and the man who carries it - and the four doors those
    /// four things go out through.
    ///
    /// The FAMILIES sheet draws its form rows from here and sends through the same
    /// <see cref="HouseOps"/> door a rival's mind does, so a key on the table and a
    /// rival's own intent cannot behave differently. Nothing here decides whether a
    /// word MAY be said - <see cref="HouseTable"/> asks the gateway that, in the
    /// gateway's own words.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        /// <summary>What the next offer carries, in dollars.</summary>
        int tableMoney;

        /// <summary>Which of our streets a word or a line names.</summary>
        int tableStreet;

        /// <summary>Which house a pact or a joined war is against.</summary>
        int tableThird;

        /// <summary>Carried in person by which of our lieutenants, or by telephone.
        /// </summary>
        bool tableInPerson;
        int tableEnvoy;

        /// <summary>The gateway's last word about what was asked from this sheet.
        /// </summary>
        string tableNote = "";

        const float TableKeyH = 26f;
        const int TableMoneyStep = 500;

        readonly List<Territory.TerritoryBlockId> tableStreets =
            new List<Territory.TerritoryBlockId>();
        readonly List<Gangs.Gang> tableThirds = new List<Gangs.Gang>();
        readonly List<Character> tableEnvoys = new List<Character>();

        // ------------------------------------------------------ what a word can point at

        /// <summary>Our streets: every block the map paints as ours.</summary>
        void CollectStreets()
        {
            tableStreets.Clear();
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            if (runtime == null)
                return;
            for (var i = 0; i < holdings.Count; i++)
            {
                if (holdings[i].GangId != Gangs.GangCatalog.PlayerGangId)
                    continue;
                if (!runtime.TryGetBlock(holdings[i].BlockId, out var blockId) || !blockId.IsValid)
                    continue;
                if (!tableStreets.Contains(blockId))
                    tableStreets.Add(blockId);
            }
            tableStreet = tableStreets.Count > 0
                ? Mathf.Clamp(tableStreet, 0, tableStreets.Count - 1) : 0;
        }

        string StreetName(Territory.TerritoryBlockId blockId)
        {
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            if (runtime?.Geography != null &&
                runtime.Geography.TryGetBlock(blockId, out var block) &&
                !string.IsNullOrEmpty(block.DisplayName))
                return block.DisplayName;
            return blockId.Value;
        }

        /// <summary>The houses a pact can be sworn against - every other one - or, for a
        /// war joined, only the ones we are at war with.</summary>
        void CollectThirds(IReadOnlyList<Gangs.Gang> gangs, bool atWarOnly)
        {
            tableThirds.Clear();
            foreach (var gang in gangs)
            {
                if (gang.IsPlayer || gang.Id == tableFor)
                    continue;
                if (atWarOnly && outfit && outfit.StanceWith(gang.Id) != Stance.War)
                    continue;
                tableThirds.Add(gang);
            }
            tableThird = tableThirds.Count > 0
                ? Mathf.Clamp(tableThird, 0, tableThirds.Count - 1) : 0;
        }

        /// <summary>Our lieutenants, standing and free to walk. The Don never goes.
        /// </summary>
        void CollectEnvoys()
        {
            tableEnvoys.Clear();
            var roster = director != null ? director.Roster : null;
            if (roster == null)
            {
                tableInPerson = false;
                return;
            }
            foreach (var man in roster.Members)
                if (!man.Gone && man.Rank == Rank.Lieutenant &&
                    man.Status == CharacterStatus.Active)
                    tableEnvoys.Add(man);
            if (tableEnvoys.Count == 0)
                tableInPerson = false;
            else
                tableEnvoy = Mathf.Clamp(tableEnvoy, 0, tableEnvoys.Count - 1);
        }

        // ------------------------------------------------------------------ the rows

        /// <summary>
        /// The money a word carries, wound between nothing and what the book will honour
        /// - a bill's ceiling, the street's tribute figure, or what the safe holds.
        /// Answers the y the next row starts at.
        /// </summary>
        float MoneyPicker(Transform panel, float x, float y, float w, int most)
        {
            most = Mathf.Max(0, most);
            tableMoney = Mathf.Clamp(tableMoney, 0, most);

            var less = LedgerV2.Button(panel, "−", x, y, 26f, TableKeyH, () =>
            {
                tableMoney = Mathf.Max(0, tableMoney - TableMoneyStep);
                dirty = true;
            }, LedgerV2.Key.Outline, 11f);
            LedgerV2.Figure(panel, x + 32f, y, 96f, LedgerText.Cash(tableMoney), 14f,
                tableMoney > 0 ? LedgerV2.Ink : LedgerV2.Muted);
            var more = LedgerV2.Button(panel, "+", x + 134f, y, 26f, TableKeyH, () =>
            {
                tableMoney = Mathf.Min(most, tableMoney + TableMoneyStep);
                dirty = true;
            }, LedgerV2.Key.Outline, 11f);
            LedgerV2.KeyEnabled(less, tableMoney > 0);
            LedgerV2.KeyEnabled(more, tableMoney < most);

            var note = Line(panel, LedgerStyle.MonoItalic, 10.8f, LedgerV2.Muted,
                x + 168f, y, Mathf.Max(40f, w - 168f), LineBox(10.8f),
                "at most " + LedgerText.Cash(most));
            note.overflowMode = TextOverflowModes.Ellipsis;
            return y - (TableKeyH + 8f);
        }

        /// <summary>Which of our streets the word names.</summary>
        float StreetPicker(Transform panel, float x, float y, float w)
        {
            if (tableStreets.Count == 0)
            {
                Line(panel, LedgerStyle.MonoItalic, 11.4f, LedgerV2.Muted, x, y, w,
                    LineBox(11.4f), "no street of ours to name");
                return y - (TableKeyH + 8f);
            }
            return Stepper(panel, x, y, w, StreetName(tableStreets[tableStreet]),
                tableStreets.Count, by =>
                    tableStreet = (tableStreet + by + tableStreets.Count) % tableStreets.Count);
        }

        /// <summary>Which third house a pact or a joined war is against.</summary>
        float ThirdPicker(Transform panel, float x, float y, float w)
        {
            if (tableThirds.Count == 0)
            {
                Line(panel, LedgerStyle.MonoItalic, 11.4f, LedgerV2.Muted, x, y, w,
                    LineBox(11.4f), "no third house");
                return y - (TableKeyH + 8f);
            }
            return Stepper(panel, x, y, w, tableThirds[tableThird].Name,
                tableThirds.Count, by =>
                    tableThird = (tableThird + by + tableThirds.Count) % tableThirds.Count);
        }

        /// <summary>A one-of-many wound with two arrows: what a segmented run would say
        /// if the panel were wide enough for one.</summary>
        float Stepper(Transform panel, float x, float y, float w, string value, int count,
            System.Action<int> step)
        {
            var back = LedgerV2.Button(panel, "<", x, y, 26f, TableKeyH, () =>
            {
                step(-1);
                dirty = true;
            }, LedgerV2.Key.Outline, 11f);
            var text = Line(panel, LedgerStyle.MonoBold, 12f, LedgerV2.Ink, x + 32f, y,
                Mathf.Max(60f, w - 96f), LineBox(12f), value);
            text.overflowMode = TextOverflowModes.Ellipsis;
            var forth = LedgerV2.Button(panel, ">", x + Mathf.Max(60f, w - 96f) + 38f, y,
                26f, TableKeyH, () =>
                {
                    step(1);
                    dirty = true;
                }, LedgerV2.Key.Outline, 11f);
            LedgerV2.KeyEnabled(back, count > 1);
            LedgerV2.KeyEnabled(forth, count > 1);
            return y - (TableKeyH + 8f);
        }

        /// <summary>
        /// WHO CARRIES IT. By telephone it is heard at once by whoever else is on the
        /// line; in a man's hand it is two days on the road, his streetwise moves their
        /// desk our way, and he can be shot at their door.
        /// </summary>
        float CarriedPicker(Transform panel, float x, float y, float w)
        {
            CollectEnvoys();
            var labels = tableEnvoys.Count > 0
                ? new[] { "BY TELEPHONE", "IN PERSON" }
                : new[] { "BY TELEPHONE" };
            LedgerV2.Segmented(panel, x, y - 2f, TableKeyH, labels,
                tableInPerson && tableEnvoys.Count > 0 ? 1 : 0, index =>
                {
                    tableInPerson = index == 1 && tableEnvoys.Count > 0;
                    dirty = true;
                }, Mathf.Max(96f, Mathf.Min(120f, w / labels.Length)), 9.5f);
            y -= TableKeyH + 6f;

            if (tableInPerson && tableEnvoys.Count > 0)
            {
                var envoy = tableEnvoys[tableEnvoy];
                y = Stepper(panel, x, y, w,
                    envoy.FullName + " · " +
                    LedgerText.Stars(envoy.GetHalfSteps(CharacterAttribute.Streetwise)),
                    tableEnvoys.Count, by =>
                        tableEnvoy = (tableEnvoy + by + tableEnvoys.Count) % tableEnvoys.Count);
                var note = Paragraph(panel, LedgerStyle.SerifItalic, 12.3f, LedgerV2.Muted,
                    x, y, w, 30f,
                    "In a man's hand — two days on the road, and it cannot be denied after.",
                    lineSpacing: 1f);
                note.overflowMode = TextOverflowModes.Truncate;
                return y - 32f;
            }

            var plain = Paragraph(panel, LedgerStyle.SerifItalic, 12.3f, LedgerV2.Muted,
                x, y, w, 30f, tableEnvoys.Count > 0
                    ? "Fast, and heard by whoever else is on the line."
                    : "No lieutenant to send — the Don stays home.", lineSpacing: 1f);
            plain.overflowMode = TextOverflowModes.Truncate;
            return y - 32f;
        }

        // ------------------------------------------------------------------ the doing

        /// <summary>
        /// The word said. An answer to something of theirs goes out through Reply or
        /// Ambush, a declaration through SetStance, and everything else is filed as a
        /// proposal - by telephone, or carried by the lieutenant the row names.
        /// </summary>
        void SayTheWord(TableMove move)
        {
            if (move == null || !outfit || tableFor < 0)
                return;

            if (move.AnswerTo >= 0)
            {
                var answer = move.Ambushes
                    ? outfit.Ambush(move.AnswerTo)
                    : outfit.Reply(move.AnswerTo, move.Accepts);
                Filed(answer.Ok, answer.Reason, move.Head);
                return;
            }

            if (move.War)
            {
                var declared = outfit.SetStance(tableFor, Stance.War);
                Filed(declared.Ok, declared.Reason, move.Head);
                return;
            }

            var proposal = new Proposal { To = tableFor, Kind = move.Word };
            if (move.NeedsMoney)
                proposal.Terms.Money = tableMoney;
            if (move.NeedsStreet)
            {
                if (tableStreets.Count == 0)
                {
                    Filed(false, HouseDiplomacy.ReasonNoStreetOfOurs, move.Head);
                    return;
                }
                proposal.Terms.Blocks.Add(tableStreets[tableStreet].Value);
            }
            if (move.NeedsThird)
            {
                if (tableThirds.Count == 0)
                {
                    Filed(false, HouseDiplomacy.ReasonNoThirdHouse, move.Head);
                    return;
                }
                proposal.Terms.Third = tableThirds[tableThird].Id;
            }
            if (move.Word == ProposalKind.TributeTerms && proposal.Terms.Money <= 0)
            {
                Filed(false, "terms need a figure", move.Head);
                return;
            }

            var result = tableInPerson && tableEnvoys.Count > 0
                ? outfit.SendToSitDown(proposal, tableEnvoys[tableEnvoy].Id)
                : outfit.Propose(proposal);
            if (!result.Ok)
            {
                Filed(false, result.Reason, move.Head);
                return;
            }

            var filed = outfit.Diplomacy?.Find(proposal.Id);
            var how = filed != null && filed.InTransit
                ? "carried in person"
                : "by telephone";
            var came = filed == null ? ""
                : filed.InTransit ? " — two days on the road"
                : filed.Status == ProposalStatus.Open ? " — waiting on them"
                : filed.Status == ProposalStatus.Accepted ? " — accepted"
                : " — refused, " + filed.Answer;
            Filed(true, "", move.Head + " — " + how + ", day " +
                          (outfit ? outfit.Campaign.Day : 1) + came);
        }

        /// <summary>What came of a press, in the foot of the sheet and in the note over
        /// the panel. A refusal is the gateway's own sentence, never a paraphrase.
        /// </summary>
        void Filed(bool ok, string reason, string line)
        {
            if (ok)
            {
                tableLastWord = line.EndsWith(".") ? line : line + ".";
                tableNote = "";
                tableMove = -1;
                tableMoney = 0;
            }
            else
                tableNote = reason;
            dirty = true;
        }
    }
}
