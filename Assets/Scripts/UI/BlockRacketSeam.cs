using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;
using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>What a door's row says about its standing with OUR house. Kind and
    /// Severity are what the page sorts and colours by; Line is the words, already
    /// composed by the simulation's own vocabulary - the page never composes them.</summary>
    public enum DoorStandingKind
    {
        Shut,
        Rival,
        Refused,
        Wavering,
        Late,
        Short,
        Paying,
        Unvisited,
        Other,
    }

    public readonly struct DoorStanding
    {
        public DoorStanding(TerritoryBusinessId businessId, DoorStandingKind kind,
            string line, int owed, int daysLate, int newsDay, string rivalName)
        {
            BusinessId = businessId;
            Kind = kind;
            Line = line ?? "";
            Owed = owed;
            DaysLate = daysLate;
            NewsDay = newsDay;
            RivalName = rivalName ?? "";
        }

        public TerritoryBusinessId BusinessId { get; }
        public DoorStandingKind Kind { get; }

        /// <summary>e.g. "refused us · 5 Jan", "owes $400 · 3 days late".</summary>
        public string Line { get; }

        /// <summary>Dollars owed to us right now, 0 if none.</summary>
        public int Owed { get; }

        /// <summary>0 unless Kind == Late.</summary>
        public int DaysLate { get; }

        /// <summary>Campaign day of the last news about this door, 0 = none.</summary>
        public int NewsDay { get; }

        /// <summary>For Kind == Rival.</summary>
        public string RivalName { get; }

        /// <summary>2 = red (Refused, Late), 1 = amber (Wavering, Short), 0 = the rest.</summary>
        public int Severity =>
            Kind == DoorStandingKind.Refused || Kind == DoorStandingKind.Late ? 2
            : Kind == DoorStandingKind.Wavering || Kind == DoorStandingKind.Short ? 1
            : 0;
    }

    /// <summary>The block card's racket figures, derived at read by the simulation.</summary>
    public readonly struct BlockRacketView
    {
        public BlockRacketView(bool hasResponsible, string responsibleName,
            int responsibleCrewId, CrewPolicy policy, int collectsWeekday,
            string collectsWord, int collectors, bool roundOut, int roundCursor,
            int roundStops, int roundCarried, string roundCollectorName,
            int owed, int inTheBag, int bankedThisWeek, int bankedAllGame,
            int lastRoundDay, int lastRoundBanked, int lastRoundShort,
            int doorsNeedingAnswer, int holdouts,
            int collectorId = -1, string collectorName = "",
            bool collectorNamedByBoss = false)
        {
            CollectorId = collectorId;
            CollectorName = collectorName ?? "";
            CollectorNamedByBoss = collectorNamedByBoss;
            HasResponsible = hasResponsible;
            ResponsibleName = responsibleName ?? "";
            ResponsibleCrewId = responsibleCrewId;
            Policy = policy;
            CollectsWeekday = collectsWeekday;
            CollectsWord = collectsWord ?? "";
            Collectors = collectors;
            RoundOut = roundOut;
            RoundCursor = roundCursor;
            RoundStops = roundStops;
            RoundCarried = roundCarried;
            RoundCollectorName = roundCollectorName ?? "";
            Owed = owed;
            InTheBag = inTheBag;
            BankedThisWeek = bankedThisWeek;
            BankedAllGame = bankedAllGame;
            LastRoundDay = lastRoundDay;
            LastRoundBanked = lastRoundBanked;
            LastRoundShort = lastRoundShort;
            DoorsNeedingAnswer = doorsNeedingAnswer;
            Holdouts = holdouts;
        }

        public bool HasResponsible { get; }
        public string ResponsibleName { get; }

        /// <summary>-1 when none.</summary>
        public int ResponsibleCrewId { get; }

        public CrewPolicy Policy { get; }

        /// <summary>0..6, -1 when no collector on that crew.</summary>
        public int CollectsWeekday { get; }

        /// <summary>"Thursdays" - composed by the simulation.</summary>
        public string CollectsWord { get; }

        /// <summary>Men on collector duty in the responsible crew.</summary>
        public int Collectors { get; }

        public bool RoundOut { get; }

        /// <summary>Doors done.</summary>
        public int RoundCursor { get; }

        /// <summary>Doors on the round.</summary>
        public int RoundStops { get; }

        /// <summary>Dollars in the bag.</summary>
        public int RoundCarried { get; }

        public string RoundCollectorName { get; }

        /// <summary>Sum owed by the block's doors.</summary>
        public int Owed { get; }

        /// <summary>Carried by a round out now (0 otherwise).</summary>
        public int InTheBag { get; }

        public int BankedThisWeek { get; }

        /// <summary>Every dollar our rounds have ever banked off this block, from the
        /// first day of the campaign. Never reset - the week's figure above is, and a
        /// boss reading only that cannot tell a block that has paid for itself twice
        /// over from one that has never paid at all.</summary>
        public int BankedAllGame { get; }

        /// <summary>0 = never.</summary>
        public int LastRoundDay { get; }

        public int LastRoundBanked { get; }
        public int LastRoundShort { get; }

        /// <summary>Severity &gt; 0 count.</summary>
        public int DoorsNeedingAnswer { get; }

        /// <summary>Refused + Wavering count (LEAN ON THE HOLDOUTS).</summary>
        public int Holdouts { get; }

        /// <summary>THE BAG MAN (GAN-262): the one hood of the responsible crew who
        /// carries its bag, -1 when nobody does.</summary>
        public int CollectorId { get; }

        public string CollectorName { get; }

        /// <summary>Whether the boss named him from the ledger, as against the
        /// lieutenant handing him the bag himself.</summary>
        public bool CollectorNamedByBoss { get; }
    }

    /// <summary>One hood of a crew, as the bag menu lists him: who he is, how good a
    /// bag man he would make, and where he stands today.</summary>
    public readonly struct CrewHandView
    {
        public CrewHandView(int id, string name, int fitnessHalfSteps, bool walksTheStreet,
            bool carries)
        {
            Id = id;
            Name = name ?? "";
            FitnessHalfSteps = fitnessHalfSteps;
            WalksTheStreet = walksTheStreet;
            Carries = carries;
        }

        public int Id { get; }
        public string Name { get; }

        /// <summary>CollectorChoice.Fitness: three trades summed, 6..30.</summary>
        public int FitnessHalfSteps { get; }

        /// <summary>One of the men in the crew's street line today.</summary>
        public bool WalksTheStreet { get; }

        /// <summary>He has the bag now.</summary>
        public bool Carries { get; }
    }

    public interface IBlockRacketSource
    {
        bool TryGetBlock(TerritoryBlockId blockId, out BlockRacketView view);
        void CollectDoorStandings(TerritoryBlockId blockId, List<DoorStanding> into);
        bool IsCollector(int characterId);

        /// <summary>Every hood of the crew, in the crew's own order, for the bag
        /// menu (GAN-262).</summary>
        void CollectCrewHoods(int crewId, List<CrewHandView> into);

        /// <summary>The collector node's escorts, in posting order.</summary>
        void EscortsOf(int crewId, List<CrewHandView> into);

        /// <summary>The block a man is walking a round on, or invalid. WHO STANDS HERE
        /// prints "on the round · &lt;block&gt;".</summary>
        bool TryGetRoundOf(int characterId, out TerritoryBlockId blockId);

        /// <summary>Why a key is disabled, or "" when it may fire. Keys: "shakedown",
        /// "round", "lean".</summary>
        string Refusal(string key, int crewId, TerritoryBlockId blockId);

        /// <summary>Moves whenever any of the above would change.</summary>
        int Version { get; }
    }

    public interface IBlockRacketActions
    {
        TerritoryCommandResult ShakeDown(int crewId, TerritoryBlockId blockId);
        TerritoryCommandResult SendRound(int crewId, TerritoryBlockId blockId);
        TerritoryCommandResult LeanOnHoldouts(int crewId, TerritoryBlockId blockId);

        /// <summary>"" on success, else the refusal.</summary>
        string SetPolicy(int crewId, CrewPolicy policy);

        /// <summary>"" on success, else the refusal. On: the man is named for his own
        /// crew's bag; off: the bag is taken off him and left with nobody.</summary>
        string SetCollector(int characterId, bool on);

        /// <summary>THE BOSS NAMES THE BAG MAN for a crew (GAN-262) - one of its own
        /// hoods. "" on success, else the refusal.</summary>
        string NameCollector(int crewId, int hoodId);

        /// <summary>The lieutenant hands the bag to one of his own, as well as his
        /// Organization lets him. "" on success, else the refusal.</summary>
        string LetLieutenantPick(int crewId);

        /// <summary>The bag comes off him and stays with nobody until the boss says
        /// otherwise. "" on success, else the refusal.</summary>
        string TakeOffTheBag(int hoodId);

        string PostEscort(int crewId, int hoodId);
        string PullEscort(int hoodId);
    }

    /// <summary>The one place the page reads from and acts through. The mechanics half
    /// (GAN-224) installs the real pair at territory init; until then the stub stands in
    /// so the page can be built and reviewed with every state on it.</summary>
    public static class BlockRacketSeam
    {
        public static IBlockRacketSource Source { get; set; }
        public static IBlockRacketActions Actions { get; set; }
        public static IBlockRacketSource SourceOrStub => Source ?? StubBlockRacket.Instance;
        public static IBlockRacketActions ActionsOrStub => Actions ?? StubBlockRacket.Instance;

        /// <summary>Whether the figures on the page are the stub's. The block card says
        /// so out loud rather than letting a reviewer read invented money as the city's.
        /// </summary>
        public static bool IsStub => Source == null;

        /// <summary>Play-stop with a real source installed would otherwise leave the
        /// next session reading a dead simulation's object.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            Source = null;
            Actions = null;
            StubBlockRacket.Instance.Reset();
        }
    }

    /// <summary>
    /// DETERMINISTIC FAKE MONEY, clearly labelled. Every figure is derived from the
    /// block id's own string (FNV-1a, never string.GetHashCode, which is not stable
    /// across runs), so a given block always shows the same rows and a reviewer can find
    /// every state without a simulation running.
    ///
    /// It exists to prove the PAGE - that each standing has a colour, each key has a
    /// state and each refusal has a line - and for the bench scenes that open the ledger
    /// with no city behind it. Nothing here is a rule; when the real source is installed
    /// this whole class stops being asked anything.
    /// </summary>
    public sealed class StubBlockRacket : IBlockRacketSource, IBlockRacketActions
    {
        public static readonly StubBlockRacket Instance = new StubBlockRacket();

        StubBlockRacket() { }

        readonly Dictionary<int, bool> collectors = new Dictionary<int, bool>();
        readonly HashSet<int> escorts = new HashSet<int>();
        readonly Dictionary<int, CrewPolicy> policies = new Dictionary<int, CrewPolicy>();
        int version;

        public int Version => version;

        public void Reset()
        {
            collectors.Clear();
            escorts.Clear();
            policies.Clear();
            namedByBoss.Clear();
            version = 0;
        }

        /// <summary>The one hash. FNV-1a over the id's own characters: stable across
        /// runs, across machines and across a domain reload, which string.GetHashCode
        /// is not.</summary>
        static uint Hash(string text)
        {
            unchecked
            {
                var hash = 2166136261u;
                for (var i = 0; i < (text?.Length ?? 0); i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
                return hash;
            }
        }

        static readonly string[] Weekdays =
        {
            "Sundays", "Mondays", "Tuesdays", "Wednesdays", "Thursdays", "Fridays",
            "Saturdays",
        };

        public bool TryGetBlock(TerritoryBlockId blockId, out BlockRacketView view)
        {
            view = default;
            if (!blockId.IsValid)
                return false;

            var hash = Hash(blockId.Value);
            // Three blocks in rotation, so one page of the ledger shows a round on its
            // way, a block with nobody on the bag, and a quiet one.
            var shape = (int)(hash % 3u);
            var crewId = (int)(hash % 4u);
            var owed = 120 + (int)(hash % 9u) * 60;

            var bagMan = BagManOf(crewId);
            view = shape switch
            {
                0 => new BlockRacketView(
                    true, "Dutch Kaminski", crewId, PolicyOf(crewId), 4, "Thursdays", 2,
                    true, 3, 7, 410, "Dutch Kaminski",
                    owed, 410, 1240, 18600,
                    5, 980, 1, 3, 2,
                    bagMan, NameOf(bagMan), bagMan >= 0 && namedByBoss.Contains(bagMan)),
                1 => new BlockRacketView(
                    true, "Sal Petrosino", crewId, PolicyOf(crewId), -1, "", 0,
                    false, 0, 0, 0, "",
                    owed, 0, 0, 0,
                    0, 0, 0, 4, 3,
                    bagMan, NameOf(bagMan), bagMan >= 0 && namedByBoss.Contains(bagMan)),
                _ => new BlockRacketView(
                    true, "Frank Bevilacqua", crewId, PolicyOf(crewId), 1, "Mondays", 1,
                    false, 0, 0, 0, "",
                    owed, 0, 860, 7430,
                    3, 720, 0, 1, 1,
                    bagMan, NameOf(bagMan), bagMan >= 0 && namedByBoss.Contains(bagMan)),
            };
            return true;
        }

        // The stub's crews: four invented hoods to a crew, ids crewId*10+1..4, so the
        // bag menu has rows to show and a reviewer can name one and see it stick.
        static readonly string[] StubHoods =
            { "Eddie Falco", "Tony Marsh", "Carmine Vella", "Lou Brandt" };

        static readonly int[] StubFitness = { 24, 18, 27, 12 };

        readonly HashSet<int> namedByBoss = new HashSet<int>();

        static string NameOf(int hoodId) =>
            hoodId >= 0 ? StubHoods[hoodId % StubHoods.Length] : "";

        int BagManOf(int crewId)
        {
            for (var h = 1; h <= StubHoods.Length; h++)
                if (IsCollector(crewId * 10 + h))
                    return crewId * 10 + h;
            return -1;
        }

        public void CollectCrewHoods(int crewId, List<CrewHandView> into)
        {
            into?.Clear();
            if (into == null || crewId < 0)
                return;
            for (var h = 1; h <= StubHoods.Length; h++)
            {
                var id = crewId * 10 + h;
                into.Add(new CrewHandView(id, StubHoods[h - 1], StubFitness[h - 1],
                    walksTheStreet: !IsCollector(id), carries: IsCollector(id)));
            }
        }

        public void EscortsOf(int crewId, List<CrewHandView> into)
        {
            into?.Clear();
            if (into == null)
                return;
            for (var h = 1; h <= StubHoods.Length; h++)
            {
                var id = crewId * 10 + h;
                if (escorts.Contains(id))
                    into.Add(new CrewHandView(id, StubHoods[h - 1], StubFitness[h - 1],
                        false, false));
            }
        }

        public string NameCollector(int crewId, int hoodId)
        {
            if (hoodId / 10 != crewId)
                return "he is not one of that lieutenant's men";
            for (var h = 1; h <= StubHoods.Length; h++)
                collectors[crewId * 10 + h] = false;
            collectors[hoodId] = true;
            namedByBoss.Add(hoodId);
            version++;
            Debug.Log("[BlockRacketSeam stub] " + NameOf(hoodId) + " named for crew " +
                      crewId + "'s bag");
            return "";
        }

        public string LetLieutenantPick(int crewId)
        {
            // The stub's lieutenant is a four-star organizer: the fittest man.
            var best = -1;
            for (var h = 1; h <= StubHoods.Length; h++)
                if (best < 0 || StubFitness[h - 1] > StubFitness[best - 1])
                    best = h;
            for (var h = 1; h <= StubHoods.Length; h++)
                collectors[crewId * 10 + h] = false;
            collectors[crewId * 10 + best] = true;
            namedByBoss.Remove(crewId * 10 + best);
            version++;
            Debug.Log("[BlockRacketSeam stub] the lieutenant handed crew " + crewId +
                      "'s bag to " + StubHoods[best - 1]);
            return "";
        }

        public string TakeOffTheBag(int hoodId)
        {
            collectors[hoodId] = false;
            namedByBoss.Remove(hoodId);
            version++;
            Debug.Log("[BlockRacketSeam stub] the bag is off " + NameOf(hoodId));
            return "";
        }

        public string PostEscort(int crewId, int hoodId)
        {
            if (BagManOf(crewId) < 0)
                return "nobody carries the bag for him to guard";
            if (hoodId / 10 != crewId)
                return "he is not one of that lieutenant's men";
            if (escorts.Contains(hoodId))
                return "he is already on the bag's detail";
            var posted = 0;
            for (var h = 1; h <= StubHoods.Length; h++)
                if (escorts.Contains(crewId * 10 + h))
                    posted++;
            if (posted >= Crew.MaxEscorts)
                return "his escort is full · " + Crew.MaxEscorts + " men";
            escorts.Add(hoodId);
            version++;
            return "";
        }

        public string PullEscort(int hoodId)
        {
            if (!escorts.Remove(hoodId))
                return "he is not on the bag's detail";
            version++;
            return "";
        }

        CrewPolicy PolicyOf(int crewId) =>
            policies.TryGetValue(crewId, out var policy) ? policy : CrewPolicy.Normal;

        /// <summary>The kinds in rotation, so a block with nine doors or more shows
        /// every one of them and a reviewer never has to hunt for a state.</summary>
        static readonly DoorStandingKind[] Rotation =
        {
            DoorStandingKind.Refused,
            DoorStandingKind.Late,
            DoorStandingKind.Wavering,
            DoorStandingKind.Short,
            DoorStandingKind.Paying,
            DoorStandingKind.Rival,
            DoorStandingKind.Unvisited,
            DoorStandingKind.Shut,
            DoorStandingKind.Other,
        };

        public void CollectDoorStandings(TerritoryBlockId blockId, List<DoorStanding> into)
        {
            // The stub knows nothing of the block's real doors. The page asks by
            // BusinessId and falls back to today's tenure line for a door with no entry,
            // so the stub answers per door instead - see StandingFor.
            into?.Clear();
        }

        /// <summary>The standing of ONE door, since the stub has no door list of its
        /// own. Keyed off the door's id so the same shop always reads the same way.
        /// </summary>
        public DoorStanding StandingFor(TerritoryBusinessId businessId, int index)
        {
            var kind = Rotation[index % Rotation.Length];
            var hash = Hash(businessId.Value);
            var owed = 40 + (int)(hash % 12u) * 40;
            var late = 1 + (int)(hash % 6u);
            var day = 1 + (int)(hash % 9u);
            var line = kind switch
            {
                DoorStandingKind.Refused => "refused us · day " + day,
                DoorStandingKind.Late =>
                    "owes $" + owed + " · " + late + (late == 1 ? " day late" : " days late"),
                DoorStandingKind.Wavering => "wavering · not visited since day " + day,
                DoorStandingKind.Short => "short last round · \"a bad week\"",
                DoorStandingKind.Paying => "pays us · $" + owed + " owed · collects Thu",
                DoorStandingKind.Rival => "Castellano holds it · their man comes Thu",
                DoorStandingKind.Unvisited => "nobody has been to see him",
                DoorStandingKind.Shut => "shut · reopens day " + (day + 4),
                _ => "on the books · nothing outstanding",
            };
            return new DoorStanding(
                businessId, kind, line,
                kind == DoorStandingKind.Late || kind == DoorStandingKind.Paying ? owed : 0,
                kind == DoorStandingKind.Late ? late : 0,
                day,
                kind == DoorStandingKind.Rival ? "Castellano" : "");
        }

        public bool IsCollector(int characterId) =>
            collectors.TryGetValue(characterId, out var on) && on;

        public bool TryGetRoundOf(int characterId, out TerritoryBlockId blockId)
        {
            blockId = default;
            return false;
        }

        public string Refusal(string key, int crewId, TerritoryBlockId blockId)
        {
            if (crewId < 0)
                return "nobody is picked to send";
            if (!TryGetBlock(blockId, out var view))
                return "this block is not on the geography";
            return key switch
            {
                "round" => view.CollectsWeekday < 0
                    ? "nobody on this block carries the bag"
                    : view.RoundOut ? "a round is already out" : "",
                "lean" => view.Holdouts > 0 ? "" : "nobody is holding out",
                _ => "",
            };
        }

        public TerritoryCommandResult ShakeDown(int crewId, TerritoryBlockId blockId) =>
            Did("shake down " + blockId.Value, "the crew is walking the block");

        public TerritoryCommandResult SendRound(int crewId, TerritoryBlockId blockId) =>
            Did("send the round on " + blockId.Value, "the round is out");

        public TerritoryCommandResult LeanOnHoldouts(int crewId, TerritoryBlockId blockId) =>
            Did("lean on the holdouts of " + blockId.Value, "the men are on their way");

        public string SetPolicy(int crewId, CrewPolicy policy)
        {
            policies[crewId] = policy;
            version++;
            Debug.Log("[BlockRacketSeam stub] policy " + policy + " for crew " + crewId);
            return "";
        }

        public string SetCollector(int characterId, bool on)
        {
            collectors[characterId] = on;
            version++;
            Debug.Log("[BlockRacketSeam stub] collector " + on + " for " + characterId);
            return "";
        }

        TerritoryCommandResult Did(string what, string reason)
        {
            version++;
            Debug.Log("[BlockRacketSeam stub] " + what);
            return new TerritoryCommandResult(
                version, TerritoryCommandStatus.Accepted, reason);
        }
    }
}
