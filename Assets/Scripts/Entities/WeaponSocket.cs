using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Puts a weapon in a humanoid's right hand.
    ///
    /// The hand is asked for through the HUMANOID rig rather than by object name. The two packs
    /// disagree about naming - Animated People ships Hand_Container_R sockets, Epic City uses
    /// Mixamo bone names and ships no socket at all - but both import as Humanoid, and
    /// GetBoneTransform is the same call on either. So this works on man-mafia and on
    /// man_business without a per-pack table to keep in sync.
    ///
    /// Where the weapon sits in the fist is AUTHORED, not derived. Three attempts at deriving it
    /// came first - the hand bone's axes, the forearm line, then posing the rig into the aim clip
    /// and reading the answer off it - and each was wrong in a new way, because how a hand looks
    /// closed around a grip is a judgement rather than a calculation. The numbers below were set
    /// by eye against man-mafia and are simply kept.
    ///
    /// They are local to the RIGHT HAND BONE, so they are specific to that rig's skeleton. Epic
    /// City's people all share it, so the whole pack is covered; a character from a pack with a
    /// differently-oriented hand bone needs its own pass with the gizmo, and the values here
    /// then become that pack's starting point rather than its answer.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class WeaponSocket : MonoBehaviour
    {
        [Tooltip("Weapon prefab. Its origin is expected at the grip; which way the barrel runs " +
                 "is measured off the mesh, so it does not matter what axis it was authored on.")]
        [SerializeField] GameObject weaponPrefab;

        [Tooltip("Where the grip sits, local to the right hand bone. Set by eye - Attach, then " +
                 "drag the weapon in the Scene view, then copy its transform back here.")]
        [SerializeField] Vector3 gripPosition = new(-0.01f, 0.09f, 0.00265f);

        [Tooltip("How the weapon is turned in the fist, local to the right hand bone, in degrees.")]
        [SerializeField] Vector3 gripRotation = new(-95.1f, -20f, 105.8f);

        [Tooltip("The attached weapon. Filled in when you attach it; assign one by hand to use " +
                 "a weapon you placed yourself.")]
        [SerializeField] Transform weapon;

        /// <summary>The attached weapon, or null before it is equipped.</summary>
        public Transform Weapon => weapon;

        public GameObject Prefab => weaponPrefab;

        [Tooltip("Optional. An empty child of the weapon marking where the flash goes - drag it " +
                 "in the Scene view like anything else. Overrides the offset below.")]
        [SerializeField] Transform muzzlePoint;

        [Tooltip("Where the bore is, local to the weapon, when there is no marker. Used by " +
                 "anything spawned at runtime, which has no scene object to place by hand.")]
        [SerializeField] Vector3 muzzleOffset = new(0f, 0.09f, 0.2263f);

        /// <summary>
        /// Where the flash goes, in world space.
        ///
        /// A hand-placed marker wins; otherwise the authored offset. The mesh measurement is NOT
        /// used for this any more - it reads bounds, which put the bore about 3cm low on this
        /// revolver, and the measured value only ever existed because there was nothing better.
        /// There is now: the offset default is the bore, taken off a marker placed by eye.
        /// </summary>
        public Vector3 Muzzle
        {
            get
            {
                if (muzzlePoint)
                    return muzzlePoint.position;

                return weapon ? weapon.TransformPoint(muzzleOffset) : transform.position;
            }
        }

        /// <summary>The hand-placed muzzle marker, or null when the measured tip is being used.</summary>
        public Transform MuzzleMarker => muzzlePoint;

        /// <summary>Takes a marker created by the Editor. Nothing moves it afterwards.</summary>
        public void AdoptMuzzle(Transform marker) => muzzlePoint = marker;

        /// <summary>The barrel tip as MEASURED, ignoring any marker. The Editor places the marker here.</summary>
        public Vector3 MeasuredMuzzle => weapon ? weapon.TransformPoint(muzzleLocal) : transform.position;

        /// <summary>Which way the barrel runs, in world space.</summary>
        public Vector3 BarrelDirection =>
            weapon ? weapon.TransformDirection(barrelLocal).normalized : transform.forward;

        Vector3 muzzleLocal;
        Vector3 barrelLocal = Vector3.forward;

        /// <summary>
        /// Re-reads the weapon's mesh. Public so the Editor can measure in edit mode, where
        /// Start has never run and the fields above are still at their defaults.
        /// </summary>
        public void Measure()
        {
            if (weapon)
                MeasureBarrel(weapon);
        }

        /// <summary>
        /// Draws the muzzle and the line the shot leaves on, because a point you cannot see is
        /// a point you cannot place. Selecting the character shows both.
        /// </summary>
        void OnDrawGizmosSelected()
        {
            if (!weapon)
                return;

            // Edit mode never ran Start, so nothing has measured yet.
            if (muzzleLocal == Vector3.zero)
                MeasureBarrel(weapon);

            var muzzle = Muzzle;

            Gizmos.color = new Color(1f, 0.85f, 0.35f);
            Gizmos.DrawSphere(muzzle, 0.012f);
            Gizmos.DrawRay(muzzle, BarrelDirection * 0.6f);

            // The measured tip alongside it, so a marker dragged somewhere silly is obvious.
            if (!muzzlePoint)
                return;

            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(MeasuredMuzzle, 0.012f);
        }

        void Start()
        {
            // A weapon already in the scene is AUTHORED, and entering Play must never move it.
            //
            // This used to re-derive the placement here whenever Auto Place was on, which threw
            // away by-hand adjustment the moment you pressed Play - the exact work the scene
            // object exists to make possible. Auto Place governs the EDITOR only: whether the
            // inspector values drive the weapon while you drag them. Runtime does not get a
            // vote.
            //
            // The barrel is still measured, because Muzzle needs it and it reads the mesh
            // rather than moving anything.
            if (weapon)
            {
                MeasureBarrel(weapon);
                return;
            }

            Equip();
        }

        /// <summary>The hand the weapon hangs from, or null when the rig is not Humanoid.</summary>
        public Transform Hand
        {
            get
            {
                var hand = GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
                if (!hand)
                    Debug.LogWarning($"[WeaponSocket] {name} has no humanoid right hand - is " +
                                     "the rig imported as Humanoid?", this);
                return hand;
            }
        }

        /// <summary>
        /// Spawns the weapon and places it. Used at runtime; the Editor attaches through
        /// Adopt instead, so the scene gets a real prefab instance it can save.
        /// </summary>
        public void Equip()
        {
            if (weapon || !weaponPrefab)
                return;

            var hand = Hand;
            if (!hand)
                return;

            // Scale is left alone deliberately: the weapon inherits the hand's, and on a rig
            // whose humanScale is not 1 that is what keeps the gun in proportion to the person
            // holding it rather than to the world.
            Adopt(Instantiate(weaponPrefab, hand).transform);
        }

        /// <summary>
        /// Takes ownership of an already-created weapon: parents it to the hand, measures its
        /// barrel and places it. The Editor's Attach button comes through here.
        /// </summary>
        public void Adopt(Transform instance)
        {
            if (!instance)
                return;

            var hand = Hand;
            if (!hand)
                return;

            instance.SetParent(hand, worldPositionStays: false);
            weapon = instance;

            MeasureBarrel(weapon);
            Reposition();
        }

        /// <summary>
        /// Puts the weapon at the authored grip. One assignment - the values ARE the answer, so
        /// there is nothing to compute.
        ///
        /// Only ever called when a weapon is first attached, or from the Editor's Re-place
        /// button. Entering Play never calls it: a weapon already in the scene keeps exactly the
        /// transform it was saved with, whatever the values here say.
        /// </summary>
        public void Reposition()
        {
            if (!weapon)
                return;

            weapon.localPosition = gripPosition;
            weapon.localRotation = Quaternion.Euler(gripRotation);
        }

        /// <summary>Removes the weapon. The Editor's Detach button.</summary>
        public void Release() => weapon = null;

        /// <summary>
        /// Finds which way the barrel runs in the weapon's OWN space, and where its tip is, by
        /// measuring the mesh.
        ///
        /// Assuming an axis is what put the revolver in the gunman's fist pointing at the sky.
        /// The source OBJ is authored barrel-down-+Z, but Unity imports .obj through the FBX
        /// importer, which applies its own axis conversion - so +Z on disk arrived as +Y in the
        /// scene, and every calibration downstream faithfully aimed the wrong axis.
        ///
        /// A gun is much longer than it is wide, and this one has its origin at the grip, so the
        /// mesh answers both questions on its own: the barrel is the longest axis of the bounds,
        /// pointing to whichever end is further from the origin, and the tip is that end. No
        /// convention to be wrong about, and it holds for the next weapon too.
        /// </summary>
        void MeasureBarrel(Transform weapon)
        {
            var filters = weapon.GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0)
            {
                Debug.LogWarning($"[WeaponSocket] {name}: the weapon has no mesh to measure.", this);
                return;
            }

            var bounds = new Bounds();
            var started = false;

            foreach (var filter in filters)
            {
                if (!filter.sharedMesh)
                    continue;

                // Into the weapon root's space: an imported model puts its geometry on children,
                // one per material group, each with its own transform.
                var local = filter.sharedMesh.bounds;
                foreach (var corner in Corners(local))
                {
                    var point = weapon.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (started)
                        bounds.Encapsulate(point);
                    else
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        started = true;
                    }
                }
            }

            if (!started)
                return;

            // Longest axis wins, and its sign is the end that reaches furthest from the grip.
            var size = bounds.size;
            var axis = size.x > size.y && size.x > size.z ? 0 : size.y > size.z ? 1 : 2;
            var far = Mathf.Abs(bounds.max[axis]) >= Mathf.Abs(bounds.min[axis])
                ? bounds.max[axis]
                : bounds.min[axis];

            barrelLocal = Vector3.zero;
            barrelLocal[axis] = Mathf.Sign(far);

            // For the other two axes, the WHOLE gun's centre is wrong - it sits halfway up
            // between the butt and the sights, about 5cm under the bore on this revolver. The
            // piece that reaches furthest along the barrel axis is the barrel, so its centre is
            // the bore. Falls back to the whole-gun centre for a single-mesh weapon, where the
            // two are the same thing anyway.
            muzzleLocal = FurthestPieceCentre(weapon, filters, axis, far) ?? bounds.center;
            muzzleLocal[axis] = far;
        }

        /// <summary>
        /// The centre of whichever sub-mesh reaches furthest along the barrel, in the weapon's
        /// space. Null when there is only one piece to choose from.
        /// </summary>
        static Vector3? FurthestPieceCentre(Transform weapon, MeshFilter[] filters, int axis, float far)
        {
            if (filters.Length < 2)
                return null;

            Vector3? best = null;
            var bestReach = float.NegativeInfinity;

            foreach (var filter in filters)
            {
                if (!filter.sharedMesh)
                    continue;

                Bounds piece = default;
                var started = false;

                foreach (var corner in Corners(filter.sharedMesh.bounds))
                {
                    var point = weapon.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (started)
                        piece.Encapsulate(point);
                    else
                    {
                        piece = new Bounds(point, Vector3.zero);
                        started = true;
                    }
                }

                if (!started)
                    continue;

                // Signed so it works for a barrel running down the negative axis too.
                var reach = far >= 0f ? piece.max[axis] : -piece.min[axis];
                if (reach <= bestReach)
                    continue;

                bestReach = reach;
                best = piece.center;
            }

            return best;
        }

        static Vector3[] Corners(Bounds b)
        {
            var c = b.center;
            var e = b.extents;
            return new[]
            {
                c + new Vector3(+e.x, +e.y, +e.z), c + new Vector3(+e.x, +e.y, -e.z),
                c + new Vector3(+e.x, -e.y, +e.z), c + new Vector3(+e.x, -e.y, -e.z),
                c + new Vector3(-e.x, +e.y, +e.z), c + new Vector3(-e.x, +e.y, -e.z),
                c + new Vector3(-e.x, -e.y, +e.z), c + new Vector3(-e.x, -e.y, -e.z),
            };
        }

    }
}
