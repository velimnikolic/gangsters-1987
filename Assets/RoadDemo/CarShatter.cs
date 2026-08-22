using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What a bomb leaves of a car: the body comes apart where it stands. Every solid
    /// piece the pack modelled it from - the shell, the doors, the wheels, the glass -
    /// is taken off the husk and thrown outward from the blast, then falls under its own
    /// weight to the road and settles there (BombDebris does the falling, on its own
    /// arithmetic, so a scene with no ground collider still catches it).
    ///
    /// A car's solid parts are its MeshRenderers; a man riding in it is a
    /// SkinnedMeshRenderer (a rigged body), so taking only the mesh pieces leaves the
    /// people where they are - the blast kills them the same way it kills anyone near it
    /// (Explosion), it does not fling their limbs as scrap metal.
    ///
    /// Nothing here reaches into how the car drove: the RoadCar is Wrecked first (off the
    /// network, never ticked again), and this only tears up the shell that is left.
    /// </summary>
    public static class CarShatter
    {
        /// <summary>Pieces past this many are left on the husk - a body the pack cut into
        /// hundreds of tiny trims would make hundreds of falling scraps for no more
        /// spectacle than a dozen big ones give.</summary>
        const int MaxPieces = 28;

        /// <summary>Seconds a scrap lies on the road before it is cleared - long enough
        /// to read as a wreck, short enough that a street bombed all night does not fill
        /// with metal.</summary>
        const float Linger = 22f;

        static Transform _root;

        /// <summary>Blow this car's shell apart, thrown outward from <paramref name="blast"/>.
        /// The RoadCar is wrecked here if it has not been already, so the husk is out of
        /// the traffic model before its body leaves it.</summary>
        public static void Shatter(RoadCar car, Vector3 blast)
        {
            if (car == null || car.Tf == null) return;
            car.Wreck();

            float floorY = car.RoadY + 0.04f;
            var pieces = Collect(car.Tf);
            int taken = 0;
            for (int i = 0; i < pieces.Count && taken < MaxPieces; i++)
            {
                if (Detach(pieces[i], blast, floorY)) taken++;
            }
        }

        /// <summary>Give the whole thing a home so a hundred wrecks a night do not clutter
        /// the crews' object, and reset it for a fresh Play.</summary>
        static Transform Root()
        {
            if (_root == null) _root = new GameObject("Car Debris").transform;
            return _root;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => _root = null;

        static readonly List<MeshRenderer> _buf = new List<MeshRenderer>();

        static List<MeshRenderer> Collect(Transform car)
        {
            _buf.Clear();
            foreach (var mr in car.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (mr == null) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                _buf.Add(mr);
            }
            return _buf;
        }

        static bool Detach(MeshRenderer mr, Vector3 blast, float floorY)
        {
            var t = mr.transform;
            var pos = t.position;

            // outward from the blast, always with a lift - a piece dead level with the
            // charge would otherwise be shot flat along the ground and never tumble
            Vector3 out3 = pos - blast; out3.y = 0f;
            Vector3 dir = out3.sqrMagnitude > 1e-3f ? out3.normalized : Random.insideUnitSphere;
            float reach = out3.magnitude;
            // the nearer a piece sat to the charge, the harder it is thrown
            float kick = Mathf.Lerp(7f, 3.2f, Mathf.Clamp01(reach / 3f));
            Vector3 v = dir * kick + Vector3.up * Random.Range(3.5f, 6.5f);
            v += Random.insideUnitSphere * 1.2f;

            t.SetParent(Root(), true);
            var d = t.gameObject.AddComponent<BombDebris>();
            d.Launch(v, Random.insideUnitSphere * Random.Range(180f, 520f), floorY, Linger);
            return true;
        }
    }

    /// <summary>One thrown scrap of a wrecked car. It falls on its own gravity and
    /// catches on the road at a fixed height, tumbling as it goes and rocking to rest -
    /// no Rigidbody and no collider, so it needs nothing of the scene and lands the same
    /// way in a headless run as on screen. It clears itself after a while.</summary>
    public sealed class BombDebris : MonoBehaviour
    {
        Vector3 _vel;
        Vector3 _spin;      // degrees a second about a fixed axis
        Vector3 _axis;
        float _floorY;
        float _life;
        bool _settled;

        const float Gravity = 22f;

        public void Launch(Vector3 velocity, Vector3 spinDegPerSec, float floorY, float life)
        {
            _vel = velocity;
            _axis = spinDegPerSec.sqrMagnitude > 1e-4f ? spinDegPerSec.normalized : Vector3.up;
            _spin = spinDegPerSec;
            _floorY = floorY;
            _life = life;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _life -= dt;
            if (_life <= 0f) { Destroy(gameObject); return; }

            if (!_settled)
            {
                _vel.y -= Gravity * dt;
                var p = transform.position + _vel * dt;
                if (p.y <= _floorY)
                {
                    p.y = _floorY;
                    // rest once it has stopped bouncing; until then, a damped hop
                    if (_vel.y > -1.4f) { _settled = true; _vel = Vector3.zero; }
                    else { _vel.y = -_vel.y * 0.32f; _vel.x *= 0.5f; _vel.z *= 0.5f; _spin *= 0.5f; }
                }
                transform.position = p;
                float mag = _spin.magnitude;
                if (mag > 0.01f) transform.Rotate(_axis, mag * dt, Space.World);
                _spin = Vector3.MoveTowards(_spin, Vector3.zero, 40f * dt);
            }
        }
    }
}
