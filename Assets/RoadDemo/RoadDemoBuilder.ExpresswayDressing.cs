using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RoadDemo
{
    // What stands on and under the expressway. A motorway is not only a road surface:
    // it is a barrier down the middle, a light every fifty metres, a fence along the
    // line nobody may cross, a green board that says where the next way off goes, a
    // billboard turned to face the traffic - and, under the deck, the part of the city
    // that ends up there.
    public partial class RoadDemoBuilder
    {
        const string PalmPropsDir = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string PalmSignsDir = "Assets/Synty/PolygonPalmCity/Prefabs/Signs/";
        const string CityPropsDir = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string GangPropsDir = "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";

        Transform _xwDress;
        Material _xwGreen;

        static GameObject XwLoad(string path) => DemoAssetLoad.Load<GameObject>(path);

        static float XwYaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        GameObject XwPut(GameObject prefab, Vector3 at, float yaw, string name = null)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f), _xwDress);
            go.name = name ?? prefab.name;
            if (!go.activeSelf) go.SetActive(true);
            return go;
        }

        /// <summary>Which way a piece reaches out of its own pivot, how far, and how
        /// high it stands: all three measured off the prefab. A cantilever the pack drew
        /// down its local +X and one it drew down -Z then both end up hanging over the
        /// road, rather than one of them over the ditch beside it.</summary>
        static void XwReach(GameObject prefab, out Vector3 local, out float reach, out float top)
        {
            var b = FreewayKit.Measure(prefab);
            bool alongX = Mathf.Abs(b.center.x) >= Mathf.Abs(b.center.z);
            float off = alongX ? b.center.x : b.center.z;
            local = Mathf.Abs(off) < 0.15f ? Vector3.forward             // a bare post: no arm
                  : alongX ? new Vector3(Mathf.Sign(off), 0f, 0f)
                           : new Vector3(0f, 0f, Mathf.Sign(off));
            reach = alongX ? (off >= 0f ? b.max.x : -b.min.x)
                           : (off >= 0f ? b.max.z : -b.min.z);
            top = b.max.y;
        }

        /// <summary>A point ON a deck's road surface: d metres across its own line, at
        /// the height the profile gives it there plus the bank of the bend.</summary>
        Vector3 OnDeck(XwDeck deck, float ds, float across)
        {
            var at = deck.Line.Pose(ds, across);
            at.y = DeckSurfaceY(deck, ds) + DeckMesh.Camber(deck.Line, ds, across);
            return at;
        }

        /// <summary>The strip between the median barrier's kerb and the inside lane. It
        /// is the same distance off the deck's line from one end of the road to the
        /// other, which the outer edge is not.</summary>
        const float MedianShoulder = -ExpresswayLayout.DeckHalf + DeckMesh.Kerb + 0.35f;

        /// <summary>How far the city's own 6.5 m street light is drawn out to make a
        /// motorway one: ten and a half metres to the lamp, which is the height a 1987
        /// manual puts one at over a road this wide.</summary>
        const float MotorwayLight = 1.6f;

        /// <summary>The headroom a 1987 manual leaves under anything built over a
        /// motorway. A gantry whose ARM stands clear of it may carry its board over the
        /// lanes; one that does not carries it outboard of its own post, over the
        /// parapet, where nothing drives. (The panel itself then hangs BELOW the arm and
        /// so below this - the pack's cantilever tops out at six metres, and six is as
        /// high as the piece goes.)</summary>
        const float Headroom = 4.9f;

        /// <summary>And the one inside the outer parapet, which moves out by four metres
        /// wherever the deck is carrying an exit or an entrance.</summary>
        float OuterShoulder(XwDeck deck, float ds) => DeckWidth(deck, ds).y - DeckMesh.Kerb - 0.55f;

        /// <summary>Everything the road wears.</summary>
        void DressExpressway()
        {
            if (!_xwReady) return;
            _xwDress = ((IDistrictHost)this).StaticRoot("Expressway dressing");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            _xwGreen = new Material(lit) { name = "Guide sign", color = new Color(0.05f, 0.28f, 0.16f) };

            if (expressway.lamps) DressLamps();
            if (expressway.guideSigns) DressGuideSigns();
            if (expressway.billboards) DressBillboards();
            if (expressway.underDeck) DressUnderDeck();
        }

        /// <summary>A light every fifty metres, taken by the two carriageways turn and
        /// turn about: the city's own cobra head - one piece, 6.5 m of mast with its own
        /// arm out to 2.3 - standing ON the deck beside the median barrier, the arm
        /// turned out over the carriageway.
        ///
        /// NOT on the trunk's own line, which is where this used to put them. The two
        /// carriageways are 1.6 m apart there and what lies between them is the open air
        /// under the viaduct: a post on that line is a light hanging in the sky, seven
        /// metres up, holding on to nothing. The median SHOULDER is on the road, is the
        /// same distance off the line the whole way along (the outer edge is not: it
        /// moves four metres out where the deck widens for an exit), and a lamp there
        /// still lights both carriageways.</summary>
        void DressLamps()
        {
            if (_poleBase == null) return;
            var mast = XwLoad("Assets/CityKit/Airport/airport-apron-mast.prefab");
            var trunk = _xw.Trunk;
            int n = 0;
            for (float s = 40f; s < trunk.Length - 40f; s += 50f)
            {
                var deck = _xwDecks[n++ % _xwDecks.Length];
                if (deck == null) continue;
                float ds = DeckSOn(deck, s);
                if (ds < 12f || ds > deck.Line.Length - 12f) continue;
                // the cobra arm is drawn down the piece's own +Z, so the pole is turned
                // to the deck's right and the lamp ends up over the road
                var go = XwPut(_poleBase, OnDeck(deck, ds, MedianShoulder),
                               XwYaw(deck.Line.RightAt(ds)), "Expressway light");
                if (go != null) go.transform.localScale = new Vector3(1f, MotorwayLight, 1f);
            }
            if (mast == null) return;
            foreach (var ex in _xw.Exchanges)
                for (int k = -1; k <= 1; k += 2)
                {
                    var at = _xw.Trunk.Pose(Mathf.Clamp(ex.S, 0f, trunk.Length), k * 46f);
                    at.y = RoadBed;
                    var go = XwPut(mast, at, 0f, "High mast");
                    if (go != null) go.transform.localScale = new Vector3(1f, 2f, 1f);
                }
        }

        // NOTHING STANDS BESIDE THIS ROAD. Twice the plan asked for a line of something
        // down the length of it and twice it came out a wall:
        //
        //  - the NOISE WALL was to go on the parapet where a body of houses stands within
        //    sixty metres. It was laid down the whole elevated length instead, a metre
        //    above the parapet and outside it - a mile of brick hanging in the air beside
        //    a viaduct that runs a hundred and twenty metres clear of the nearest window.
        //  - the RIGHT OF WAY fence was to be wire, at twenty metres either side. The
        //    pack's SM_Env_Fence_01 is chain link on a concrete plinth, and at any
        //    distance in this light the plinth is all that reads: a second wall, in a
        //    field, following a road nobody is fenced out of.
        //
        // Both are gone. A road that one day runs past somebody's window can have a
        // concrete wall on its parapet for the length that it does, and if the motorway
        // is ever to be fenced it is with wire (PolygonNightclubs SM_Prop_Fence_Wire_01,
        // 2.6 m panels, no plinth), not with either of these.

        /// <summary>The green boards: half a mile out, a quarter, and one at the nose of
        /// the ramp itself; and a limit sign after every entrance. Every one of them
        /// stands ON the deck, on the shoulder inside the outer parapet - a gantry put a
        /// metre and a bit OUTSIDE the edge of an elevated road, which is what this used
        /// to do, stands on seven metres of nothing. The legend is not lettered on to the
        /// panel - the board, its post and where it stands are what read at a distance,
        /// and a texture of type on a low-poly sign does not.</summary>
        void DressGuideSigns()
        {
            var arm = XwLoad(PalmPropsDir + "SM_Prop_Sign_Pole_04.prefab");
            var speed = XwLoad(PalmSignsDir + "SM_Sign_Speed_55.prefab");
            var post = XwLoad(PalmPropsDir + "SM_Prop_Sign_Pole_01.prefab");
            var trunk = _xw.Trunk;

            foreach (var deck in _xwDecks)
            {
                if (deck == null) continue;
                foreach (var ex in _xw.Exchanges)
                {
                    float gore = ex.S - deck.Side * ExpresswayLayout.Gore;
                    foreach (float back in new[] { 800f, 400f, 40f })
                    {
                        float s = gore - deck.Side * back;
                        if (s < 40f || s > trunk.Length - 40f) continue;
                        float ds = DeckSOn(deck, s);
                        if (ds < 20f || ds > deck.Line.Length - 20f) continue;
                        string legend = back < 100f
                            ? "EXIT " + ex.Number
                            : "EXIT " + ex.Number + "\n" + ex.Name + "\n" +
                              (back > 600f ? "1/2 MILE" : "1/4 MILE");
                        HangBoard(arm, deck, ds, back < 100f ? 2.6f : 4.2f, legend);
                    }
                    // and the limit, past the entrance
                    float after = ex.S + deck.Side * (ExpresswayLayout.Gore + 90f);
                    if (speed == null || after < 30f || after > trunk.Length - 30f) continue;
                    float das = DeckSOn(deck, after);
                    if (das < 20f || das > deck.Line.Length - 20f) continue;
                    var foot = OnDeck(deck, das, OuterShoulder(deck, das));
                    float facing = XwYaw(-deck.Line.DirAt(das));
                    if (post != null) XwPut(post, foot, facing, "Sign post");
                    XwPut(speed, foot + Vector3.up * 1.4f, facing, "Speed limit");
                }
            }
        }

        /// <summary>One green board and what holds it up: the gantry on the outer
        /// shoulder, its arm turned in over the carriageway, and the panel hung off the
        /// arm - or, where the arm does not stand high enough to hang anything over a
        /// lane, outboard of the post, where the panel overhangs the parapet and nothing
        /// else. How far the arm reaches and how high it stands are measured off the
        /// prefab, so a pack with a shorter cantilever gets a sign it can carry.</summary>
        void HangBoard(GameObject arm, XwDeck deck, float ds, float width, string legend)
        {
            var at = OnDeck(deck, ds, OuterShoulder(deck, ds));
            var over = -deck.Line.RightAt(ds);              // in over the carriageway
            float reach = 0f, top = 5.4f;
            if (arm != null)
            {
                XwReach(arm, out var local, out reach, out top);
                XwPut(arm, at, XwYaw(over) - XwYaw(local), "Sign gantry");
            }
            float boardHigh = width * 0.42f;                // the board's own height
            float boardY = Mathf.Max(top - 0.2f - boardHigh * 0.5f, 2.2f + boardHigh * 0.5f);
            bool overALane = reach >= 2f && top >= Headroom;
            float across = overALane ? Mathf.Min(reach * 0.55f, 4.2f) : -(width * 0.5f - 0.35f);
            GuideBoard(at + over * across + Vector3.up * boardY, deck.Line.DirAt(ds), width, legend);
        }

        /// <summary>One green board on its gantry: a panel and the white edge round it,
        /// which is the whole of what a guide sign is at any distance a driver sees it.</summary>
        void GuideBoard(Vector3 at, Vector3 facing, float width, string legend)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Guide sign";
            panel.transform.SetParent(_xwDress, false);
            panel.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-facing, Vector3.up));
            panel.transform.localScale = new Vector3(width, width * 0.42f, 0.14f);
            Destroy(panel.GetComponent<Collider>());
            panel.GetComponent<MeshRenderer>().sharedMaterial = _xwGreen;
            XwLegend(at - facing * 0.12f, facing, width, legend);
        }

        /// <summary>The white on the green: what the board actually says. It hangs off
        /// the LIVE root and not the static one - a text mesh folded into a merged batch
        /// loses its own material and draws as a grey smear.</summary>
        void XwLegend(Vector3 at, Vector3 facing, float width, string legend)
        {
            if (string.IsNullOrEmpty(legend)) return;
            if (TMPro.TMP_Settings.defaultFontAsset == null) return;
            var go = new GameObject("Sign legend");
            go.transform.SetParent(_traffic, false);
            // FACING, not -facing. A text mesh is read from BEHIND its own forward - it
            // is why every billboarding script in Unity turns text to look the same way
            // the camera does rather than back at it - so a legend turned to face the
            // driver is a legend he reads in a mirror. Which is what EXIT 1 PORT said.
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(facing, Vector3.up));
            var text = go.AddComponent<TMPro.TextMeshPro>();
            text.text = legend;
            text.font = TMPro.TMP_Settings.defaultFontAsset;
            text.color = new Color(0.95f, 0.96f, 0.93f);
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            // Sized to the PANEL, not typed in: a legend of three lines at the size one
            // line was given hangs a line of it off the top of the board and a line off
            // the bottom, which is what EXIT 1 / PORT / 1/2 MILE did. The board's own
            // area is handed to TMP and it fits the words to it.
            text.rectTransform.sizeDelta = new Vector2(width * 0.9f, width * 0.42f * 0.8f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.2f;
            text.fontSizeMax = width * 1.9f;
        }

        /// <summary>Billboards turned to the traffic, outside the fence - the one thing
        /// a 1987 motorway had more of than lights.</summary>
        void DressBillboards()
        {
            var board = XwLoad(CityPropsDir + "SM_Prop_Billboard_01.prefab");
            var pole = XwLoad(CityPropsDir + "SM_Prop_Billboard_Pole_01.prefab");
            if (board == null) return;
            var trunk = _xw.Trunk;
            int n = 0;
            for (float s = 200f; s < trunk.Length - 200f; s += 320f)
            {
                if (!_xw.Elevated(s)) continue;
                int side = (n++ % 2 == 0) ? -1 : 1;
                var at = trunk.Pose(s, side * 34f);
                at.y = RoadBed;
                if (InAnyRoad(at)) continue;
                var facing = -trunk.DirAt(s) * side;         // toward the traffic that sees it
                if (pole != null) XwPut(pole, at, XwYaw(facing), "Billboard pole");
                var go = XwPut(board, at + Vector3.up * 6.4f, XwYaw(facing), "Billboard");
                if (go != null) go.transform.localScale = Vector3.one * 1.1f;
            }
        }

        // --------------------------------------------------------- under the deck

        static readonly string[] UnderJunk =
        {
            GangPropsDir + "SM_Prop_Barrel_Metal_01.prefab",
            GangPropsDir + "SM_Prop_Barrel_PlasticLid_01.prefab",
            GangPropsDir + "SM_Prop_Dumpster_01.prefab",
            CityPropsDir + "SM_Prop_Pallet_01.prefab",
            CityPropsDir + "SM_Prop_CardboardBox_01.prefab",
            CityPropsDir + "SM_Prop_CardboardBox_02.prefab",
            CityPropsDir + "SM_Prop_TrashBag_01.prefab",
            CityPropsDir + "SM_Prop_TrashBag_02.prefab",
            CityPropsDir + "SM_Prop_Trashbin_01.prefab",
            CityPropsDir + "SM_Prop_Trashbin_02.prefab",
        };

        /// <summary>The ground between the piers. Nothing was ever built there and
        /// nothing ever will be: what ends up under a city motorway is what the city
        /// has finished with, and the people it has finished with too.</summary>
        void DressUnderDeck()
        {
            var junk = new List<GameObject>();
            foreach (var p in UnderJunk) { var g = XwLoad(p); if (g != null) junk.Add(g); }

            var rng = new System.Random(spacingSeed * 31 + 7);
            var trunk = _xw.Trunk;
            var camps = new List<Vector3>();
            for (float s = 30f; s < trunk.Length - 30f; s += 22f)
            {
                if (!_xw.Elevated(s)) continue;
                // thicker where the ramps come down and people pass
                bool near = false;
                foreach (var ex in _xw.Exchanges)
                    if (Mathf.Abs(ex.S - s) < ExpresswayLayout.AuxOut) { near = true; break; }
                int want = near ? 3 : 1;
                for (int k = 0; k < want; k++)
                {
                    float off = (float)(rng.NextDouble() * 2.0 - 1.0) * 15f;
                    var at = trunk.Pose(s + (float)rng.NextDouble() * 16f, off);
                    at.y = RoadBed;
                    if (InAnyRoad(at) || !XwPierFree(at)) continue;
                    if (junk.Count > 0 && rng.NextDouble() < 0.75)
                        XwPut(junk[rng.Next(junk.Count)], at, (float)rng.NextDouble() * 360f, "Under the deck");
                    if (rng.NextDouble() < 0.10) camps.Add(at);
                }
            }

            int people = Mathf.Min(expressway.vagrants, camps.Count);
            if (people <= 0 || _pedPrefabs.Count == 0 || _idleClip == null) return;
            var sit = _sitLoopClip != null ? _sitLoopClip : _idleClip;
            for (int i = 0; i < people; i++)
            {
                var at = camps[i * camps.Count / Mathf.Max(1, people)];
                var prefab = _pedPrefabs[rng.Next(_pedPrefabs.Count)];
                bool seated = rng.NextDouble() < 0.5;
                RoadsideFigure.Stand(prefab, at, (float)rng.NextDouble() * 360f,
                                     seated ? sit : _idleClip, _xwDress);
            }
        }
    }

    /// <summary>A body that stands (or sits) where it is put and does nothing else: no
    /// walk graph, no errand, no part in anything. It holds one animation clip, which
    /// is what keeps it off the T-pose the pack's characters ship in.</summary>
    public sealed class RoadsideFigure : MonoBehaviour
    {
        PlayableGraph _graph;

        public static RoadsideFigure Stand(GameObject prefab, Vector3 at, float yaw,
                                           AnimationClip loop, Transform parent)
        {
            if (prefab == null || loop == null) return null;
            var go = Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f), parent);
            go.name = "Under the deck";
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null) { Destroy(go); return null; }
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;

            var fig = go.AddComponent<RoadsideFigure>();
            fig._graph = PlayableGraph.Create("under the deck");
            fig._graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var clip = AnimationClipPlayable.Create(fig._graph, loop);
            clip.SetApplyFootIK(false);
            clip.SetTime(Random.value * Mathf.Max(0.1f, loop.length));
            var output = AnimationPlayableOutput.Create(fig._graph, "pose", animator);
            output.SetSourcePlayable(clip);
            fig._graph.Play();
            return fig;
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
