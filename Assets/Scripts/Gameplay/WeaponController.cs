using System.Collections;
using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The half of a gunshot the demo never needed: hit resolution. GunmanAim plays the
    /// shot (animation, and the bang through CityAudioDirector); this class decides what
    /// the shot DID - flash at the muzzle, wait out the trigger-to-impact beat, and put the
    /// damage on the target if the shot still stands - and announces both the shot and any
    /// kill on CrimeFeed, which is where Phase 2's witnesses will be listening.
    ///
    /// Damage is deterministic, not a physics hitscan. At four metres against a 0.2m
    /// animated capsule a raycast adds only failure modes (the capsule is disabled on
    /// death-in-progress, mid-stride it swings past the ray); what makes a shot honest is
    /// the range + line-of-sight check, and that is re-run at impact time, so a target that
    /// made a corner during the 0.12s is genuinely missed.
    ///
    /// The impact rides a fixed delay rather than an animation event because the controller
    /// is a GENERATED asset - CityAssetBootstrap rebuilds it, and any event added to the
    /// asset would be silently regenerated away.
    /// </summary>
    public sealed class WeaponController : MonoBehaviour
    {
        /// <summary>Chest height on the target - GunmanAim's own aim point convention.</summary>
        const float TargetChestHeight = 1.3f;

        /// <summary>Slack on the impact-time range check: a step or two of drift between
        /// trigger and impact is still a hit, a dash around a corner is not.</summary>
        const float ImpactRangeSlack = 1.5f;

        /// <summary>Everything except the pedestrian layer blocks a shot - walls and props
        /// do, the crowd does not. The player shares layer 8, so his own capsule can never
        /// shadow his own muzzle.</summary>
        static readonly int LosBlockers = ~(1 << PedestrianSpawner.PedestrianLayer);

        WeaponSocket socket;
        Light flash;

        /// <summary>The player's bought-weapon numbers; stays null on every police rig,
        /// which is what keeps a purchase from arming the whole force. Resolved lazily,
        /// not in Awake - GameplayBootstrap AddComponents the arsenal after this component
        /// already ran its Awake.</summary>
        PlayerArsenal arsenal;

        PlayerArsenal Arsenal => arsenal ? arsenal : arsenal = GetComponent<PlayerArsenal>();

        CombatConfig Combat => GameplayRuntime.Combat;

        /// <summary>
        /// The two waits every shot yields, made once per value rather than per shot: a
        /// shootout is dozens of shots a minute and each was three fresh allocations.
        /// Re-made only when the config number moves, so a tuned asset still lands live.
        /// </summary>
        WaitForSeconds hitWait;
        float hitWaitSeconds = -1f;
        WaitForSeconds flashWait;
        float flashWaitSeconds = -1f;

        WaitForSeconds HitWait(CombatConfig combat) =>
            Cached(ref hitWait, ref hitWaitSeconds, combat ? combat.hitDelay : 0.12f);

        WaitForSeconds FlashWait(CombatConfig combat) =>
            Cached(ref flashWait, ref flashWaitSeconds, combat ? combat.flashSeconds : 0.06f);

        static WaitForSeconds Cached(ref WaitForSeconds wait, ref float seconds, float wanted)
        {
            if (wait == null || seconds != wanted)
            {
                wait = new WaitForSeconds(wanted);
                seconds = wanted;
            }
            return wait;
        }

        void Awake()
        {
            socket = GetComponent<WeaponSocket>();
            BuildFlash();
        }

        /// <summary>
        /// The Shooting Demo's muzzle flash, made permanent: one point light, created once,
        /// disabled, moved to the muzzle per shot. Not parented to the weapon - the muzzle
        /// rides the hand bone through the recoil, and the flash belongs where the shot
        /// LEFT, not where the barrel went afterwards.
        /// </summary>
        void BuildFlash()
        {
            var holder = new GameObject("Muzzle Flash");
            holder.transform.SetParent(transform, false);

            // Plain AddComponent on a fresh object - Light is native, and the
            // GetComponent-??-AddComponent idiom silently skips natives in Unity 6.
            flash = holder.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.enabled = false;
        }

        /// <summary>
        /// True when nothing solid stands between the muzzle and the target's chest.
        /// </summary>
        public bool HasLineOfSight(InteractableNpc target)
        {
            if (!target)
                return false;

            var from = socket
                ? socket.Muzzle
                : transform.position + Vector3.up * 1.4f;
            var to = target.transform.position + Vector3.up * TargetChestHeight;

            var direction = to - from;
            var distance = direction.magnitude;
            if (distance < 0.2f)
                return true;

            return !Physics.Raycast(from, direction / distance, distance - 0.1f,
                                    LosBlockers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Called alongside GunmanAim.Fire: flash now, impact after the beat. The caller
        /// owns cadence and aiming; this owns consequences.
        /// </summary>
        public void ResolveShot(InteractableNpc target)
        {
            if (!target)
                return;

            CrimeFeed.Report(new CrimeEvent(
                CrimeKind.Gunshot, MuzzleWorld, transform, target.transform));

            StartCoroutine(ShotRoutine(target));
        }

        Vector3 MuzzleWorld => socket ? socket.Muzzle : transform.position + Vector3.up * 1.4f;

        IEnumerator ShotRoutine(InteractableNpc target)
        {
            var combat = Combat;
            var arms = Arsenal;

            StartCoroutine(FlashOnce(combat));

            yield return HitWait(combat);

            if (!target || target.IsDead)
                yield break;

            // The shot is re-judged at impact time: still roughly in range, still a clear
            // line. A target that made a corner during the delay is missed, not homed onto.
            // The range comes through the arsenal when there is one - a longer-reaching
            // weapon must be re-judged at ITS reach, or every shot fired past the global
            // range would be silently voided here.
            var engageRange = (arms ? arms.EngageRange(combat)
                                    : combat ? combat.engageRange : 4f) + ImpactRangeSlack;
            var flat = PedestrianSteering.Flat(target.transform.position - transform.position);
            if (flat.sqrMagnitude > engageRange * engageRange || !HasLineOfSight(target))
                yield break;

            var health = target.Health;
            if (!health)
                yield break;

            health.TakeDamage(arms ? arms.DamagePerShot(combat)
                                   : combat ? combat.damagePerShot : 50f);

            if (target.IsDead)
                CrimeFeed.Report(new CrimeEvent(
                    CrimeKind.Murder, target.transform.position, transform, target.transform));
        }

        /// <summary>
        /// The police side of the trigger: same flash, same impact beat, but the verdict is
        /// an accuracy ROLL rather than a certainty - a deliberate miss model, because
        /// perfect police make escape impossible and the roll is where distance and a
        /// running target buy the player his life. No CrimeFeed report: returning fire is
        /// not a crime.
        /// </summary>
        public void ResolveShotAtPlayer(PlayerMafioso target, float hitChance, float damage)
        {
            if (target)
                StartCoroutine(PoliceShotRoutine(target, hitChance, damage));
        }

        IEnumerator PoliceShotRoutine(PlayerMafioso target, float hitChance, float damage)
        {
            var combat = Combat;

            StartCoroutine(FlashOnce(combat));

            yield return HitWait(combat);

            if (!target || target.IsDead)
                yield break;

            if (!HasLineOfSightTo(target.transform.position + Vector3.up * TargetChestHeight))
                yield break;

            if (Random.value <= hitChance)
                target.TakeDamage(damage);
        }

        /// <summary>Muzzle to an arbitrary chest-height point - the officers' aiming check.</summary>
        public bool HasLineOfSightTo(Vector3 worldPoint)
        {
            var from = MuzzleWorld;
            var direction = worldPoint - from;
            var distance = direction.magnitude;
            if (distance < 0.2f)
                return true;

            return !Physics.Raycast(from, direction / distance, distance - 0.1f,
                                    LosBlockers, QueryTriggerInteraction.Ignore);
        }

        IEnumerator FlashOnce(CombatConfig combat)
        {
            if (!flash)
                yield break;

            // Position read per shot, not cached: the muzzle rides the hand bone.
            flash.transform.position = MuzzleWorld;
            flash.range = combat ? combat.flashRange : 4f;
            flash.intensity = combat ? combat.flashIntensity : 30f;
            flash.color = combat ? combat.flashColour : new Color(1f, 0.85f, 0.55f);
            flash.enabled = true;

            yield return FlashWait(combat);

            flash.enabled = false;
        }
    }
}
