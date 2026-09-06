using UnityEngine;

namespace RoadDemo
{
    /// <summary>Who a crew can SEE to start something with: the two scans TickCombat
    /// asks of every crew without a target, read over the tick's one picture of the
    /// street (StreetPicture) and judged live for the few men inside range.</summary>
    public partial class DemoCrews
    {
        readonly StreetPicture _picture = new StreetPicture();
        // true only inside TickCombat, where one picture serves every scan of the tick
        bool _snapLive;

        /// <summary>The picture this scan reads: the tick's own inside TickCombat, a
        /// fresh one anywhere else. A crew on nobody's list (a bench's hand-dealt mob)
        /// is added to it so it can look at the street like any other.</summary>
        int SnapFor(Unit unit)
        {
            if (!_snapLive) _picture.Take(Units);
            for (int i = 0; i < _picture.Crews.Count; i++)
                if (ReferenceEquals(_picture.Crews[i].Unit, unit)) return i;
            return _picture.Add(unit);
        }

        /// <summary>The old walk's own test of a man on the other side, asked live of the
        /// few men inside range: alive, with a body that is switched on, not sat in a
        /// car.</summary>
        bool OnTheStreet(CrewWalker man) =>
            man != null && !man.Dead && man.Tf != null &&
            man.Tf.gameObject.activeInHierarchy && !IsAboard(man);

        /// <summary>The old walk's test of a man on the looking side, asked live.</summary>
        static bool Looking(CrewWalker man) =>
            man != null && !man.Dead && man.Tf != null && man.Tf.gameObject.activeInHierarchy;

        /// <summary>A crew with an ORDER on this one, one of whose men is on foot with
        /// his gun out, in sight of one of these men inside SightRange. Never the
        /// law, never a car going by, never a man lying in wait.</summary>
        Unit EnemyComing(Unit unit)
        {
            int ui = SnapFor(unit);
            var us = _picture.Crews[ui];
            if (!us.AnyPresent) return null;
            float r2 = SightRange * SightRange;
            for (int oi = 0; oi < _picture.Crews.Count; oi++)
            {
                var other = _picture.Crews[oi].Unit;
                if (other == unit || other.Faction == unit.Faction || other.Wiped) continue;
                if (other.IsPolice || other.TargetUnit != unit || !other.OrderedFight) continue;
                var os = _picture.Crews[oi];
                if (!os.AnyPresent || StreetPicture.Apart(us, os, SightRange)) continue;
                for (int i = us.Start; i < us.Start + us.Count; i++)
                {
                    var a = _picture.Men[i];
                    if (!a.Present || !Looking(a.Walker)) continue;
                    for (int j = os.Start; j < os.Start + os.Count; j++)
                    {
                        var b = _picture.Men[j];
                        // on his feet and in the fight: not a rider, not a passenger,
                        // not a man off on a raid, not one with his hands up or running
                        if (b.Present && (a.At - b.At).sqrMagnitude < r2 &&
                            CanEngageOnFoot(b.Walker) && b.Walker.Armed && !b.Walker.Surrendered &&
                            !b.Walker.Retreating &&
                            !Concealed(b.Walker, a.At) &&
                            InSight(a.At, b.At))
                            return other;
                    }
                }
            }
            return null;
        }

        Unit EnemyWithin(Unit unit, float range, bool provoked, bool noPolice = false)
        {
            int ui = SnapFor(unit);
            var us = _picture.Crews[ui];
            if (!us.AnyPresent) return null;
            float r2 = range * range;
            for (int oi = 0; oi < _picture.Crews.Count; oi++)
            {
                var other = _picture.Crews[oi].Unit;
                if (other == unit || other.Faction == unit.Faction || other.Wiped) continue;
                if (noPolice && other.IsPolice) continue;
                var os = _picture.Crews[oi];
                // the circles before the stance: the stance reads the territory under
                // the other crew, and most pairs of crews are streets apart
                if (!os.AnyPresent || StreetPicture.Apart(us, os, range)) continue;
                if (!MayEngage(unit, other, provoked)) continue;
                for (int i = us.Start; i < us.Start + us.Count; i++)
                {
                    var a = _picture.Men[i];
                    if (!a.Present || !Looking(a.Walker)) continue;
                    // a man in a car is just a car going by until somebody shoots
                    for (int j = os.Start; j < os.Start + os.Count; j++)
                    {
                        var b = _picture.Men[j];
                        // close enough AND in view: a crew on the far side of a block of
                        // flats has not "seen the outfit walk up", whatever the tape says -
                        // and a man LYING IN WAIT is not walking up at all (COVER-004)
                        if (b.Present && (a.At - b.At).sqrMagnitude < r2 &&
                            OnTheStreet(b.Walker) &&
                            !Concealed(b.Walker, a.At) &&
                            InSight(a.At, b.At))
                            return other;
                    }
                }
            }
            return null;
        }
    }
}
