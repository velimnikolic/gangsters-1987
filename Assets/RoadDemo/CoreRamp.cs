using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The ramp a block's way out needs when the yard at its foot lies below the pavement.
    ///
    /// The police station is what asked for it. Its basement garage opens into a yard three
    /// metres down, and the pack drew the yard and the arrows painted on it and then stopped
    /// - between that yard and the street there is nothing at all, because a building is
    /// drawn to stand anywhere and the ground it stands in is the city's business. Laid with
    /// flat tiles the way out ended at a three metre cliff over the very arrows it was
    /// pointing at.
    ///
    /// So it is drawn, and there is nothing to draw it with: the pack has road tiles and
    /// sidewalk tiles and no piece anywhere in it that slopes. Six triangles, then - a deck
    /// and the two walls of the trench it is cut into - wearing the pack's own colours,
    /// which are read off the pack's own tiles rather than typed in
    /// (<see cref="DeckMesh.Flat"/>): every surface in these packs is a flat swatch of one
    /// atlas, so a whole face wants one point of it and no more.
    ///
    /// Built in its own frame, +Z the way it climbs and the middle of it at nought, so the
    /// caller has only to stand it and turn it (<see cref="CorePavement.Slope.Facing"/>).
    /// </summary>
    public static class CoreRamp
    {
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";

        /// <summary>What the two surfaces are painted like: the running surface off the
        /// pack's plain road square, the trench walls off its plain paving square. Both are
        /// the tiles the pavement round the ramp is made of, so the ramp is the same road
        /// and the same concrete as the block it is cut into.</summary>
        const string RoadTile = "SM_Env_Road_Bare_01";
        const string PavingTile = "SM_Env_Sidewalk_01";

        /// <summary>The deck and the walls come off two different atlases - PolygonCity
        /// paints its roads and its pavements with two materials - so they are two meshes
        /// and two renderers, not one mesh with a seam down it.</summary>
        public readonly struct Drawn
        {
            public readonly Mesh Mesh;
            public readonly Material Wearing;
            public Drawn(Mesh mesh, Material wearing) { Mesh = mesh; Wearing = wearing; }
            public bool Any => Mesh != null && Wearing != null;
        }

        /// <summary>
        /// The two pieces of one ramp: the deck it is driven on and the walls of the cut it
        /// runs in. Both in the slope's own frame - +Z uphill, the middle of the run at the
        /// origin, the pavement at y = 0 - so both go under one turned transform.
        ///
        /// The walls are drawn on BOTH sides whatever stands there. A yard cut into a
        /// building has the building on both sides of its way out, and a wall inside a wall
        /// is not seen; a yard with open ground beside it needs them, and telling the two
        /// apart would be guessing at ground this class cannot see.
        /// </summary>
        public static void Build(CorePavement.Slope slope, out Drawn deck, out Drawn walls)
        {
            deck = default;
            walls = default;

            float wide = slope.Wide * 0.5f, along = slope.Long * 0.5f, foot = slope.Foot;
            if (wide <= 0f || along <= 0f || foot >= 0f) return;

            // the four corners of the running surface: the low pair at the yard's floor, the
            // high pair where the run meets the crossing and the pavement's own level
            var lowWest = new Vector3(-wide, foot, -along);
            var lowEast = new Vector3(wide, foot, -along);
            var highEast = new Vector3(wide, 0f, along);
            var highWest = new Vector3(-wide, 0f, along);

            deck = Face(RoadTile, "core ramp deck",
                        new[] { lowWest, highWest, highEast, lowWest, highEast, lowEast });

            // each wall is the triangle between the deck's edge and the pavement it is cut
            // into: level along the top, climbing along the bottom, and shut at the yard end
            var lipWest = new Vector3(-wide, 0f, -along);
            var lipEast = new Vector3(wide, 0f, -along);
            walls = Face(PavingTile, "core ramp walls",
                         new[] { lowWest, lipWest, highWest, lowEast, highEast, lipEast });
        }

        /// <summary>One flat-coloured surface: the triangles as given, every vertex at the
        /// one point of the atlas the named tile's own face uses.</summary>
        static Drawn Face(string tile, string name, Vector3[] corners)
        {
            var skin = DeckMesh.Flat(DemoAssetLoad.Load<GameObject>(CityEnv + tile + ".prefab"));
            if (skin.Mat == null) return default;

            var uv = new Vector2[corners.Length];
            for (int i = 0; i < uv.Length; i++) uv[i] = skin.Concrete;

            var tris = new int[corners.Length];
            for (int i = 0; i < tris.Length; i++) tris[i] = i;

            var mesh = new Mesh { name = name };
            mesh.SetVertices(corners);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return new Drawn(mesh, skin.Mat);
        }
    }
}
