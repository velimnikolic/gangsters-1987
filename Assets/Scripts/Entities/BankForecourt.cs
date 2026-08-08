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
    /// </summary>
    public sealed class BankForecourt : StallHost
    {
        public const string PrefabName = "building-bank";
    }
}
