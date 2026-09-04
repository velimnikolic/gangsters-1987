using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Police
{
    /// <summary>What a witness IS - and every one of the four is worth a different
    /// amount to a prosecutor.</summary>
    public enum WitnessKind
    {
        /// <summary>The shopkeeper who picked up the telephone. He is the case, in a
        /// complaint - and he is also the man the crew can go back and lean on.</summary>
        Complainant,

        /// <summary>Somebody who was on the pavement and saw it happen.</summary>
        Eyewitness,

        /// <summary>An officer who watched the act itself - the shot fired, the body
        /// dropping. There is no cure for one of these.</summary>
        PoliceSawIt,

        /// <summary>The arresting officer, who found the men at the scene and nothing
        /// more. Every complaint arrest has one; it is the weakest thing on the
        /// docket.</summary>
        PoliceFoundThem,
    }

    /// <summary>Where a witness stands on the morning of the trial.</summary>
    public enum WitnessStanding
    {
        WillTestify,
        Withdrawn,
        Dead,
    }

    /// <summary>Where the case itself stands.</summary>
    public enum CaseStatus
    {
        Open,
        Dismissed,
        Tried,

        // Appended (GAN-302) so every serialized status above keeps its meaning.

        /// <summary>Closed without a trial: its counts were folded into a later case,
        /// or every man on it was taken off it before a judge ever saw one. Not a
        /// dismissal - nothing was thrown out - and not a trial.</summary>
        Folded,
    }

    /// <summary>How one man's case ended.</summary>
    public enum CaseOutcome
    {
        Convicted,
        Acquitted,

        /// <summary>The case was thrown out before any roll: nobody was left to give
        /// evidence.</summary>
        Dismissed,

        /// <summary>He was bailed and never appeared. The money is gone and the case
        /// stays open against him.</summary>
        BailForfeit,

        /// <summary>The boss closed the outfit's file on him while he was inside.</summary>
        CutLoose,

        /// <summary>He died before the state delivered him. Appended so every serialized
        /// outcome above keeps its value.</summary>
        Killed,
    }

    /// <summary>
    /// WHAT THE COURT DID TO ONE MAN, kept on the case rather than only on his sheet.
    ///
    /// The rap sheet is the MAN's book and stays exactly as it is - free text, one line
    /// per thing that happened to him. This is the CASE's book: it says what became of
    /// every name on this docket number, so the ledger can print a closed case as the
    /// thing it was rather than reassembling it out of prose.
    /// </summary>
    public sealed class CaseVerdict
    {
        public int CharacterId;
        public CaseOutcome Outcome;
        public DoorAnswer Answer;
        public bool Sprung;

        /// <summary>Days of the sentence, or 0 for anything but a conviction.
        /// <see cref="Sentencing.Life"/> when it is life.</summary>
        public int Days;

        /// <summary>The absolute day he comes out, 0 when there is none.</summary>
        public int OutOnDay;

        /// <summary>The absolute campaign day it was decided.</summary>
        public int Day;
    }

    /// <summary>
    /// One name on the prosecution's list. Snapshotted when the incident opened and
    /// never added to afterwards (the epic's own rule): a list that could grow would
    /// mean leaning on a witness bought the crew nothing, because another one would
    /// simply appear.
    ///
    /// The position is three floats rather than a Vector3 - this whole layer is
    /// engine-free, and the turf map does the converting.
    /// </summary>
    public sealed class Witness
    {
        public WitnessKind Kind;

        /// <summary>What the paper calls him. A civilian's is dealt off his seed
        /// (Entities.PedestrianIdentity); a shopkeeper's is his shop's.</summary>
        public string Name = "";

        /// <summary>The stream everything about him is rolled off - his nerve, and so
        /// whether a lean actually silences him.</summary>
        public int Seed;

        /// <summary>Where he was when the case opened. A witness who has despawned
        /// keeps his marker here, which is the whole reason it is stored.</summary>
        public float X, Y, Z;

        public WitnessStanding Standing = WitnessStanding.WillTestify;

        /// <summary>The business he keeps, for a complainant - what the Fear gate at
        /// court day is read against. Empty for everybody else.</summary>
        public string BusinessId = "";

        public bool Willing => Standing == WitnessStanding.WillTestify;

        /// <summary>Whether a crew can do anything about him at all. A policeman is not
        /// leaned on and not shot at over an affray charge (v1).</summary>
        public bool CanBePressured =>
            Kind == WitnessKind.Eyewitness || Kind == WitnessKind.Complainant;
    }

    /// <summary>
    /// ONE CASE, against one crew, for one deed.
    ///
    /// The whole unit is tried together (the epic's rule: the whole crew goes in), so
    /// the case carries every defendant and one witness list, and the VERDICT is per
    /// man - a lieutenant with a name in the paper and the hood beside him do not go
    /// down for the same number of days off the same roll.
    ///
    /// Pure record: no logic beyond counting its own witnesses. It lives in the
    /// PrisonPipeline beside the prisoners, and Prisoner.CaseId points at it.
    /// </summary>
    public sealed class CourtCase
    {
        public int CaseId;
        public Deed Deed;

        /// <summary>The faction the case is against - 0 the outfit, >0 a mob. A
        /// complaint that took nobody is still filed against the crew that made it.</summary>
        public int GangId;

        /// <summary>The door it was rung from, when it began as a complaint. Empty
        /// otherwise.</summary>
        public string BusinessId = "";

        /// <summary>What the shop is called, for the paper.</summary>
        public string Where = "";

        public readonly List<int> Defendants = new List<int>();
        public readonly List<Witness> Witnesses = new List<Witness>();

        /// <summary>The victim's body is physical evidence even when every civilian
        /// who saw the shooting is dead or unwilling. Set only by the civilian-death
        /// wire: an ordinary complaint with a silenced shopkeeper does not acquire an
        /// invisible witness and cannot become a count.</summary>
        public bool BodyEvidence;

        /// <summary>Other cases folded into this one as extra counts, by their id -
        /// open complaints the same crew never answered for.</summary>
        public readonly List<int> Counts = new List<int>();

        /// <summary>Deed-typed counts born in this arrest rather than another case.</summary>
        public readonly List<Deed> ExtraCharges = new List<Deed>();

        public int OpenedDay;

        /// <summary>The absolute day it is heard; 0 while nobody has been taken and it
        /// is only on the docket.</summary>
        public int CourtDay;

        /// <summary>The lawyer of record, or -1. Set at the trial off the roster - the
        /// outfit's counsel is counsel on every case it has while he is on the books.</summary>
        public int LawyerId = -1;

        public CaseStatus Status = CaseStatus.Open;

        /// <summary>Whether anybody on this case has actually been TRIED, as against
        /// walking for want of a witness. It decides what the case is stamped when the
        /// last defendant is resolved: one man tried is a case that was heard, however
        /// the men who came after him got on (PrisonPipeline.ResolveDefendant).</summary>
        public bool AnyTried;

        /// <summary>What became of each man who was ever a defendant here (GAN-302).
        /// Written by <see cref="PrisonPipeline"/> at every close and by nothing else.
        /// Empty while the case is still being answered.</summary>
        public readonly List<CaseVerdict> Verdicts = new List<CaseVerdict>();

        /// <summary>What the court did to one man on this case, or null.</summary>
        public CaseVerdict VerdictFor(int characterId)
        {
            for (var i = 0; i < Verdicts.Count; i++)
                if (Verdicts[i].CharacterId == characterId)
                    return Verdicts[i];
            return null;
        }

        public bool HasDefendant(int characterId) => Defendants.Contains(characterId);

        /// <summary>Eyewitnesses still willing to stand up.</summary>
        public int WillingEyewitnesses()
        {
            var count = 0;
            for (var i = 0; i < Witnesses.Count; i++)
                if (Witnesses[i].Kind == WitnessKind.Eyewitness && Witnesses[i].Willing)
                    count++;
            return count;
        }

        public bool Has(WitnessKind kind)
        {
            for (var i = 0; i < Witnesses.Count; i++)
                if (Witnesses[i].Kind == kind && Witnesses[i].Willing)
                    return true;
            return false;
        }

        /// <summary>Anybody at all left to put up. A case with nobody is not tried -
        /// it is thrown out before the roll (the epic's rule).</summary>
        public bool AnyWilling()
        {
            for (var i = 0; i < Witnesses.Count; i++)
                if (Witnesses[i].Willing)
                    return true;
            return false;
        }

        /// <summary>Anything the state can still put before a court. A body carries a
        /// murder file without pretending to be a living witness.</summary>
        public bool AnyEvidence() => BodyEvidence || AnyWilling();

        /// <summary>How many willing witnesses there are, for the paper and the file.</summary>
        public int WillingCount()
        {
            var count = 0;
            for (var i = 0; i < Witnesses.Count; i++)
                if (Witnesses[i].Willing)
                    count++;
            return count;
        }
    }

    /// <summary>
    /// WHETHER HE RINGS. A leaned-on shopkeeper is not a number that moves any more -
    /// he decides, and the decision is the family's STANDING on his street against his
    /// own nerve to go against it.
    ///
    /// The arc is the whole point: a family nobody has heard of gets the telephone
    /// picked up on it most of the time - a stranger in the doorway is a matter for
    /// the precinct - and a family the street already answers to does not, because
    /// the man behind the counter has watched what happened to the last one who rang,
    /// or has simply watched every other door on the block pay. A cousin at the
    /// precinct is worth something whatever the standing; a man who wants no trouble
    /// is worth less. Both ends are clamped so no shopkeeper is ever a certainty in
    /// either direction - the player has to expect the telephone, not know about it.
    /// </summary>
    public static class ComplaintRoll
    {
        public const float Floor = 0.02f;
        public const float Ceiling = 0.95f;

        /// <summary>What a shopkeeper with no cousin anywhere rings at when nobody on
        /// his street fears the family or pays it: most of the time.</summary>
        public const float NewcomerBase = 0.55f;

        /// <summary>What his own connections add on top of that, at full connections.</summary>
        public const float ConnectionsWeight = 0.45f;

        /// <summary>How fast standing silences a street. The bold share is
        /// (1 - standing) to this power: at half standing a quarter of the calls are
        /// left, at seventy percent one in ten, at ninety next to none.</summary>
        public const float Steep = 2f;

        /// <summary>What a cousin at the precinct is worth on top of the roll itself -
        /// added AFTER standing has done its work, so a connected owner still rings
        /// now and then on a family the rest of the street would not dare to.</summary>
        public const float ConnectedBonus = 0.15f;

        /// <summary>What being the kind of man who does not want trouble takes off
        /// it.</summary>
        public const float CowardlyPenalty = 0.15f;

        /// <summary>
        /// HOW ESTABLISHED THE FAMILY IS ON HIS STREET, 0..1: the larger of what the
        /// street fears of it (its fear over the cap) and how much of the street
        /// already pays it (the block's compliance share, 0..100). A family that has
        /// never fired a shot but collects from every door on the block is as
        /// established, to the man behind the counter, as one that has.
        /// </summary>
        public static float Standing(float businessFear, float fearCap, float payingShare)
        {
            var cap = fearCap > 1f ? fearCap : 1f;
            var frightened = businessFear / cap;
            var paying = payingShare / 100f;
            var standing = frightened > paying ? frightened : paying;
            if (standing < 0f) standing = 0f;
            if (standing > 1f) standing = 1f;
            return standing;
        }

        /// <summary>
        /// The odds this owner picks up the telephone about what was just said to him,
        /// against a family with this standing on his street.
        ///
        /// Traits are handed in as the two flags that matter rather than as the
        /// Territory enum: this layer knows nothing about the economy layer and is not
        /// going to start now.
        /// </summary>
        public static float Chance(float connections, float standing,
            bool connected, bool cowardly)
        {
            if (connections < 0f) connections = 0f;
            if (connections > 1f) connections = 1f;
            if (standing < 0f) standing = 0f;
            if (standing > 1f) standing = 1f;

            var bold = (float)System.Math.Pow(1f - standing, Steep);
            var chance = bold * (NewcomerBase + ConnectionsWeight * connections)
                         + (connected ? ConnectedBonus : 0f)
                         - (cowardly ? CowardlyPenalty : 0f);
            return chance < Floor ? Floor : chance > Ceiling ? Ceiling : chance;
        }

        /// <summary>The same roll for a street where nobody pays the family yet: fear
        /// is the whole of its standing.</summary>
        public static float Chance(float connections, float businessFear, float fearCap,
            bool connected, bool cowardly) =>
            Chance(connections, Standing(businessFear, fearCap, 0f), connected, cowardly);

        /// <summary>The roll itself, off a stream the caller mixed - never a shared
        /// RNG, so the same city on the same day answers the same way.</summary>
        public static bool Rings(float chance, int stream) =>
            new System.Random(stream).NextDouble() < chance;

        /// <summary>The stream one complaint is rolled on: the shop, the day and the
        /// incident that provoked it.</summary>
        public static int StreamFor(int citySeed, string businessId, int day, int incident)
        {
            unchecked
            {
                var h = 2166136261u;
                h = (h ^ (uint)citySeed) * 16777619u;
                for (var i = 0; businessId != null && i < businessId.Length; i++)
                    h = (h ^ businessId[i]) * 16777619u;
                h = (h ^ (uint)day) * 16777619u;
                h = (h ^ (uint)incident) * 16777619u;
                h ^= h >> 15;
                return (int)h;
            }
        }
    }

    /// <summary>
    /// THE TRIAL. There is no jury, no courthouse and no appeal: the judge is a roll,
    /// and what he is rolling against is what the prosecution actually has.
    ///
    /// The whole design is here in one formula, and the one line that matters is that
    /// a threat with nobody but the shopkeeper behind it is MOSTLY LOST - "moja rec
    /// protiv njegove". A word against a word is 0.55 with no counsel and 0.15 with a
    /// good lawyer; two people who saw it happen convict; a policeman who watched the
    /// act is not talked away by anybody.
    /// </summary>
    public static class Verdict
    {
        public const float ExtortionBase = 0.30f;
        public const float AffrayBase = 0.50f;
        public const float MurderBase = 0.55f;
        public const float CopKillingBase = 0.95f;
        public const float AssaultOnOfficerBase = 0.75f;
        public const float ResistingBase = 0.45f;
        public const float BatteryBase = 0.30f;

        /// <summary>What each eyewitness is worth, and how many of them the court
        /// bothers to count. A third man who saw the same thing is not a third case.</summary>
        public const float PerEyewitness = 0.20f;
        public const int EyewitnessesCounted = 2;

        public const float PoliceSawItWeight = 0.30f;
        public const float PoliceFoundThemWeight = 0.10f;
        public const float ComplainantWeight = 0.15f;

        /// <summary>What each prior conviction on his sheet is worth, and how many of
        /// them count.</summary>
        public const float PerPrior = 0.05f;
        public const int PriorsCounted = 3;

        /// <summary>What one star of counsel takes off it.</summary>
        public const float PerLawyerSkill = 0.08f;

        public const float Floor = 0.05f;
        public const float Ceiling = 0.98f;

        /// <summary>
        /// Fear of the family, on the shop's own account, at or above which the man who
        /// rang the precinct will not stand up in court after all. He is not bribed and
        /// nothing is written down: he has had five days of them standing in his
        /// doorway, and he has decided he did not see anything.
        ///
        /// A CONNECTED owner testifies whatever this says - his cousin at the precinct
        /// is why he rang, and it is why he turns up.
        /// </summary>
        public const float TestifyFearCap = 55f;

        public static float BaseFor(Deed deed) => deed switch
        {
            Deed.CopKilling => CopKillingBase,
            Deed.Murder => MurderBase,
            Deed.AssaultOnOfficer => AssaultOnOfficerBase,
            Deed.Resisting => ResistingBase,
            Deed.Battery => BatteryBase,
            Deed.Affray => AffrayBase,
            Deed.Extortion => ExtortionBase,
            Deed.WitnessTampering => ExtortionBase,
            _ => throw new System.ArgumentOutOfRangeException(nameof(deed), deed,
                "Every deed needs an explicit verdict base."),
        };

        /// <summary>What the odds are that this defendant goes down.</summary>
        public static float ConvictionChance(Deed deed, int eyewitnesses, bool policeSawIt,
            bool policeFoundThem, bool complainant, int priors, int lawyerSkill)
        {
            var seen = eyewitnesses < 0 ? 0 : eyewitnesses;
            if (seen > EyewitnessesCounted) seen = EyewitnessesCounted;
            var record = priors < 0 ? 0 : priors;
            if (record > PriorsCounted) record = PriorsCounted;
            var counsel = lawyerSkill < 0 ? 0 : lawyerSkill;

            var chance = BaseFor(deed)
                         + PerEyewitness * seen
                         + (policeSawIt ? PoliceSawItWeight : 0f)
                         + (policeFoundThem ? PoliceFoundThemWeight : 0f)
                         + (complainant ? ComplainantWeight : 0f)
                         + PerPrior * record
                         - PerLawyerSkill * counsel;
            return chance < Floor ? Floor : chance > Ceiling ? Ceiling : chance;
        }

        /// <summary>The odds for one man against one case, read off the case's own
        /// witness list. The one door the trial actually goes through.</summary>
        public static float ConvictionChance(CourtCase file, int priors, int lawyerSkill) =>
            file == null
                ? Floor
                : ConvictionChance(file.Deed, file.WillingEyewitnesses(),
                    file.Has(WitnessKind.PoliceSawIt), file.Has(WitnessKind.PoliceFoundThem),
                    file.Has(WitnessKind.Complainant), priors, lawyerSkill);

        /// <summary>The roll. One stream per man per day, like the sentence itself.</summary>
        public static bool Convicts(float chance, int stream) =>
            new System.Random(stream).NextDouble() < chance;

        /// <summary>The same roll off a stream already open - the verdict and the
        /// sentence are ONE stream per man per day, drawn in that order, so a man tried
        /// twice in a campaign is never sentenced twice off the same numbers.</summary>
        public static bool Convicts(float chance, System.Random rng) =>
            rng == null || rng.NextDouble() < chance;

        /// <summary>
        /// HOW IT READS, and the ONLY table that says so (GAN-302). The banner while the
        /// player waits on a verdict and the counsel's read on the ledger's law sheet
        /// are the same four bands: two tables would have the street and the book
        /// disagreeing about the same case.
        ///
        /// Words, never a number. The player is meant to expect the court, not to know
        /// it - and the draw itself is fixed per man per court day, so no amount of
        /// reading the line tells him where it fell.
        /// </summary>
        public static string Leaning(float chance) =>
            chance >= 0.8f ? "IT LOOKS BAD FOR HIM"
            : chance >= 0.55f ? "THE STATE HAS A CASE"
            : chance >= 0.3f ? "IT COULD GO EITHER WAY"
            : "THEY HAVE ALMOST NOTHING";

        /// <summary>What the read says when the prosecution has nobody left to put up.
        /// Not a band: it is a certainty, and the case is thrown out before any
        /// roll.</summary>
        public const string NoWitnessesLeft = "THEY HAVE NOBODY";

        /// <summary>And what it says with no lawyer on the books to ask.</summary>
        public const string NoCounselToAsk = "NO COUNSEL TO ASK";
    }

    /// <summary>
    /// LEANING ON A WITNESS. The five days between the arrest and the court day are
    /// what the player plays, and this is what he plays them with.
    ///
    /// A witness has a nerve dealt off his own seed - the same trick the shopkeepers
    /// use (Territory.TerritoryOwnerProfile) and for the same reason: no stored state,
    /// no draw order, the same man twice. Lean on him and either he withdraws or he
    /// rings the precinct about the men who came to see him, which is the risk that
    /// makes it a decision.
    /// </summary>
    public static class WitnessPressure
    {
        /// <summary>How much of a fright it takes to move him, 0..1.</summary>
        public static float Nerve(int seed)
        {
            unchecked
            {
                var h = (uint)seed * 2654435761u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFF) / 65535f;
            }
        }

        /// <summary>The odds a lean silences him. A man of no nerve at all folds nine
        /// times in ten; a steady one folds a quarter of the time.</summary>
        public static float WithdrawChance(int seed) => 0.9f - 0.65f * Nerve(seed);

        /// <summary>The roll, off a stream the caller mixed with the day.</summary>
        public static bool Withdraws(int seed, int stream) =>
            new System.Random(stream).NextDouble() < WithdrawChance(seed);
    }
}
