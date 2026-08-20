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
        [Tooltip("Crews of the outfit on the street - one lieutenant each.")]
        [Min(1)] public int lieutenants = 3;
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

            Wrote = $"{made} lieutenant{(made == 1 ? "" : "s")}, {hoods} hood" +
                    $"{(hoods == 1 ? "" : "s")}, {guns} gun{(guns == 1 ? "" : "s")} off the counter";
            if (made < want)
                Wrote += $" (asked for {want})";
            Debug.Log("[BlockDemo] the outfit as written: " + Wrote);
            if (RoadDemo.DriveTrace.On)
                RoadDemo.DriveTrace.Event("outfit", "book", Wrote);
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
