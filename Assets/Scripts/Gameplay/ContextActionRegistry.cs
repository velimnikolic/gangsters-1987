using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Every action the context menu can offer, sorted once at registration. The built-ins
    /// register themselves before the first scene loads; gameplay code can Register more at
    /// any time (a mission granting a temporary option is one Register call).
    /// </summary>
    public static class ContextActionRegistry
    {
        static readonly List<IContextAction> Registered = new List<IContextAction>();

        public static IReadOnlyList<IContextAction> Actions => Registered;

        public static void Register(IContextAction action)
        {
            if (action == null || Registered.Contains(action))
                return;

            Registered.Add(action);
            Registered.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        }

        /// <summary>
        /// Fills a caller-owned buffer with the actions available for this pair, in menu
        /// order. Buffer-filling rather than returning a fresh list so opening the menu
        /// allocates nothing beyond its buttons.
        /// </summary>
        public static void GetAvailable(
            PlayerMafioso actor, IContextTarget target, List<IContextAction> buffer)
        {
            buffer.Clear();
            foreach (var action in Registered)
                if (action.IsAvailable(actor, target))
                    buffer.Add(action);
        }

        /// <summary>
        /// Static state outlives Play when domain reload is off; re-seeding here keeps one
        /// session's registrations out of the next. Same fix as OverlayRegistry.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Registered.Clear();
            Register(new KillAction());
            Register(new CancelAction());
        }
    }
}
