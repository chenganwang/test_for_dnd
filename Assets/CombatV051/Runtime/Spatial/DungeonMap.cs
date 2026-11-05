using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CombatV051.Domain;

namespace CombatV051.Runtime
{
    [Serializable] public sealed class DungeonRoomGeometry
    {
        public string id;
        public Vector3 center;
        public float width = 24, depth = 20;
    }

    [Serializable] public sealed class DungeonPassageGeometry
    {
        public string id, roomA, roomB;
        public Vector3 center;
        public float width = 6;
        public bool open;
    }

    /// <summary>Original XZ geometry and an open-passage graph. No progression rules, resources or random streams live here.</summary>
    public sealed class DungeonMap : MonoBehaviour
    {
        const int WallLayer = 8;
        const float WallThickness = .35f, WallHeight = 2.8f, EntryDepth = 1.25f;
        readonly Dictionary<string, DungeonRoomGeometry> rooms = new Dictionary<string, DungeonRoomGeometry>();
        readonly Dictionary<string, Passage> passages = new Dictionary<string, Passage>();
        readonly Dictionary<string, Cover> covers = new Dictionary<string, Cover>();
        readonly Dictionary<string, string[]> routeCache = new Dictionary<string, string[]>();
        readonly Dictionary<string,GameObject> v12Obstacles=new Dictionary<string,GameObject>();
        readonly Dictionary<string,string> v12RemovalFlags=new Dictionary<string,string>();
        Transform generated;

        sealed class Passage
        {
            public DungeonPassageGeometry geometry;
            public Vector3 forward;
            public float halfGap;
            public GameObject door, strip;
            public BoxCollider collider;
            public Vector3 Inside(string room) => geometry.center + forward * ((room == geometry.roomB ? 1 : -1) * (halfGap + EntryDepth));
            public string Other(string room) => room == geometry.roomA ? geometry.roomB : geometry.roomA;
        }
        sealed class Cover { public Transform view; public Vector3 original, moved; }

        public void Initialize(Camera camera, DungeonRoomGeometry[] roomGeometry, DungeonPassageGeometry[] passageGeometry,V12MapObstacle[] obstacles=null)
        {
            if (!camera) throw new ArgumentNullException(nameof(camera));
            if (roomGeometry == null || roomGeometry.Length == 0 || passageGeometry == null) throw new ArgumentException("Dungeon geometry is required.");
            var inputRooms = new Dictionary<string, DungeonRoomGeometry>();
            foreach (var r in roomGeometry)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.id) || inputRooms.ContainsKey(r.id) || !Finite(r.center) || r.width < 8 || r.depth < 8)
                    throw new ArgumentException("Invalid or duplicate room geometry.");
                inputRooms.Add(r.id, new DungeonRoomGeometry { id = r.id, center = r.center, width = r.width, depth = r.depth });
            }
            var inputPassages = new List<DungeonPassageGeometry>();
            var ids = new HashSet<string>();
            foreach (var p in passageGeometry)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.id) || !ids.Add(p.id) || !inputRooms.ContainsKey(p.roomA) || !inputRooms.ContainsKey(p.roomB) || p.roomA == p.roomB || !Finite(p.center) || p.width < 2)
                    throw new ArgumentException("Invalid or duplicate passage geometry.");
                Vector3 difference = inputRooms[p.roomB].center - inputRooms[p.roomA].center;
                if ((Mathf.Abs(difference.x) < .01f) == (Mathf.Abs(difference.z) < .01f)) throw new ArgumentException("Passages must connect cardinally aligned rooms.");
                var a = inputRooms[p.roomA]; var b = inputRooms[p.roomB]; bool horizontal = Mathf.Abs(difference.x) > .01f;
                float gap = horizontal ? Mathf.Abs(difference.x) - (a.width + b.width) * .5f : Mathf.Abs(difference.z) - (a.depth + b.depth) * .5f;
                float offsetA = horizontal ? Mathf.Abs(p.center.z - a.center.z) : Mathf.Abs(p.center.x - a.center.x);
                float offsetB = horizontal ? Mathf.Abs(p.center.z - b.center.z) : Mathf.Abs(p.center.x - b.center.x);
                if (gap <= 0 || offsetA + p.width * .5f > (horizontal ? a.depth : a.width) * .5f || offsetB + p.width * .5f > (horizontal ? b.depth : b.width) * .5f)
                    throw new ArgumentException("Passage opening is outside its room wall.");
                inputPassages.Add(new DungeonPassageGeometry { id = p.id, roomA = p.roomA, roomB = p.roomB, center = p.center, width = p.width, open = p.open });
            }

            Dispose();
            generated = new GameObject("Connected dungeon geometry").transform; generated.SetParent(transform, false);
            foreach (var pair in inputRooms) rooms.Add(pair.Key, pair.Value);
            foreach (var p in inputPassages)
            {
                var a = rooms[p.roomA]; var b = rooms[p.roomB]; Vector3 forward = BattleGeometry.Ground(b.center - a.center).normalized;
                bool horizontal = Mathf.Abs(forward.x) > .5f;
                float gap = horizontal ? Mathf.Abs(a.center.x - b.center.x) - (a.width + b.width) * .5f : Mathf.Abs(a.center.z - b.center.z) - (a.depth + b.depth) * .5f;
                passages.Add(p.id, new Passage { geometry = p, forward = forward, halfGap = gap * .5f });
            }
            var wallLists = rooms.Keys.ToDictionary(id => id, id => new List<Renderer>());
            foreach (var room in rooms.Values) BuildRoom(room, wallLists[room.id]);
            foreach (var passage in passages.Values) BuildPassage(passage, wallLists[passage.geometry.roomA]);
            foreach (var room in rooms.Values)
            {
                var cutaway = new GameObject("Cutaway " + room.id); cutaway.transform.SetParent(generated, false);
                cutaway.AddComponent<FrontWallCutaway>().Configure(wallLists[room.id].ToArray(), camera, room.center);
            }
            if(obstacles==null)BuildCovers();
            else foreach(var obstacle in obstacles)
            {
                var item=BattleVisual.Box(obstacle.id,new Vector3(obstacle.x,obstacle.height*.5f,obstacle.z),new Vector3(obstacle.width,obstacle.height,obstacle.depth),obstacle.roomId=="V12_R3"?new Color(.32f,.25f,.18f):new Color(.31f,.43f,.40f),generated,WallLayer);
                if(obstacle.lowCover)item.AddComponent<P5LowCover>();
                v12Obstacles.Add(obstacle.id,item);if(!string.IsNullOrEmpty(obstacle.removedByFlag))v12RemovalFlags.Add(obstacle.id,obstacle.removedByFlag);
            }
            Physics.SyncTransforms();
        }
        public void SetV12WorldFlags(IReadOnlyCollection<string> flags)
        {
            bool changed=false;
            foreach(var pair in v12RemovalFlags){var obj=v12Obstacles[pair.Key];bool show=!flags.Contains(pair.Value);if(obj.activeSelf!=show){obj.SetActive(show);changed=true;}}
            if(changed)Physics.SyncTransforms();
        }

        void BuildRoom(DungeonRoomGeometry room, List<Renderer> wallList)
        {
            var holder = new GameObject("Room " + room.id).transform; holder.SetParent(generated, false);
            BattleVisual.Box("Floor " + room.id, room.center + Vector3.down * .15f,
                new Vector3(room.width, .3f, room.depth), new Color(.12f, .17f, .2f), holder);
            BuildWallSide(room, holder, wallList, true, -1); BuildWallSide(room, holder, wallList, true, 1);
            BuildWallSide(room, holder, wallList, false, -1); BuildWallSide(room, holder, wallList, false, 1);
        }

        void BuildWallSide(DungeonRoomGeometry room, Transform parent, List<Renderer> wallList, bool eastWest, int sign)
        {
            // eastWest fixes X and cuts intervals along Z; northSouth fixes Z and cuts along X.
            float low = (eastWest ? room.center.z - room.depth * .5f : room.center.x - room.width * .5f);
            float high = low + (eastWest ? room.depth : room.width);
            var openings = new List<Vector2>();
            foreach (var p in passages.Values.Where(p => p.geometry.roomA == room.id || p.geometry.roomB == room.id))
            {
                Vector3 direction = rooms[p.Other(room.id)].center - room.center;
                if (eastWest ? Mathf.Abs(direction.x) < .01f || Mathf.Sign(direction.x) != sign : Mathf.Abs(direction.z) < .01f || Mathf.Sign(direction.z) != sign) continue;
                float middle = eastWest ? p.geometry.center.z : p.geometry.center.x;
                openings.Add(new Vector2(middle - p.geometry.width * .5f, middle + p.geometry.width * .5f));
            }
            float cursor = low;
            foreach (var opening in openings.OrderBy(v => v.x))
            {
                AddWallInterval(room, parent, wallList, eastWest, sign, cursor, Mathf.Min(high, opening.x));
                cursor = Mathf.Max(cursor, opening.y);
            }
            AddWallInterval(room, parent, wallList, eastWest, sign, cursor, high);
        }

        void AddWallInterval(DungeonRoomGeometry room, Transform parent, List<Renderer> wallList, bool eastWest, int sign, float low, float high)
        {
            if (high - low < .01f) return;
            Vector3 center = eastWest ? new Vector3(room.center.x + sign * room.width * .5f, room.center.y + WallHeight * .5f, (low + high) * .5f) :
                new Vector3((low + high) * .5f, room.center.y + WallHeight * .5f, room.center.z + sign * room.depth * .5f);
            Vector3 size = eastWest ? new Vector3(WallThickness, WallHeight, high - low) : new Vector3(high - low, WallHeight, WallThickness);
            wallList.Add(BattleVisual.Box("Wall " + room.id, center, size, new Color(.2f, .27f, .31f), parent, WallLayer).GetComponent<Renderer>());
        }

        void BuildPassage(Passage p, List<Renderer> wallList)
        {
            var holder = new GameObject("Passage " + p.geometry.id).transform; holder.SetParent(generated, false);
            bool horizontal = Mathf.Abs(p.forward.x) > .5f;
            Vector3 side = Vector3.Cross(Vector3.up, p.forward);
            Vector3 floorSize = horizontal ? new Vector3(p.halfGap * 2, .3f, p.geometry.width) : new Vector3(p.geometry.width, .3f, p.halfGap * 2);
            BattleVisual.Box("Corridor floor", p.geometry.center + Vector3.down * .15f, floorSize, new Color(.16f, .23f, .25f), holder);
            Vector3 railSize = horizontal ? new Vector3(p.halfGap * 2 + WallThickness, WallHeight, WallThickness) : new Vector3(WallThickness, WallHeight, p.halfGap * 2 + WallThickness);
            foreach (int sign in new[] { -1, 1 })
                wallList.Add(BattleVisual.Box("Corridor side", p.geometry.center + side * (sign * p.geometry.width * .5f) + Vector3.up * (WallHeight * .5f),
                    railSize, new Color(.2f, .27f, .31f), holder, WallLayer).GetComponent<Renderer>());
            Vector3 doorSize = horizontal ? new Vector3(.28f, 2.4f, p.geometry.width) : new Vector3(p.geometry.width, 2.4f, .28f);
            p.door = BattleVisual.Box("Door " + p.geometry.id, p.geometry.center + Vector3.up * 1.2f, doorSize, new Color(.78f, .46f, .16f), holder, WallLayer);
            p.collider = p.door.GetComponent<BoxCollider>(); wallList.Add(p.door.GetComponent<Renderer>());
            Vector3 stripSize = horizontal ? new Vector3(.34f, .06f, p.geometry.width) : new Vector3(p.geometry.width, .06f, .34f);
            p.strip = BattleVisual.Box("Closed threshold " + p.geometry.id, p.geometry.center + Vector3.up * .03f, stripSize, new Color(1, .6f, .2f), holder, 0, false);
            ApplyPassage(p, p.geometry.open);
        }

        public void SetPassage(string id, bool open)
        {
            if (!passages.TryGetValue(id, out Passage passage)) throw new ArgumentException("Unknown passage: " + id);
            if (passage.geometry.open == open) return;
            ApplyPassage(passage, open); routeCache.Clear(); Physics.SyncTransforms();
        }

        static void ApplyPassage(Passage p, bool open)
        {
            p.geometry.open = open; p.collider.enabled = !open; p.door.SetActive(!open); p.strip.SetActive(!open);
        }

        public bool IsPassageOpen(string id) => passages.TryGetValue(id, out Passage passage) && passage.geometry.open;

        void BuildCovers()
        {
            if (rooms.TryGetValue("R4", out DungeonRoomGeometry r4))
            {
                Vector3 line = r4.center + new Vector3(0, .65f, 2);
                BattleVisual.Box("R4 left low cover", line + Vector3.left * 5, new Vector3(6, 1.3f, .7f), new Color(.31f, .43f, .43f), generated, WallLayer);
                BattleVisual.Box("R4 right low cover", line + Vector3.right * 5, new Vector3(6, 1.3f, .7f), new Color(.31f, .43f, .43f), generated, WallLayer);
                AddCover("R4_CENTER", line, r4.center + new Vector3(9, .65f, 6), new Vector3(4, 1.3f, .7f));
            }
            if (rooms.TryGetValue("R5", out DungeonRoomGeometry r5))
                AddCover("R5_SHIFT", r5.center + new Vector3(0, .65f, 2), r5.center + new Vector3(0, .65f, 6.5f), new Vector3(7, 1.3f, .9f));
        }

        void AddCover(string id, Vector3 original, Vector3 moved, Vector3 size)
        {
            var view = BattleVisual.Box("Cover " + id, original, size, new Color(.4f, .55f, .53f), generated, WallLayer).transform;
            covers.Add(id, new Cover { view = view, original = original, moved = moved });
        }

        public void SetCover(string id, bool moved)
        {
            if (!covers.TryGetValue(id, out Cover cover)) throw new ArgumentException("Unknown cover: " + id);
            Vector3 destination = moved ? cover.moved : cover.original;
            if (cover.view.position == destination) return;
            cover.view.position = destination; Physics.SyncTransforms();
        }

        /// <summary>Positive margin insets the room: use it for the all-members-safely-inside check. Corridors return null.</summary>
        public string RoomAt(Vector3 point, float margin = 0)
        {
            foreach (var room in rooms.Values)
                if (Mathf.Abs(point.x - room.center.x) <= room.width * .5f - margin && Mathf.Abs(point.z - room.center.z) <= room.depth * .5f - margin)
                    return room.id;
            return null;
        }

        public bool HasRoute(string fromRoom, string toRoom) => Route(fromRoom, toRoom) != null;

        public bool HasRoute(Vector3 from, Vector3 to)
        {
            var fromRooms = EndpointRooms(from); var toRooms = EndpointRooms(to);
            return fromRooms.Any(a => toRooms.Any(b => HasRoute(a, b)));
        }

        /// <summary>Next doorway approach or point inside its far room. Uses a cached room graph, never teleports or scans Physics.</summary>
        public Vector3 NextWaypoint(Vector3 from, Vector3 to)
        {
            if (rooms.Count == 0 || !Finite(from) || !Finite(to)) return from;
            string targetRoom = RoomAt(to);
            Passage targetPassage = targetRoom == null ? PassageAt(to) : null;
            if (targetRoom == null && targetPassage == null)
            {
                // A formation offset can extend just outside an exterior wall. Clamp that
                // intention inside the nearest room instead of leading allies off the floor.
                var nearest = rooms.Values.Select(r => new { room = r, point = ClampInside(r, to) }).OrderBy(r => BattleGeometry.DistanceXZ(r.point, to)).First();
                if (BattleGeometry.DistanceXZ(nearest.point, to) > 4) return from;
                targetRoom = nearest.room.id; to = nearest.point;
            }
            if (targetPassage != null && !targetPassage.geometry.open) return from;
            string sourceRoom = RoomAt(from, .8f);
            Passage sourcePassage = sourceRoom == null ? PassageAt(from, 1f) : null;
            if (sourcePassage != null)
            {
                if (!sourcePassage.geometry.open) return from;
                if (sourcePassage == targetPassage) return to;
                string exit = BestExit(sourcePassage, targetRoom, targetPassage, to);
                return exit == null ? from : sourcePassage.Inside(exit);
            }
            sourceRoom = sourceRoom ?? RoomAt(from);
            if (sourceRoom == null) return from;
            if (sourceRoom == targetRoom) return to;
            if (targetPassage != null && (targetPassage.geometry.roomA == sourceRoom || targetPassage.geometry.roomB == sourceRoom))
                return DoorwayWaypoint(from, sourceRoom, targetPassage, to);
            string routeTarget = targetRoom;
            if (targetPassage != null)
            {
                float a = RouteCost(sourceRoom, targetPassage.geometry.roomA), b = RouteCost(sourceRoom, targetPassage.geometry.roomB);
                routeTarget = a <= b ? targetPassage.geometry.roomA : targetPassage.geometry.roomB;
            }
            string[] path = Route(sourceRoom, routeTarget);
            if (path == null || path.Length == 0) return from;
            Passage next = passages[path[0]];
            return DoorwayWaypoint(from, sourceRoom, next, next.Inside(next.Other(sourceRoom)));
        }

        Vector3 DoorwayWaypoint(Vector3 from, string sourceRoom, Passage p, Vector3 destination)
        {
            Vector3 near = p.Inside(sourceRoom), direction = p.forward * (sourceRoom == p.geometry.roomA ? 1 : -1);
            Vector3 offset = BattleGeometry.Ground(from - near);
            float along = Vector3.Dot(offset, direction);
            float side = Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, direction)));
            // The progress test stays true after crossing the approach point. A distance-only
            // test would send a member back toward the near point on the following frame.
            return along >= -.2f && side <= p.geometry.width * .5f - .6f ? destination : near;
        }

        string BestExit(Passage p, string targetRoom, Passage targetPassage, Vector3 target)
        {
            string best = null; float cost = float.PositiveInfinity;
            foreach (string side in new[] { p.geometry.roomA, p.geometry.roomB })
            {
                float routeCost = targetRoom != null ? RouteCost(side, targetRoom) :
                    Mathf.Min(RouteCost(side, targetPassage.geometry.roomA), RouteCost(side, targetPassage.geometry.roomB));
                if (float.IsPositiveInfinity(routeCost)) continue;
                float score = routeCost + BattleGeometry.DistanceXZ(p.Inside(side), target) * .001f;
                if (score < cost) { cost = score; best = side; }
            }
            return best;
        }

        string[] EndpointRooms(Vector3 p)
        {
            string room = RoomAt(p); if (room != null) return new[] { room };
            Passage passage = PassageAt(p);
            return passage != null && passage.geometry.open ? new[] { passage.geometry.roomA, passage.geometry.roomB } : Array.Empty<string>();
        }

        Passage PassageAt(Vector3 point, float roomOverlap = 0)
        {
            foreach (var p in passages.Values)
            {
                Vector3 delta = BattleGeometry.Ground(point - p.geometry.center);
                if (Mathf.Abs(Vector3.Dot(delta, p.forward)) <= p.halfGap + roomOverlap &&
                    Mathf.Abs(Vector3.Dot(delta, Vector3.Cross(Vector3.up, p.forward))) <= p.geometry.width * .5f - .2f) return p;
            }
            return null;
        }

        string[] Route(string from, string to)
        {
            if (from == null || to == null || !rooms.ContainsKey(from) || !rooms.ContainsKey(to)) return null;
            if (from == to) return Array.Empty<string>();
            string key = from + ">" + to;
            if (routeCache.TryGetValue(key, out string[] cached)) return cached;
            var distance = rooms.Keys.ToDictionary(id => id, id => float.PositiveInfinity);
            var previousRoom = new Dictionary<string, string>(); var previousPassage = new Dictionary<string, string>();
            var pending = new HashSet<string>(rooms.Keys); distance[from] = 0;
            while (pending.Count > 0)
            {
                string current = pending.OrderBy(id => distance[id]).ThenBy(id => id, StringComparer.Ordinal).First();
                pending.Remove(current); if (float.IsPositiveInfinity(distance[current]) || current == to) break;
                foreach (Passage edge in passages.Values.Where(p => p.geometry.open && (p.geometry.roomA == current || p.geometry.roomB == current)).OrderBy(p => p.geometry.id, StringComparer.Ordinal))
                {
                    string next = edge.Other(current); if (!pending.Contains(next)) continue;
                    float cost = distance[current] + BattleGeometry.DistanceXZ(rooms[current].center, edge.geometry.center) + BattleGeometry.DistanceXZ(edge.geometry.center, rooms[next].center);
                    if (cost >= distance[next]) continue;
                    distance[next] = cost; previousRoom[next] = current; previousPassage[next] = edge.geometry.id;
                }
            }
            if (!previousRoom.ContainsKey(to)) { routeCache[key] = null; return null; }
            var path = new List<string>(); string cursor = to;
            while (cursor != from) { path.Add(previousPassage[cursor]); cursor = previousRoom[cursor]; }
            path.Reverse(); return routeCache[key] = path.ToArray();
        }

        float RouteCost(string from, string to)
        {
            string[] path = Route(from, to); if (path == null) return float.PositiveInfinity;
            float cost = 0; string current = from;
            foreach (string id in path)
            {
                Passage p = passages[id]; string next = p.Other(current);
                cost += BattleGeometry.DistanceXZ(rooms[current].center, p.geometry.center) + BattleGeometry.DistanceXZ(p.geometry.center, rooms[next].center); current = next;
            }
            return cost;
        }

        static Vector3 ClampInside(DungeonRoomGeometry room, Vector3 p) => new Vector3(
            Mathf.Clamp(p.x, room.center.x - room.width * .5f + 1, room.center.x + room.width * .5f - 1), room.center.y,
            Mathf.Clamp(p.z, room.center.z - room.depth * .5f + 1, room.center.z + room.depth * .5f - 1));
        static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z));

        public void Dispose()
        {
            if (generated) { generated.gameObject.SetActive(false); Destroy(generated.gameObject); generated = null; }
            rooms.Clear(); passages.Clear(); covers.Clear(); routeCache.Clear();v12Obstacles.Clear();v12RemovalFlags.Clear();
        }
        void OnDestroy() => Dispose();
    }
}
