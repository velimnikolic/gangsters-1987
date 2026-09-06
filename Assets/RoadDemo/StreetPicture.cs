using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// THE STREET'S PICTURE OF EVERY MAN, TAKEN ONCE A TICK. The arena's enemy scans
    /// (DemoCrews.Sight) ask, for every crew still looking for a fight, of every man of
    /// every other crew: is he on his feet, is he in a car, where is he. In the full
    /// city that is fourteen thousand pairs a frame, each paying native calls (the
    /// body's activeInHierarchy, the car lists) before the distance was even looked at.
    ///
    /// The picture is a PREFILTER, never the verdict. Nobody moves inside TickCombat
    /// and nobody's body is switched on inside it, so a crew's bounding circle from
    /// the picture lets a pair of crews too far apart for any two of their men to be
    /// within range skip the men altogether, and a man the picture found absent is
    /// absent for the tick. A man it found present is judged again, live, the way the
    /// old walk judged him (alive, body on, not in a car) - but only once he is inside
    /// range, which is the handful of pairs a frame that matter. The tick itself can
    /// take men off the street partway through (TakeOffRetreated), and that live look
    /// is what keeps a later crew from engaging a man who has gone.
    /// </summary>
    sealed class StreetPicture
    {
        public struct Man
        {
            public CrewWalker Walker;
            public Vector3 At;
            public bool Present;   // alive, with a body, and that body switched on
        }

        public struct Crew
        {
            public DemoCrews.Unit Unit;
            public int Start, Count;   // its men, in Men
            public Vector3 Centre;     // of the present men
            public float Radius;       // the furthest present man from the centre
            public bool AnyPresent;
        }

        public readonly List<Man> Men = new List<Man>();
        public readonly List<Crew> Crews = new List<Crew>();

        /// <summary>The whole street, crew by crew, in the arena's order.</summary>
        public void Take(List<DemoCrews.Unit> units)
        {
            Men.Clear();
            Crews.Clear();
            foreach (var unit in units) Add(unit);
        }

        /// <summary>One crew more; its index in <see cref="Crews"/>.</summary>
        public int Add(DemoCrews.Unit unit)
        {
            var snap = new Crew { Unit = unit, Start = Men.Count };
            var sum = Vector3.zero;
            int present = 0;
            foreach (var man in unit.All())
            {
                var m = new Man { Walker = man };
                if (man != null && !man.Dead && man.Tf != null &&
                    man.Tf.gameObject.activeInHierarchy)
                {
                    m.Present = true;
                    m.At = man.Tf.position;
                    sum += m.At;
                    present++;
                }
                Men.Add(m);
            }
            snap.Count = Men.Count - snap.Start;
            if (present > 0)
            {
                snap.AnyPresent = true;
                snap.Centre = sum / present;
                float far = 0f;
                for (int i = snap.Start; i < snap.Start + snap.Count; i++)
                    if (Men[i].Present)
                        far = Mathf.Max(far, (Men[i].At - snap.Centre).sqrMagnitude);
                snap.Radius = Mathf.Sqrt(far);
            }
            Crews.Add(snap);
            return Crews.Count - 1;
        }

        /// <summary>No two men of these crews can be within <paramref name="range"/>
        /// of each other: the crews' circles are further apart than that.</summary>
        public static bool Apart(in Crew a, in Crew b, float range)
        {
            float reach = range + a.Radius + b.Radius;
            return (a.Centre - b.Centre).sqrMagnitude >= reach * reach;
        }
    }
}
