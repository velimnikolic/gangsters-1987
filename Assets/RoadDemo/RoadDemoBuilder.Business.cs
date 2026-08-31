using LivingCity.Business;
using UnityEngine;

namespace RoadDemo
{
    public partial class RoadDemoBuilder
    {
        // ------------------------------------------------- the city's businesses
        //
        // One scene owner for every simulated shop, yard and firm, dealt ONCE from the
        // plan data the districts have just published and before anything asks who
        // trades where: the outfit fronts pick a door, the map draws the turf, the
        // territory runtime answers approach orders, and all three now read the same
        // directory rather than a hierarchy that is half streamed out.
        //
        // The pass deliberately runs after BuildDistricts (the plans exist) and before
        // BuildCityLife (the first consumer). It costs one plan sweep and no geometry.
        void BuildBusinessSimulation()
        {
            var go = new GameObject("Business Simulation");
            go.transform.SetParent(transform, false);
            var runtime = go.AddComponent<BusinessRuntime>();
            runtime.Init(this, BuiltFromSeed);
        }

        /// <summary>
        /// A recycled block view has just been composed: bind whatever businesses stand on
        /// that recipe to the pieces now in the world. Called by CityBlockRecycler, and a
        /// pure projection - see BusinessRuntime.BindBlockView.
        /// </summary>
        internal static int BindBusinessViews(string recipeId, Transform content)
        {
            var runtime = BusinessRuntime.Instance;
            return runtime != null ? runtime.BindBlockView(recipeId, content) : 0;
        }
    }
}
