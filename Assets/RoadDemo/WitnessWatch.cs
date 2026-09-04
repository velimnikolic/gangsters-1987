using System.Collections.Generic;
using LivingCity.Police;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// THE BODY BEHIND A NAME ON THE DOCKET (GAN-245).
    ///
    /// A <see cref="Witness"/> is pure data - a name, a seed, a position, a standing -
    /// and deliberately holds no Unity reference: the docket has to outlive the man who
    /// despawned. This is the one place the two are tied together while both exist, so
    /// that a crew can be walked to him and so that killing him takes his evidence off
    /// the case.
    ///
    /// Nothing here decides anything. The roll that says whether a lean silences him is
    /// Police.WitnessPressure's; the arithmetic that says what his absence is worth is
    /// Police.Verdict's.
    /// </summary>
    public static class WitnessWatch
    {
        sealed class Pair
        {
            public CivilianAgent Body;
            public Witness Name;
            public CourtCase File;
        }

        static readonly List<Pair> _pairs = new List<Pair>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            _pairs.Clear();
            _leans.Clear();
        }

        /// <summary>This man on the pavement is that name on the case.</summary>
        public static void Register(CourtCase file, Witness witness, CivilianAgent body)
        {
            if (file == null || witness == null || body == null) return;
            _pairs.Add(new Pair { Body = body, Name = witness, File = file });
        }

        /// <summary>The body a name still has, or null - he has walked off the street,
        /// or he was never a man in the first place (an officer, a shopkeeper who is
        /// spoken to through his shop).</summary>
        public static CivilianAgent BodyOf(Witness witness)
        {
            for (var i = 0; i < _pairs.Count; i++)
                if (_pairs[i].Name == witness)
                    return _pairs[i].Body != null && !_pairs[i].Body.Dead
                        ? _pairs[i].Body : null;
            return null;
        }

        /// <summary>
        /// The name this body carries on an open case AGAINST THIS FAMILY, or null.
        ///
        /// The gang is not optional. Complaint cases are opened against rivals too, and
        /// a card that asked only "is this man a witness" handed the player a lean on a
        /// witness in somebody else's proceedings - mutating a rival's case and printing
        /// it on our own wire.
        /// </summary>
        public static Witness NameOf(CivilianAgent body, int gangId)
        {
            for (var i = 0; i < _pairs.Count; i++)
                if (_pairs[i].Body == body && _pairs[i].Name.Willing &&
                    _pairs[i].File != null && _pairs[i].File.Status == CaseStatus.Open &&
                    _pairs[i].File.GangId == gangId)
                    return _pairs[i].Name;
            return null;
        }

        /// <summary>The case a name is on.</summary>
        public static CourtCase CaseOf(Witness witness)
        {
            for (var i = 0; i < _pairs.Count; i++)
                if (_pairs[i].Name == witness)
                    return _pairs[i].File;
            return null;
        }

        /// <summary>
        /// A DEAD WITNESS IS OFF THE CASE. Swept rather than pushed: a bystander dies
        /// down StreetAlarm like everybody else, and matching a death position back to
        /// a name would be guesswork where this is a fact.
        ///
        /// The pair is dropped when its case closes, so this list is as short as the
        /// number of open cases - a handful, at most.
        /// </summary>
        public static void Tick()
        {
            for (var i = _pairs.Count - 1; i >= 0; i--)
            {
                var pair = _pairs[i];
                if (pair.File == null || pair.File.Status != CaseStatus.Open)
                {
                    _pairs.RemoveAt(i);
                    continue;
                }
                // GONE OFF THE STREET IS NOT GONE OFF THE DOCKET. A witness who walked
                // home is still a witness - his marker stands where he was seen last
                // (Witness.X/Y/Z, which is why it is stored) - so a vanished body only
                // ends the WATCH. Only death takes the name off the case.
                if (pair.Body == null) { _pairs.RemoveAt(i); continue; }
                if (pair.Body.Dead)
                {
                    KillName(pair.Name);
                    _pairs.RemoveAt(i);
                }
            }
            TickLeans();
        }

        // ------------------------------------------------------------- LEAN ON HIM

        /// <summary>Metres from the man a crew has to get before anything is said.</summary>
        const float LeanReach = 4.5f;

        /// <summary>Seconds a crew is given to reach him before the order lapses. A man
        /// who has walked off, or a crew that was told to do something else on the way,
        /// leaves nothing hanging.</summary>
        const float LeanPatience = 120f;

        /// <summary>What a lean on a witness puts on the street's memory - the same
        /// category a threat at a shop door files under, because it is the same act
        /// done to a man with no counter to stand behind.</summary>
        const float LeanSeverity = 1f;

        sealed class Lean
        {
            public DemoCrews.Unit Crew;
            public Witness Name;
            public CivilianAgent Body;
            public int Faction;
            public float By;
            public bool Kill;
            public Vector3 LastDestination;
            public float RepathAt;
        }

        static readonly List<Lean> _leans = new List<Lean>();

        /// <summary>
        /// GO AND SEE HIM. The crew is already walking (the overlay gave the order);
        /// this is the thing that happens when they get there.
        /// </summary>
        public static void OrderLean(DemoCrews.Unit crew, Witness witness,
            CivilianAgent body)
        {
            if (crew == null || witness == null || body == null) return;
            for (var i = 0; i < _leans.Count; i++)
                if (_leans[i].Crew == crew && _leans[i].Name == witness)
                    return;
            _leans.Add(new Lean
            {
                Crew = crew,
                Name = witness,
                Body = body,
                Faction = crew.Faction,
                By = Time.time + LeanPatience,
                LastDestination = body.Tf != null ? body.Tf.position : Vector3.zero,
                RepathAt = Time.time + 0.75f,
            });
        }

        /// <summary>
        /// SHOOT HIM. This is the violent twin of a lean: the card supplies the first
        /// walk, this watch keeps correcting that walk as the witness moves, and only
        /// the shared crew-combat path is allowed to fire the point-blank round.
        /// </summary>
        public static void OrderKill(DemoCrews.Unit crew, Witness witness,
            CivilianAgent body)
        {
            if (crew == null || witness == null || body == null) return;
            for (var i = 0; i < _leans.Count; i++)
                if (_leans[i].Crew == crew)
                    return;
            _leans.Add(new Lean
            {
                Crew = crew,
                Name = witness,
                Body = body,
                Faction = crew.Faction,
                By = Time.time + LeanPatience,
                Kill = true,
                LastDestination = body.Tf != null ? body.Tf.position : Vector3.zero,
                RepathAt = Time.time + 0.75f,
            });
        }

        /// <summary>Whether this crew has been told to go and see somebody - what the
        /// card reads to refuse a second order rather than stacking two.</summary>
        public static bool Leaning(DemoCrews.Unit crew)
        {
            for (var i = 0; i < _leans.Count; i++)
                if (_leans[i].Crew == crew) return true;
            return false;
        }

        static void TickLeans()
        {
            for (var i = _leans.Count - 1; i >= 0; i--)
            {
                var lean = _leans[i];
                var over = lean.Crew == null || lean.Crew.Wiped || lean.Crew.Surrendered ||
                           lean.Body == null || lean.Body.Dead || lean.Body.Tf == null ||
                           !lean.Name.Willing || Time.time > lean.By;
                if (over) { _leans.RemoveAt(i); continue; }

                var man = DemoCrews.NearestOf(lean.Crew, lean.Body.Tf.position);
                if (man == null || man.Tf == null) continue;
                if ((man.Tf.position - lean.Body.Tf.position).sqrMagnitude >
                    LeanReach * LeanReach)
                {
                    // A witness is not a pin in the map. Correct the quiet walk when he
                    // has moved, and periodically even when a previously valid route was
                    // blocked after it was issued.
                    var destination = lean.Body.Tf.position;
                    if (Time.time >= lean.RepathAt &&
                        ((destination - lean.LastDestination).sqrMagnitude > 0.25f ||
                         !man.HasOrder))
                    {
                        var crews = DemoCrews.Active;
                        if (crews != null && crews.OrderUnit(
                                lean.Crew, destination, out var accepted,
                                run: false, speak: false))
                            lean.LastDestination = accepted;
                        lean.RepathAt = Time.time + 0.75f;
                    }
                    continue;
                }

                if (lean.Kill)
                {
                    var crews = DemoCrews.Active;
                    if (crews == null ||
                        !crews.ExecuteCivilian(man, lean.Body, lean.Body.Tf.position))
                        continue;
                    _leans.RemoveAt(i);
                    continue;
                }

                _leans.RemoveAt(i);
                Said(lean);
            }
        }

        /// <summary>
        /// What the owner beating becomes on the docket. An open complaint by this
        /// proprietor against this same family makes it witness tampering; without that
        /// exact relationship it is ordinary battery.
        /// </summary>
        public static LivingCity.Personnel.Deed DeedForBeating(
            PrisonPipeline pipeline, string businessId, int gangId)
        {
            if (pipeline != null && !string.IsNullOrEmpty(businessId))
                for (var f = 0; f < pipeline.Cases.Count; f++)
                {
                    var file = pipeline.Cases[f];
                    if (file == null || file.Status != CaseStatus.Open ||
                        file.GangId != gangId)
                        continue;
                    for (var w = 0; w < file.Witnesses.Count; w++)
                    {
                        var witness = file.Witnesses[w];
                        if (witness != null && witness.Willing &&
                            witness.Kind == WitnessKind.Complainant &&
                            witness.BusinessId == businessId)
                            return LivingCity.Personnel.Deed.WitnessTampering;
                    }
                }
            return LivingCity.Personnel.Deed.Battery;
        }

        public static LivingCity.Personnel.Deed DeedForBeating(
            string businessId, int gangId) =>
            DeedForBeating(PoliceForce.Instance != null ? PoliceForce.Instance.Pipeline : null,
                businessId, gangId);

        /// <summary>
        /// The proprietor was killed behind his counter. He is not one case's special
        /// witness: every still-open case against every family that relied on him loses
        /// that complainant at once.
        /// </summary>
        public static int OwnerKilled(PrisonPipeline pipeline, string businessId)
        {
            if (pipeline == null || string.IsNullOrEmpty(businessId)) return 0;
            var killed = 0;
            for (var f = 0; f < pipeline.Cases.Count; f++)
            {
                var file = pipeline.Cases[f];
                if (file == null || file.Status != CaseStatus.Open) continue;
                for (var w = 0; w < file.Witnesses.Count; w++)
                {
                    var witness = file.Witnesses[w];
                    if (witness == null || witness.Standing == WitnessStanding.Dead ||
                        witness.Kind != WitnessKind.Complainant ||
                        witness.BusinessId != businessId)
                        continue;
                    KillName(witness, evenIfSilent: true);
                    killed++;
                }
            }
            return killed;
        }

        public static int OwnerKilled(string businessId) =>
            OwnerKilled(PoliceForce.Instance != null ? PoliceForce.Instance.Pipeline : null,
                businessId);

        static void KillName(Witness witness, bool evenIfSilent = false)
        {
            if (witness == null || witness.Standing == WitnessStanding.Dead ||
                (!evenIfSilent && !witness.Willing))
                return;
            witness.Standing = WitnessStanding.Dead;
            LawWire.WitnessKilled(witness);
            CrewOverlay.Announce(
                witness.Name.ToUpperInvariant() + " WILL NOT BE GIVING EVIDENCE",
                4.5f, new Color(1f, 0.55f, 0.45f));
        }

        /// <summary>
        /// THE WORDS, AND WHAT HE DOES ABOUT THEM. Either he has remembered nothing, or
        /// he rings the precinct about the men who came to see him - which is a fresh
        /// complaint, a fresh case and the same officer at the same door, and is what
        /// makes leaning on a witness a decision rather than a free move.
        ///
        /// One stream, off his own seed and the day: the same man on the same morning
        /// answers the same way twice.
        /// </summary>
        static void Said(Lean lean)
        {
            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            var today = outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
            var citySeed = LivingCity.Business.BusinessRuntime.Instance != null
                ? LivingCity.Business.BusinessRuntime.Instance.CitySeed : 1987;

            // The street remembers it whichever way it goes: men stood over a man in
            // the open is a threat, and the block feels it like any other.
            var runtime = TerritoryRuntime.Instance;
            if (runtime != null && lean.Body.Tf != null &&
                runtime.TryGetBlockForAct(lean.Body.Tf.position, out var blockId))
                runtime.RecordViolence(
                    new LivingCity.Territory.TerritoryGangId(lean.Faction), blockId,
                    LivingCity.Territory.TerritoryFearCategory.Threat, LeanSeverity);

            if (WitnessPressure.Withdraws(lean.Name.Seed,
                    LivingCity.Personnel.Sentencing.StreamFor(citySeed, lean.Name.Seed, today)))
            {
                Withdraw(lean.Name);
                return;
            }

            CrewOverlay.Announce(
                lean.Name.Name.ToUpperInvariant() + " IS RINGING THE PRECINCT",
                4.5f, new Color(1f, 0.55f, 0.45f));
            StreetAlarm.Complain(lean.Body.Tf.position, lean.Faction, "",
                lean.Name.Name, runtime != null ? runtime.GameHour : 0.0,
                LivingCity.Personnel.Deed.WitnessTampering);
        }

        /// <summary>He has remembered nothing after all. The one door a lean silences a
        /// witness through, so the paper and the case can never disagree.</summary>
        public static void Withdraw(Witness witness)
        {
            if (witness == null || witness.Standing != WitnessStanding.WillTestify)
                return;
            witness.Standing = WitnessStanding.Withdrawn;
            LawWire.WitnessWithdrawn(witness);
            CrewOverlay.Announce(
                witness.Name.ToUpperInvariant() + " HAS REMEMBERED NOTHING",
                4.5f, new Color(0.75f, 0.95f, 0.7f));
        }
    }
}
