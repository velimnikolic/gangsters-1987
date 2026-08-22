using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Outfit;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The outfit the monkey needs, written into the ledger before the run starts.
    ///
    /// The seeded roster is one lieutenant and a couple of hoods, unarmed but for the
    /// .38 in every coat, and it owns no wheels - so a monkey told to run a drive-by
    /// has nothing to run it with. This promotes a few crews, deals hoods behind them,
    /// buys a gun a man off the armory counter, and buys the crew a car and a machine.
    ///
    /// Every line of it goes through the ledger's own ops (Promote, AssignToCrew,
    /// Recruit, AddEquipment, GiveEquipment, and the quartermaster's deal behind them),
    /// so what stands on the pavement is an outfit a player could have arranged himself
    /// on the P-key pages - the run tests the game, not a back door into it.
    /// </summary>
    public sealed class MonkeyOutfit : MonoBehaviour
    {
        public int lieutenants = 3;
        public int hoodsEach = 3;
        public string car = "Sedan";
        public string motorcycle = "Motorbike";
        public int seed = 1987;

        bool _done;

        void Update()
        {
            if (_done) return;
            var director = PersonnelDirector.Instance;
            if (director == null || director.Roster == null) return;
            _done = true;
            Write(director);
        }

        void Write(PersonnelDirector director)
        {
            var roster = director.Roster;
            var rng = new System.Random(seed);

            // a clean book: the seeded crew back into the pool, then the crews the run
            // wants promoted out of it
            for (var i = roster.Crews.Count - 1; i >= 0; i--)
                director.Demote(roster.Crews[i].LieutenantId);

            var free = new System.Collections.Generic.List<int>();
            foreach (var member in roster.Members)
                if (!member.Gone && member.Id != roster.FrontId &&
                    member.Specialty == Specialty.None)
                    free.Add(member.Id);

            int at = 0, made = 0, hoods = 0, guns = 0;
            for (var i = 0; i < Mathf.Max(1, lieutenants); i++)
            {
                var boss = at < free.Count ? free[at++] : Sign(roster, rng);
                if (boss < 0 || !director.Promote(boss, out var crewId).Ok) continue;
                made++;

                for (var h = 0; h < Mathf.Min(hoodsEach, Crew.MaxHoods); h++)
                {
                    var hood = at < free.Count ? free[at++] : -1;
                    if (hood < 0 && director.Recruit(crewId, out _).Ok) { hoods++; continue; }
                    if (hood < 0) hood = Sign(roster, rng);
                    if (hood >= 0 && director.AssignToCrew(hood, crewId).Ok) hoods++;
                }

                // a piece for every hand, handed to the lieutenant: the quartermaster
                // deals them out behind him, best gun to the best shot
                for (var k = 0; k <= Mathf.Min(hoodsEach, Crew.MaxHoods); k++)
                {
                    var pick = ArmoryCatalog.Weapons[rng.Next(ArmoryCatalog.Weapons.Length)];
                    var item = director.AddEquipment(pick.Kind, pick.DisplayName, pick.Price);
                    if (item != null && director.GiveEquipment(item.Id, boss).Ok) guns++;
                }
            }

            // A car for one crew and a machine for ANOTHER, where there is more than one
            // lieutenant: the two men who ride are taken off the crew that owns the
            // machine, so putting both in one crew's hands leaves the monkey with one
            // crew that can never do either job while it is doing the other.
            var wheels = Buy(director, ArmoryCatalog.Vehicles, car, first: true);
            var machine = Buy(director, ArmoryCatalog.Motorcycles, motorcycle, first: false);

            Debug.Log($"[monkey] the outfit as written: {made} lieutenants, {hoods} hoods, " +
                      $"{guns} guns off the counter" +
                      (wheels ? ", a " + car.ToLowerInvariant() : ", NO CAR") +
                      (machine ? ", a " + motorcycle.ToLowerInvariant() : ", NO MACHINE"));
        }

        /// <summary>One listing off the counter, paid for and signed for by a lieutenant -
        /// which is the project's rule for all gear (weapons-via-lieutenant). The safe is
        /// asked but not obeyed: the lab wants the drive-by it asked for rather than the
        /// drive-by it can afford, and the quartermaster still deals it.</summary>
        static bool Buy(PersonnelDirector director, ArmoryItem[] counter, string named,
                        bool first)
        {
            if (string.IsNullOrEmpty(named)) return false;

            var found = false;
            var listing = default(ArmoryItem);
            foreach (var item in counter)
                if (item.DisplayName == named) { listing = item; found = true; break; }
            if (!found)
            {
                Debug.LogWarning($"[monkey] '{named}' is not on the armory counter.");
                return false;
            }

            if (OutfitDirector.Instance != null)
                OutfitDirector.Instance.Purchase(listing.Price, listing.DisplayName);

            var stock = director.AddEquipment(listing.Kind, listing.DisplayName, listing.Price);
            if (stock == null) return false;

            var lieutenants = new List<int>();
            foreach (var member in director.Roster.Members)
                if (!member.Gone && member.Rank == Rank.Lieutenant)
                    lieutenants.Add(member.Id);

            if (lieutenants.Count > 0)
            {
                var order = first ? 0 : lieutenants.Count - 1;
                if (director.GiveEquipment(stock.Id, lieutenants[order]).Ok) return true;
                foreach (var id in lieutenants)
                    if (director.GiveEquipment(stock.Id, id).Ok) return true;
            }

            Debug.LogWarning("[monkey] no lieutenant to sign for " + listing.DisplayName);
            return false;
        }

        static int Sign(Roster roster, System.Random rng) =>
            RosterSeeder.Recruit(roster, rng).Id;
    }
}
