using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>
    /// ONE MODAL GATE (EPIC 40, STREET-000). The screens that own the picture used to
    /// be eight hard-coded name checks across seven files, each listing a different
    /// two or three of them - so a fourth modal (THE PHONE) would have had to be added
    /// eight times, and the arrest clock ran behind any it was forgotten on. Every
    /// site reads one of the four questions here instead.
    /// </summary>
    public static class ModalGate
    {
        public enum Paper
        {
            Ledger,
            Newspaper,
            Phone,
        }

        /// <summary>A screen that owns the whole picture and stops the clock: the
        /// ledger, the morning edition, the phone.</summary>
        public static bool PaperUp =>
            PersonnelAlmanac.IsOpen || NewspaperHud.IsOpen || EventCardHud.IsOpen;

        /// <summary>A paper screen other than the one asking - what a modal checks
        /// before it opens over another.</summary>
        public static bool OtherPaperUp(Paper which) =>
            (which != Paper.Ledger && PersonnelAlmanac.IsOpen) ||
            (which != Paper.Newspaper && NewspaperHud.IsOpen) ||
            (which != Paper.Phone && EventCardHud.IsOpen);

        /// <summary>Paper screens hide the street. The TurfMap remains part of the city.</summary>
        public static bool ScreenTaken => PaperUp;

        /// <summary>Anything at all over the street, the turf plate included.</summary>
        public static bool Any => ScreenTaken || RoadDemo.TurfMapHud.IsOpen;

        /// <summary>The arrest clock stops behind these: the player can neither see
        /// the banner nor give the order that would overrule the roll.</summary>
        public static bool Blocked => PaperUp;

        /// <summary>Esc belongs to one of the paper screens this frame - including the
        /// frame it closed on, because polling input cannot be consumed.</summary>
        public static bool ClaimsEsc =>
            PersonnelAlmanac.ClaimsEsc || NewspaperHud.ClaimsEsc || EventCardHud.ClaimsEsc;
    }
}
