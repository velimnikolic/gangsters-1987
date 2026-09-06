using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LivingCity.UI
{
    /// <summary>
    /// The pointer's half of a register line, a day tick or a pen swatch.
    ///
    /// One component rather than a Button and a hover zone: the wire's rows are POOLED,
    /// so what a view answers to changes every time the scroll brings a different slip
    /// into it, and an index rebound at lay time is the whole of that. Added at build
    /// time and never serialized.
    ///
    /// It reads the double click itself because the day rail has two verbs on one tick -
    /// a click jumps to that day, a double click isolates it - and Unity's own Button
    /// knows only the first.
    /// </summary>
    public sealed class WireHit : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerClickHandler
    {
        public Action<int> enter, exit, click, doubleClick;

        /// <summary>What this view is showing right now - a line's place in the run, or
        /// a campaign day.</summary>
        public int index;

        public void OnPointerEnter(PointerEventData eventData) => enter?.Invoke(index);

        public void OnPointerExit(PointerEventData eventData) => exit?.Invoke(index);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount >= 2 && doubleClick != null)
            {
                doubleClick(index);
                return;
            }
            click?.Invoke(index);
        }
    }
}
