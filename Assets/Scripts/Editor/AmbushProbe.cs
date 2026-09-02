using System.Linq;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace GangstersTools
{
    /// <summary>
    /// THE SACEKUSA, from the terminal (EPIC 28).
    ///
    /// The right click the player makes on a bin or on somebody else's parked car, and
    /// the two things about it that no unattended run reaches by luck: the same click
    /// given to a crew that is ALREADY in a fight (which is manual cover - the fight
    /// goes on from the new flanks), and the car under a lying-in-wait crew driving off.
    ///
    /// Read-only with no flags: it says who is holding what, and whether the tin they
    /// are behind is still there.
    /// </summary>
    public static class AmbushPipelineCommands
    {
        sealed class Point3
        {
            public float x, y, z;
            public Point3(Vector3 v) { x = Mathf.Round(v.x * 100f) / 100f; y = Mathf.Round(v.y * 100f) / 100f; z = Mathf.Round(v.z * 100f) / 100f; }
        }

        static Point3 Point(Vector3 v) => new Point3(v);

        [CliCommand("gangsters_ambush_probe",
                    "The ambush click from the terminal: who is lying in wait behind what. " +
                    "--order clicks the nearest thing to get behind toward the nearest " +
                    "rival, --fight starts the fight first (manual cover), --drive takes " +
                    "the tin they are behind off the road.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "gameplay" })]
        public static object Probe(
            [CliArg("order", "Order the ambush at the nearest anchor toward the nearest rival.")] bool order = false,
            [CliArg("fight", "Start the fight first, so the click is manual cover on a live mark.")] bool fight = false,
            [CliArg("car", "Take somebody else's parked car as the anchor, not a prop.")] bool car = false,
            [CliArg("drive", "Take the anchor the men are behind off the road - a car pulling away.")] bool drive = false,
            [CliArg("within", "Metres round the point between the crews to look for something to get behind.")] float within = 16f)
        {
            if (!Application.isPlaying)
                return new { ok = false, reason = "Play Mode is not running." };
            var crews = Object.FindAnyObjectByType<DemoCrews>();
            var ours = crews?.Units.FirstOrDefault(u => u != null && u.Faction == 0 && !u.Wiped);
            if (ours == null)
                return new { ok = false, reason = crews == null ? "No live DemoCrews." : "No live Outfit crew." };

            string ordered = null;
            if (order)
            {
                var rival = crews.Units
                    .Where(u => u != null && u.Faction != ours.Faction && !u.Wiped && !u.IsPolice)
                    .OrderBy(u => (u.Position - ours.Position).sqrMagnitude).FirstOrDefault();
                if (rival == null) return new { ok = false, reason = "No live rival." };
                crews.Select(ours);
                if (fight && !crews.OrderAttack(rival))
                    return new { ok = false, reason = "The attack order was refused." };

                var toward = rival.Position - ours.Position;
                toward.y = 0f;
                float gap = toward.magnitude;
                var look = ours.Position +
                           (gap > 1f ? toward.normalized : Vector3.forward) * Mathf.Min(40f, gap * 0.5f);
                DemoCrews.CoverAnchor anchor;
                if (car)
                {
                    var tin = NearestStood(look, Mathf.Max(2f, within));
                    anchor = DemoCrews.AnchorOf(tin);
                    if (!anchor.Valid)
                        return new { ok = false, reason = $"No car stood within {within:F0} m of the line." };
                }
                else if (!DemoCrews.AnchorNear(look, Mathf.Max(2f, within), out anchor))
                    return new { ok = false, reason = $"Nothing to get behind within {within:F0} m of the line." };
                ordered = crews.OrderAmbush(ours, anchor, run: false)
                    ? (anchor.IsCar ? "a car" : "a prop")
                    : null;
                if (ordered == null)
                    return new { ok = false, reason = crews.AmbushRefusal ?? "The ambush order was refused." };
            }

            string drove = null;
            if (drive)
            {
                // A CAR PULLS AWAY. Nothing here drives it - taking it off the road's
                // books is exactly what a despawn does, and it is the half of COVER-005
                // that no unattended run reaches (a StoodCar never moves).
                // the man who is actually behind a CAR, not merely the first man with a
                // flank - most of a crew is behind bins, and taking the tin nearest one
                // of those off the road proves nothing
                var held = ours.All().FirstOrDefault(m => m != null && !m.Dead && m.BehindTin);
                var under = held != null ? NearestStood(held.HeldCover.Value, 4f) : null;
                if (under == null) return new { ok = false, reason = "Nobody of the crew is behind a car." };
                StreetTraffic.Users.Remove(under);
                drove = "took one off the road's books";
            }

            return new
            {
                ok = true,
                ordered,
                drove,
                crew = ours.GangName,
                word = CrewStatus.Short(ours),
                men = ours.All().Where(m => m != null && !m.Dead).Select(m => new
                {
                    who = m.DisplayName,
                    held = m.HeldCover.HasValue,
                    heldAt = m.HeldCover.HasValue ? Point(m.HeldCover.Value) : null,
                    behindTin = m.BehindTin,
                    hidden = m.Hidden,
                    lurking = m.Lurking,
                    armed = m.Armed,
                    inCover = m.InCover,
                    fighting = m.Target != null ? m.Target.DisplayName : null,
                    fromHeld = m.HeldCover.HasValue && m.CoverSpot.HasValue &&
                               (m.CoverSpot.Value - m.HeldCover.Value).sqrMagnitude < 0.01f,
                    onRoad = InCarriageway(m.Tf.position),
                }).ToArray(),
            };
        }

        /// <summary>Is he stood in the traffic's half of the street? The walker's own
        /// reading is internal to the runtime assembly, so the same question is asked of
        /// the lane net here: NOBODY GUARDS THE SPACE WHERE A CAR WAS (COVER-005), and
        /// this is the field that says whether anybody is.</summary>
        static bool InCarriageway(Vector3 p)
        {
            var net = LaneNet.Active;
            if (net == null) return false;
            var road = net.Locate(p, out _, out float d, 8f);
            return road != null && Mathf.Abs(d) < road.HalfRoad;
        }

        /// <summary>The road user stood nearest a flank - whose tin the man behind it is
        /// using, near enough for this bench's purposes.</summary>
        static IRoadUser NearestStood(Vector3 spot, float within)
        {
            IRoadUser best = null;
            float bestD = within * within;
            for (int i = 0; i < StreetTraffic.Users.Count; i++)
            {
                var u = StreetTraffic.Users[i];
                if (u == null || u.RoadSpeed > 0.5f) continue;
                float d = (u.RoadPosition - spot).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = u;
            }
            return best;
        }
    }
}
