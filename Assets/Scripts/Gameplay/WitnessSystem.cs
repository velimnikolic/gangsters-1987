using System.Collections.Generic;
using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// One reported crime, shared by everyone who witnessed it. The report is per-CASE, not
    /// per-witness: the first witness whose call goes through delivers it (heat scaled by
    /// how many saw it), later calls find it already delivered. Kill every holder of an
    /// undelivered case before their delay runs out and the crime never reaches the police.
    /// </summary>
    public sealed class CrimeCase
    {
        public CrimeKind Kind;
        public Vector3 Position;
        public Transform Perpetrator;
        public int VisualWitnesses;
        public bool Delivered;
    }

    /// <summary>
    /// Turns crimes into consequences. Listens on CrimeFeed, and on a quarter-second tick -
    /// never per frame - decides who saw what and hands each witness an NpcWitness reaction.
    ///
    /// Two radii per crime, because a shot is HEARD much further than it is SEEN. Seeing
    /// takes all three: inside sightRadius, inside the witness's view cone, and a clear
    /// raycast. Hearing takes only the radius - a sound witness knows something happened,
    /// not who did it, so their report carries gunshot heat and the CRIME's position, never
    /// the player's.
    ///
    /// The same tick sweeps undiscovered corpses: a civilian with a clear line inside
    /// discovery range raises BodyDiscovered - the delayed price of the unwitnessed kill.
    ///
    /// Police are their own branch: a beat officer inside sight range with a clear line
    /// reports INSTANTLY (no fumbling delay), and anyone already wearing a badge is
    /// excluded from the civilian panic path - response officers answer through their FSM.
    /// </summary>
    public sealed class WitnessSystem : MonoBehaviour
    {
        const float TickSeconds = 0.25f;

        /// <summary>Head heights for the sight raycast - eye to scene, over car roofs.</summary>
        const float EyeHeight = 1.5f;
        const float SceneHeight = 1.2f;

        /// <summary>What blocks sight: everything except the crowd's own layer.</summary>
        static readonly int SightBlockers = ~(1 << PedestrianSpawner.PedestrianLayer);

        /// <summary>Sound radius in a dense crowd can hold hundreds of capsules; 512 is
        /// truncation-in-theory, plenty-in-practice - a crime with 500 witnesses is already
        /// maximally witnessed.</summary>
        static readonly Collider[] Overlaps = new Collider[512];

        readonly List<CrimeEvent> pending = new List<CrimeEvent>();
        readonly HashSet<InteractableNpc> seenThisCrime = new HashSet<InteractableNpc>();

        float nextTickAt;

        WantedConfig Config => GameplayRuntime.Wanted;

        void OnEnable() => CrimeFeed.Reported += OnCrime;

        void OnDisable() => CrimeFeed.Reported -= OnCrime;

        void OnCrime(CrimeEvent crime) => pending.Add(crime);

        void Update()
        {
            if (Time.time < nextTickAt)
                return;
            nextTickAt = Time.time + TickSeconds;

            if (pending.Count > 0)
            {
                for (var i = 0; i < pending.Count; i++)
                    ProcessCrime(pending[i]);
                pending.Clear();
            }

            SweepCorpses();
        }

        void ProcessCrime(in CrimeEvent crime)
        {
            var config = Config;
            var sight = config ? config.sightRadius : 25f;
            var sound = config ? config.soundRadius : 50f;
            var cone = config ? config.viewConeDot : 0.4f;
            var callChance = config ? config.soundWitnessCallChance : 0.5f;

            var openCase = new CrimeCase
            {
                Kind = crime.Kind,
                Position = crime.Position,
                Perpetrator = crime.Perpetrator,
            };

            // Killing an officer skips the witness lottery entirely - the force knows its
            // own. The case is born delivered so civilian calls do not stack a second
            // murder's heat on top; they still panic below.
            if (crime.Kind == CrimeKind.Murder && crime.Victim &&
                crime.Victim.GetComponentInParent<PoliceOfficerAgent>())
            {
                openCase.Delivered = true;
                if (WantedSystem.Instance && crime.Perpetrator)
                    WantedSystem.Instance.ReportCrime(
                        CrimeKind.AssaultingOfficer, crime.Position, 1,
                        knowsPerpetrator: true, crime.Perpetrator.position);
            }

            var radius = crime.Kind == CrimeKind.BodyDiscovered ? sight : sound;
            var count = Physics.OverlapSphereNonAlloc(
                crime.Position, radius, Overlaps, 1 << PedestrianSpawner.PedestrianLayer,
                QueryTriggerInteraction.Ignore);

            seenThisCrime.Clear();
            var witnesses = 0;
            var policeReported = openCase.Delivered;

            for (var i = 0; i < count; i++)
            {
                var collider = Overlaps[i];
                if (!collider)
                    continue;

                // A beat officer who SEES it radios it in on the spot.
                var beatOfficer = collider.GetComponentInParent<PoliceOfficerAgent>();
                if (beatOfficer)
                {
                    if (!policeReported &&
                        WithinSight(beatOfficer.transform, crime.Position, sight, coneDot: -1f))
                    {
                        policeReported = true;
                        openCase.Delivered = true;
                        if (WantedSystem.Instance)
                            WantedSystem.Instance.ReportCrime(
                                crime.Kind, crime.Position, 1,
                                knowsPerpetrator: crime.Perpetrator,
                                crime.Perpetrator ? crime.Perpetrator.position : crime.Position);
                    }
                    continue;
                }

                var npc = collider.GetComponentInParent<InteractableNpc>();
                if (!npc || npc.IsDead || seenThisCrime.Contains(npc))
                    continue;

                // An officer answers a crime through his own duty, not by panicking at it.
                if (npc.GetComponent<PoliceOfficerAgent>())
                    continue;

                // Hidden means inside a shop or building - and hidden does NOT mean the
                // collider is off (the crosswalk lesson). A wall between them and the
                // street is not a witness position.
                if (npc.Human && npc.Human.body is { Hidden: true })
                    continue;

                seenThisCrime.Add(npc);

                var flat = npc.transform.position - crime.Position;
                flat.y = 0f;
                var distance = flat.magnitude;

                // Stumbling onto a corpse needs no view cone - the body is at your feet.
                var visual = distance <= sight &&
                             WithinSight(npc.transform, crime.Position, sight,
                                 crime.Kind == CrimeKind.BodyDiscovered ? -1f : cone);

                var existing = npc.GetComponent<NpcWitness>();
                if (existing)
                {
                    // Already reacting to an earlier crime - a gunshot, usually. The graver
                    // case rides the same witness and the same fumbling timer, so a murder
                    // is never masked by its own muzzle report.
                    existing.AddCase(openCase, visual);
                    if (visual)
                        openCase.VisualWitnesses++;
                    witnesses++;
                    continue;
                }

                if (visual)
                {
                    openCase.VisualWitnesses++;
                    witnesses++;
                    npc.gameObject.AddComponent<NpcWitness>()
                        .Begin(this, openCase, visual: true, callsPolice: true);
                }
                else
                {
                    witnesses++;
                    var calls = Random.value < callChance;
                    npc.gameObject.AddComponent<NpcWitness>()
                        .Begin(this, openCase, visual: false, callsPolice: calls);
                }
            }

            if (WantedSystem.Instance)
                WantedSystem.Instance.LastCrimeWitnesses = witnesses;
        }

        /// <summary>A witness's call went through. First delivery wins; the rest are noise.</summary>
        public void Deliver(CrimeCase openCase, bool sawIt, Vector3 witnessPosition)
        {
            if (openCase.Delivered || !WantedSystem.Instance)
                return;

            openCase.Delivered = true;

            // A sound witness heard SOMETHING - the police get a gunshot report at the
            // crime's position, not a murder pinned on a man they never saw.
            var kind = sawIt ? openCase.Kind : CrimeKind.Gunshot;
            var knowsPerpetrator = sawIt && openCase.Perpetrator;

            WantedSystem.Instance.ReportCrime(
                kind, openCase.Position,
                Mathf.Max(1, openCase.VisualWitnesses),
                knowsPerpetrator,
                knowsPerpetrator ? openCase.Perpetrator.position : openCase.Position);
        }

        void SweepCorpses()
        {
            var config = Config;
            var discoveryRadius = config ? config.corpseDiscoveryRadius : 6f;

            for (var c = 0; c < CorpseEvidence.Corpses.Count; c++)
            {
                var corpse = CorpseEvidence.Corpses[c];
                if (!corpse || corpse.Discovered)
                    continue;

                var count = Physics.OverlapSphereNonAlloc(
                    corpse.transform.position, discoveryRadius, Overlaps,
                    1 << PedestrianSpawner.PedestrianLayer, QueryTriggerInteraction.Ignore);

                for (var i = 0; i < count; i++)
                {
                    var npc = Overlaps[i] ? Overlaps[i].GetComponentInParent<InteractableNpc>() : null;
                    if (!npc || npc.IsDead || npc.GetComponent<PoliceOfficerAgent>())
                        continue;
                    if (npc.GetComponent<NpcWitness>())
                        continue;
                    if (npc.Human && npc.Human.body is { Hidden: true })
                        continue;
                    if (!ClearLine(npc.transform.position, corpse.transform.position))
                        continue;

                    corpse.Discovered = true;
                    CrimeFeed.Report(new CrimeEvent(
                        CrimeKind.BodyDiscovered, corpse.transform.position,
                        null, corpse.transform));
                    break;
                }
            }
        }

        bool WithinSight(Transform witness, Vector3 crimePosition, float sightRadius, float coneDot)
        {
            var toCrime = crimePosition - witness.position;
            toCrime.y = 0f;
            if (toCrime.sqrMagnitude > sightRadius * sightRadius)
                return false;

            if (coneDot > -1f && toCrime.sqrMagnitude > 0.01f &&
                Vector3.Dot(witness.forward, toCrime.normalized) < coneDot)
                return false;

            return ClearLine(witness.position, crimePosition);
        }

        static bool ClearLine(Vector3 from, Vector3 to)
        {
            var a = from + Vector3.up * EyeHeight;
            var b = to + Vector3.up * SceneHeight;
            var direction = b - a;
            var distance = direction.magnitude;
            if (distance < 0.2f)
                return true;

            return !Physics.Raycast(a, direction / distance, distance,
                                    SightBlockers, QueryTriggerInteraction.Ignore);
        }
    }
}
