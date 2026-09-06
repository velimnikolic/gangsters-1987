using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A CITIZEN NOBODY CAN SEE WALKS AT HALF THE FRAME RATE. The fog of war hides
    /// nearly every body in the city from the player (2,800 of 2,825 at the opening
    /// view, 2026-09-06), and a hidden man is ticked every other frame with the two
    /// frames' time together: he covers the same ground, his timers run the same
    /// seconds, his animation blends over the same span - nobody is watching the
    /// frames in between. Odd and even citizens take turns so the load is level. A
    /// man in the open, or with no arena to ask, is ticked every frame as before.
    /// </summary>
    static class CivilianCadence
    {
        // faster than any citizen moves: the flee is ~4 m/s and a crossing hustles it
        // by 1.35 (PedestrianAgent.GraphPace), 5.4 m/s, with room to spare
        const float FastestPace = 6f;

        static readonly System.Action<CivilianAgent, float> TickCitizen = (c, d) => c.TickCivilian(d);

        public static void Tick(List<CivilianAgent> citizens, DemoCrews fog, float dt) =>
            Tick(citizens, fog, dt, TickCitizen);

        /// <summary>The same cadence for any walker the fog may hide - the harbour's
        /// dock hands, the field's ramp crew - with its own tick.</summary>
        public static void Tick<T>(List<T> walkers, DemoCrews fog, float dt, System.Action<T, float> tick)
            where T : PedestrianAgent
        {
            int frame = Time.frameCount;
            for (int i = 0; i < walkers.Count; i++)
            {
                var citizen = walkers[i];
                // Two frames' time is paid in one tick, and a tick's step is capped
                // (PedestrianAgent.MaxStepPerFrame): a hidden man is only deferred while
                // the combined time cannot reach that cap at the fastest civilian pace,
                // so a slow frame rate never shortens his stride.
                if (fog != null && fog.FoggedOut(citizen.Tf) && ((frame + i) & 1) != 0 &&
                    (dt + dt + citizen.OwedSeconds) * FastestPace <= PedestrianAgent.MaxStepPerFrame)
                {
                    citizen.OwedSeconds += dt;
                    continue;
                }
                float owed = citizen.OwedSeconds;
                citizen.OwedSeconds = 0f;
                // a hitch after a deferral: the debt is paid as its own tick, so the
                // per-tick step cap never eats the metres the two frames were owed
                if (owed > 0f && (dt + owed) * FastestPace > PedestrianAgent.MaxStepPerFrame)
                {
                    tick(citizen, owed);
                    tick(citizen, dt);
                }
                else tick(citizen, dt + owed);
            }
        }
    }
}
