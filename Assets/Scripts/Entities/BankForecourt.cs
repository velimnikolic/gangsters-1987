using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Data marker on the placed bank: the forecourt stalls its customers park in. Attached by
    /// BlockBuilder alongside the static bakes rather than instead of them - unlike the police
    /// station, whose whole forecourt is reserved for the fleet, the bank keeps a few parked
    /// cars standing permanently and lets the LEFTOVER bays live. A forecourt that empties
    /// completely whenever no visitor happens to be there reads as a closed bank.
    ///
    /// Which is why the recording order differs from the station's: MarkPoliceStation runs
    /// BEFORE FillStalls because nothing is ever baked there, while this marker is filled in
    /// AFTER, from the stalls FillStalls reports it did not use. Recording all of them would
    /// let a visitor drive into a bay a static car is already standing in.
    ///
    /// It is also the bank's face on the overlay: a square marker over the roof, and a popup
    /// that reports how many customers are here and whether an arriving driver would find room.
    /// The police station has no such marker on purpose - its units carry their own, and a
    /// second marker over the building they are standing outside of would say nothing new.
    /// </summary>
    public sealed class BankForecourt : StallHost, UI.IOverlaySubject
    {
        public const string PrefabName = "building-bank";

        /// <summary>The director, handed over once it has found this marker. Null on a seed
        /// where the visitors stood down, which the popup reports rather than hides.</summary>
        ForecourtVisitorDirector director;

        float markerHeight = -1f;

        public void BindDirector(ForecourtVisitorDirector owner) => director = owner;

        void OnEnable() => UI.OverlayRegistry.Register(this);

        void OnDisable() => UI.OverlayRegistry.Unregister(this);

        Transform UI.IOverlaySubject.OverlayAnchor => transform;

        float UI.IOverlaySubject.OverlayHeight
        {
            get
            {
                // Once, on first read - the renderer sweep allocates and the bank is not going
                // to change height. See SchoolMarker for why not in OnEnable.
                if (markerHeight < 0f)
                    markerHeight = UI.BuildingMarker.HeightFor(transform);

                return markerHeight;
            }
        }

        bool UI.IOverlaySubject.OverlayHidden => false;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Square;
        Color UI.IOverlaySubject.OverlayColor => UI.IntentionPalette.Place;
        string UI.IOverlaySubject.OverlayTitle => UI.ForecourtIntention.BankTitle();

        string UI.IOverlaySubject.OverlayLine =>
            UI.ForecourtIntention.BankLine(
                director ? director.VisitorCount : 0, FreeStallCount, StallCount);

        long UI.IOverlaySubject.OverlayKey =>
            ((long)(director ? director.VisitorCount : 0) << 32) | (uint)FreeStallCount;
    }
}
