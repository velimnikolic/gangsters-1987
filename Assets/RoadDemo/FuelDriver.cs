using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The man who got out of the car: a walker with no pavement under him, sent from
    /// point to point across a forecourt by whoever is running his errand
    /// (<see cref="FuelCustomer"/>).
    ///
    /// The crowd's own base drives the body - the same playable graph, the same walk
    /// and idle, the same crowd avoidance - and this only says WHERE. The stepping is
    /// the crews' free stride rather than the pavement graph's: a straight line at the
    /// goal, steered round whatever the scene has blocked (WalkObstacles.Steer), which
    /// is what takes him round the back of his own car instead of through it.
    /// </summary>
    public sealed class FuelDriver : PedestrianAgent
    {
        /// <summary>How near the goal counts as arrived. A pace: he is going to stand
        /// and do something there, not hit a mark.</summary>
        const float Reach = 0.45f;

        /// <summary>His shoulders, for the steering - the crowd's own walking radius.</summary>
        const float Shoulders = 0.35f;

        /// <summary>How far ahead the steering looks. Short: a forecourt is a tight
        /// place and a man who plans four metres ahead on it never stops steering.</summary>
        const float LookAhead = 2.2f;

        Vector3 _goal;
        bool _walking;
        Vector3 _face;      // what he turns to once he is there, or zero for wherever he stopped
        int _side;          // which way round an obstacle he settled on, held between frames

        /// <summary>What he is up to, for the trace and the audit.</summary>
        public string Doing = "waiting";

        public bool Arrived => !_walking;

        /// <summary>Send him to a point. He turns up facing <paramref name="face"/> when
        /// one is given (the pump, the shop door), else facing the way he walked in.</summary>
        public void WalkTo(Vector3 point, Vector3 face = default)
        {
            _goal = point;
            _goal.y = Tf != null ? Tf.position.y : point.y;
            _face = face;
            _walking = true;
            _side = 0;
        }

        /// <summary>Stop him where he stands.</summary>
        public void Halt()
        {
            _walking = false;
        }

        /// <summary>Out of sight - inside the shop, or back in the driving seat. The
        /// graph is suspended with him: a body nobody can see is not worth a blend.</summary>
        public void Show(bool shown)
        {
            if (Tf == null) return;
            if (Tf.gameObject.activeSelf == shown) return;
            Tf.gameObject.SetActive(shown);
            Suspend(!shown);
        }

        public bool Shown => Tf != null && Tf.gameObject.activeSelf;

        /// <summary>Something to do with his hands while he stands there. Ignored while
        /// he is already playing one, so a call a frame is harmless.</summary>
        public void Fidget()
        {
            if (Acting) return;
            PlayAction(CrewKit.Fidgets);
        }

        public void TickDriver(float dt)
        {
            if (Tf == null || !Tf.gameObject.activeSelf) return;
            if (!_walking)
            {
                if (_face.sqrMagnitude > 1e-4f) TurnToward(_face, 220f, dt);
                ReadCrowd(dt, Tf.forward);
                BlendLocomotion(dt, false);
                return;
            }

            var here = Tf.position;
            var to = _goal - here;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < Reach)
            {
                _walking = false;
                BlendLocomotion(dt, false);
                return;
            }

            var want = to / dist;
            ReadCrowd(dt, want);
            // round the parked bodies and the island, and round whoever is in his way:
            // the crowd's shove is lateral metres, the same units the steer answers in
            var line = WalkObstacles.Steer(here, want, Tf.forward, Shoulders, LookAhead,
                                           ref _side, out float clear);
            var right = new Vector3(line.z, 0f, -line.x);
            var step = line + right * Mathf.Clamp(CrowdPush, -1f, 1f);
            if (step.sqrMagnitude > 1e-6f) step.Normalize();

            float pace = Speed * Mathf.Clamp01(CrowdHold);
            float run = Mathf.Min(pace * dt, Mathf.Max(0f, clear), dist);
            Tf.position = here + step * run;
            TurnToward(step, 360f, dt);
            BlendLocomotion(dt, run > 0.001f);
        }

        protected override string TraceState() => Doing;
        protected override bool Moving => _walking;
    }
}
