using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>One generated residential catalogue and the district frame that places
    /// its model-space recipes into the shared world/map.</summary>
    public readonly struct ResidentialMapSource
    {
        public readonly ResidentialBlockModel Model;
        public readonly DistrictFrame Frame;

        public ResidentialMapSource(ResidentialBlockModel model, DistrictFrame frame)
        {
            Model = model;
            Frame = frame;
        }
    }

    /// <summary>The host side of generated-block streaming, shared by CoreDemo and any
    /// future game scene that installs CoreDistrict through RoadDemoBuilder.</summary>
    public partial class RoadDemoBuilder : IStreamedDistrictHost
    {
        [Header("City view")]
        [Tooltip("Shared street/map and generated-block budget. Null loads Assets/Configs/CityViewConfig.asset.")]
        public CityViewConfig cityViewConfig;

        readonly List<Transform> _streamRoots = new List<Transform>();
        readonly List<CityBlockRecycler> _blockRecyclers = new List<CityBlockRecycler>();
        readonly List<ResidentialMapSource> _residentialMapSources =
            new List<ResidentialMapSource>();
        CityViewConfig _resolvedCityView;
        float _runtimeMax3DDistance = -1f;

        CityViewConfig ResolvedCityView =>
            _resolvedCityView != null
                ? _resolvedCityView
                : (_resolvedCityView = CityViewConfig.Resolve(cityViewConfig));

        CityViewConfig IStreamedDistrictHost.ViewConfig => ResolvedCityView;

        /// <summary>The same local-map scale used by every scene hosted by this runtime.</summary>
        public float MinimapViewHeight => ResolvedCityView.MinimapViewHeight;

        /// <summary>Recipe sources are model data, never the currently attached views.
        /// TurfMapSurvey reads these so recycling cannot erase a block from the map.</summary>
        public IReadOnlyList<ResidentialMapSource> ResidentialMapSources =>
            _residentialMapSources;

        /// <summary>External district progression hook, including blocks without a live view.
        /// It deliberately emits no geometry invalidation or business-state change.</summary>
        public bool SetResidentialNeglect(string recipeId, float value)
        {
            if (string.IsNullOrEmpty(recipeId)) return false;
            foreach (var source in _residentialMapSources)
                if (source.Model.TryGet(recipeId, out var recipe))
                {
                    recipe.SetNeglect(value);
                    return true;
                }
            return false;
        }

        /// <summary>Bumped when a future generator replaces a recipe. Both map surfaces
        /// compare this stamp before redrawing their cached geometry.</summary>
        public int ResidentialGeometryVersion { get; private set; }

        Transform IStreamedDistrictHost.StreamRoot(string name)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_districtGroup != null ? _districtGroup : DistrictRoot, false);
            _streamRoots.Add(root);
            return root;
        }

        void IStreamedDistrictHost.RegisterResidentialModel(
            ResidentialBlockModel model, DistrictFrame frame)
        {
            if (model == null) return;
            for (int i = 0; i < _residentialMapSources.Count; i++)
                if (ReferenceEquals(_residentialMapSources[i].Model, model)) return;

            _residentialMapSources.Add(new ResidentialMapSource(model, frame));
            model.Changed += ResidentialGeometryChanged;
            unchecked { ResidentialGeometryVersion++; }
        }

        void ResidentialGeometryChanged(
            ResidentialBlockRecipe recipe, ResidentialBlockChange change)
        {
            unchecked { ResidentialGeometryVersion++; }
        }

        void IStreamedDistrictHost.RegisterBlockRecycler(CityBlockRecycler recycler)
        {
            if (recycler == null || _blockRecyclers.Contains(recycler)) return;
            _blockRecyclers.Add(recycler);
            if (_rig != null) recycler.SetCamera(_rig);
        }

        /// <summary>Runtime settings/UI hook. The shared map and every recycler read the
        /// same threshold, so a 300 m preset cannot leave one of them at 180 m.</summary>
        public void SetMax3DDistance(float metres)
        {
            _runtimeMax3DDistance = Mathf.Max(40f, metres);
            if (_rig != null) _rig.mapAt = _runtimeMax3DDistance;
        }

        void ConfigureCityView(DemoCamera rig)
        {
            if (rig == null) return;
            rig.mapAt = _runtimeMax3DDistance >= 40f
                ? _runtimeMax3DDistance
                : ResolvedCityView.Max3DDistance;
            rig.ConfigurePitch(ResolvedCityView.StreetPitch, ResolvedCityView.PitchFreedom);
            for (int i = 0; i < _blockRecyclers.Count; i++)
                if (_blockRecyclers[i] != null) _blockRecyclers[i].SetCamera(rig);
        }

        void DisposeStreaming()
        {
            for (int i = 0; i < _residentialMapSources.Count; i++)
                if (_residentialMapSources[i].Model != null)
                    _residentialMapSources[i].Model.Changed -= ResidentialGeometryChanged;
            _residentialMapSources.Clear();
            _blockRecyclers.Clear();
            _streamRoots.Clear();
        }
    }
}
