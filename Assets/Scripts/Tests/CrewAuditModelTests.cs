using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>
    /// Pure contracts behind CrewAudit's live formation checks and the streamed
    /// walking-obstacle classifier. No scene, walker or UnityEngine.Object is created,
    /// so these run while the editor is idle.
    /// </summary>
    public static class CrewAuditModelTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();
            AlignedStepsStayTogether(failures);
            DivergentStepIsMeasured(failures);
            StationaryJitterHasNoHeading(failures);
            GroundSpreadIgnoresHeight(failures);
            BreachNeedsItsWholeGrace(failures);
            ClearingABreachResetsTheClock(failures);
            ComposedCafeFurnitureIsPhysical(failures);
            ComposedVenueShellsArePhysical(failures);
            CombatCornerHandoffDoesNotOrbit(failures);
            MovingWithoutProgressReplans(failures);
            RoutedCombatStrideIsASeparationMover(failures);
            TwoRoutedMoversDoNotCancelOneAnother(failures);
            RejectedRouteStartCanRecover(failures);
            RouteStallUsesRecentTravel(failures);
            RouteOrbitRequiresLostGround(failures);
            CombatRouteFailureReachesCoverCaller(failures);
            RoutedSteerMayStepSidewaysButNotBack(failures);
            CoverUseRequiresAUsefulShotAndShieldedAngle(failures);
            CoverApproachStaysOnTheNearFace(failures);
            BreachedCoverReopensLocalDefense(failures);
            EmptyCoverRecheckKeepsAShieldedFlank(failures);
            FailedCoverHopsEventuallyAdvance(failures);
            RecoveredRangeResetsCoverHopBudget(failures);
            LostSightKeepsAReachedShield(failures);
            StalledFleeReplansAreBounded(failures);
            TemporaryFleeReturnsToALiveThreat(failures);
            CrossUnitAttackerGetsAnAnswer(failures);
            CoverGuardsAreNotDraftedIntoAChase(failures);
            EndedSearchKeepsAVisiblePersonalFight(failures);
            CrossUnitReturnFireDoesNotInheritAnOrder(failures);
            PendingChaseRemovalIsNotAnActiveChaser(failures);
            NearRouteCornerIsRetained(failures);
            MixamoRunPacesClearSharedGaitGate(failures);
            BagSyncRecallHonorsRoundAndDefenceOwnership(failures);
            return failures;
        }

        static void BagSyncRecallHonorsRoundAndDefenceOwnership(List<string> failures)
        {
            if (DemoCrews.BagSyncShouldStationModel(
                    rosterChanged: false, roundAway: false, defenceAway: false) ||
                DemoCrews.BagSyncShouldStationModel(
                    rosterChanged: true, roundAway: true, defenceAway: false) ||
                DemoCrews.BagSyncShouldStationModel(
                    rosterChanged: true, roundAway: false, defenceAway: true) ||
                !DemoCrews.BagSyncShouldStationModel(
                    rosterChanged: true, roundAway: false, defenceAway: false))
                failures.Add("Bag detail: roster sync bypasses round or HQ-defence ownership.");
        }

        static void MixamoRunPacesClearSharedGaitGate(List<string> failures)
        {
            // Root pace read from the imported Mixamo male/female run clips. Their
            // city pace is deliberately capped below that natural pace, but remains
            // inside the supported playback band and must still enter the jog.
            const float maleNatural = 4.3845f;
            const float femaleNatural = 3.7423f;
            if (!CrewWalker.GaitPaceAllowedModel(3.8f, maleNatural, 0.9f, false) ||
                !CrewWalker.GaitPaceAllowedModel(3.37f, femaleNatural, 0.9f, false))
                failures.Add("Crew run: the Mixamo jog is rejected at the city's own pace.");

            // The hysteresis is still there - 3.5 holds a jog it could not have
            // entered - but it no longer reaches down to where the feet visibly beat
            // the ground. Below the band floor the rate clamps UP to it, so a man held
            // at 3.0 against a 4.3845 clip plays 3.95 while covering 3.0: thirty per
            // cent of skate, which is what the crowd used to buy.
            if (!CrewWalker.GaitPaceAllowedModel(3.5f, maleNatural, 0.9f, true) ||
                CrewWalker.GaitPaceAllowedModel(3.5f, maleNatural, 0.9f, false))
                failures.Add("Crew run: the crowd hysteresis is lost.");
            if (CrewWalker.GaitPaceAllowedModel(3f, maleNatural, 0.9f, true) ||
                CrewWalker.GaitPaceAllowedModel(2.5f, maleNatural, 0.9f, true))
                failures.Add("Crew run: a braked man keeps a gait his feet outrun.");
        }

        static void NearRouteCornerIsRetained(List<string> failures)
        {
            if (!WalkRoute.RetainPulledCornerModel(0.005f * 0.005f) ||
                WalkRoute.RetainPulledCornerModel(0f))
                failures.Add("WalkRoute: a required near tangent was silently omitted.");
        }

        static void RouteOrbitRequiresLostGround(List<string> failures)
        {
            if (CrewAudit.RouteOrbitModel(30f, 332f, 29f) ||
                !CrewAudit.RouteOrbitModel(8f, 360f, 2f) ||
                !CrewAudit.RouteOrbitModel(3f, 20f, 0.5f) ||
                CrewAudit.RouteOrbitModel(2f, 360f, 0f))
                failures.Add("CrewAudit: straight pursuit is an orbit or a real loop is missed.");
        }

        static void RoutedSteerMayStepSidewaysButNotBack(List<string> failures)
        {
            float perpendicular = Mathf.Cos(90f * Mathf.Deg2Rad);
            float backwards = Mathf.Cos(110f * Mathf.Deg2Rad);
            if (!CrewWalker.RoutedHeadingAllowedModel(perpendicular) ||
                CrewWalker.RoutedHeadingAllowedModel(backwards))
                failures.Add("Combat route: tangent escape is blocked or reverse fallback is allowed.");
        }

        static void CombatRouteFailureReachesCoverCaller(List<string> failures)
        {
            if (CrewWalker.CombatRouteFailedModel(0.8f, false) ||
                !CrewWalker.CombatRouteFailedModel(0.81f, false) ||
                !CrewWalker.CombatRouteFailedModel(0f, true))
                failures.Add("Combat route: a failed cover approach is hidden by corner reset.");
        }

        static void CoverUseRequiresAUsefulShotAndShieldedAngle(List<string> failures)
        {
            const float range = 18f;
            // Three metres and the gun's full reach are inclusive. A flank outside
            // either edge is not a firing position worth retaining.
            if (!CrewWalker.CoverUsableModel(3f, range, true, 0.5f) ||
                !CrewWalker.CoverUsableModel(range, range, true, 0.5f) ||
                CrewWalker.CoverUsableModel(2.99f, range, true, 1f) ||
                CrewWalker.CoverUsableModel(range + 0.01f, range, true, 1f))
                failures.Add("Cover retention: point-blank or out-of-range cover passed the shot envelope.");

            // A known anchor has to remain between the man and his mark: sixty
            // degrees is the inclusive edge. Legacy/hand-authored cover with no
            // recorded anchor keeps relying on the shot envelope alone.
            if (!CrewWalker.CoverUsableModel(10f, range, true, 0.5f) ||
                CrewWalker.CoverUsableModel(10f, range, true, 0.49f) ||
                !CrewWalker.CoverUsableModel(10f, range, false, -1f))
                failures.Add("Cover retention: the anchor angle does not hold its sixty-degree boundary.");
        }

        static void CoverApproachStaysOnTheNearFace(List<string> failures)
        {
            var anchor = Vector3.zero;
            var protectedSpot = new Vector3(1f, 0f, 0f);
            if (!DemoCrews.CoverApproachSafeModel(
                    protectedSpot, anchor, new Vector3(4f, 0f, 0f)) ||
                DemoCrews.CoverApproachSafeModel(
                    protectedSpot, anchor, new Vector3(-4f, 0f, 0f)))
                failures.Add("Cover approach: a man may still cross round the obstacle onto its far face.");

            // A long car is not useful side-on when the threat is at its nose. The
            // selected face must put the anchor on the same line as the threat.
            var threatAtNose = new Vector3(0f, 0f, 10f);
            if (!DemoCrews.CoverShieldsModel(
                    new Vector3(0f, 0f, -2f), anchor, threatAtNose) ||
                DemoCrews.CoverShieldsModel(
                    new Vector3(2f, 0f, 0f), anchor, threatAtNose))
                failures.Add("Cover geometry: a car side was accepted against a longitudinal threat.");
        }

        static void BreachedCoverReopensLocalDefense(List<string> failures)
        {
            if (!DemoCrews.NeedsLocalDefenseModel(
                    hasCover: false, currentCoverShields: false) ||
                !DemoCrews.NeedsLocalDefenseModel(
                    hasCover: true, currentCoverShields: false) ||
                DemoCrews.NeedsLocalDefenseModel(
                    hasCover: true, currentCoverShields: true))
                failures.Add("Cover breach: an invalid old flank suppresses the local defensive search.");
        }

        static void EmptyCoverRecheckKeepsAShieldedFlank(List<string> failures)
        {
            // A periodic search returning nothing is not an instruction to step into
            // the open. The current flank survives while its anchor still shields the
            // man, independently of whether the mark is presently inside gun range.
            if (!CrewWalker.KeepCoverAfterRecheckModel(
                    hadCover: true, foundCover: false, currentCoverShields: true) ||
                CrewWalker.KeepCoverAfterRecheckModel(
                    hadCover: true, foundCover: false, currentCoverShields: false) ||
                CrewWalker.KeepCoverAfterRecheckModel(
                    hadCover: false, foundCover: false, currentCoverShields: true))
                failures.Add("Cover recheck: an empty search drops a shielding flank or invents one in the open.");

            bool shotOutsideRange = !CrewWalker.CoverUsableModel(
                shotDistance: 24f, range: 18f, anchorKnown: true, anchorDot: 1f);
            bool shieldSurvivesFailedLeapfrog = CrewWalker.KeepCoverAfterRecheckModel(
                hadCover: true, foundCover: false, currentCoverShields: true);
            if (!shotOutsideRange || !shieldSurvivesFailedLeapfrog)
                failures.Add("Cover leapfrog: a shielded flank was dropped when its mark moved out of range and no replacement existed.");

            // A real replacement wins; this answer means "keep the old point", not
            // merely that the man will still have some cover after the recheck.
            if (CrewWalker.KeepCoverAfterRecheckModel(
                    hadCover: true, foundCover: true, currentCoverShields: true))
                failures.Add("Cover recheck: a new flank was ignored in favour of the old one.");

            if (CrewWalker.KeepCoverAfterRecheckModel(
                    hadCover: true, foundCover: false,
                    currentCoverShields: true, tooClose: true))
                failures.Add("Cover recheck: a point-blank flank survives a failed replacement search.");
        }

        static void LostSightKeepsAReachedShield(List<string> failures)
        {
            if (!CrewWalker.GuardCoverAfterLostSightModel(
                    hasCover: true, reachedCover: true, stillShields: true) ||
                CrewWalker.GuardCoverAfterLostSightModel(
                    hasCover: false, reachedCover: true, stillShields: true) ||
                CrewWalker.GuardCoverAfterLostSightModel(
                    hasCover: true, reachedCover: false, stillShields: true) ||
                CrewWalker.GuardCoverAfterLostSightModel(
                    hasCover: true, reachedCover: true, stillShields: false))
                failures.Add("Lost sight: a reached shielding flank is dropped or an invalid one is guarded.");

            var actuallySeen = new Vector3(4f, 0f, 7f);
            var hiddenTransform = new Vector3(-9f, 0f, 3f);
            if (CrewWalker.LostSightGuardPointModel(
                    hasLastSeenTarget: true, lastSeenTarget: actuallySeen,
                    coverThreat: hiddenTransform) !=
                actuallySeen ||
                CrewWalker.LostSightGuardPointModel(
                    hasLastSeenTarget: false, lastSeenTarget: actuallySeen,
                    coverThreat: hiddenTransform) !=
                hiddenTransform)
                failures.Add("Lost sight: cover reacts to the hidden live transform instead of the last visible line.");
        }

        static void FailedCoverHopsEventuallyAdvance(List<string> failures)
        {
            if (CrewWalker.CoverHopShouldReleaseModel(
                    outOfReach: true, failedHops: 0) ||
                CrewWalker.CoverHopShouldReleaseModel(
                    outOfReach: true, failedHops: 1) ||
                !CrewWalker.CoverHopShouldReleaseModel(
                    outOfReach: true, failedHops: 64) ||
                CrewWalker.CoverHopShouldReleaseModel(
                    outOfReach: false, failedHops: 64))
                failures.Add("Cover leapfrog: failed protected hops either release immediately or freeze forever.");
        }

        static void RecoveredRangeResetsCoverHopBudget(List<string> failures)
        {
            if (CrewWalker.CoverHopMissesForRangeModel(
                    outOfReach: false, failedHops: 2) != 0 ||
                CrewWalker.CoverHopMissesForRangeModel(
                    outOfReach: true, failedHops: 2) != 2)
                failures.Add("Cover leapfrog: a new out-of-range spell inherits stale failed hops.");
        }

        static void StalledFleeReplansAreBounded(List<string> failures)
        {
            if (CrewWalker.FleeShouldReplanModel(
                    arrived: true, stalled: true, replans: 0) ||
                CrewWalker.FleeShouldReplanModel(
                    arrived: false, stalled: false, replans: 0))
                failures.Add("Flee route: arrival or a still-moving run spuriously requests a replan.");

            // The first blocked escape must try another reachable line instead of
            // turning the man into a standing target. It must also have a finite
            // retry ceiling, so a boxed-in man cannot re-roll forever.
            bool first = CrewWalker.FleeShouldReplanModel(
                arrived: false, stalled: true, replans: 0);
            int ceiling = -1;
            for (int replans = 1; replans <= 64; replans++)
            {
                if (CrewWalker.FleeShouldReplanModel(
                        arrived: false, stalled: true, replans: replans)) continue;
                ceiling = replans;
                break;
            }
            if (!first || ceiling < 1)
                failures.Add("Flee route: a stalled first leg is abandoned immediately or retries forever.");
            else
            {
                // Once the budget is spent, larger counts must never reopen it.
                for (int replans = ceiling; replans <= ceiling + 4; replans++)
                    if (CrewWalker.FleeShouldReplanModel(
                            arrived: false, stalled: true, replans: replans))
                    {
                        failures.Add("Flee route: the exhausted replan budget becomes available again.");
                        break;
                    }
            }
        }

        static void TemporaryFleeReturnsToALiveThreat(List<string> failures)
        {
            if (!CrewWalker.FleeShouldResumeFightModel(
                    retreating: false, threatAlive: true) ||
                CrewWalker.FleeShouldResumeFightModel(
                    retreating: false, threatAlive: false) ||
                CrewWalker.FleeShouldResumeFightModel(
                    retreating: true, threatAlive: true) ||
                CrewWalker.FleeShouldResumeFightModel(
                    retreating: true, threatAlive: false))
                failures.Add("Flee recovery: temporary panic and a true retreat share the wrong combat outcome.");
        }

        static void CrossUnitAttackerGetsAnAnswer(List<string> failures)
        {
            if (!DemoCrews.AnswerCrossUnitAttackerModel(
                    hasCurrentEnemyUnit: true, sameEnemyUnit: false,
                    attackerVisible: true, canEngage: true) ||
                DemoCrews.AnswerCrossUnitAttackerModel(
                    hasCurrentEnemyUnit: false, sameEnemyUnit: false,
                    attackerVisible: true, canEngage: true) ||
                DemoCrews.AnswerCrossUnitAttackerModel(
                    hasCurrentEnemyUnit: true, sameEnemyUnit: true,
                    attackerVisible: true, canEngage: true) ||
                DemoCrews.AnswerCrossUnitAttackerModel(
                    hasCurrentEnemyUnit: true, sameEnemyUnit: false,
                    attackerVisible: false, canEngage: true) ||
                DemoCrews.AnswerCrossUnitAttackerModel(
                    hasCurrentEnemyUnit: true, sameEnemyUnit: false,
                    attackerVisible: true, canEngage: false))
                failures.Add("Return fire: a visible attacker from a second enemy crew is ignored or steals the whole crew's fight.");
        }

        static void CoverGuardsAreNotDraftedIntoAChase(List<string> failures)
        {
            if (!DemoCrews.ChaseCandidateModel(
                    ordinarilyEligible: true, guardingCover: false,
                    hasVisiblePersonalTarget: false) ||
                DemoCrews.ChaseCandidateModel(
                    ordinarilyEligible: true, guardingCover: true,
                    hasVisiblePersonalTarget: false) ||
                DemoCrews.ChaseCandidateModel(
                    ordinarilyEligible: true, guardingCover: false,
                    hasVisiblePersonalTarget: true) ||
                DemoCrews.ChaseCandidateModel(
                    ordinarilyEligible: false, guardingCover: false,
                    hasVisiblePersonalTarget: false))
                failures.Add("Lost sight: the chase selector pulls a guarding or actively fighting man away.");
        }

        static void EndedSearchKeepsAVisiblePersonalFight(List<string> failures)
        {
            if (DemoCrews.DropAtEndSearchModel(
                    dead: false, chasing: false, guardingCover: false,
                    hasPersonalTarget: true, personalTargetProtected: true) ||
                !DemoCrews.DropAtEndSearchModel(
                    dead: false, chasing: false, guardingCover: false,
                    hasPersonalTarget: true, personalTargetProtected: false) ||
                !DemoCrews.DropAtEndSearchModel(
                    dead: false, chasing: false, guardingCover: true,
                    hasPersonalTarget: false, personalTargetProtected: false))
                failures.Add("Search end: a visible personal fight is dropped or a finished cover guard survives.");
        }

        static void CrossUnitReturnFireDoesNotInheritAnOrder(List<string> failures)
        {
            if (!DemoCrews.OrderedAddressAppliesModel(
                    unitOrderedFight: true,
                    personalTargetBelongsToStrategicUnit: true) ||
                DemoCrews.OrderedAddressAppliesModel(
                    unitOrderedFight: true,
                    personalTargetBelongsToStrategicUnit: false) ||
                DemoCrews.OrderedAddressAppliesModel(
                    unitOrderedFight: false,
                    personalTargetBelongsToStrategicUnit: true))
                failures.Add("Return fire: a cross-unit personal target inherits the strategic order's omniscient address.");
        }

        static void PendingChaseRemovalIsNotAnActiveChaser(List<string> failures)
        {
            if (!DemoCrews.ActiveChaserAtSearchEndModel(
                    registeredChaser: true, queuedForRemoval: false) ||
                DemoCrews.ActiveChaserAtSearchEndModel(
                    registeredChaser: true, queuedForRemoval: true) ||
                DemoCrews.ActiveChaserAtSearchEndModel(
                    registeredChaser: false, queuedForRemoval: false))
                failures.Add("Chase cleanup: a reacquirer queued for removal is skipped by EndSearch promotion.");
        }

        static void RouteStallUsesRecentTravel(List<string> failures)
        {
            float elapsed = 0f, recent = 0f;
            if (CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.11f, 0.5f) ||
                CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0f, 0.5f) ||
                CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0f, 0.5f) ||
                !CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0f, 0.5f))
                failures.Add("CrewAudit: an early route nudge grants permanent stall immunity.");

            elapsed = 0f;
            recent = 0f;
            if (CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.04f, 0.5f) ||
                CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.04f, 0.5f) ||
                CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.04f, 0.5f) ||
                elapsed != 0f || recent != 0f)
                failures.Add("CrewAudit: real recent route movement does not renew stall grace.");

            elapsed = 0f;
            recent = 0f;
            if (CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.03f, 0.5f) ||
                CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.03f, 0.5f) ||
                !CrewAudit.AdvanceRouteStall(ref elapsed, ref recent, 0.03f, 0.5f))
                failures.Add("CrewAudit: sub-threshold shuffling hides a routed stall.");
        }

        static void RejectedRouteStartCanRecover(List<string> failures)
        {
            if (!WalkObstacles.RouteStartNeedsRecoveryModel(
                    clearanceBlocked: true, centreOverlapping: false,
                    hasValidator: false, validatorAccepts: true) ||
                !WalkObstacles.RouteStartNeedsRecoveryModel(
                    clearanceBlocked: false, centreOverlapping: false,
                    hasValidator: true, validatorAccepts: false) ||
                WalkObstacles.RouteStartNeedsRecoveryModel(
                    clearanceBlocked: false, centreOverlapping: false,
                    hasValidator: true, validatorAccepts: true) ||
                WalkObstacles.RouteStartNeedsRecoveryModel(
                    clearanceBlocked: true, centreOverlapping: true,
                    hasValidator: true, validatorAccepts: false))
                failures.Add("Combat route: a clear but lattice-isolated start cannot recover safely.");
        }

        static void TwoRoutedMoversDoNotCancelOneAnother(List<string> failures)
        {
            if (DemoCrews.SeparationPairNeedsEaseModel(true, true) ||
                !DemoCrews.SeparationPairNeedsEaseModel(true, false) ||
                !DemoCrews.SeparationPairNeedsEaseModel(false, true) ||
                !DemoCrews.SeparationPairNeedsEaseModel(false, false))
                failures.Add("Crew separation: two active routes can cancel each other's stride.");
        }

        static void RoutedCombatStrideIsASeparationMover(List<string> failures)
        {
            if (!DemoCrews.SeparationMoverModel(false, true) ||
                !DemoCrews.SeparationMoverModel(true, false) ||
                DemoCrews.SeparationMoverModel(false, false))
                failures.Add("Crew separation: routed combat movement was treated as a standing body.");
        }

        static void CombatCornerHandoffDoesNotOrbit(List<string> failures)
        {
            if (CrewWalker.CombatCornerCanAdvanceModel(0.001f, false) ||
                CrewWalker.CombatCornerCanAdvanceModel(0.02f, false) ||
                CrewWalker.CombatCornerCanAdvanceModel(0.2f, false) ||
                CrewWalker.CombatCornerCanAdvanceModel(0.8f, false) ||
                !CrewWalker.CombatCornerCanAdvanceModel(3f, true) ||
                !Mathf.Approximately(CrewWalker.CombatCornerStopModel(
                    last: false, endsAtTarget: false, terminalStop: 7f), 0f) ||
                !Mathf.Approximately(CrewWalker.CombatCornerStopModel(
                    last: true, endsAtTarget: true, terminalStop: 7f), 7f))
                failures.Add("Combat route: corner handoff ignored the proved next chord.");
        }

        static void MovingWithoutProgressReplans(List<string> failures)
        {
            float best = float.MaxValue, stalled = 0f;
            if (CrewWalker.CombatCornerStalledModel(5f, 0.5f, ref best, ref stalled) ||
                CrewWalker.CombatCornerStalledModel(4.7f, 0.5f, ref best, ref stalled))
                failures.Add("Combat route: real waypoint progress was read as a stall.");

            // Full movement with alternating 135-degree headings is still a stall when
            // none of it beats the closest point already reached.
            if (CrewWalker.CombatCornerStalledModel(5.2f, 0.5f, ref best, ref stalled) ||
                CrewWalker.CombatCornerStalledModel(4.9f, 0.5f, ref best, ref stalled) ||
                !CrewWalker.CombatCornerStalledModel(5.1f, 0.25f, ref best, ref stalled))
                failures.Add("Combat route: a moving orbit never forces a replan.");
        }

        /// <summary>The streamed residential collector sees prefab roots by these names,
        /// and uses the same classifier on a shared mesh when a composer renamed the root.
        /// Table dressing must not turn a mug or plate into a walking obstacle.</summary>
        static void ComposedCafeFurnitureIsPhysical(List<string> failures)
        {
            var furniture = new[]
            {
                "SM_Prop_Table_02",
                "SM_Prop_Chair_01",
            };
            for (int i = 0; i < furniture.Length; i++)
                if (!WalkObstacles.PhysicalPropName(furniture[i]))
                    failures.Add($"Walk props: streamed cafe furniture {furniture[i]} was not physical.");

            var dressing = new[]
            {
                "SM_Gen_Prop_Mug_01",
                "SM_Gen_Prop_Plate_01",
            };
            for (int i = 0; i < dressing.Length; i++)
                if (WalkObstacles.PhysicalPropName(dressing[i]))
                    failures.Add($"Walk props: table dressing {dressing[i]} became a physical obstacle.");
        }

        /// <summary>A CityKit restaurant shell is not an SM_Prop_. Its authored root
        /// collider must still enter a streamed view's walking plan. Harvested cafes use
        /// their structural proxy first, but their runtime `(cafe)` identity remains a
        /// conservative fallback. Neither rule broadens to courts and car yards.</summary>
        static void ComposedVenueShellsArePhysical(List<string> failures)
        {
            var venues = new[]
            {
                "building-diner (cafe)",
                "building-coffeeshop (cafe)",
                "building-burger-joint (cafe)",
                "building-cafe",
                "building-restaurant",
                "pizzapub (cafe)",
                "pizzapub2 (cafe)",
                "radnja1 (cafe)",
                "dinner (7,4) 90",
                "dinner2 (6,5) 180",
            };
            for (int i = 0; i < venues.Length; i++)
                if (!WalkObstacles.PhysicalVenueName(venues[i]))
                    failures.Add($"Walk props: streamed venue shell {venues[i]} was not physical.");

            var openLots = new[]
            {
                "caryard (4,5) 0", "kosarkaskiteren (3,4) 90", "park",
                "building-house", "building-office",
            };
            for (int i = 0; i < openLots.Length; i++)
                if (WalkObstacles.PhysicalVenueName(openLots[i]))
                    failures.Add($"Walk props: open amenity {openLots[i]} became a solid venue shell.");
        }

        static void AlignedStepsStayTogether(List<string> failures)
        {
            var steps = new[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0.08f, 0f, 1f),
                new Vector3(-0.05f, 0f, 1f),
            };
            float spread = CrewAudit.FormationHeadingSpread(steps, 0.025f);
            if (spread > 10f)
                failures.Add($"CrewAudit: aligned crew steps read as {spread:F1} degrees apart");
        }

        static void DivergentStepIsMeasured(List<string> failures)
        {
            var steps = new[] { Vector3.forward, Vector3.right, Vector3.forward };
            float spread = CrewAudit.FormationHeadingSpread(steps, 0.025f);
            if (Mathf.Abs(spread - 90f) > 0.01f)
                failures.Add($"CrewAudit: a right-angle split measured {spread:F1}, not 90 degrees");
        }

        static void StationaryJitterHasNoHeading(List<string> failures)
        {
            var steps = new[]
            {
                Vector3.forward,
                new Vector3(-0.001f, 0f, 0f),
            };
            float spread = CrewAudit.FormationHeadingSpread(steps, 0.025f);
            if (spread > 0.01f)
                failures.Add($"CrewAudit: stationary jitter invented a {spread:F1}-degree heading");
        }

        static void GroundSpreadIgnoresHeight(List<string> failures)
        {
            var positions = new[]
            {
                new Vector3(0f, -4f, 0f),
                new Vector3(6f, 9f, 8f),
                new Vector3(3f, 100f, 4f),
            };
            float spread = CrewAudit.FormationPositionSpread(positions);
            if (Mathf.Abs(spread - 10f) > 0.01f)
                failures.Add($"CrewAudit: 6-8-10 ground spread measured {spread:F2} m");
        }

        static void BreachNeedsItsWholeGrace(List<string> failures)
        {
            float held = 0f;
            if (CrewAudit.AdvanceSustained(ref held, true, 0.5f, 1.5f) ||
                CrewAudit.AdvanceSustained(ref held, true, 0.5f, 1.5f) ||
                CrewAudit.AdvanceSustained(ref held, true, 0.5f, 1.5f))
                failures.Add("CrewAudit: a breach faults before its full grace elapses");
            if (!CrewAudit.AdvanceSustained(ref held, true, 0.01f, 1.5f))
                failures.Add("CrewAudit: a sustained breach does not fault after its grace");
        }

        static void ClearingABreachResetsTheClock(List<string> failures)
        {
            float held = 1.4f;
            if (CrewAudit.AdvanceSustained(ref held, false, 0.1f, 1.5f) || held != 0f)
                failures.Add("CrewAudit: clearing a formation breach does not reset its clock");
            if (CrewAudit.AdvanceSustained(ref held, true, 0.2f, 1.5f))
                failures.Add("CrewAudit: a cleared breach resumed on its old clock");
        }
    }
}
