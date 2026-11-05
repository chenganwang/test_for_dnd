using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        public string LastTickError {get;private set;}
        static bool IsAI(CombatCommand c){return c.flag&&c.kind!=CommandKind.SelectSmiteOvercharge&&c.kind!=CommandKind.RefillPracticeResources||c.authority==CommandAuthority.AllyAI||c.authority==CommandAuthority.EnemyAI;}
        void ValidateAuthority(CombatState s,CombatCommand c)
        {
            Require(Enum.IsDefined(typeof(CommandAuthority),c.authority),"invalid_authority");
            if(!s.IsPartyP1){if(c.flag&&c.kind!=CommandKind.BasicAttack&&c.kind!=CommandKind.AwardFact&&c.kind!=CommandKind.EndEncounter)throw new InvalidOperationException("ai_precious_resource_forbidden");return;}
            if(IsAI(c))
            {
                Require(c.kind==CommandKind.BasicAttack||c.kind==CommandKind.StartBasicAttack||c.kind==CommandKind.StartGuard||c.kind==CommandKind.EndGuard||c.kind==CommandKind.StartDodge||c.kind==CommandKind.CancelAim||V12Enabled&&c.kind==CommandKind.RequestHeroAction,"ai_precious_resource_forbidden");
                var a=Live(s,c.actorId);Require(c.authority==CommandAuthority.EnemyAI?a.isEnemy:!a.isEnemy,"authority_actor_mismatch");
            }
            if(c.kind==CommandKind.FinalizeIncoming)Require(c.authority==CommandAuthority.System,"system_command_required");
            if(c.authority==CommandAuthority.Player&&(c.kind==CommandKind.BasicAttack||c.kind==CommandKind.StartAim||c.kind==CommandKind.StartGuard||c.kind==CommandKind.StartDodge||c.kind==CommandKind.CancelAim||c.kind==CommandKind.EndGuard))Require(!Live(s,c.actorId).isEnemy,"invalid_party_actor");
        }
        static int EffectiveArmorClass(ActorState a){return a.armorClass+a.statuses.Where(x=>x.remaining>0).Sum(x=>x.armorClassBonus);}
        bool IsBehind(ActorState actor,ActorState target)
        {
            float dx=actor.x-target.x,dz=actor.z-target.z,fx=target.lockedX-target.x,fz=target.lockedZ-target.z;
            double distance=Math.Sqrt(dx*dx+dz*dz),facing=Math.Sqrt(fx*fx+fz*fz);
            return distance>.001&&facing>.001&&(dx*fx+dz*fz)/(distance*facing)<=Config.rogueBackstabDot;
        }
        public CombatResult SwitchActive(string commandId,string actorId){return Execute(new CombatCommand{id=commandId,kind=CommandKind.SwitchActive,actorId=actorId});}
        public CombatResult SelectCommandActor(string commandId,string actorId){return Execute(new CombatCommand{id=commandId,kind=CommandKind.SelectCommandActor,actorId=actorId});}
        bool TryPartyCommand(CombatState s,CombatCommand c,out CombatResult result)
        {
            result=null;
            if(!s.IsPartyP1)
            {
                Require((int)c.kind<(int)CommandKind.SwitchActive,"party_profile_required");return false;
            }
            switch(c.kind)
            {
                case CommandKind.SwitchActive:
                {
                    Require((s.inCombat||!s.encounterResolved&&s.enemies.Count==0)&&SimulationMode(s),"cannot_switch_active");var next=Live(s,c.actorId);Require(!next.isEnemy,"invalid_party_actor");
                    if(s.activeActorId!=next.id){var old=s.Actor(s.activeActorId);if(old!=null){old.aimPreparing=false;old.aimRemaining=0;old.guardRemaining=0;}s.activeActorId=next.id;}
                    result=new CombatResult();return true;
                }
                case CommandKind.SelectCommandActor:
                    Require((s.mode==CombatMode.TacticalFocus||NormalMode(s))&&s.pendingRoll==null&&s.activeReaction==null&&s.activePendingEvent==null,"finish_pending_roll_first");Require(!Live(s,c.actorId).isEnemy,"invalid_party_actor");s.commandActorId=c.actorId;result=new CombatResult();return true;
                case CommandKind.IncomingBatch:result=RecordIncomingBatch(s,c);return true;
                case CommandKind.SetReactionSettings:
                    Require(c.reactionSettings!=null,"reaction_settings_required");ValidateReactionSettings(c.reactionSettings,s);s.reactionSettings=CombatJson.Clone(c.reactionSettings);result=new CombatResult();return true;
                case CommandKind.BeginReaction:result=BeginReaction(s,c);return true;
                case CommandKind.ConfirmReactionRoll:result=ConfirmReaction(s,c);return true;
                case CommandKind.CommitReaction:result=FinishReaction(s,ActiveWindow(s,c),true);return true;
                case CommandKind.DeclineReaction:result=DeclineReactionCommand(s,c);return true;
                case CommandKind.FinalizeIncoming:
                {
                    var e=s.incomingEvents.Find(x=>x.id==c.incomingId);Require(e!=null&&e.stage=="Ready"&&!e.damageApplied,"incoming_not_ready");DrainIncoming(s);result=IncomingResult(e);return true;
                }
                case CommandKind.Commit:case CommandKind.Cancel:
                    if(s.pendingRoll!=null&&s.pendingRoll.purpose=="Reaction"){Require(s.mode==CombatMode.PostRollDecision&&s.pendingRoll.stage=="AwaitDecision","post_roll_required");Require(string.IsNullOrEmpty(c.ticketId)||s.pendingRoll.id==c.ticketId,"pending_ticket_mismatch");result=FinishReaction(s,ActiveWindow(s,c),c.kind==CommandKind.Commit);return true;}
                    return false;
                default:return false;
            }
        }
        static void BreakConcentration(CombatState s,ActorState owner)
        {
            if(string.IsNullOrEmpty(owner.concentrationId))return;string id=owner.concentrationId;
            foreach(var target in s.actors.Concat(s.enemies))target.statuses.RemoveAll(x=>x.sourceId==owner.id&&x.concentrationId==id);
            owner.concentrationId=null;
        }
        static void CleanupConcentration(CombatState s)
        {
            foreach(var owner in s.actors.Concat(s.enemies))
            {
                if(string.IsNullOrEmpty(owner.concentrationId))continue;
                if(!owner.Alive)BreakConcentration(s,owner);
                else if(!s.actors.Concat(s.enemies).Any(a=>a.statuses.Any(x=>x.sourceId==owner.id&&x.concentrationId==owner.concentrationId&&x.remaining>0)))owner.concentrationId=null;
            }
        }
    }
}
