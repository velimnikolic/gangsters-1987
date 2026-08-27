using System.Collections.Generic;
using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LivingCity.City
{

    public class PathFinding : MonoBehaviour
    {
        private BinaryHeap Open = new BinaryHeap(2048);
        private Dictionary<int, int> OpenIDs = new Dictionary<int, int>();
        private Dictionary<int, PathNode> Closed = new Dictionary<int, PathNode>();
        [HideInInspector]
        public List<Path> PathList = new List<Path>();
        [HideInInspector]
        public List<Path> wholePath;

        private Vector3 startPoint;
        private Vector3 endPoint;

        private Tile endTile;
        private Tile startTile;
        private int endId;

        public List<Path> GetPath(Vector3 start, Vector3 end, PathType pathType)
        {
            Open.Clear();
            PathList = new List<Path>();
            Closed.Clear();
            OpenIDs.Clear();

            startPoint = start;
            endPoint = end;

            startTile = FindClosestTile(startPoint, pathType);
            endTile = FindClosestTile(endPoint, pathType);

            // PATCH (Living City): FindClosestTile returns null when its 10m box finds no tile of
            // the right type - off the map, over a block interior, or on a park with PathType.Road
            // - and every use of it below dereferenced that straight away. A caller that gets null
            // back re-paths or gives up, which is what the null return already means everywhere
            // else in this method.
            if (!startTile || !endTile)
                return null;

            List<Path> startPaths;
            if(pathType == PathType.Sidewalk)
            {
                startPaths = startTile.sidewalkPaths;
            }
            else
                startPaths = startTile.paths;

            if (startPaths == null || startPaths.Count == 0)
                return null;

            foreach (Path path in startPaths)
            {
                float h = CalculateHeuristic(path.pathPositions[path.pathPositions.Count - 1].position);
                float g = Vector3.Distance(startPoint, path.pathPositions[0].position) + Vector3.Distance(startPoint, path.pathPositions[path.pathPositions.Count-1].position);
                PathNode node = new PathNode() {path = path, lastNode = null, currentScore = g, score = h +g};
                Open.Insert(node);
                OpenIDs.Add(node.path.Id, 0);
            }
            if(DoBfsSteps())
            {
                // PATCH (Living City): lastNode is null when the search ended on a SEED node, i.e.
                // when the destination resolved onto the tile the querent is already standing on.
                // That is not exotic - every scripted destination (an exit gate, the school kerb,
                // the station forecourt, a bank bay) is a point beside a landmark, and a car
                // re-pathing while already on that tile lands here. The line below used to
                // dereference lastNode unconditionally and take the whole frame down with a
                // NullReferenceException.
                //
                // What it is doing is refining the LAST lane: pick, among the lanes the previous
                // one leads into, whichever ends closest to the destination. With no previous lane
                // the analogous set is the start tile's own lanes, which is exactly the
                // single-tile trip this is.
                var last = Closed[endId];
                last.path = last.lastNode != null
                    ? FindClosestPath(endPoint, last.lastNode.path.nextPaths)
                    : FindClosestPath(endPoint, startPaths);

                GetPathList(last);
                PathList.Reverse();
                //PathList[0] = FindClosestPath(startPoint, startPaths);
                return PathList;
            }
            else
            {
                return null;
            }
        }
        public List<Path> GetPathWithCheckpoints(List<Vector3> checkpoints, PathType pathType)
        {
            wholePath = new List<Path>();
            
            for (int i = 1; i < checkpoints.Count ;i++)
            {
                startPoint = checkpoints[i-1];
                endPoint = checkpoints[i];
                Open.Clear();
                PathList = new List<Path>();
                OpenIDs.Clear();

                startTile = FindClosestTile(checkpoints[i-1], pathType);
                endTile = FindClosestTile(checkpoints[i], pathType);

                // PATCH (Living City): same guard as GetPath - a checkpoint that lands off the
                // tile network is a route this cannot serve, not a crash.
                if (!startTile || !endTile)
                    return null;

                Closed.Clear();
                List<Path> startPaths;
                // A later leg re-picks the previous leg's last path: it starts from the
                // path before it and replaces it. A previous leg of a single path has no
                // path before its last, so that leg continues from the last path instead and
                // keeps it.
                bool replacesLast = false;
                if (i == 1)
                {
                    if (pathType == PathType.Sidewalk)
                        startPaths = startTile.sidewalkPaths;
                    else
                        startPaths = startTile.paths;
                }
                else if (wholePath.Count >= 2)
                {
                    startPaths = wholePath[wholePath.Count - 2].nextPaths;
                    replacesLast = true;
                }
                else
                    startPaths = wholePath[wholePath.Count - 1].nextPaths;
                foreach (Path path in startPaths)
                {
                    float h = CalculateHeuristic(path.pathPositions[path.pathPositions.Count - 1].position);
                    float g = Vector3.Distance(startPoint, path.pathPositions[0].position) + Vector3.Distance(startPoint, path.pathPositions[path.pathPositions.Count - 1].position);
                    PathNode node = new PathNode() {path = path, lastNode = null, currentScore = g, score = h + g};
                    Open.Insert(node);
                    OpenIDs.Add(node.path.Id, 0);
                }
                if (DoBfsSteps())
                {
                    // PATCH (Living City): lastNode is null on a leg that starts and ends on the
                    // same tile - see the same guard in GetPath.
                    var last = Closed[endId];
                    if (i == checkpoints.Count - 1)
                        last.path = last.lastNode != null
                            ? FindClosestPath(checkpoints[i], last.lastNode.path.nextPaths)
                            : FindClosestPath(checkpoints[i], startPaths);

                    GetPathList(last);
                    PathList.Reverse();
                    //PathList[0] = FindClosestPath(startPoint, startPaths);
                    if (replacesLast)
                    {
                        wholePath.RemoveAt(wholePath.Count - 1);
                    }
                    wholePath.AddRange(PathList);
                }
                else
                {
                    return null;
                }
            }
            return wholePath;
        }
        private bool DoBfsSteps()
         {
                //int i = 0;
             while (Open.Count > 0)
             {

                PathNode node = GetBestNode();
               //Debug.Log(i++ + " " + (node.score) + " "+ node.path.transform.parent.parent.name);
                if (node.path.TileId == endTile.Id)
                {
                    Closed.Add(node.path.Id, node);
                    endId = node.path.Id;
                    return true;
                }
                foreach (Path item in node.path.nextPaths)
                {
                    if(!Closed.ContainsKey(item.Id) && !OpenIDs.ContainsKey(item.Id))
                    {
                        Vector3 distance = item.pathPositions[0].position - item.pathPositions[item.pathPositions.Count-1].position;
                        float currentScore = node.currentScore + Math.Abs(distance.x) + Math.Abs(distance.y) + Math.Abs(distance.z) - ((item.speed /10));

                        Open.Insert(new PathNode() { path = item, lastNode = node, currentScore = currentScore, score = CalculateHeuristic(item.pathPositions[item.pathPositions.Count - 1].position) + currentScore });
                        OpenIDs.Add(item.Id, 0);
                    }
                }
                    Closed.Add(node.path.Id, node);

             }
            return false;
         }

        private PathNode GetBestNode()
        {
            PathNode pathNode = Open.PopTop();
            OpenIDs.Remove(pathNode.path.Id);
            return pathNode;
        }

        private float CalculateHeuristic(Vector3 currentTile)
        {
            Vector3 distance = endPoint - currentTile;
            return Math.Abs(distance.x) + Math.Abs(distance.y) + Math.Abs(distance.z);
        }
        private void GetPathList(PathNode thisNode)
        {
            if (thisNode != null)
            {
                while (thisNode.lastNode != null)
                {
                    PathList.Add(thisNode.path);
                    thisNode = thisNode.lastNode;
                }
                if (thisNode != null)
                    PathList.Add(thisNode.path);
            }
        }

        // PATCH (Living City): the overlap box used to allocate its result array per call and
        // to sweep in EVERY collider around the querent - at crowd scale that is a fistful
        // of pedestrian capsules per search, none of which can ever be a Tile. Shared
        // buffer, and the pedestrian layer masked out.
        private static readonly Collider[] TileOverlapBuffer = new Collider[128];
        private const int TileOverlapMask = ~(1 << LivingCity.Entities.PedestrianSpawner.PedestrianLayer);

        private Tile FindClosestTile(Vector3 point, PathType pathType)
        {
            Tile closestTile = null;
            int hits = Physics.OverlapBoxNonAlloc(point, new Vector3(Mathf.Abs(5 * transform.lossyScale.x), Mathf.Abs(5 * transform.lossyScale.y), Mathf.Abs(5 * transform.lossyScale.z)), TileOverlapBuffer, Quaternion.identity, TileOverlapMask);
            for (int c = 0; c < hits; c++)
            {
                Collider collider = TileOverlapBuffer[c];
                closestTile = collider.transform.GetComponent<Tile>();
                if (closestTile != null)
                {
                    if (pathType == PathType.Road)
                    {
                        if(closestTile.tileType == Tile.TileType.Road || closestTile.tileType == Tile.TileType.RoadAndRail)
                        {
                            return closestTile;
                        }
                    }
                    else if(pathType == PathType.Rail)
                    {
                        if (closestTile.tileType == Tile.TileType.Rail || closestTile.tileType == Tile.TileType.RoadAndRail)
                        {
                            return closestTile;
                        }
                    }
                    else if (pathType == PathType.Sidewalk)
                    {
                        if (closestTile.tileType == Tile.TileType.Road || closestTile.tileType == Tile.TileType.OnlyPathwalk)
                        {
                            return closestTile;
                        }
                    }
                    
                }
            }
            float minDistance = Mathf.Infinity;
            
            foreach (Tile tile in Tile.Tiles)
            {
                float distance = Vector3.Distance(tile.transform.position, point);
                if (distance < minDistance)
                {
                    if (pathType == PathType.Road)
                    {
                        if (tile.tileType == Tile.TileType.Road || tile.tileType == Tile.TileType.RoadAndRail)
                        {
                            minDistance = distance;
                            closestTile = tile;
                        }
                    }
                    else if (pathType == PathType.Rail)
                    {
                        if (tile.tileType == Tile.TileType.Rail || tile.tileType == Tile.TileType.RoadAndRail)
                        {
                            minDistance = distance;
                            closestTile = tile;
                        }
                    }
                    else if (pathType == PathType.Sidewalk)
                    {
                        if (tile.tileType == Tile.TileType.Road || tile.tileType == Tile.TileType.OnlyPathwalk)
                        {
                            minDistance = distance;
                            closestTile = tile;
                            
                        }
                    }
                   
                }
            }

            return closestTile;
        }
        private Path FindClosestPath(Vector3 point, List<Path> paths)
        {
            Path closestPath = null;
            float minDistance = Mathf.Infinity;
            foreach (Path path in paths)
            {
                for (int i = 0; i < path.pathPositions.Count; i++)
                {
                    float distance = Vector3.Distance(path.pathPositions[i].position, point);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestPath = path;
                    }
                }
            }
            return closestPath;
        }
    }
    #region Editor
#if UNITY_EDITOR
    [CustomEditor(typeof(PathFinding)), CanEditMultipleObjects]
    public class CustomPathEditor : Editor
    {
        PathFinding navPath;
        private void OnEnable()
        {
            navPath = target as PathFinding;
        }
        void OnSceneGUI()
        {
            if (navPath.wholePath.Count == 0)
            {
                if (navPath.PathList.Count != 0)
                {
                    for (int i = 0; i < navPath.PathList.Count; i++)
                    {
                        if (navPath.PathList[i] != null)
                        {
                            if (i < navPath.PathList.Count - 1)
                            {
                                for (int j = 1; j < navPath.PathList[i].pathPositions.Count; j++)
                                {
                                    Handles.color = Color.white;
                                    Handles.DrawLine(navPath.PathList[i].pathPositions[j - 1].position, navPath.PathList[i].pathPositions[j].position);
                                    Handles.color = Color.blue;
                                    Handles.ArrowHandleCap(0, navPath.PathList[i].pathPositions[j - 1].position, Quaternion.LookRotation(navPath.PathList[i].pathPositions[j].position - navPath.PathList[i].pathPositions[j - 1].position), 3f, EventType.Repaint);
                                    if (i == 0)
                                        Handles.color = Color.blue;
                                    else if (i == navPath.PathList.Count - 1)
                                        Handles.color = Color.red;
                                    else
                                        Handles.color = Color.white;
                                    Handles.SphereHandleCap(0, navPath.PathList[i].pathPositions[j].position, Quaternion.LookRotation(navPath.PathList[i].pathPositions[j].position), 0.2f, EventType.Repaint);
                                }

                            }
                        }

                    }
                }
            }
            else 
            {
                for (int i = 0; i < navPath.wholePath.Count; i++)
                {
                    if (navPath.wholePath[i] != null)
                    {
                        if (i < navPath.wholePath.Count - 1)
                        {
                            for (int j = 1; j < navPath.wholePath[i].pathPositions.Count; j++)
                            {
                                Handles.color = Color.white;
                                Handles.DrawLine(navPath.wholePath[i].pathPositions[j - 1].position, navPath.wholePath[i].pathPositions[j].position);
                                Handles.color = Color.blue;
                                Handles.ArrowHandleCap(0, navPath.wholePath[i].pathPositions[j - 1].position, Quaternion.LookRotation(navPath.wholePath[i].pathPositions[j].position - navPath.wholePath[i].pathPositions[j - 1].position), 3f, EventType.Repaint);
                                if (i == 0)
                                    Handles.color = Color.blue;
                                else if (i == navPath.wholePath.Count - 1)
                                    Handles.color = Color.red;
                                else
                                    Handles.color = Color.white;
                                Handles.SphereHandleCap(0, navPath.wholePath[i].pathPositions[j].position, Quaternion.LookRotation(navPath.wholePath[i].pathPositions[j].position), 0.2f, EventType.Repaint);
                            }

                        }
                    }

                }
            }
            
        }
    }
#endif
    #endregion
}
