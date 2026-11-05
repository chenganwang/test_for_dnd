using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        string v12SupplyId;
        Transform v12PatrolRoot;
        readonly List<ArtActorView> v12PatrolViews=new List<ArtActorView>();
        readonly List<ActorState> v12PatrolPoses=new List<ActorState>();
        string NearbyV12Interaction()
        {
            if(V12BeamAvailable)return "v12-beam";
            if(Profile.IsInEncounter||!Profile.IsSafe)return null;
            if(Profile.PendingReward!=null)return "reward";
            string choice=null;float nearest=float.PositiveInfinity;
            foreach(var point in Profile.ActiveMode.v12Run.points)
            {
                if(point.roomId!=Profile.ActiveRoom.templateId||point.kind=="supply"&&Profile.ActiveMode.usedRestIds.Contains(point.id))continue;
                float distance=BattleGeometry.DistanceXZ(Position(ActiveActor),new Vector3(point.x,0,point.z));
                if(distance<=point.range&&distance<nearest){nearest=distance;choice=point.kind=="hub"?"hub":"supply:"+point.id;}
            }
            foreach(var ev in DungeonConfig.events)
            {
                if(ev.roomId!=Profile.ActiveRoom.templateId||Profile.ActiveMode.events.Find(x=>x.templateId==ev.id)?.stage=="Committed")continue;
                float distance=BattleGeometry.DistanceXZ(Position(ActiveActor),new Vector3(ev.x,0,ev.z));
                if(distance<=ev.range&&distance<nearest){nearest=distance;choice="event:"+ev.id;}
            }
            return choice;
        }
        V12WorldPoint V12SupplyPoint=>Profile?.ActiveMode.v12Run?.points.Find(p=>p.id==v12SupplyId);
        void ClearV12PatrolPresentation()
        {
            if(v12PatrolRoot){v12PatrolRoot.gameObject.SetActive(false);Destroy(v12PatrolRoot.gameObject);}
            v12PatrolRoot=null;v12PatrolViews.Clear();v12PatrolPoses.Clear();
        }
        void ReconcileV12PatrolPresentation()
        {
            if(Profile?.UsesV12Dungeon!=true){if(v12PatrolRoot)v12PatrolRoot.gameObject.SetActive(false);return;}
            var room=Profile.ActiveMode.Room("V12_R3");
            bool visible=!room.resolved&&room.encounterId==null;
            if(!v12PatrolRoot&&visible)
            {
                InitializeArtPresentation();v12PatrolRoot=new GameObject("Registered optional library patrol").transform;v12PatrolRoot.SetParent(DungeonMap.transform,false);
                foreach(var spawn in room.enemySpawns)
                {
                    var holder=new GameObject("Patrol approach "+spawn.id).transform;holder.SetParent(v12PatrolRoot,false);
                    var actor=new ActorState{id=room.id+"/encounter/"+spawn.id,role=ActorRole.Enemy,isEnemy=true,hp=1,maxHp=1,x=spawn.x,z=spawn.z,lockedX=spawn.x-2,lockedZ=spawn.z};
                    var view=ArtActorView.Attach(holder,artResources,ActorRole.Enemy,spawn.templateId,actor.id);
                    v12PatrolPoses.Add(actor);v12PatrolViews.Add(view);
                }
            }
            if(v12PatrolRoot)v12PatrolRoot.gameObject.SetActive(visible);
            AdvanceV12PatrolPresentation();
        }
        void AdvanceV12PatrolPresentation()
        {
            if(!v12PatrolRoot||!v12PatrolRoot.gameObject.activeSelf||Profile?.UsesV12Dungeon!=true)return;
            var room=Profile.ActiveMode.Room("V12_R3");
            for(int i=0;i<v12PatrolViews.Count;i++)
            {
                var actor=v12PatrolPoses[i];var spawn=room.enemySpawns[i];
                actor.x=spawn.x+Mathf.Sin(State.simulationSeconds*.5f)*.8f;actor.z=spawn.z;actor.lockedX=actor.x-1;
                v12PatrolViews[i].transform.position=new Vector3(actor.x,.8f,actor.z);
                v12PatrolViews[i].Apply(actor,false,false,false,IsFrozen,State.simulationSeconds);
            }
        }
        string V12DungeonSummary()
        {
            var purpose=Profile.ActiveV12Purpose;string interaction=NearbyDungeonInteraction();
            string objective=Profile.ActiveRoom.resolved?"此房已完成，沿亮色门口前往下一房。":purpose.objective;
            if(Profile.ActiveRoom.templateId=="V12_R3")objective="北侧主门可直接通行；西侧读书页开小回环，右下巡逻可绕行。";
            return "探索 · "+purpose.name+"  "+Mathf.FloorToInt(Profile.ActiveMode.elapsedRealSeconds/60)+":"+Mathf.FloorToInt(Profile.ActiveMode.elapsedRealSeconds%60).ToString("00")+"\n"+objective+"\n"+(interaction==null?"WASD移动 · 1/2/3切人 · Q关键行动 · E交互":"E · "+InteractionName(interaction))+"\n"+BuildDescription;
        }
    }
}
