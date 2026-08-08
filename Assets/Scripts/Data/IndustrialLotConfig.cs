using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Data
{
    /// <summary>
    /// Tuning for the works yard dressing: how the ground inside a compound is partitioned, what
    /// is laid on it, and how worn it looks.
    ///
    /// Its own asset rather than more fields on CityConfig because these are the knobs for one
    /// zone, and CityConfig is already the place every city-wide number lives. The generator runs
    /// perfectly well without it - IndustrialLotBuilder falls back to the same defaults this
    /// asset ships with - so a project that has not re-run the bootstrap still builds yards
    /// rather than failing on a null reference.
    ///
    /// Note the trap this shares with CityConfig: CityAssetBootstrap.GetOrCreate never rewrites
    /// an asset that already exists, so a default changed HERE does not reach a project that
    /// already has the .asset. Change it in the inspector, or delete the asset and re-run
    /// Tools/City/Create or Refresh Config Assets.
    /// </summary>
    [CreateAssetMenu(fileName = "IndustrialLotConfig", menuName = "Living City/Industrial Lot Config")]
    public sealed class IndustrialLotConfig : ScriptableObject
    {
        [Header("Zoning")]
        [SerializeField] IndustrialLotPlanner.Tuning zoning = IndustrialLotPlanner.Tuning.Default;

        [Header("Surface")]
        [Tooltip("Patches jittered along each of a zone's two LONG edges, so concrete meets dirt " +
                 "on a worn line rather than a drawn one. A ragged edge sells the yard harder " +
                 "than any prop.\n\n" +
                 "Costs two objects per zone per step, and a dense works plans sixteen zones - " +
                 "at four per edge on all four edges a single yard came to 270 surface objects.")]
        [Range(0, 10)] public int fringePatchesPerEdge = 3;

        [Tooltip("How far a fringe patch reaches past its zone edge.")]
        public float minFringeReach = 1.5f;
        public float maxFringeReach = 4f;

        [Tooltip("How much of one edge a single fringe patch runs along.")]
        public float minFringeRun = 2f;
        public float maxFringeRun = 6f;

        [Header("Grime")]
        [Tooltip("Track gauge of the tyre ruts drawn from the gate down each carriageway.")]
        public float rutGauge = 1.8f;

        public float rutWidth = 0.55f;

        [Tooltip("Oil stains dropped on the loading aprons and the truck staging.")]
        [Range(0, 40)] public int maxStains = 14;

        public float minStainRadius = 0.7f;
        public float maxStainRadius = 2.2f;

        [Header("Rail spur")]
        [Tooltip("A stub-end works spur, painted rather than tiled: the pack's rail tile is 30m " +
                 "and mostly ballast, and the deepest free band in a compound is about 16m. " +
                 "Drawn only where a straight run of at least this length exists.")]
        public float minSpurRun = 20f;

        public float spurGauge = 1.5f;
        public float sleeperPitch = 0.9f;

        [Header("Budget")]
        [Tooltip("Hard ceiling on prop instances per yard. Read by the prop pass, not the " +
                 "surface pass - kept here so the whole zone has one place to tune.")]
        [Min(0)] public int maxPropsPerLot = 120;

        public IndustrialLotPlanner.Tuning Zoning => zoning;

        void OnValidate()
        {
            maxFringeReach = Mathf.Max(maxFringeReach, minFringeReach);
            maxFringeRun = Mathf.Max(maxFringeRun, minFringeRun);
            maxStainRadius = Mathf.Max(maxStainRadius, minStainRadius);
            rutGauge = Mathf.Max(0.2f, rutGauge);
            sleeperPitch = Mathf.Max(0.2f, sleeperPitch);
        }
    }
}
