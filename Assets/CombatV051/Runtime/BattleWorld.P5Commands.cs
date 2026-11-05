using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        public bool P5Enabled => Session!=null && Session.Config.p5!=null && Session.Config.p6==null;
        // A UI selection is not a reservation. Confirm/start revalidates it in the domain transaction.
        public string SelectedOpportunity {get;private set;}
        bool legacyTestRules;
        public void UseP5RulesForTests() { legacyTestRules=false; }
        public List<P5OpportunityQuote> OpportunityChoices(string actionId=null)
        {
            if(!P5Enabled)return new List<P5OpportunityQuote>();
            return Session.GetOpportunityChoices(SelectedActor,SelectedTarget,actionId??SelectedAction).ToList();
        }
        public void ChooseOpportunity(string id)
        {
            if(!P5Enabled||Menu||IsFrozen||State.activePendingEvent!=null||State.pendingRoll!=null)return;
            SelectedOpportunity=SelectedOpportunity==id?null:id;
            Message=SelectedOpportunity==null?"本次不消费机会。":"已选择机会；确认前不占用，起手失败或放弃不返还已消费机会。";
            Hud.RefreshView();
        }
        public ActionQuote SelectedActionQuote()
        {
            if(V13Enabled&&V13NodeAvailable(SelectedTarget))return new ActionQuote{legal=true};
            if(V13Enabled&&SelectedAction=="v13_evan_correct")return new ActionQuote{legal=V14Enabled?Session.QuoteV14Correction(SelectedTarget).legal:State.Actor(CombatSession.V12Evan)?.inspiration>0,rollType="Check"};
            if(V12Enabled&&SelectedAction=="v12_finn_grove")
            {
                var a=State.Actor(SelectedActor);
                return Session.QuoteHero(new CombatCommand{actorId=SelectedActor,targetId=SelectedTarget,actionId=SelectedAction,x=WorldAimPoint.x,z=WorldAimPoint.z,qualified=OrderPointFree(WorldAimPoint),lineOfSight=a!=null&&HasLineOfEffect(CastOrigin(a),WorldAimPoint)});
            }
            if(!P5Enabled)return Session.Quote(SelectedActor,SelectedTarget,SelectedAction);
            var c=new CombatCommand{kind=CommandKind.ConfirmRoll,actorId=SelectedActor,targetId=SelectedTarget,actionId=SelectedAction,
                p5=new P5CommandData{opportunityId=SelectedOpportunity}};
            PopulateP5CommandEvidence(c);return Session.QuoteP5(c);
        }
        public CombatResult IssueFollowupOrder(bool replace=false)
        {
            if(!P5Enabled||Menu||IsFrozen||State.activePendingEvent!=null||State.pendingRoll!=null)
                return new CombatResult{reason="followup_unavailable"};
            // F is explicit authorization. If none is selected, choose the first legal ally opportunity,
            // but never infer this intent for an ordinary attack or for autonomous AI.
            var choices=OpportunityChoices("basic_followup");
            var choice=string.IsNullOrEmpty(SelectedOpportunity)?choices.FirstOrDefault(q=>q.legal):choices.FirstOrDefault(q=>q.id==SelectedOpportunity);
            if(!string.IsNullOrEmpty(SelectedOpportunity)&&(choice==null||!choice.legal))
            {var reason=choice?.reason??"opportunity_expired";Message=BattleHud.HumanReason(reason);return new CombatResult{reason=reason};}
            if(choice==null){Message="没有可接应的队友机会：先守势化解、侧翼命中或凝霜命中。";return new CombatResult{reason="opportunity_missing"};}
            var command=NewOrder(BasicOrderKind.Followup,replace);
            command.p5=new P5CommandData{opportunityId=choice.id};
            var result=ExecuteWorldCommand(command);
            if(result.success)Message="接应已授权：接近目标，替代下一次普攻；起手时消费一次机会，不花战术次数。";
            return result;
        }
        void HandleP5FollowupInput()
        {
            if(!P5Enabled||State.mode!=CombatMode.TacticalFocus&&State.mode!=CombatMode.RealtimeCombat&&State.mode!=CombatMode.QueuedExecution)return;
            if(!IsFocus&&!FreeOrdersVisible)SelectedActor=State.activeActorId;
            RequestBasicOrder(IssueFollowupOrder,"免费接应");Hud.RefreshView();
        }
        public string P5ActionName(string id)
        {
            if(V11CombatEnabled)return V11AbilityCatalog.Name(id);
            if(id=="ability_smite_recharge")return "至圣斩";
            if(id=="ability_bind_radiance")return "定身术 · 裂光协同";
            if(P5Enabled)
            {
                if(id=="shove")return "推撞 · 运动检定";
                if(id=="sneak")return "协同突袭";
                if(id=="control")return "雷鸣冲击 · CON豁免";
                if(id=="basic_followup")return "免费接应";
            }
            return id=="shove"?"推击 / STR Save":id=="sneak"?"绕背突袭":id=="control"?"定身 / WIS Save":id=="attack"?"投枪":id;
        }
        public static string OpportunityName(string kind)=>kind=="off_balance"?"失衡":kind=="exposed"?"破绽":kind=="bound"?"受制":kind;
        public string P5ConeWarning
        {
            get
            {
                if(!P5Enabled||SelectedAction!="control")return "";
                var actor=State.Actor(SelectedActor);var target=State.Actor(SelectedTarget);if(actor==null||target==null)return "";
                var context=State.pendingRoll?.actorId==actor.id&&State.pendingRoll.actionId=="control"?State.pendingRoll.p5:null;
                var direction=context==null?Position(target)-Position(actor):new Vector3(context.directionX,0,context.directionZ);
                var area=AreaShape.Cone(Position(actor),direction,Session.Config.Action("control").range,Session.Config.p5.thunderAngle*.5f);
                var allies=State.actors.Where(a=>a.Alive&&a.id!=actor.id&&BattleGeometry.Contains(area,Position(a))&&HasLineOfEffect(Position(actor),Position(a))).Select(a=>a.role==ActorRole.Fighter?"战士":a.role==ActorRole.Rogue?"游荡者":"法师").ToArray();
                return allies.Length==0?"扇形 3m / 90° · 影响双方":"友伤预警："+string.Join("、",allies);
            }
        }
        void AdvanceP5FollowupOrder(ActorState actor,float dt)
        {
            var order=actor.basicOrder;var target=State.Actor(order.targetId);
            if(target==null||!target.Alive) {CancelOrderInternal(actor,"target_unavailable");return;}
            var opportunity=Session.GetOpportunityChoices(actor.id,target.id,"basic_followup").FirstOrDefault(q=>q.id==order.p5?.opportunityId);
            if(opportunity==null||!opportunity.legal){CancelOrderInternal(actor,"opportunity_expired");return;}
            var range=Session.Config.BasicFor(actor).range;
            float distance=BattleGeometry.DistanceXZ(Position(actor),Position(target));
            if(distance<=range&&HasLineOfEffect(Position(actor),Position(target)))
            {
                // Followup is a basic replacement: unlike a paid tactical action it must
                // wait for the entire ordinary basic recovery, never grant an extra attack.
                if(actor.majorRemaining>0||actor.windupRemaining>0||actor.staggerRemaining>0||!string.IsNullOrEmpty(actor.heldBy)||actor.basicAttackIntent!=null&&!actor.basicAttackIntent.resolved)return;
                var command=new CombatCommand{id=order.id+"/start",kind=CommandKind.StartBasicAttack,actorId=actor.id,targetId=target.id,
                    orderId=order.id,actionId="basic_followup",authority=CommandAuthority.System,p5=CombatJson.Clone(order.p5)};
                var result=ExecuteWorldCommand(command);
                if(!result.success&&result.reason!="major_unavailable")Message="接应等待："+BattleHud.HumanReason(result.reason);
                return;
            }
            var direction=BattleGeometry.Ground(Position(target)-Position(actor)).normalized;
            var destination=Position(target)-direction*Mathf.Max(.6f,range-.2f);
            if(TryAiWalkingWaypoint(actor,destination,dt,out var waypoint))
            {
                var delta=BattleGeometry.Ground(waypoint-Position(actor));MoveActor(actor,delta.normalized*Mathf.Min(delta.magnitude,Presentation.moveSpeed*dt));
            }
            else Message="接应路线受阻；机会尚未消费，可移动或取消指令。";
        }
    }
}
