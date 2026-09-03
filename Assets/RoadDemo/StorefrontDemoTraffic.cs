using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A small, repeatable showroom route through a real Storefront threshold. The
    /// storefront remains the only owner of the leaf animation; this component only
    /// asks it to open or close while the model walks the same line a visitor uses.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class StorefrontDemoTraffic : MonoBehaviour
    {
        const float OpenForEntry = 0.10f;
        const float EnterAt = 0.20f;
        const float EnteredAt = 0.42f;
        const float CloseAfterEntry = 0.47f;
        const float OpenForExit = 0.59f;
        const float ExitAt = 0.68f;
        const float ExitedAt = 0.90f;
        const float CloseAfterExit = 0.91f;

        [SerializeField] Storefront storefront;
        [SerializeField] Transform visitor;
        [SerializeField] AnimationClip idleClip;
        [SerializeField] AnimationClip walkClip;
        [SerializeField, Range(0f, 1f)] float phaseOffset;
        [SerializeField, Min(4f)] float cycleSeconds = 13.5f;
        [SerializeField, Min(1f)] float pavementDistance = 4f;
        [SerializeField, Min(0.4f)] float interiorDepth = 1.15f;

        ResidentialBlockLife.AmbientMotion motion;
        Renderer[] visitorRenderers = System.Array.Empty<Renderer>();
        int lastDoorRequest = -1;
        bool visitorVisible = true;
        float cycle;

        public Storefront Storefront => storefront;
        public Transform Visitor => visitor;
        public float Cycle => cycle;
        public bool VisitorVisible => visitorVisible;
        public bool DoorRequestedOpen => lastDoorRequest == 1;

        public void Configure(Storefront door, Transform person,
                              AnimationClip idle, AnimationClip walk, float phase)
        {
            storefront = door;
            visitor = person;
            idleClip = idle;
            walkClip = walk;
            phaseOffset = Mathf.Repeat(phase, 1f);

            // The saved Edit-mode scene starts as a legible line-up. Play Mode is
            // what staggers the actors around the full enter/exit cycle.
            if (storefront != null && visitor != null)
            {
                Vector3 outward = FlatOutward();
                visitor.position = storefront.DoorWorld + outward * pavementDistance;
                visitor.rotation = Quaternion.LookRotation(-outward, Vector3.up);
            }
        }

        void Awake()
        {
            if (visitor == null || storefront == null)
            {
                enabled = false;
                return;
            }

            visitorRenderers = visitor.GetComponentsInChildren<Renderer>(true);
            foreach (var collider in visitor.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var body in visitor.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }

            var animator = visitor.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;
                motion = new ResidentialBlockLife.AmbientMotion(
                    animator, idleClip, walkClip, null, null, null, null,
                    phaseOffset, seated: false);
            }

            storefront.SnapClosed();
            TickRoute(0f);
        }

        void Update() => TickRoute(Mathf.Max(0f, Time.deltaTime));

        void TickRoute(float dt)
        {
            if (storefront == null || visitor == null) return;

            cycle = Mathf.Repeat(Time.time / Mathf.Max(4f, cycleSeconds) + phaseOffset, 1f);
            bool wantsDoor = (cycle >= OpenForEntry && cycle < CloseAfterEntry) ||
                             (cycle >= OpenForExit && cycle < CloseAfterExit);
            RequestDoor(wantsDoor);

            Vector3 outward = FlatOutward();
            Vector3 threshold = storefront.DoorWorld;
            threshold.y = storefront.DoorWorld.y + 0.025f;
            Vector3 pavement = threshold + outward * pavementDistance;
            Vector3 interior = threshold - outward * interiorDepth;
            Vector3 at;
            Vector3 facing;
            bool walking;

            if (cycle < EnterAt)
            {
                at = pavement;
                facing = -outward;
                walking = false;
            }
            else if (cycle < EnteredAt)
            {
                at = Vector3.Lerp(pavement, interior,
                    Smooth((cycle - EnterAt) / (EnteredAt - EnterAt)));
                facing = -outward;
                walking = true;
            }
            else if (cycle < ExitAt)
            {
                at = interior;
                facing = outward;
                walking = false;
            }
            else if (cycle < ExitedAt)
            {
                at = Vector3.Lerp(interior, pavement,
                    Smooth((cycle - ExitAt) / (ExitedAt - ExitAt)));
                facing = outward;
                walking = true;
            }
            else
            {
                at = pavement;
                facing = outward;
                walking = false;
            }

            SetVisible(cycle < EnteredAt || cycle >= ExitAt);
            visitor.position = at;
            if (facing.sqrMagnitude > 0.001f)
                visitor.rotation = Quaternion.LookRotation(facing, Vector3.up);

            motion?.Select(walking
                    ? ResidentialBlockLife.AmbientMotion.BasePose.Walk
                    : ResidentialBlockLife.AmbientMotion.BasePose.Idle,
                loop: true, speed: walking ? 1.1f : 1f);
            motion?.Tick(dt);
        }

        Vector3 FlatOutward()
        {
            Vector3 outward = storefront != null ? storefront.OutwardWorld : Vector3.forward;
            outward.y = 0f;
            return outward.sqrMagnitude > 0.001f ? outward.normalized : Vector3.forward;
        }

        void RequestDoor(bool open)
        {
            int request = open ? 1 : 0;
            if (lastDoorRequest == request) return;
            lastDoorRequest = request;
            if (open) storefront.Open();
            else storefront.Close();
        }

        void SetVisible(bool visible)
        {
            visitorVisible = visible;
            for (int i = 0; i < visitorRenderers.Length; i++)
                if (visitorRenderers[i] != null)
                    visitorRenderers[i].enabled = visible;
        }

        static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        void OnDisable()
        {
            if (Application.isPlaying && storefront != null) storefront.Close();
        }

        void OnDestroy()
        {
            motion?.Dispose();
            motion = null;
        }
    }
}
