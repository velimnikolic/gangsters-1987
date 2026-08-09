using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Data
{
    /// <summary>
    /// Tuning for the park block: archetype weights, path widths, entrance count, planting and
    /// prop spacing, density bounds. The knobs of ONE zone, so its own asset rather than more
    /// fields on CityConfig - the IndustrialLotConfig precedent, trap included: the generator
    /// runs perfectly well without it (ParkDresser falls back to the same defaults this asset
    /// ships with), and CityAssetBootstrap.GetOrCreate never rewrites an asset that already
    /// exists, so a default changed HERE does not reach a project that already has the .asset.
    /// Change it in the inspector, or delete the asset and re-run
    /// Tools/City/Create or Refresh Config Assets.
    ///
    /// The values live in ParkLayout.Tuning - a plain serializable struct - because the layout
    /// is pure geometry tested in a bare .NET host, where a ScriptableObject cannot follow.
    /// </summary>
    [CreateAssetMenu(fileName = "ParkConfig", menuName = "Living City/Park Config")]
    public sealed class ParkConfig : ScriptableObject
    {
        [SerializeField] ParkLayout.Tuning tuning = ParkLayout.Tuning.Default;

        public ParkLayout.Tuning Tuning => tuning;

        void OnValidate()
        {
            tuning.minEntrances = Mathf.Clamp(tuning.minEntrances, 1, 4);
            tuning.maxEntrances = Mathf.Clamp(tuning.maxEntrances, tuning.minEntrances, 4);
            tuning.mainPathWidth = Mathf.Max(1f, tuning.mainPathWidth);
            tuning.secondaryPathWidth = Mathf.Max(1f, tuning.secondaryPathWidth);
            tuning.gateHalfWidth = Mathf.Max(1f, tuning.gateHalfWidth);
            tuning.lampSpacingMax = Mathf.Max(tuning.lampSpacingMax, tuning.lampSpacingMin);
            tuning.lampMinSeparation = Mathf.Max(1f, tuning.lampMinSeparation);
            tuning.groveMax = Mathf.Max(tuning.groveMax, tuning.groveMin);
            tuning.treeScaleMin = Mathf.Clamp(tuning.treeScaleMin, 0.5f, 1f);
            tuning.treeScaleMax = Mathf.Clamp(tuning.treeScaleMax, tuning.treeScaleMin, 1.5f);
            tuning.accentShare = Mathf.Clamp01(tuning.accentShare);
            tuning.maxDeadTrees = Mathf.Clamp(tuning.maxDeadTrees, 0, 8);
            tuning.knollScaleMin = Mathf.Clamp(tuning.knollScaleMin, 0.05f, 0.3f);
            tuning.knollScaleMax = Mathf.Clamp(tuning.knollScaleMax, tuning.knollScaleMin, 0.3f);
            tuning.densityMaxPer100 = Mathf.Max(tuning.densityMaxPer100, tuning.densityMinPer100);
            tuning.maxStations = Mathf.Max(10, tuning.maxStations);
        }
    }
}
