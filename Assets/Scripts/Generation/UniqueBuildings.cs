using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// The prefabs a city gets exactly one of, and which of them have been used.
    ///
    /// Every other "only one" rule in the generator is a rule about BLOCKS: ZonePlanner spends a
    /// per-zone quota, and a block places its landmark once. That covers the hospital, the police
    /// station and the school, because each of those is the landmark of a zone of its own. It does
    /// not cover the post office or the fire station. Both are prefabs in the residential Shops
    /// group - a storefront between the flats - so they are chosen per SLOT, and the only thing
    /// that stops a prefab repeating is a ShuffleBag whose scope is one lot. A bag that is rebuilt
    /// for every lot in the city cannot know the city already has a post office.
    ///
    /// The fire station arrived here by demotion: it used to be a zone of its own, and a whole
    /// block for one fire house was the city's scarcest thing spent on its least. Nothing about
    /// this mechanism changed to take it - it is one more entry in the same list.
    ///
    /// So this is the one piece of state in the build that is neither per-block nor per-lot but
    /// per-CITY. It is static for the same reason BlockBuilder.placeRejections is: the test sits
    /// four levels down (Build -> BuildBlock -> BuildLot -> WalkSide) under signatures that
    /// already carry fifteen parameters, generation is single-threaded, and threading a set
    /// through all of them buys nothing. What that costs is a lifetime longer than one build, so
    /// Reset is not optional - call it at the top of every build, or the second city in an editor
    /// session comes out with no post office at all.
    ///
    /// Nothing is capped unless the database says so, and IsSpent short-circuits on the capped
    /// set, so the check is a single hash lookup on the hot path and nothing at all in a project
    /// that leaves uniqueBuildings empty.
    /// </summary>
    public static class UniqueBuildings
    {
        static readonly HashSet<GameObject> capped = new();
        static readonly HashSet<GameObject> spent = new();

        /// <summary>Rebuilds the capped set from the database and forgets what has been built.</summary>
        public static void Reset(PrefabDatabase prefabs)
        {
            capped.Clear();
            spent.Clear();

            if (prefabs == null || prefabs.uniqueBuildings == null)
                return;

            foreach (var prefab in prefabs.uniqueBuildings)
                if (prefab)
                    capped.Add(prefab);
        }

        /// <summary>True when this prefab is capped AND the city has already taken its one.</summary>
        public static bool IsSpent(GameObject prefab) =>
            capped.Count > 0 && prefab && spent.Contains(prefab);

        /// <summary>
        /// Records that the city now has this one. A no-op for anything uncapped, so callers can
        /// hand over whatever they just placed without asking first.
        /// </summary>
        public static void Spend(GameObject prefab)
        {
            if (prefab && capped.Contains(prefab))
                spent.Add(prefab);
        }
    }
}
