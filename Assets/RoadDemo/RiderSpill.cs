using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A man off a machine: through the air, into the road, and then either lying there
    /// or getting back up.
    ///
    /// It is the other half of <see cref="BikeSpill"/> and it exists because a rider is
    /// not a thing that can simply be deleted when he is shot. A pillion who takes a
    /// round at fifty kilometres an hour leaves the bike carrying the bike's speed, and
    /// the picture that reads is the one where the machine goes on and he does not. The
    /// city already knew that a dead rider must come off (DemoCrews dismounts him); what
    /// it had no way of showing was the coming off.
    ///
    /// WHAT DRIVES WHAT. The flight is the TRANSFORM's - a plain ballistic arc with a
    /// tumble on it, no rigidbody, the same hand-driven kinematics as every other moving
    /// thing in this project. The clip only has to make the body look like a body while
    /// that happens. So there is no thrown-off-a-motorcycle animation anywhere and none
    /// is wanted: the pack's long fall covers the air, the pack's hard landing covers
    /// getting up, and the crowd's own death covers not getting up.
    ///
    /// THE ONE THING THAT MUST HAPPEN FIRST is that <see cref="BikePose"/> stops writing
    /// him. The pose puts his hips on the saddle and his fists on the bars every
    /// LateUpdate, off the bike's own transform - so a man who is thrown but whose pose
    /// is still live is snapped back onto the machine the same frame, every frame, and
    /// the throw is invisible. <see cref="Throw"/> disables it before it touches
    /// anything else.
    /// </summary>
    public sealed class RiderSpill : MonoBehaviour
    {
        /// <summary>What a spill needs a body to be, and no more than that.
        ///
        /// There are two kinds of man on a motorcycle in this project and they animate
        /// nothing like each other. The traffic's rider (<see cref="BikeOccupant"/>) is
        /// a dumb body with a two-input mixer whose whole life is "play this clip". The
        /// outfit's hood (<see cref="CrewWalker"/>) carries the crowd's nineteen-slot
        /// pose graph, walks, fights and dies in it, and has to be handed back to it
        /// afterwards. Everything a FALL needs is in this interface - somewhere to move,
        /// a pose to switch off, and a way to put a clip on him - so the flight, the
        /// road and the getting up are written once and both kinds spill the same.
        ///
        /// The bench next door (Assets/BikeDemo) is what the numbers were tuned against
        /// with the dumb kind, and the street then gets exactly what the bench shows.</summary>
        public interface IBody
        {
            /// <summary>The transform the spill throws. His root, not a bone.</summary>
            Transform Root { get; }

            /// <summary>The riding pose that has to stop writing him. May be null.</summary>
            BikePose Pose { get; }

            /// <summary>What is on him this instant, or null.</summary>
            AnimationClip Playing { get; }

            /// <summary>A one-shot that has run out and is being held on its last
            /// frame - what "he has finished dying" means.</summary>
            bool Finished { get; }

            /// <summary>His death is ALREADY running - he was killed in the saddle by
            /// something that owns his animation (a crew man's Kill plays the crowd's
            /// own death). Such a man comes off limp whatever the dice say, and nothing
            /// here plays a clip over the top of it.</summary>
            bool AlreadyDying { get; }

            /// <summary>Play this on him. A body that cannot (a man whose own death is
            /// running) simply does not, which is this project's rule for every
            /// optional asset applied to an optional actor.</summary>
            void Play(AnimationClip clip, bool loop, float fade, float speed, float at);
        }

        /// <summary>The four clips a spill can want. Any of them may be null - a missing
        /// one simply means that beat is played in whatever was already on him, the
        /// project's rule for every optional asset.</summary>
        public struct Wardrobe
        {
            /// <summary>In the air.</summary>
            public AnimationClip Fall;
            /// <summary>Hitting the road and standing up out of it.</summary>
            public AnimationClip Land;
            /// <summary>On his feet again.</summary>
            public AnimationClip Idle;
            /// <summary>Not getting up.</summary>
            public AnimationClip Death;

            /// <summary>Every death the crowd owns, if the caller has them. One clip
            /// dealt to every man who comes off a machine is the fault the crowd already
            /// solved for its own dead (CrewKit.Deaths): two hoods shot off two bikes in
            /// one pass land in the same pose at the same rate and the eye reads them as
            /// one thing copied. A spill draws its own out of here; <see cref="Death"/>
            /// is what it falls back on when the pool is empty.</summary>
            public AnimationClip[] Deaths;

            /// <summary>The wardrobe the demos use: the pack's fall and landing, the
            /// crowd's stand, and the crowd's own deaths (the Mixamo crumple, three and
            /// a half seconds of it, and the library's brisker Death01).</summary>
            public static Wardrobe Stock()
            {
                var pool = CrewKit.Deaths;
                var deaths = new AnimationClip[pool.Count];
                for (int i = 0; i < pool.Count; i++) deaths[i] = pool[i];
                return new Wardrobe
                {
                    Fall = CrewKit.Fall,
                    Land = CrewKit.Land,
                    Idle = CrewKit.StockIdle,
                    Death = CrewKit.Clips().Death,
                    Deaths = deaths,
                };
            }
        }

        // ------------------------------------------------------------------ the numbers
        //
        // Every one of them is a static so the bench can push it about while Play runs
        // (Assets/BikeDemo) - the same contract BikeBody and BikePose keep. They are set
        // by eye and are meant to be argued with.

        /// <summary>Metres a second squared. Heavier than the real thing on purpose: a
        /// body thrown off a machine has to be down and still inside a second or the
        /// pass has ridden out of shot while he is still in the air.</summary>
        public static float Gravity = 17f;

        /// <summary>How he leaves the machine: up off the saddle, out to the side he
        /// falls, and the share of the machine's own pace he carries with him. A man
        /// does not stop when the bike does - that is the whole point of the picture.</summary>
        public static float ThrowUp = 3.0f, ThrowOut = 1.8f, ThrowAhead = 0.9f;

        /// <summary>Metres a second squared off his speed once he is on the road. Cloth
        /// and skin on tarmac, not a tyre: it is a hard number and he does not slide far.</summary>
        public static float Drag = 6.5f;

        /// <summary>Degrees a second he turns over in the air. The living man goes head
        /// over heels; a man who is being killed by the clip he is playing is only
        /// turned about his own axis, or the crumple happens sideways in mid-air and
        /// reads as a rag rather than a man.</summary>
        public static float TumbleRate = 300f, DeadTumbleRate = 90f;

        /// <summary>How long he lies there before he starts to get up, and the seconds
        /// either side of it that are drawn per man. A quarter of a second was the old
        /// number and it was the whole of what was wrong with a man walking away from a
        /// spill: he touched the road and stood, which is not a man picking himself up,
        /// it is a machine resetting. A second and a bit of lying still is what makes
        /// the getting up read as effort, and the jitter is what stops two men off one
        /// bike rising in step.</summary>
        public static float LieThere = 1.1f, LieJitter = 0.45f;

        /// <summary>The share of the death clip that is skipped when a man hits the
        /// road, and how fast the rest of it is played.
        ///
        /// The crumple is authored from a man standing on his feet: it staggers, it
        /// folds, and only its last half is a body going into the ground. A rider who
        /// has just come out of a tumble is already half way down, so playing it from
        /// the top gives him a stagger he has no business doing at thirty miles an hour
        /// on his back. Starting it part way in and running it a little fast is the
        /// impact - the fold and the sprawl, and nothing before them.</summary>
        public static float SprawlFrom = 0.42f, SprawlSpeed = 1.3f;

        /// <summary>How fast the landing take is played when he gets up, and the blend
        /// into it. Both are slower than a landing's own numbers on purpose: the clip
        /// was authored as a man dropping onto his feet, and what it is being asked to
        /// do here is carry a man off the road, so it wants the time. The fade is long
        /// enough that the sprawl he is held in gathers into the crouch rather than
        /// cutting to it.</summary>
        public static float RiseSpeed = 0.7f, RiseFade = 0.3f;

        /// <summary>The share either side of one that a death is played at. Two men
        /// dying at exactly one rate is the same fault as two men dying in one clip,
        /// and it is cheaper to fix.</summary>
        public static float DeathSpeedJitter = 0.15f;

        /// <summary>The chance a dead man is limp before he lands rather than after.
        ///
        /// Both readings are true and the pass wants both. LIMP: the round kills him in
        /// the saddle, the death clip runs while he is in the air, and what comes off
        /// the machine is already a body - it turns about its own axis and pours into
        /// the road. LATE: he is hit, he comes off alive-looking with his arms out, and
        /// it is the road that finishes him - he goes head over heels like a man who
        /// means to land, and only crumples when he arrives.</summary>
        public static float LimpChance = 0.5f;

        /// <summary>Seconds it takes him to come upright once he is down. NOT zero, and
        /// this is the one number here that is a fix for something rather than a guess:
        /// a man tumbling at three hundred degrees a second arrives at the road at
        /// whatever angle he happens to have reached, and both clips that follow are
        /// authored from a body standing up. Snapping him upright on the frame he lands
        /// is a body flicking through ninety degrees in one frame, which reads as a
        /// glitch rather than a landing; a fifth of a second of it reads as him
        /// finishing the roll.</summary>
        public static float UprightIn = 0.18f;

        /// <summary>How much of the landing clip is the getting-up part. The pack's hard
        /// landing lands him and stands him in one take, so the stand is simply what is
        /// left after it; when the clip is missing he stands at once.</summary>
        public static float StandFade = 0.25f;

        // ------------------------------------------------------------------ the state

        public enum Phase { Air, Road, Up }

        public Phase Where { get; private set; } = Phase.Air;

        /// <summary>He is on the ground - the machine may ride on without him.</summary>
        public bool Down => Where != Phase.Air;

        /// <summary>He was thrown to die, and is not getting up.</summary>
        public bool Dying { get; private set; }

        /// <summary>He was dead before he left the machine - the death is running in the
        /// air. False means he is being killed by the road (see <see cref="LimpChance"/>).</summary>
        public bool Limp { get; private set; }

        /// <summary>Nothing more is going to happen to him: he is standing, or he has
        /// finished dying and is lying still. What a scene watches to know a run is
        /// over.</summary>
        public bool Settled { get; private set; }

        IBody _man;
        Wardrobe _wardrobe;
        Vector3 _vel, _spinAxis;
        Quaternion _face = Quaternion.identity;
        Quaternion _fromRoll, _upright;   // the tumble he landed in, and the way he ends up
        float _ground, _since, _upT;
        AnimationClip _death;             // the one he drew, not the one everybody plays
        float _deathSpeed = 1f, _lie;     // his own rate, his own time in the road
        bool _rising;

        /// <summary>Take this man off the machine and throw him.
        ///
        /// <paramref name="velocity"/> is what the machine was doing; the throw is built
        /// off it. <paramref name="dies"/> decides which of the two endings he gets.
        /// <paramref name="world"/> is what he is re-parented to - anything that is not
        /// the bike, because the bike is about to go somewhere he is not.</summary>
        public static RiderSpill Throw(IBody man, Vector3 velocity, bool dies,
            Wardrobe wardrobe, Transform world, float side = 1f, float groundY = 0f)
        {
            if (man == null || man.Root == null) return null;
            if (man.Root.GetComponent<RiderSpill>() != null) return null;   // already off it

            var pose = man.Pose;
            var right = Vector3.right;
            var flat = new Vector3(velocity.x, 0f, velocity.z);
            var ahead = flat.sqrMagnitude > 0.01f ? flat.normalized : man.Root.forward;
            if (pose != null)
            {
                right = pose.transform.right;
                // FIRST, and before anything is moved: the pose owns his hips, his
                // rotation and his four limbs every LateUpdate. Left live, it puts him
                // straight back on the saddle and nothing below this line is ever seen.
                pose.Rider = null;
                pose.enabled = false;
            }

            var tf = man.Root;
            tf.localScale = Vector3.one;   // BikePose may have taken him down to fit the saddle
            tf.SetParent(world, worldPositionStays: true);

            var spill = tf.gameObject.AddComponent<RiderSpill>();
            spill._man = man;
            spill._wardrobe = wardrobe;
            spill.Dying = dies;
            spill._ground = groundY;
            spill._face = Quaternion.LookRotation(ahead, Vector3.up);
            spill._vel = flat * ThrowAhead + Vector3.up * ThrowUp + right * (Mathf.Sign(side) * ThrowOut);

            // his own death, his own rate, his own time in the road: what stops two men
            // off one machine reading as one man drawn twice
            spill._death = Draw(wardrobe);
            spill._deathSpeed = 1f + Random.Range(-DeathSpeedJitter, DeathSpeedJitter);
            spill._lie = Mathf.Max(0f, LieThere + Random.Range(-LieJitter, LieJitter));
            // A man whose own death is already running left the saddle a body, whatever
            // the dice would have said (IBody.AlreadyDying): the street shoots a hood off
            // a pillion and CrewWalker.Kill has him crumpling before this is ever called.
            spill.Limp = dies && (man.AlreadyDying || Random.value < LimpChance);

            // head over heels about his own right - which is what a living man does and
            // also what a man the ROAD is going to kill does, because until he lands
            // there is nothing to tell them apart. Only the man who left the saddle dead
            // turns about his own axis: a crumple thrown end over end reads as a rag.
            spill._spinAxis = spill.Limp ? Vector3.up : right;
            if (spill.Limp) spill._man.Play(spill._death, false, 0.08f, spill._deathSpeed, 0f);
            else spill._man.Play(wardrobe.Fall, false, 0.08f, 1f, 0f);
            return spill;
        }

        /// <summary>One death out of the pool, or the wardrobe's single one when there
        /// is no pool - the crowd's rule for every optional asset.</summary>
        static AnimationClip Draw(Wardrobe wardrobe)
        {
            var pool = wardrobe.Deaths;
            if (pool == null || pool.Length == 0) return wardrobe.Death;
            var drawn = pool[Random.Range(0, pool.Length)];
            return drawn != null ? drawn : wardrobe.Death;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _since += dt;

            switch (Where)
            {
                case Phase.Air: Fly(dt); break;
                case Phase.Road: Lie(dt); break;
                case Phase.Up: Stand(dt); break;
            }
        }

        void Fly(float dt)
        {
            _vel.y -= Gravity * dt;
            transform.position += _vel * dt;
            float rate = Limp ? DeadTumbleRate : TumbleRate;
            _face = Quaternion.AngleAxis(rate * dt, _spinAxis) * _face;
            transform.rotation = _face;
            if (transform.position.y > _ground) return;

            // the road. He keeps what he had along it and loses the rest.
            var at = transform.position;
            at.y = _ground;
            transform.position = at;
            _vel.y = 0f;
            Where = Phase.Road;
            _since = 0f;

            // Upright again, facing the way he was going - over UprightIn seconds, not
            // on this frame. Both clips that follow are authored from a body standing on
            // the ground, so the tumble has to end somewhere, and the end of it is the
            // last part of the fall rather than a jump cut.
            var flat = new Vector3(_vel.x, 0f, _vel.z);
            _upright = flat.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                : Quaternion.Euler(0f, _face.eulerAngles.y, 0f);
            _fromRoll = transform.rotation;
            _upT = 0f;

            // What arriving looks like. A man - living or dying - goes INTO the road and
            // stays there; the road is not a landing mat. The only one who does nothing
            // here is the man who was already dead in the air, because his crumple has
            // been running since he left the saddle and is where it should be.
            if (Limp) return;
            Sprawl();
        }

        /// <summary>Into the road: the back half of a death clip, played a little fast.
        ///
        /// The living man and the man the road kills sprawl in exactly the same way and
        /// that is not a shortcut - up to the moment he stops moving they ARE the same
        /// picture, and what tells them apart is which one gets up afterwards. It is
        /// also the fix for the old landing, where the pack's hard landing was started
        /// on the frame he touched down: that take stands a man on his feet inside a
        /// second and a half, so he never lay in the road at all.</summary>
        void Sprawl()
        {
            if (_man == null) return;
            if (_death == null)
            {
                // no death anywhere in the wardrobe: the old behaviour, which at least
                // puts him on his feet rather than leaving him in the fall
                _man.Play(_wardrobe.Land, false, 0.08f, 1f, 0f);
                return;
            }
            float len = Mathf.Max(0.01f, _death.length);
            _man.Play(_death, false, 0.07f,
                Mathf.Max(0.1f, SprawlSpeed * _deathSpeed),
                len * Mathf.Clamp01(SprawlFrom));
        }

        void Lie(float dt)
        {
            Slide(dt);
            Upright(dt);
            if (Dying)
            {
                // A dead man is settled when the fall has finished falling. The clip is
                // held on its last frame by the player itself, so this is a read and
                // never a thing that has to be stopped.
                if (_man == null || _man.Finished || _man.Playing == null) Settled = true;
                return;
            }
            // He lies there. NOT a formality: the sprawl is being held on its last frame
            // while this runs, so this is the second of stillness that the getting up is
            // read against, and without it there is nothing to get up FROM.
            if (_since < _lie) return;
            Where = Phase.Up;
            _since = 0f;
            _rising = false;
        }

        void Stand(float dt)
        {
            Slide(dt);
            Upright(dt);
            if (Settled) return;

            // off the road. The landing take is the only thing in the project that
            // stands a man up out of the ground, and it is played SLOW and blended into
            // over a long fade - it was authored as a drop onto the feet, and it is
            // being asked to carry a man who has been lying down.
            if (!_rising)
            {
                _rising = true;
                if (_man != null && _wardrobe.Land != null)
                {
                    _man.Play(_wardrobe.Land, false, RiseFade,
                        Mathf.Max(0.1f, RiseSpeed), 0f);
                    return;
                }
            }

            bool done = _man == null || _man.Playing == null || _man.Playing == _wardrobe.Idle ||
                        _man.Finished;
            if (!done) return;
            if (_man != null && _wardrobe.Idle != null && _man.Playing != _wardrobe.Idle)
                _man.Play(_wardrobe.Idle, true, StandFade, 1f, 0f);
            Settled = true;
        }

        void Upright(float dt)
        {
            if (_upT >= 1f) return;
            _upT = UprightIn <= 0.001f ? 1f : Mathf.Min(1f, _upT + dt / UprightIn);
            transform.rotation = Quaternion.Slerp(_fromRoll, _upright, _upT);
        }

        void Slide(float dt)
        {
            var flat = new Vector3(_vel.x, 0f, _vel.z);
            float speed = flat.magnitude;
            if (speed < 0.05f) { _vel = Vector3.zero; return; }
            speed = Mathf.Max(0f, speed - Drag * dt);
            _vel = flat.normalized * speed;
            transform.position += _vel * dt;
        }
    }
}
