using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Outfit
{
    /// <summary>One door as a house can see it from across the street.</summary>
    public readonly struct HouseDoor
    {
        public HouseDoor(TerritoryBusinessId businessId, int tier, int weeklyRate,
            TerritoryGangId protector, TerritoryProtectionState standing, int owed,
            bool shut, bool trades, DoorTenure tenure)
        {
            BusinessId = businessId;
            Tier = tier;
            WeeklyRate = weeklyRate;
            Protector = protector;
            Standing = standing;
            Owed = owed;
            Shut = shut;
            Trades = trades;
            Tenure = tenure;
        }

        public TerritoryBusinessId BusinessId { get; }
        public int Tier { get; }

        /// <summary>What a week of protection is worth here - the figure the racket
        /// would charge, whoever charged it.</summary>
        public int WeeklyRate { get; }

        /// <summary>The house this door pays, if any. Never a roster, never a safe:
        /// which family holds a door is public knowledge on the street.</summary>
        public TerritoryGangId Protector { get; }

        /// <summary>Where the door stands with US.</summary>
        public TerritoryProtectionState Standing { get; }

        /// <summary>What it owes US right now.</summary>
        public int Owed { get; }

        public bool Shut { get; }

        /// <summary>False for a flat: a door with no till behind it.</summary>
        public bool Trades { get; }

        /// <summary>Whose paper the premises itself is on.</summary>
        public DoorTenure Tenure { get; }

        public bool Unprotected => !Protector.IsValid;
    }

    /// <summary>Trouble on ground we hold that nobody has answered for.</summary>
    public readonly struct HouseIncident
    {
        public HouseIncident(TerritoryBlockId blockId, int unanswered, double since)
        {
            BlockId = blockId;
            Unanswered = unanswered;
            Since = since;
        }

        public TerritoryBlockId BlockId { get; }
        public int Unanswered { get; }
        public double Since { get; }
    }

    /// <summary>
    /// A door that has told us no and does not pay us. It stays on this list after the
    /// threat that moves it off Defiant - a man who has refused once is not asked the
    /// same question again, he is worked up the ladder or left alone.
    /// </summary>
    public readonly struct HouseDefiance
    {
        public HouseDefiance(TerritoryBusinessId businessId, TerritoryBlockId blockId,
            double openedAt, int threats)
        {
            BusinessId = businessId;
            BlockId = blockId;
            OpenedAt = openedAt;
            Threats = threats;
        }

        public TerritoryBusinessId BusinessId { get; }
        public TerritoryBlockId BlockId { get; }
        public double OpenedAt { get; }

        /// <summary>How many times we have leant on him since. Our own record of our own
        /// visits - the ladder stops rather than knocking for ever.</summary>
        public int Threats { get; }
    }

    /// <summary>
    /// THE WALL A MIND LOOKS THROUGH.
    ///
    /// Everything <see cref="HouseMind"/> is allowed to know, and nothing else. It has
    /// its own books - roster, safe, order book - and then only what anybody standing on
    /// that street could work out: who holds a door, what a week there is worth, how much
    /// ground the family holds, how frightened the block is, how much law is on it.
    ///
    /// NOT here, and never to be added: another house's roster, safe or order book; a
    /// shopkeeper's personality; any roll. A mind that could read those would be playing
    /// a different game from the player.
    ///
    /// The runtime fills it from the real ledgers; a test fills it from a bench.
    /// </summary>
    public sealed class HouseView
    {
        static readonly TerritoryBlockId[] NoBlocks = new TerritoryBlockId[0];
        static readonly HouseDoor[] NoDoors = new HouseDoor[0];
        static readonly HouseIncident[] NoIncidents = new HouseIncident[0];
        static readonly HouseDefiance[] NoDefiances = new HouseDefiance[0];
        static readonly string[] NoRefusals = new string[0];

        public TerritoryGangId House;
        public Roster Roster;
        public Accounts Accounts;
        public OrderBook Book;

        /// <summary>The family's own premises, and the block it stands on.</summary>
        public TerritoryBusinessId Front;
        public TerritoryBlockId FrontBlock;

        /// <summary>Every block the family can see - the ones it stands on and the ones
        /// next to them.</summary>
        public IReadOnlyList<TerritoryBlockId> Blocks = NoBlocks;

        public System.Func<TerritoryBlockId, IReadOnlyList<TerritoryBlockId>> NeighbourLook;
        public System.Func<TerritoryBlockId, IReadOnlyList<HouseDoor>> DoorLook;
        public System.Func<TerritoryBlockId, float> PresenceLook;
        public System.Func<TerritoryBlockId, float> FearLook;
        public System.Func<TerritoryBlockId, float> AttentionLook;
        public System.Func<TerritoryBlockId, TerritoryControlState> ControlLook;
        public System.Func<TerritoryBlockId, TerritoryGangId> LeaderLook;
        public System.Func<TerritoryGangId, Stance> StanceLook;

        public IReadOnlyList<HouseIncident> Incidents = NoIncidents;
        public IReadOnlyList<HouseDefiance> Defiances = NoDefiances;

        /// <summary>Intents the gateway refused since the last think, in its own words.
        /// A mind that keeps proposing a refused thing is a mind with a bug, and this is
        /// how it finds out.</summary>
        public IReadOnlyList<string> LastRefusals = NoRefusals;

        public double GameHour;
        public int Day;

        public IReadOnlyList<TerritoryBlockId> Neighbours(TerritoryBlockId blockId) =>
            NeighbourLook != null ? NeighbourLook(blockId) ?? NoBlocks : NoBlocks;

        public IReadOnlyList<HouseDoor> Businesses(TerritoryBlockId blockId) =>
            DoorLook != null ? DoorLook(blockId) ?? NoDoors : NoDoors;

        public float OurPresence(TerritoryBlockId blockId) =>
            PresenceLook != null ? PresenceLook(blockId) : 0f;

        public float OurFear(TerritoryBlockId blockId) =>
            FearLook != null ? FearLook(blockId) : 0f;

        public float PoliceAttention(TerritoryBlockId blockId) =>
            AttentionLook != null ? AttentionLook(blockId) : 0f;

        public TerritoryControlState ControlState(TerritoryBlockId blockId) =>
            ControlLook != null ? ControlLook(blockId) : TerritoryControlState.Unknown;

        public TerritoryGangId Leader(TerritoryBlockId blockId) =>
            LeaderLook != null ? LeaderLook(blockId) : default;

        public Stance StanceToward(TerritoryGangId other) =>
            StanceLook != null ? StanceLook(other) : Outfit.Stance.Peace;

        /// <summary>What the men cost every day. The one figure the reserve rule and
        /// every purchase in the mind are weighed against.</summary>
        public int DailyPayroll => Wages.DailyPayroll(Roster);

        public int Safe => Accounts != null ? Accounts.Safe : 0;
    }
}
