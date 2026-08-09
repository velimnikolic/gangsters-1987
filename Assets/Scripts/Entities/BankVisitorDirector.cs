using System.Collections;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Keeps a trickle of customers arriving at the bank. The bank's forecourt used to be a row
    /// of static cars and nothing else - and, because landmarkCars is a property of the PALETTE
    /// rather than of the landmark, the cars it got were the police station's. Civilian bakes
    /// fixed what stood there; this fixed that nothing ever happened there.
    ///
    /// The trip itself is ForecourtVisitorDirector's, shared with the school's parents; all that
    /// is left here is which building, how many, how long and what to call them. The class name
    /// is load-bearing and must not change: this component is baked into grad.unity, and it
    /// carries the serialized config and prefabs references the editor wired.
    ///
    /// Home is the BankForecourt marker BlockBuilder attached at generation. A scene generated
    /// before that marker existed - or a seed whose bank drew its own block, where the parking
    /// is the block's perimeter rather than a forecourt - has none, and the system stands down
    /// with one warning rather than guessing.
    /// </summary>
    public sealed class BankVisitorDirector : ForecourtVisitorDirector
    {
        protected override string LogTag => "[BankVisitors]";
        protected override int SeedOffset => SeedOffsets.BankVisitors;
        protected override int Population => config.bankVisitorCount;
        protected override Vector2 GapRange => config.bankVisitorGapRange;
        protected override Vector2 StayRange => config.bankVisitStayRange;
        protected override UI.ForecourtErrand Errand => UI.ForecourtErrand.Bank;

        protected override StallHost FindForecourt() => FindFirstObjectByType<BankForecourt>();

        /// <summary>The bank's own position: its forecourt spans the whole facade and its door
        /// is not recorded anywhere, unlike the school's.</summary>
        protected override Vector3 KerbFocusFrom(StallHost host) => host.transform.position;

        protected override void BindToForecourt(StallHost host)
        {
            if (host is BankForecourt bank)
                bank.BindDirector(this);
        }

        protected override string MissingForecourtMessage =>
            "No BankForecourt marker in the scene - this seed's bank has no forecourt, or the " +
            "city predates the marker. Visitors stand down; regenerate the city " +
            "(Tools/City/Set Up Scene) to fix.";

        IEnumerator Start() => Run();
    }
}
