using System.Collections.Generic;

namespace LivingCity.Outfit
{
    /// <summary>
    /// The three-step scale. What each stance DOES (once execution lands - the state
    /// and its wording ship now so the player is never surprised later):
    ///
    ///  Peace - no engagement. Your men and theirs pass in the street, claimed ground
    ///          or not.
    ///  Truce - territorial. Their men engage yours caught inside THEIR territory, and
    ///          yours engage theirs on YOURS. Neutral ground stays quiet.
    ///  War   - on sight. Their men engage yours anywhere in the city, and yours theirs.
    /// </summary>
    public enum Stance
    {
        Peace,
        Truce,
        War,
    }

    /// <summary>
    /// The player's standing toward each rival gang. A stance change never lands
    /// mid-week: it is stored as PENDING and applied when the week commits - orders in
    /// flight were priced under the old rules, and a war declared on Tuesday would
    /// reprice a Monday plan behind the player's back.
    /// </summary>
    public sealed class GangRelations
    {
        readonly Dictionary<int, Stance> current = new Dictionary<int, Stance>();
        readonly Dictionary<int, Stance> pending = new Dictionary<int, Stance>();

        /// <summary>Everyone starts at Peace - the outfit arrives quietly.</summary>
        public Stance StanceWith(int gangId) =>
            current.TryGetValue(gangId, out var stance) ? stance : Stance.Peace;

        public bool TryGetPending(int gangId, out Stance stance) =>
            pending.TryGetValue(gangId, out stance);

        /// <summary>Setting the pending stance back to the current one withdraws the
        /// change - "never mind" must be expressible before the week seals it.</summary>
        public void SetPending(int gangId, Stance stance)
        {
            if (stance == StanceWith(gangId))
                pending.Remove(gangId);
            else
                pending[gangId] = stance;
        }

        /// <summary>The commit boundary calls this - next week begins under new rules.</summary>
        public void ApplyPending()
        {
            foreach (var entry in pending)
                current[entry.Key] = entry.Value;
            pending.Clear();
        }
    }
}
