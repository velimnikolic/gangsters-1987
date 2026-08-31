using System.Collections.Generic;

namespace LivingCity.Personnel
{
    public enum AssignmentKind
    {
        Pool,
        Crew,
        Front,
        Specialist,
        Boss,
    }

    public readonly struct Assignment
    {
        public readonly AssignmentKind Kind;

        /// <summary>Meaningful only when Kind is Crew.</summary>
        public readonly int CrewId;

        public Assignment(AssignmentKind kind, int crewId)
        {
            Kind = kind;
            CrewId = crewId;
        }
    }

    /// <summary>
    /// Everyone and everything on the outfit's books: members (specialists included),
    /// crews, and the shared equipment stock. Pure data plus derivation - every mutation
    /// goes through RosterOps.
    ///
    /// The pool is DERIVED, never stored: a member is pooled when he is in no crew, is not
    /// the front, and is not a specialist. There is exactly one source of truth per fact
    /// (crew membership lives on the Crew, the front on FrontId, an item's holder on the
    /// item), which is what keeps the coming order/execution layers from inheriting a
    /// dual-write drift bug.
    /// </summary>
    public sealed class Roster
    {
        public readonly List<Character> Members = new List<Character>();
        public readonly List<Crew> Crews = new List<Crew>();
        public readonly List<RosterEquipment> Equipment = new List<RosterEquipment>();
        public OrganizationState Organization { get; } = new OrganizationState();

        public int BossId => Organization.BossId;

        /// <summary>The member managing headquarters; -1 when the desk is empty.</summary>
        public int FrontId = -1;

        /// <summary>The seed this roster was dealt from, kept so a man taken on later -
        /// off a corner or out of the paper - can roll his hidden ceilings off the same
        /// campaign's stream instead of whatever rng happened to be in the caller's
        /// hand. Zero on a roster built by hand.</summary>
        public int Seed;

        int nextCharacterId;
        int nextCrewId;
        int nextEquipmentId;

        public int NextCharacterId() => nextCharacterId++;
        public int NextCrewId() => nextCrewId++;
        public int NextEquipmentId() => nextEquipmentId++;

        public Character Find(int id)
        {
            for (var i = 0; i < Members.Count; i++)
                if (Members[i].Id == id)
                    return Members[i];
            return null;
        }

        public Character FindBoss() => Find(BossId);

        public Crew FindCrew(int crewId)
        {
            for (var i = 0; i < Crews.Count; i++)
                if (Crews[i].Id == crewId)
                    return Crews[i];
            return null;
        }

        /// <summary>The crew a member belongs to - as lieutenant or hood - or null.</summary>
        public Crew CrewOf(int id)
        {
            for (var i = 0; i < Crews.Count; i++)
            {
                var crew = Crews[i];
                if (crew.LieutenantId == id || crew.HoodIds.Contains(id))
                    return crew;
            }
            return null;
        }

        public Assignment AssignmentOf(int id)
        {
            var member = Find(id);
            if (member != null && member.Specialty != Specialty.None)
                return new Assignment(AssignmentKind.Specialist, -1);
            if (id == BossId)
                return new Assignment(AssignmentKind.Boss, -1);
            if (id == FrontId)
                return new Assignment(AssignmentKind.Front, -1);

            var crew = CrewOf(id);
            return crew != null
                ? new Assignment(AssignmentKind.Crew, crew.Id)
                : new Assignment(AssignmentKind.Pool, -1);
        }

        /// <summary>Fills the buffer with pooled member ids, in Members order.</summary>
        public void PoolIds(List<int> buffer)
        {
            buffer.Clear();
            for (var i = 0; i < Members.Count; i++)
            {
                var id = Members[i].Id;
                if (AssignmentOf(id).Kind == AssignmentKind.Pool)
                    buffer.Add(id);
            }
        }

        /// <summary>Fills the buffer with the items this member holds, in Equipment order.</summary>
        public void HeldBy(int id, List<RosterEquipment> buffer)
        {
            buffer.Clear();
            for (var i = 0; i < Equipment.Count; i++)
                if (Equipment[i].HolderId == id)
                    buffer.Add(Equipment[i]);
        }

        public int HeldCount(int id)
        {
            var count = 0;
            for (var i = 0; i < Equipment.Count; i++)
                if (Equipment[i].HolderId == id)
                    count++;
            return count;
        }
    }
}
