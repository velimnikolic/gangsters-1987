using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The stamp DemoScratch puts on everything a demo builds for itself.
    ///
    /// It carries no behaviour at all. Its whole job is to be findable: a scratch object
    /// is flagged DontSaveInEditor, which also means Unity will NOT destroy it when the
    /// scene closes, and once its scene is gone the object is not in any hierarchy any
    /// more - there is nothing left to search by except a type.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DemoScratchMark : MonoBehaviour
    {
    }
}
