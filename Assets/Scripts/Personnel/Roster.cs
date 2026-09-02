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

        /// <summary>The campaign year as of the last day tick, written through by the
        /// runner. The Personnel core has no business knowing about the Outfit layer's
        /// calendar, but a man taken on in the third year has to be dealt a date of
        /// birth in the third year - otherwise every late recruit reads three years
        /// older than he is. Zero means "the campaign's opening year".</summary>
        public int Year;

        /// <summary>The campaign day as of the last tick, written through by the runner
        /// for the same reason as <see cref="Year"/>: a rank change has to be stamped
        /// with the day it happened, and the rules layer cannot reach the calendar.</summary>
        public int Day;

        /// <summary>
        /// Which house's book this is. The player's outfit is house 0, and a roster
        /// built by hand - every test fixture - is house 0 too, which is why the plain
        /// constructor still works and still numbers from zero.
        ///
        /// It is written ONCE, by <see cref="Create"/>, and it is not how anybody finds
        /// out whose man somebody is: ids are unique across all twenty-one books by
        /// construction (the counters below open on the house's own span), so nothing
        /// anywhere decodes a house from an id, and nothing may start.
        /// </summary>
        public int GangId { get; private set; }

        /// <summary>How far apart two houses' character ids stand. A house that
        /// out-recruited a hundred thousand men would run into the next one's numbers,
        /// which is a hundred thousand more men than the city has pavement for.</summary>
        public const int CharacterIdSpan = 100_000;

        /// <summary>The same for crews. A house may run eight (Command.MaxLieutenants);
        /// the span is a thousand.</summary>
        public const int CrewIdSpan = 1_000;

        /// <summary>And for the stock. A gun is numbered off its own counter, so this
        /// span is independent of the men's.</summary>
        public const int EquipmentIdSpan = 100_000;

        int nextCharacterId;
        int nextCrewId;
        int nextEquipmentId;

        /// <summary>
        /// A book for one house, with its counters opened on that house's span. House 0
        /// gets exactly the numbers the outfit has always had - the first man off this
        /// counter is character 0 - so the player's campaign is dealt unchanged.
        /// </summary>
        public static Roster Create(int gangId)
        {
            if (gangId < 0)
                gangId = 0;
            return new Roster
            {
                GangId = gangId,
                nextCharacterId = gangId * CharacterIdSpan,
                nextCrewId = gangId * CrewIdSpan,
                nextEquipmentId = gangId * EquipmentIdSpan,
            };
        }

        /// <summary>Where the three counters stand, for the save. Reading one must not
        /// advance it, which is why they are not NextXId().</summary>
        public int PeekNextCharacterId => nextCharacterId;
        public int PeekNextCrewId => nextCrewId;
        public int PeekNextEquipmentId => nextEquipmentId;

        /// <summary>
        /// THE LOAD BOUNDARY for a roster's own numbers. The lists are refilled by
        /// RosterSnapshot; this is the identity and the three counters, which have no
        /// other setter on purpose - an id counter that anything could wind back would
        /// hand two men the same number.
        /// </summary>
        public void RestoreIdentity(int gangId, int seed, int year, int day, int frontId,
            int nextCharacter, int nextCrew, int nextEquipment)
        {
            GangId = gangId;
            Seed = seed;
            Year = year;
            Day = day;
            FrontId = frontId;
            nextCharacterId = nextCharacter;
            nextCrewId = nextCrew;
            nextEquipmentId = nextEquipment;
        }

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
