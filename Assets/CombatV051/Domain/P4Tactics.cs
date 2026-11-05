using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public enum BasicOrderKind { Move, Hold, FocusTarget, Follow, Prepare, Followup }
    [Serializable] public sealed partial class BasicOrder
    {
        public string id,targetId,protectTargetId,stage="Active",reason,threatId,actionCategory="Attack";
        public BasicOrderKind kind;
        public float x,z,startX,startZ,remaining,elapsed,directionX,directionZ;
        public bool protectionLineOfSight=true;
        public int startingHp;
    }
    [Serializable] public sealed partial class SetupToken
    {
        public string id,ownerId,targetId,actionCategory="Attack",threatId,usedBy,usedRoot;
        public float remaining;
        public bool consumed;
    }
    [Serializable] public sealed partial class BasicAttackIntent
    {
        public string id,targetId,actionId,rootId;
        public float remaining;
        public bool resolved;
    }
    [Serializable] public sealed class SetupUse
    {
        public string root,tokenId,ownerId,consumerId,targetId;
        public bool rewarded;
    }
    public sealed partial class ActorState
    {
        public BasicOrder basicOrder;
        public SetupToken setupToken;
        public BasicAttackIntent basicAttackIntent;
        public float preparationCooldown;
        public bool p4ExposeUsed;
    }
    public sealed partial class CombatCommand
    {
        public bool replaceExisting;
        public string orderId,preparationActionCategory="Attack";
    }
    public sealed partial class CombatResult
    {
        public string orderId,setupTokenId,basicAttackId;
    }
    public sealed partial class CombatState
    {
        public List<string> consumedSetupIds=new List<string>(),knownEnemyInfo=new List<string>();
        public List<SetupUse> setupUses=new List<SetupUse>();
    }
    public sealed partial class CombatSession
    {
        static bool ActiveOrder(ActorState actor)=>actor.basicOrder!=null&&(actor.basicOrder.stage=="Active"||actor.basicOrder.stage=="Preparing"||actor.basicOrder.stage=="Holding"||actor.basicOrder.stage=="WaitingWindow");
        public static bool HasPlayerOrder(ActorState actor)=>actor!=null&&ActiveOrder(actor)&&actor.basicOrder.kind!=BasicOrderKind.Follow;
        public static void ResetP4PreparationState(CombatState s)
        {
            foreach(var a in s.actors.Concat(s.enemies)){a.basicOrder=null;a.setupToken=null;a.basicAttackIntent=null;a.preparationCooldown=0;a.p4ExposeUsed=false;}
            s.consumedSetupIds.Clear();s.knownEnemyInfo.Clear();s.setupUses.Clear();
        }
        public bool CanUseRearPosition(string actorId,string targetId)=>State.Actor(actorId)!=null&&State.Actor(targetId)!=null&&IsBehind(State.Actor(actorId),State.Actor(targetId));
        bool TryP4Command(CombatState s,CombatCommand c,out CombatResult result)
        {
            result=new CombatResult();
            switch(c.kind)
            {
                case CommandKind.IssueBasicOrder:
                case CommandKind.StartPreparation:
                {
                    Require(s.inCombat&&SimulationMode(s)&&s.mode!=CombatMode.PostRollDecision&&s.mode!=CombatMode.ReactionPrompt,"basic_order_unavailable");
                    Require(c.authority==CommandAuthority.Player&&!c.flag,"player_order_required");
                    var a=Live(s,c.actorId);Require(!a.isEnemy,"invalid_party_actor");VoluntaryActionFree(a);
                    Require(a.basicAttackIntent==null||a.basicAttackIntent.resolved,"basic_contact_pending");
                    Require(!ActiveOrder(a)||c.replaceExisting,"basic_order_replacement_required");
                    BasicOrderKind kind;
                    Require(Enum.TryParse(c.kind==CommandKind.StartPreparation?"Prepare":c.actionId,true,out kind),"unknown_basic_order");
                    Require(Finite(c.x)&&Finite(c.z),"invalid_order_destination");
                    var order=new BasicOrder{id=c.id,kind=kind,targetId=c.targetId,x=c.x,z=c.z,startX=a.x,startZ=a.z,startingHp=a.hp};
                    if(kind==BasicOrderKind.Move)Require(c.qualified,"order_destination_unreachable");
                    if(kind==BasicOrderKind.Hold){order.x=a.x;order.z=a.z;order.stage="Holding";}
                    if(kind==BasicOrderKind.FocusTarget){var t=Live(s,c.targetId);Require(t.isEnemy&&c.lineOfSight,"invalid_order_target");a.aiTargetId=t.id;}
                    if(kind==BasicOrderKind.Follow)order.stage="Finished";
                    if(kind==BasicOrderKind.Prepare)
                    {
                        Require(a.preparationCooldown<=0&&!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0,"preparation_unavailable");
                        if(a.role==ActorRole.Fighter)
                        {
                            var ally=Live(s,string.IsNullOrEmpty(c.targetId)?a.id:c.targetId);
                            Require(!ally.isEnemy&&c.lineOfSight&&Distance(a,ally)<=Config.RulesFor(a.id).protectionRange,"protection_target_out_of_range");
                            order.protectTargetId=ally.id;order.stage="Preparing";order.remaining=Config.preparationSeconds;
                            var threat=s.enemies.Where(e=>e.Alive).OrderBy(e=>Distance(a,e)).FirstOrDefault();
                            float dx=threat==null?0:threat.x-a.x,dz=threat==null?1:threat.z-a.z,length=(float)Math.Sqrt(dx*dx+dz*dz);
                            order.directionX=length<.001f?0:dx/length;order.directionZ=length<.001f?1:dz/length;
                        }
                        else
                        {
                            var target=Live(s,c.targetId);Require(target.isEnemy&&Distance(a,target)<=Config.preparationRange&&c.lineOfSight,"preparation_target_invalid");
                            if(a.role==ActorRole.Rogue){Require(c.qualified,"rear_destination_unreachable");order.stage="Active";}
                            else if(a.role==ActorRole.Mage){Require(c.preparationActionCategory=="Attack"||c.preparationActionCategory=="Save","preparation_category_invalid");order.actionCategory=c.preparationActionCategory;order.stage="Preparing";order.remaining=Config.RulesFor(a.id).studySeconds;}
                            else throw new InvalidOperationException("preparation_role_invalid");
                        }
                        a.setupToken=null;a.preparationCooldown=Config.preparationRepeatSeconds;
                    }
                    a.aimPreparing=false;a.aimRemaining=0;a.basicOrder=order;result.orderId=order.id;return true;
                }
                case CommandKind.CancelBasicOrder:
                case CommandKind.CancelPreparation:
                {
                    Require(SimulationMode(s),"world_frozen");var a=Live(s,c.actorId);
                    Require(a.basicOrder!=null&&(string.IsNullOrEmpty(c.orderId)||a.basicOrder.id==c.orderId),"basic_order_missing");
                    a.basicOrder.stage="Canceled";a.basicOrder.reason=c.actionId=="danger"?"emergency_reposition":c.actionId=="route_blocked"?"route_blocked":"player_cancel";
                    if(a.basicOrder.kind==BasicOrderKind.Prepare)a.guardRemaining=0;
                    result.orderId=a.basicOrder.id;return true;
                }
                case CommandKind.CompleteBasicOrder:
                {
                    Require(SimulationMode(s)&&c.authority==CommandAuthority.System,"system_command_required");var a=Live(s,c.actorId);var o=a.basicOrder;
                    Require(o!=null&&o.id==c.orderId&&ActiveOrder(a),"basic_order_missing");
                    Require(DistanceTo(a,o.x,o.z)<=Config.orderArrivalDistance+.001f,"order_not_arrived");
                    if(o.kind==BasicOrderKind.Prepare&&a.role==ActorRole.Rogue)
                    {
                        var target=Live(s,o.targetId);Require(c.lineOfSight&&IsBehind(a,target),"sneak_position_required");
                        if(target.aiState!=1||string.IsNullOrEmpty(target.attackGroupId)){o.stage="WaitingWindow";result.reason="waiting_facing_commitment";return true;}
                        CreateP4Setup(s,a,target,"Attack",target.attackGroupId,Config.RulesFor(a.id).flankWindowSeconds);result.setupTokenId=a.setupToken.id;
                    }
                    // Arrival keeps the actor at the requested position. Default AI must not
                    // immediately walk away or consume the newly prepared attack on its own.
                    o.kind=BasicOrderKind.Hold;o.stage="Holding";result.orderId=o.id;return true;
                }
                case CommandKind.StartBasicAttack:
                {
                    Require(s.inCombat&&SimulationMode(s)&&(IsAI(c)||NormalMode(s)),"basic_attack_not_allowed");
                    var a=Live(s,c.actorId);var target=Live(s,c.targetId);Require(c.authority!=CommandAuthority.Player||!a.isEnemy,"invalid_party_actor");MajorFree(a);
                    Require(!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0&&!PreparingP4(a),"actor_busy");
                    Require(a.basicAttackIntent==null||a.basicAttackIntent.resolved,"basic_contact_pending");
                    var basic=Config.BasicFor(a);Require(target.isEnemy!=a.isEnemy&&c.lineOfSight&&Distance(a,target)<=basic.range+.001f,"target_out_of_range");
                    a.basicAttackIntent=new BasicAttackIntent{id=c.id,targetId=target.id,actionId=basic.id,rootId=c.id,remaining=Config.basicContactSeconds,v14PlayerCommand=V14Enabled&&c.authority==CommandAuthority.Player};
                    a.windupRemaining=Config.basicContactSeconds;result.basicAttackId=c.id;return true;
                }
                case CommandKind.ResolveBasicAttack:
                {
                    Require(SimulationMode(s)&&c.authority==CommandAuthority.System,"system_command_required");var a=Live(s,c.actorId);var intent=a.basicAttackIntent;
                    Require(intent!=null&&!intent.resolved&&intent.id==c.orderId&&intent.remaining<=0,"basic_contact_not_ready");
                    intent.resolved=true;a.windupRemaining=0;a.majorRemaining=Config.majorRecovery;
                    var target=s.Actor(intent.targetId);var basic=Config.BasicFor(a);
                    result.basicAttackId=intent.id;
                    if(target==null||!target.Alive||!c.lineOfSight||Distance(a,target)>basic.range+.001f){result.reason="basic_swing_missed";return true;}
                    int hpBefore=target.hp;
                    result=Incoming(s,new CombatCommand{id=intent.id,kind=CommandKind.IncomingAttack,actorId=a.id,targetId=target.id,amount=basic.damage,rootId=intent.rootId,qualified=true,parryable=!basic.ranged,authority=CommandAuthority.System});
                    V11OnBasicHpDamage(s,a,target,intent,basic.id,hpBefore-target.hp,result);
                    result.basicAttackId=intent.id;
                    if(!a.isEnemy)ConsumeP4Setup(s,a,target,"Attack",intent.rootId,result.damage>0);
                    if(result.hit&&!a.isEnemy)
                    {
                        AwardP4ActionOutcome(s,target,intent.rootId,null,false,!target.Alive);
                        AwardP4(s,"basic-hit",intent.rootId,"basic-attack",true);
                    }
                    return true;
                }
                case CommandKind.AwardFact:
                {
                    Require(SimulationMode(s)&&s.inCombat,"world_frozen");
                    Require(c.authority==CommandAuthority.System&&c.qualified&&!string.IsNullOrEmpty(c.rootId)&&!string.IsNullOrEmpty(c.factId),"unqualified_world_fact");
                    Require(c.actionId!="refund"&&!c.refundOrigin,"refund_cannot_generate_reward");
                    float amount=c.actionId=="environment"?Config.environmentMomentum:c.actionId=="interrupt"?Config.interruptMomentum:c.actionId=="threat-kill"?Config.threatKillMomentum:-1;
                    Require(amount>=0,"unregistered_external_reward_kind");
                    AwardP4Root(s,c.actionId=="interrupt"?"objective-interrupt":c.actionId,c.rootId,amount,true,c.paidChainId,c.refundOrigin);return true;
                }
                default:return false;
            }
        }
        static float DistanceTo(ActorState a,float x,float z){float dx=a.x-x,dz=a.z-z;return(float)Math.Sqrt(dx*dx+dz*dz);}
        static bool PreparingP4(ActorState actor)=>ActiveOrder(actor)&&actor.basicOrder.kind==BasicOrderKind.Prepare;
        static void CancelP4Preparation(ActorState actor,string reason)
        {actor.basicOrder.stage="Canceled";actor.basicOrder.reason=reason;if(actor.role==ActorRole.Fighter)actor.guardRemaining=0;}
        ActorState FindP4PreparedProtector(CombatState s,ActorState source,ActorState target)
        {
            return s.actors.Where(actor=>actor.Alive&&actor.role==ActorRole.Fighter&&actor.guardRemaining>0&&PreparingP4(actor)&&actor.basicOrder.stage=="Holding"&&
                actor.basicOrder.protectTargetId==target.id&&actor.basicOrder.protectionLineOfSight&&Distance(actor,target)<=Config.RulesFor(actor.id).protectionRange&&Math.Abs(actor.heightY-target.heightY)<1&&Math.Abs(actor.heightY-source.heightY)<1)
                .Where(actor=>(source.x-actor.x)*actor.basicOrder.directionX+(source.z-actor.z)*actor.basicOrder.directionZ>=0)
                .OrderBy(actor=>Distance(actor,target)).FirstOrDefault();
        }
        void CancelP4InterruptedPreparation(CombatState s)
        {
            foreach(var actor in s.actors)
            {
                if(!PreparingP4(actor))continue;
                var order=actor.basicOrder;var target=s.Actor(order.targetId);
                if(!actor.Alive||target==null||!target.Alive||actor.staggerRemaining>0||actor.hp<order.startingHp)
                {CancelP4Preparation(actor,!actor.Alive?"actor_down":"preparation_interrupted");}
            }
        }
        void TickP4State(CombatState s,float dt)
        {
            if(dt<=0)return;
            foreach(var a in s.actors.Concat(s.enemies))
            {
                a.preparationCooldown=Down(a.preparationCooldown,dt);
                if(a.setupToken!=null)a.setupToken.remaining=Down(a.setupToken.remaining,dt);
                if(a.basicAttackIntent!=null&&!a.basicAttackIntent.resolved)a.basicAttackIntent.remaining=Down(a.basicAttackIntent.remaining,dt);
                if(!ActiveOrder(a))continue;
                var o=a.basicOrder;o.elapsed+=dt;
                if(!a.Alive){o.stage="Canceled";o.reason="actor_down";continue;}
                if(o.kind!=BasicOrderKind.Prepare)continue;
                var target=s.Actor(o.targetId);
                if(target==null||!target.Alive||a.staggerRemaining>0||a.hp<o.startingHp)
                {CancelP4Preparation(a,"preparation_interrupted");continue;}
                if(a.role==ActorRole.Rogue)
                {if(o.elapsed>Config.orderTimeoutSeconds){o.stage="Canceled";o.reason="flank_window_expired";}continue;}
                if(DistanceTo(a,o.startX,o.startZ)>.05f){CancelP4Preparation(a,"preparation_moved");continue;}
                if(o.stage!="Preparing")continue;
                o.remaining=Down(o.remaining,dt);if(o.remaining>0)continue;
                if(a.role==ActorRole.Fighter)
                {a.guardRemaining=Config.guardSeconds;a.guardElapsed=0;o.stage="Holding";o.remaining=Config.guardSeconds;}
                else
                {
                    if(Distance(a,target)>Config.preparationRange){o.stage="Canceled";o.reason="preparation_target_invalid";continue;}
                    if(!s.knownEnemyInfo.Contains(target.id))s.knownEnemyInfo.Add(target.id);
                    CreateP4Setup(s,a,target,o.actionCategory,o.id,Config.preparationEffectSeconds);o.stage="Finished";
                }
            }
            foreach(var a in s.actors)
                if(ActiveOrder(a)&&a.basicOrder.kind==BasicOrderKind.Prepare&&a.role==ActorRole.Fighter&&a.basicOrder.stage=="Holding"&&a.guardRemaining<=0)
                {a.basicOrder.stage="Finished";a.basicOrder.reason="guard_complete";}
        }
        void CreateP4Setup(CombatState s,ActorState actor,ActorState target,string category,string threat,float duration)
        {
            actor.setupToken=new SetupToken{id=actor.basicOrder.id+"/setup",ownerId=actor.id,targetId=target.id,actionCategory=category,threatId=threat,remaining=duration};
        }
        SetupToken AvailableP4Setup(CombatState s,ActorState consumer,ActorState target,string category)
        {
            return s.actors.Where(a=>a.Alive).Select(a=>a.setupToken).Where(t=>t!=null&&!t.consumed&&t.remaining>0&&t.targetId==target.id&&t.actionCategory==category&&!s.consumedSetupIds.Contains(t.id)).OrderBy(t=>t.ownerId==consumer.id?0:1).ThenBy(t=>t.id,StringComparer.Ordinal).FirstOrDefault();
        }
        bool GetP4SetupAdvantage(CombatState s,ActorState actor,ActorState target,string category)=>AvailableP4Setup(s,actor,target,category)!=null;
        void ConsumeP4Setup(CombatState s,ActorState actor,ActorState target,string category,string root,bool effective,string paidChainId=null)
        {
            if(Config.p5!=null)return;
            var use=s.setupUses.Find(x=>x.root==root);
            if(use==null)
            {
                var token=AvailableP4Setup(s,actor,target,category);if(token==null)return;
                token.consumed=true;token.usedBy=actor.id;token.usedRoot=root;s.consumedSetupIds.Add(token.id);
                use=new SetupUse{root=root,tokenId=token.id,ownerId=token.ownerId,consumerId=actor.id,targetId=target.id};s.setupUses.Add(use);
            }
            if(effective&&!use.rewarded&&use.ownerId!=actor.id&&use.consumerId==actor.id&&use.targetId==target.id)
            {
                use.rewarded=true;
                AwardP4Root(s,"setup",root,Config.flankMomentum,true,paidChainId,false);
                var owner=s.Actor(use.ownerId);
                if(owner!=null&&Config.RulesFor(owner.id).firstExposeExtension&&!owner.p4ExposeUsed&&target.staggerRemaining>0)
                {owner.p4ExposeUsed=true;target.staggerRemaining+=1;}
            }
        }
        void ValidateP4Tactics(CombatState s)
        {
            Require(s.consumedSetupIds!=null&&s.knownEnemyInfo!=null&&s.setupUses!=null,"missing_p4_tactics_state");
            Require(s.consumedSetupIds.Distinct().Count()==s.consumedSetupIds.Count&&s.setupUses.All(u=>u!=null&&!string.IsNullOrEmpty(u.root)&&(s.Actor(u.ownerId)!=null||s.p5!=null&&s.p5.opportunities.Any(t=>t.id==u.tokenId&&t.p5.neutral))&&s.Actor(u.consumerId)!=null&&s.Actor(u.targetId)!=null)&&s.setupUses.Select(u=>u.root).Distinct().Count()==s.setupUses.Count,"invalid_setup_history");
            foreach(var a in s.actors.Concat(s.enemies))
            {
                Require(Finite(a.preparationCooldown)&&a.preparationCooldown>=0,"invalid_preparation_cooldown");
                var o=a.basicOrder;if(o!=null)Require(!string.IsNullOrEmpty(o.id)&&Enum.IsDefined(typeof(BasicOrderKind),o.kind)&&Finite(o.x)&&Finite(o.z)&&Finite(o.remaining)&&o.remaining>=0&&new[]{"Active","Preparing","Holding","WaitingWindow","Finished","Canceled"}.Contains(o.stage),"invalid_basic_order");
                var t=a.setupToken;if(t!=null)Require(t.ownerId==a.id&&!string.IsNullOrEmpty(t.id)&&s.Actor(t.targetId)!=null&&Finite(t.remaining)&&t.remaining>=0&&(t.actionCategory=="Attack"||t.actionCategory=="Save"),"invalid_setup_token");
                var b=a.basicAttackIntent;if(b!=null)Require(!string.IsNullOrEmpty(b.id)&&!string.IsNullOrEmpty(b.targetId)&&Finite(b.remaining)&&b.remaining>=0,"invalid_basic_contact");
            }
        }
    }
}
