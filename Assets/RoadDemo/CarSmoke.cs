using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What comes out of a car: the wisp off a tailpipe while the engine is turning, and
    /// the plume off a bonnet once it has been shot through. Both are the particle pack's
    /// smoke, re-tuned here, and the tuning is the whole of this class.
    ///
    /// THE PACK'S SIZES ARE NOT METRES. Its smoke is a MESH particle - SM_Particle_Smoke_01,
    /// measured 0.191m across - and a ParticleSystem's start size MULTIPLIES that mesh. The
    /// pack ships its "Small" preset at three to six, which is a puff a metre wide: right
    /// for a burning car, absurd for an exhaust. So nothing here writes a start size
    /// directly; it writes the width it wants IN METRES and Puffs() divides it by the mesh.
    /// Every number below can then be read as what it looks like on the street.
    /// </summary>
    public static class CarSmoke
    {
        /// <summary>How wide the pack's smoke mesh is, at a start size of one. Measured
        /// off SM_Particle_Smoke_01's bounds, not guessed.</summary>
        public const float PuffMesh = 0.191f;

        /// <summary>A width in metres, in the start sizes the ParticleSystem wants.</summary>
        public static float Puffs(float metres) => metres / PuffMesh;

        /// <summary>Take the pack's smoke and make it the size, life and speed asked for.
        /// The prefab keeps everything the tuning does not name - its material, its colour
        /// over life, its shape - so the smoke still looks like the pack's smoke.
        ///
        /// The simulation space is left as the prefab has it (world): a puff belongs to the
        /// air it was left in, not to the car that left it, and a car pulling away from its
        /// own smoke is the point of drawing any.</summary>
        public static ParticleSystem Tuned(GameObject prefab, Transform under, Vector3 at,
                                           float wide, float grow, float lifeLo, float lifeHi,
                                           float speed, float rate)
        {
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, under, worldPositionStays: false);
            go.name = "Smoke";
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps == null) { Object.Destroy(go); return null; }

            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(Puffs(wide), Puffs(wide * grow));
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeLo, lifeHi);
            main.startSpeed = speed;
            // prewarm fills a system with a whole lifetime of smoke on its first frame,
            // which is a puff of exhaust appearing round a car that has only just started
            main.prewarm = false;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            ps.Clear();
            return ps;
        }

        /// <summary>Rounds through the bonnet have stopped whatever was turning under it.
        /// Black smoke off the front of the car, for as long as the car is there.
        ///
        /// This is the part the damage model was missing: EngineDead was a bool read once
        /// by a caller who printed a line about it, so a car killed by gunfire looked
        /// exactly like a car that had parked. A consequence nothing can SEE is the same
        /// bug as a consequence nothing can reach.</summary>
        public static void Bonnet(RoadCar car)
        {
            if (car == null || car.Tf == null) return;
            Tuned(CrewKit.EngineSmoke, car.Tf,
                  new Vector3(0f, BonnetHeight, car.HalfLen * BonnetAlong),
                  wide: BonnetWide, grow: 1.9f, lifeLo: 1.8f, lifeHi: 3.2f,
                  speed: BonnetRise, rate: BonnetRate);
        }

        /// <summary>Where on the car the plume stands, and how much of it there is: up out
        /// of the middle of the bonnet, at about the height of the bonnet's own line.</summary>
        const float BonnetHeight = 0.85f;
        const float BonnetAlong = 0.6f;
        const float BonnetWide = 0.38f;
        const float BonnetRise = 0.9f;
        const float BonnetRate = 8f;
    }
}
