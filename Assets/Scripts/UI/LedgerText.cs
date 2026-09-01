using System.Globalization;
using LivingCity.Personnel;

namespace LivingCity.UI
{
    /// <summary>
    /// Every string the personnel ledger shows - the intention-helper discipline
    /// (PoliceIntention et al.): pure, free of UnityEngine.Object, so the headless suite
    /// asserts exhaustiveness and column budgets here instead of the player finding an
    /// empty cell on screen. RosterOps' refusal reasons live here too, so a failed
    /// operation explains itself in the ledger's own voice.
    ///
    /// No star or half glyphs anywhere: neither U+2605 nor U+00BD is trusted to be in the
    /// LiberationSans atlas, so stars are drawn as Images and numbers read "3.5".
    /// </summary>
    public static class LedgerText
    {
        // ---------------------------------------------------------------- labels

        public static string AttributeLabel(CharacterAttribute attribute) => attribute switch
        {
            CharacterAttribute.Combat => "Combat",
            CharacterAttribute.Awareness => "Awareness",
            CharacterAttribute.Stealth => "Stealth",
            CharacterAttribute.Driving => "Driving",
            CharacterAttribute.Streetwise => "Streetwise",
            CharacterAttribute.Leadership => "Leadership",
            CharacterAttribute.Organization => "Organization",
            // "Street Authority" is sixteen characters and the label cell takes
            // thirteen; the card has room for the word that carries the meaning.
            CharacterAttribute.StreetAuthority => "Authority",
            CharacterAttribute.Persuasion => "Persuasion",
            CharacterAttribute.Intimidation => "Intimidation",
            CharacterAttribute.Connections => "Connections",
            _ => "",
        };

        /// <summary>The hover note over a card's attribute row - what the stat is FOR,
        /// in the order table's own terms, so the tooltip teaches the system the number
        /// feeds instead of restating the label.</summary>
        public static string AttributeNote(CharacterAttribute attribute) => attribute switch
        {
            CharacterAttribute.Combat =>
                "Every violent trade: raids, kills, assaults, smash-ups, kidnappings, " +
                "torch and bomb work, patrols and guard duty. The best draw the guns " +
                "when gear is dealt.",
            CharacterAttribute.Awareness =>
                "What he notices: collecting protection, audits, recruiting, bribes " +
                "and police contacts.",
            CharacterAttribute.Stealth =>
                "Moving unseen - exploring and scouting another outfit's turf, and " +
                "leaving a job quietly.",
            CharacterAttribute.Driving =>
                "Behind the wheel. The best drivers draw the cars when gear is dealt.",
            CharacterAttribute.Streetwise =>
                "Buying premises, setting up and running businesses, and knowing " +
                "where a donation does the most good.",
            CharacterAttribute.Leadership =>
                "Command of men: how many will follow him, and how well they hold " +
                "when it goes wrong.",
            CharacterAttribute.Organization =>
                "How much of the armory a lieutenant actually gets into his crew's " +
                "hands. A lieutenancy leans on it.",
            CharacterAttribute.StreetAuthority =>
                "What the street concedes him before he speaks - the standing a made " +
                "name carries into a room.",
            CharacterAttribute.Persuasion =>
                "Talking a man round: the deal that is taken rather than forced.",
            CharacterAttribute.Intimidation =>
                "The lean: extortion, threats and raising protection rates.",
            CharacterAttribute.Connections =>
                "Who he knows outside the outfit - police, lawyers, judges and " +
                "whoever owes him a call.",
            _ => "",
        };

        /// <summary>
        /// The two or three words a wire slip is headed with - what KIND of thing came
        /// in, before the sentence that says what happened. The sentence itself is
        /// IncidentText's and is never re-worded here; this is only the label over it,
        /// and it lives beside every other label the book prints so a re-wording cannot
        /// make two pages disagree.
        /// </summary>
        public static string IncidentLabel(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Froze: return "Froze";
                case IncidentKind.Fled: return "Ran";
                case IncidentKind.Escalated: return "Gunfire";
                case IncidentKind.Deviated: return "Off the order";
                case IncidentKind.TookRivalMoney: return "Bought";
                case IncidentKind.DemandedARaise: return "Wants more";
                case IncidentKind.CaughtSkimming: return "Skimming";
                case IncidentKind.SlowingDown: return "Slowing";
                case IncidentKind.DiedOnTheDetail: return "Man down";
                case IncidentKind.StoppedIt: return "Took it";
                case IncidentKind.BearsWatching: return "Watch him";
                case IncidentKind.Defected: return "Gone over";
                case IncidentKind.Promoted: return "Made";
                default: return "Wire";
            }
        }

        public static string RankLabel(Rank rank) => rank switch
        {
            Rank.Hood => "Hood",
            Rank.Lieutenant => "Lieutenant",
            Rank.Boss => "Boss",
            _ => "",
        };

        public static string SpecialtyLabel(Specialty specialty) => specialty switch
        {
            Specialty.None => "",
            Specialty.Accountant => "Accountant",
            Specialty.Lawyer => "Lawyer",
            _ => "",
        };

        public static string StatusLabel(CharacterStatus status) => status switch
        {
            CharacterStatus.Active => "Active",
            CharacterStatus.Jailed => "Jailed",
            CharacterStatus.Hospitalized => "Hospitalized",
            CharacterStatus.Dead => "Dead",
            CharacterStatus.Deserted => "Deserted",
            _ => "",
        };

        public static string EquipmentLabel(EquipmentKind kind) => kind switch
        {
            EquipmentKind.Pistol => "Pistol",
            EquipmentKind.Vehicle => "Vehicle",
            EquipmentKind.Shotgun => "Shotgun",
            EquipmentKind.Rifle => "Rifle",
            EquipmentKind.TommyGun => "Tommy Gun",
            EquipmentKind.TwinPistols => "Twin Pistols",
            EquipmentKind.MachinePistol => "Machine Pistol",
            EquipmentKind.Motorcycle => "Motorcycle",
            EquipmentKind.Grenade => "Grenade",
            _ => "",
        };

        /// <summary>How one piece of gear reads on a line: its kind, then the name the
        /// counter sold it under. The catalogue names a rifle "Rifle", so the two words
        /// are one word - print it once rather than "Rifle  ·  Rifle".</summary>
        public static string EquipmentLine(EquipmentKind kind, string displayName)
        {
            var label = EquipmentLabel(kind);
            return string.IsNullOrEmpty(displayName) || displayName == label
                ? label
                : label + "  ·  " + displayName;
        }

        /// <summary>
        /// The balance sheet's exact figure: "$1,247", "-$300", "$0". Deliberately NOT
        /// BusinessIntention.Money, which abbreviates to "$1.2k" for 280px popups - an
        /// accounting column that rounds is worse than no column.
        /// </summary>
        public static string Cash(int amount)
        {
            var magnitude = amount < 0 ? -amount : amount;
            var figure = "$" + magnitude.ToString("N0", CultureInfo.InvariantCulture);
            return amount < 0 ? "-" + figure : figure;
        }

        public static string RiskLabel(Outfit.RiskRating risk) => risk switch
        {
            Outfit.RiskRating.None => "None",
            Outfit.RiskRating.Low => "Low",
            Outfit.RiskRating.Moderate => "Moderate",
            Outfit.RiskRating.High => "High",
            _ => "",
        };

        public static string InsufficientFunds(int price, int safe) =>
            "The safe holds " + Cash(safe) + "; that costs " + Cash(price) + ".";

        /// <summary>"3" or "3.5" - halves only when earned, invariant culture so the
        /// ledger reads the same whatever the machine's locale.</summary>
        public static string Stars(int halfSteps)
        {
            var whole = halfSteps / 2;
            return (halfSteps & 1) == 0
                ? whole.ToString(CultureInfo.InvariantCulture)
                : whole.ToString(CultureInfo.InvariantCulture) + ".5";
        }

        // ----------------------------------------------------------- composed lines

        public static string CrewName(string lieutenantSurname) =>
            "CREW OF " + lieutenantSurname.ToUpperInvariant();

        public static string AssignmentLine(Assignment assignment, string crewName) =>
            assignment.Kind switch
            {
                AssignmentKind.Crew => crewName,
                AssignmentKind.Front => "Runs the front",
                AssignmentKind.Specialist => "On retainer",
                AssignmentKind.Boss => "Runs the outfit",
                _ => "Unassigned",
            };

        public static string HeldByLine(string holderName) => "held by " + holderName;

        public static string MemberCount(int count) =>
            count == 1 ? "1 MAN ON THE BOOKS" : count + " MEN ON THE BOOKS";

        // ------------------------------------------------------- warnings and confirms

        public static string PromoteWarning(string name) =>
            name + " is short on brains or order for a lieutenant's job. Promote anyway?";

        public static string TommyGunWarning(string name) =>
            name + " is a poor shot - in his hands the tommy gun sprays the street.";

        // ---------------------------------------------------------------- diplomacy

        public static string StanceLabel(Outfit.Stance stance) => stance switch
        {
            Outfit.Stance.Peace => "Peace",
            Outfit.Stance.Truce => "Truce",
            Outfit.Stance.War => "War",
            _ => "",
        };

        /// <summary>The system most likely to kill a crew unexplained - so the page
        /// says outright what each stance does, in one sentence each.</summary>
        public static string StanceEffect(Outfit.Stance stance) => stance switch
        {
            Outfit.Stance.Peace =>
                "PEACE — no engagement. Your men and theirs pass in the street, " +
                "claimed ground or not.",
            Outfit.Stance.Truce =>
                "TRUCE — territorial. Their men engage yours caught inside THEIR " +
                "territory, and yours engage theirs on YOURS. Neutral ground stays quiet.",
            Outfit.Stance.War =>
                "WAR — on sight. Their men engage yours anywhere in the city, and " +
                "yours theirs.",
            _ => "",
        };

        public const string StanceTakesEffect =
            "Stance changes take effect when the week is committed - never mid-plan.";

        public const string StrengthUnknown = "Unknown — no eyes inside";

        public const string ReasonOwnOutfit =
            "You cannot take a stance toward your own outfit.";

        // -------------------------------------------------------------------- orders

        public static string OrderLabel(Outfit.OrderType type) => type switch
        {
            Outfit.OrderType.Extort => "Extort",
            Outfit.OrderType.Intimidate => "Intimidate",
            Outfit.OrderType.CollectProtection => "Collect Protection",
            Outfit.OrderType.AdjustProtection => "Adjust Protection",
            Outfit.OrderType.Assault => "Assault",
            Outfit.OrderType.SmashUp => "Smash Up",
            Outfit.OrderType.Raid => "Raid",
            Outfit.OrderType.Torch => "Torch",
            Outfit.OrderType.Bomb => "Bomb",
            Outfit.OrderType.Kill => "Kill",
            Outfit.OrderType.Kidnap => "Kidnap",
            Outfit.OrderType.Patrol => "Patrol",
            Outfit.OrderType.Guard => "Guard",
            Outfit.OrderType.Ambush => "Ambush",
            Outfit.OrderType.Explore => "Explore",
            Outfit.OrderType.BuyPremises => "Buy Premises",
            Outfit.OrderType.SetUpBusiness => "Set Up Business",
            Outfit.OrderType.RunBusiness => "Run Business",
            Outfit.OrderType.Audit => "Audit",
            Outfit.OrderType.Recruit => "Recruit",
            Outfit.OrderType.Bribe => "Bribe",
            Outfit.OrderType.EmployPolice => "Employ Police",
            Outfit.OrderType.Donate => "Donate",
            _ => "",
        };

        public static string CategoryLabel(Outfit.OrderCategory category) => category switch
        {
            Outfit.OrderCategory.Extortion => "Extortion & Territory",
            Outfit.OrderCategory.Violence => "Violence",
            Outfit.OrderCategory.Defense => "Defense & Recon",
            Outfit.OrderCategory.Business => "Business",
            Outfit.OrderCategory.Influence => "Personnel & Influence",
            _ => "",
        };

        public static string TargetModeHint(Outfit.TargetMode mode) =>
            mode == Outfit.TargetMode.Area
                ? "Drag a box on the map - every eligible block inside becomes a target."
                : "Click one building or one man on the map.";

        public static string RequirementLine(CharacterAttribute attribute, int floorHalfSteps) =>
            floorHalfSteps <= 0
                ? "No particular talent required."
                : "Wants " + AttributeLabel(attribute) + " " + Stars(floorHalfSteps) + "+.";

        public static string OutcomeLabel(Outfit.OrderOutcome outcome) => outcome switch
        {
            Outfit.OrderOutcome.Completed => "Completed",
            Outfit.OrderOutcome.Failed => "Failed",
            Outfit.OrderOutcome.CalledOff => "Called off",
            _ => "",
        };

        /// <summary>What a crew is doing right now, in the words a lieutenant would
        /// use. The hours are rounded to whole ones on purpose - nobody reports back
        /// in decimals.</summary>
        public static string StageLine(Outfit.Job job)
        {
            if (job == null)
                return "";
            return job.Stage switch
            {
                Outfit.JobStage.Queued => "waiting their turn",
                Outfit.JobStage.Travelling => "on their way - " + Hours(job.TravelHoursLeft),
                Outfit.JobStage.Working =>
                    Outfit.OrderTable.SpecOf(job.Type).Resolution == Outfit.JobResolution.Standing
                        ? "standing it - day " + (job.DaysStood + 1)
                        : "at it - " + Hours(job.WorkHoursLeft) + " left",
                _ => "done",
            };
        }

        public static string Hours(float hours)
        {
            var whole = hours < 1f ? 1 : (int)(hours + 0.5f);
            return whole + (whole == 1 ? " hour" : " hours");
        }

        /// <summary>
        /// How long until a campaign day comes round, in the only two units the ledger
        /// owns. There are no weeks anywhere in this book: a thing eleven days out says
        /// eleven days, and a thing due before this midnight counts down in HOURS,
        /// because that is the horizon a man on a real-time clock actually plans on.
        /// </summary>
        /// <param name="dueDay">The campaign day it falls due.</param>
        /// <param name="today">The campaign day now.</param>
        /// <param name="hourNow">The city clock's hour, 0-24, for the last day's count.</param>
        public static string DueIn(int dueDay, int today, float hourNow)
        {
            var days = dueDay - today;
            if (days < 0)
                return "OVERDUE";

            // Inside the last day the answer is hours - and the hours LEFT of today,
            // not the hours elapsed, which is the subtraction that reads backwards.
            if (days == 0)
            {
                var left = Outfit.Campaign.HoursPerDay - hourNow;
                if (left <= 1f)
                    return "WITHIN THE HOUR";
                return "IN " + (int)left + " HOURS";
            }

            if (days == 1)
            {
                // Tomorrow, but how far into tomorrow depends on where today stands.
                var left = Outfit.Campaign.HoursPerDay - hourNow + Outfit.Campaign.HoursPerDay;
                return left < 30f ? "IN " + (int)left + " HOURS" : "TOMORROW";
            }

            return "IN " + days + " DAYS";
        }

        /// <summary>The same countdown in the lower-case voice the blotter's sub-notes
        /// and the printout's foot are typed in.</summary>
        public static string DueInPlain(int dueDay, int today, float hourNow) =>
            DueIn(dueDay, today, hourNow).ToLowerInvariant();

        /// <summary>How long a stretch of days lasts, said as a stretch rather than a
        /// date - what a CONDITION note under a hurt or a held man reads.</summary>
        public static string DaysLeft(int backOnDay, int today)
        {
            var days = backOnDay - today;
            if (backOnDay <= 0)
                return "no date set";
            if (days <= 0)
                return "back today";
            return days == 1 ? "1 day" : days + " days";
        }

        /// <summary>The odds the job card quotes. Words rather than a percentage: the
        /// ledger is a typed memo, and "about three in four" is what a man writes.</summary>
        public static string OddsLine(float chance) =>
            chance >= 0.85f ? "near certain"
            : chance >= 0.7f ? "about three in four"
            : chance >= 0.55f ? "rather better than even"
            : chance >= 0.45f ? "about even"
            : chance >= 0.3f ? "about one in three"
            : chance >= 0.15f ? "about one in five"
            : "a long shot";

        public static string MenOutLine(int menOut, int available) =>
            menOut + " of " + available + " men out";

        public const string ReasonNoTargets = "No targets picked.";
        public const string ReasonNoSuchOrder = "No such order in the queue.";
        public const string ReasonNoCrewSelected = "Pick a lieutenant first.";
        public const string ReasonJobUnderway = "They are already out on it.";

        public static string DemoteConfirm(string name, int hoodCount) => hoodCount switch
        {
            0 => "Disband " + name + "'s crew? It has no men.",
            1 => "Disband " + name + "'s crew? One man returns to the pool.",
            _ => "Disband " + name + "'s crew? " + hoodCount + " men return to the pool.",
        };

        // -------------------------------------------------------------- op refusals

        public const string ReasonNoSuchMember = "No such man on the books.";
        public const string ReasonNoDemand = "He has not asked for anything.";

        /// <summary>Why a man cannot be put under this lieutenant: he already holds
        /// everybody he can hold. Names him and the count, because the answer to it is
        /// either a better lieutenant or another one.</summary>
        public static string CrewFull(string lieutenant, int men) =>
            lieutenant + " already holds " + men +
            (men == 1 ? " man" : " men") + " - as many as he can lead.";

        /// <summary>Why nobody else can be made a lieutenant: the Boss already has as
        /// many branches as he can keep an eye on.</summary>
        public static string SpanFull(string boss, int lieutenants) =>
            boss + " already has " + lieutenants +
            (lieutenants == 1 ? " lieutenant" : " lieutenants") +
            " - as many as he can hold.";

        public static string BlocksFull(string leader, int blocks) =>
            leader + " already answers for " + blocks +
            (blocks == 1 ? " block" : " blocks") + " - as much ground as he can carry.";
        public const string ReasonNoSuchCrew = "No such crew.";
        public const string ReasonNoSuchItem = "No such item in the stock.";
        public const string ReasonFinanceUnavailable =
            "The outfit's account book is unavailable.";
        public const string ReasonInvalidRecruitmentCost =
            "The recruitment cost is invalid.";
        public const string ReasonDead = "The man is dead.";
        public const string ReasonDeserted = "The man deserted.";
        public const string ReasonSpecialist = "A specialist stays on retainer.";
        public const string ReasonAlreadyLieutenant = "He already runs a crew.";
        public const string ReasonNotLieutenant = "He does not run a crew.";
        public const string ReasonLieutenantMoves = "A lieutenant is demoted, not reassigned.";
        public const string ReasonBossMoves = "The boss is the root of the outfit.";
        public const string ReasonAlreadyInCrew = "He is already in that crew.";
        public const string ReasonAlreadyUnderBoss = "He already answers directly to the boss.";
        public const string ReasonNoBoss = "The outfit has no authoritative boss.";
        public const string ReasonUnknownBlock = "No such canonical block.";
        public const string ReasonInvalidCommandParent =
            "Only the boss or a lieutenant can carry that responsibility.";
        public const string ReasonAlreadyFront = "He already runs the front.";
        public const string ReasonAlreadyHolds = "He already holds it.";
        public const string ReasonNotHeld = "Nobody holds it.";
        public const string ReasonGearViaLieutenant =
            "Gear goes to a lieutenant - his crew draws from him.";
    
        /// <summary>What a street's derived reading is called on a card. The words are
        /// the presentation profile's; this is the plain fallback for surfaces that do
        /// not carry one.</summary>
        public static string ControlWord(Territory.TerritoryControlState state) => state switch
        {
            Territory.TerritoryControlState.Influenced => "influence here",
            Territory.TerritoryControlState.Contested => "contested ground",
            Territory.TerritoryControlState.Controlled => "holds this street",
            Territory.TerritoryControlState.Dominated => "owns this street outright",
            Territory.TerritoryControlState.Uncontrolled => "nobody's street",
            _ => "not known",
        };

}
}
