using System.Collections.Generic;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>One man of a crew, as the map's book prints him.</summary>
    public struct MapCrewMan
    {
        public string Name;
        public string Role;
        public string Weapon;

        /// <summary>Nought to one.</summary>
        public float Condition;
    }

    /// <summary>
    /// A crew as the map holds it: a dot on the sheet, a row in the roster and a card in
    /// the inspector, all off one record.
    ///
    /// The design sheet's point is that a crew is NOT anonymous. It carries its
    /// lieutenant's full name and his face, so the player learns the map by the men on
    /// it rather than by a list of numbered units.
    /// </summary>
    public sealed class MapCrew
    {
        public int Id;

        /// <summary>0 the outfit, -2 the law, anything else a rival family.</summary>
        public int Gang;

        /// <summary>The LEADER'S FULL NAME. Not a unit number and not a street handle -
        /// this is who the crew is.</summary>
        public string Name = "-";

        public string Surname = "-";

        /// <summary>The street handle. Derived from the ground the crew works, because
        /// nothing in this project records one - see the note on Collect.</summary>
        public string Alias = "-";

        /// <summary>His rank, off the personnel ledger. Taken from the SAME place the
        /// men's roles below are taken from, so the header cannot contradict the list
        /// under it - which is the whole point of the rule.</summary>
        public string Rank = "-";

        /// <summary>The prefab the leader was cast from. It keys his portrait through
        /// PortraitStudio's own cache, which is exactly what the sheet's `crew.mug`
        /// slug does: one stable key, one print, reused everywhere he appears.</summary>
        public GameObject Mug;

        public readonly List<MapCrewMan> Men = new List<MapCrewMan>();

        public string Ride = "ON FOOT";
        public int Heat;
        public int Loyalty;

        /// <summary>What the crew costs a week, off the ledger's own wage table. NOT
        /// what it earns - see the note on Collect.</summary>
        public int Wage;

        public float Condition;
        public Vector3 Position;
        public DemoCrews.Unit Unit;

        public int Strength => Men.Count;
    }

    /// <summary>
    /// Reads the city's crews into the records the map draws and prints.
    ///
    /// Everything here comes from somewhere real: the men from
    /// <see cref="DemoCrews"/>, their names, ranks, wages and wanted flags from the
    /// personnel ledger, their weapons from what they are actually carrying, the ride
    /// from the car the crew is signed out. Two fields could not be:
    ///
    /// ALIAS. Nothing in this project gives a crew a street handle. It is derived from
    /// the quarter the crew is standing in - "BRICKTOWN CREW" - which is a derivation
    /// off real city data rather than an invented name, and it changes if they move,
    /// which is honest for a handle that means "the crew that works this ground".
    ///
    /// WAGE, not TAKE. The design sheet asks for WEEKLY TAKE. This project has no
    /// per-crew income at all - takings live on a family's FRONT, one per family, not on
    /// its crews - but it does have a real weekly wage per man. So the card prints the
    /// wage and says WAGE. Printing a cost under a heading that says "take" would be a
    /// lie with a real number attached, which is worse than a blank.
    /// </summary>
    public sealed class MapCrews
    {
        readonly List<MapCrew> _all = new List<MapCrew>();
        readonly Dictionary<int, MapCrew> _byId = new Dictionary<int, MapCrew>();
        LivingCity.Gameplay.PersonnelDirector _personnel;

        public IReadOnlyList<MapCrew> All => _all;

        public MapCrew Get(int crewId) =>
            _byId.TryGetValue(crewId, out var crew) ? crew : null;

        /// <summary>The player's crews only, in the order the roster prints them.</summary>
        public int Mine { get; private set; }

        /// <summary>Every man the player has standing - what MANPOWER counts.</summary>
        public int Manpower { get; private set; }

        public void Collect(DemoCrews crews, MapTurf turf)
        {
            _all.Clear();
            _byId.Clear();
            Mine = 0;
            Manpower = 0;
            if (crews == null)
                return;

            if (_personnel == null)
                _personnel = Object.FindAnyObjectByType<LivingCity.Gameplay.PersonnelDirector>();
            var roster = _personnel != null ? _personnel.Roster : null;

            foreach (var unit in crews.Units)
            {
                if (unit == null || unit.Wiped)
                    continue;

                var boss = unit.Boss;
                var crew = new MapCrew
                {
                    Id = unit.CrewId,
                    Gang = unit.IsPolice ? -2 : unit.Faction,
                    Unit = unit,
                    Loyalty = unit.Loyalty,
                    Position = boss != null && boss.Tf != null ? boss.Tf.position : Vector3.zero,
                };

                Name(crew, unit, boss, roster);
                Book(crew, unit, roster);
                Wheels(crew, unit, crews);

                if (turf != null)
                {
                    var district = turf.At(new Vector2(crew.Position.x, crew.Position.z));
                    if (district != null && !string.IsNullOrEmpty(district.Name))
                        crew.Alias = district.Name.ToUpperInvariant() + " CREW";
                }

                _all.Add(crew);
                _byId[crew.Id] = crew;

                if (crew.Gang != 0 || unit.IsPolice)
                    continue;
                Mine++;
                Manpower += crew.Strength;
            }
        }

        static void Name(MapCrew crew, DemoCrews.Unit unit, CrewWalker boss, Roster roster)
        {
            var full = boss != null && !string.IsNullOrEmpty(boss.DisplayName)
                ? boss.DisplayName
                : unit.Name;
            if (string.IsNullOrEmpty(full))
                full = "CREW " + unit.CrewId;

            crew.Name = full;
            var space = full.LastIndexOf(' ');
            crew.Surname = space > 0 ? full.Substring(space + 1) : full;
            crew.Mug = boss != null ? boss.SourcePrefab : null;

            var leader = Man(roster, boss);
            crew.Rank = leader != null
                ? leader.Rank.ToString().ToUpperInvariant()
                : (boss != null && boss.IsLieutenant ? "LIEUTENANT" : "HOOD");
        }

        static void Book(MapCrew crew, DemoCrews.Unit unit, Roster roster)
        {
            var health = 0f;
            var most = 0f;

            foreach (var man in unit.All())
            {
                if (man == null || man.Dead)
                    continue;

                var character = Man(roster, man);
                var whole = Mathf.Max(1, man.MaxHealth);
                var condition = Mathf.Clamp01((float)man.Health / whole);
                health += man.Health;
                most += whole;

                crew.Men.Add(new MapCrewMan
                {
                    Name = !string.IsNullOrEmpty(man.DisplayName) ? man.DisplayName
                        : character != null ? character.FullName : "-",
                    Role = character != null
                        ? character.Rank.ToString().ToUpperInvariant()
                        : (man.IsLieutenant ? "LIEUTENANT" : "HOOD"),
                    Weapon = man.WeaponPrefab != null
                        ? LivingCity.UI.LedgerText.EquipmentLabel(man.WeaponKind).ToUpperInvariant()
                        : "UNARMED",
                    Condition = condition,
                });

                if (character == null)
                    continue;
                if (character.Wanted)
                    crew.Heat++;
                crew.Wage += LivingCity.Outfit.Wages.WageFor(character);
            }

            crew.Condition = most > 0f ? health / most : 0f;
        }

        static void Wheels(MapCrew crew, DemoCrews.Unit unit, DemoCrews crews)
        {
            var car = crews.CarOf(unit);
            crew.Ride = car != null && !string.IsNullOrEmpty(car.DisplayName)
                ? car.DisplayName.ToUpperInvariant()
                : "ON FOOT";
        }

        /// <summary>The ledger entry behind a man on the street. Rivals are on nobody's
        /// books and answer null, which is why every read of this is guarded.</summary>
        static Character Man(Roster roster, CrewWalker walker) =>
            roster != null && walker != null && walker.CharacterId >= 0
                ? roster.Find(walker.CharacterId)
                : null;
    }
}
