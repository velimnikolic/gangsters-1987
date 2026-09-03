using LivingCity.Gameplay;
using LivingCity.Personnel;
using LivingCity.Police;

namespace RoadDemo
{
    /// <summary>
    /// EVERY LINE THE LAW PUTS IN THE PAPER (GAN-245), in one place.
    ///
    /// The wire composes nothing of its own (WireBook's one rule): IncidentText wrote
    /// these sentences and this only files them, so the paper, the ledger's rail and
    /// the strip over the street print the same words about the same afternoon.
    ///
    /// A scene with no campaign behind it - the crew demo, a bench - files nothing and
    /// says nothing about it. Every method here is safe to call from the street.
    /// </summary>
    public static class LawWire
    {
        /// <summary>The one family whose paper this is. A complaint about somebody
        /// else's men is not news for OUR wire - the feed's sentences are written in
        /// the first person ("our men", "our names") and would be lies about a rival.</summary>
        const int Ours = LivingCity.Gangs.GangCatalog.PlayerGangId;

        static void File(int characterId, string name, IncidentKind kind, string where,
            int heat = 0)
        {
            var outfit = OutfitDirector.Instance;
            if (outfit == null || outfit.Incidents == null)
                return;
            var day = outfit.Campaign != null ? outfit.Campaign.Day : 0;
            outfit.Incidents.Add(new Incident(characterId, name ?? "", kind, day,
                where ?? "", heat, IncidentText.Line(kind, name ?? "", where ?? "")));
        }

        /// <summary>The SUBJECT of a law slip that is about a place rather than a man -
        /// the shop that rang, the door a statement was taken at. Never empty: every
        /// line in the paper names the thing it is about (IncidentText's own contract),
        /// and a slip with a hole where the name goes is not a sentence.</summary>
        static string Named(string where, string fallback) =>
            string.IsNullOrEmpty(where) ? fallback : where;

        /// <summary>The telephone came off the hook.</summary>
        public static void ComplaintRung(StreetAlarm.Complaint call)
        {
            if (call.Faction != Ours) return;
            File(-1, Named(call.Where, "A shopkeeper"), IncidentKind.ComplaintRung, "");
        }

        /// <summary>The telephone rang and the precinct had nobody to send. The one law
        /// line that is about the DEPARTMENT rather than about us - and the one the
        /// player needs most, because a call that dies in the switchboard used to leave
        /// no trace anywhere and read as the telephone itself being broken.</summary>
        public static void NobodyCame(StreetAlarm.Complaint call)
        {
            if (call.Faction != Ours) return;
            File(-1, Named(call.Where, "the door"), IncidentKind.NobodyCame, "");
        }

        /// <summary>The question was put and the answer was no. Filed apart from the
        /// statement on purpose: a statement says the officer found nobody, and printing
        /// that over a crew that stood there and refused him is a lie the paper would
        /// have to correct.</summary>
        public static void RefusedTheOfficer(StreetAlarm.Complaint call)
        {
            if (call.Faction != Ours) return;
            File(-1, Named(call.Where, "the door"), IncidentKind.RefusedTheOfficer, "");
        }

        /// <summary>A uniform in the doorway and nobody to take in.</summary>
        public static void StatementTaken(StreetAlarm.Complaint call)
        {
            if (call.Faction != Ours) return;
            File(-1, Named(call.Where, "the door"), IncidentKind.StatementTaken, "");
        }

        /// <summary>A case on the docket - the five days the player has to do something
        /// about it start here.</summary>
        public static void CaseOpened(CourtCase file)
        {
            if (file == null || file.GangId != Ours) return;
            File(-1, Named(file.Where, "last night"), IncidentKind.CaseOpened, "");
        }

        /// <summary>He has remembered nothing after all.</summary>
        public static void WitnessWithdrawn(Witness witness) =>
            File(-1, witness != null ? witness.Name : "", IncidentKind.WitnessWithdrawn, "");

        /// <summary>He will not be giving evidence.</summary>
        public static void WitnessKilled(Witness witness) =>
            File(-1, witness != null ? witness.Name : "", IncidentKind.WitnessKilled, "");

        public static void BailPosted(Character man) =>
            File(man != null ? man.Id : -1, man != null ? man.FullName : "",
                IncidentKind.BailPosted, "");

        public static void BailForfeit(Character man) =>
            File(man != null ? man.Id : -1, man != null ? man.FullName : "",
                IncidentKind.BailForfeit, "");

        public static void CutLoose(Character man) =>
            File(man != null ? man.Id : -1, man != null ? man.FullName : "",
                IncidentKind.CutLoose, "");

        /// <summary>What the court did to one man - the one line the player watches
        /// the whole epic for.</summary>
        public static void Verdict(Character man, PrisonStage stage, CaseStatus status)
        {
            if (man == null) return;
            var kind = stage == PrisonStage.Sentenced
                ? IncidentKind.Convicted
                : status == CaseStatus.Dismissed
                    ? IncidentKind.CaseDismissed
                    : IncidentKind.Acquitted;
            File(man.Id, man.FullName, kind, "");
        }
    }
}
