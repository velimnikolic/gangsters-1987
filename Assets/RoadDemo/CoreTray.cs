using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The buttons on a block tray.
    ///
    /// It is a marker and nothing else - no state, no behaviour, nothing at runtime. The
    /// whole of it is the inspector Unity draws for anything carrying it
    /// (CoreTrayInspector), which is how a tray comes to have a Pave button on it instead
    /// of a menu to be hunted through. The tray itself is still what it always was: a
    /// rectangle called "pad" with whatever is standing on it.
    ///
    /// It has to be a runtime script because a MonoBehaviour cannot be anything else. It
    /// never reaches a build: the scenes that hold trays are authoring scenes.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("City/Core Block Tray")]
    public class CoreTray : MonoBehaviour
    {
        /// <summary>
        /// How wide the pavement runs round the buildings, in 5 m tiles.
        ///
        /// The artists' own figure is ONE - 275 of their 350 kerb tiles have the facade
        /// exactly 5 m behind (see <see cref="CorePavement"/>). Two is what this project
        /// asked for (2026-08-25, "slobodno moze i siri malo"), and it is the default: a
        /// little more room on the pavement than the pack takes, and a squarer block.
        /// </summary>
        [Range(1, 4)]
        public int pavementTiles = CoreBlockMetrics.PavementTiles;

        /// <summary>
        /// Does what stands on this tray bring its OWN ground?
        ///
        /// Some blocks are a building on bare earth and want a floor laid under them - the
        /// police station is one, and without it the block is baked with a hole where its
        /// footprint was. Others arrive complete: the gang-warfare warehouse is a walled
        /// yard with its own asphalt, its own markings and its own gate, and laying city
        /// slabs over that replaces work somebody drew with a grid of squares. On, the
        /// pavement lays its kerb and its band and NOTHING inside the footprint.
        ///
        /// Declared rather than guessed: an artist's yard plate and a building's own
        /// concrete floor are the same thing to a mesh, and the difference is what the
        /// block is FOR.
        /// </summary>
        [Tooltip("The buildings arrive with their own yard - pave the band, not the middle.")]
        public bool ownGround;
    }
}
