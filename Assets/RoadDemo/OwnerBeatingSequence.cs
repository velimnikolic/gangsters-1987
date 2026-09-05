using System;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Reusable presentation of a nonfatal owner beating. The caller owns both walkers
    /// and ticks them once per frame. Business consequences belong to the caller and can
    /// be attached to FirstImpact; this presentation never closes a business or kills a man.
    /// All movement and animation use the city's walker and held-take paths.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class OwnerBeatingSequence : MonoBehaviour
    {
        public enum BeatPhase { Ready, Approach, Open, Enter, Address, Threat, ClearDoor, Extract, SquareUp,
            Combo, Punch, GroinStrike, Down, Recover, Return, Close, Complete, Cancelled }

        public AnimationClip walk, idle, threat, jab, cross, knee, hitHead, hitBody, hitHeavy, fall, recover, crawl;
        public AnimationClip combination;
        public AudioClip[] impacts;
        public BeatPhase Phase { get; private set; }
        public int ImpactCount { get; private set; }
        public bool Running => Phase > BeatPhase.Ready && Phase < BeatPhase.Complete;
        public event Action FirstImpact;
        public event Action Finished;

        CrewWalker _man, _owner;
        DoorBeat.DoorSwing _door;
        Vector3 _inside, _outside, _street, _forward, _right;
        float _phaseTime, _manSpeed, _ownerSpeed, _groundTime;
        int _punch, _comboHits;
        bool _struck, _following, _departed, _ownerPlaced;
        Vector3 _ownerMark, _manMark;
        const float ReactionSpeed = 1.3f, ReactionStart = 0.08f;
        const float ComboSpeed = 1.05f, ComboStart = 0f;
        const float JabContact = 0.39f, CrossContact = 0.64f;
        // Source time where the retargeted knee reaches the owner's pelvis height.
        const float KneeSpeed = 1.15f, KneeContact = 0.66f;
        const float FallSpeed = 1.35f, FallStart = 0.65f, FallGroundContact = 3.3f;
        const float GroundPause = 2.5f;
        const float RecoverySpeed = 1.15f, RecoveryStart = 1.2f, RecoveryEnd = 3.35f;
        const float CrawlSpeed = 0.38f, CrawlRate = 1.2f;
        AudioSource _audio;

        public bool Begin(CrewWalker man, CrewWalker owner, Transform doorway,
            Vector3 inside, Vector3 outside, Vector3 street)
        {
            if (Running || man?.Tf == null || owner?.Tf == null || man == owner ||
                man.Dead || owner.Dead || man.Riding || owner.Riding ||
                walk == null || idle == null || threat == null || jab == null || cross == null || knee == null ||
                combination == null || hitHead == null || hitBody == null || hitHeavy == null || fall == null || recover == null || crawl == null ||
                Vector3.ProjectOnPlane(outside - inside, Vector3.up).sqrMagnitude < 0.1f)
                return false;
            var manRig = man.Tf.GetComponentInChildren<Animator>();
            var ownerRig = owner.Tf.GetComponentInChildren<Animator>();
            if (manRig == null || ownerRig == null || !manRig.isHuman || !ownerRig.isHuman)
                return false;
            _man = man;
            _owner = owner;
            _inside = inside;
            _outside = outside;
            _street = street;
            _forward = Vector3.ProjectOnPlane(outside - inside, Vector3.up).normalized;
            _right = Vector3.Cross(Vector3.up, _forward);
            _manSpeed = man.Speed;
            _ownerSpeed = owner.Speed;
            _ownerMark = street - _forward * 0.75f;
            _manMark = _ownerMark - _forward * 0.65f;
            _door = new DoorBeat.DoorSwing(doorway);
            if (_audio == null)
            {
                var sound = new GameObject("Owner impact audio");
                sound.transform.SetParent(transform, false);
                _audio = sound.AddComponent<AudioSource>();
                _audio.spatialBlend = 1f;
                _audio.minDistance = 3f;
                _audio.maxDistance = 22f;
                _audio.playOnAwake = false;
            }
            ImpactCount = _punch = _comboHits = 0;
            _following = _departed = _ownerPlaced = false;
            _groundTime = 0f;
            man.Speed = 1.7f;
            owner.Speed = 1.65f;
            man.OrderAcross(outside);
            SetPhase(BeatPhase.Approach);
            return true;
        }

        void Update()
        {
            if (!Running) return;
            if (_man?.Tf == null || _owner?.Tf == null || _man.Dead || _owner.Dead ||
                !_man.Tf.gameObject.activeInHierarchy || !_owner.Tf.gameObject.activeInHierarchy)
            { Cancel(); return; }
            float dt = Time.deltaTime;
            _phaseTime += dt;
            _door.Tick(dt);
            // Missing routes/streamed objects cannot hold a gameplay actor forever.
            if (_phaseTime > 20f) { Cancel(); return; }
            switch (Phase)
            {
                case BeatPhase.Approach:
                    if (Arrived(_man, _outside)) { _door.Open(); SetPhase(BeatPhase.Open); }
                    break;
                case BeatPhase.Open:
                    if (_door.IsOpen)
                    {
                        _man.OrderThroughDoorway(_inside);
                        SetPhase(BeatPhase.Enter);
                    }
                    break;
                case BeatPhase.Enter:
                    if (Arrived(_man, _inside))
                    {
                        Stop(_man); Stop(_owner);
                        SetPhase(BeatPhase.Address);
                    }
                    break;
                case BeatPhase.Address:
                    _man.WatchToward(_owner.Tf.position - _man.Tf.position);
                    _owner.WatchToward(_man.Tf.position - _owner.Tf.position);
                    if (Facing(_man, _owner.Tf.position) && Facing(_owner, _man.Tf.position) &&
                        !_man.Joining && !_owner.Joining)
                    {
                        _man.PlayTake(threat, false, 1f, 0f);
                        _owner.PlayTake(idle, true, 1f, 0f);
                        SetPhase(BeatPhase.Threat);
                    }
                    break;
                case BeatPhase.Threat:
                    if (_phaseTime >= Mathf.Min(1.15f, threat.length))
                    {
                        _man.EndTake();
                        _man.OrderThroughDoorway(_inside + _right * 0.8f - _forward * 0.3f);
                        SetPhase(BeatPhase.ClearDoor);
                    }
                    break;
                case BeatPhase.ClearDoor:
                    if (Arrived(_man, _inside + _right * 0.8f - _forward * 0.3f))
                    {
                        Stop(_man);
                        _man.PlayTake(idle, true, 1f, 0f);
                        _owner.EndTake();
                        _owner.Speed = 1.35f;
                        _owner.OrderThroughDoorway(_ownerMark);
                        SetPhase(BeatPhase.Extract);
                    }
                    break;
                case BeatPhase.Extract:
                    // Let the owner clear the doorway first. Both walk forwards;
                    // the walker alone owns their heading throughout the passage.
                    if (!_following && Vector3.Dot(_owner.Tf.position - _inside, _forward) > 0.8f)
                    {
                        _following = true;
                        _man.EndTake();
                        _man.Speed = 1.55f;
                        _man.OrderThroughDoorway(_manMark);
                    }
                    // The owner turns as soon as he reaches the pavement; he needn't
                    // wait motionless for the man following him through the door.
                    if (!_ownerPlaced && Arrived(_owner, _ownerMark))
                    {
                        _ownerPlaced = true;
                        Stop(_owner);
                    }
                    if (_ownerPlaced) _owner.WatchToward(_inside - _owner.Tf.position);
                    if (_following && _ownerPlaced && Arrived(_man, _manMark))
                    {
                        Stop(_man);
                        _man.WatchToward(_owner.Tf.position - _man.Tf.position);
                        _owner.WatchToward(_inside - _owner.Tf.position);
                        _door.Close();
                        SetPhase(BeatPhase.SquareUp);
                    }
                    break;
                case BeatPhase.SquareUp:
                    // Use the shared standing turn, including its footwork. Wait for
                    // it to finish instead of turning the root over a walking pose.
                    _man.WatchToward(_owner.Tf.position - _man.Tf.position);
                    _owner.WatchToward(_inside - _owner.Tf.position);
                    if (Facing(_owner, _inside) && Facing(_man, _owner.Tf.position) &&
                        !_owner.Joining && !_man.Joining)
                    {
                        _owner.PlayTake(idle, true, 1f, 0f);
                        _man.PlayTakeRange(combination, ComboStart, 1.35f, false, ComboSpeed, 0.2f);
                        SetPhase(BeatPhase.Combo);
                    }
                    break;
                case BeatPhase.Combo:
                    // The first pair keeps the source take's weight transfer and
                    // follow-through. Reactions are keyed to its actual hand contacts.
                    if (_comboHits == 0 && _man.TakeTime >= JabContact)
                    {
                        Hit(hitBody, 1.55f, 0.055f);
                        _comboHits++;
                    }
                    if (_comboHits == 1 && _man.TakeTime >= CrossContact)
                    {
                        Hit(hitHeavy, ReactionSpeed, 0.065f);
                        _comboHits++;
                    }
                    if (_comboHits == 2 && _phaseTime >=
                        (CrossContact - ComboStart) / ComboSpeed +
                        (hitHeavy.length - ReactionStart) / ReactionSpeed - jab.length / 1.15f * 0.34f + 0.12f)
                    {
                        _punch = 2;
                        StartPunch();
                    }
                    else if (_comboHits == 2 && _man.TakeFinished) Guard();
                    break;
                case BeatPhase.Punch:
                    var clip = _punch % 2 == 0 ? jab : cross;
                    float punchSpeed = _punch == 2 ? 1.15f : 1.05f;
                    float duration = clip.length / punchSpeed;
                    var reaction = _punch == 3 ? hitHeavy : _punch % 2 == 0 ? hitHead : hitBody;
                    float impactAt = duration * 0.34f;
                    if (!_struck && _man.Take == clip && _man.TakeTime >= clip.length * 0.34f)
                    {
                        _struck = true;
                        Hit(reaction, ReactionSpeed, 0.085f);
                    }
                    // Let the recoil finish before the next contact. Its attack can
                    // wind up during recovery, so the exchange keeps its pace.
                    float recoil = (reaction.length - ReactionStart) / ReactionSpeed;
                    float nextWindup = _punch == 3 ? KneeContact / KneeSpeed :
                        cross.length / 1.05f * 0.34f;
                    if (_phaseTime >= Mathf.Max(duration + 0.06f, impactAt + recoil - nextWindup + 0.08f))
                    {
                        if (++_punch < 4) StartPunch();
                        else
                        {
                            _struck = false;
                            _man.PlayTake(knee, false, KneeSpeed, 0f, blendSeconds: 0.18f, continuePrevious: true);
                            SetPhase(BeatPhase.GroinStrike);
                        }
                    }
                    else if (_man.Take == clip && _man.TakeFinished) Guard();
                    break;
                case BeatPhase.GroinStrike:
                    if (_man.TakeTime >= KneeContact)
                    {
                        ImpactCount++;
                        _owner.PlayTake(fall, false, FallSpeed, FallStart, blendSeconds: 0.12f, continuePrevious: true);
                        if (impacts != null && impacts.Length > 0)
                        {
                            _audio.transform.position = _owner.Tf.position;
                            _audio.PlayOneShot(impacts[impacts.Length - 1], 0.9f);
                        }
                        SetPhase(BeatPhase.Down);
                    }
                    break;
                case BeatPhase.Down:
                    // Keep the attack's follow-through while the victim folds and falls.
                    if (_man.Take == knee && _man.TakeFinished) Guard();
                    // Leave at ground contact, independently of the owner's rest.
                    // The remaining fall frames settle his body into the held pose.
                    if (!_departed && _owner.TakeTime >= FallGroundContact)
                    {
                        _departed = true;
                        // Spend the remaining small movements on the ground slowly
                        // instead of freezing the owner in the final fall frame.
                        _owner.SetTakeSpeed(0.4f);
                        _man.EndTake();
                        _man.StopWatching();
                        _man.Speed = 1.6f;
                        _man.OrderAcross(_street + _right * 4.5f + _forward * 0.5f);
                    }
                    if (_departed) _groundTime += dt;
                    if (_groundTime >= GroundPause && _owner.TakeFinished)
                    {
                        // Skip the recovery clip's duplicate lying hold. Give the
                        // curled fall pose time to roll into the prone push-up.
                        _owner.PlayTakeRange(recover, RecoveryStart, RecoveryEnd, false, RecoverySpeed, 0.65f);
                        _door.Open();
                        SetPhase(BeatPhase.Recover);
                    }
                    break;
                case BeatPhase.Recover:
                    if (_owner.TakeFinished && _door.IsOpen)
                    {
                        _owner.Speed = 0.1f;
                        _owner.OrderThroughDoorway(_inside - _forward * 0.3f);
                        _owner.PlayTake(crawl, true, CrawlRate, 0f,
                            allowMovement: true, blendSeconds: 0.35f, continuePrevious: true);
                        SetPhase(BeatPhase.Return);
                    }
                    break;
                case BeatPhase.Return:
                    _owner.Speed = Mathf.MoveTowards(_owner.Speed, CrawlSpeed, dt * 0.6f);
                    if (Arrived(_owner, _inside - _forward * 0.3f))
                    {
                        // Keep the current supported pose; don't jump to an arbitrary
                        // crawl frame when he reaches the far side of the threshold.
                        _owner.SetTakeSpeed(0f);
                        _owner.EndDoorway();
                        _owner.OrderToPoint(_owner.Tf.position);
                        _door.Close();
                        SetPhase(BeatPhase.Close);
                    }
                    break;
                case BeatPhase.Close:
                    if (_door.IsClosed)
                    {
                        Stop(_man); _man.Speed = _manSpeed;
                        _owner.Speed = _ownerSpeed;
                        SetPhase(BeatPhase.Complete);
                        Finished?.Invoke();
                    }
                    break;
            }
        }

        void StartPunch()
        {
            _struck = false;
            _man.PlayTake(_punch % 2 == 0 ? jab : cross, false, _punch == 2 ? 1.15f : 1.05f, 0f,
                blendSeconds: 0.16f, continuePrevious: true);
            SetPhase(BeatPhase.Punch);
        }

        void Guard() => _man.PlayTakeRange(combination, 1.05f, 1.95f, true, 0.85f, 0.18f);

        void Hit(AnimationClip reaction, float speed, float blend)
        {
            ImpactCount++;
            _owner.PlayTake(reaction, false, speed, ReactionStart, blendSeconds: blend, continuePrevious: true);
            CrewGore.BeatingHit(_owner, _man.Tf.position, _street.y, ImpactCount / 4f);
            if (impacts != null && impacts.Length > 0)
            {
                _audio.transform.position = _owner.Tf.position;
                _audio.PlayOneShot(impacts[(ImpactCount - 1) % impacts.Length], ImpactCount < 3 ? 0.65f : 0.8f);
            }
            if (ImpactCount == 1) FirstImpact?.Invoke();
        }

        static bool Facing(CrewWalker actor, Vector3 at)
        {
            var toward = Vector3.ProjectOnPlane(at - actor.Tf.position, Vector3.up);
            return toward.sqrMagnitude < 0.001f || Vector3.Angle(actor.Tf.forward, toward) < 6f;
        }

        static bool Arrived(CrewWalker actor, Vector3 at) =>
            Vector3.ProjectOnPlane(actor.Tf.position - at, Vector3.up).sqrMagnitude < 0.0324f;
        static void Stop(CrewWalker actor)
        {
            actor.EndTake(); actor.EndDoorway(); actor.StopWatching(); actor.OrderToPoint(actor.Tf.position);
        }
        void SetPhase(BeatPhase phase) { Phase = phase; _phaseTime = 0f; }
        void Restore()
        {
            if (_man?.Tf != null) { Stop(_man); _man.Speed = _manSpeed; }
            if (_owner?.Tf != null) { Stop(_owner); _owner.Speed = _ownerSpeed; }
        }
        public void Cancel()
        {
            if (!Running && Phase != BeatPhase.Complete) return;
            Restore(); _door?.SnapClosed(); SetPhase(BeatPhase.Cancelled);
        }
        void OnDisable() { Cancel(); }
    }
}
