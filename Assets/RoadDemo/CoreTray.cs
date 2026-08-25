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
    }
}
