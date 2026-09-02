using LivingCity.Gangs;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>
    /// The city's twenty-one families, dealt once from the city's own seed. One
    /// <see cref="House"/> apiece - the player's outfit is house 0 - and one shared
    /// classified column, because there is one newspaper in town.
    ///
    /// This is the only place a campaign's books are created, and there is exactly one
    /// of it (<see cref="Current"/>). The two directors are hosts of it, not owners:
    /// whichever of them wakes first calls <see cref="Ensure"/>, and both then read the
    /// same houses.
    ///
    /// Pure and free of UnityEngine. The Play-mode reset and the fault printer are
    /// wired from the Gameplay layer (UnderworldHost).
    /// </summary>
    public sealed class Underworld
    {
        /// <summary>The books this Play is running on, or null before anybody has
        /// dealt them.</summary>
        public static Underworld Current { get; private set; }

        /// <summary>Where a fault goes when there is nobody to throw at - a second deal
        /// from a different seed. Wired to the console by the Gameplay layer; null in a
        /// headless suite, where the string would go nowhere anyway.</summary>
        public static System.Action<string> Fault;

        readonly House[] houses = new House[GangCatalog.GangCount];

        /// <summary>The seed the whole underworld was dealt from - the city's own.</summary>
        public int CitySeed { get; private set; }

        /// <summary>One newspaper for the whole city: the men who advertise this
        /// morning advertise to everybody, and the first house to sign one takes
        /// him.</summary>
        public HireMarket Column { get; } = new HireMarket();

        public int Count => houses.Length;

        public House Of(int gangId) =>
            gangId >= 0 && gangId < houses.Length ? houses[gangId] : null;

        public House Player => Of(GangCatalog.PlayerGangId);

        /// <summary>
        /// Deals the whole underworld: every house's roster off its own stream, its own
        /// safe with the same opening stake, and the same organization rules, bodyguard
        /// detail and arms deal the player's outfit gets. One pass, one rule, twenty-one
        /// times.
        /// </summary>
        public static Underworld Deal(int citySeed)
        {
            var underworld = new Underworld { CitySeed = citySeed };
            for (var gangId = 0; gangId < underworld.houses.Length; gangId++)
            {
                var roster = RosterSeeder.Generate(citySeed, gangId);
                RosterOps.ConfigureOrganization(roster, OrganizationLimits.Default);
                Bodyguards.FallIn(roster);
                RosterOps.NormalizeArms(roster);

                // The runner is told WHOSE books it keeps. Twenty-one of these tick
                // every midnight and anything that asks "which family am I?" - where a
                // defector's door opens, most of all - reads it from here rather than
                // assuming house zero.
                var runner = new CampaignRunner { Seed = citySeed, GangId = gangId };
                runner.OpenFirstSheet();
                underworld.houses[gangId] = new House(gangId, roster, runner);
            }
            return underworld;
        }

        /// <summary>
        /// The books, dealt if nobody has dealt them yet. Idempotent by design: the
        /// city builder, the personnel director and the outfit director all call it and
        /// whichever runs first wins. A second call naming a DIFFERENT seed is a fault
        /// and not a re-deal - two halves of one Play would otherwise be looking at two
        /// different cities.
        /// </summary>
        public static Underworld Ensure(int citySeed)
        {
            if (Current == null)
                Current = Deal(citySeed);
            else if (Current.CitySeed != citySeed)
                Fault?.Invoke("[Underworld] already dealt from seed " + Current.CitySeed +
                              "; the call for seed " + citySeed + " was ignored.");
            return Current;
        }

        /// <summary>Statics outlive Play when domain reload is off - the BusinessDeeds
        /// discipline, closed the same way. Called from the Gameplay layer's
        /// SubsystemRegistration hook, before any scene wakes.</summary>
        public static void ResetForPlay() => Current = null;

        /// <summary>Puts a dealt underworld back in the holder. The save file's door
        /// when there is one (RIVAL-010), and the one the headless suite uses to leave
        /// a running Play exactly as it found it.</summary>
        public static void Restore(Underworld underworld) => Current = underworld;

        /// <summary>
        /// Every house works its book for the hours that passed. The player's house
        /// goes first so a page painted this frame reads his outfit as it was after his
        /// own men moved, never mid-sweep.
        /// </summary>
        public bool AdvanceHours(float hours)
        {
            var moved = false;
            for (var i = 0; i < houses.Length; i++)
            {
                var house = houses[i];
                if (house == null || house.Extinct)
                    continue;
                if (house.Runner.AdvanceHours(house.Roster, hours))
                {
                    house.Touch();
                    moved = true;
                }
            }
            return moved;
        }

        /// <summary>
        /// Midnight for everybody: wages out of each house's own safe, its own men
        /// aging, learning, souring and walking. Tribute is the player's alone - he is
        /// the one who pays the houses above him - and that is the whole of the
        /// difference (D20).
        /// </summary>
        /// <returns>What the PLAYER paid his men, for the line the ledger prints.</returns>
        public int DayTick()
        {
            var paidByPlayer = 0;
            for (var i = 0; i < houses.Length; i++)
            {
                var house = houses[i];
                if (house == null || house.Extinct)
                    continue;
                var paid = house.Runner.DayTick(house.Roster, payTribute: house.IsPlayer);
                house.Touch();
                if (house.IsPlayer)
                    paidByPlayer = paid;
            }
            return paidByPlayer;
        }
    }
}
