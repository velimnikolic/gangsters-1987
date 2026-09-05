using UnityEngine;

namespace HarborDemo
{
    // The barrier at a gate: a post, a counterweight and a banded arm across one lane,
    // which lifts when a lorry comes up to it and drops again behind her.
    //
    // Built out of stretched Base-kit pillars, the way HarborCrane builds a gantry -
    // no pack in the project ships a lift gate, and a prop hinged the wrong way is
    // worse than none. Three transforms deep: the post stands still, a hinge at the
    // top of it carries the arm, and the arm is five painted lengths in its own row.
    //
    // Plain class: the district ticks it with the rest of the port's routine.
    public sealed class HarborBoom
    {
        /// <summary>How far up the arm swings, and how fast - a gate arm is a slow
        /// thing, three or four seconds end to end.</summary>
        const float LiftDegrees = 82f, LiftRate = 34f;
        /// <summary>How near a lorry has to be before the gate is opened for her, and
        /// how long it stands open behind her once she is through.</summary>
        public const float Notice = 26f, HoldOpen = 3.5f;

        Transform _hinge;
        float _angle;
        float _sign;              // which way the arm lies, and so which way it lifts
        float _hold;

        /// <summary>Where the arm crosses, in the port's own coordinates - what a lorry's
        /// distance is measured to.</summary>
        public Vector3 At { get; private set; }

        /// <summary>Asked for by the routine every frame: raise it now.</summary>
        public void Ask() => _hold = HoldOpen;

        public bool ClearForTraffic => _angle >= 70f;

        public void Tick(float dt)
        {
            if (_hinge == null) return;
            _hold = Mathf.Max(-1f, _hold - dt);
            float want = _hold > 0f ? LiftDegrees : 0f;
            _angle = Mathf.MoveTowards(_angle, want, LiftRate * dt);
            _hinge.localRotation = Quaternion.Euler(0f, 0f, _angle * _sign);
        }

        /// <summary>A gate arm across the lane at <paramref name="laneX"/>, hinged on the
        /// side the post stands: <paramref name="postSide"/> is -1 for a post west of the
        /// lane (the arm reaching east) and +1 for the other hand.</summary>
        public static HarborBoom Build(Transform parent, Vector3 at, float postSide, float reach, Material post, Material warn, Material pale)
        {
            var pillar = HarborKit.TryLoad(HarborKit.GenBase + "SM_Bld_Base_Pillar_01.prefab");
            if (pillar == null) return null;
            var boom = new HarborBoom { At = at, _sign = -postSide };

            var root = new GameObject("Boom").transform;
            root.SetParent(parent, false);
            root.localPosition = at;

            float px = postSide * (reach * 0.5f + 0.4f);
            Bar(root, pillar, new Vector3(px, 1.1f, 0f), new Vector3(0.36f, 2.2f, 0.36f), post, "BoomPost");
            // the little cabinet at its foot: what a gate arm is actually driven by
            Bar(root, pillar, new Vector3(px + postSide * 0.5f, 0.5f, 0.55f), new Vector3(0.55f, 1f, 0.5f), post, "BoomDrive");

            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(px, 2.05f, 0f);
            boom._hinge = hinge;

            // the arm: five lengths banded warning-red and white, reaching back across
            // the lane from the post, and a stub counterweight behind the hinge
            const int Bands = 5;
            float len = reach / Bands;
            for (int i = 0; i < Bands; i++)
            {
                float cx = -postSide * (i + 0.5f) * len;
                Bar(hinge, pillar, new Vector3(cx, 0f, 0f), new Vector3(len, 0.2f, 0.2f),
                    (i & 1) == 0 ? warn : pale, "BoomArm");
            }
            Bar(hinge, pillar, new Vector3(postSide * 0.55f, 0f, 0f), new Vector3(1.1f, 0.34f, 0.34f), post, "BoomWeight");
            return boom;
        }

        /// <summary>One stretched pillar as a box of the wanted size, painted. The pillar
        /// is measured, not assumed: it is 0.43 square and a storey tall in the pack this
        /// project has, and it need not be in the next.</summary>
        static void Bar(Transform parent, GameObject pillar, Vector3 centre, Vector3 size, Material mat, string name)
        {
            var b = HarborKit.PrefabBounds(pillar);
            var go = Object.Instantiate(pillar, parent);
            go.name = name;
            var s = new Vector3(size.x / Mathf.Max(0.01f, b.size.x),
                                size.y / Mathf.Max(0.01f, b.size.y),
                                size.z / Mathf.Max(0.01f, b.size.z));
            go.transform.localScale = s;
            go.transform.localPosition = centre - Vector3.Scale(b.center, s);
            go.transform.localRotation = Quaternion.identity;
            if (mat != null)
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) mr.sharedMaterial = mat;
        }
    }
}
