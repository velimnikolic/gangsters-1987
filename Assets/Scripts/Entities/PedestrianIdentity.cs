using System.Collections.Generic;

namespace LivingCity.Entities
{
    /// <summary>What a pedestrian does for a living. Grouped by the crowd's picker labels -
    /// a Suit rolls a suit's job, a Worker a worker's - with a handful pinned by the model
    /// itself: the man in the judge's robe is a judge, whatever group dealt him.</summary>
    public enum PedestrianOccupation
    {
        // Suits
        Lawyer, Accountant, Banker, Broker, Salesman, Judge, Butler, Wiseguy,

        // Civilians - gendered pools, because it is 1980 and the barber and the
        // seamstress are different people on this street.
        Barber, Grocer, Baker, Pharmacist, Tailor, Clerk,
        Teacher, Nurse, Secretary, Waitress, Seamstress,

        // Workers
        Dockworker, Mechanic, Electrician, Bricklayer, Plumber, Welder, Farmhand,

        // Fringe
        Veteran, Drifter, Punk, Musician, Unemployed,

        // Children
        Schoolkid,
    }

    /// <summary>
    /// Who a pedestrian IS: name and occupation, derived - never stored - from the per-agent
    /// seed the spawner already hands out. Pure static and free of UnityEngine.Object so the
    /// headless suite can walk every table.
    ///
    /// Derivation is integer hashing of the seed, deliberately NOT the agent's live
    /// System.Random: the crowd's determinism is a tested property, and identity must read
    /// the same however many activity rolls preceded the click. Strings are composed only
    /// when the popup asks (once per selection), so ten thousand walkers cost ten thousand
    /// ints and no strings at all.
    ///
    /// Names are the 1980s American mix the setting wants - Italian, Irish, Polish, Jewish,
    /// Anglo - and every table entry is budgeted: the popup line is 280px NoWrap, and the
    /// title runs at 16px bold, so first names and surnames stop at nine letters
    /// ("Marcheselli" is the char that broke the business popup). The budget is asserted in
    /// PedestrianModelTests, so a name added here that overflows fails there, not on screen.
    /// </summary>
    public static class PedestrianIdentity
    {
        static readonly string[] MaleNames =
        {
            "Frank", "Sal", "Tony", "Vince", "Eddie", "Joey", "Mickey", "Paulie",
            "Lou", "Sonny", "Angelo", "Carmine", "Rocco", "Nick", "Jimmy", "Ray",
            "Artie", "Bruno", "Marty", "Gus", "Walt", "Harold", "Stan", "Bernie",
            "Seamus", "Patrick", "Declan", "Hank", "Earl", "Leon", "Felix", "Oscar",
            "Rudy", "Sid", "Abe", "Ziggy", "Wade", "Curtis", "Vernon", "Chester",
        };

        static readonly string[] FemaleNames =
        {
            "Carmela", "Donna", "Angela", "Rose", "Marie", "Gloria", "Connie", "Teresa",
            "Lucia", "Gina", "Fran", "Dottie", "Peggy", "Ethel", "Ruth", "Irene",
            "Sylvia", "Rita", "Norma", "Doris", "Maureen", "Bridget", "Kathleen", "Nora",
            "Deirdre", "Wanda", "Halina", "Zofia", "Esther", "Miriam", "Sadie", "Pearl",
            "Opal", "Lois", "June", "Marge", "Betty", "Shirley", "Alma", "Vivian",
        };

        static readonly string[] Surnames =
        {
            "Moretti", "DeLuca", "Russo", "Marino", "Greco", "Rizzo", "Ferraro", "Esposito",
            "Lombardi", "Gallo", "Costa", "Conti", "Sullivan", "Murphy", "Kelly", "Byrne",
            "Doyle", "Brennan", "Walsh", "Duffy", "Malone", "O'Hara", "Kowalski", "Nowak",
            "Zielinski", "Wozniak", "Mazur", "Kaminski", "Goldberg", "Katz", "Levine", "Stein",
            "Rosen", "Shapiro", "Weiss", "Johnson", "Miller", "Davis", "Brown", "Wilson",
            "Carter", "Hayes", "Brooks", "Turner", "Reed", "Cole", "Murray", "Quinn",
        };

        // The group pools. A label the picker never uses (or a null from the flat-list
        // fallback) lands in the civilian pool - a generic townsperson, never an empty title.
        static readonly PedestrianOccupation[] Suits =
        {
            PedestrianOccupation.Lawyer, PedestrianOccupation.Accountant,
            PedestrianOccupation.Banker, PedestrianOccupation.Broker,
            PedestrianOccupation.Salesman,
        };

        static readonly PedestrianOccupation[] CivilianMen =
        {
            PedestrianOccupation.Barber, PedestrianOccupation.Grocer,
            PedestrianOccupation.Baker, PedestrianOccupation.Pharmacist,
            PedestrianOccupation.Tailor, PedestrianOccupation.Clerk,
        };

        static readonly PedestrianOccupation[] CivilianWomen =
        {
            PedestrianOccupation.Teacher, PedestrianOccupation.Nurse,
            PedestrianOccupation.Secretary, PedestrianOccupation.Waitress,
            PedestrianOccupation.Seamstress, PedestrianOccupation.Clerk,
        };

        static readonly PedestrianOccupation[] Workers =
        {
            PedestrianOccupation.Dockworker, PedestrianOccupation.Mechanic,
            PedestrianOccupation.Electrician, PedestrianOccupation.Bricklayer,
            PedestrianOccupation.Plumber, PedestrianOccupation.Welder,
        };

        static readonly PedestrianOccupation[] Fringe =
        {
            PedestrianOccupation.Veteran, PedestrianOccupation.Drifter,
            PedestrianOccupation.Musician, PedestrianOccupation.Unemployed,
        };

        /// <summary>Read-only views for the tests, which budget every entry.</summary>
        public static IReadOnlyList<string> AllMaleNames => MaleNames;
        public static IReadOnlyList<string> AllFemaleNames => FemaleNames;
        public static IReadOnlyList<string> AllSurnames => Surnames;

        /// <summary>
        /// "woman" and "girl" BEFORE any thought of "man": every woman prefab contains
        /// "man" as a substring, which is exactly the trap this method exists to step over.
        /// </summary>
        public static bool IsFemale(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return false;

            var name = prefabName.ToLowerInvariant();
            return name.Contains("woman") || name.Contains("girl");
        }

        /// <summary>
        /// The job. Model pins first - a judge's robe outranks the group roll - then the
        /// group's pool, hashed off the seed so two walkers of the same model still differ.
        /// </summary>
        public static PedestrianOccupation Occupation(
            string groupLabel, string prefabName, bool female, int seed)
        {
            var name = prefabName == null ? string.Empty : prefabName.ToLowerInvariant();

            // "construction" before "worker": one contains the other.
            if (name.Contains("judge")) return PedestrianOccupation.Judge;
            if (name.Contains("butler")) return PedestrianOccupation.Butler;
            if (name.Contains("mafia")) return PedestrianOccupation.Wiseguy;
            if (name.Contains("homeless")) return PedestrianOccupation.Drifter;
            if (name.Contains("punk")) return PedestrianOccupation.Punk;
            if (name.Contains("soldier")) return PedestrianOccupation.Veteran;
            if (name.Contains("farm")) return PedestrianOccupation.Farmhand;
            if (name.Contains("golf")) return PedestrianOccupation.Salesman;
            if (name.Contains("construction")) return PedestrianOccupation.Bricklayer;
            if (name.Contains("boy") || name.Contains("girl"))
                return PedestrianOccupation.Schoolkid;

            var pool = groupLabel switch
            {
                "Suits" => Suits,
                "Civilians" => female ? CivilianWomen : CivilianMen,
                "Workers" => Workers,
                "Fringe" => Fringe,
                "Children" => null,
                _ => female ? CivilianWomen : CivilianMen,
            };

            if (pool == null)
                return PedestrianOccupation.Schoolkid;

            return pool[(int)(Mix(seed, 3u) % (uint)pool.Length)];
        }

        public static string FirstName(int seed, bool female)
        {
            var table = female ? FemaleNames : MaleNames;
            return table[(int)(Mix(seed, 1u) % (uint)table.Length)];
        }

        public static string Surname(int seed) =>
            Surnames[(int)(Mix(seed, 2u) % (uint)Surnames.Length)];

        /// <summary>Empty for an unmapped value on purpose - that is what the
        /// exhaustiveness test asserts against.</summary>
        public static string OccupationLabel(PedestrianOccupation occupation) => occupation switch
        {
            PedestrianOccupation.Lawyer => "Lawyer",
            PedestrianOccupation.Accountant => "Accountant",
            PedestrianOccupation.Banker => "Banker",
            PedestrianOccupation.Broker => "Broker",
            PedestrianOccupation.Salesman => "Salesman",
            PedestrianOccupation.Judge => "Judge",
            PedestrianOccupation.Butler => "Butler",
            PedestrianOccupation.Wiseguy => "Wiseguy",
            PedestrianOccupation.Barber => "Barber",
            PedestrianOccupation.Grocer => "Grocer",
            PedestrianOccupation.Baker => "Baker",
            PedestrianOccupation.Pharmacist => "Pharmacist",
            PedestrianOccupation.Tailor => "Tailor",
            PedestrianOccupation.Clerk => "Clerk",
            PedestrianOccupation.Teacher => "Teacher",
            PedestrianOccupation.Nurse => "Nurse",
            PedestrianOccupation.Secretary => "Secretary",
            PedestrianOccupation.Waitress => "Waitress",
            PedestrianOccupation.Seamstress => "Seamstress",
            PedestrianOccupation.Dockworker => "Dockworker",
            PedestrianOccupation.Mechanic => "Mechanic",
            PedestrianOccupation.Electrician => "Electrician",
            PedestrianOccupation.Bricklayer => "Bricklayer",
            PedestrianOccupation.Plumber => "Plumber",
            PedestrianOccupation.Welder => "Welder",
            PedestrianOccupation.Farmhand => "Farmhand",
            PedestrianOccupation.Veteran => "Veteran",
            PedestrianOccupation.Drifter => "Drifter",
            PedestrianOccupation.Punk => "Punk",
            PedestrianOccupation.Musician => "Musician",
            PedestrianOccupation.Unemployed => "Unemployed",
            PedestrianOccupation.Schoolkid => "Schoolkid",
            _ => string.Empty,
        };

        /// <summary>"Frank Moretti — Baker". Composed once per popup read and cached by the
        /// caller; nothing here runs at spawn.</summary>
        public static string ComposeTitle(int seed, bool female, PedestrianOccupation occupation) =>
            $"{FirstName(seed, female)} {Surname(seed)} — {OccupationLabel(occupation)}";

        /// <summary>
        /// Splitmix-style avalanche. The salt keys the field - first name, surname,
        /// occupation - so one seed yields independent draws without a Random instance.
        /// </summary>
        static uint Mix(int seed, uint salt)
        {
            var z = (uint)seed + 0x9E3779B9u * (salt + 1u);
            z ^= z >> 16; z *= 0x85EBCA6Bu;
            z ^= z >> 13; z *= 0xC2B2AE35u;
            z ^= z >> 16;
            return z;
        }
    }
}
