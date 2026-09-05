using LivingCity.Gangs;
using LivingCity.News;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>
    /// The city's families, dealt once from the city's own seed. One
    /// <see cref="House"/> apiece - the player's outfit is house 0 - and one shared
    /// classified column, because there is one newspaper in town.
    ///
    /// HOW MANY there are is the city's decision and not this table's: the catalogue
    /// holds twenty-one names so an id never moves, and a city deals as many of them as
    /// it can stand men for (<see cref="Dealt"/>). Nobody runs on paper.
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

        /// <summary>
        /// HOW MANY FAMILIES THIS CITY HOLDS - the player's house and the rivals dealt
        /// beside him, ids 0 upwards. The table above is always the full twenty-one long
        /// because an id must never move (a saved campaign, a stance and a map colour all
        /// hang off it); this figure is how many of those slots a given city actually
        /// deals, and every one of them stands men on the pavement.
        ///
        /// NO FAMILY RUNS ON PAPER (the user's rule of 2026-09-03). If the city cannot
        /// carry twenty-one houses it holds fewer houses, not twenty-one houses of which
        /// most are invisible: the street decides the number
        /// (RoadDemoBuilder.HousesInThisCity) and hands it here.
        /// </summary>
        public int Dealt { get; private set; }

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

        /// <summary>The public record shared by every house in the city.</summary>
        public PressBook Press { get; } = new PressBook();

        /// <summary>
        /// PABLO'S MAN (EPIC 40, ruling 6). Exactly one man in the city carries the
        /// Direct line. What the deal draws hidden is his TURN - which signing of a
        /// connection man, city-wide, is him - because ids are allocated when a man is
        /// made and his cannot exist at Deal. His id is bound at that signing; unsigned
        /// (his card expired, the house walked away) he moves on to the next signing,
        /// and not for thirty days.
        /// </summary>
        public int DirectTurn { get; set; } = 1;

        public int DirectManId { get; set; } = -1;
        public int DirectNotBeforeDay { get; set; }

        /// <summary>How many connection men have been signed city-wide.</summary>
        public int TheManSigned { get; set; }

        /// <summary>Whether the NEXT signing, on this day, is his.</summary>
        public bool NextSigningIsDirect(int day) =>
            DirectManId < 0 && TheManSigned + 1 == DirectTurn && day >= DirectNotBeforeDay;

        /// <summary>A connection man signed somewhere. Binds the id when the turn is
        /// his.</summary>
        public void ConnectionManSigned(int characterId, int day)
        {
            if (NextSigningIsDirect(day))
                DirectManId = characterId;
            TheManSigned++;
        }

        /// <summary>A man's card went unanswered or was walked away from while the
        /// next signing was Pablo's: he moves on, and not for thirty days.</summary>
        public void DirectDeclined(int day)
        {
            if (DirectManId >= 0 || TheManSigned + 1 != DirectTurn)
                return;
            DirectNotBeforeDay = day + Connection.BurnedDays;
        }

        /// <summary>WHAT EVERY HOUSE HAS ASKED EVERY OTHER (EPIC 42) - one book for the
        /// city, beside the book of standings, for the same reason.</summary>
        public HouseDiplomacy Diplomacy { get; } = new HouseDiplomacy();

        /// <summary>
        /// MONEY BETWEEN TWO HOUSES - the one door it crosses through (EPIC 42). Paid
        /// out of the payer's safe first, dirty-first like everything, and only then
        /// received by the payee, dirty: street money arriving is street money. Both
        /// sheets carry the line. Answers the payer's refusal, or null when it moved.
        /// </summary>
        public string Transfer(int from, int to, int amount)
        {
            var payer = Of(from);
            var payee = Of(to);
            if (payer == null || payee == null || from == to || amount <= 0)
                return "nothing to move";
            var refusal = BalanceMath.Pay(payer.Runner.Accounts, amount, out _);
            if (refusal != null)
                return refusal;
            BalanceMath.Receive(payee.Runner.Accounts, amount, MoneyKind.Dirty);
            var paid = payer.Runner.Accounts.Current;
            if (paid != null)
                paid.ToHouses += amount;
            var got = payee.Runner.Accounts.Current;
            if (got != null)
                got.FromHouses += amount;
            payer.Touch();
            payee.Touch();
            return null;
        }

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
        /// Deals the underworld: every house's roster off its own stream, its own safe
        /// with the same opening stake, and the same organization rules, bodyguard detail
        /// and arms deal the player's outfit gets. One pass, one rule, once per house
        /// this city holds.
        ///
        /// <paramref name="houses"/> is the player's house and the rivals beside him -
        /// ids 0 upwards, so the same seed always deals the same families and a smaller
        /// city is the same city with the far names left out. The slots above it stay
        /// empty and <see cref="Of"/> answers null for them, which every reader of a
        /// house already allows for.
        /// </summary>
        public static Underworld Deal(int citySeed, int houses = GangCatalog.GangCount)
        {
            var dealt = houses < 1 ? 1
                : houses > GangCatalog.GangCount ? GangCatalog.GangCount
                : houses;
            var underworld = new Underworld { CitySeed = citySeed, Dealt = dealt };
            // Which signing carries Pablo's man: drawn hidden, off the city's seed.
            underworld.DirectTurn = 1 + (int)((uint)Personnel.Potential.Mix(
                citySeed + 40_003, 7_2_1_9) % (uint)dealt);
            for (var gangId = 0; gangId < dealt; gangId++)
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
                    World = underworld,
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
        public static Underworld Ensure(int citySeed, int houses = GangCatalog.GangCount)
        {
            if (Current == null)
                Current = Deal(citySeed, houses);
            else if (Current.CitySeed != citySeed)
                Fault?.Invoke("[Underworld] already dealt from seed " + Current.CitySeed +
                              "; the call for seed " + citySeed + " was ignored.");
            else if (Current.Dealt != houses && houses != GangCatalog.GangCount)
                // The same fault as a second seed, and for the same reason: two halves of
                // one Play would be looking at two different cities. The default is not a
                // demand for twenty-one - it is the ledger and the bench asking for
                // whatever this city already dealt.
                Fault?.Invoke("[Underworld] already dealt " + Current.Dealt +
                              " houses; the call for " + houses + " was ignored.");
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
                if (house == null)
                    continue;
                // THE END IS NOTICED BEFORE THE DOOR SHUTS ON IT. A Don shot with
                // nobody of rank behind him makes his house Finished at once, and a
                // finished house is not worked - so the skip below used to run before
                // the runner could ever observe its own ending, and the player's
                // campaign latched nothing while the black leaf waited on a flag that
                // was never going to be set.
                house.Runner.NoticeTheEnd(house.Roster);
                if (house.Finished)
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
            if (house == null || house.Finished)
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
            // THE BASE OF THE ROTA IS READ ONCE, NOT PER STEP (AI-008 found it). The
            // cursor moves as houses think, and using it as the loop's own base moved
            // the ground under the walk: with maxPerCall above one, every house that
            // took a turn shifted the indexing for the rest of the pass and some
            // houses were never reached at all. The live runtime lets exactly one
            // house think per call and never saw it; the yardstick lets all of them,
            // and one family in a paper city simply stopped being asked from day four.
            var from = thinkCursor;
            for (var i = 0; i < houses.Length && thought < maxPerCall; i++)
            {
                var house = houses[(i + from) % houses.Length];
                if (house == null || house.IsPlayer || house.Finished)
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

        /// <summary>THE ENVOY ARRIVED (EPIC 42, DIPL-008): the proposal his job carried
        /// is delivered to the desk it was carried to.</summary>
        public void SitDown(Job job, int by, int day)
        {
            if (job == null || job.ProposalId <= 0)
                return;
            var proposal = Diplomacy.Find(job.ProposalId);
            if (proposal == null || proposal.From != by)
                return;
            HouseOps.Deliver(this, job.ProposalId, HouseOps.Look);
        }

        /// <summary>
        /// A MAN IN SOMEBODY'S CELLAR (RIVAL-009 step 6; EPIC 42 DIPL-005 brought it
        /// into the books from the scene edge). He is off his own family's books for
        /// KidnapDays, they are owed for it, and the price to have him back at once is
        /// on the table as a proposal - answered at the desk by his own house's rule,
        /// or waiting in the player's inbox as long as he is held.
        /// </summary>
        public void TakeHim(Job job, int by, int day)
        {
            if (job == null || job.TargetCharacterId < 0 || Of(by) == null)
                return;
            for (var g = 0; g < houses.Length; g++)
            {
                var house = houses[g];
                var man = house?.Roster?.Find(job.TargetCharacterId);
                if (man == null || man.Gone || man.Status != CharacterStatus.Active)
                    continue;

                RosterOps.Taken(house.Roster, man.Id, day + OrderResolution.KidnapDays,
                    "held by " + GangCatalog.Names[by]);
                house.Touch();
                if (house.GangId != by)
                    Relations.Note(house.GangId, by, GrievanceKind.ManTaken, day);

                var ransom = new Proposal { To = house.GangId, Kind = ProposalKind.Ransom };
                ransom.Terms.Money = EconomyPrices.KidnapCut;
                ransom.Terms.CharacterId = man.Id;
                ransom.Terms.Label = man.FullName;
                if (house.GangId != by)
                    HouseOps.Propose(this, Of(by), ransom, HouseOps.Look);
                return;
            }
        }

        readonly System.Collections.Generic.List<AgreementOutcome> outcomes =
            new System.Collections.Generic.List<AgreementOutcome>();

        readonly System.Collections.Generic.List<int> soured =
            new System.Collections.Generic.List<int>();

        readonly System.Collections.Generic.List<StanceLanded> landed =
            new System.Collections.Generic.List<StanceLanded>();

        /// <summary>
        /// THE ENVELOPES CROSS (EPIC 42, DIPL-004, ruling 2). Every house re-prices
        /// what it owes the houses above it against this morning's city, in gang-id
        /// order so two runs of one seed pay in one order, and every envelope that
        /// falls due goes through Transfer - out of the payer's safe and INTO the
        /// levying house's, which it never reached before. A house that cannot cover
        /// its envelope is owed for it by the house it stiffed (D14).
        /// </summary>
        void SettleTribute(int day)
        {
            for (var g = 0; g < houses.Length; g++)
            {
                var house = houses[g];
                if (house == null || house.Finished)
                    continue;
                house.Runner.AssessTribute(day);
                var payer = house.GangId;
                house.Runner.Tribute.Settle(
                    levy => Transfer(payer, levy.GangId, levy.Amount), day, soured);
                for (var i = 0; i < soured.Count; i++)
                    Relations.Note(soured[i], payer, GrievanceKind.TributeUnpaid, day);
                house.Touch();
            }
        }

        /// <summary>
        /// Midnight for everybody: wages out of each house's own safe, its own men
        /// aging, learning, souring and walking - and, since EPIC 42 (DIPL-004), every
        /// house's tribute, settled in one pass once the books have turned and
        /// crossing into the levying house's safe. D20's "the player's alone" is
        /// retired: a big house grows by what the small ones kick up.
        /// </summary>
        /// <returns>What the PLAYER paid his men, for the line the ledger prints.</returns>
        public int DayTick()
        {
            // MIDNIGHT FOR THE WHOLE CITY. Every pending stance lands at once and every
            // grudge fades by a day, before anybody's books are turned: a war declared
            // yesterday is a war this morning, for both sides at the same moment. An
            // agreement made at the table lands over the pending slot, or breaks; the
            // money it held follows it either way (EPIC 42).
            Relations.ApplyPending(outcomes, landed);
            Relations.DayTick(Player != null ? Player.Runner.Campaign.Day : 0);

            var paidByPlayer = 0;
            for (var i = 0; i < houses.Length; i++)
            {
                var house = houses[i];
                if (house == null)
                    continue;
                house.Runner.NoticeTheEnd(house.Roster);
                if (house.Finished)
                    continue;
                // TRIBUTE IS EVERY HOUSE'S NOW (EPIC 42, DIPL-004), settled below in
                // one pass once every book has turned - never inside one house's tick,
                // where a levy would be priced against a city half of which had not
                // yet woken.
                var paid = house.Runner.DayTick(house.Roster, payTribute: false);
                house.Touch();
                if (house.IsPlayer)
                    paidByPlayer = paid;
            }
            // THE TABLE'S OWN MIDNIGHT, once every book has turned - a runner's tick
            // clears its desk of the night's incidents, so anything printed before it
            // would be gone by morning. The escrow follows the agreements that landed
            // or broke above; the pacts honour against the wars that landed, for the
            // NEXT midnight (DIPL-007), and never cascade.
            var morning = Player != null ? Player.Runner.Campaign.Day : 0;
            Diplomacy.ReleaseEscrows(this, outcomes, morning);
            Diplomacy.HonourPacts(this, landed, morning, Relations.Config);
            SettleTribute(morning);

            // THE TABLE'S OWN MIDNIGHT (EPIC 42): a proposal nobody answered lapses on
            // its day, and a word given to keep off a street lifts on its day. After
            // the books have turned, so "day" is this morning's.
            Diplomacy.Expire(morning, Relations);
            return paidByPlayer;
        }
    }
}
