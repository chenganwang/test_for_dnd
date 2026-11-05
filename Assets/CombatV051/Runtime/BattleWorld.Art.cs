using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        ArtResources artResources;
        ArtEnvironment artEnvironment;
        ArtCueEffects artEffects;
        Transform artGeometry;
        readonly Dictionary<string,ArtActorView> artActors=new Dictionary<string,ArtActorView>();
        readonly Dictionary<AreaPreview,AreaFillOverlay> artFills=new Dictionary<AreaPreview,AreaFillOverlay>();
        readonly HashSet<string> artCueIds=new HashSet<string>();
        readonly Queue<string> artCueOrder=new Queue<string>();
        public ArtResources ArtPalette=>artResources;
        public ArtCueEffects ArtEffects=>artEffects;
        public ArtEnvironment EnvironmentArt=>artEnvironment;

        public void InitializeArtPresentation()
        {
            if(artResources)return;
            var root=new GameObject("P3 original art presentation");root.transform.SetParent(transform,false);
            artResources=root.AddComponent<ArtResources>();
            artEnvironment=root.AddComponent<ArtEnvironment>();artEnvironment.Initialize(artResources);
            artEffects=root.AddComponent<ArtCueEffects>();artEffects.Initialize(artResources);
        }
        public void RefreshArtPresentation()
        {
            InitializeArtPresentation();
            if(Session==null)return;
            bool freeze=IsFrozen;
            var visibleMode=State.mode==CombatMode.CombatPaused?State.resumeMode:State.mode;
            bool roll=visibleMode==CombatMode.RollFreeze;
            bool focus=visibleMode==CombatMode.TacticalFocus||visibleMode==CombatMode.PostRollDecision||visibleMode==CombatMode.ReactionPrompt;
            artResources.SetMode(focus,roll);
            Transform geometry=DungeonMap?DungeonMap.transform:map;
            if(geometry!=artGeometry)
            {
                artGeometry=geometry;artEnvironment.Decorate(geometry);
                if(DungeonMap)CreateArtLandmarks();
            }
            foreach(var actor in State.actors.Concat(State.enemies))
            {
                if(!views.TryGetValue(actor.id,out var view)||!view)continue;
                if(!artActors.TryGetValue(actor.id,out var visual)||!visual)
                {
                    string template=Profile?.EnemyTemplateFor(actor.id)?.id;
                    visual=ArtActorView.Attach(view,artResources,actor.role,template,actor.id);artActors[actor.id]=visual;
                }
                visual.Apply(actor,actor.id==State.activeActorId,actor.id==SelectedTarget&&actor.isEnemy,IsFocus,freeze,State.simulationSeconds);
            }
            foreach(var pair in warnings)EnsureArtFill(pair.Value,true);
            if(targeting)EnsureArtFill(targeting,false);
            if(tacticalPreviewRoot){EnsureArtFill(tacticalCurrent,false);EnsureArtFill(tacticalTarget,false);EnsureArtFill(tacticalRange,false);EnsureArtFill(tacticalDestination,false);}
            foreach(var pair in artFills)if(pair.Key&&pair.Value)pair.Value.Refresh(freeze);
            string nearby=Profile!=null&&!Menu&&!DungeonModal?NearbyDungeonInteraction():null;
            artEnvironment.RefreshLandmarks(nearby,freeze,State.simulationSeconds);
            artEffects.Refresh(State.simulationSeconds,freeze);
        }
        void EnsureArtFill(AreaPreview source,bool danger)
        {
            if(!source||artFills.ContainsKey(source))return;
            var fill=source.GetComponent<AreaFillOverlay>();
            if(!fill){fill=source.gameObject.AddComponent<AreaFillOverlay>();fill.Initialize(source,artResources,danger);}
            artFills[source]=fill;
        }
        void CreateArtLandmarks()
        {
            if(Profile?.UsesV12Dungeon==true)
            {
                foreach(var point in Profile.ActiveMode.v12Run.points)artEnvironment.AddLandmark(point.kind=="hub"?"hub":"supply:"+point.id,new Vector3(point.x,0,point.z),point.kind=="hub"?"hub":"rest");
                foreach(var ev in DungeonConfig.events)artEnvironment.AddLandmark("event:"+ev.id,new Vector3(ev.x,0,ev.z),"event");
                return;
            }
            artEnvironment.AddLandmark("hub",Vector3.zero,"hub");
            artEnvironment.AddLandmark("rest",new Vector3(0,0,22),"rest");
            artEnvironment.AddLandmark("shortcut",new Vector3(0,0,14),"gate");
            artEnvironment.AddLandmark("gate",new Vector3(48,0,8),"gate");
            artEnvironment.AddLandmark("cover",new Vector3(26,0,24),"cover");
            if(DungeonConfig!=null)foreach(var ev in DungeonConfig.events)artEnvironment.AddLandmark("event:"+ev.id,new Vector3(ev.x,0,ev.z),"event");
        }
        public void ResetArtObservation()
        {
            InitializeArtPresentation();artEffects.Clear();artCueIds.Clear();artCueOrder.Clear();
            foreach(var visual in artActors.Values)if(visual)visual.ResetObservation();
        }
        public void EmitImpactArt(string kind,string actorId,float scale,float flinch,string sourceId)
        {
            InitializeArtPresentation();var actor=State.Actor(actorId);if(actor==null)return;
            bool damage=kind=="hit"||kind=="heavy"||kind=="critical"||kind=="skill"||kind=="boss-kill"||kind=="smite"||kind=="smite-full";
            if(damage&&artActors.TryGetValue(actorId,out var view)&&view)view.Cue("hit",State.simulationSeconds,flinch);
            string cue=kind=="reaction"?"parry":kind=="boss-kill"?"critical":kind=="heavy"?"critical":kind=="skill"?"contact":kind=="dodge"?"perfect-dodge":kind;
            artEffects.Play(cue,Position(actor),State.simulationSeconds,scale);
        }
        public void NotifyArtCue(string cue,string actorId=null,string targetId=null,string sourceId=null)
        {
            if(Session==null||string.IsNullOrEmpty(cue))return;
            switch(cue)
            {
                case "hit":case "block":case "parry":case "dodge":case "perfect-dodge":case "momentum":case "dice-earned":case "break":case "telegraph":case "attack":case "spell":case "door":case "interact":case "critical":case "contact":case "deflect":case "miss":break;
                default:return;
            }
            InitializeArtPresentation();
            if(!string.IsNullOrEmpty(sourceId))
            {
                string key=sourceId+"/"+cue+"/"+actorId+"/"+targetId;
                if(!artCueIds.Add(key))return;
                artCueOrder.Enqueue(key);
                while(artCueOrder.Count>512)artCueIds.Remove(artCueOrder.Dequeue());
            }
            var actor=State.Actor(actorId);var target=State.Actor(targetId);
            if(actor!=null&&artActors.TryGetValue(actor.id,out var visual)&&visual)visual.Cue(cue,State.simulationSeconds);
            Vector3 position=Position(target??actor??ActiveActor);
            artEffects.Play(cue,position,State.simulationSeconds);
        }
    }
}
