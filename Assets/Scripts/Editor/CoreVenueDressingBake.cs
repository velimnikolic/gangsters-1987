using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LivingCity.Entities;
using RoadDemo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace LivingCity.EditorTools
{
    /// <summary>Repeatable dressing of the two shared, authored Core blocks. Keeps their
    /// building identity, placement and footprint; generated additions live in one group.</summary>
    public static class CoreVenueDressingBake
    {
        const string Folder = "Assets/Prefabs/CoreBlocks/VenueDressing";
        const string GroupName = "Venue character";
        const string Palm = "Assets/Synty/PolygonPalmCity/Prefabs/";
        const string Club = "Assets/Synty/PolygonNightclubs/Prefabs/Props/";
        const string PoliceProps = "Assets/Synty/PolygonPoliceStation/Prefabs/Props/";
        const float Floor = .06f;
        static Transform structure;
        static Material lettering;
        static Material navy, gold, white, dark, pink, cyan, asphalt, concrete, plum;

        [MenuItem("Tools/City/Core/Bake Police and Nightclub Character")]
        public static string Bake()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Leave Play before baking venues.");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            navy = Mat("Police navy", new Color(.065f,.15f,.24f));
            gold = Mat("Brass", new Color(.68f,.49f,.20f));
            white = Mat("Ivory", new Color(.84f,.85f,.79f));
            dark = Mat("Graphite", new Color(.065f,.07f,.085f));
            pink = Mat("Neon pink", new Color(.95f,.12f,.38f), true);
            cyan = Mat("Neon ice", new Color(.12f,.75f,.84f), true);
            asphalt = Mat("Parking asphalt", new Color(.19f,.20f,.21f));
            concrete = Mat("Entrance stone", new Color(.43f,.43f,.42f));
            plum = Mat("Club plum", new Color(.16f,.065f,.13f));
            var font=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            lettering=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Sign lettering.mat");
            if(lettering==null){lettering=new Material(font.material);AssetDatabase.CreateAsset(lettering,Folder+"/Sign lettering.mat");}
            lettering.shader=font.material.shader; lettering.CopyPropertiesFromMaterial(font.material);
            lettering.SetFloat("_ZTestMode",4); lettering.SetFloat("_CullMode",2); EditorUtility.SetDirty(lettering);
            foreach (string venue in new[] { "police-station-block", "nightclub-block" })
            {
                string path = "Assets/Prefabs/CoreBlocks/" + venue + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var previous = root.transform.Find(GroupName);
                    if (previous != null) Object.DestroyImmediate(previous.gameObject);
                    var dressing = Group(root.transform, GroupName);
                    structure = Group(dressing, "Architecture and markings");
                    if (venue == "nightclub-block") Nightclub(root, dressing);
                    else Police(root, dressing);
                    Merge(venue);
                    foreach (var t in dressing.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { structure = null; PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            return "Shared police station and nightclub dressed; nightclub parking has " + ParkingBlockPlan.Generate(40,15).Stalls.Count + " bays.";
        }

        static void Nightclub(GameObject root, Transform dressing)
        {
            // The source has its real double entrance at (-5.17, 10.67), under the
            // barrel awning. Its old painted forecourt extends the mesh bounds to 22.67.
            // Keep all new street furniture out of the measured entrance and side exits.
            var building = root.transform.Find("building-nightclub");
            ClearOldForecourt(building);
            var entrance = building.Find("Measured nightclub entrance");
            if (entrance == null) entrance = Group(building, "Measured nightclub entrance");
            entrance.localPosition = new Vector3(-5.17f,0,10.67f);
            var door = entrance.GetComponent<ShopEntrance>() ?? entrance.gameObject.AddComponent<ShopEntrance>();
            door.SetDoor(Vector3.zero); door.SetFrontage(2.1f);

            // The original whole-mesh collider also included the painted parking area.
            // Restrict it to the venue; the new parking is not inside a solid building box.
            var box = building.GetComponent<BoxCollider>();
            box.center = new Vector3(0,3.23f,-4.9f);
            box.size = new Vector3(32.84f,7.3f,35.5f);

            // Backed and supported sign, low enough to retain the existing catalog height.
            Box("Marquee back", V(-1,4.5f,10.12f), V(20,2.05f,.24f), dark);
            foreach (float x in new[]{-10f,8f}) Box("Marquee roof support",V(x,3.7f,10.02f),V(.16f,1.4f,.26f),dark);
            Box("Marquee pink crown", V(-1,5.48f,10.28f), V(20,.10f,.09f),pink);
            Box("Marquee ice underline", V(-1,3.51f,10.28f), V(20,.07f,.09f),cyan);
            Label(dressing,"PINK FLAMINGO",V(-1,4.77f,10.27f),16.8f,.83f,new Color(1,.35f,.58f),180);
            Label(dressing,"D I S C O   /   D A N C E   /   L A T E",V(-1,3.95f,10.27f),14f,.27f,new Color(.6f,1,1),180);
            // Pink edge lighting repeats the building's stepped shape instead of adding
            // unrelated props across the roof.
            foreach (var side in new[]{-11.55f,16.12f})
                Box("Neon side cornice",V(side,3.13f,-5.8f),V(.09f,.09f,31.4f),pink);
            Box("Rear neon cornice",V(2.3f,3.13f,-21.47f),V(27.7f,.09f,.09f),pink);
            // Keep the entire rear window band exposed. The sign stands above the
            // stepped roof, with each leg measured against its own roof surface.
            foreach(float x in new[]{-1.5f,6.1f})
            {
                float roof=RoofY(building,x,-20.7f);
                Box("Rear sign roof foot",V(x,roof+.045f,-20.7f),V(.65f,.09f,.65f),dark);
                Box("Rear sign upright",V(x,(roof+5.45f)*.5f,-20.7f),V(.16f,5.45f-roof,.18f),dark);
            }
            Box("Club rear identity panel",V(2.3f,5.45f,-20.8f),V(10,1.5f,.18f),plum);
            Box("Rear sign neon border",V(2.3f,6.16f,-20.91f),V(10,.065f,.06f),pink);
            Label(dressing,"FLAMINGO",V(2.3f,5.65f,-20.91f),8.5f,.65f,new Color(1,.35f,.58f));
            Label(dressing,"AFTER DARK",V(2.3f,5.08f,-20.91f),4f,.20f,Color.white);

            // Door landing and the path to the west pavement stay clear. A contrasting
            // inset follows this path; the rope queue runs alongside it.
            Box("Entrance landing",V(-5.17f,.045f,15.7f),V(4.3f,.03f,5.8f),plum);

            // The pack supplies ropes and posts as separate prefabs. Match each rope's
            // actual endpoints with supported posts instead of grounding the rope itself.
            for(int i=0;i<4;i++)
                Prop(dressing,Club+"SM_Prop_Stanchion_Gold_01.prefab","Queue post "+i,V(-9f,Floor,13.8f+i*1.9f),0);
            for(int i=0;i<3;i++)
                Rope(dressing,V(-9f,Floor,13.8f+i*1.9f),V(-9f,Floor,15.7f+i*1.9f));
            for(int i=0;i<2;i++)
            {
                float x=i==0?-13.2f:18.1f;
                // Stand outside the glazed facade, on the two ends of the plaza.
                Box("Event board foot",V(x,.13f,12.9f),V(2.4f,.14f,.85f),dark);
                foreach(float dx in new[]{-.85f,.85f})
                    Box("Event board leg",V(x+dx,.62f,12.9f),V(.10f,1.04f,.14f),gold);
                Box("Poster frame",V(x,1.75f,12.9f),V(2.2f,2.2f,.18f),gold);
                Box("Poster face",V(x,1.75f,13.01f),V(2.02f,2.02f,.04f),i==0?plum:navy);
                Label(dressing,i==0?"FRIDAY":"SATURDAY",V(x,2.35f,13.04f),1.8f,.25f,Color.white,180);
                Label(dressing,i==0?"DISCO\nFEVER":"MIDNIGHT\nRADIO",V(x,1.65f,13.04f),1.75f,.5f,new Color(1,.35f,.58f),180);
                Label(dressing,"DOORS  21:00",V(x,.96f,13.04f),1.7f,.18f,Color.white,180);
            }
            // Planted borders frame the forecourt. The existing palms and service doors
            // are retained; nothing is placed on the main door's -5.17 x approach.
            Prop(dressing,"Assets/Prefabs/Residential/Landscaping/StreetBorder.prefab","West planted frontage",V(-16.5f,Floor,15.7f),0);
            Prop(dressing,"Assets/Prefabs/Residential/Landscaping/StreetBorder.prefab","East planted frontage",V(13.6f,Floor,15.7f),0);
            Prop(dressing,Palm+"Props/SM_Prop_Bench_Seat_01.prefab","Smoking corner bench",V(13.5f,Floor,12.9f),180,2.6f,1.2f);
            Prop(dressing,Palm+"Props/SM_Prop_Trash_Bin_01.prefab","Smoking corner bin",V(18.6f,Floor,17.8f),0,1,1);
            NightParking(root,dressing);
        }

        static void NightParking(GameObject root, Transform dressing)
        {
            // Replace the actual pavement cells, including the north street edge. A ten
            // metre opening in the new kerb leads straight to a six metre turning aisle.
            foreach (Transform t in root.transform.Cast<Transform>().ToArray())
            {
                if (!t.name.StartsWith("SM_Env_Sidewalk",StringComparison.Ordinal)) continue;
                var r=t.GetComponent<Renderer>(); if(r==null)continue;
                var c=r.bounds.center;
                if(c.x>-20.01f&&c.x<20.01f&&c.z>10f) Object.DestroyImmediate(t.gameObject);
            }
            Box("Single entrance plaza",V(0,.015f,15),V(40,.03f,10),concrete);
            Box("Visitor parking asphalt",V(0,.025f,27.5f),V(40,.07f,15),asphalt);
            foreach(float x in new[]{-12.5f,12.5f})
                Box("Street kerb",V(x,.10f,34.85f),V(15,.16f,.3f),concrete);
            foreach(float x in new[]{-19.88f,19.88f})
                Box("Parking side kerb",V(x,.10f,27.5f),V(.24f,.16f,15),concrete);
            var plan=ParkingBlockPlan.Generate(40,15);
            Vector3 World(Vector3 p)=>new Vector3(20-p.x,Floor,35-p.z);
            foreach(var stripe in plan.Markings)
                Line("Parking bay paint",World(new Vector3(stripe.A.x,0,stripe.A.y))+Vector3.up*.005f,
                    World(new Vector3(stripe.B.x,0,stripe.B.y))+Vector3.up*.005f,.07f,.008f,white);
            for(int i=0;i<plan.Stalls.Count;i++)
            {
                var p=World(plan.Stalls[i].Stand);
                Box("Wheel stop",V(p.x,Floor+.07f,p.z-2.35f),V(1.65f,.14f,.18f),concrete);
                if(i%3==1)continue;
                var car=Prop(dressing,LivingCity.Gameplay.CivilianVehicleCatalog.PathAt(i),"Visitor parked car "+(i+1),p,180,2.25f,5.05f);
                if(car!=null)
                {
                    string variant=new[]{"A","B","C"}[i%3];
                    var body=AssetDatabase.LoadAssetAtPath<Material>("Assets/Synty/PolygonPalmCity/Materials/Alts/PolygonPalmCity_03_"+variant+".mat");
                    foreach(var r in car.GetComponentsInChildren<Renderer>())
                        r.sharedMaterials=r.sharedMaterials.Select(m=>m!=null&&m.name.StartsWith("PolygonPalmCity_03_")&&body!=null?body:m).ToArray();
                }
            }
            for(int i=0;i<3;i++) Box("Entry guide dash",V(0,Floor+.006f,29.5f+i*1.7f),V(.10f,.008f,.85f),white);
            Box("Parking sign plinth",V(19,.15f,32),V(.9f,.18f,.9f),concrete);
            Box("Parking sign post",V(19,1.55f,32),V(.12f,2.7f,.12f),dark);
            Box("Parking sign",V(19,2.8f,32),V(1.7f,1.5f,.12f),navy);
            Label(dressing,"P",V(19,3.02f,32.08f),.8f,.75f,Color.white,180);
            Label(dressing,"CLUB GUESTS",V(19,2.40f,32.08f),1.45f,.18f,Color.white,180);
            // Subtle, deterministic worn patches remain below the wheels and markings.
            for(int i=0;i<8;i++)
                Box("Parking repair",V(-16+i*4.5f,Floor+.001f,30.2f+(i%2)*1.8f),V(1.6f,.001f,.65f),dark);
        }

        static void Police(GameObject root, Transform dressing)
        {
            var building=root.transform.Find("building-policestation");
            // Reuse the existing sign boards as civic signage instead of beer adverts.
            var renderer=building.GetComponent<Renderer>();
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m!=null&&m.name.StartsWith("Billboard_")?navy:m).ToArray();
            Box("Police roof sign",V(-3.5f,10.45f,-16.1f),V(16,2,.26f),navy);
            foreach(float x in new[]{-10.5f,3.5f}) Box("Police sign roof support",V(x,9.65f,-16.03f),V(.18f,1,.3f),dark);
            Box("Police sign gold cap",V(-3.5f,11.42f,-16.26f),V(16,.09f,.07f),gold);
            Label(dressing,"POLICE",V(-3.5f,10.62f,-16.25f),12,1.3f,Color.white);
            Label(dressing,"PALM CITY  /  PRECINCT 07",V(-3.5f,9.82f,-16.25f),12,.25f,new Color(.96f,.77f,.37f));
            // Matching identity over the fleet wing, facing the patrol approach.
            Box("Patrol wing sign",V(16f,10.65f,13.8f),V(10,1.7f,.22f),navy);
            foreach(float x in new[]{11.8f,20.2f})Box("Patrol sign support",V(x,9.75f,13.8f),V(.18f,1.3f,.3f),dark);
            Label(dressing,"POLICE  /  07",V(16f,10.86f,13.94f),8.5f,.9f,Color.white,180);
            Label(dressing,"PATROL DIVISION",V(16f,10.05f,13.94f),7,.22f,new Color(.96f,.77f,.37f),180);
            PoliceWallSigns(building,dressing);
            foreach(float x in new[]{-16f,9f})
                Prop(dressing,"Assets/Prefabs/Residential/Landscaping/StreetBorder.prefab","Formal civic border",V(x,Floor,-23f),0);
            // Preserve all six fleet transforms: Core promotes these exact cars to patrols.
            for(int i=0;i<=6;i++)
                Box("Patrol bay divider",V(i*(20f/6f),.012f,23.5f),V(.09f,.016f,6.6f),white);
            for(int i=0;i<6;i++)
                Box("Patrol bay end",V((i+.5f)*(20f/6f),.012f,20.15f),V(3.05f,.016f,.09f),gold);
            Box("Duty board base",V(24,.15f,22),V(2,.18f,.65f),concrete);
            foreach(float x in new[]{23.3f,24.7f})Box("Duty board post",V(x,1.3f,22),V(.10f,2.3f,.10f),dark);
            Box("Duty board",V(24,2.1f,22),V(2.3f,1.4f,.16f),navy);
            Label(dressing,"07",V(24,2.40f,22.09f),1.2f,.55f,new Color(.96f,.77f,.37f),180);
            Label(dressing,"POLICE ONLY",V(24,1.89f,22.09f),2,.24f,Color.white,180);
            Label(dressing,"KEEP CLEAR",V(24,1.61f,22.09f),1.7f,.16f,Color.white,180);
            // Wall-mounted emergency phones have a physical back plate and hood.
            foreach(float x in new[]{-23f,23f})
            {
                float z = WallZ(building, x, 1.6f, false);
                Box("Emergency phone backing",V(x,1.6f,z-.06f),V(.7f,1.2f,.14f),navy);
                Box("Emergency phone",V(x,1.5f,z-.21f),V(.34f,.5f,.18f),gold);
                Box("Emergency phone hood",V(x,2.24f,z-.20f),V(.78f,.09f,.38f),navy);
            }
            PoliceExterior(dressing);
            PoliceAirUnit(building,dressing);
        }

        static void PoliceWallSigns(Transform building,Transform dressing)
        {
            // Each material contains two different sign boxes. Measure the actual
            // face rather than mistaking a corner vertex for the board's centre.
            WallSign(building,dressing,29,p=>p.z< -10,0,"CITY OF PALM","POLICE DEPARTMENT");
            WallSign(building,dressing,29,p=>p.x< -20,90,"POLICE","PRECINCT 07");
            WallSign(building,dressing,30,p=>p.z>10,180,"PATROL","STAFF ONLY");
            WallSign(building,dressing,30,p=>p.x>20,270,"POLICE","PUBLIC ENQUIRIES");
        }

        static void WallSign(Transform building,Transform dressing,int submesh,Func<Vector3,bool> select,float yaw,string title,string subtitle)
        {
            var mesh=building.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;
            var points=mesh.GetTriangles(submesh).Select(i=>building.TransformPoint(vertices[i])).Where(select).ToArray();
            if(points.Length==0)throw new InvalidOperationException("Missing police sign face: "+title);
            var bounds=new Bounds(points[0],Vector3.zero);foreach(var point in points)bounds.Encapsulate(point);
            var centre=bounds.center;
            if(yaw==0)centre.z=bounds.min.z-.025f;
            else if(yaw==180)centre.z=bounds.max.z+.025f;
            else if(yaw==90)centre.x=bounds.min.x-.025f;
            else centre.x=bounds.max.x+.025f;
            float width=(yaw==0||yaw==180)?bounds.size.x:bounds.size.z;
            float height=bounds.size.y;
            Label(dressing,title,centre+Vector3.up*(height*.16f),width*.84f,height*.18f,Color.white,yaw);
            Label(dressing,subtitle,centre-Vector3.up*(height*.19f),width*.84f,height*.10f,new Color(.96f,.77f,.37f),yaw);
        }

        static void PoliceAirUnit(Transform building,Transform dressing)
        {
            // The authored landing deck is nine metres across. Centre the landing
            // gear on its H; the rotor may overhang the deck, clear of roof equipment.
            const float x=-16.07f,z=-8.55f;
            float deck=RoofY(building,x,z);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/SM_Veh_Helicopter_01.prefab");
            if(source==null)throw new FileNotFoundException("Police helicopter prefab missing");
            var helicopter=(GameObject)PrefabUtility.InstantiatePrefab(source,dressing);
            helicopter.name="Police air unit - parked on helipad";
            helicopter.transform.localPosition=V(x,deck,z);
            helicopter.transform.localRotation=Quaternion.Euler(0,155,0);
            var bounds=BoundsOf(helicopter);
            helicopter.transform.position+=Vector3.up*(deck-bounds.min.y);
            foreach(var body in helicopter.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(body);
            foreach(var collider in helicopter.GetComponentsInChildren<Collider>(true))collider.enabled=false;

            foreach(float dx in new[]{-4.15f,4.15f})
                foreach(float dz in new[]{-4.15f,4.15f})
                {
                    float y=RoofY(building,x+dx,z+dz);
                    Box("Helipad beacon housing",V(x+dx,y+.055f,z+dz),V(.28f,.11f,.28f),dark);
                    Box("Helipad blue beacon",V(x+dx,y+.13f,z+dz),V(.18f,.055f,.18f),cyan);
                }
            float service=RoofY(building,-8.5f,-8.5f);
            Box("Air unit safety cabinet",V(-8.5f,service+.72f,-8.5f),V(1.15f,1.44f,.55f),navy);
            Box("Safety cabinet cap",V(-8.5f,service+1.47f,-8.5f),V(1.26f,.08f,.65f),gold);
            Label(dressing,"FIRE",V(-8.5f,service+1.08f,-8.79f),.85f,.23f,Color.white);
            Label(dressing,"AIR UNIT 07",V(-8.5f,service+.53f,-8.79f),.94f,.14f,new Color(.96f,.77f,.37f));
            Prop(dressing,PoliceProps+"SM_Prop_Fire_Extinguisher_01.prefab","Helipad fire extinguisher",V(-7.45f,service,-8.5f),0,.45f,.45f);
            Prop(dressing,PoliceProps+"SM_Prop_Gun_Case_03.prefab","Air unit equipment case",V(-7.1f,service,-6.6f),90,1.4f,1);
        }

        static void PoliceExterior(Transform dressing)
        {
            // Public information sits off the west approach. Keep the central south
            // frontage and the entire north fleet/driveway strip free of additions.
            Box("Public information plinth",V(-28.9f,.16f,-22.5f),V(2.8f,.20f,1.1f),concrete);
            Box("Precinct directory",V(-28.9f,1.75f,-22.5f),V(2.3f,3,.30f),navy);
            Box("Directory gold cap",V(-28.9f,3.22f,-22.68f),V(2.3f,.08f,.06f),gold);
            Label(dressing,"07",V(-28.9f,2.7f,-22.67f),1.3f,.65f,new Color(.96f,.77f,.37f));
            Label(dressing,"POLICE",V(-28.9f,2.02f,-22.67f),1.9f,.32f,Color.white);
            Label(dressing,"PUBLIC ENQUIRIES\n24 HOURS",V(-28.9f,1.22f,-22.67f),2,.42f,Color.white);
            Label(dressing,"EMERGENCY  911",V(-28.9f,.58f,-22.67f),2,.18f,new Color(.96f,.77f,.37f));
            var flag=Prop(dressing,PoliceProps+"SM_Prop_Flag_Stand_01.prefab","Civic flag",V(-29,Floor,-17),0,2.5f,2.5f);
            flag.transform.localScale*=2.2f;
            var flagBounds=BoundsOf(flag);
            flag.transform.position+=dressing.TransformPoint(V(-29,Floor,-17))-V(flagBounds.center.x,flagBounds.min.y,flagBounds.center.z);
            foreach(var p in new[]{V(-33,Floor,-16),V(-33,Floor,14),V(33,Floor,-15),V(33,Floor,15)})
                Prop(dressing,"Assets/Prefabs/Residential/Landscaping/StreetBorder.prefab","Civic side planting",p,90,1.3f,7);

            // The noticeboard has its own frame and feet, away from glazing.
            foreach(float x in new[]{24.1f,26.7f})
                Box("Noticeboard leg",V(x,1.35f,-23.7f),V(.12f,2.58f,.14f),dark);
            Box("Community noticeboard",V(25.4f,2.1f,-23.7f),V(3.2f,2,.22f),navy);
            Box("Noticeboard hood",V(25.4f,3.13f,-23.85f),V(3.5f,.10f,.7f),dark);
            Label(dressing,"COMMUNITY NOTICES",V(25.4f,2.87f,-23.83f),2.8f,.21f,Color.white);
            foreach(float x in new[]{24.5f,25.4f,26.3f})
            {
                Box("Public notice paper",V(x,2.05f,-23.835f),V(.73f,1.02f,.012f),white);
                for(int i=0;i<5;i++)Box("Notice print",V(x,2.36f-i*.13f,-23.846f),V(i==0?.5f:.57f,.026f,.006f),navy);
            }
            Label(dressing,"REPORTS  /  LOST PROPERTY",V(25.4f,1.34f,-23.83f),2.85f,.16f,new Color(.96f,.77f,.37f));

            // Short groups protect the front corners without closing a pedestrian route.
            foreach(float x in new[]{-25f,-22.5f,20.5f,23f})
                Prop(dressing,Palm+"Props/SM_Prop_Bollard_02.prefab","Civic security bollard",V(x,Floor,-26),0,.4f,.4f);
            foreach(var p in new[]{V(-29,Floor,-10),V(29,Floor,-17),V(29,Floor,17)})
            {
                Box("Security pole base",p+V(0,.09f,0),V(.65f,.18f,.65f),concrete);
                Box("Security pole",p+V(0,2.1f,0),V(.14f,4.2f,.14f),dark);
                Box("Camera support arm",p+V(0,3.65f,-.28f),V(.10f,.10f,.65f),dark);
                Prop(dressing,PoliceProps+"SM_Prop_Security_Camera_01.prefab","Precinct CCTV",p+V(0,3.45f,-.55f),0,.65f,.75f);
                Box("Forecourt floodlight",p+V(0,4.1f,-.12f),V(.72f,.26f,.30f),dark);
                Box("Floodlight lens",p+V(0,4.08f,-.285f),V(.61f,.16f,.025f),white);
            }

            // Bicycle parking occupies the east-side furniture strip, leaving the
            // existing benches and the walkway beside the building accessible.
            Prop(dressing,PoliceProps+"SM_Prop_Bike_Stand_01.prefab","Visitor bicycle rack",V(31.5f,Floor,-1),90,2.6f,4.2f);
            foreach(float z in new[]{-1.6f,-.45f})
                Prop(dressing,"Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/SM_Veh_Bike_01.prefab","Parked visitor bicycle",V(30.9f,Floor,z),90,2.1f,.75f);
            Box("Cycle shelter roof",V(31.5f,2.72f,-1),V(3.3f,.14f,5),navy);
            foreach(float z in new[]{-3.15f,1.15f})
                Box("Cycle shelter upright",V(32.8f,1.41f,z),V(.14f,2.7f,.14f),dark);
            Box("Cycle shelter rear beam",V(32.8f,2.58f,-1),V(.16f,.15f,4.5f),dark);

            // Group service clutter within a small screened bay on the west side,
            // outside the building envelope and away from the vehicle yard.
            Box("Service bay pad",V(-31.5f,.035f,-1.5f),V(4,.07f,5),concrete);
            Box("Service screen back",V(-33.25f,1.05f,-1.5f),V(.16f,2,5),dark);
            foreach(float z in new[]{-3.9f,.9f})
                Box("Service screen return",V(-31.9f,1.05f,z),V(2.7f,2,.16f),dark);
            Prop(dressing,PoliceProps+"SM_Prop_Dumpster_01.prefab","Screened service dumpster",V(-31.4f,Floor,-2),90,2.4f,2.7f);
            Prop(dressing,PoliceProps+"SM_Prop_Bin_03.prefab","Service recycling bin",V(-31.2f,Floor,.15f),90,.75f,.85f);
        }

        static float RoofY(Transform building,float x,float z)
        {
            var probe=new GameObject("Temporary roof probe");
            try
            {
                probe.transform.SetPositionAndRotation(building.position,building.rotation);
                probe.transform.localScale=building.lossyScale;
                var collider=probe.AddComponent<MeshCollider>();collider.sharedMesh=building.GetComponent<MeshFilter>().sharedMesh;
                if(!collider.Raycast(new Ray(V(x,30,z),Vector3.down),out var hit,40))
                    throw new InvalidOperationException("No roof supporting sign at "+x+", "+z);
                return hit.point.y;
            }
            finally{Object.DestroyImmediate(probe);}
        }

        static void ClearOldForecourt(Transform building)
        {
            // Start from the stable source every bake. Trim the old forecourt while
            // keeping the building, entrance steps and rooftop details intact.
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CityKit/Buildings/building-nightclub.prefab").GetComponent<MeshFilter>().sharedMesh;
            // Build fresh buffers: a cloned mesh retains derived rendering data for
            // the original index layout, which becomes stale when triangles are cut.
            var copy=new Mesh{name="Nightclub without duplicated forecourt",indexFormat=IndexFormat.UInt32};
            copy.vertices=source.vertices;copy.normals=source.normals;
            copy.tangents=source.tangents;copy.colors32=source.colors32;
            for(int channel=0;channel<8;channel++)
            {
                var uv=new List<Vector4>();source.GetUVs(channel,uv);
                if(uv.Count>0)copy.SetUVs(channel,uv);
            }
            copy.subMeshCount=source.subMeshCount;
            for(int i=0;i<source.subMeshCount;i++)copy.SetTriangles(source.GetTriangles(i),i);
            var vertices=copy.vertices;
            foreach(int index in new[]{19,20})
            {
                var triangles=copy.GetTriangles(index);var keep=new List<int>();
                for(int i=0;i<triangles.Length;i+=3)
                {
                    float z=Mathf.Max(vertices[triangles[i]].z,vertices[triangles[i+1]].z,vertices[triangles[i+2]].z);
                    if(z>10.5f)continue;
                    keep.Add(triangles[i]);keep.Add(triangles[i+1]);keep.Add(triangles[i+2]);
                }
                copy.SetTriangles(keep,index);
            }
            // The atlas submesh also contains the old parking stripes and low kerbs.
            // Clear that obsolete forecourt dressing beyond the entrance steps.
            var atlasTriangles=copy.GetTriangles(0);var atlasKeep=new List<int>();
            for(int i=0;i<atlasTriangles.Length;i+=3)
            {
                var a=vertices[atlasTriangles[i]];var b=vertices[atlasTriangles[i+1]];var c=vertices[atlasTriangles[i+2]];
                if(Mathf.Min(a.z,b.z,c.z)>=12.5f&&Mathf.Max(a.y,b.y,c.y)<=.20f)continue;
                atlasKeep.Add(atlasTriangles[i]);atlasKeep.Add(atlasTriangles[i+1]);atlasKeep.Add(atlasTriangles[i+2]);
            }
            copy.SetTriangles(atlasKeep,0);
            string path=Folder+"/Nightclub shell.asset";var stored=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(stored==null){AssetDatabase.CreateAsset(copy,path);stored=copy;}
            else{stored.Clear();EditorUtility.CopySerialized(copy,stored);Object.DestroyImmediate(copy);}
            EditorUtility.SetDirty(stored);
            building.GetComponent<MeshFilter>().sharedMesh=stored;
        }
        static void Rope(Transform parent,Vector3 a,Vector3 b)
        {
            // Continuous sagging rope, with both ends tied at the stanchion's crown.
            float top=.94f;
            for(int i=0;i<12;i++)
            {
                Vector3 Point(float t)=>Vector3.Lerp(a,b,t)+Vector3.up*(top-.20f*Mathf.Sin(t*Mathf.PI));
                Vector3 from=Point(i/12f),to=Point((i+1)/12f);
                var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name="Velvet rope";go.transform.SetParent(structure,false);
                go.transform.localPosition=(from+to)*.5f;go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,(to-from).normalized);
                go.transform.localScale=V(.035f,Vector3.Distance(from,to)*.5f,.035f);go.GetComponent<Renderer>().sharedMaterial=plum;
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }
        }

        static float WallZ(Transform building, float x, float y, bool north)
        {
            var probe = new GameObject("Temporary surface probe");
            try
            {
                probe.transform.SetPositionAndRotation(building.position, building.rotation);
                probe.transform.localScale = building.lossyScale;
                var collider = probe.AddComponent<MeshCollider>();
                collider.sharedMesh = building.GetComponent<MeshFilter>().sharedMesh;
                var ray = new Ray(V(x,y,north?40:-40),north?Vector3.back:Vector3.forward);
                if (!collider.Raycast(ray,out var hit,80))
                    throw new InvalidOperationException("No wall supporting venue prop at x="+x);
                return hit.point.z;
            }
            finally { Object.DestroyImmediate(probe); }
        }

        static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
        static Transform Group(Transform parent,string name){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        static Material Mat(string name,Color color,bool glow=false)
        {
            string path=Folder+"/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find(glow?"Universal Render Pipeline/Lit":"LivingCity/Street Prop Surface"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.25f);m.enableInstancing=true;
            if(glow){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*2.2f);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;}
            EditorUtility.SetDirty(m);return m;
        }
        static void Box(string name,Vector3 p,Vector3 size,Material mat)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(structure,false);
            go.transform.localPosition=p;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        static void Line(string name,Vector3 a,Vector3 b,float width,float height,Material mat)
        {
            Box(name,(a+b)*.5f,V(width,height,Vector3.Distance(a,b)),mat);
            structure.GetChild(structure.childCount-1).localRotation=Quaternion.LookRotation((b-a).normalized,Vector3.up);
        }
        static void Label(Transform parent,string value,Vector3 p,float width,float height,Color color,float yaw=0)
        {
            var go=new GameObject(value.Replace('\n',' '));go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localRotation=Quaternion.Euler(0,yaw,0);
            var text=go.AddComponent<TMPro.TextMeshPro>();
            text.font=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            text.fontSharedMaterial=lettering;text.text=value;text.fontSize=100;text.color=color;
            text.alignment=TMPro.TextAlignmentOptions.Center;text.textWrappingMode=TMPro.TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta=new Vector2(200,30);text.ForceMeshUpdate(true,true);
            var b=text.mesh.bounds;
            float fit=Mathf.Min(width/Mathf.Max(b.size.x,.001f),height/Mathf.Max(b.size.y,.001f));
            var scale=V(fit,fit,1);
            go.transform.localScale=scale;go.transform.localPosition=p-go.transform.localRotation*Vector3.Scale(b.center,scale);
            go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
        }

        static GameObject Prop(Transform parent,string path,string name,Vector3 p,float yaw,float maxWidth=100,float maxDepth=100)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(source==null)throw new FileNotFoundException("Venue prop missing",path);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(source,parent);go.name=name;go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.Euler(0,yaw,0);
            Bounds b=BoundsOf(go);float scale=Mathf.Min(1,maxWidth/b.size.x,maxDepth/b.size.z);go.transform.localScale*=scale;b=BoundsOf(go);
            go.transform.position+=parent.TransformPoint(p)-V(b.center.x,b.min.y,b.center.z);
            foreach(var body in go.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(body);
            foreach(var c in go.GetComponentsInChildren<Collider>(true))c.enabled=false;
            return go;
        }
        static Bounds BoundsOf(GameObject go){var rs=go.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
        static void Merge(string venue)
        {
            var batches=structure.GetComponentsInChildren<MeshFilter>().GroupBy(m=>m.GetComponent<Renderer>().sharedMaterial).ToArray();
            foreach(var batch in batches)
            {
                var mesh=new Mesh{name=venue+" "+batch.Key.name,indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(batch.Select(m=>new CombineInstance{mesh=m.sharedMesh,transform=structure.worldToLocalMatrix*m.transform.localToWorldMatrix}).ToArray());
                string path=Folder+"/"+venue+"-"+batch.Key.name+".asset";
                var stored=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(stored==null){AssetDatabase.CreateAsset(mesh,path);stored=mesh;}else{stored.Clear();EditorUtility.CopySerialized(mesh,stored);Object.DestroyImmediate(mesh);}
                EditorUtility.SetDirty(stored);
                var go=new GameObject(batch.Key.name);go.transform.SetParent(structure,false);go.AddComponent<MeshFilter>().sharedMesh=stored;go.AddComponent<MeshRenderer>().sharedMaterial=batch.Key;
                foreach(var mf in batch)Object.DestroyImmediate(mf.gameObject);
            }
        }
    }
}
