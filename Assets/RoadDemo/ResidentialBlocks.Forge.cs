using System;
using UnityEngine;
using static RoadDemo.Composer;

namespace RoadDemo
{
    /// <summary>Runtime evidence captured from the actual stood hierarchy.</summary>
    public sealed class ForgeStandAudit : MonoBehaviour
    {
        public int RequiredParts, RaisedParts;
        public int RequiredStorefronts, Storefronts;
        public int RequiredStorefrontBays, CoveredStorefrontBays;
        public bool HasRendererBounds, WithinUnitBounds;
        public Vector3 RendererMin, RendererMax;
    }

    public static partial class ResidentialBlocks
    {
        public static GameObject StandSheet(
            ResidentialFacade.Sheet sheet, Transform parent, int way = 0) =>
            StandSheet(sheet, parent, way,
                (prefab, root) => UnityEngine.Object.Instantiate(prefab, root));

        public static GameObject StandSheet(
            ResidentialFacade.Sheet sheet, Transform parent, int way,
            Func<GameObject, Transform, GameObject> raise)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (sheet.Unit == null)
                throw new ArgumentException("A facade sheet needs its synthetic unit.", nameof(sheet));
            if (raise == null) throw new ArgumentNullException(nameof(raise));

            var faults = ResidentialFacade.Judge(sheet);
            if (faults.Length != 0)
                throw new InvalidOperationException(
                    $"Cannot stand {sheet.Signature}: {faults.Length} facade fault(s); " +
                    faults[0]);

            Begin(raise);
            ForgetMissing();
            var building = new GameObject(string.IsNullOrEmpty(sheet.Signature)
                ? "forged residential building" : sheet.Signature);
            building.transform.SetParent(parent, false);
            try
            {
                var pieces = sheet.Pieces ?? Array.Empty<ResidentialFacade.Piece>();
                int stood = 0;
                for (int i = 0; i < pieces.Length; i++)
                {
                    var piece = pieces[i];
                    var module = ResidentialModules.Find(piece.Module);
                    if (module == null || string.IsNullOrEmpty(module.Path))
                        throw new InvalidOperationException("Unresolved facade module " + piece.Module);
                    var go = Raise(module.Path, building.transform);
                    if (go == null)
                        throw new InvalidOperationException("Could not raise " + module.Path);
                    stood++;
                    ResidentialFacade.Pivot(piece, module, out float x, out float z);
                    go.transform.localPosition = new Vector3(
                        x, piece.Floor * ResidentialFacade.Storey, z);
                    go.transform.localRotation = Quaternion.Euler(0f, piece.Yaw, 0f);
                    go.name = $"{module.Name} ({piece.I},{piece.J},{piece.Floor}) {piece.Yaw}";
                }

                var props = sheet.Props ?? Array.Empty<ResidentialFacade.Prop>();
                for (int i = 0; i < props.Length; i++)
                {
                    var prop = props[i];
                    var module = FindForgePrefab(prop.Prefab);
                    string path = module != null ? module.Path : prop.Prefab;
                    if (string.IsNullOrEmpty(path))
                        throw new InvalidOperationException("Unresolved facade prop " + prop.Prefab);
                    var go = Raise(path, building.transform);
                    if (go == null)
                        throw new InvalidOperationException("Could not raise " + path);
                    stood++;
                    go.transform.localPosition = new Vector3(prop.X, prop.Y, prop.Z);
                    go.transform.localRotation = Quaternion.Euler(0f, prop.Yaw, 0f);
                    go.name = $"{(module != null ? module.Name : go.name)} prop {i:00}";
                }
                if (stood != pieces.Length + props.Length)
                    throw new InvalidOperationException(
                        $"Raised {stood}/{pieces.Length + props.Length} facade parts.");

                Colourway(building, way);
                var report = new Stood();
                foreach (int _ in StorefrontDressingSteps(
                    building, sheet.Unit, "sheet:" + sheet.Unit.Name,
                    sheet.Seed, null, report)) { }
                int requiredBays = sheet.Unit.ShopBays?.Length ?? 0;
                int requiredStorefronts = 0;
                for (int i = 0; i < requiredBays; i++)
                    if (sheet.Unit.ShopBays[i].Door.Leaves > 0) requiredStorefronts++;
                var audit = building.AddComponent<ForgeStandAudit>();
                audit.RequiredParts = pieces.Length + props.Length;
                audit.RaisedParts = stood;
                audit.RequiredStorefronts = requiredStorefronts;
                audit.Storefronts = report.Storefronts;
                audit.RequiredStorefrontBays = requiredBays;
                audit.CoveredStorefrontBays = report.StorefrontBays;
                audit.HasRendererBounds = LocalRendererBounds(
                    building.transform, out audit.RendererMin, out audit.RendererMax);
                audit.WithinUnitBounds = audit.HasRendererBounds &&
                    WithinUnitBounds(sheet.Unit, audit.RendererMin, audit.RendererMax);
                if (audit.RaisedParts != audit.RequiredParts ||
                    audit.Storefronts < audit.RequiredStorefronts ||
                    audit.CoveredStorefrontBays < audit.RequiredStorefrontBays ||
                    !audit.WithinUnitBounds)
                    throw new InvalidOperationException(
                        $"Stood audit failed for {sheet.Signature}: parts " +
                        $"{audit.RaisedParts}/{audit.RequiredParts}, storefronts " +
                        $"{audit.Storefronts}/{audit.RequiredStorefronts}, bays " +
                        $"{audit.CoveredStorefrontBays}/{audit.RequiredStorefrontBays}, " +
                        $"bounds {(audit.WithinUnitBounds ? "inside" : "outside")} unit envelope " +
                        $"actual {audit.RendererMin}..{audit.RendererMax}, " +
                        $"allowed ({-sheet.Unit.Over[3]}, {sheet.Unit.Floor}, {-sheet.Unit.Over[0]}).." +
                        $"({sheet.Unit.CW * ResidentialFacade.Cell + sheet.Unit.Over[1]}, " +
                        $"{sheet.Unit.MaxH}, " +
                        $"{sheet.Unit.CD * ResidentialFacade.Cell + sheet.Unit.Over[2]}).");
                ResidentialUnits.RememberGenerated(sheet.Unit);
                return building;
            }
            catch
            {
                building.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(building);
                else UnityEngine.Object.DestroyImmediate(building);
                throw;
            }
        }

        static ResidentialModule FindForgePrefab(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            var direct = ResidentialModules.Find(prefab);
            if (direct != null) return direct;
            int slash = Mathf.Max(prefab.LastIndexOf('/'), prefab.LastIndexOf('\\'));
            string name = slash >= 0 ? prefab.Substring(slash + 1) : prefab;
            if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 7);
            return ResidentialModules.Find(name);
        }

        static bool LocalRendererBounds(Transform root, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            bool any = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                // Storefront owns inactive renderers for future smashed/boarded/shuttered
                // states. They are not part of the currently stood visible building and can
                // deliberately sit a little proud of its facade; auditing them made a clean
                // intact sheet fail its Unit envelope before the showroom could be saved.
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy) continue;
                Bounds bounds = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = root.InverseTransformPoint(bounds.center +
                                Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                            min = Vector3.Min(min, corner);
                            max = Vector3.Max(max, corner);
                        }
                any = true;
            }
            return any;
        }

        static bool WithinUnitBounds(ResidentialUnit unit, Vector3 min, Vector3 max)
        {
            if (unit == null || unit.Over == null || unit.Over.Length < 4) return false;
            const float tolerance = 0.02f;
            return min.x >= -unit.Over[3] - tolerance &&
                   max.x <= unit.CW * ResidentialFacade.Cell + unit.Over[1] + tolerance &&
                   min.z >= -unit.Over[0] - tolerance &&
                   max.z <= unit.CD * ResidentialFacade.Cell + unit.Over[2] + tolerance &&
                   min.y >= unit.Floor - tolerance && max.y <= unit.MaxH + tolerance;
        }
    }
}
