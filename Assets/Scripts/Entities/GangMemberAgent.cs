using System.Collections;
using UnityEngine;
using LivingCity.City;

namespace LivingCity.Entities
{
    /// <summary>
    /// One gang member, loitering outside his family's front for as long as the city
    /// runs. Spawned and bound by GangDirector, persistent, and deliberately NOT a
    /// PedestrianAgent - a made man never wanders, never shops, never joins the chat
    /// pairing; staying out of PedestrianAgent.Agents is the whole opt-out, exactly as
    /// it is for a docker.
    ///
    /// DockWorkerAgent's standing model: the HumanBehavior follower is off for LIFE
    /// (the pavement outside one door needs no path graph), the body is Stationary so
    /// the crowd flows around him, and every little shuffle goes through Probe/Blend
    /// with the AllowedAdvance clamp. The loiter loop re-faces and side-steps on a
    /// per-agent rng so a crew never stands like a parade.
    ///
    /// Unlike the docker he IS on the overlay - registered, PoliceOfficerAgent's way,
    /// which is also what puts his coloured dot on the strategic map (StrategicMapHud
    /// tracks registered subjects; MapAffiliation supplies the gang colour). Clickable
    /// for free: the authored _AI prefab keeps a root capsule and both pick sites
    /// resolve with GetComponentInParent.
    ///
    /// The WalkTo/Face loop is the shared AgentLocomotion one, over this agent's own
    /// handles.
    ///
    /// Crowd invariants honoured: the body registers once for the object's lifetime;
    /// Stationary is kept truthful around every shuffle; no per-instance Update.
    /// </summary>
    [RequireComponent(typeof(HumanBehavior))]
    public sealed class GangMemberAgent : MonoBehaviour, UI.IOverlaySubject,
                                          UI.IOverlayStyledSubject
    {
        /// <summary>Seconds between loiter beats - a re-face or a little shuffle.</summary>
        const float LoiterSecondsMin = 12f;
        const float LoiterSecondsMax = 30f;

        /// <summary>Metres of side-step around the assigned post.</summary>
        const float ShuffleRadius = 0.8f;

        /// <summary>A shuffle is one step, not a march - short leash on the timeout.</summary>
        const float ShuffleTimeout = 6f;

        /// <summary>Degrees of idle gaze swing around the outward facing.</summary>
        const float GazeSwing = 120f;

        /// <summary>PoliceOfficerAgent's reasoning: the head is ~1.9m up.</summary>
        const float MarkerHeight = 2.2f;

        HumanBehavior human;
        Animator animator;
        PedestrianBody body;
        PedestrianAnthropometryStamp anthropometry;
        bool hasSpeedParam;

        System.Random rng;
        Vector3 post;
        Vector3 outward;
        string title;

        public int GangId { get; private set; } = -1;
        public Gangs.GangMemberIdentity Identity { get; private set; }

        void Awake()
        {
            human = GetComponent<HumanBehavior>();
            animator = GetComponent<Animator>();
            anthropometry = GetComponent<PedestrianAnthropometryStamp>();
        }

        void OnEnable()
        {
            if (body == null)
            {
                body = PedestrianRegistry.Register(transform);
                human.body = body;
            }

            UI.OverlayRegistry.Register(this);
        }

        void OnDisable()
        {
            UI.OverlayRegistry.Unregister(this);
        }

        void OnDestroy()
        {
            PedestrianRegistry.Unregister(body);
            body = null;
        }

        /// <summary>Bound by the director, standing at <paramref name="standPost"/> and
        /// facing <paramref name="faceOutward"/> (the street, off the facade normal).</summary>
        public void Bind(
            int gangId, Gangs.GangMemberIdentity identity, int seed,
            Vector3 standPost, Vector3 faceOutward)
        {
            GangId = gangId;
            Identity = identity;
            rng = new System.Random(seed);
            PedestrianRegistry.Seed(body, seed);
            post = standPost;
            outward = faceOutward.sqrMagnitude > 1e-4f ? faceOutward.normalized : Vector3.forward;

            // Off for life - DockWorkerAgent's reason: this man's world is one kerb, and
            // the follower's stale-route traps buy him nothing.
            human.enabled = false;
            human.randomDestination = false;

            hasSpeedParam = HasParameter(animator, PedestrianAnimation.SpeedHash);
            SetStationary(true);

            StartCoroutine(LoiterRoutine());
        }

        IEnumerator LoiterRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(LoiterSecondsMin
                    + (float)rng.NextDouble() * (LoiterSecondsMax - LoiterSecondsMin));

                // Mostly a bored glance; every third beat or so, a step's worth of
                // weight-shift around the post.
                if (rng.Next(3) == 0)
                {
                    var jitter = new Vector3(
                        ((float)rng.NextDouble() - 0.5f) * 2f * ShuffleRadius,
                        0f,
                        ((float)rng.NextDouble() - 0.5f) * 2f * ShuffleRadius);
                    yield return WalkTo(post + jitter, 0.3f, ShuffleTimeout);
                }

                var swing = ((float)rng.NextDouble() - 0.5f) * GazeSwing;
                yield return Face(transform.position
                                  + Quaternion.Euler(0f, swing, 0f) * outward);
                SetStationary(true);
            }
        }

        // ------------------------------------------------------------------ overlay

        // The ephemeral-vs-registered call: civilians stay off the registry because ten
        // thousand markers would thrash SyncMarkers; a couple dozen gang members are
        // police-fleet scale, and registration is what tracks them onto the M-map.
        Transform UI.IOverlaySubject.OverlayAnchor => transform;
        float UI.IOverlaySubject.OverlayHeight => anthropometry ? anthropometry.OverlayHeight : MarkerHeight;
        bool UI.IOverlaySubject.OverlayHidden => body != null && body.Hidden;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Diamond;
        Color UI.IOverlaySubject.OverlayColor => UI.GangPalette.Of(GangId);

        string UI.IOverlaySubject.OverlayTitle =>
            title ??= Identity == null
                ? name
                : UI.GangIntention.Title(Identity.FirstName, Identity.Surname,
                                         Identity.Lieutenant);

        string UI.IOverlaySubject.OverlayLine =>
            UI.GangIntention.Line(Gangs.GangRegistry.NameOf(GangId),
                                  GangId == Gangs.GangCatalog.PlayerGangId);

        long UI.IOverlaySubject.OverlayKey =>
            ((long)GangId << 8) | (Identity != null && Identity.Lieutenant ? 1L : 0L);

        /// <summary>
        /// A diamond over the head only while this man is the selection - the
        /// businesses' rule, for the same reason. Every family's men standing at every
        /// front put a permanent coloured dot over every head in the city at once, which
        /// is a screen full of marks the player is never going to act on; the man himself
        /// is the thing to look at, and he is as clickable with the marker off (picking
        /// is physics, it never consults the Image).
        /// </summary>
        UI.MarkerStyle UI.IOverlayStyledSubject.MarkerStyle =>
            new UI.MarkerStyle { SizeScale = 1f, SelectedOnly = true };

        // ------------------------------------------------------------------ movement
        // The shared AgentLocomotion loop, over this agent's own handles.

        IEnumerator WalkTo(Vector3 target, float stopWithin, float timeout) =>
            AgentLocomotion.WalkTo(transform, human, body, animator, hasSpeedParam,
                                   target, stopWithin, timeout);

        IEnumerator Face(Vector3 point, float seconds = 0.35f) =>
            AgentLocomotion.Face(transform, point, seconds);

        // ------------------------------------------------------------------ plumbing

        void SetStationary(bool stationary) =>
            AgentLocomotion.SetStationary(body, animator, hasSpeedParam, stationary);

        void SetSpeed(float metresPerSecond) =>
            AgentLocomotion.SetSpeed(animator, hasSpeedParam, metresPerSecond);

        static bool HasParameter(Animator animator, int nameHash) =>
            AgentLocomotion.HasParameter(animator, nameHash);
    }
}
