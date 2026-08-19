using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Personnel;

namespace LivingCity.Gangs
{
    /// <summary>
    /// Which Synty bodies may be gangsters, and the rule that stops a crew from being
    /// one man copied. The two tables were picked by hand off the cast catalog
    /// (Tools > City > Catalog > Build Cast Catalog Scene) - everything else the packs
    /// ship is a civilian, a cop or a costume, and none of it belongs on a crew.
    ///
    /// Plain pack prefab names. LedgerModelSet.PersonNamed resolves them and tolerates
    /// the retired "_AI" suffix, so this one table answers the ledger's mugshots, the
    /// outfit's men on the street, and every rival mob - and a body added here shows up
    /// in all three at once.
    ///
    /// Engine-free like the rest of the Gangs core, so the headless suite asserts the
    /// tables and the no-twins rule rather than the player finding two identical hoods
    /// standing on the same corner.
    /// </summary>
    public static class GangLooks
    {
        /// <summary>The muscle - the men who do things. Wide enough that a crew of four
        /// never has to repeat, and mixed across the packs so a line of hoods is a line
        /// of people rather than one pack's idea of a thug.
        ///
        /// The women on it are approved stock waiting on a roster that deals women:
        /// RosterSeeder draws male first names only today, and <see cref="Draw"/> hands
        /// nobody a body of the wrong sex, so those three simply are not dealt yet.
        /// </summary>
        public static readonly string[] Hoods =
        {
            "SM_Chr_GangMember_Male_01",
            "SM_Chr_Gang_Male_01",
            "SM_Chr_Criminal_Male_01",
            "SM_Chr_GangMember_Male_02",
            "SM_Chr_Gang_Male_02",
            "SM_Gen_Chr_Street_Male_01",
            "SM_Chr_GangMember_Male_03",
            "SM_Chr_Goon_01",
            "SM_Chr_Bouncer_Male_01",
            "SM_Chr_Criminal_Female_01",
            "SM_Chr_Salesman_01",
            "SM_Chr_GangMember_Female_01",
            "SM_Chr_Gang_Female_01",
        };

        /// <summary>The men who run things. A capo is dressed a rank above his hoods -
        /// that is how the street reads which of five men to shoot at.</summary>
        public static readonly string[] Lieutenants =
        {
            "SM_Chr_Italian_Gangster_01",
            "SM_Chr_Kingpin_01",
            "SM_Gen_Chr_Business_Male_01",
            "Character_BusinessMan_Suit",
            "SM_Chr_Goon_01",
            "SM_Chr_Criminal_Male_01",
        };

        // --------------------------------------------------------------- the draw

        /// <summary>The body at this seat: the table entry a stable number points at,
        /// walked forward past every body of the wrong sex and past whatever the man's
        /// crewmates already wear. Stable on its own - the same number and the same
        /// neighbours always give the same man - so a face only moves when the crew
        /// around it does.
        ///
        /// <paramref name="taken"/> may be null (a man in nobody's crew: the pool, the
        /// front, a lone rival), and entries in it may carry the "_AI" suffix. A sex
        /// with no free body left on the table falls back to one of the other's rather
        /// than leaving a man with no body at all - a table too short for its crew is
        /// the fault, and it shows.</summary>
        public static string Draw(string[] table, int stable, ICollection<string> taken,
                                  bool female = false)
        {
            if (table == null || table.Length == 0)
                return "";

            var start = ((stable % table.Length) + table.Length) % table.Length;
            for (var pass = 0; pass < 2; pass++)
                for (var step = 0; step < table.Length; step++)
                {
                    var look = table[(start + step) % table.Length];
                    if (IsTaken(taken, look)) continue;
                    // first pass takes only his own sex; the second takes anything free
                    if (pass == 0 && PedestrianIdentity.IsFemale(look) != female) continue;
                    return look;
                }

            // more men in one crew than there are bodies on the table - the crew is over
            // its own MaxHoods, and a repeat is better than nobody standing there
            return table[start];
        }

        /// <summary>One crew's worth of soldier bodies: all different, and none of them
        /// the body the lieutenant is already wearing. What a rival mob is dealt when it
        /// is put on a pavement by hand - the outfit's own men come out of the roster,
        /// one seat at a time (<see cref="LookFor"/>).
        ///
        /// The walk starts at the gang's own staple (GangCatalog.SoldierModels), so a
        /// Falcone crew still reads as Falcone's - its staple leads, the rest of the
        /// stock fills in behind him. Rival hoods are dealt male names (RoadDemoBuilder,
        /// CrewDemoBuilder), so they are dealt men's bodies.</summary>
        public static List<string> HoodsFor(string lieutenantLook, string staple, int count)
        {
            var taken = new List<string>(count + 1);
            if (!string.IsNullOrEmpty(lieutenantLook))
                taken.Add(Bare(lieutenantLook));

            var start = IndexOf(staple);
            var looks = new List<string>(count);
            for (var k = 0; k < count; k++)
            {
                var look = Draw(Hoods, start + k, taken);
                taken.Add(look);
                looks.Add(look);
            }

            return looks;
        }

        /// <summary>Whether this body is spoken for by the mob - on either table above.
        /// A body that can be dealt to a gangster is NOT a passer-by: the crowd pools
        /// (RoadDemoBuilder, CrewDemoBuilder) scan the packs wholesale and drop
        /// everything this answers true for, the way they already drop uniformed
        /// officers. Otherwise the same coat walks past the player as a nobody and
        /// stands on the corner as one of Falcone's men, and the street stops reading.
        ///
        /// The two tables are the whole rule: a body added to Hoods or Lieutenants
        /// leaves the crowd in the next build with no edit anywhere else.</summary>
        public static bool IsGangBody(string look)
        {
            var bare = Bare(look);
            if (string.IsNullOrEmpty(bare))
                return false;
            foreach (var name in Hoods)
                if (name == bare) return true;
            foreach (var name in Lieutenants)
                if (name == bare) return true;
            return false;
        }

        /// <summary>Where this body sits in the hood stock, or 0 for one that is not on
        /// it - an unapproved name never throws, it simply starts the walk at the top.
        /// </summary>
        public static int IndexOf(string look)
        {
            var bare = Bare(look);
            for (var i = 0; i < Hoods.Length; i++)
                if (Hoods[i] == bare)
                    return i;
            return 0;
        }

        // ------------------------------------------------------------ the outfit's own

        /// <summary>Suits for the men who run things, street muscle for the men who do
        /// them - which table a man of this rank sits for.</summary>
        public static string[] TableFor(Character member) =>
            member != null &&
            (member.Rank == Rank.Lieutenant || member.Specialty != Specialty.None)
                ? Lieutenants : Hoods;

        /// <summary>The body this member of the outfit wears - in his ledger photograph
        /// and on the street both, because they are the same man.
        ///
        /// Picked by his stable Id, so sixty men are not one man in one coat and the
        /// same man always sits for the same photo. Then: NO TWO MEN IN ONE CREW WEAR
        /// THE SAME BODY. The crew is cast in one fixed order - the lieutenant first,
        /// then his hoods as the crew lists them - and each man walks his own pick
        /// forward past what the men before him already wear. A man who stays put keeps
        /// his coat; only being moved into another crew can change it, and DemoCrews
        /// swaps the body on the street where the old one stood.
        ///
        /// <paramref name="roster"/> may be null (no director yet) and the man may be in
        /// no crew (the pool, the front) - either way he simply takes his own seat.
        /// </summary>
        public static string LookFor(Character member, Roster roster)
        {
            if (member == null)
                return "";

            var crew = roster?.CrewOf(member.Id);
            if (crew == null)
                return Draw(TableFor(member), member.Id, null, IsFemale(member));

            var taken = new List<string>(Crew.MaxHoods + 1);
            foreach (var id in CastOrder(crew))
            {
                var man = id == member.Id ? member : roster.Find(id);
                if (man == null) continue;

                var look = Draw(TableFor(man), man.Id, taken, IsFemale(man));
                if (id == member.Id) return look;
                taken.Add(look);
            }

            // his crew does not list him (mid-edit): he is nobody's neighbour this frame
            return Draw(TableFor(member), member.Id, null, IsFemale(member));
        }

        /// <summary>Whether the books call this member a woman. The roster carries no sex
        /// field - it deals out of the shared name tables - so the name is what answers,
        /// and a roster that only deals men (today's) only ever asks for men's bodies.
        /// </summary>
        public static bool IsFemale(Character member)
        {
            if (member == null || string.IsNullOrEmpty(member.FirstName))
                return false;
            foreach (var name in PedestrianIdentity.AllFemaleNames)
                if (name == member.FirstName)
                    return true;
            return false;
        }

        /// <summary>The order a crew sits for its photographs - the lieutenant, then his
        /// hoods as the crew lists them. Fixed, so a man's coat never depends on which of
        /// his crewmates the caller happened to ask about first.</summary>
        static IEnumerable<int> CastOrder(Crew crew)
        {
            yield return crew.LieutenantId;
            foreach (var id in crew.HoodIds)
                yield return id;
        }

        /// <summary>The plain pack name behind a model reference - GangCatalog still
        /// names its men the crowd's old way ("SM_Chr_Goon_01_AI"), and a suffix must
        /// not make one body look like two.</summary>
        public static string Bare(string name) =>
            !string.IsNullOrEmpty(name) && name.EndsWith("_AI")
                ? name.Substring(0, name.Length - 3)
                : name;

        static bool IsTaken(ICollection<string> taken, string look)
        {
            if (taken == null)
                return false;
            foreach (var worn in taken)
                if (Bare(worn) == look)
                    return true;
            return false;
        }
    }
}
