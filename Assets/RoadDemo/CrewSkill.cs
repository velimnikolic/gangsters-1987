using LivingCity.Personnel;

namespace RoadDemo
{
    /// <summary>
    /// Where the ledger's attribute sheet meets the street. Two jobs, both shared
    /// rather than scene-local on purpose (the house rule: behaviour lives in shared
    /// classes and scenes only configure them) - turn a man's half-steps into the
    /// multiplier the fight applies, and bank what he learns doing it back onto his
    /// line in the book.
    ///
    /// The banking is deliberately ONE LESSON A DAY, per man and per kind of work. A
    /// firefight is one lesson however many rounds it takes: without the cap a crew
    /// pinned down for a minute of real time would come home better shots than a crew
    /// that spent a fortnight working, and the improvement system would reward standing
    /// in the open. What the lesson is WORTH is not decided here - it comes off
    /// <see cref="ActivityXp"/> like every other point of practice in the game.
    /// </summary>
    public static class CrewSkill
    {
        /// <summary>What the gun's own accuracy is multiplied by: 0.82 at one star,
        /// 1.30 at five.</summary>
        public static float Aim(int halfSteps) =>
            0.70f + 0.06f * AttributeScale.Clamp(halfSteps);

        // ------------------------------------------------ the closer threat (EPIC 33)
        //
        // THE PURE CORE OF THE RETARGET, AND OF THE SCATTER. Three numbers a man's
        // Combat stat decides, and one verdict off them. Not one of these touches a
        // Transform, a Unit or the city's walls: they take metres and seconds and give
        // back metres and seconds, so the offline suite can drive the whole policy with
        // the editor shut and the live fight is left holding nothing but the geometry
        // (AIM-002, D7).
        //
        // The table, from the user's word of 2026-09-03:
        //
        //   Combat            margin   dwell   miss cone
        //   1 star  (2 hs)     4.0 m   0.90 s     1.25x
        //   3 stars (6 hs)     3.0 m   0.55 s     1.00x
        //   5 stars (10 hs)    2.0 m   0.25 s     0.75x
        //
        // The better shot notices a SMALLER but real positional advantage, and notices
        // it SOONER. No roll decides whether he notices: the same two men in the same
        // two places always reach the same verdict, which is the only way a fight can
        // be argued about after the fact.

        /// <summary>How much nearer than his current mark another man has to be before
        /// he is worth turning onto - metres of street, not a share of the gun's reach
        /// (D3). The rule is about who is closing on the shooter; a shotgun does not
        /// make an enemy less close.</summary>
        public static float ThreatMargin(int halfSteps) =>
            Across(halfSteps, 4.0f, 3.0f, 2.0f);

        /// <summary>How long the advantage has to HOLD before he acts on it. A dwell
        /// and not a polling interval (D2): the condition is measured continuously and
        /// the clock restarts the moment it lapses, so a man who dips inside the margin
        /// for a stride never takes the aim and the same geometry gives the same answer
        /// every run.</summary>
        public static float ThreatDwell(int halfSteps) =>
            Across(halfSteps, 0.90f, 0.55f, 0.25f);

        /// <summary>What his hands do to the width of a missed round's cone.</summary>
        public static float MissCone(int halfSteps) =>
            Across(halfSteps, 1.25f, 1.00f, 0.75f);

        /// <summary>The half-angle a missed round may leave the barrel at, in degrees:
        /// the gun's own cone widened or tightened by the man behind it.
        ///
        /// AN ANGLE, NOT AN OFFSET (D6). A fixed sideways nudge of a metre or so is
        /// invisible on screen and says nothing about range; a cone opens with distance
        /// on its own, which is why a one-star rifleman at twenty-five metres misses
        /// wide enough to watch and a five-star at the same range does not.
        ///
        /// The gun's share comes off its OWN accuracy rather than a second table, so a
        /// weapon added tomorrow needs no new number: 4 degrees for the steadiest piece
        /// in the game and ten more as accuracy falls away. Pistol 0.55 -> 8.5 deg,
        /// machine pistol 0.30 -> 11 deg, Tommy gun 0.35 -> 10.5 deg, rifle 0.88 ->
        /// 5.2 deg, shotgun 0.97 -> 4.3 deg.</summary>
        public static float MissConeDegrees(float accuracy, int halfSteps) =>
            BaseMissConeDegrees(accuracy) * MissCone(halfSteps);

        /// <summary>The gun's own cone, before the man behind it.</summary>
        public static float BaseMissConeDegrees(float accuracy)
        {
            if (accuracy < 0f) accuracy = 0f;
            else if (accuracy > 1f) accuracy = 1f;
            return 4f + 10f * (1f - accuracy);
        }

        /// <summary>How much of the cone a missed round is allowed to climb or dip
        /// against how far it may go wide: about a third. A round that misses a man
        /// goes past his shoulder rather more often than over his head.</summary>
        public const float MissPitchShare = 0.34f;

        /// <summary>
        /// WHERE INSIDE THE CONE ONE MISSED ROUND WENT - the yaw across the street and
        /// the pitch up or down, in degrees, off two rolls in [0,1).
        ///
        /// Drawn as a POINT IN A DISC and not as two independent numbers, and that is
        /// the whole reason this is a function rather than two Random.Range calls at the
        /// callsite. Yaw and pitch each drawn at their own maximum put a corner round
        /// outside the cone the table advertises, the trace reports and the acceptance
        /// measures - a shooter quietly worse than his sheet says he is, and telemetry
        /// that cannot be trusted to catch it. Here the radius is bounded by the cone
        /// before it is split into the two axes, so no draw can leave it:
        /// sqrt(yaw^2 + pitch^2) &lt;= coneDegrees, always, and the widest draw reaches
        /// the cone exactly rather than falling short of it.
        ///
        /// The radius carries a square root so the draw is even over the disc rather
        /// than piled up on the aim line: a spread, which is what a spread looks like.
        /// The pitch share squashes that disc vertically afterwards, which can only
        /// bring a round further inside the bound.
        ///
        /// Pure, like everything else in this block: two numbers in, two numbers out,
        /// so the offline suite can sweep the whole cone without a scene.
        /// </summary>
        public static void MissAngles(float coneDegrees, float radiusRoll,
                                      float azimuthRoll,
                                      out float yawDegrees, out float pitchDegrees)
        {
            yawDegrees = 0f;
            pitchDegrees = 0f;
            if (coneDegrees <= 0f) return;
            float radius = coneDegrees * (float)System.Math.Sqrt(Unit(radiusRoll));
            double azimuth = Unit(azimuthRoll) * 2.0 * System.Math.PI;
            yawDegrees = radius * (float)System.Math.Cos(azimuth);
            pitchDegrees = radius * (float)System.Math.Sin(azimuth) * MissPitchShare;
        }

        /// <summary>A roll held inside [0,1] - a caller's stream is trusted for its
        /// spread and not for its bounds.</summary>
        static float Unit(float roll) => roll < 0f ? 0f : (roll > 1f ? 1f : roll);

        /// <summary>How far off the aim line a round drawn at these two angles actually
        /// leaves, in degrees. The yaw and the pitch are perpendicular turns and do not
        /// simply add: the round's direction is the aim line plus a tangent offset on
        /// each axis, and this is the angle of the result. What the acceptance measures,
        /// and what the trace's `off` field reports.</summary>
        public static float MissOffAxisDegrees(float yawDegrees, float pitchDegrees)
        {
            double y = System.Math.Tan(yawDegrees * System.Math.PI / 180.0);
            double p = System.Math.Tan(pitchDegrees * System.Math.PI / 180.0);
            return (float)(System.Math.Atan(System.Math.Sqrt(y * y + p * p)) *
                           180.0 / System.Math.PI);
        }

        /// <summary>
        /// Should this shooter abandon the man he is aiming at for the one he is
        /// watching? Both distances are HORIZONTAL street metres (D8) and both are
        /// measured the same way, so a mark stood on a kerb and one in the road are
        /// compared as the street compares them.
        ///
        /// Two conditions and no third: the candidate beats the current mark by the
        /// whole of the skill's margin, and the advantage has held for the whole of the
        /// skill's dwell. Margin plus dwell IS the hysteresis - after A gives way to B,
        /// B is what the next candidate has to beat, so two men stood nearly level
        /// cannot flicker the aim between them.
        /// </summary>
        public static bool ShouldSwitch(float currentDistXZ, float candidateDistXZ,
                                        int halfSteps, float heldFor) =>
            candidateDistXZ + ThreatMargin(halfSteps) <= currentDistXZ &&
            heldFor >= ThreatDwell(halfSteps);

        /// <summary>The table above read at a half-step, straight-lined between the
        /// three stars it was written for. Piecewise on purpose: the user gave three
        /// rows and the halves between them are read off the rows, not off a curve
        /// nobody chose.</summary>
        static float Across(int halfSteps, float atOneStar, float atThreeStars,
                            float atFiveStars)
        {
            int hs = AttributeScale.Clamp(halfSteps);
            if (hs <= 6)
                return atOneStar + (atThreeStars - atOneStar) * (hs - 2) / 4f;
            return atThreeStars + (atFiveStars - atThreeStars) * (hs - 6) / 4f;
        }

        /// <summary>
        /// A round that found its mark taught him something - firing off a magazine
        /// into a wall teaches nobody anything, which is why this is called on the hit
        /// and not on the shot. Ignores rivals, who carry negative ids and are on
        /// nobody's books, and men whose crews are dealt in a scene with no ledger
        /// behind them.
        /// </summary>
        public static void Landed(int characterId) =>
            Learn(characterId, Activity.AttackOnARival, XpOutcome.Completed);

        /// <summary>
        /// He got them out. The man at the bars never fires - both hands stay on them -
        /// so without this the one man on a drive-by who did the hardest part of it
        /// would come home having learned nothing at all.
        /// </summary>
        public static void Drove(int characterId, bool clean) =>
            Learn(characterId, Activity.Getaway,
                clean ? XpOutcome.Completed : XpOutcome.Partial);

        /// <summary>
        /// He walked the round and stood at the door (XP-003). Ordering a shakedown on
        /// paper trains a man; sending the same man to do it on his feet has to train
        /// him too, or the book teaches what the street does not. A door that paid is a
        /// job done; a door that did not is the half-paid collection the table already
        /// has a word for.
        /// </summary>
        public static void Collected(int characterId, bool paid) =>
            Learn(characterId, Activity.RacketCollection,
                paid ? XpOutcome.Completed : XpOutcome.Partial);

        /// <summary>
        /// He leaned on somebody at his own door - asked, threatened, or swung for it
        /// (XP-003). What he learns is the same lesson the ordered shakedown banks,
        /// because it is the same work.
        /// </summary>
        public static void Leaned(int characterId, bool gaveIn) =>
            Learn(characterId, Activity.Leaning,
                gaveIn ? XpOutcome.Completed : XpOutcome.Partial);

        static void Learn(int characterId, Activity activity, XpOutcome outcome)
        {
            if (characterId < 0)
                return;

            // EVERY house's men learn their trades. Ids are unique across all
            // twenty-one books by construction, so the man is found by asking the
            // houses in turn and nothing anywhere has to know whose he is.
            var member = Man(characterId);
            if (member == null || member.Gone)
                return;

            var day = LivingCity.Gameplay.OutfitDirector.Instance != null
                ? LivingCity.Gameplay.OutfitDirector.Instance.Campaign.Day
                : 0;
            if (!Allow(characterId, activity, day))
                return;

            ActivityXp.Award(member, activity, outcome);
        }

        /// <summary>The man behind a character id, in whichever family's book he
        /// stands. Null for a body on nobody's books.</summary>
        static LivingCity.Personnel.Character Man(int characterId)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            if (underworld == null)
            {
                var director = LivingCity.Gameplay.PersonnelDirector.Instance;
                return director != null && director.Roster != null
                    ? director.Roster.Find(characterId)
                    : null;
            }

            for (var gangId = 0; gangId < underworld.Count; gangId++)
            {
                var found = underworld.Of(gangId)?.Roster?.Find(characterId);
                if (found != null)
                    return found;
            }
            return null;
        }

        // Whose lessons have been counted, and for which day. Cleared wholesale the
        // moment the day changes rather than per entry: one comparison, and the table
        // can never carry a stale count into tomorrow. The key is the man AND the kind
        // of work, so a night that involved both a firefight and a getaway teaches him
        // both without either standing in for the other.
        static readonly System.Collections.Generic.HashSet<long> Counted =
            new System.Collections.Generic.HashSet<long>();
        static int countedDay = -1;

        static bool Allow(int characterId, Activity activity, int day)
        {
            if (day != countedDay)
            {
                Counted.Clear();
                countedDay = day;
            }

            return Counted.Add((long)characterId * 64 + (int)activity);
        }

        // Static state outlives Play when domain reload is off - the same trap
        // OverlayRegistry and DayClock reset against.
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Counted.Clear();
            countedDay = -1;
        }
    }
}
