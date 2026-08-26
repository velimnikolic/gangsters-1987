using System.Collections.Generic;
using LivingCity.Gameplay;

namespace LivingCity.Tests
{
    /// <summary>
    /// The per-body performance table, held to the rules it has to keep for a street of
    /// mixed traffic to be an improvement on a street where everything drove the same.
    ///
    /// Engine-free, like the table it tests: load the built Assembly-CSharp.dll into a
    /// bare .NET host, call <see cref="Run"/>, read the returned list. Nothing here logs -
    /// UnityEngine.Debug.Log throws outside the Unity runtime, so a failure comes back as
    /// data.
    ///
    /// The assertions are about PROPERTIES, not about the numbers: retuning a supercar
    /// does not touch this file, and putting a supercar's stopping distance somewhere the
    /// belt cannot accept does.
    /// </summary>
    public static class VehiclePerformanceTests
    {
        /// <summary>Runs every check. An empty list means everything passed.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();

            EveryRowIsInBand(failures);
            NoBodyIsNamedTwice(failures);
            AnUnlistedBodyDrivesLikeItAlwaysDid(failures);
            APresetInheritsItsShell(failures);
            APresetWithARowOfItsOwnKeepsIt(failures);
            ASceneNameIsStillABody(failures);
            TheHeavyEndIsSlowerThanTheLightEnd(failures);
            NoBodyNeedsUnreasonableRoomToStop(failures);
            EveryDrivableBodyHasARow(failures);
            EveryCounterListingNamesItsOwnBody(failures);
            TheCounterSellsRealDifferences(failures);

            return failures;
        }

        // ------------------------------------------------------------------ the table

        static void EveryRowIsInBand(List<string> failures)
        {
            foreach (var entry in VehiclePerformance.Table)
                if (!VehiclePerformance.InBand(entry.Machine))
                    failures.Add($"EveryRowIsInBand: {entry.Name} is outside the band " +
                                 $"(top {entry.Machine.Top}, pull {entry.Machine.Pull}, grip {entry.Machine.Grip}).");
        }

        static void NoBodyIsNamedTwice(List<string> failures)
        {
            var seen = new HashSet<string>();
            foreach (var entry in VehiclePerformance.Table)
                if (!seen.Add(entry.Name))
                    failures.Add($"NoBodyIsNamedTwice: {entry.Name} has two rows; " +
                                 "the first one wins and the second is a lie.");
        }

        // ------------------------------------------------------------------ resolving

        static void AnUnlistedBodyDrivesLikeItAlwaysDid(List<string> failures)
        {
            // The whole reason the table is safe to add to a game that already drives:
            // a body nobody has written a row for must come out at the driver's own
            // numbers, which is exactly how every car drove before the table existed.
            Same("AnUnlistedBodyDrivesLikeItAlwaysDid", VehiclePerformance.For("SM_Veh_Nonesuch_99"),
                 VehiclePerformance.Ordinary, failures);
            Same("AnUnlistedBodyDrivesLikeItAlwaysDid: nothing at all", VehiclePerformance.For(null),
                 VehiclePerformance.Ordinary, failures);
            Same("AnUnlistedBodyDrivesLikeItAlwaysDid: an empty name", VehiclePerformance.For(""),
                 VehiclePerformance.Ordinary, failures);
        }

        static void APresetInheritsItsShell(List<string> failures)
        {
            // A taxi is a saloon with a roof sign, and the table says nothing about it -
            // so it must be answered by the saloon rather than fall through to Ordinary
            // by accident, which is the same numbers today and would stop being the same
            // numbers the moment anybody retunes a saloon.
            Same("APresetInheritsItsShell", VehiclePerformance.For("SM_Veh_Sedan_01_Preset_Taxi"),
                 VehiclePerformance.For("SM_Veh_Sedan_01"), failures);
            Same("APresetInheritsItsShell: the works pickup",
                 VehiclePerformance.For("SM_Veh_Pickup_01_Preset_Construction"),
                 VehiclePerformance.For("SM_Veh_Pickup_01"), failures);
        }

        static void APresetWithARowOfItsOwnKeepsIt(List<string> failures)
        {
            // The cruiser IS a saloon shell, and must NOT be answered by the saloon: it
            // has a row saying the force pays for a better engine. A resolver that took
            // the first stem it found would quietly give the law a civilian car.
            var cruiser = VehiclePerformance.For("SM_Veh_Sedan_01_Preset_Police");
            var saloon = VehiclePerformance.For("SM_Veh_Sedan_01");
            if (cruiser.Pull <= saloon.Pull)
                failures.Add("APresetWithARowOfItsOwnKeepsIt: the cruiser pulls no better than the " +
                             $"saloon it is built on ({cruiser.Pull} vs {saloon.Pull}) - its own row was not read.");
        }

        static void ASceneNameIsStillABody(List<string> failures)
        {
            // Every car in the game finds its machine off the name of its own transform,
            // and a transform in a scene is not called what the prefab is called: an
            // instance wears "(Clone)" and a bike's model wears the frame suffix. Both
            // used to answer Ordinary, which is a whole street of identical traffic.
            Same("ASceneNameIsStillABody: an instance", VehiclePerformance.For("SM_Veh_Truck_01(Clone)"),
                 VehiclePerformance.For("SM_Veh_Truck_01"), failures);
            Same("ASceneNameIsStillABody: a bike's frame", VehiclePerformance.For("SM_Veh_Moped_01 (frame)"),
                 VehiclePerformance.For("SM_Veh_Moped_01"), failures);
            Same("ASceneNameIsStillABody: a path", VehiclePerformance.For(
                     "Assets/Synty/PolygonCity/Prefabs/Vehicles/SM_Veh_Supercar_01.prefab"),
                 VehiclePerformance.For("SM_Veh_Supercar_01"), failures);
        }

        // ------------------------------------------------------------------ the order

        static void TheHeavyEndIsSlowerThanTheLightEnd(List<string> failures)
        {
            // The point of the whole table, stated as an order rather than as numbers.
            // Pull first, because pull is the column the street shows: nothing caps it,
            // whereas a lane limit flattens the top end of everything ordinary.
            Slower("a lorry", "a saloon", "SM_Veh_Truck_01", "SM_Veh_Sedan_01", failures);
            Slower("a saloon", "a muscle car", "SM_Veh_Sedan_01", "SM_Veh_Car_Muscle_01", failures);
            Slower("a muscle car", "a supercar", "SM_Veh_Car_Muscle_01", "SM_Veh_Supercar_01", failures);
            Slower("a moped", "a motorcycle", "SM_Veh_Moped_01", "SM_Veh_Motorbike_01", failures);
            Slower("a bus", "a van", "SM_Veh_Bus_01", "SM_Veh_Van_01", failures);

            // and the top end, where the road allows one
            var moped = VehiclePerformance.For("SM_Veh_Moped_01");
            var saloon = VehiclePerformance.For("SM_Veh_Sedan_01");
            if (moped.Top >= saloon.Top)
                failures.Add($"TheHeavyEndIsSlowerThanTheLightEnd: a moped's top end ({moped.Top}) " +
                             $"is not under a saloon's ({saloon.Top}).");
        }

        static void Slower(string slowWhat, string quickWhat, string slow, string quick, List<string> failures)
        {
            float a = VehiclePerformance.For(slow).Pull, b = VehiclePerformance.For(quick).Pull;
            if (a >= b)
                failures.Add($"TheHeavyEndIsSlowerThanTheLightEnd: {slowWhat} pulls away no worse than " +
                             $"{quickWhat} ({a} vs {b}).");
        }

        // ------------------------------------------------------------------ the belt

        /// <summary>The one safety property, and the reason the top end of the table is
        /// held tighter than the pull. Stopping room goes as v² over the brake, so a body
        /// scaled Top on the pace and Grip on the brake needs Top²/Grip of the room the
        /// driver's own numbers were tuned for. Every profile in DriverProfile was tuned
        /// against a saloon, and the getaway pair were tuned against the belt refusing
        /// steps - which is a machine that would have driven through another one.
        ///
        /// Half again is the line. It is not a physical law, it is how much slack those
        /// profiles were left with, and a row that wants more than it has to say so out
        /// loud rather than turn up as a belt hit in a soak.</summary>
        const float MostRoomEver = 1.5f;

        static void NoBodyNeedsUnreasonableRoomToStop(List<string> failures)
        {
            foreach (var entry in VehiclePerformance.Table)
            {
                float room = entry.Machine.Top * entry.Machine.Top / entry.Machine.Grip;
                if (room > MostRoomEver)
                    failures.Add($"NoBodyNeedsUnreasonableRoomToStop: {entry.Name} needs {room:F2}x the " +
                                 $"stopping room the driver's numbers were tuned for (the line is {MostRoomEver}).");
            }
        }

        // ------------------------------------------------------------------ the catalog

        static void EveryDrivableBodyHasARow(List<string> failures)
        {
            // VehicleCatalog names what the game is allowed to put in somebody's hands.
            // A body on one of those lists with no row of its own is not a bug today -
            // it drives like a saloon - but it is a body nobody decided about, which is
            // exactly what this table exists to stop.
            Listed("the outfit's cars", VehicleCatalog.GangsterCars, failures);
            Listed("the two-wheelers", VehicleCatalog.Motorcycles, failures);
            Listed("the law's cars", VehicleCatalog.PoliceCars, failures);
            Listed("the law's two-wheelers", VehicleCatalog.PoliceMotorcycles, failures);
        }

        static void Listed(string what, string[] bodies, List<string> failures)
        {
            foreach (var body in bodies)
                if (!VehiclePerformance.Lists(body))
                    failures.Add($"EveryDrivableBodyHasARow: {body} is on {what} and has no row in the table.");
        }

        // ------------------------------------------------------------------ the counter

        static void EveryCounterListingNamesItsOwnBody(List<string> failures)
        {
            // ArmoryCatalog.BodyFor ends in a default, and a default is a silent failure:
            // a listing somebody adds to the counter without a line in that table is sold,
            // priced, photographed and driven as a saloon, and nothing anywhere says so.
            Named(LivingCity.Outfit.ArmoryCatalog.Vehicles, failures);
            Named(LivingCity.Outfit.ArmoryCatalog.Motorcycles, failures);
        }

        static void Named(LivingCity.Outfit.ArmoryItem[] listings, List<string> failures)
        {
            foreach (var listing in listings)
            {
                if (listing.DisplayName == "Sedan")
                    continue;   // the one listing the default is the right answer for
                if (LivingCity.Outfit.ArmoryCatalog.BodyFor(listing.DisplayName) == "SM_Veh_Sedan_01")
                    failures.Add($"EveryCounterListingNamesItsOwnBody: \"{listing.DisplayName}\" is on " +
                                 "the counter and falls through ArmoryCatalog.BodyFor to a saloon.");
            }
        }

        static void TheCounterSellsRealDifferences(List<string> failures)
        {
            // What the player is buying, stated as an order. The counter's prices are a
            // matter of taste unless the machines behind them actually differ, and the
            // campaign's own travel arithmetic reads exactly these numbers
            // (OrderMath.TravelHours through CrewKit.MachineTopOf).
            float Top(string listing) =>
                VehiclePerformance.For(LivingCity.Outfit.ArmoryCatalog.BodyFor(listing)).Top;

            if (!(Top("Panel Van") < Top("Sedan")))
                failures.Add($"TheCounterSellsRealDifferences: the panel van ({Top("Panel Van")}) is " +
                             $"no slower than the working car ({Top("Sedan")}).");
            if (!(Top("Armoured Wagon") < Top("Sedan")))
                failures.Add($"TheCounterSellsRealDifferences: six thousand dollars of armour " +
                             $"({Top("Armoured Wagon")}) costs the outfit nothing in pace.");
            if (!(Top("Moped") < Top("Motorbike")))
                failures.Add($"TheCounterSellsRealDifferences: the moped ({Top("Moped")}) keeps up " +
                             $"with the motorcycle ({Top("Motorbike")}).");
            // the tourer is the machine for a job across town and must not be the slowest
            // thing on the shelf it is the dearest thing on
            if (!(Top("Tourer") > Top("Moped")))
                failures.Add($"TheCounterSellsRealDifferences: the tourer ({Top("Tourer")}) is worth " +
                             $"no more on the road than the moped ({Top("Moped")}).");
        }

        // ------------------------------------------------------------------ helper

        static void Same(string test, VehiclePerformance.Machine got, VehiclePerformance.Machine want,
                         List<string> failures)
        {
            if (got.Top != want.Top || got.Pull != want.Pull || got.Grip != want.Grip)
                failures.Add($"{test}: got ({got.Top}, {got.Pull}, {got.Grip}), " +
                             $"wanted ({want.Top}, {want.Pull}, {want.Grip}).");
        }
    }
}
