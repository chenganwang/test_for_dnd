using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        public bool V13Enabled=>Config.v13!=null;
        public bool V13AssistMode=>V13Enabled&&!V14Trio(State);
        public static bool V13Physical(CombatState s,ActorState a)=>a!=null&&(s.v13==null||V14Trio(s)||a.isEnemy||a.id==s.v13.coreId);
        public static bool V13PartyAlive(CombatState s)=>s.v13==null||V14Trio(s)?s.actors.Any(a=>a.Alive):s.Actor(s.v13.coreId)?.Alive==true;
        public static bool V13OwnedAction(string actor,string action,string tree)=>actor==V12Evan&&action=="v13_evan_correct"||actor==V12Finn&&tree=="guard"&&(action=="v13_finn_shield"||action=="v13_finn_prayer");
        public string HeroBasic(string actor)=>V13Enabled&&actor==V12Finn&&Config.v13.finnTree=="guard"?"v13_finn_shield":V12BasicAction(actor);
        public string HeroKey(string actor)=>V13Enabled&&actor==V12Evan?"v13_evan_correct":V13Enabled&&actor==V12Finn&&Config.v13.finnTree=="guard"?"v13_finn_prayer":V14Enabled&&actor==V12Finn?Config.v14.finnGrowthAction:V12KeyAction(actor);
        public ActorState V13Anchor(CombatState s,ActorState a)=>V13Enabled&&!V14Trio(s)&&!a.isEnemy&&a.id!=s.v13.coreId?s.Actor(s.v13.coreId):a;
        public static bool V13FateBusy(CombatState s)=>s?.v13?.fate!=null&&(s.v13.fate.stage=="Rolled"||s.v13.fate.stage=="Deciding");
        public V13Member Member(CombatState s,string id)=>s.v13?.members.Find(x=>x.actorId==id);
        public static void InitializeV13State(CombatState s,CombatConfig c)
        {
            if(c.v13==null)return;
            if(c.v14!=null&&s.v14==null)s.v14=new V14CombatState{version=c.v14.version,partyCombatMode=c.v14.partyCombatMode};
            if(s.v13==null)s.v13=new V13CombatState{coreId=c.v11.coreActorId,fateRng=RandomStreams.Stream(s.random.masterSeed,"v13/fate/1"),members=s.actors.Select(a=>new V13Member{actorId=a.id,prayers=a.id==V12Finn?c.v13.prayerCapacity:0}).ToList()};
            if(!V14Trio(s))s.activeActorId=s.v13.coreId;
        }
        void ValidateV13(CombatState s)
        {
            ValidateV14Damage(s);
            if(!V13Enabled){Require(s.v13==null,"v13_adapter_missing");return;}
            var v=s.v13;Require(Config.v13.version=="v13-core-assist-1"&&v!=null&&v.version==Config.v13.version&&v.coreId==Config.v11.coreActorId&&(V14Trio(s)||s.activeActorId==v.coreId),"v13_rules_or_core_invalid");
            if(Config.v14!=null)Require(s.v14!=null&&s.v14.version==Config.v14.version&&s.v14.version=="v14-dual-builds-1"&&s.v14.partyCombatMode==Config.v14.partyCombatMode&&(s.v14.partyCombatMode==PartyCombatMode.PersistentTrio||s.v14.partyCombatMode==PartyCombatMode.CoreAndAssist),"v14_mode_snapshot_invalid");
            else Require(s.v14==null,"v14_rules_missing");
            Require(new[]{"growth","guard"}.Contains(Config.v13.finnTree)&&new[]{Config.v13.actionSeconds,Config.v13.inputDebounce,Config.v13.threatGap,Config.v13.coreHealthScale,Config.v13.fateInterval,Config.v13.fateWarning,Config.v13.fateDecision}.All(x=>Finite(x)&&x>0),"v13_tree_or_limits_unknown");
            Require(v.members.Count==3&&v.members.Select(x=>x.actorId).SequenceEqual(s.actors.Select(a=>a.id))&&v.members.All(m=>m.prayers>=0&&m.prayers<=Config.v13.prayerCapacity),"v13_members_invalid");
            Require(v.fateRng!=null&&(v.fateRng.increment&1)==1&&v.fateCount>=0&&v.fateCount<=3&&v.history.Select(x=>x.id).Distinct().Count()==v.history.Count,"v13_fate_invalid");
            Require(v.echo==null||v.echo.raw>=1&&v.echo.raw<=20&&s.actors.Any(a=>a.id==v.echo.actorId),"v13_echo_invalid");
            Require(new[]{v.combatSinceFate,v.enemyRestUntil,v.ruleUntil}.All(x=>Finite(x)&&x>=0)&&v.activeRule>=0&&v.activeRule<=12&&v.members.All(m=>new[]{m.actionUntil,m.inputUntil,m.appearanceUntil,m.correctionUntil,m.assistRewardUntil}.All(x=>Finite(x)&&x>=0)),"v13_timers_invalid");
            Require(v.terrain.All(t=>new[]{"cover","heal","lightning"}.Contains(t.kind)&&new[]{t.x,t.z,t.warning,t.remaining,t.nextTick}.All(Finite)&&t.warning>=0&&t.remaining>=0)&&v.terrain.Select(t=>t.id).Distinct().Count()==v.terrain.Count,"v13_terrain_invalid");
            if(v.boss!=null)Require(s.enemies.Any(e=>e.id==v.boss.actorId)&&new[]{"Recovery","Warning","Charge","Ritual","Pulse"}.Contains(v.boss.stage)&&(v.boss.phase==1||v.boss.phase==2)&&new[]{v.boss.remaining,v.boss.dx,v.boss.dz,v.boss.travel,v.boss.breakImmunity,v.boss.arenaX,v.boss.arenaZ}.All(Finite),"v13_boss_snapshot_invalid");
            if(v.fate!=null)Require(v.fate.encounterId==s.encounterId&&new[]{"Scheduled","Rolled","Deciding","Applied"}.Contains(v.fate.stage)&&(v.fate.stage=="Scheduled"||v.fate.original>=1&&v.fate.original<=100&&v.fate.final>=1&&v.fate.final<=100),"v13_fate_context_invalid");
        }
        void V13BeforeCommand(CombatState s,CombatCommand c)
        {
            if(!V13Enabled)return;
            V14BeforeCoreCommand(s,c);
            bool fate=c.kind==CommandKind.V13FateReveal||c.kind==CommandKind.V13FateAccept||c.kind==CommandKind.V13FateReplace;
            if(V13FateBusy(s))Require(fate||c.kind==CommandKind.Pause||c.kind==CommandKind.Resume,"fate_decision_only");
            if(s.v13.fate?.stage=="Scheduled"&&s.v13.fate.warning<=0)
                Require(c.kind!=CommandKind.StartBasicAttack&&c.kind!=CommandKind.BasicAttack&&c.kind!=CommandKind.ConfirmRoll&&c.kind!=CommandKind.RequestBindRadiance&&(c.kind!=CommandKind.RequestHeroAction||c.actionId=="v13_evan_correct"),"fate_waiting_current_action");
            if(c.kind==CommandKind.SwitchActive&&!V14Trio(s))Require(c.actorId==s.v13.coreId,"core_locked_for_run");
            if(c.kind==CommandKind.StartBasicAttack||c.kind==CommandKind.BasicAttack||c.kind==CommandKind.StartDodge||c.kind==CommandKind.StartGuard||c.kind==CommandKind.IssueBasicOrder)Require(V13Physical(s,s.Actor(c.actorId)),"logical_member_has_no_body");
            if(c.kind==CommandKind.StartBasicAttack||c.kind==CommandKind.BasicAttack)Require(Member(s,c.actorId)==null||Member(s,c.actorId).actionUntil<=s.simulationSeconds,"major_unavailable");
            if(c.kind==CommandKind.IncomingAttack||c.kind==CommandKind.IncomingSave)Require(V13Physical(s,s.Actor(c.targetId)),"logical_member_cannot_be_hit");
            if(c.kind==CommandKind.IncomingBatch)Require(c.incomingContacts.All(x=>V13Physical(s,s.Actor(x.targetId))),"logical_member_cannot_be_hit");
            if(c.kind==CommandKind.SelectSmiteOvercharge&&!V14Trio(s))Require(s.pendingRoll?.actorId==s.v13.coreId,"assist_full_smite_unprepared");
            if(!V14Trio(s))foreach(var a in s.actors.Where(a=>a.id!=s.v13.coreId)){var core=s.Actor(s.v13.coreId);a.x=core.x;a.z=core.z;a.heightY=core.heightY;}
        }
        void V13AfterCommand(CombatState s,CombatCommand c,CombatResult r)
        {
            if(!V13Enabled)return;
            foreach(var ally in s.actors){int spent=State.Actor(ally.id).spellSlots-ally.spellSlots;for(int i=0;i<spent;i++)s.v13.receipts.Add("spell-spent/"+ally.id+"/"+c.id+"/"+i);}
            var a=s.Actor(c.actorId);var m=Member(s,c.actorId);
            if(m!=null&&(c.kind==CommandKind.RequestHeroAction||c.kind==CommandKind.RequestBindRadiance||c.kind==CommandKind.ConfirmRoll))
            {m.appearanceUntil=s.simulationSeconds+1.1f;m.inputUntil=s.simulationSeconds+Config.v13.inputDebounce;}
            foreach(var t in s.rolls.Where(t=>t.stage=="Resolved"&&t.rollType=="Attack"&&t.chargeSpent>0&&t.purpose=="Tactical"))
                if(!s.v13.receipts.Contains("echo/"+t.id)){s.v13.receipts.Add("echo/"+t.id);s.v13.echo=new V13Echo{id=t.id,actorId=t.actorId,raw=t.selectedFace};}
            V14AfterCoreCommand(s);
        }
        void TickV13(CombatState s,float dt)
        {
            if(!V13Enabled||dt<=0)return;
            if(!V14Trio(s))foreach(var a in s.actors.Where(a=>a.id!=s.v13.coreId)){var core=s.Actor(s.v13.coreId);a.x=core.x;a.z=core.z;a.heightY=core.heightY;}
            foreach(var actor in s.actors.Concat(s.enemies))if(actor.p6.v13FateShield>0&&actor.p6.v13FateShieldUntil<=s.simulationSeconds){actor.p6.absorptionShield-=actor.p6.v13FateShield;actor.p6.v13FateShield=0;}
            if(!s.inCombat||!V13PartyAlive(s)||!s.enemies.Any(a=>a.Alive))return;
            if(!V13FateBusy(s)&&HasP4Threat(s))s.v13.combatSinceFate+=dt;
            bool scheduledNow=false;
            if(s.v13.fate==null&&!s.v13.timeUsed&&s.v13.fateCount<3&&s.v13.combatSinceFate>=Config.v13.fateInterval-Config.v13.fateWarning)
            {s.v13.fate=new V13FateEvent{id=s.modeInstanceId+"/fate/time",encounterId=s.encounterId,trigger="time",warning=Math.Max(0,Config.v13.fateInterval-s.v13.combatSinceFate)};scheduledNow=true;}
            if(s.v13.fate?.stage=="Scheduled"&&!scheduledNow){s.v13.fate.warning=Down(s.v13.fate.warning,dt);s.v13.fate.age+=dt;}
            foreach(var terrain in s.v13.terrain){if(terrain.warning>0)terrain.warning=Down(terrain.warning,dt);else terrain.remaining=Down(terrain.remaining,dt);}
        }
        bool TryV13Command(CombatState s,CombatCommand c,out CombatResult result)
        {
            result=null;if(!V13Enabled)return false;
            if(TryV13FateCommand(s,c,out result)||TryV13BossCommand(s,c,out result))return true;
            if(TryV14Correction(s,c,out result))return true;
            if(c.kind==CommandKind.RequestHeroAction&&c.actionId=="v13_evan_correct")
            {
                var evan=Live(s,V12Evan);Require(c.actorId==evan.id&&Config.AllowsAction(evan,c.actionId),"ability_locked_or_wrong_character");
                Require(evan.inspiration>0&&evan.reactionRemaining<=0&&evan.windupRemaining<=0&&!V12ActionActive(evan)&&string.IsNullOrEmpty(evan.reactionHeldBy),"correction_resource_or_reaction_unavailable");
                var t=s.pendingRoll;
                if(t!=null)
                {
                    Require(s.mode==CombatMode.PostRollDecision&&s.actors.Any(x=>x.id==t.rollOwnerId)&&!t.modifyUsed&&!t.precisionUsed&&(t.rollType=="Attack"||t.rollType=="Defense"||t.rollType=="Check"),"correction_ineligible_roll");
                    t.flatBonus+=2;t.modifyUsed=true;t.paidResourceIds.Add("v13-correction/"+c.id);Evaluate(s,t);result=TicketResult(t);
                }
                else
                {
                    Require(SimulationMode(s)&&s.activePendingEvent==null&&s.actors.Any(x=>x.id==c.targetId),"correction_party_target_required");
                    var m=Member(s,evan.id);Require(m.correctionUntil<=s.simulationSeconds,"correction_already_prepared");m.correctionTarget=c.targetId;m.correctionUntil=s.simulationSeconds+8;result=new CombatResult();
                }
                evan.inspiration--;evan.reactionRemaining=Config.reactionRecovery;return true;
            }
            return false;
        }
        static void V13ConsumeShield(ActorState a,int amount)
        {int fate=Math.Min(amount,a.p6.v13FateShield);a.p6.v13FateShield-=fate;if(a.v11!=null)a.v11.shieldGranted=Math.Max(0,a.v11.shieldGranted-(amount-fate));}
        int V13OwnSaveBonus(CombatState s,ActorState a)
        {
            if(!V13Enabled||a.isEnemy)return 0;int bonus=0;
            var evan=Member(s,V12Evan);if(evan!=null&&evan.correctionUntil>s.simulationSeconds&&evan.correctionTarget==a.id){bonus+=2;evan.correctionUntil=0;}
            var inspiration=a.statuses.Find(x=>x.id=="v13-inspiration"&&x.remaining>0);if(inspiration!=null){bonus+=2;a.statuses.Remove(inspiration);}return bonus;
        }
        double V13OwnBasicDamage(CombatState s,PendingIncoming incoming,ActorState target)
        {
            var a=s.Actor(incoming.attackerId);
            bool own=V13Enabled&&s.v13.coreId==V12Evan&&a?.id==V12Evan&&V12Passive(a)&&incoming.sourceKind=="Attack"&&incoming.hitBeforeReaction&&a.basicAttackIntent?.rootId==incoming.rootId&&a.basicAttackIntent.actionId==Config.BasicFor(a).id&&target.statuses.Any(x=>x.v12?.family=="opening"&&x.sourceId==a.id&&x.remaining>0);
            return incoming.prospectiveDamage+(own?Config.BasicFor(a).damage*.15:0);
        }
        void V13PrepareRoll(CombatState s,RollTicket t)
        {
            if(!V13Enabled)return;
            var actor=s.Actor(t.rollOwnerId);
            if(actor==null)return;
            if(t.rollType=="Attack"&&s.v13.activeRule==3&&s.v13.ruleUntil>s.simulationSeconds)t.baseBonus--;
            if(!actor.isEnemy&&(t.rollType=="Attack"||t.rollType=="Defense"||t.rollType=="Check"))
            {
                V14PrepareCorrectionTicket(s,t);
                var m=Member(s,V12Evan);if(m!=null&&m.correctionUntil>s.simulationSeconds&&m.correctionTarget==actor.id){t.flatBonus+=2;t.modifyUsed=true;m.correctionUntil=0;t.modifierSources.Add("埃文预校正 +2");}
                var inspiration=actor.statuses.Find(x=>x.id=="v13-inspiration"&&x.remaining>0);if(inspiration!=null){t.baseBonus+=2;actor.statuses.Remove(inspiration);t.modifierSources.Add("灵启 +2");}
                if(t.rollType=="Attack"&&t.actionId!=Config.BasicFor(actor).id)
                {var mark=s.Actor(t.targetId)?.statuses.Find(x=>x.v12?.family=="opening"&&x.sourceId==V12Evan&&x.remaining>0&&x.stacks>0);if(mark!=null){t.advantage=true;mark.stacks=0;}}
            }
        }
    }
}
