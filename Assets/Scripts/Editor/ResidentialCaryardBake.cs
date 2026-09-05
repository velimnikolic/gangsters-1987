using System;
using System.IO;
using System.Linq;
using RoadDemo;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
namespace LivingCity.EditorTools
{
    /// <summary>Complete sales forecourt, authored once for every residential city path.</summary>
    public static class ResidentialCaryardBake
    {
        const string Path = "Assets/Prefabs/Residential/caryard.prefab";
        const string Folder = "Assets/Prefabs/Residential/Caryard";
        const string Palm = "Assets/Synty/PolygonPalmCity/Prefabs/";
        const float Deck = ResidentialCaryard.Deck;
        static Transform geometry;
        static Material asphalt, repair, oil, concrete, steel, cream, teal, paint, glass;

        [MenuItem("Tools/City/Residential/Bake Expanded Caryard")]
        public static bool Bake()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                while (root.transform.childCount > 0) Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
                asphalt = Material("Asphalt", new Color(.245f,.25f,.24f), "STONE");
                repair = Material("Repairs", new Color(.205f,.215f,.21f), "STONE");
                oil = Material("Oil", new Color(.13f,.135f,.13f), "STONE");
                concrete = Material("Concrete", new Color(.49f,.475f,.435f), "STONE");
                steel = Material("Steel", new Color(.16f,.19f,.19f), "PAINT", .45f);
                cream = Material("Office", new Color(.66f,.615f,.50f), "STONE");
                teal = Material("Fascia", new Color(.095f,.255f,.26f), "PAINT");
                paint = Material("Markings", new Color(.79f,.73f,.53f), "PAINT");
                glass = Material("Windows", new Color(.19f,.28f,.30f), "PAINT", .35f);
                geometry = Group(root.transform, "Forecourt structure");
                Ground(); Boundary(root.transform); Vehicles(root.transform); Office(root.transform); Details(root.transform);
                CombineStructure();
                var unit = ResidentialCaryard.Describe();
                var tag = root.GetComponent<LivingCity.Generation.BlockLotTag>();
                if (tag == null) tag = root.AddComponent<LivingCity.Generation.BlockLotTag>();
                tag.lotWidth = 40f; tag.lotDepth = 25f;
                ResidentialHarvest.PreparePhysics(root, 40f, 25f, 0f, unit.MaxH);
                var proxy = ResidentialTurfPrefab.BakeInto(root, 40f, 25f, unit.MaxH);
                var b = Bounds(root);
                if (b.min.x < -.01f || b.min.z < -.01f || b.max.x > 40.01f || b.max.z > 25.01f || b.min.y < -.01f || b.max.y > unit.MaxH+.01f)
                    throw new InvalidOperationException("Caryard exceeds its envelope: " + b);
                PrefabUtility.SaveAsPrefabAsset(root, Path);
                var catalog = AssetDatabase.LoadAssetAtPath<ResidentialTurfCatalog>(ResidentialTurfCatalog.AssetPath);
                if (catalog != null) { catalog.ReplaceEntry("caryard", proxy.CopyMasses()); AssetDatabase.SaveAssetIfDirty(catalog); }
                AssetDatabase.SaveAssets(); return true;
            }
            finally { geometry = null; PrefabUtility.UnloadPrefabContents(root); }
        }

        static void Ground()
        {
            Box("Continuous asphalt", new Vector3(20f,Deck*.5f,12.5f), new Vector3(40f,Deck,25f), asphalt);
            Box("Office apron", new Vector3(32.5f,Deck+.01f,19.4f), new Vector3(13f,.02f,10.4f), concrete);
            Box("Preparation pad", new Vector3(21f,Deck+.01f,20.9f), new Vector3(8f,.02f,7f), concrete);
            // Cross aisle is seven metres wide; the south gate aligns with a ten-metre drive.
            foreach(float x in new[]{2f,6f,10f,14f,26f,30f,34f,38f})
                Box("Front bay line",new Vector3(x,Deck+.002f,5f),new Vector3(.07f,.004f,6.3f),paint);
            foreach(float x in new[]{2f,6f,10f,14f})
                Box("Rear bay line",new Vector3(x,Deck+.002f,20f),new Vector3(.07f,.004f,6.3f),paint);
            foreach(float z in new[]{8.2f,16.8f})
                Box("Display row edge",new Vector3(8f,Deck+.002f,z),new Vector3(12f,.004f,.07f),paint);
            Box("Display row edge",new Vector3(32f,Deck+.002f,8.2f),new Vector3(12f,.004f,.07f),paint);
            for(int i=0;i<4;i++) Box("Entrance centre dash",new Vector3(20f,Deck+.002f,1f+i*2.4f),new Vector3(.08f,.004f,1.1f),paint);
            for(int i=0;i<4;i++) Box("Pedestrian office approach",new Vector3(25.8f+i*.55f,Deck+.022f,15.1f),new Vector3(.25f,.004f,1.4f),paint);
            var rng=new System.Random(871984);
            for(int i=0;i<44;i++)
            {
                float x=.8f+(float)rng.NextDouble()*38.4f, z=.8f+(float)rng.NextDouble()*23.4f;
                if(x>16f&&z>14f)continue;
                Patch(x,z,.35f+(float)rng.NextDouble()*1.15f,i%3==0?oil:repair,rng);
            }
            foreach(float x in new[]{15.2f,24.8f})
            {
                Box("Drain trench",new Vector3(x,Deck+.003f,12.1f),new Vector3(.2f,.006f,5.7f),steel);
                for(int i=0;i<23;i++)Box("Drain grate",new Vector3(x,Deck+.008f,9.35f+i*.25f),new Vector3(.22f,.012f,.035f),concrete);
            }
        }

        static void Vehicles(Transform root)
        {
            var cars=Group(root,"Cars for sale");
            string[] names={"Sedan_01","Pickup_01","Sedan_01","Supercar_01","Sedan_01","Suv_01","Sedan_01","Van_01","Sedan_01"};
            Vector2[] positions={new Vector2(4,5),new Vector2(8,5),new Vector2(12,5),new Vector2(28,5),new Vector2(32,5),new Vector2(36,5),new Vector2(4,20),new Vector2(8,20),new Vector2(12,20)};
            for(int i=0;i<positions.Length;i++)
            {
                var at=positions[i];var car=Stand("Vehicles/SM_Veh_"+names[i],cars,at.x,at.y,i<6?180f:0f,Deck);
                // Fit the vehicle uniformly to a real bay, including any mirrors and bumpers.
                var b=Bounds(car);car.transform.localScale*=Mathf.Min(1f,Mathf.Min(2.65f/b.size.x,5.7f/b.size.z));Center(car,at.x,at.y,Deck);
                // Synty atlas alternatives change the paint while retaining tyres and trim.
                string suffix = new[] { "A", "B", "C" }[i % 3];
                var finish = AssetDatabase.LoadAssetAtPath<Material>("Assets/Synty/PolygonPalmCity/Materials/Alts/PolygonPalmCity_03_" + suffix + ".mat");
                if (finish != null) foreach (var renderer in car.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m != null && m.name.StartsWith("PolygonPalmCity_03") ? finish : m).ToArray();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(car.transform);
                Box("Wheel stop",new Vector3(at.x,Deck+.065f,i<6?2.1f:22.9f),new Vector3(1.9f,.13f,.16f),concrete);
                // Small supported price boards sit beside the bonnet, outside each vehicle envelope.
                float boardX=at.x+1.58f,boardZ=i<6?6.9f:18.1f;
                Box("Price stand",new Vector3(boardX,Deck+.44f,boardZ),new Vector3(.035f,.88f,.035f),steel);
                Box("Price stand foot",new Vector3(boardX,Deck+.02f,boardZ),new Vector3(.32f,.04f,.30f),steel);
                Box("Price card",new Vector3(boardX,Deck+.92f,boardZ),new Vector3(.6f,.42f,.04f),cream);
                Label(root,"$"+(2950+i*650),new Vector3(boardX,Deck+.94f,boardZ-.025f),.48f,.18f);
            }
            var prep=Group(root,"Vehicle preparation");
            var service=Stand("Vehicles/SM_Veh_Sedan_01",prep,21f,20.6f,0f,Deck+.02f);
            var sb=Bounds(service);service.transform.localScale*=Mathf.Min(1f,Mathf.Min(2.7f/sb.size.x,5.6f/sb.size.z));Center(service,21,20.6f,Deck+.02f);PrefabUtility.RecordPrefabInstancePropertyModifications(service.transform);
            foreach(float x in new[]{17.3f,24.7f})foreach(float z in new[]{17.7f,24f})
                Box("Preparation canopy post",new Vector3(x,1.65f,z),new Vector3(.12f,3.3f,.12f),steel);
            Box("Preparation canopy",new Vector3(21,3.4f,20.9f),new Vector3(7.8f,.16f,6.8f),teal);
            for(int i=0;i<17;i++)Box("Canopy seam",new Vector3(17.2f+i*.47f,3.49f,20.9f),new Vector3(.035f,.035f,6.8f),steel);
            Box("Tool cabinet",new Vector3(24f,.48f,21f),new Vector3(.8f,.84f,1.4f),teal);
            for(int i=0;i<4;i++)Box("Drawer handle",new Vector3(23.57f,.25f+i*.18f,21f),new Vector3(.035f,.03f,.95f),steel);
            Stand("Props/SM_Prop_Cone_01",prep,25.4f,18f,0f,Deck);
            Stand("Props/SM_Prop_Trash_Bin_01",prep,24.7f,23.4f,0f,Deck+.02f);
        }

        static void Office(Transform root)
        {
            // Low office, window bays and a recessed door facing the forecourt.
            Box("Office back",new Vector3(33,1.65f,23.5f),new Vector3(10f,3.2f,.18f),cream);
            foreach(float x in new[]{28f,38f})Box("Office side",new Vector3(x,1.65f,20.5f),new Vector3(.18f,3.2f,6f),cream);
            Box("Window sill wall",new Vector3(33,.47f,17.5f),new Vector3(10f,.82f,.18f),cream);
            Box("Window header",new Vector3(33,2.98f,17.5f),new Vector3(10f,.5f,.18f),cream);
            foreach(float x in new[]{28f,30.6f,33.2f,35.8f,38f})Box("Window mullion",new Vector3(x,1.8f,17.5f),new Vector3(.12f,2.1f,.22f),teal);
            foreach(float x in new[]{29.3f,31.9f,34.5f,36.9f})
            {
                Box("Recessed glazing",new Vector3(x,1.8f,17.56f),new Vector3(x>36?2.02f:2.42f,1.82f,.025f),glass);
                Box("Window transom",new Vector3(x,2.24f,17.44f),new Vector3(x>36?2.08f:2.48f,.045f,.06f),steel);
            }
            Box("Office door",new Vector3(33.2f,1.28f,17.36f),new Vector3(1.22f,2.46f,.08f),teal);
            Box("Door glass",new Vector3(33.2f,1.65f,17.31f),new Vector3(.98f,1.38f,.025f),glass);
            Box("Door handle",new Vector3(33.66f,1.09f,17.25f),new Vector3(.035f,.26f,.05f),paint);
            Box("Office roof",new Vector3(33,3.38f,20.5f),new Vector3(10.5f,.22f,6.5f),steel);
            Box("Office fascia",new Vector3(33,3.45f,17.22f),new Vector3(10.5f,.65f,.12f),teal);
            Label(root,"PALM MOTORS",new Vector3(33,3.47f,17.15f),7.6f,.40f);
            Label(root,"SALES OFFICE",new Vector3(33.2f,2.64f,17.29f),1.1f,.12f);
            // A supported awning and waiting bench keep the office threshold clear.
            Box("Porch awning",new Vector3(33,2.88f,16.65f),new Vector3(10.4f,.12f,1.3f),teal);
            foreach(float x in new[]{28.1f,37.9f})Box("Awning post",new Vector3(x,1.46f,16.1f),new Vector3(.09f,2.84f,.09f),steel);
            Stand("Props/SM_Prop_Bench_Seat_01",root,29.6f,16.6f,0f,Deck+.02f);
            Stand("Props/SM_Prop_Trash_Bin_01",root,37f,16.6f,0f,Deck+.02f);
            StandAbsolute("Assets/Synty/PolygonGeneric/Prefabs/Props/SM_Gen_Prop_Aircon_Roof_01.prefab",root,35.5f,21.5f,0f,3.49f);
        }

        static void Boundary(Transform root)
        {
            // Open ten-metre southern gate; no fence or props cross the approach.
            var fence=Group(root,"Sales perimeter");
            for(int side=0;side<4;side++)
            {
                int count=side%2==0?16:10;
                for(int i=0;i<count;i++)
                {
                    if(side==0&&i>=6&&i<10)continue;
                    float x=side==1?39.75f:side==3?.25f:1.25f+i*2.5f;
                    float z=side==0?.25f:side==2?24.75f:1.25f+i*2.5f;
                    var panel=Stand("Buildings/SM_Bld_Fence_01",fence,x,z,side%2==0?0:90,Deck);
                    var b=Bounds(panel);panel.transform.localScale=Vector3.Scale(panel.transform.localScale,
                        new Vector3(side%2==0?2.4f/b.size.x:1f,1.15f/b.size.y,side%2==1?2.4f/b.size.z:1f));
                    Center(panel,x,z,Deck);PrefabUtility.RecordPrefabInstancePropertyModifications(panel.transform);
                    Box("Fence footing",new Vector3(x,.10f,z),side%2==0?new Vector3(2.5f,.2f,.24f):new Vector3(.24f,.2f,2.5f),concrete);
                }
            }
            foreach(float x in new[]{15.1f,24.9f})Stand("Props/SM_Prop_Bollard_03",root,x,.6f,0f,Deck);
            foreach(float x in new[]{2f,12f})Box("Dealership sign post",new Vector3(x,2.5f,1.1f),new Vector3(.15f,5f,.15f),steel);
            Box("Dealership sign",new Vector3(7f,4.55f,1.1f),new Vector3(11f,1.35f,.16f),teal);
            Label(root,"PALM MOTORS",new Vector3(7f,4.75f,1f),9.3f,.56f);
            Label(root,"USED CARS  /  BUY - SELL - TRADE",new Vector3(7f,4.22f,1f),9.3f,.22f);
            // Bunting spans only display rows, with visible supports at both ends.
            foreach(float z in new[]{8.8f,23.5f})
            {
                foreach(float x in new[]{1.5f,14.5f})Box("Pennant pole",new Vector3(x,2f,z),new Vector3(.045f,4f,.045f),steel);
                for(int i=0;i<5;i++)Stand("Props/SM_Prop_Bunting_01",root,2.75f+i*2.6f,z,0f,3.72f);
                Box("Pennant cable",new Vector3(8f,3.98f,z),new Vector3(13f,.013f,.013f),steel);
            }
        }

        static void Details(Transform root)
        {
            foreach(var p in new[]{new Vector2(.95f,12.7f),new Vector2(39.05f,10.4f)})
                StandAbsolute(ResidentialLandscaping.Folder+"StreetBorder.prefab",root,p.x,p.y,90f,Deck);
            foreach(var p in new[]{new Vector2(1.3f,23.5f),new Vector2(38.8f,24.1f)})
                Stand("Props/SM_Prop_Street_Lamp_01",root,p.x,p.y,0f,Deck);
            // Service-side scuffs and tyre arcs are flush with the surface.
            for(int i=0;i<2;i++)
                StandAbsolute("Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Tyremarks_Short_Corner_01.prefab",root,18f+i*4f,12.6f,90f*i,Deck+.006f);
        }

        static void Patch(float x,float z,float radius,Material mat,System.Random rng)
        {
            var v=new Vector3[11];var indices=new int[30];v[0]=new Vector3(x,Deck+.001f,z);
            for(int i=0;i<10;i++){float a=i*Mathf.PI*.2f;float r=radius*(.6f+(float)rng.NextDouble()*.4f);v[i+1]=v[0]+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r*.55f);indices[i*3]=0;indices[i*3+1]=(i+1)%10+1;indices[i*3+2]=i+1;}
            var mesh=new Mesh { name="Asphalt wear",vertices=v,triangles=indices };mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject("Asphalt wear");go.transform.SetParent(geometry,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;
        }
        static GameObject Stand(string name,Transform parent,float x,float z,float yaw,float y) => StandAbsolute(Palm+name+".prefab",parent,x,z,yaw,y);
        static GameObject StandAbsolute(string path,Transform parent,float x,float z,float yaw,float y)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new FileNotFoundException(path);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.transform.localRotation=Quaternion.Euler(0,yaw,0);Center(go,x,z,y);PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);return go;
        }
        static void Center(GameObject go,float x,float z,float y){var b=Bounds(go);go.transform.position+=new Vector3(x-b.center.x,y-b.min.y,z-b.center.z);}
        static Bounds Bounds(GameObject go){var rr=go.GetComponentsInChildren<Renderer>();if(rr.Length==0)return new Bounds(go.transform.position,Vector3.zero);var b=rr[0].bounds;foreach(var r in rr)b.Encapsulate(r.bounds);return b;}
        static Transform Group(Transform p,string name){var go=new GameObject(name);go.transform.SetParent(p,false);return go.transform;}
        static void Box(string name,Vector3 at,Vector3 size,Material mat){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(geometry,false);go.transform.localPosition=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(go.GetComponent<Collider>());}
        static Material Material(string name,Color tint,string profile,float metallic=0f)
        {
            string path="Assets/Materials/Caryard_"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(Shader.Find("LivingCity/Street Prop Surface"));AssetDatabase.CreateAsset(mat,path);}
            mat.SetColor("_BaseColor",tint);mat.SetTexture("_BaseMap",null);mat.SetFloat("_Metallic",metallic);mat.SetFloat("_Smoothness",.18f);mat.SetFloat("_Prop",profile=="STONE"?2f:0f);mat.shaderKeywords=new[]{"_PROP_"+profile};mat.enableInstancing=true;EditorUtility.SetDirty(mat);return mat;
        }
        static void Label(Transform parent,string value,Vector3 at,float width,float height)
        {
            var go=new GameObject(value);go.transform.SetParent(parent,false);go.transform.localPosition=at;
            var t=go.AddComponent<TextMesh>();t.text=value;t.fontSize=64;t.characterSize=.3f;t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.color=new Color(.96f,.91f,.72f);go.GetComponent<Renderer>().sharedMaterial=t.font.material;
            var b=go.GetComponent<Renderer>().bounds;go.transform.localScale*=Mathf.Min(width/Mathf.Max(.001f,b.size.x),height/Mathf.Max(.001f,b.size.y));
        }
        static void CombineStructure()
        {
            var filters=geometry.GetComponentsInChildren<MeshFilter>();
            foreach(var group in filters.GroupBy(f=>f.GetComponent<MeshRenderer>().sharedMaterial))
            {
                string path=Folder+"/"+group.Key.name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}mesh.Clear();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=geometry.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);mesh.name=group.Key.name;EditorUtility.SetDirty(mesh);
                var go=new GameObject(group.Key.name);go.transform.SetParent(geometry.parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
            var transient=filters.Select(f=>f.sharedMesh).Where(m=>string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m))).Distinct().ToArray();Object.DestroyImmediate(geometry.gameObject);foreach(var mesh in transient)Object.DestroyImmediate(mesh);
        }
    }
}
