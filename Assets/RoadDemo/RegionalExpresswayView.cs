using UnityEngine;

namespace RoadDemo
{
    /// <summary>The same swept deck, painted lanes and measured piers as ExpresswayDemo.</summary>
    public static class RegionalExpresswayView
    {
        public static void Build(RegionalExpresswayPlan plan, IDistrictHost host,
            GameObject asphalt, System.Func<float, float, float> ground)
        {
            var root = host.StaticRoot("Regional Expressway");
            var owned = root.gameObject.AddComponent<LandscapeResources>();
            var skin = DeckMesh.Probe(FreewayKit.TryLoad(FreewayKit.DeckPath)).Surfaced(DeckMesh.Flat(asphalt));
            var white = owned.Material(new Material(Shader.Find("Universal Render Pipeline/Unlit"))
                { name = "Expressway white paint", color = new Color(0.88f, 0.87f, 0.8f) });
            var yellow = owned.Material(new Material(white) { name = "Expressway median paint", color = new Color(0.93f, 0.69f, 0.18f) });
            var pillar = FreewayKit.TryLoad(FreewayKit.PillarPath);
            var steel = owned.Material(new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { name = "River bridge weathered green steel", color = new Color(0.19f, 0.28f, 0.24f) });
            var lamp = DemoAssetLoad.Load<GameObject>("Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Street_Lamp_01.prefab");
            foreach (var deck in plan.Decks)
            {
                RegionalBridgeView.Build(deck, root, owned, steel, ground);
                float Height(float s) => deck.Height(s);
                for (float a = 0; a < deck.Line.Length; a += 192f)
                {
                    float b = Mathf.Min(a + 192f, deck.Line.Length);
                    owned.Mesh(DeckMesh.Build(deck.Line, a, b, Height,
                        s => new Vector2(-ExpresswayLayout.DeckHalf, deck.Half(s)),
                        s => new Vector2(DeckMesh.Parapet, deck.Wall(s)), skin, root, "Curved motorway deck", 8f));
                    owned.Mesh(DeckMesh.Paint(deck.Line, a, b, Height, s => 0f, 0.15f, true, white, root, "Lane divider"));
                    owned.Mesh(DeckMesh.Paint(deck.Line, a, b, Height, s => -5f, 0.16f, false, yellow, root, "Median edge"));
                    owned.Mesh(DeckMesh.Paint(deck.Line, a, b, Height, s => deck.Half(s) - 0.65f,
                        0.16f, false, white, root, "Shoulder edge"));
                }
                for (float s = 24; s < deck.Line.Length; s += 48f)
                {
                    var at = deck.Line.PointAt(s);
                    float floor = ground(at.x, at.z);
                    if (pillar == null || floor < RoadDemoBuilder.WaterY || !PierFree(plan, at)) continue;
                    at.y = deck.Height(s) - 1.65f;
                    FreewayKit.StandPillar(pillar, at, Yaw(deck.Line.DirAt(s)), root, floor);
                }
                if (lamp != null)
                    for (float s = 30; s < deck.Line.Length; s += 64f)
                    {
                        var at = deck.Line.Pose(s, -4.75f);
                        at.y = deck.Height(s) + DeckMesh.Camber(deck.Line, s, -4.75f);
                        var go = Object.Instantiate(lamp, at, Quaternion.Euler(0, Yaw(deck.Line.RightAt(s)), 0), root);
                        go.name = "Motorway light";
                        go.transform.localScale *= 1.6f;
                    }
            }
            foreach (var ramp in plan.Ramps)
            {
                owned.Mesh(DeckMesh.Build(ramp.Line, 0, ramp.Line.Length, ramp.Height, ramp.Width,
                    s => new Vector2(ramp.Width(s).x > -3.6f ? 0f : DeckMesh.Parapet, DeckMesh.Parapet),
                    skin, root, "Curved interchange ramp", 4f));
                owned.Mesh(DeckMesh.Paint(ramp.Line, 0, ramp.Line.Length, ramp.Height,
                    s => 2.85f, 0.14f, false, white, root, "Ramp shoulder"));
                for (float s = 25; s < ramp.Line.Length - 20f; s += 42f)
                {
                    var at = ramp.Line.PointAt(s); at.y = ramp.Height(s) - 1.65f;
                    float floor = ground(at.x, at.z);
                    if (pillar == null || at.y - floor < 2f || floor < RoadDemoBuilder.WaterY ||
                        !PierFree(plan, at, ramp)) continue;
                    FreewayKit.StandPillar(pillar, at, Yaw(ramp.Line.DirAt(s)), root, floor);
                }
            }
        }

        /// <summary>Never plant bridge structure in an at-grade road or another ramp.</summary>
        public static bool PierFree(RegionalExpresswayPlan plan, Vector3 at,
            RegionalExpresswayPlan.Ramp own = null)
        {
            var point = new Vector2(at.x, at.z);
            foreach (var road in plan.Ground)
            {
                road.Line.Project(at, out float s, out _);
                var p = road.Line.PointAt(s);
                if (Vector2.Distance(point, new Vector2(p.x, p.z)) < StreetKit.OuterHalf + 3f) return false;
            }
            foreach (var ramp in plan.Ramps)
            {
                if (ramp == own) continue;
                ramp.Line.Project(at, out float s, out float d);
                if (s > 3f && s < ramp.Line.Length - 3f &&
                    Mathf.Abs(d) < ExpresswayLayout.RampHalf + 3f) return false;
            }
            return true;
        }

        static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    }
}
