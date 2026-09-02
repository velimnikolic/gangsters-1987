using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Writes EVERY house's premises into the deed book, so the building each family
    /// actually holds reads as held everywhere the city asks: the block file's tenure
    /// column, the holdings sweep, the turf map's wash - and, since RIVAL-001, so that
    /// each house's own runner can settle its front's net into its own safe at midnight.
    ///
    /// The deed is DATA. What the player is allowed to SEE of it is a separate question
    /// and is answered somewhere else: a rival's premises stays a rumour until a crew of
    /// ours has stood outside it, and the gate for that lives in
    /// <see cref="LivingCity.Gameplay.DoorHolder"/> against
    /// <see cref="TurfKnowledge"/>. This class used to enforce the secret by not writing
    /// the paper at all, which also meant twenty families held nothing anywhere in the
    /// simulation.
    ///
    /// It waits, because it has to: the fronts are seated before the business directory
    /// is always dealt, and a deed can only be written against a business the directory
    /// already names. It stops when every seated front has its paper, or when the city
    /// plainly has no directory to ask (the demo scenes), so a scene without one does
    /// not poll forever.
    /// </summary>
    public sealed class FrontDeeds : MonoBehaviour
    {
        /// <summary>Seconds to wait for a business directory before giving up quietly.
        /// A scene with no simulation behind it is a demo harness, not a fault.</summary>
        const float Patience = 20f;

        float _gaveUpAt;
        int _written;

        void Start() => _gaveUpAt = Time.time + Patience;

        void Update()
        {
            var fronts = GangFront.All;
            if (fronts.Count == 0)
            {
                if (Time.time > _gaveUpAt) enabled = false;
                return;
            }

            var pending = 0;
            for (var i = 0; i < fronts.Count; i++)
            {
                var front = fronts[i];
                if (front == null)
                    continue;

                var business = front.BusinessId;
                if (!business.IsValid)
                {
                    pending++;
                    continue;
                }

                if (LivingCity.Business.BusinessDeeds.GangOf(business) == front.GangId)
                    continue;

                LivingCity.Business.BusinessDeeds.SetGang(
                    business, front.GangId, front.BlockId);
                _written++;
                if (front.GangId == LivingCity.Gameplay.PlayerCommands.House.Value)
                    Debug.Log($"[Turf] The outfit holds the paper on {business.Value} - " +
                              (front.Books != null ? front.Books.Sign : front.name) + ".");
            }

            if (pending == 0)
            {
                Debug.Log($"[Turf] {_written} front(s) on the deed book.");
                enabled = false;
                return;
            }

            if (Time.time <= _gaveUpAt)
                return;

            // Out of patience with the doors the directory never named. The outfit's own
            // is the one worth a warning: the ledger prints the player's headquarters,
            // and a headquarters the block file reads as open ground is a fault he can
            // see.
            var ours = DemoCrews.PlayerFront();
            if (ours != null && !ours.BusinessId.IsValid && ours.SiteId.IsValid)
                Debug.LogWarning(
                    "[Turf] The outfit's own door is not a premises the business " +
                    "directory names - the block file will print it as open ground.",
                    this);
            enabled = false;
        }
    }
}
