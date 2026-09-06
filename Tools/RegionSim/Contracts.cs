using System;
using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

// View-free contracts. Runtime district/vehicle build is deliberately outside this harness.
namespace RoadDemo
{
    public sealed class TooltipAttribute : System.Attribute { public TooltipAttribute(string text) {} }
    public class RoadNode { public float X,Z,XMin,XMax,ZMin,ZMax; }
    public class LaneNet { public readonly List<RoadNode> Nodes = new(); }
    public struct DistrictPortal { public Vector3 Local,LocalDir; public RoadNode Node; public string Tag; }
    public class DistrictSlot { public DistrictKind kind; public CityEdge edge; public int seed,sizeAcross,sizeDeep; public string name; public float strip; }
    public class DistrictReservations { public void NoFlora(Rect world) {} }
    public interface IDistrict
    {
        string Name { get; } DistrictFrame Frame {get;set;} Rect LocalBounds {get;}
        IReadOnlyList<DistrictPortal> Portals {get;} void Plan(float[] links,int seed);
    }
    public abstract class DistrictContract : IDistrict
    {
        public abstract string Name {get;}
        public DistrictFrame Frame {get;set;}
        public Rect LocalBounds {get;protected set;}
        public readonly List<DistrictPortal> Published = new();
        public IReadOnlyList<DistrictPortal> Portals => Published;
        protected void Publish(float[] links)
        {
            Published.Clear();
            if(links!=null) foreach(float x in links)
                Published.Add(new DistrictPortal {Local=new Vector3(x,0,0),LocalDir=Vector3.forward,Node=new RoadNode()});
        }
        public abstract void Plan(float[] links,int seed);
    }
    public partial class CoreDistrict
    {
        public CoreRoads.Raster Raster;
        public IReadOnlyList<RasterGateways.Gateway> RegionGateways;
        public DistrictFrame Frame;
        public LaneNet Net = new();
        public CoreLayout.Plan Layout;
        public Rect LocalBounds;
        public List<CoreAmenityLayout.Site> FuelSites = new();
        List<CoreLayout.Block> _blocks;
        CoreLayout.Plan _plan { get => Layout; set => Layout=value; }
        CoreRoads.Raster _raster { get => Raster; set => Raster=value; }
        int _seed;
        public int quarterBudget;
        public void Plan(int seed)
        {
            _seed=seed;
            _blocks=CoreBlockCatalog.CreateBlocks();
            Layout=CoreLayout.Arrange(_blocks,seed,out Raster);
            KeepQuarters();
            LocalBounds=Rect.MinMaxRect(Raster.X0,Raster.Z0,Raster.X(Raster.NX),Raster.Z(Raster.NZ));
            Frame=DistrictFrame.At(-LocalBounds.xMin,-LocalBounds.yMin,0);
            foreach(var box in Raster.Junctions)
            {
                var world=Frame.ToWorldRect(box);
                Net.Nodes.Add(new RoadNode {X=world.center.x,Z=world.center.y,XMin=world.xMin,XMax=world.xMax,ZMin=world.yMin,ZMax=world.yMax});
            }
        }
    }
    public class IndustrialDistrict : DistrictContract
    {
        public bool compact, pocket, portZone;
        public static int RejectPlans, PlanCalls;
        public override string Name => "Industry";
        public CoreRoads.Raster Raster;
        public IndustrialLayout.Plan Layout;
        public float SeawardCut => Layout?.SeawardCut ?? float.PositiveInfinity;
        public static List<float> SeawardStreets(CoreRoads.Raster raster,float cut)
        {
            var crowns=new List<float>();
            if(raster==null||float.IsPositiveInfinity(cut)) return crowns;
            foreach(var reach in raster.Stretches)
                if(!reach.Vertical&&reach.NodeB<0&&Math.Abs(reach.To-cut)<CoreRoads.Cell*0.51f) crowns.Add(reach.Crown);
            crowns.Sort(); return crowns;
        }
        public LaneNet Net = new();
        public override void Plan(float[] links,int seed)
        {
            PlanCalls++;
            if(RejectPlans>0) { RejectPlans--; Raster=new CoreRoads.Raster(); LocalBounds=new Rect(0,0,100,100); return; }
            Layout=IndustrialLayout.Arrange(seed,out Raster,IndustrialLayout.Shape.For(compact,pocket,portZone)); LocalBounds=IndustrialLayout.Bounds(Raster);
            if(!float.IsPositiveInfinity(Layout.SeawardCut)) LocalBounds=Rect.MinMaxRect(LocalBounds.xMin,LocalBounds.yMin,Math.Min(LocalBounds.xMax,Layout.SeawardCut),LocalBounds.yMax);
            foreach(var box in Raster.Junctions) Net.Nodes.Add(new RoadNode());
        }
    }
    // Traffic prefab loading is outside this land-use harness.
    public static class CivilianFleet { public static List<GameObject> Load() => new(); }
    public static class StreetKit { public const float StreetHalf=7.5f, OuterHalf=StreetHalf+6.5f; }
    public static class FuelStationBlock
    {
        public const float BlockFrontage=60f,BlockDepth=55f;
        public static Rect PreviewBounds => new Rect(-30,-35,60,55);
    }
    public static class ParkingBlockSite { public static Rect Surface(Rect box,ParkingBlockStyle style) => box; }
}
namespace HarborDemo
{
    public partial class HarborDistrict : DistrictContract
    {
        public override string Name => "Harbor";
        int seed;
        float[] _links;
        System.Random _rng;
        // The bounds planner and its numerical inputs are extracted from runtime source.
    }
}
namespace AirportDemo
{
    public class AirportDistrict : DistrictContract
    {
        public override string Name => "Airport";
        public override void Plan(float[] links,int seed) { LocalBounds=new Rect(-880,-660,1600,660); Publish(links); }
    }
}
namespace SuburbDemo
{
    public class SuburbDistrict : DistrictContract
    {
        public override string Name => "Suburb";
        int columns,rows;
        public float areaScale=1f;
        public static SuburbDistrict ForCity(DistrictSlot slot,float areaScale=1f) => new SuburbDistrict {columns=slot.sizeAcross,rows=slot.sizeDeep,areaScale=areaScale};
        public override void Plan(float[] links,int seed) { LocalBounds=new Rect(-130,-rows*90*areaScale,columns*100,rows*90*areaScale); Publish(links); }
    }
}
