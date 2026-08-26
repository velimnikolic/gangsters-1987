using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>A row of cars left standing at a street's kerb - the geometry a crew
    /// walks round, takes cover behind and squeezes past. Nobody drives them; they are
    /// put down once and handed to the road (<see cref="StoodCar"/>) so the traffic
    /// plans round them.
    ///
    /// It is one class because every scene that laid a row by hand got the same two
    /// things wrong, and both are the same mistake: the body is never measured.
    ///
    ///   * A car stands at the kerb by ITS OWN width (StreetKit.KerbInset). The labs
    ///     both stood every body at the offset of a 1.9 m car, and the pack's bodies
    ///     run from 2.07 m across to 2.78 - the SUV hung 0.82 m over the stone, the
    ///     pickup 0.71, the low car 0.65. That is a car parked on the pavement.
    ///   * A car takes up ITS OWN length. Six metres of bodywork dropped at a random
    ///     point of seventeen metres of kerb meets itself soon enough, and two bodies
    ///     dealt the same yard of kerb stand inside one another.
    ///
    /// So a spot is claimed, nose to tail, before the body is stood on it, and a body
    /// that cannot be given a clear one is not put down at all.</summary>
    public static class KerbCars
    {
        /// <summary>What a car wants of the kerb beyond its own length: the room a
        /// driver leaves himself to get out of the space again.</summary>
        public const float Gap = 1.4f;

        /// <summary>A car as it was put down: the body, and the world box its meshes
        /// fill where it stands - what the caller blocks off, reserves or dresses
        /// round, so nothing has to measure it a second time.</summary>
        public struct Stood
        {
            public GameObject Go;
            public Bounds Box;
        }

        /// <summary>Stand up to <paramref name="count"/> bodies at the two kerbs of one
        /// street, sides alternating. The street runs along world X
        /// (<paramref name="alongX"/>) or world Z, its centre line is at
        /// <paramref name="centre"/> on the other axis, and
        /// <paramref name="spans"/> are the stretches of it, in the along-axis, a car
        /// may be left on - a junction, a crossing or a driveway is simply left out of
        /// them.
        ///
        /// Which way a car points is not a choice: it stands the way the lane it is
        /// parked in runs, right-hand traffic.</summary>
        public static List<Stood> Park(Transform root, IList<GameObject> bodies, int count,
            bool alongX, float centre, float halfRoad, float roadY,
            IList<Vector2> spans, bool paint = true)
        {
            var stood = new List<Stood>(Mathf.Max(0, count));
            if (root == null || bodies == null || bodies.Count == 0 ||
                count <= 0 || spans == null || spans.Count == 0) return stood;

            // what each kerb has already given away, nose to tail: [0] the +side (north
            // kerb of a street along X, east kerb of one along Z), [1] the -side
            var taken = new[] { new List<Vector2>(), new List<Vector2>() };

            for (int i = 0; i < count; i++)
            {
                var prefab = bodies[Random.Range(0, bodies.Count)];
                if (prefab == null) continue;
                int side = (i % 2 == 0) ? 1 : -1;
                // the lane's own direction: on a street along X the far kerb's traffic
                // runs west and the near kerb's east; along Z, the east kerb runs north
                float yaw = alongX ? (side > 0 ? 270f : 90f) : (side > 0 ? 0f : 180f);

                var go = Object.Instantiate(prefab, new Vector3(0f, roadY, 0f),
                                            Quaternion.Euler(0f, yaw, 0f), root);
                go.name = go.name.Replace("(Clone)", "");
                var box = Measure(go);
                if (box.size.sqrMagnitude <= 0f) { Discard(go); continue; }

                float halfLength = alongX ? box.extents.x : box.extents.z;
                float halfWidth = alongX ? box.extents.z : box.extents.x;
                if (!Spot(spans, taken[side > 0 ? 0 : 1], halfLength, out float at))
                {
                    Discard(go);   // this kerb has no clear length left for it
                    continue;
                }

                float across = centre + side * StreetKit.KerbInset(halfRoad, halfWidth);
                var want = alongX ? new Vector3(at, roadY, across) : new Vector3(across, roadY, at);
                // the TIN goes on the spot, not the pivot: a pack body's mesh sits a
                // hand's width off its origin, and the kerb is measured against the tin.
                // The height it was instantiated at is the road, and stays.
                var slide = want - box.center;
                slide.y = 0f;
                go.transform.position += slide;
                box.center += slide;

                // a colour of its own, unless the body carries somebody's livery - or a
                // row of parked cars is the only thing in the scene still wearing what
                // the pack shipped, beside repainted traffic on the same bodies
                if (paint) LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Object.Destroy(col);
                // the ground it stands on is gone for anybody on foot, and the box
                // carries its height, so a man can still see over it (WalkObstacles)
                WalkObstacles.Block(box);
                // and it is ON THE ROAD: a car coming round the corner has to go round
                // it, which it only knows if the thing is among the road's users
                StoodCar.Park(go);
                stood.Add(new Stood { Go = go, Box = box });
            }

            if (stood.Count < count)
                Debug.Log("[KerbCars] " + root.name + ": room at the kerb for " + stood.Count +
                          " of " + count + " cars.");
            return stood;
        }

        /// <summary>A clear length of kerb for a body of this half length: dealt at
        /// random first, since a row at an even pitch reads as a car park, and swept
        /// end to end only when the random draws keep landing on somebody.</summary>
        static bool Spot(IList<Vector2> spans, List<Vector2> taken, float halfLength, out float at)
        {
            at = 0f;
            float need = halfLength + Gap;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var span = spans[Random.Range(0, spans.Count)];
                float lo = Mathf.Min(span.x, span.y) + halfLength;
                float hi = Mathf.Max(span.x, span.y) - halfLength;
                if (hi <= lo) continue;
                float a = Random.Range(lo, hi);
                if (Occupied(taken, a - need, a + need)) continue;
                taken.Add(new Vector2(a - halfLength, a + halfLength));
                at = a;
                return true;
            }
            // the kerb is tight: walk it. Half a metre at a time is finer than any body
            // is short, so this finds a gap if the street is holding one at all.
            for (int s = 0; s < spans.Count; s++)
            {
                float lo = Mathf.Min(spans[s].x, spans[s].y) + halfLength;
                float hi = Mathf.Max(spans[s].x, spans[s].y) - halfLength;
                for (float a = lo; a <= hi; a += 0.5f)
                {
                    if (Occupied(taken, a - need, a + need)) continue;
                    taken.Add(new Vector2(a - halfLength, a + halfLength));
                    at = a;
                    return true;
                }
            }
            return false;
        }

        /// <summary>A body that was measured and then found no room. Destroy leaves it
        /// standing until the end of the frame, and in the editor with nothing playing
        /// it leaves it standing for good - which would be a car parked on top of
        /// another, the very thing this class is here to stop.</summary>
        static void Discard(GameObject go)
        {
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        static bool Occupied(List<Vector2> taken, float from, float to)
        {
            for (int i = 0; i < taken.Count; i++)
                if (from < taken[i].y && to > taken[i].x) return true;
            return false;
        }

        /// <summary>The world box this body's meshes fill where it stands.</summary>
        static Bounds Measure(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
