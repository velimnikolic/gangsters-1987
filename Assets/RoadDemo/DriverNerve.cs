using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What a driver does about gunfire, the same for the crew demo's street traffic
    /// and the city's lane-graph cars: a shot close by and he stands on the brake and
    /// sits tight (a few honk); heard up the road ahead and he slows to a crawl and
    /// stops well short of it - or, where the road lets him, turns round; heard
    /// behind him and he puts his foot down. All of it wears off a few seconds after
    /// the last shot. One of these per car; Limit() once a frame.
    /// </summary>
    public struct DriverNerve
    {
        const float CloseShot = 25f;   // metres: brake dead
        const float NearShot = 60f;    // metres: slow ahead / bolt behind
        const float StopShort = 20f;   // metres short of the shooting an approaching car halts

        public float HoldUntil;
        public float SlowUntil;
        public float BoltUntil;
        public float HeardAt;
        public Vector3 Threat;
        public bool Honked;

        public bool Holding => Time.time < HoldUntil;
        public bool Approaching => Time.time < SlowUntil;
        public bool Bolting => Time.time < BoltUntil;
        public bool Frightened => Holding || Approaching || Bolting;

        /// <summary>Hears the newest shot (each shot once) and gives the speed this
        /// driver will allow himself this frame: nought while he holds, a crawl that
        /// runs down to a stop short of the shooting while he approaches it, no cap
        /// otherwise. <paramref name="hard"/>: he is braking as hard as the car will.</summary>
        public float Limit(Vector3 pos, Vector3 forward, float brake, out bool hard)
        {
            hard = false;
            float now = Time.time;
            if (StreetAlarm.LastShotAt > HeardAt && now - StreetAlarm.LastShotAt < 0.6f)
            {
                HeardAt = StreetAlarm.LastShotAt;
                var to = StreetAlarm.LastShotPos - pos;
                to.y = 0f;
                float d = to.magnitude;
                float ahead = Vector3.Dot(to, forward);
                if (d < CloseShot)
                {
                    Threat = StreetAlarm.LastShotPos;
                    HoldUntil = Mathf.Max(HoldUntil, now + Random.Range(3f, 6f));
                    if (!Honked && Random.value < 0.3f)
                    {
                        Honked = true;
                        DemoAudio.At(DemoSounds.Pick(DemoSounds.Horns), pos, DemoSounds.HornVolume, 0.06f);
                    }
                }
                else if (d < NearShot)
                {
                    Threat = StreetAlarm.LastShotPos;
                    if (ahead > 0f) SlowUntil = Mathf.Max(SlowUntil, now + 4f);
                    else BoltUntil = Mathf.Max(BoltUntil, now + 6f);
                }
            }

            if (now < HoldUntil) { hard = true; return 0f; }
            if (now < SlowUntil)
            {
                var to = Threat - pos;
                to.y = 0f;
                float ahead = Vector3.Dot(to, forward);
                return Mathf.Min(4f, Mathf.Sqrt(2f * brake * Mathf.Max(0f, ahead - StopShort)));
            }
            return float.MaxValue;
        }
    }
}
