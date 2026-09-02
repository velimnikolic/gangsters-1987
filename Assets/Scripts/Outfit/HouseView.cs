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
            bool shut, bool trades, DoorTenure tenure, bool late = false)
        {
            Late = late;
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

        /// <summary>A week's money owed, or a week since anybody collected. A street
        /// with late doors is a street somebody has stopped walking.</summary>
        public bool Late { get; }

        public bool Unprotected => !Protector.IsValid;
    }

    /// <summary>
    /// Somebody put hands on this family, recently enough to still be about it: a shot,
    /// a killing, a wrecked front. What a mind is told is what the street saw - where,
    /// roughly when, whose men if anybody recognised them, and whether we have anyone
    /// near enough to answer.
    /// </summary>
    public readonly struct HouseThreat
    {
        public HouseThreat(TerritoryGangId by, TerritoryBlockId blockId, double at,
            bool inReach, bool atOurFront)
        {
            By = by;
            BlockId = blockId;
            At = at;
            InReach = inReach;
            AtOurFront = atOurFront;
        }

        /// <summary>Whose men, if the street knew them.</summary>
        public TerritoryGangId By { get; }

        public TerritoryBlockId BlockId { get; }
        public double At { get; }

        /// <summary>One of our crews is close enough to be sicced on them.</summary>
        public bool InReach { get; }

        /// <summary>They were at our own front door.</summary>
        public bool AtOurFront { get; }
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
        static readonly HouseThreat[] NoThreats = new HouseThreat[0];
        static readonly TerritoryGangId[] NoRivals = new TerritoryGangId[0];
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

        /// <summary>How far up the ladder we are with them - what WE are owed, never
        /// what they are.</summary>
        public System.Func<TerritoryGangId, LadderStep> LadderLook;

        /// <summary>How many days we believe THEY could pay their men through a war.
        /// Never their books: a haze between 0.7 and 1.3 of the truth (D15).</summary>
        public System.Func<TerritoryGangId, int> EnduranceLook;

        /// <summary>Every other family this one has anything to do with.</summary>
        public IReadOnlyList<TerritoryGangId> Rivals = NoRivals;

        /// <summary>Men we have lost since this war opened. Enough of them and a family
        /// sues for peace whatever it is owed (D15).</summary>
        public int LossesThisWar;

        public IReadOnlyList<HouseIncident> Incidents = NoIncidents;
        public IReadOnlyList<HouseThreat> Threats = NoThreats;
        public IReadOnlyList<HouseDefiance> Defiances = NoDefiances;

        /// <summary>Intents the gateway refused since the last think, in its own words.
        /// A mind that keeps proposing a refused thing is a mind with a bug, and this is
        /// how it finds out.</summary>
        public IReadOnlyList<string> LastRefusals = NoRefusals;

        public double GameHour;
        public int Day;

        /// <summary>How many thinks running have found nothing louder to do than spend
        /// money. A family buys cars when the street is quiet, not while it is being
        /// shot at (D22).</summary>
        public int QuietThinks;

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

        /// <summary>How many days WE could pay our men through a war (D15).</summary>
        public int Endurance => HouseRelations.Endurance(Safe, DailyPayroll);

        public LadderStep Ladder(TerritoryGangId other) =>
            LadderLook != null ? LadderLook(other) : LadderStep.Ignore;

        /// <summary>What we believe they could last. Never the truth.</summary>
        public int TheirEndurance(TerritoryGangId other) =>
            EnduranceLook != null ? EnduranceLook(other) : 0;
    }
}
