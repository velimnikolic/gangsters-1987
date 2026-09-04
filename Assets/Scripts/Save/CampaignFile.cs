using System;
using LivingCity.Outfit;
using LivingCity.Territory;

namespace LivingCity.Save
{
    /// <summary>One premises and whose name is on it.</summary>
    [Serializable]
    public sealed class DeedDto
    {
        public string businessId;
        public int gangId;
        public int legacyBlockId;
    }

    /// <summary>A shop that is shut, why, and until when.</summary>
    [Serializable]
    public sealed class ShutdownDto
    {
        public string businessId;
        public int cause;
        public double startedAt;
        public double recoveryAt;
    }

    /// <summary>The successor generation currently behind one business door.</summary>
    [Serializable]
    public sealed class OwnerGenerationDto
    {
        public string businessId;
        public int generation;
    }

    /// <summary>One man the city is holding, and where in the pipe he stands.</summary>
    [Serializable]
    public sealed class PrisonerDto
    {
        public int characterId;
        public int gangId = -1;
        public int deed;
        public int takenOnDay;
        public int courtDay;
        public int sentenceDays;
        public int outOnDay;
        public int stage;

        // Appended (GAN-302). Without these five a loaded man was tried with no docket
        // behind him - convicted without a roll, whatever the player did to the
        // witnesses - and a bailed man came back with his bail unpaid and the boss's
        // order to skip forgotten.

        /// <summary>The docket number he is on, or -1.</summary>
        public int caseId = -1;

        /// <summary>Which drive is due, or running (PrisonLeg).</summary>
        public int leg;

        /// <summary>The day the van to the prison runs.</summary>
        public int prisonDay;

        /// <summary>What the outfit put up to get him out.</summary>
        public int bailPaid;

        /// <summary>The boss has said he is not turning up.</summary>
        public bool skipOrdered;

        /// <summary>Appended (AI-006). How many days running the transfer for his
        /// current leg has failed to run: at TransferFailsBeforePaper the leg is
        /// carried on paper, so a file that dropped it let a reload put a man back at
        /// the start of the wait, and a player could defeat the anti-stall guarantee by
        /// loading (Codex adversarial review, 2026-09-04). Zero is the old file's
        /// value and the honest one.</summary>
        public int transferFails;

        // Appended (GAN-315): how he answered at the door, and whether this custody
        // had already been broken before the station threshold.
        public int answer;
        public bool sprung;
    }

    /// <summary>One public fact in the retained city newspaper archive.</summary>
    [Serializable]
    public sealed class PressDto
    {
        public int day;
        public float hour;
        public int kind;
        public string where;
        public string business;
        public int[] factions;
        public int namedGangId = -1;
        public int attribution;
        public int witnesses;
        public int gangsterDeaths;
        public int civilianDeaths;
        public int officerDeaths;
        public int shots;
        public string[] names;
        public string[] models;
        public int deed;
        public int sentenceDays;
        public int caseId = -1;
        public int incidentNumber = -1;
        public int outcome;
        public int weight;
    }

    /// <summary>One name on a case's witness list, as it went to disk. The position is
    /// three floats because the whole police layer is engine-free.</summary>
    [Serializable]
    public sealed class WitnessDto
    {
        public int kind;
        public string name;
        public int seed;
        public float x, y, z;
        public int standing;
        public string businessId;
    }

    /// <summary>What the court did to one man on one case.</summary>
    [Serializable]
    public sealed class CaseVerdictDto
    {
        public int characterId;
        public int outcome;
        public int days;
        public int outOnDay;
        public int day;
        public int answer;
        public bool sprung;
    }

    /// <summary>
    /// ONE DOCKET NUMBER (GAN-302). The case is what a trial is decided on - its
    /// witnesses are what the player leaned on, its counts are what he answers for -
    /// and it was not saved at all: a man loaded out of a file was tried with no case
    /// behind him and convicted without a roll.
    /// </summary>
    [Serializable]
    public sealed class CourtCaseDto
    {
        public int caseId;
        public int deed;
        public int gangId;
        public string businessId;
        public string where;
        public int[] defendants;
        public WitnessDto[] witnesses;

        /// <summary>A civilian-death file carries the body independently of its
        /// witness list. Appended to version 3; an older version-3 JSON simply reads
        /// false, which is its original meaning.</summary>
        public bool bodyEvidence;

        public int[] counts;
        public int[] extraCharges;
        public int openedDay;
        public int courtDay;
        public int lawyerId = -1;
        public int status;
        public bool anyTried;
        public CaseVerdictDto[] verdicts;
    }

    /// <summary>What one family has learnt of the city.</summary>
    [Serializable]
    public sealed class KnowledgeDto
    {
        public int gangId;
        public string[] places;
        public int[] men;
    }

    /// <summary>
    /// A CAMPAIGN, WRITTEN DOWN.
    ///
    /// JsonUtility's rules and no others: a [Serializable] class of public fields and
    /// arrays. No dictionaries, no properties, nothing reflected over. Every stateful
    /// class copies its own values by name in its own file, so a field that moves breaks
    /// the build instead of quietly disappearing from the save.
    ///
    /// WHAT IS DELIBERATELY NOT IN HERE (D19): positions. Where a man was standing is a
    /// frame, not a campaign; bodies are re-stood from the rosters on load. Nor fear,
    /// presence or derived control - all three are decaying readings of bodies that no
    /// longer exist, and the streets re-learn what they feel within a day.
    /// </summary>
    [Serializable]
    public sealed class CampaignFile
    {
        /// <summary>Bump when a field is removed or its meaning changes. A file from a
        /// LATER version is refused with a printed reason rather than half-read.
        /// </summary>
        /// <summary>
        /// 2 (GAN-302): the file carries the DOCKET. A version 1 file has prisoners and
        /// no cases at all, which is not a corrupt file - it is a file written before
        /// cases were saved - and <see cref="PrisonSnapshot.Restore"/> migrates it
        /// rather than reading a man onto a docket number that does not exist.
        /// </summary>
        public const int Version = 3;

        /// <summary>The last version written before the docket was part of the
        /// file.</summary>
        public const int VersionBeforeDocket = 1;

        /// <summary>The last file version written before the city press book.</summary>
        public const int VersionBeforePress = 2;

        /// <summary>The last version before successor generations were persisted.</summary>
        public const int VersionBeforeOwnerGenerations = 2;

        public int version = Version;
        public int citySeed;

        /// <summary>The campaign day and the hour of it. The clock is restored to these
        /// and to nothing else.</summary>
        public int day = 1;
        public float hourOfDay;

        public UnderworldDto underworld;

        /// <summary>
        /// THE MEN THE CITY IS HOLDING. A held man carries no release date - the day
        /// tick will not discharge a man without one - so the pipe is the only thing
        /// that ever lets him out. Leaving it out of the file jailed him for the rest
        /// of the campaign.
        /// </summary>
        public PrisonerDto[] prisoners;

        /// <summary>Everybody who has ever come out of the back of a car. The next
        /// judge reads it.</summary>
        public int[] escaped;

        public int prisonRosterSeed;

        /// <summary>THE DOCKET (GAN-302): every case the city has opened, open or
        /// closed, with its witnesses and its verdicts.</summary>
        public CourtCaseDto[] cases;

        /// <summary>The next docket number, so a case opened after a load does not
        /// collide with one already on the books.</summary>
        public int nextCaseId = 1;

        public TerritoryDto territory;
        public DeedDto[] deeds;
        public ShutdownDto[] shutdowns;
        public OwnerGenerationDto[] ownerGenerations;
        public KnowledgeDto[] knowledge;

        /// <summary>The citywide public record and the last edition already handed to
        /// the player. Top-level because neither belongs to one house.</summary>
        public PressDto[] press;
        public int lastEditionDay;
    }
}
