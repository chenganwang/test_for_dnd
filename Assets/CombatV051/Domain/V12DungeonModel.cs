using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V12RoomPurpose
    {
        public string roomId,name,objective;
        public bool optionalEncounter;
        public float triggerX,triggerZ,triggerRadius;
    }
    [Serializable] public sealed class V12MapObstacle
    {
        public string id,roomId,removedByFlag;
        public float x,z,width,depth,height=1.2f;
        public bool lowCover;
    }
    [Serializable] public sealed class V12WorldPoint
    {
        public string id,roomId,kind,label;
        public float x,z,range=2.5f,healFraction;
    }
    [Serializable] public sealed class V12DungeonSnapshot
    {
        public string version="v12-six-rooms-1",hash;
        public DungeonConfig dungeon;
        public List<V12RoomPurpose> purposes=new List<V12RoomPurpose>();
        public List<V12MapObstacle> obstacles=new List<V12MapObstacle>();
        public List<V12WorldPoint> points=new List<V12WorldPoint>();
        public List<int> layoutSlots=new List<int>();
    }
    public sealed partial class ModeState
    {
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12DungeonSnapshot v12Run;
    }
    public static class V12DungeonDefaults
    {
        public static V12DungeonSnapshot Create()=>CombatJson.Read<V12DungeonSnapshot>(V12DungeonJson.Text);
        public static string SourceJson=>V12DungeonJson.Text;
    }
}
