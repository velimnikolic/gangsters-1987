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

        /// <summary>
        /// WHERE EVERY HOUSE STANDS WITH EVERY OTHER. One book for the city, not one per
        /// family: a stance belongs to the pair, and two families cannot disagree about
        /// whether they are at war.
        /// </summary>
        public HouseRelations Relations { get; } = new HouseRelations();

        public int Count => houses.Length;

        /// <summary>
        /// One number that moves whenever ANY house's men or money do - the dirty key
        /// the street re-deals on, so a man recruited by the Falcones and a man shot
        /// dead in our own crew both reach the pavement the same way.
        /// </summary>
        public int Version
        {
            get
            {
                var moved = 0;
                for (var i = 0; i < houses.Length; i++)
                    if (houses[i] != null)
                        moved += houses[i].Version;
                return moved;
            }
        }

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
                ArmTheFamily(roster);
                RosterOps.NormalizeArms(roster);

                // The runner is told WHOSE books it keeps. Twenty-one of these tick
                // every midnight and anything that asks "which family am I?" - where a
                // defector's door opens, most of all - reads it from here rather than
                // assuming house zero. The city's one book of standings is hung on it
                // in the same breath: a stance belongs to the pair, so it is lent to
                // every house and owned by none of them.
                var runner = new CampaignRunner
                {
                    Seed = citySeed,
                    GangId = gangId,
                    Relations = underworld.Relations,
                };
                runner.OpenFirstSheet();
                underworld.houses[gangId] = new House(gangId, roster, runner);
            }
            return underworld;
        }

        /// <summary>
        /// What a family already has in its hands on day one. The mobs have carried
        /// these three guns since the street first stood them up - the .38 every man
        /// owns, a shotgun, a machine pistol, one to a crew and rotating by family - and
        /// the guns were the STREET's, picked where the bodies were spawned. They are on
        /// the family's own books now, so a rival's iron is a line in a ledger like the
        /// player's: the quartermaster's deal puts it in the best hand, a dead man's
        /// piece goes back to the safe, and a crew wiped out leaves its guns behind.
        ///
        /// The .38 is not stock anywhere (ArmoryCatalog: the counter sells what is
        /// BETTER than the gun in his coat), so a crew on the first rotation carries
        /// nothing on paper and the street arms it from the default sidearm exactly as
        /// it always did.
        /// </summary>
        static void ArmTheFamily(Roster roster)
        {
            if (roster == null || roster.GangId == GangCatalog.PlayerGangId)
                return;

            var crewIndex = 0;
            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                var lieutenant = roster.Find(crew.LieutenantId);
                if (lieutenant == null || lieutenant.Rank != Rank.Lieutenant)
                    continue;

                var gun = MobArms[(roster.GangId + crewIndex) % MobArms.Length];
                crewIndex++;
                if (string.IsNullOrEmpty(gun))
                    continue;

                var listing = Armory(gun);
                if (listing.DisplayName != gun)
                    continue;

                // One apiece, on the capo's own deck - his to hand out, which is what
                // NormalizeArms then does by who can shoot.
                for (var man = 0; man <= crew.HoodIds.Count; man++)
                {
                    var item = RosterOps.AddEquipment(
                        roster, listing.Kind, listing.DisplayName, listing.Price);
                    if (item != null)
                        RosterOps.GiveEquipment(roster, item.Id, crew.LieutenantId);
                }
            }
        }

        /// <summary>The three the street has always dealt a mob, in the order it dealt
        /// them. An empty name is the .38 in every man's own coat.</summary>
        static readonly string[] MobArms = { "", "Machine Pistol", "Shotgun" };

        static ArmoryItem Armory(string displayName)
        {
            for (var i = 0; i < ArmoryCatalog.Weapons.Length; i++)
                if (ArmoryCatalog.Weapons[i].DisplayName == displayName)
                    return ArmoryCatalog.Weapons[i];
            return default;
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
        /// An order into the book of the house that filed it. The one door every
        /// family's orders go through, the player's included - the ledger names his
        /// house and a mind names its own.
        /// </summary>
        public OpResult Issue(Job job)
        {
            var house = job != null ? Of(job.GangId) : null;
            if (house == null || house.Extinct)
                return OpResult.Fail(UI.LedgerText.ReasonNoSuchCrew);
            return house.Runner.Issue(house.Roster, job);
        }

        /// <summary>
        /// EVERY FAMILY'S TURN OF MIND (D7).
        ///
        /// A house thinks when its own four hours are up, staggered by its number so the
        /// twenty-one never land on one frame. <paramref name="think"/> is handed the
        /// house and does the reading and the doing - the model owns the cadence, the
        /// scene owns the ledgers.
        ///
        /// THE PLAYER'S HOUSE HAS NO MIND. He is the mind.
        /// </summary>
        /// <returns>How many houses thought this call.</returns>
        /// <param name="maxPerCall">How many houses may think in ONE call. One, so a
        /// single frame never carries twenty turns of mind; the rota is round-robin, so
        /// nobody starves behind a busy neighbour (RIVAL-008).</param>
        public int Think(double gameHour, float everyHours, System.Action<House> think,
            int maxPerCall = 1)
        {
            if (think == null || everyHours <= 0f || maxPerCall < 1)
                return 0;

            var thought = 0;
            for (var i = 0; i < houses.Length && thought < maxPerCall; i++)
            {
                var house = houses[(i + thinkCursor) % houses.Length];
                if (house == null || house.IsPlayer || house.Extinct)
                    continue;
                if (house.NextThinkHour <= 0.0)
                    house.OpenTheRota(gameHour, everyHours, houses.Length);
                if (gameHour < house.NextThinkHour)
                    continue;

                house.NextThinkHour = gameHour + everyHours;
                think(house);
                thought++;
                thinkCursor = (house.GangId + 1) % houses.Length;
            }
            return thought;
        }

        /// <summary>Where the rota is. Kept so one busy family cannot soak up every
        /// turn the city ever takes.</summary>
        int thinkCursor;

        /// <summary>
        /// Midnight for everybody: wages out of each house's own safe, its own men
        /// aging, learning, souring and walking. Tribute is the player's alone - he is
        /// the one who pays the houses above him - and that is the whole of the
        /// difference (D20).
        /// </summary>
        /// <returns>What the PLAYER paid his men, for the line the ledger prints.</returns>
        public int DayTick()
        {
            // MIDNIGHT FOR THE WHOLE CITY. Every pending stance lands at once and every
            // grudge fades by a day, before anybody's books are turned: a war declared
            // yesterday is a war this morning, for both sides at the same moment.
            Relations.ApplyPending();
            Relations.DayTick(Player != null ? Player.Runner.Campaign.Day : 0);

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
