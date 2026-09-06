using System;
using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>Street names earned by the skills a character has when dealt. Written
    /// into the existing identity fields once, so practice, promotion and loading a
    /// save cannot rename somebody the player already knows.</summary>
    public static class GangsterNames
    {
        static readonly string[] Surnames =
        {
            "Smith", "Knox", "Brown", "Banks", "Black", "Burns", "Graves", "Cash",
            "Payne", "Crook", "Stone", "Steel", "Butcher", "Barker", "Bishop", "Booth",
            "Brooks", "Buckley", "Cole", "Cook", "Cooper", "Cross", "Dawson", "Drake",
            "Fisher", "Ford", "Frost", "Gibbs", "Grant", "Green", "Hall", "Hardy",
            "Harris", "Hayes", "Hill", "Holt", "Hunt", "King", "Lane", "Mason",
            "Mills", "Price", "Reed", "Shaw", "Sparks", "Ward", "Webb", "Wells",
        };

        // Keyed by skill so the ledger display order cannot change a nickname's meaning.
        static readonly Dictionary<CharacterAttribute, string[]> Trades =
            new Dictionary<CharacterAttribute, string[]>
        {
            [CharacterAttribute.Combat] =
                new[] { "Kneecaps", "Headshot", "Bodybag", "Crowbar", "Haymaker", "Knuckles" },
            [CharacterAttribute.Awareness] =
                new[] { "Nosy", "Wiretap", "Side-Eye", "Snoop", "Eagle-Eye", "Busybody" },
            [CharacterAttribute.Stealth] =
                new[] { "Sneaky", "Shifty", "Backdoor", "Ghost", "Catfeet", "Creep" },
            [CharacterAttribute.Driving] =
                new[] { "Speedy", "Leadfoot", "Skidmark", "Burnout", "Roadkill", "Tailpipe" },
            [CharacterAttribute.Streetwise] =
                new[] { "Hustler", "Grifter", "Scumbag", "Low-Life", "Ratbag", "Gutter" },
            [CharacterAttribute.Leadership] =
                new[] { "Bigshot", "Bossy", "Bigmouth", "King Shit", "Kingpin", "Top Dog" },
            [CharacterAttribute.Organization] =
                new[] { "Cookbooks", "Pencil", "Clipboard", "Tightwad", "Tax Dodge", "Nitpick" },
            [CharacterAttribute.StreetAuthority] =
                new[] { "Big Daddy", "Big Balls", "Godfather", "Hot Shit", "Big Noise", "Fat Cat" },
            [CharacterAttribute.Persuasion] =
                new[] { "Bullshit", "Shit-Talk", "Slick", "Sweet Lips", "Loudmouth", "Blowhard" },
            [CharacterAttribute.Intimidation] =
                new[] { "Bastard", "Shitbrick", "Jawbreaker", "Nutbuster", "Hardass", "Dirtbag" },
            [CharacterAttribute.Connections] =
                new[] { "Asskiss", "Brownnose", "Backslap", "Favor Boy", "Name-Drop", "Bootlick" },
        };

        static readonly string[] Washouts =
            { "Fuckup", "Dipshit", "Shitshow", "Deadbeat", "Halfwit", "Meathead" };
        static readonly string[] Lawyers =
            { "Loophole", "Sleazebag", "Slimy", "Fineprint", "Shark", "Ambulance" };

        public static IReadOnlyList<string> AllSurnames => Surnames;
        public static IReadOnlyList<string> NicknamesFor(CharacterAttribute trade) => Trades[trade];
        public static IEnumerable<string> AllNicknames
        {
            get
            {
                foreach (var trade in Trades.Values)
                    foreach (var name in trade) yield return name;
                foreach (var name in Washouts) yield return name;
                foreach (var name in Lawyers) yield return name;
            }
        }

        /// <summary>Reserve the two original name draws before dealing skills. Name
        /// selection and collision handling then use their own stream.</summary>
        public static int DrawSeed(Random rng) => Potential.Mix(rng.Next(), rng.Next());

        /// <summary>Called only while creating a person, after final skill floors and
        /// specialty are known. Returns the named trade for the classified headline, so
        /// ties cannot advertise one skill while the name describes another. Family
        /// bosses retain the surname on their family's door.</summary>
        public static CharacterAttribute Assign(Roster roster, Character member, int seed,
            string familySurname = null)
        {
            var rng = new Random(seed);
            var names = Pool(member, rng.Next(AttributeScale.Count), out var trade);
            int first = rng.Next(names.Length), last = rng.Next(Surnames.Length);
            int surnameCount = familySurname == null ? Surnames.Length : 1;
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var other in roster.Members)
                if (!ReferenceEquals(other, member)) taken.Add(other.FullName);

            // Walk every pair before using a suffix. Unlike a random retry limit,
            // this cannot silently return a duplicate on a crowded roster.
            for (var suffix = 0; ; suffix++)
                for (var s = 0; s < surnameCount; s++)
                    for (var n = 0; n < names.Length; n++)
                    {
                        member.FirstName = names[(first + n) % names.Length];
                        member.Surname = familySurname ?? Surnames[(last + s) % Surnames.Length];
                        if (suffix > 0) member.FirstName += " " + (suffix + 1);
                        if (!taken.Contains(member.FullName)) return trade;
                    }
        }

        static string[] Pool(Character member, int tieStart, out CharacterAttribute trade)
        {
            trade = CharacterAttribute.Awareness;
            if (member.Specialty == Specialty.Lawyer) return Lawyers;
            if (member.Specialty == Specialty.Accountant)
            {
                trade = CharacterAttribute.Organization;
                return Trades[trade];
            }

            int best = tieStart;
            for (var i = 1; i < AttributeScale.Count; i++)
            {
                int candidate = (tieStart + i) % AttributeScale.Count;
                if (member.GetHalfSteps((CharacterAttribute)candidate) >
                    member.GetHalfSteps((CharacterAttribute)best)) best = candidate;
            }
            trade = (CharacterAttribute)best;
            // Nobody who tops out below three stars gets billed as a skilled hand.
            return member.GetHalfSteps((CharacterAttribute)best) >= 6 ? Trades[trade] : Washouts;
        }
    }
}
