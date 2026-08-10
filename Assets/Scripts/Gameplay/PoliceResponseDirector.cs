using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.City;
using LivingCity.Entities;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The dispatcher: turns the wanted level into boots on the street. Ambient patrols
    /// (PoliceDirector's) walk their beats regardless; THIS force exists only while the
    /// player is wanted, scaled by the star table in PoliceConfig - officers on foot from
    /// one star, cars that drive in and disgorge more officers from three.
    ///
    /// Pooled, not churned: a stood-down officer deactivates back into the pool and the
    /// next escalation reuses him. Officers the player kills are corpses - they leave the
    /// pool for good and CorpseEvidence disposes of them like any other body.
    ///
    /// Units spawn near the INCIDENT but never near the player: a response that
    /// materialises in his lap reads as teleporting police, so spawn points keep
    /// spawnMinDistanceFromPlayer away - ideally arriving from off-screen.
    ///
    /// Also owns the two endings: an arrest freezes the world for the ARRESTED card and
    /// respawns the player clean; a death does the same with WASTED. Both clear the slate -
    /// jail time and lost takings can plug in here later.
    /// </summary>
    public sealed class PoliceResponseDirector : MonoBehaviour
    {
        [Tooltip("The authored police walker (man_police_AI). Set by GameplaySetup.")]
        [SerializeField] GameObject officerPrefab;

        [Tooltip("The pack's police car. Set by GameplaySetup.")]
        [SerializeField] GameObject carPrefab;

        [Tooltip("People Interaction Controller - the pistol states live there.")]
        [SerializeField] RuntimeAnimatorController animatorController;

        [Tooltip("The revolver, equipped into every response officer's hand.")]
        [SerializeField] GameObject weaponPrefab;

        readonly List<PoliceResponseOfficer> active = new List<PoliceResponseOfficer>();
        readonly List<PoliceResponseOfficer> pool = new List<PoliceResponseOfficer>();
        readonly List<PoliceResponseCar> activeCars = new List<PoliceResponseCar>();
        readonly List<PoliceResponseCar> carPool = new List<PoliceResponseCar>();

        PlayerMafioso player;
        float nextTickAt;
        int unitCounter;
        int carCounter;
        bool endingRunning;

        /// <summary>For the debug panel: every officer currently on the street.</summary>
        public IReadOnlyList<PoliceResponseOfficer> ActiveOfficers => active;

        PoliceConfig Police => GameplayRuntime.Police;

        void Start()
        {
            player = FindAnyObjectByType<PlayerMafioso>();

            // Self-wire whatever the scene did not: the ambient police already carry the
            // PrefabDatabase, and the player already carries the revolver. A runtime-
            // bootstrapped director (never touched by the editor menu) fields the same
            // force as a wired one.
            if (!officerPrefab || !carPrefab || !animatorController)
            {
                var patrol = FindAnyObjectByType<PoliceDirector>();
                var database = patrol ? patrol.Prefabs : null;
                if (database)
                {
                    if (!officerPrefab)
                        officerPrefab = database.policeOfficerPrefab;
                    if (!carPrefab)
                        carPrefab = database.policeCarPrefab;
                    if (!animatorController)
                        animatorController = database.pedestrianController;
                }
            }

            if (!weaponPrefab && player)
            {
                var socket = player.GetComponent<WeaponSocket>();
                if (socket)
                    weaponPrefab = socket.Prefab;
            }

            if (!officerPrefab)
                Debug.LogWarning("[PoliceResponse] No officer prefab anywhere - the wanted " +
                                 "level will rise but nobody will come. Run the City asset " +
                                 "bootstrap.", this);
        }

        void Update()
        {
            if (Time.time < nextTickAt)
                return;
            var config = Police;
            nextTickAt = Time.time + (config ? config.responseTickSeconds : 0.5f);

            var wanted = WantedSystem.Instance;
            if (!wanted || !player || !officerPrefab)
                return;

            var level = Mathf.Clamp(wanted.WantedLevel, 0, 5);
            if (level <= 0)
            {
                // Officers notice the cleared slate themselves and stand down; parked cars
                // are released here.
                for (var i = activeCars.Count - 1; i >= 0; i--)
                    activeCars[i].Release();
                return;
            }

            var incident = wanted.LastKnownPosition;

            var desiredOfficers = TableValue(config ? config.officersPerLevel : null, level, 1);
            while (active.Count < desiredOfficers)
                if (!Deploy(incident, nearPlayerAllowed: false))
                    break;

            var desiredCars = carPrefab
                ? TableValue(config ? config.carsPerLevel : null, level, 0)
                : 0;
            while (activeCars.Count < desiredCars)
                if (!DispatchCar(incident))
                    break;
        }

        static int TableValue(int[] table, int level, int fallback)
        {
            if (table == null || table.Length == 0)
                return fallback * level;

            return table[Mathf.Clamp(level, 0, table.Length - 1)];
        }

        // ------------------------------------------------------------------- officers

        bool Deploy(Vector3 incident, bool nearPlayerAllowed, Vector3? exactPoint = null)
        {
            var officer = TakeFromPool();
            if (!officer)
                return false;

            var spawn = exactPoint ?? FindSpawnPoint(incident, nearPlayerAllowed);
            officer.transform.position = spawn;
            officer.gameObject.SetActive(true);
            active.Add(officer);
            officer.Deploy();
            return true;
        }

        PoliceResponseOfficer TakeFromPool()
        {
            while (pool.Count > 0)
            {
                var last = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                if (last && !last.IsDown)
                    return last;
            }

            return BuildOfficer();
        }

        /// <summary>
        /// One officer, built once and pooled forever: the authored police walker with its
        /// HumanBehavior permanently off (a response officer is always hand-steered), the
        /// interaction controller (pistol states), a revolver in the hand, and the full
        /// interactable chain - the player can shoot back, and the force will know.
        /// </summary>
        PoliceResponseOfficer BuildOfficer()
        {
            if (!officerPrefab)
                return null;

            var go = Instantiate(officerPrefab, transform);
            go.name = $"Response Officer {unitCounter + 1}";
            PedestrianSpawner.SetLayerRecursively(go.transform, PedestrianSpawner.PedestrianLayer);

            var human = go.GetComponent<HumanBehavior>();
            if (human)
                human.enabled = false;

            var animator = go.GetComponent<Animator>();
            if (animator && animatorController)
                animator.runtimeAnimatorController = animatorController;

            // Socket before GunmanAim: GunmanAim caches the socket in its Awake, which
            // runs the instant the component is added.
            var socket = go.AddComponent<WeaponSocket>();
            if (weaponPrefab && socket.Hand)
                socket.Adopt(Instantiate(weaponPrefab, socket.Hand).transform);

            go.AddComponent<GunmanAim>();
            go.AddComponent<WeaponController>();
            go.AddComponent<InteractableNpc>();

            var officer = go.AddComponent<PoliceResponseOfficer>();
            officer.UnitNumber = ++unitCounter;
            officer.Configure(this, player);

            go.SetActive(false);
            return officer;
        }

        public void Release(PoliceResponseOfficer officer)
        {
            active.Remove(officer);
            if (!officer || officer.IsDown)
                return;

            officer.gameObject.SetActive(false);
            pool.Add(officer);
        }

        /// <summary>Killed in the line of duty - a corpse now, never pooled again.</summary>
        public void OnOfficerDown(PoliceResponseOfficer officer)
        {
            active.Remove(officer);
        }

        /// <summary>
        /// Somewhere walkable near the incident but never near the player - the response
        /// arrives, it does not materialise.
        /// </summary>
        Vector3 FindSpawnPoint(Vector3 incident, bool nearPlayerAllowed)
        {
            var config = Police;
            var minFromPlayer = config ? config.spawnMinDistanceFromPlayer : 25f;
            var maxFromIncident = config ? config.spawnMaxDistanceFromIncident : 60f;

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var angle = Random.value * Mathf.PI * 2f;
                var distance = Mathf.Lerp(maxFromIncident * 0.5f, maxFromIncident, Random.value);
                var candidate = incident +
                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

                if (!nearPlayerAllowed && player)
                {
                    var toPlayer = candidate - player.transform.position;
                    toPlayer.y = 0f;
                    if (toPlayer.sqrMagnitude < minFromPlayer * minFromPlayer)
                        continue;
                }

                var tile = NearestWalkableTile(candidate);
                if (tile)
                    return PedestrianSpawner.SidewalkPoint(
                        tile, Random.value < 0.5f ? 1f : -1f, 0f);
            }

            var fallback = NearestWalkableTile(incident);
            return fallback
                ? PedestrianSpawner.SidewalkPoint(fallback, 1f, 0f)
                : incident;
        }

        static Tile NearestWalkableTile(Vector3 point)
        {
            Tile best = null;
            var bestSqr = float.MaxValue;
            foreach (var tile in Tile.Tiles)
            {
                if (!tile || (tile.tileType != Tile.TileType.Road &&
                              tile.tileType != Tile.TileType.OnlyPathwalk))
                    continue;

                var sqr = (tile.transform.position - point).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = tile;
                }
            }

            return best;
        }

        // ----------------------------------------------------------------------- cars

        bool DispatchCar(Vector3 incident)
        {
            var car = TakeCarFromPool();
            if (!car)
                return false;

            var tile = DistantRoadTile(incident);
            if (!tile)
                return false;

            car.transform.SetPositionAndRotation(tile.transform.position, Quaternion.identity);
            car.gameObject.SetActive(true);
            activeCars.Add(car);
            car.Dispatch(incident);
            return true;
        }

        PoliceResponseCar TakeCarFromPool()
        {
            if (carPool.Count > 0)
            {
                var last = carPool[carPool.Count - 1];
                carPool.RemoveAt(carPool.Count - 1);
                if (last)
                    return last;
            }

            if (!carPrefab)
                return null;

            var go = Instantiate(carPrefab, transform);
            go.name = $"Response Car {carCounter + 1}";

            var car = go.AddComponent<PoliceResponseCar>();
            car.UnitNumber = ++carCounter;
            car.Configure(this);

            go.SetActive(false);
            return car;
        }

        /// <summary>A road tile far enough away that the car ARRIVES rather than appears.</summary>
        static Tile DistantRoadTile(Vector3 incident)
        {
            Tile best = null;
            var bestScore = float.MaxValue;
            foreach (var tile in Tile.Tiles)
            {
                if (!tile || (tile.tileType != Tile.TileType.Road &&
                              tile.tileType != Tile.TileType.RoadAndRail))
                    continue;

                var flat = tile.transform.position - incident;
                flat.y = 0f;
                // Closest to 80m out - far enough to drive in, near enough to matter.
                var score = Mathf.Abs(flat.magnitude - 80f) + Random.value * 20f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = tile;
                }
            }

            return best;
        }

        /// <summary>The car is parked at the scene - its crew hits the pavement.</summary>
        public void OnCarArrived(PoliceResponseCar car)
        {
            var config = Police;
            var crew = config ? config.officersPerCar : 2;
            var wanted = WantedSystem.Instance;
            var incident = wanted ? wanted.LastKnownPosition : car.transform.position;

            for (var i = 0; i < crew; i++)
            {
                var beside = car.transform.position +
                             car.transform.right * (i % 2 == 0 ? 2f : -2f) +
                             car.transform.forward * (i / 2) * 1.5f;
                Deploy(incident, nearPlayerAllowed: true, exactPoint: beside);
            }
        }

        public void ReleaseCar(PoliceResponseCar car)
        {
            activeCars.Remove(car);
            if (!car)
                return;

            car.gameObject.SetActive(false);
            carPool.Add(car);
        }

        // -------------------------------------------------------------------- endings

        /// <summary>An officer completed the arrest. One card, whoever got there first.</summary>
        public void OnArrest()
        {
            if (!endingRunning)
                StartCoroutine(Ending(arrested: true));
        }

        /// <summary>The shootout went the other way.</summary>
        public void OnPlayerDown()
        {
            if (!endingRunning)
                StartCoroutine(Ending(arrested: false));
        }

        IEnumerator Ending(bool arrested)
        {
            endingRunning = true;

            if (WantedHud.Instance)
                WantedHud.Instance.ShowEnding(arrested);

            yield return new WaitForSeconds(arrested ? 3f : 4f);

            if (player)
                player.Respawn();
            if (WantedSystem.Instance)
                WantedSystem.Instance.ClearWanted();

            if (WantedHud.Instance)
                WantedHud.Instance.HideEnding();

            endingRunning = false;
        }
    }
}
