using System.Collections.Generic;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    // The outfit's guns on the outfit's men. Which gun a man carries is the
    // ledger's call (the armory item his id holds; RosterOps deals them), and
    // which pack body plays it is the ledger's too (ArmoryCatalog names the model,
    // LedgerModelSet holds it) - so the street shows exactly what the book says,
    // down to the plating.
    //
    // Where the gun sits in the fist is derived, not authored: the humanoid rig's
    // own T-pose. In the T-pose the right hand points along +X with the palm down
    // and the thumb forward, so a pistol held in that fist runs its barrel along
    // +X and its top toward +Z; expressed in the hand bone's own frame that holds
    // in every pose the animator puts the arm through, on every Synty rig, without
    // a per-pack table of eyeballed angles. A small authored nudge (GripNudge,
    // GripTilt) rides on top for the last centimetre.
    public static class CrewArms
    {
        /// <summary>Metres from the wrist joint to the palm's centre, along the hand.</summary>
        public static Vector3 GripNudge = new Vector3(0.075f, -0.015f, 0.01f);

        /// <summary>Degrees of extra turn on the gun in the fist (barrel-relative
        /// pitch, yaw, roll) for taste; zero is the derived hold.</summary>
        public static Vector3 GripTilt = Vector3.zero;

        /// <summary>The revolver every man carries when the scene says "everyone
        /// armed" and the ledger gave him nothing.</summary>
        public const string DefaultSidearm = "SM_Wep_Pistol_Revolver_01";

        // ------------------------------------------------------------- the ledger

        static readonly List<RosterEquipment> held = new List<RosterEquipment>();

        /// <summary>The firearm this member holds per the ledger, or null: the first
        /// gun among his items (a man with two pistols still shows one; a bat is not
        /// a gun and never shows).</summary>
        public static RosterEquipment FirearmOf(Roster roster, int id)
        {
            if (roster == null) return null;
            roster.HeldBy(id, held);
            foreach (var item in held)
                if (IsFirearm(item.Kind)) return item;
            return null;
        }

        public static bool IsFirearm(EquipmentKind kind) => kind switch
        {
            EquipmentKind.Pistol => true,
            EquipmentKind.TwinPistols => true,
            EquipmentKind.Shotgun => true,
            EquipmentKind.Rifle => true,
            EquipmentKind.TommyGun => true,
            EquipmentKind.MachinePistol => true,
            _ => false,
        };

        /// <summary>The pack body that plays this item - the catalogue listing of the
        /// same name says which, exactly as the armory page photographs it.</summary>
        public static GameObject ModelFor(RosterEquipment item)
        {
            if (item == null) return null;
            string modelName = null;
            foreach (var listing in LivingCity.Outfit.ArmoryCatalog.Weapons)
                if (listing.DisplayName == item.DisplayName) { modelName = listing.ModelName; break; }
            return LivingCity.UI.LedgerModelSet.WeaponModelFor(item.Kind, modelName);
        }

        // ------------------------------------------------------------- the ballistics

        public struct Stats
        {
            public float Range;     // metres a man closes to before he fires
            public float Interval;  // seconds between shots
            public int Damage;      // hits taken off a man's health per hit
            public float Accuracy;  // hit chance inside half range; falls to half of it at full range
        }

        // The ladder the armory sells, weakest to strongest, and each gun's own
        // reach - a shotgun man walks in almost to arm's length, a rifleman opens
        // up from across the street, so a crew with mixed arms strings itself out
        // by gun rather than stopping in one line.
        public static Stats StatsFor(EquipmentKind kind) => kind switch
        {
            // .38: pistol range, one round a second, a fair shot - misses plenty
            EquipmentKind.Pistol => new Stats { Range = 10f, Interval = 1.05f, Damage = 1, Accuracy = 0.55f },
            // twin pack: the same reach, twice the lead, wilder still
            EquipmentKind.TwinPistols => new Stats { Range = 10f, Interval = 0.55f, Damage = 1, Accuracy = 0.45f },
            // shotgun: close, slow - and inside its reach it does not miss, and it
            // puts a man down in two
            EquipmentKind.Shotgun => new Stats { Range = 6f, Interval = 1.6f, Damage = 2, Accuracy = 0.97f },
            // machine pistol: pistol reach, sprayed - most of the clip goes wide
            EquipmentKind.MachinePistol => new Stats { Range = 12f, Interval = 0.2f, Damage = 1, Accuracy = 0.3f },
            // rifle: the long gun - a street away, deliberate, hard-hitting, sure
            EquipmentKind.Rifle => new Stats { Range = 26f, Interval = 1.7f, Damage = 2, Accuracy = 0.88f },
            // tommy gun: the strongest piece - reach and a stream of lead, a third of it on
            EquipmentKind.TommyGun => new Stats { Range = 18f, Interval = 0.14f, Damage = 1, Accuracy = 0.35f },
            _ => new Stats { Range = 10f, Interval = 1.05f, Damage = 1, Accuracy = 0.6f },
        };

        // ------------------------------------------------------------- the fist

        /// <summary>Puts the gun in this rig's right hand. Returns the gun, or null when
        /// the rig has no humanoid hand.</summary>
        public static Transform Attach(Animator animator, GameObject prefab)
        {
            if (!animator || !prefab) return null;
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (!hand) return null;

            var gun = Object.Instantiate(prefab).transform;
            gun.name = prefab.name;
            foreach (var col in gun.GetComponentsInChildren<Collider>()) Object.Destroy(col);
            foreach (var rb in gun.GetComponentsInChildren<Rigidbody>()) Object.Destroy(rb);

            // the gun's own frame: barrel along the longest axis of its mesh, toward
            // the end that reaches furthest from the grip pivot; the pack authors its
            // pieces barrel +Z, top +Y, and the measure only confirms which way is which
            MeasureBarrel(gun, out var barrelLocal, out var muzzleLocal);
            var upLocal = Mathf.Abs(barrelLocal.y) > 0.5f ? Vector3.forward : Vector3.up;
            var gunFrame = Quaternion.LookRotation(barrelLocal, upLocal); // gun-local -> "gun frame"

            // the hand's frame in the T-pose: +X along the fingers, +Y off the back of
            // the hand, +Z along the thumb - so a pistol runs barrel +X, top +Z
            var tPose = TPoseRotation(animator, hand);
            var holdWorld = Quaternion.LookRotation(Vector3.right, Vector3.forward)
                            * Quaternion.Euler(GripTilt);
            // local to the hand bone: what turns hand-space into the hold, then
            // gun-space into the gun frame - resolved once, valid in every pose
            var handLocal = Quaternion.Inverse(tPose) * holdWorld * Quaternion.Inverse(gunFrame);

            gun.SetParent(hand, false);
            gun.localRotation = handLocal;
            gun.localPosition = Quaternion.Inverse(tPose) * GripNudge;
            gun.localScale = Vector3.one;

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(gun, false);
            muzzle.localPosition = muzzleLocal;
            muzzle.localRotation = Quaternion.LookRotation(barrelLocal, upLocal);
            return gun;
        }

        /// <summary>The marker Attach left at the bore, or the gun itself.</summary>
        public static Transform MuzzleOf(Transform gun)
        {
            if (!gun) return null;
            var m = gun.Find("Muzzle");
            return m ? m : gun;
        }

        /// <summary>World rotation of this bone in the avatar's T-pose - the rig's own
        /// definition of "arms out, palms down", read off the HumanDescription that
        /// built the avatar. Falls back to the bone's rest rotation when the avatar
        /// keeps no skeleton (a hand-made avatar), which is the honest best guess.</summary>
        static Quaternion TPoseRotation(Animator animator, Transform bone)
        {
            var avatar = animator.avatar;
            var byName = new Dictionary<string, Quaternion>();
            if (avatar)
            {
                var skeleton = avatar.humanDescription.skeleton;
                if (skeleton != null)
                    foreach (var sb in skeleton)
                        byName[sb.name] = sb.rotation;
            }

            var rotation = Quaternion.identity;
            var root = animator.transform;
            // parent-most first: R = R_root * ... * R_bone
            var chain = new List<Transform>();
            for (var t = bone; t != null && t != root.parent; t = t.parent)
            {
                chain.Add(t);
                if (t == root) break;
            }
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var t = chain[i];
                if (t == root)
                {
                    rotation = root.rotation;
                    continue;
                }
                rotation *= byName.TryGetValue(t.name, out var q) ? q : t.localRotation;
            }
            // local to the animator root: the hold is expressed in the man's own frame
            return Quaternion.Inverse(root.rotation) * rotation;
        }

        /// <summary>WeaponSocket's measure, kept: the barrel is the longest axis of the
        /// gun's mesh, pointing to whichever end lies further from the pivot; the
        /// muzzle is that end at the bore.</summary>
        static void MeasureBarrel(Transform gun, out Vector3 barrelLocal, out Vector3 muzzleLocal)
        {
            barrelLocal = Vector3.forward;
            muzzleLocal = new Vector3(0f, 0.09f, 0.22f);

            var filters = gun.GetComponentsInChildren<MeshFilter>();
            var started = false;
            var bounds = new Bounds();
            foreach (var f in filters)
            {
                if (!f.sharedMesh) continue;
                var b = f.sharedMesh.bounds;
                foreach (var corner in Corners(b))
                {
                    var p = gun.InverseTransformPoint(f.transform.TransformPoint(corner));
                    if (started) bounds.Encapsulate(p);
                    else { bounds = new Bounds(p, Vector3.zero); started = true; }
                }
            }
            if (!started) return;

            var size = bounds.size;
            int axis = size.x > size.y && size.x > size.z ? 0 : size.y > size.z ? 1 : 2;
            float far = Mathf.Abs(bounds.max[axis]) >= Mathf.Abs(bounds.min[axis])
                ? bounds.max[axis] : bounds.min[axis];
            barrelLocal = Vector3.zero;
            barrelLocal[axis] = Mathf.Sign(far);

            // the bore: the barrel's own centre line, which on a pistol sits above
            // the whole-gun centre; take the top third of the gun's height as the bore
            var centre = bounds.center;
            muzzleLocal = centre;
            muzzleLocal[axis] = far;
            int upAxis = axis == 1 ? 2 : 1;
            muzzleLocal[upAxis] = Mathf.Lerp(bounds.min[upAxis], bounds.max[upAxis], 0.78f);
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            var c = b.center;
            var e = b.extents;
            for (int i = 0; i < 8; i++)
                yield return c + new Vector3(
                    (i & 1) == 0 ? e.x : -e.x,
                    (i & 2) == 0 ? e.y : -e.y,
                    (i & 4) == 0 ? e.z : -e.z);
        }
    }
}
