using System;
using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Personnel
{
    /// <summary>
    /// The one editable tuning table for outfit command capacity. Capacity is soft:
    /// these figures describe overload, they never refuse an otherwise valid assignment.
    /// </summary>
    [Serializable]
    public sealed class OrganizationCapacityConfig
    {
        public const int DefaultBossManpower = 70;
        public const int DefaultBossBlocks = 4;
        public const int DefaultLieutenantManpower = 50;
        public const int DefaultLieutenantBlocks = 3;

        public int bossManpower = DefaultBossManpower;
        public int bossBlocks = DefaultBossBlocks;
        public int lieutenantManpower = DefaultLieutenantManpower;
        public int lieutenantBlocks = DefaultLieutenantBlocks;

        public OrganizationLimits Snapshot() => new OrganizationLimits(
            bossManpower, bossBlocks, lieutenantManpower, lieutenantBlocks);
    }

    public readonly struct OrganizationLimits
    {
        public OrganizationLimits(
            int bossManpower, int bossBlocks,
            int lieutenantManpower, int lieutenantBlocks)
        {
            BossManpower = Math.Max(0, bossManpower);
            BossBlocks = Math.Max(0, bossBlocks);
            LieutenantManpower = Math.Max(0, lieutenantManpower);
            LieutenantBlocks = Math.Max(0, lieutenantBlocks);
        }

        public int BossManpower { get; }
        public int BossBlocks { get; }
        public int LieutenantManpower { get; }
        public int LieutenantBlocks { get; }

        public static OrganizationLimits Default => new OrganizationLimits(
            OrganizationCapacityConfig.DefaultBossManpower,
            OrganizationCapacityConfig.DefaultBossBlocks,
            OrganizationCapacityConfig.DefaultLieutenantManpower,
            OrganizationCapacityConfig.DefaultLieutenantBlocks);
    }

    public readonly struct CapacityMeasure
    {
        public CapacityMeasure(int current, int maximum)
        {
            Current = Math.Max(0, current);
            Maximum = Math.Max(0, maximum);
        }

        public int Current { get; }
        public int Maximum { get; }
        public int Overage => Math.Max(0, Current - Maximum);
        public bool IsOverCapacity => Overage > 0;
        public bool IsWithinCapacity => !IsOverCapacity;
    }

    public readonly struct OrganizationCapacityView
    {
        public OrganizationCapacityView(CapacityMeasure manpower, CapacityMeasure blocks)
        {
            Manpower = manpower;
            Blocks = blocks;
        }

        public CapacityMeasure Manpower { get; }
        public CapacityMeasure Blocks { get; }
        public bool IsOverCapacity => Manpower.IsOverCapacity || Blocks.IsOverCapacity;
    }

    public readonly struct OrganizationBlockResponsibility
    {
        public OrganizationBlockResponsibility(TerritoryBlockId blockId, int leaderId)
        {
            BlockId = blockId;
            LeaderId = leaderId;
        }

        public TerritoryBlockId BlockId { get; }
        public int LeaderId { get; }
    }

    /// <summary>
    /// Organization data owned by the Roster. Crew.HoodIds is the lieutenant branch;
    /// this object stores the Boss branch and administrative block responsibility.
    /// Tactical street membership is deliberately absent.
    /// </summary>
    public sealed class OrganizationState
    {
        internal readonly List<int> BossHoodIds = new List<int>();
        internal readonly List<OrganizationBlockResponsibility> BlockResponsibilities =
            new List<OrganizationBlockResponsibility>();

        internal int BossId = -1;
        internal OrganizationLimits Limits = OrganizationLimits.Default;
    }

    public readonly struct OrganizationPerson
    {
        public OrganizationPerson(Character member, Assignment assignment = default)
        {
            hasValue = member != null;
            Id = member != null ? member.Id : -1;
            Name = member != null ? member.FullName : "";
            Rank = member != null ? member.Rank : Rank.Hood;
            Status = member != null ? member.Status : CharacterStatus.Dead;
            Assignment = assignment.Kind;
        }

        public int Id { get; }
        public string Name { get; }
        public Rank Rank { get; }
        public CharacterStatus Status { get; }
        public AssignmentKind Assignment { get; }
        readonly bool hasValue;
        public bool IsValid => hasValue;
        public bool IsAvailable => Status == CharacterStatus.Active;
        public bool IsUnassigned => Rank == Rank.Hood && Assignment == AssignmentKind.Pool;
    }

    /// <summary>No Transform or GameObject crosses the organization query boundary.</summary>
    public readonly struct TacticalPersonnelMapping
    {
        public TacticalPersonnelMapping(int groupId, int commandParentId, int[] personnelIds)
        {
            GroupId = groupId;
            CommandParentId = commandParentId;
            PersonnelIds = personnelIds == null || personnelIds.Length == 0
                ? Array.Empty<int>()
                : Array.AsReadOnly((int[])personnelIds.Clone());
        }

        public int GroupId { get; }
        public int CommandParentId { get; }
        public IReadOnlyList<int> PersonnelIds { get; }
    }

    public interface IOrganizationPhysicalSource
    {
        void CollectPhysicalMappings(List<TacticalPersonnelMapping> into);
    }

    /// <summary>
    /// The public read seam for organization, territory and UI consumers. Callers fill
    /// their own buffers and never inspect the Roster's organization collections.
    /// </summary>
    public interface IOrganizationQuery
    {
        bool TryGetBoss(out OrganizationPerson boss);
        void CollectLieutenants(List<OrganizationPerson> into);
        void CollectHoods(List<OrganizationPerson> into);
        void CollectDirectSubordinates(int leaderId, List<OrganizationPerson> into);
        bool TryGetCommandParent(int characterId, out OrganizationPerson parent);
        void CollectBlockResponsibilities(
            int leaderId, List<OrganizationBlockResponsibility> into);
        OrganizationCapacityView CapacityOf(int leaderId);
        void CollectPhysicalMappings(List<TacticalPersonnelMapping> into);
    }

    public sealed class OrganizationQuery : IOrganizationQuery
    {
        Roster roster;
        IOrganizationPhysicalSource physical;

        public OrganizationQuery(Roster roster = null) => this.roster = roster;

        internal void Bind(Roster value) => roster = value;
        internal void BindPhysical(IOrganizationPhysicalSource value) => physical = value;

        public bool TryGetBoss(out OrganizationPerson boss)
        {
            var member = roster?.FindBoss();
            boss = Person(member);
            return member != null && member.Rank == Rank.Boss;
        }

        public void CollectLieutenants(List<OrganizationPerson> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (roster == null)
                return;

            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var member = roster.Find(roster.Crews[i].LieutenantId);
                if (member != null && member.Rank == Rank.Lieutenant && !member.Gone)
                    into.Add(Person(member));
            }
        }

        /// <summary>
        /// Every living ordinary Hood as an immutable organization snapshot. Assignment
        /// is derived from the Roster at read time, so the Ledger can distinguish a real
        /// unassigned pool member from a Hood who merely reports directly to the Boss
        /// while running the front.
        /// </summary>
        public void CollectHoods(List<OrganizationPerson> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (roster == null)
                return;

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member != null && member.Rank == Rank.Hood &&
                    member.Specialty == Specialty.None && !member.Gone)
                    into.Add(Person(member));
            }
        }

        public void CollectDirectSubordinates(int leaderId, List<OrganizationPerson> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (roster == null)
                return;

            if (leaderId == roster.BossId)
            {
                for (var i = 0; i < roster.Crews.Count; i++)
                {
                    var branch = roster.Crews[i];
                    // His own bodyguard detail is a crew he leads himself. Its MEN are
                    // his subordinates; he is not his own.
                    if (branch.LieutenantId == leaderId)
                    {
                        for (var g = 0; g < branch.HoodIds.Count; g++)
                        {
                            var guard = roster.Find(branch.HoodIds[g]);
                            if (guard != null && !guard.Gone)
                                into.Add(Person(guard));
                        }
                        continue;
                    }

                    var lieutenant = roster.Find(branch.LieutenantId);
                    if (lieutenant != null && !lieutenant.Gone)
                        into.Add(Person(lieutenant));
                }

                var direct = roster.Organization.BossHoodIds;
                for (var i = 0; i < direct.Count; i++)
                {
                    var hood = roster.Find(direct[i]);
                    if (hood != null && !hood.Gone)
                        into.Add(Person(hood));
                }
                return;
            }

            var crew = roster.CrewOf(leaderId);
            if (crew == null || crew.LieutenantId != leaderId)
                return;
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var hood = roster.Find(crew.HoodIds[i]);
                if (hood != null && !hood.Gone)
                    into.Add(Person(hood));
            }
        }

        public bool TryGetCommandParent(int characterId, out OrganizationPerson parent)
        {
            parent = default;
            if (roster == null || characterId == roster.BossId)
                return false;

            var member = roster.Find(characterId);
            if (member == null)
                return false;

            if (member.Rank == Rank.Lieutenant)
            {
                var boss = roster.FindBoss();
                if (boss == null)
                    return false;
                parent = Person(boss);
                return true;
            }

            var direct = roster.Organization.BossHoodIds;
            if (direct.Contains(characterId))
            {
                var boss = roster.FindBoss();
                if (boss == null)
                    return false;
                parent = Person(boss);
                return true;
            }

            var crew = roster.CrewOf(characterId);
            var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
            if (lieutenant == null)
                return false;
            parent = Person(lieutenant);
            return true;
        }

        OrganizationPerson Person(Character member) =>
            new OrganizationPerson(member,
                member != null && roster != null
                    ? roster.AssignmentOf(member.Id)
                    : default);

        public void CollectBlockResponsibilities(
            int leaderId, List<OrganizationBlockResponsibility> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (roster == null)
                return;

            var assignments = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < assignments.Count; i++)
                if (assignments[i].LeaderId == leaderId)
                    into.Add(assignments[i]);
        }

        public OrganizationCapacityView CapacityOf(int leaderId)
        {
            if (roster == null)
                return default;

            var leader = roster.Find(leaderId);
            if (leader == null || (leader.Rank != Rank.Boss && leader.Rank != Rank.Lieutenant))
                return default;

            var manpower = 0;
            if (leader.Rank == Rank.Boss)
            {
                var ids = roster.Organization.BossHoodIds;
                for (var i = 0; i < ids.Count; i++)
                {
                    var hood = roster.Find(ids[i]);
                    if (hood != null && !hood.Gone && hood.Rank == Rank.Hood)
                        manpower++;
                }

                // The bodyguard detail is men he is holding too - they cost him wages
                // and they cost him a place at his own cap.
                var detail = Bodyguards.DetailOf(roster);
                if (detail != null)
                    for (var i = 0; i < detail.HoodIds.Count; i++)
                    {
                        var guard = roster.Find(detail.HoodIds[i]);
                        if (guard != null && !guard.Gone && guard.Rank == Rank.Hood)
                            manpower++;
                    }
            }
            else
            {
                var crew = roster.CrewOf(leaderId);
                if (crew != null && crew.LieutenantId == leaderId)
                    for (var i = 0; i < crew.HoodIds.Count; i++)
                    {
                        var hood = roster.Find(crew.HoodIds[i]);
                        if (hood != null && !hood.Gone && hood.Rank == Rank.Hood)
                            manpower++;
                    }
            }

            var blocks = 0;
            var assignments = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < assignments.Count; i++)
                if (assignments[i].LeaderId == leaderId)
                    blocks++;

            // The ceiling is the config's; what THIS man can actually hold of it is
            // his Leadership's (Command.ManCap). Every page that prints "17 / 32 men"
            // reads it from here, so a lieutenant who gets better at command visibly
            // holds more the same day.
            var limits = roster.Organization.Limits;
            return new OrganizationCapacityView(
                new CapacityMeasure(manpower, Command.ManCap(leader, limits)),
                new CapacityMeasure(blocks, Command.BlockCap(leader, limits)));
        }

        public void CollectPhysicalMappings(List<TacticalPersonnelMapping> into)
        {
            if (into == null)
                return;
            into.Clear();
            physical?.CollectPhysicalMappings(into);
        }
    }

    /// <summary>Actionable diagnostics only; validation never repairs or deletes data.</summary>
    public static class OrganizationValidator
    {
        public static void Validate(
            Roster roster,
            ICollection<TerritoryBlockId> knownBlocks,
            IOrganizationPhysicalSource physical,
            List<string> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (roster == null)
            {
                into.Add("ORG: roster is missing.");
                return;
            }

            var memberIds = new HashSet<int>();
            var bossCount = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member == null)
                {
                    into.Add("ORG: roster contains a null Character record.");
                    continue;
                }
                if (!memberIds.Add(member.Id))
                    into.Add("ORG: duplicate Character ID " + member.Id + ".");
                if (member.Rank == Rank.Boss)
                    bossCount++;
            }

            var boss = roster.FindBoss();
            if (bossCount != 1)
                into.Add("ORG: expected exactly one Boss Character; found " + bossCount + ".");
            if (boss == null || boss.Rank != Rank.Boss)
                into.Add("ORG: BossId " + roster.BossId + " does not resolve to the Boss.");
            else if (boss.Gone)
                into.Add("ORG: authoritative Boss Character " + boss.Id + " is unavailable.");

            var parentCounts = new Dictionary<int, int>();
            var graphParents = new Dictionary<int, int>();
            var direct = roster.Organization.BossHoodIds;
            for (var i = 0; i < direct.Count; i++)
            {
                var hood = roster.Find(direct[i]);
                if (hood == null)
                    into.Add("ORG: Boss branch references missing Character " + direct[i] + ".");
                else if (hood.Rank != Rank.Hood)
                    into.Add("ORG: Boss branch child " + hood.Id + " is " + hood.Rank + ", not Hood.");
                CountParent(parentCounts, direct[i]);
                graphParents[direct[i]] = roster.BossId;
            }

            var crewIds = new HashSet<int>();
            var lieutenantIds = new HashSet<int>();
            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                if (crew == null)
                {
                    into.Add("ORG: roster contains a null lieutenant branch.");
                    continue;
                }
                if (!crewIds.Add(crew.Id))
                    into.Add("ORG: duplicate crew ID " + crew.Id + ".");
                if (!lieutenantIds.Add(crew.LieutenantId))
                    into.Add("ORG: Lieutenant " + crew.LieutenantId + " heads more than one branch.");

                var lieutenant = roster.Find(crew.LieutenantId);
                if (lieutenant == null)
                    into.Add("ORG: branch " + crew.Id + " references missing Lieutenant " +
                             crew.LieutenantId + ".");
                else if (lieutenant.Rank != Rank.Lieutenant)
                    into.Add("ORG: branch " + crew.Id + " parent " + lieutenant.Id +
                             " is " + lieutenant.Rank + ", not Lieutenant.");
                CountParent(parentCounts, crew.LieutenantId);
                graphParents[crew.LieutenantId] = roster.BossId;

                var local = new HashSet<int>();
                for (var h = 0; h < crew.HoodIds.Count; h++)
                {
                    var id = crew.HoodIds[h];
                    if (!local.Add(id))
                        into.Add("ORG: branch " + crew.Id + " lists Hood " + id + " twice.");
                    var hood = roster.Find(id);
                    if (hood == null)
                        into.Add("ORG: branch " + crew.Id + " references missing Character " + id + ".");
                    else if (hood.Rank != Rank.Hood)
                        into.Add("ORG: branch " + crew.Id + " child " + id + " is " +
                                 hood.Rank + ", not Hood.");
                    CountParent(parentCounts, id);
                    graphParents[id] = crew.LieutenantId;
                }
            }

            DetectCycles(graphParents, into);

            foreach (var pair in parentCounts)
                if (pair.Value > 1)
                    into.Add("ORG: Character " + pair.Key + " has " + pair.Value +
                             " direct command parents.");

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member == null || member.Gone || member.Specialty != Specialty.None ||
                    (member.Rank != Rank.Hood && member.Rank != Rank.Lieutenant))
                    continue;
                if (!parentCounts.TryGetValue(member.Id, out var count) || count == 0)
                    into.Add("ORG: Character " + member.Id + " (" + member.Rank +
                             ") has no direct command parent.");
            }

            var assignedBlocks = new HashSet<TerritoryBlockId>();
            var responsibilities = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < responsibilities.Count; i++)
            {
                var assignment = responsibilities[i];
                if (!assignedBlocks.Add(assignment.BlockId))
                    into.Add("ORG: block " + assignment.BlockId + " has duplicate responsibility rows.");
                if (!assignment.BlockId.IsValid ||
                    (knownBlocks != null && !knownBlocks.Contains(assignment.BlockId)))
                    into.Add("ORG: responsibility uses unknown block " + assignment.BlockId + ".");
                var leader = roster.Find(assignment.LeaderId);
                if (leader == null)
                    into.Add("ORG: block " + assignment.BlockId + " references missing leader " +
                             assignment.LeaderId + ".");
                else if (leader.Rank != Rank.Boss && leader.Rank != Rank.Lieutenant)
                    into.Add("ORG: block " + assignment.BlockId + " leader " + leader.Id +
                             " has invalid rank " + leader.Rank + ".");
                else if (leader.Gone)
                    into.Add("ORG: block " + assignment.BlockId + " leader " + leader.Id +
                             " is unavailable.");
            }

            if (physical == null)
                return;

            var mappings = new List<TacticalPersonnelMapping>();
            physical.CollectPhysicalMappings(mappings);
            var query = new OrganizationQuery(roster);
            var groups = new HashSet<int>();
            var physicallyMapped = new HashSet<int>();
            for (var i = 0; i < mappings.Count; i++)
            {
                var mapping = mappings[i];
                if (!groups.Add(mapping.GroupId))
                    into.Add("ORG: duplicate tactical group mapping " + mapping.GroupId + ".");
                if (mapping.PersonnelIds.Count > Crew.MaxTacticalHoods + 1)
                    into.Add("ORG: tactical group " + mapping.GroupId + " projects " +
                             mapping.PersonnelIds.Count + " Characters; maximum physical " +
                             "projection is " + (Crew.MaxTacticalHoods + 1) + ".");
                var leader = roster.Find(mapping.CommandParentId);
                if (leader == null || leader.Gone || leader.Rank != Rank.Lieutenant)
                    into.Add("ORG: tactical group " + mapping.GroupId +
                             " has stale command parent " + mapping.CommandParentId + ".");

                var containsParent = false;
                for (var p = 0; p < mapping.PersonnelIds.Count; p++)
                {
                    var id = mapping.PersonnelIds[p];
                    if (!physicallyMapped.Add(id))
                        into.Add("ORG: Character " + id +
                                 " is projected by more than one tactical group.");
                    var member = roster.Find(id);
                    if (member == null || member.Gone)
                    {
                        into.Add("ORG: tactical group " + mapping.GroupId +
                                 " maps unavailable Character " + id + ".");
                        continue;
                    }
                    if (id == mapping.CommandParentId)
                    {
                        containsParent = true;
                        continue;
                    }
                    if (!query.TryGetCommandParent(id, out var parent) ||
                        parent.Id != mapping.CommandParentId)
                        into.Add("ORG: tactical group " + mapping.GroupId + " maps Hood " +
                                 id + " outside command parent " + mapping.CommandParentId + ".");
                }
                if (!containsParent)
                    into.Add("ORG: tactical group " + mapping.GroupId +
                             " omits its command parent " + mapping.CommandParentId + ".");
            }
        }

        static void CountParent(Dictionary<int, int> counts, int id)
        {
            counts.TryGetValue(id, out var count);
            counts[id] = count + 1;
        }

        static void DetectCycles(Dictionary<int, int> parents, List<string> into)
        {
            var reported = new HashSet<int>();
            foreach (var pair in parents)
            {
                var path = new HashSet<int>();
                var at = pair.Key;
                while (parents.TryGetValue(at, out var parent))
                {
                    if (!path.Add(at))
                    {
                        if (reported.Add(at))
                            into.Add("ORG: command hierarchy cycle reaches Character " + at + ".");
                        break;
                    }
                    at = parent;
                }
            }
        }
    }
}
