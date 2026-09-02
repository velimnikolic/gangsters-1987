using System.Collections.Generic;

namespace LivingCity.Personnel
{
    public enum SortKey
    {
        /// <summary>Crews in formation order, men as entered.</summary>
        Roster,
        Attribute,
        Loyalty,

        /// <summary>The ledger's default: the men something has happened to, first.
        /// Needs a <see cref="ViewOptions.Board"/> to sort by; without one it falls
        /// back to the roster's own order, which is what the page did before the
        /// notability layer existed.</summary>
        Notability,
    }

    public enum RankFilter
    {
        All,
        Hoods,
        Lieutenants,
        Specialists,
    }

    public enum AssignmentFilter
    {
        All,
        Crews,
        Pool,
        Front,
    }

    public enum AvailabilityFilter
    {
        All,
        ActiveOnly,
        Unavailable,
    }

    public struct ViewOptions
    {
        public SortKey Sort;

        /// <summary>Meaningful only when Sort is Attribute.</summary>
        public CharacterAttribute SortAttribute;

        public RankFilter Rank;
        public AssignmentFilter Assignment;
        public AvailabilityFilter Availability;

        /// <summary>Assign mode's flag: emit the FRONT and POOL headers even when empty,
        /// because in that mode a header is a drop target, not a summary.</summary>
        public bool IncludeEmptySections;

        /// <summary>Today's notability figures, when there are any. A reference and not
        /// a value: it is rebuilt at every day tick and read here, never written, and it
        /// stays OUT of <see cref="Hash()"/> - the almanac repaints on the roster's own
        /// version, and a board that changed identity every midnight would make the
        /// dirty key fire on a day nothing else moved.</summary>
        public NotabilityBoard Board;

        /// <summary>Cheap dirty key for the almanac's repaint check.</summary>
        public int Hash() =>
            (int)Sort | ((int)SortAttribute << 2) | ((int)Rank << 6) |
            ((int)Assignment << 8) | ((int)Availability << 10) |
            (IncludeEmptySections ? 1 << 12 : 0);
    }

    public enum RowKind
    {
        CrewHeader,
        FrontHeader,
        PoolHeader,
        SpecialistHeader,
        Character,

        /// <summary>The lieutenant's own row, first under his crew's header. A member
        /// row like Character - selectable, inspectable, demotable - but marked so the
        /// painter can set him apart from the hoods below him.</summary>
        Lieutenant,
    }

    public readonly struct LedgerRow
    {
        public readonly RowKind Kind;

        /// <summary>The member this row shows - the lieutenant on a CrewHeader, -1 on the
        /// section headers, the man himself on a Character row.</summary>
        public readonly int CharacterId;

        /// <summary>The crew a CrewHeader stands for; -1 elsewhere.</summary>
        public readonly int CrewId;

        public LedgerRow(RowKind kind, int characterId, int crewId)
        {
            Kind = kind;
            CharacterId = characterId;
            CrewId = crewId;
        }
    }

    /// <summary>
    /// Computes the whole list shape - grouping, filtering, sorting - as data, so the
    /// sixty-man scan the screen exists for is a tested property rather than a hope, and
    /// the almanac itself is a dumb painter of rows.
    ///
    /// Grouping is the ledger's spine and survives every sort: crews first (each crew's
    /// header IS its lieutenant), then THE FRONT, THE POOL, SPECIALISTS. Sorting reorders
    /// men only WITHIN their group; filters drop rows, and a group that ends up empty
    /// drops its header with it. A crew group counts as non-empty if ANY of its men - the
    /// lieutenant included - passes, and its header always shows then: rows without their
    /// grouping is just a list, and the crew a man belongs to is half of what the player
    /// came to read.
    /// </summary>
    public static class RosterView
    {
        static readonly List<Character> Scratch = new List<Character>();

        public static void Build(Roster roster, in ViewOptions options, List<LedgerRow> rows)
        {
            rows.Clear();

            if (options.Assignment == AssignmentFilter.All ||
                options.Assignment == AssignmentFilter.Crews)
                BuildCrews(roster, options, rows);

            if (options.Assignment == AssignmentFilter.All ||
                options.Assignment == AssignmentFilter.Front)
                BuildFront(roster, options, rows);

            if (options.Assignment == AssignmentFilter.All ||
                options.Assignment == AssignmentFilter.Pool)
                BuildPool(roster, options, rows);

            if (options.Assignment == AssignmentFilter.All)
                BuildSpecialists(roster, options, rows);
        }

        static void BuildCrews(Roster roster, in ViewOptions options, List<LedgerRow> rows)
        {
            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                var lieutenant = roster.Find(crew.LieutenantId);

                Scratch.Clear();
                for (var h = 0; h < crew.HoodIds.Count; h++)
                {
                    var hood = roster.Find(crew.HoodIds[h]);
                    if (hood != null && Passes(hood, options))
                        Scratch.Add(hood);
                }

                var anyone = Scratch.Count > 0 ||
                             (lieutenant != null && Passes(lieutenant, options));
                if (!anyone && !options.IncludeEmptySections)
                    continue;

                rows.Add(new LedgerRow(RowKind.CrewHeader, crew.LieutenantId, crew.Id));

                // The lieutenant is a MEMBER of his crew, not a property of it: he gets
                // his own row, always first - the head of the crew is not subject to the
                // sort his men are - and only when he himself passes the filters.
                if (lieutenant != null && Passes(lieutenant, options))
                    rows.Add(new LedgerRow(RowKind.Lieutenant, crew.LieutenantId, crew.Id));

                SortScratch(options);
                for (var s = 0; s < Scratch.Count; s++)
                    rows.Add(new LedgerRow(RowKind.Character, Scratch[s].Id, -1));
            }
        }

        static void BuildFront(Roster roster, in ViewOptions options, List<LedgerRow> rows)
        {
            var front = roster.FrontId >= 0 ? roster.Find(roster.FrontId) : null;
            var shown = front != null && Passes(front, options);
            if (!shown && !options.IncludeEmptySections)
                return;

            rows.Add(new LedgerRow(RowKind.FrontHeader, -1, -1));
            if (shown)
                rows.Add(new LedgerRow(RowKind.Character, front.Id, -1));
        }

        static void BuildPool(Roster roster, in ViewOptions options, List<LedgerRow> rows)
        {
            Scratch.Clear();
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (roster.AssignmentOf(member.Id).Kind == AssignmentKind.Pool &&
                    Passes(member, options))
                    Scratch.Add(member);
            }

            if (Scratch.Count == 0 && !options.IncludeEmptySections)
                return;

            rows.Add(new LedgerRow(RowKind.PoolHeader, -1, -1));
            SortScratch(options);
            for (var s = 0; s < Scratch.Count; s++)
                rows.Add(new LedgerRow(RowKind.Character, Scratch[s].Id, -1));
        }

        static void BuildSpecialists(Roster roster, in ViewOptions options, List<LedgerRow> rows)
        {
            Scratch.Clear();
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Specialty != Specialty.None && Passes(member, options))
                    Scratch.Add(member);
            }

            if (Scratch.Count == 0)
                return;

            rows.Add(new LedgerRow(RowKind.SpecialistHeader, -1, -1));
            SortScratch(options);
            for (var s = 0; s < Scratch.Count; s++)
                rows.Add(new LedgerRow(RowKind.Character, Scratch[s].Id, -1));
        }

        static bool Passes(Character member, in ViewOptions options)
        {
            switch (options.Rank)
            {
                case RankFilter.Hoods:
                    if (member.Rank != Rank.Hood || member.Specialty != Specialty.None)
                        return false;
                    break;
                case RankFilter.Lieutenants:
                    if (member.Rank != Rank.Lieutenant)
                        return false;
                    break;
                case RankFilter.Specialists:
                    if (member.Specialty == Specialty.None)
                        return false;
                    break;
            }

            switch (options.Availability)
            {
                case AvailabilityFilter.ActiveOnly:
                    return member.Status == CharacterStatus.Active;
                case AvailabilityFilter.Unavailable:
                    return member.Status != CharacterStatus.Active;
            }

            return true;
        }

        /// <summary>Descending by the sort value - the screen answers "who is BEST at X" -
        /// with id as the tiebreak so equal men never swap places between repaints.</summary>
        static void SortScratch(in ViewOptions options)
        {
            if (options.Sort == SortKey.Roster)
                return;
            // Notability with nothing to read it from is the roster's own order. A page
            // painted before the first day tick - and every demo scene with no campaign
            // in it - lands here.
            if (options.Sort == SortKey.Notability && options.Board == null)
                return;

            // Insertion sort: the list is a group, groups are small, and List.Sort's
            // comparison delegate would allocate per rebuild.
            for (var i = 1; i < Scratch.Count; i++)
            {
                var current = Scratch[i];
                var key = KeyOf(current, options);
                var j = i - 1;
                while (j >= 0)
                {
                    var other = Scratch[j];
                    var otherKey = KeyOf(other, options);
                    if (otherKey > key || (otherKey == key && other.Id < current.Id))
                        break;
                    Scratch[j + 1] = other;
                    j--;
                }
                Scratch[j + 1] = current;
            }
        }

        /// <summary>The one figure a sorted roll is ordered by, whichever sort it
        /// is.</summary>
        static int KeyOf(Character man, in ViewOptions options) => options.Sort switch
        {
            SortKey.Loyalty => man.Loyalty,
            SortKey.Notability => options.Board.ScoreOf(man.Id),
            _ => man.GetHalfSteps(options.SortAttribute),
        };
    }
}
