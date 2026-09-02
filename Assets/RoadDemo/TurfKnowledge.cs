using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What the outfit has FOUND OUT about the other houses - which of their premises it
    /// has actually stood in front of. Our own places are known from the first day; a
    /// rival's door is a rumour until one of our crews has been down that street.
    ///
    /// This is knowledge, not truth. The deed book (LivingCity.Business.BusinessDeeds)
    /// says who owns what and never lies; this says what WE can see, and every surface
    /// that names a rival's place asks here first. Nothing is ever forgotten - a family
    /// can lose a premises but the player does not unlearn the address.
    ///
    /// Keyed by the premises rather than by the family, because a house holds more than
    /// one door as the campaign runs on and finding one of them must not hand over the
    /// rest. The key is the business id where the simulation names one, and the family's
    /// front otherwise (the demo scenes that stand fronts with no business directory
    /// behind them).
    ///
    /// Static and reset at SubsystemRegistration - the GangFront discipline: with domain
    /// reload off, last session's discoveries would be waiting for the next one.
    /// </summary>
    public static class TurfKnowledge
    {
        /// <summary>Metres. How close one of our men has to pass a door before the
        /// street tells him whose it is - the width of a street and its two pavements,
        /// so walking past on the far side counts and driving through the next block
        /// does not.</summary>
        public const float LearnRange = 28f;

        /// <summary>
        /// WHAT EACH FAMILY KNOWS. Keyed by house, because knowing a street is a thing
        /// a family DOES - it walks it, or it sends somebody to look - and twenty
        /// families each know a different city.
        ///
        /// The player is house 0 and reads exactly what he always did.
        /// </summary>
        static readonly Dictionary<int, HashSet<string>> known =
            new Dictionary<int, HashSet<string>>();

        static HashSet<string> Book(int gangId)
        {
            if (!known.TryGetValue(gangId, out var book))
            {
                book = new HashSet<string>();
                known.Add(gangId, book);
            }
            return book;
        }

        /// <summary>
        /// WHOSE FACES WE KNOW. A man of another family whose name our men have read on
        /// the street - close enough, the same reach a door is learnt at. The player may
        /// only name a target he has actually seen; a family he has never stood near has
        /// no names in it, only numbers.
        /// </summary>
        static readonly HashSet<int> knownMen = new HashSet<int>();

        public static bool IsKnownMan(int characterId) => knownMen.Contains(characterId);

        /// <summary>Learn a face. True only the first time.</summary>
        public static bool LearnMan(int characterId)
        {
            if (characterId < 0 || !knownMen.Add(characterId))
                return false;
            Version++;
            return true;
        }

        public static int MenKnown => knownMen.Count;

        /// <summary>Bumped whenever something new is learnt, so a view can repaint on a
        /// number rather than sweep its own marks every frame.</summary>
        public static int Version { get; private set; }

        /// <summary>The key for one premises - the id the simulation knows it by, or the
        /// family's own front when there is no directory in this scene.</summary>
        public static string KeyOf(GangFront front)
        {
            if (front == null)
                return "";
            var business = front.BusinessId;
            return business.IsValid ? business.Value : "front:" + front.GangId;
        }

        /// <summary>Ours needs no discovering: a man knows where he works.</summary>
        public static bool IsKnown(GangFront front) =>
            IsKnown(front, LivingCity.Gangs.GangCatalog.PlayerGangId);

        public static bool IsKnown(GangFront front, int gangId) =>
            front != null &&
            (front.GangId == gangId || Book(gangId).Contains(KeyOf(front)));

        /// <summary>Learn a place. True only the first time, so a caller can log or
        /// announce a discovery without keeping its own set.</summary>
        public static bool Learn(GangFront front) =>
            Learn(front, LivingCity.Gangs.GangCatalog.PlayerGangId);

        public static bool Learn(GangFront front, int gangId)
        {
            var key = KeyOf(front);
            if (key.Length == 0 || !Book(gangId).Add(key))
                return false;

            if (gangId == LivingCity.Gangs.GangCatalog.PlayerGangId)
                Version++;
            return true;
        }

        /// <summary>A door learnt by its own id - what an EXPLORE order brings back
        /// about a street nobody of ours has walked.</summary>
        public static bool LearnDoor(string businessId, int gangId)
        {
            if (string.IsNullOrEmpty(businessId) || !Book(gangId).Add(businessId))
                return false;
            if (gangId == LivingCity.Gangs.GangCatalog.PlayerGangId)
                Version++;
            return true;
        }

        public static bool IsKnownDoor(string businessId, int gangId) =>
            !string.IsNullOrEmpty(businessId) && Book(gangId).Contains(businessId);

        public static int Count =>
            known.TryGetValue(LivingCity.Gangs.GangCatalog.PlayerGangId, out var book)
                ? book.Count
                : 0;

        public static int CountOf(int gangId) => Book(gangId).Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            known.Clear();
            knownMen.Clear();
            Version = 0;
        }
    }
}
