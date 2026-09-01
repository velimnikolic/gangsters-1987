using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Writes the outfit's own premises into the deed book, so the one building the
    /// player actually holds on day one reads as held everywhere the city asks: the block
    /// file's tenure column, the holdings sweep, the turf map's wash. Before this the
    /// planned city seated its fronts as doors and nothing else - the deed book had no
    /// player-owned premises in it at all, and the ledger printed the outfit's own
    /// headquarters as an open shop nobody leans on.
    ///
    /// Only OURS. A rival's door is theirs in fact, but a deed is public knowledge the
    /// moment it is written - the map washes their block, the block file names them - and
    /// their premises is meant to stay a rumour until a crew of ours has stood outside it
    /// (<see cref="TurfKnowledge"/>). Their paper is written when the takeover layer has a
    /// rule for who is allowed to know it.
    ///
    /// It waits, because it has to: the fronts are seated before the business directory
    /// is always dealt, and a deed can only be written against a business the directory
    /// already names. It stops the moment it succeeds, or when the city plainly has no
    /// directory to ask (the demo scenes), so a scene without one does not poll forever.
    /// </summary>
    public sealed class FrontDeeds : MonoBehaviour
    {
        /// <summary>Seconds to wait for a business directory before giving up quietly.
        /// A scene with no simulation behind it is a demo harness, not a fault.</summary>
        const float Patience = 20f;

        float _gaveUpAt;

        void Start() => _gaveUpAt = Time.time + Patience;

        void Update()
        {
            var front = DemoCrews.PlayerFront();
            if (front == null)
            {
                if (Time.time > _gaveUpAt) enabled = false;
                return;
            }

            var business = front.BusinessId;
            if (!business.IsValid)
            {
                if (Time.time > _gaveUpAt)
                {
                    if (front.SiteId.IsValid)
                        Debug.LogWarning(
                            "[Turf] The outfit's own door is not a premises the business " +
                            "directory names - the block file will print it as open ground.",
                            this);
                    enabled = false;
                }
                return;
            }

            LivingCity.Business.BusinessDeeds.SetGang(
                business, LivingCity.Gangs.GangCatalog.PlayerGangId, front.BlockId);
            Debug.Log($"[Turf] The outfit holds the paper on {business.Value} - " +
                      (front.Books != null ? front.Books.Sign : front.name) + ".");
            enabled = false;
        }
    }
}
