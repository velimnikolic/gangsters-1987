using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using UnityEngine;

namespace BlockDemo
{
    /// <summary>
    /// The outfit the lab wants to send out, dealt through the ledger's own ops.
    ///
    /// The seeded roster is six men in one crew, and a run that wants three
    /// lieutenants walking at three full mobs cannot ask the street for them - the
    /// street shows whatever the BOOK says. So the book is written first: the seeded
    /// crew is disbanded, the men promoted one crew each, hoods dealt behind them if
    /// the run wants any, and a gun bought off the armory counter for every man.
    /// Nothing here reaches past the ledger's rules (Promote, AssignToCrew,
    /// AddEquipment, GiveEquipment, and the quartermaster's own deal behind them) -
    /// what stands on the pavement is a roster a player could have arranged himself.
    ///
    /// Runs once, the frame the roster exists; DemoCrews re-deals off the version bump.
    /// </summary>
    public class BlockDemoOutfit : MonoBehaviour
    {
        [Tooltip("Crews of the outfit on the street - one lieutenant each. 0 leaves the " +
                 "book exactly as the seeder wrote it, which is what a run that only " +
                 "wants a machine bought asks for.")]
        [Min(0)] public int lieutenants = 3;
        [Tooltip("Hoods behind each lieutenant. 4 is a full crew (the ledger's cap); 0 " +
                 "sends the lieutenants out alone. The books hold six men, so anything " +
                 "past the second crew is recruited in - paid for while the safe holds " +
                 "out, and signed straight onto the books after that.")]
        [Min(0)] public int hoodsEach = 0;
        [Tooltip("A gun off the armory counter for every man, drawn separately - a " +
                 "crew of a shotgun, a rifle and a machine pistol rather than three " +
                 ".38s. Off leaves the ledger's own arms alone.")]
        public bool mixedArms = true;
        [Tooltip("Same seed, same guns.")]
        public int armsSeed = 1987;

        [Tooltip("A motorcycle off the armory counter for the first lieutenant, by " +
                 "listing name (Motorbike, Moped, Scooter). Empty buys none. Bought and " +
                 "given exactly as the armory page would - so what stands at the kerb " +
                 "is a machine the book says the crew owns, not one the scene put down.")]
        public string motorcycle = "";

        /// <summary>What the book ended up saying, for the run's log.</summary>
        public string Wrote { get; private set; } = "";

        bool _done;

        void Update()
        {
            if (_done) return;
            var director = PersonnelDirector.Instance;
            if (director == null || director.Roster == null) return;
            _done = true;
            Shape(director);
        }

        void Shape(PersonnelDirector director)
        {
            var roster = director.Roster;
            var rng = new System.Random(armsSeed);

            // Nothing asked of the crews: the book stands as the seeder wrote it and
            // the only thing bought is the machine. A run that wants a drive-by out of
            // the six men the seeder deals has no business rewriting the roster first.
            if (lieutenants <= 0)
            {
                Wrote = BuyMachine(director) ? motorcycle + " off the counter"
                                             : "the book left alone";
                Debug.Log("[BlockDemo] the outfit as written: " + Wrote);
                if (RoadDemo.DriveTrace.On)
                    RoadDemo.DriveTrace.Event("outfit", "book", Wrote);
                return;
            }

            // the seeded crew first: everyone back to the pool, so the lab deals from
            // a clean book rather than around whatever the seeder happened to draw
            for (int i = roster.Crews.Count - 1; i >= 0; i--)
                director.Demote(roster.Crews[i].LieutenantId);

            // the front stays at his desk - he is not a man on the street
            var free = new List<int>();
            foreach (var member in roster.Members)
                if (!member.Gone && member.Id != roster.FrontId &&
                    member.Specialty == Specialty.None)
                    free.Add(member.Id);

            int want = Mathf.Max(1, lieutenants);
            int each = Mathf.Min(Mathf.Max(0, hoodsEach), Crew.MaxHoods);
            int at = 0, made = 0, hoods = 0, guns = 0;
            for (int i = 0; i < want; i++)
            {
                int boss = at < free.Count ? free[at++] : Sign(director, rng);
                if (boss < 0 || !director.Promote(boss, out int crewId).Ok) continue;
                made++;
                for (int h = 0; h < each; h++)
                {
                    int hood = at < free.Count ? free[at++] : -1;
                    // nobody left on the books: a new man at the recruiting door - the
                    // outfit's own gate first, so the money is spent the way a player's
                    // would be, and straight onto the books when the safe is empty
                    // (the lab wants the crew it asked for, not the crew it can afford)
                    if (hood < 0 && director.Recruit(crewId, out _).Ok) { hoods++; continue; }
                    if (hood < 0) hood = Sign(director, rng);
                    if (hood >= 0 && director.AssignToCrew(hood, crewId).Ok) hoods++;
                }
                guns += Arm(director, boss, 1 + each, rng);
            }

            bool wheels = BuyMachine(director);
            Wrote = $"{made} lieutenant{(made == 1 ? "" : "s")}, {hoods} hood" +
                    $"{(hoods == 1 ? "" : "s")}, {guns} gun{(guns == 1 ? "" : "s")} off the counter" +
                    (wheels ? ", and a " + motorcycle.ToLowerInvariant() : "");
            if (made < want)
                Wrote += $" (asked for {want})";
            Debug.Log("[BlockDemo] the outfit as written: " + Wrote);
            if (RoadDemo.DriveTrace.On)
                RoadDemo.DriveTrace.Event("outfit", "book", Wrote);
        }

        /// <summary>The machine, bought and issued the way a player would: paid for out
        /// of the safe on the armory page, added to the stock, and GIVEN to a
        /// lieutenant - all gear issues through a crew's head, and the quartermaster
        /// deals it to whichever of his men can ride (RosterOps.NormalizeArms, wheels by
        /// Driving). Nothing here reaches past those ops, so a machine that turns up at
        /// the kerb is one the book really sold.
        ///
        /// The safe is not asked twice: a run whose money has gone on guns gets the
        /// machine signed for anyway (director.AddEquipment on its own), because the lab
        /// wants the drive-by it asked for rather than the drive-by it can afford - the
        /// same fallback the recruiting door already has here.</summary>
        bool BuyMachine(PersonnelDirector director)
        {
            if (string.IsNullOrEmpty(motorcycle)) return false;

            var listing = default(LivingCity.Outfit.ArmoryItem);
            bool found = false;
            foreach (var item in LivingCity.Outfit.ArmoryCatalog.Motorcycles)
                if (item.DisplayName == motorcycle) { listing = item; found = true; break; }
            if (!found)
            {
                Debug.LogWarning($"[BlockDemo] '{motorcycle}' is not on the armory " +
                                 "counter - no machine bought.");
                return false;
            }

            var outfit = OutfitDirector.Instance;
            if (outfit != null) outfit.Purchase(listing.Price, listing.DisplayName);

            var stock = director.AddEquipment(listing.Kind, listing.DisplayName, listing.Price);
            if (stock == null) return false;

            foreach (var member in director.Roster.Members)
            {
                if (member.Gone || member.Rank != Rank.Lieutenant) continue;
                if (director.GiveEquipment(stock.Id, member.Id).Ok)
                {
                    Debug.Log($"[BlockDemo] {member.FullName} signed for the " +
                              listing.DisplayName.ToLowerInvariant() + ".");
                    return true;
                }
            }
            Debug.LogWarning("[BlockDemo] no lieutenant to sign for the machine - it " +
                             "stays in the lock-up.");
            return false;
        }

        /// <summary>A man signed straight onto the books, no money asked - what the lab
        /// falls back on when the safe cannot pay the recruiting door any more. Drawn by
        /// the seeder itself, so he is a man like any other: a name, eleven attributes
        /// and a loyalty off the run's own seed.</summary>
        int Sign(PersonnelDirector director, System.Random rng)
        {
            if (director.Roster == null) return -1;
            if (!_saidFree)
            {
                _saidFree = true;
                Debug.Log("[BlockDemo] the safe will not run to the crews the run asked " +
                          "for - the rest are signed on without paying.");
            }
            return LivingCity.Personnel.RosterSeeder.Recruit(director.Roster, rng).Id;
        }

        bool _saidFree;

        /// <summary>A piece for every hand in this crew. The guns are BOUGHT onto the
        /// books and handed to the lieutenant - his crew's deck - and the quartermaster
        /// deals them out behind him, best gun to the best shot, exactly as the armory
        /// page would.</summary>
        int Arm(PersonnelDirector director, int lieutenant, int men, System.Random rng)
        {
            if (!mixedArms) return 0;
            var counter = LivingCity.Outfit.ArmoryCatalog.Weapons;
            int bought = 0;
            for (int k = 0; k < men; k++)
            {
                var pick = counter[rng.Next(counter.Length)];
                var item = director.AddEquipment(pick.Kind, pick.DisplayName, pick.Price);
                if (item == null) continue;
                if (director.GiveEquipment(item.Id, lieutenant).Ok) bought++;
            }
            return bought;
        }
    }
}
