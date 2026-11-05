using System;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        AreaPreview v12GrovePreview;
        void RefreshV12Presentation()
        {
            if(v12GrovePreview==null)
            {
                var node=new GameObject("V12 pooled grove boundary");node.transform.SetParent(transform,false);
                v12GrovePreview=node.AddComponent<AreaPreview>();v12GrovePreview.Color=new Color(.35f,.82f,.50f,.7f);v12GrovePreview.Width=.065f;v12GrovePreview.GroundLift=.055f;
            }
            bool visible=V12Enabled&&!Menu&&State.v12?.groves.Count>0;
            v12GrovePreview.gameObject.SetActive(visible);
            if(visible){var g=State.v12.groves[0];v12GrovePreview.SetShape(AreaShape.Circle(new Vector3(g.x,0,g.z),g.radius));}
        }
        void ObserveV12Command(CombatCommand command,CombatResult result)
        {
            if(!V12Enabled||!result.success||result.duplicate)return;
            if(command.kind==CommandKind.RequestHeroAction)NotifyArtCue(command.actionId=="v12_mira_chase"?"attack":"spell",command.actorId,command.targetId,command.id);
            if(command.kind==CommandKind.StartQueued)
            {
                var queued=State.queue.FirstOrDefault(q=>q.id==command.queueId);var ticket=queued==null?null:State.rolls.FirstOrDefault(t=>t.id==queued.ticketId);
                if(ticket?.v12!=null)NotifyArtCue(ticket.actionId=="v12_mira_break"?"attack":"spell",ticket.actorId,ticket.targetId,command.id);
            }
            if(result.v12==null)return;
            foreach(var effect in result.v12.effects)
            {
                if(effect.kind=="heal"&&effect.amount>0)Feedback?.MicroHeal(Position(State.Actor(effect.targetId)));
                if(effect.kind=="opening"||effect.kind=="parasite")
                {NotifyArtCue("spell",effect.sourceId,effect.targetId,effect.id);Audio?.PlayCue(AudioCue.SpellImpact,Position(State.Actor(effect.targetId)),effect.id,.45f);}
            }
            var recent=result.v12.effects.LastOrDefault();
            if(recent!=null&&!command.flag)
            {
                string title=recent.kind=="opening"?"破绽 · 6秒承伤提高":recent.kind=="parasite"?"寄生 · 6次缓慢伤害":recent.kind=="grove"?"森息 · 区内缠绕 / 治疗":recent.kind=="dot"?"寄生伤害 "+recent.amount:recent.kind=="echo"?"战地旁注 "+recent.amount:recent.kind=="heal"?"森息回复 "+recent.amount:V11AbilityCatalog.Name(recent.actionId)+" · "+recent.amount;
                Message=title;
            }
        }
        public bool V12Enabled => Session?.Config.v11?.version == "v12-character-1" && Session.Config.v12 != null;
        public bool V12FreeAssistance => p4Preferences.v12FreeAssistance;
        public void ToggleV12FreeAssistance()
        {
            var settings=CombatJson.Clone(p4Preferences); settings.v12FreeAssistance=!settings.v12FreeAssistance;
            SaveP4Preferences(settings); Hud.RefreshView();
        }
        public static bool V12Hero(string id) => id=="char_story_evan"||id=="char_story_mira"||id=="char_story_finn";
        public static string V12BasicAction(string id) => id=="char_story_evan"?"v12_evan_mark":id=="char_story_mira"?"v12_mira_chase":id=="char_story_finn"?"v12_finn_seed":null;
        public static string V12KeyAction(string id) => id=="char_story_evan"?"v12_evan_final":id=="char_story_mira"?"v12_mira_break":id=="char_story_finn"?"v12_finn_grove":null;
        public static bool V12ImmediateAction(string id) => id=="v12_evan_mark"||id=="v12_mira_chase"||id=="v12_finn_seed"||id=="v12_finn_grove"||id=="v13_finn_shield"||id=="v13_finn_prayer"||id=="v14_finn_detonate";
        public CombatResult OpenCurrentKeyAction()
        {
            if(!V12Enabled||!V12Hero(State.activeActorId))return OpenSmite();
            if(V13Enabled&&!IsFocus)return V13QuickKey();
            if(Menu||IsFrozen||State.pendingRoll!=null||State.activePendingEvent!=null)return new CombatResult{reason="actor_busy"};
            string actor=State.activeActorId;
            if(State.Actor(actor).guardRemaining>0)Command(CommandKind.EndGuard,actor:actor);
            var result=State.mode==CombatMode.TacticalFocus?new CombatResult{success=true}:Command(CommandKind.EnterFocus,actor:actor);
            if(result.success)
            {
                SelectCommandActor(actor); p6QuickFocus=false; SelectedAction=Session.HeroKey(actor); EnsureV12Target(actor);
                Hud.RefreshView(); RefreshTacticalPreview();
            }
            return result;
        }
        public CombatResult RequestCurrentBasicAction()
        {
            if(!V12Enabled||!V12Hero(State.activeActorId))return RequestRadiance();
            EnsureV12Target(State.activeActorId);
            return RequestHeroAction(State.activeActorId,Session.HeroBasic(State.activeActorId),Session.HeroBasic(State.activeActorId)=="v13_finn_shield"?State.activeActorId:SelectedTarget,CommandAuthority.Player);
        }
        void EnsureV12Target(string actorId)
        {
            if(State.Actor(SelectedTarget)?.Alive==true&&State.Actor(SelectedTarget).isEnemy)return;
            SelectedTarget=State.enemies.Where(e=>e.Alive).OrderBy(e=>BattleGeometry.DistanceXZ(Position(e),WorldAimPoint)).FirstOrDefault()?.id;
        }
        public CombatResult RequestSelectedHeroAction()
        {
            return RequestHeroAction(SelectedActor,SelectedAction,SelectedTarget,CommandAuthority.Player);
        }
        public CombatResult RequestHeroAction(string actorId,string actionId,string targetId,CommandAuthority authority)
        {
            if(!V12Enabled||Menu||IsFrozen||State.pendingRoll!=null||State.activePendingEvent!=null)return new CombatResult{reason="actor_busy"};
            if(V13Enabled&&actionId=="v13_evan_correct")return V13Correct();
            var actor=State.Actor(actorId);if(V13AssistMode&&(actionId=="v13_finn_shield"||actionId=="v13_finn_prayer"))targetId=State.activeActorId;var target=State.Actor(targetId);
            if(actionId=="v12_finn_grove"&&target?.Alive!=true)target=actor;
            if(actor?.Alive!=true||target?.Alive!=true)return new CombatResult{reason="target_unavailable"};
            var c=new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.RequestHeroAction,actorId=actorId,targetId=targetId,actionId=actionId,authority=authority,flag=authority==CommandAuthority.AllyAI};
            var destination=Position(target);
            if(actionId=="v12_finn_grove")
            {
                destination=WorldAimPoint; c.qualified=OrderPointFree(destination)&&HasLineOfEffect(CastOrigin(actor),destination);
            }
            if(actionId=="v12_mira_chase")
            {
                if(!TryV12ChaseDestination(actor,target,out destination))
                {Message="追身斩无法到达：距离过远或墙体挡住路径。";return new CombatResult{reason="v12_chase_path_blocked"};}
                c.qualified=true;
            }
            c.x=destination.x;c.z=destination.z;
            return ExecuteWorldCommand(c);
        }
        bool TryV12ChaseDestination(ActorState actor,ActorState target,out Vector3 destination)
        {
            var start=CastOrigin(actor);var delta=BattleGeometry.Ground(Position(target)-start);
            float reach=Mathf.Max(.65f,Session.Config.BasicFor(actor).range*.72f);
            destination=start+delta.normalized*Mathf.Max(0,delta.magnitude-reach);
            float limit=Session.V12ChaseRange(actor.id);
            if(BattleGeometry.DistanceXZ(start,destination)>limit+.001f)return false;
            var swept=BattleGeometry.MoveAgainstWalls(start,destination-start,.36f,1<<WallLayer);
            return BattleGeometry.DistanceXZ(swept,destination)<.12f&&OrderPointFree(destination)&&HasLineOfEffect(destination,Position(target));
        }
        void AdvanceV12Actions(float dt)
        {
            if(!V12Enabled||dt<=0||IsFrozen)return;
            if(Session.V12TickPending)
                ExecuteWorldCommand(new CombatCommand{id="v12/tick/"+State.modeInstanceId+"/"+State.encounterId+"/"+State.revision,kind=CommandKind.ResolveHeroTick,authority=CommandAuthority.System});
            foreach(string id in State.actors.Select(a=>a.id).ToArray())
            {
                var actor=State.Actor(id);var intent=actor?.v12?.intent;
                if(intent==null||!actor.Alive)continue;
                if(intent.stage=="Moving"&&(!V13Enabled||IsPhysical(actor)))
                {
                    var destination=new Vector3(intent.destinationX,actor.heightY,intent.destinationZ);
                    var delta=BattleGeometry.Ground(destination-Position(actor));
                    MoveActor(actor,delta.normalized*Mathf.Min(delta.magnitude,12f*dt));
                    if(BattleGeometry.DistanceXZ(Position(actor),destination)>Session.Config.orderArrivalDistance&&State.simulationSeconds<intent.deadline)continue;
                    if(intent.remaining>0)continue;
                }
                else if((intent.stage!="Casting"&&intent.stage!="Moving")||intent.remaining>0)continue;
                ExecuteWorldCommand(new CombatCommand{id="resolve/"+intent.id,kind=CommandKind.ResolveHeroAction,actorId=id,targetId=intent.targetId,actionId=intent.actionId,rootId=intent.id,orderId=intent.id,authority=CommandAuthority.System,qualified=V13Enabled&&!IsPhysical(actor)||intent.stage!="Moving"||BattleGeometry.DistanceXZ(Position(actor),new Vector3(intent.destinationX,actor.heightY,intent.destinationZ))<=Session.Config.orderArrivalDistance+.01f});
            }
        }
        bool TryV12AllyAssist(ActorState actor,ActorState target)
        {
            if(!V12Enabled||!V12FreeAssistance||target==null||actor.id==State.activeActorId||actor.id=="char_story_mira"||!V12Hero(actor.id)||CombatSession.HasPlayerOrder(actor)||IsFocus&&SelectedActor==actor.id)return false;
            string action=V12BasicAction(actor.id);
            if(!Session.Quote(actor.id,target.id,action).legal||!HasLineOfEffect(Position(actor),Position(target)))return false;
            return RequestHeroAction(actor.id,action,target.id,CommandAuthority.AllyAI).success;
        }
    }
}
