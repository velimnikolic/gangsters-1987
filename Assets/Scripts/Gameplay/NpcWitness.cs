using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.City;
using LivingCity.Entities;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// One civilian's reaction to a crime, added by WitnessSystem at the moment and removed
    /// when the panic runs its course. Two shapes: a CALLER stands rooted, gesticulating,
    /// with a pulsing "!" over their head for reportDelaySeconds - kill them inside that
    /// window and their report dies with them - then flees; a fleer just runs.
    ///
    /// Ownership follows the PedestrianAgent contract: the agent is disabled first (its
    /// OnDisable releases benches, doors and re-enables HumanBehavior), then HumanBehavior
    /// is disabled and THIS component owns the transform and the speed float. The hand-back
    /// re-enables both and calls ResetRoute - the sanctioned recovery for a walker who is
    /// no longer anywhere near his old route.
    ///
    /// One witness can hold several cases (the gunshot, then the murder a second later);
    /// all undelivered cases go through with the one call.
    /// </summary>
    public sealed class NpcWitness : MonoBehaviour, UI.IOverlayStyledSubject
    {
        struct Held
        {
            public CrimeCase Case;
            public bool Visual;
        }

        static readonly WaitForFixedUpdate FixedStep = new WaitForFixedUpdate();

        /// <summary>Matches HumanBehavior's own animator scaling (speed * 0.8).</summary>
        const float AnimatorSpeedScale = 0.8f;

        readonly List<Held> held = new List<Held>(2);

        WitnessSystem system;
        HumanBehavior human;
        PedestrianAgent agent;
        PedestrianIdler idler;
        PedestrianDeath death;
        Animator animator;
        bool hasSpeedParam;
        bool hasActivityParam;
        bool calling;
        bool registered;

        public void Begin(WitnessSystem witnessSystem, CrimeCase openCase, bool visual, bool callsPolice)
        {
            system = witnessSystem;
            held.Add(new Held { Case = openCase, Visual = visual });

            human = GetComponent<HumanBehavior>();
            agent = GetComponent<PedestrianAgent>();
            idler = GetComponent<PedestrianIdler>();
            death = GetComponent<PedestrianDeath>();
            animator = GetComponent<Animator>();
            hasSpeedParam = RouteWalker.HasParameter(animator, PedestrianAnimation.SpeedHash);
            hasActivityParam = RouteWalker.HasParameter(animator, PedestrianAnimation.ActivityHash);

            // Take over. Disabling the agent runs its Restore - seats released, activity
            // cleared, HumanBehavior re-enabled - which is exactly the clean slate to
            // take the transform from.
            if (agent)
                agent.enabled = false;
            if (idler)
                idler.enabled = false;
            if (human)
            {
                human.enabled = false;
                if (human.body != null)
                {
                    human.body.SpeedMs = 0f;
                    human.body.Stationary = true;
                }
            }
            SetSpeed(0f);

            if (death)
                death.died += OnDied;

            StartCoroutine(React(openCase.Position, callsPolice));
        }

        /// <summary>A later crime lands on somebody already reacting - same timer, more cases.</summary>
        public void AddCase(CrimeCase openCase, bool visual)
        {
            held.Add(new Held { Case = openCase, Visual = visual });
        }

        IEnumerator React(Vector3 crimePosition, bool callsPolice)
        {
            var config = GameplayRuntime.Wanted;

            // Face what happened - a witness who does not turn never reads as one.
            var toCrime = PedestrianSteering.Flat(crimePosition - transform.position);
            if (toCrime.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(toCrime.normalized, Vector3.up);

            if (callsPolice)
            {
                calling = true;
                Register();
                SetActivity(PedestrianAnimation.Talk);

                var delay = Range(config ? config.reportDelayRange : new Vector2(2f, 5f));
                var reportAt = Time.time + delay;
                while (Time.time < reportAt)
                    yield return null;

                // Still breathing - the call goes through, every case at once.
                calling = false;
                SetActivity(PedestrianAnimation.None);
                for (var i = 0; i < held.Count; i++)
                    if (system)
                        system.Deliver(held[i].Case, held[i].Visual, transform.position);
            }

            // Run. Away from the crime, jittered so a crowd scatters instead of forming
            // a wedge.
            Unregister();
            var away = PedestrianSteering.Flat(transform.position - crimePosition);
            if (away.sqrMagnitude < 1e-4f)
                away = Random.insideUnitSphere;
            away.y = 0f;
            var direction = (away.normalized +
                             new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f)))
                            .normalized;

            var seconds = Range(config ? config.fleeSecondsRange : new Vector2(7f, 12f));
            // Capped in absolute terms: the pack's locomotion is a walk cycle, and a body
            // moving much faster than the fastest civilian reads as gliding however the
            // animator is driven.
            var speedCap = Mathf.Min(3.4f,
                (human ? human.maxspeed : 2.5f) * (config ? config.fleeSpeedMultiplier : 1.45f));

            yield return Flee(direction, seconds, speedCap);

            Restore();
        }

        /// <summary>
        /// The Probe/Blend/clamp loop yet again - a fleeing witness still steers around
        /// people - pointed down a direction rather than at a target.
        /// </summary>
        IEnumerator Flee(Vector3 direction, float seconds, float speedCap)
        {
            if (human && human.body != null)
                human.body.Stationary = false;

            var body = human ? human.body : null;
            var speed = 0f;
            var deadline = Time.time + seconds;
            var nextWallCheckAt = 0f;

            while (Time.time < deadline)
            {
                // A fleeing person must not grind against a facade for ten seconds - the
                // registry steers around people only. Checked on an interval, not per
                // step: a crowd of panicking witnesses is exactly when raycasts multiply.
                if (Time.time >= nextWallCheckAt)
                {
                    nextWallCheckAt = Time.time + 0.3f;
                    if (Physics.Raycast(transform.position + Vector3.up * 0.9f, direction,
                                        out var wall, 1.6f, ~(1 << PedestrianSpawner.PedestrianLayer),
                                        QueryTriggerInteraction.Ignore))
                    {
                        var slide = Vector3.ProjectOnPlane(direction, wall.normal);
                        slide.y = 0f;
                        // Commit to the new line - a panicker picks a direction and runs.
                        direction = slide.sqrMagnitude > 1e-4f ? slide.normalized : -direction;
                    }
                }

                speed = Mathf.MoveTowards(speed, speedCap, 6f * Time.deltaTime);

                var advance = speed * Time.deltaTime;
                var heading = direction;

                if (body != null)
                {
                    var obstacle = PedestrianRegistry.Probe(body, direction * 5f);
                    heading = PedestrianSteering.Blend(direction * 5f, obstacle.Push);
                    advance = Mathf.Min(advance, obstacle.AllowedAdvance);
                }

                transform.position += heading * advance;
                if (heading != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(heading, Vector3.up);

                var actual = advance / Mathf.Max(Time.deltaTime, 1e-5f);
                if (body != null)
                    body.SpeedMs = actual;
                // The walk cycle tops out around the fastest civilian; past that the clip
                // spins comically, so the FEET get the capped value and the slide is worn.
                SetSpeed(Mathf.Min(actual, 3f));

                yield return FixedStep;
            }

            if (body != null)
                body.SpeedMs = 0f;
            SetSpeed(0f);
        }

        void OnDied()
        {
            // Shot before the call went through. No delivery, no restore - PedestrianDeath
            // owns the shutdown; this component just stops existing.
            StopAllCoroutines();
            Unregister();
            Destroy(this);
        }

        /// <summary>Hand the walker back to the ambient machinery, wherever he ran to.</summary>
        void Restore()
        {
            SetActivity(PedestrianAnimation.None);
            if (human)
            {
                if (human.body != null)
                    human.body.Stationary = false;
                human.enabled = true;
                human.ResetRoute();
            }
            if (agent)
                agent.enabled = true;
            if (idler)
                idler.enabled = true;

            Destroy(this);
        }

        void OnDestroy()
        {
            Unregister();
            if (death)
                death.died -= OnDied;
        }

        void Register()
        {
            if (registered)
                return;
            registered = true;
            UI.OverlayRegistry.Register(this);
        }

        void Unregister()
        {
            if (!registered)
                return;
            registered = false;
            UI.OverlayRegistry.Unregister(this);
        }

        void SetSpeed(float metresPerSecond)
        {
            if (hasSpeedParam && animator)
                animator.SetFloat(PedestrianAnimation.SpeedHash, metresPerSecond * AnimatorSpeedScale);
        }

        void SetActivity(int value)
        {
            if (hasActivityParam && animator)
                animator.SetInteger(PedestrianAnimation.ActivityHash, value);
        }

        static float Range(Vector2 range) =>
            UnityEngine.Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));

        // ------------------------------------------------------------------ the overlay
        // The "!" over a caller's head - visible exactly for the window in which killing
        // them still cancels the report. That is information the player is meant to have.

        Transform UI.IOverlaySubject.OverlayAnchor => transform;
        float UI.IOverlaySubject.OverlayHeight => 2.2f;
        bool UI.IOverlaySubject.OverlayHidden => !calling;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Diamond;
        Color UI.IOverlaySubject.OverlayColor => new Color(1f, 0.8f, 0.15f);
        string UI.IOverlaySubject.OverlayTitle => "Witness";
        string UI.IOverlaySubject.OverlayLine => "Calling the police!";
        long UI.IOverlaySubject.OverlayKey => 1L;

        UI.MarkerStyle UI.IOverlayStyledSubject.MarkerStyle => new UI.MarkerStyle
        {
            SizeScale = 1.1f,
            Pulse = true,
            PulsePeriod = 0.5f,
            PulseAmplitude = 0.3f,
        };
    }
}
