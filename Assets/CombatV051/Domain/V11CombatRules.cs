using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        public static void InitializeV11State(CombatState s,CombatConfig config,bool newRun=false)
        {
            if(config?.v11==null)return;InitializeV12State(s,config);InitializeV13State(s,config);
            foreach(var actor in s.actors)
            {
                if(actor.v11==null)actor.v11=new V11ActorState();
                var loadout=config.v11.actors.Find(x=>x.actorId==actor.id);
                if(!newRun||actor.v11.passiveApplied||loadout==null)continue;
                actor.v11.passiveApplied=true;if(!loadout.passiveUnlocked||config.v12!=null&&V12Hero(actor.id))continue;
                switch(loadout.passiveId)
                {
                    case "v11_mira_vigor":case "v11_paladin_vigor":actor.maxHp+=4;actor.hp+=4;break;
                    case "v11_evan_insight":case "v11_mage_insight":actor.intelligenceModifier++;break;
                    case "v11_finn_resolve":actor.wisdomModifier++;actor.wisSaveBonus++;break;
                    case "v11_fighter_guard":actor.armorClass++;break;
                    case "v11_rogue_accuracy":actor.attackBonus++;break;
                }
            }
        }
        static string V11PassiveFor(string id)
        {
            switch(id){case "char_story_evan":return "v11_evan_insight";case "char_story_mira":return "v11_mira_vigor";case "char_story_finn":return "v11_finn_resolve";case "fighter":return "v11_fighter_guard";case "mage":return "v11_mage_insight";case "rogue":return "v11_rogue_accuracy";case "char_demo_paladin":return "v11_paladin_vigor";default:return null;}
        }
        bool V11Core(ActorState actor)=>Config.v11!=null&&actor!=null&&actor.id==Config.v11.coreActorId;
        int V11Buff(string id)=>(int)V11BuffCatalog.Value(Config.v11,id);
        void ValidateV11(CombatState s)
        {
            var rules=Config.v11;
            if(rules==null){Require(s.actors.All(a=>a.v11==null)&&s.rolls.All(t=>t.v11==null),"v11_state_without_rules");return;}
            Require(Config.p6!=null&&(rules.version=="v11-character-1"||rules.version=="v12-character-1")&&rules.actors!=null&&rules.actors.Count==3&&rules.actors.All(x=>x!=null)&&rules.actors.Select(x=>x.actorId).Distinct().Count()==3,"invalid_v11_party");
            bool mainTraining=s.p6?.training==true&&s.world.strings.TryGetValue("context",out var context)&&context=="TRAINING"&&s.world.strings.TryGetValue("trainingRoster",out var roster)&&roster=="MAIN_CHARACTERS"&&rules.coreActorId==null&&rules.buffs!=null&&rules.buffs.Count==0;
            Require((mainTraining||(rules.coreActorId==P6CharacterCatalog.PaladinId||Config.v12!=null&&V12Hero(rules.coreActorId))&&rules.actors.Any(x=>x.actorId==rules.coreActorId))&&rules.actors.All(x=>s.actors.Any(a=>a.id==x.actorId)),"invalid_v11_core");
            foreach(var loadout in rules.actors)
            {
                Require(loadout.passiveId==V11PassiveFor(loadout.actorId)&&loadout.passiveId!=null&&loadout.unlockedActionIds!=null&&loadout.unlockedActionIds.Distinct().Count()==loadout.unlockedActionIds.Count&&loadout.unlockedActionIds.All(id=>Config.Action(id)!=null),"invalid_v11_loadout");
                var a=s.Actor(loadout.actorId);Require(a.v11!=null&&a.v11.shieldGranted>=0&&Finite(a.v11.shieldRemaining)&&a.v11.shieldRemaining>=0&&a.p6.absorptionShield>=a.v11.shieldGranted,"invalid_v11_actor");
                ValidateV11Sustain(s,loadout,a);
            }
            Require(rules.buffs!=null&&rules.buffs.All(b=>b!=null)&&rules.buffs.Select(b=>b.id).Distinct().Count()==rules.buffs.Count,"invalid_v11_buffs");
            foreach(var b in rules.buffs)
            {
                var d=V11BuffCatalog.Raw(b.id);Require(d!=null&&V11BuffCatalog.AvailableInRules(rules,b.id)&&b.stacks>=1&&b.stacks<=d.maxStacks&&(d.pool=="Generic"||d.coreActorId==rules.coreActorId)&&d.prerequisites.All(p=>V11BuffCatalog.Stacks(rules,p)>0)&&!d.exclusives.Any(p=>V11BuffCatalog.Stacks(rules,p)>0),"invalid_v11_buff_stack");
                if(b.id=="core_radiant_resonance")Require(rules.actors.Any(a=>a.actorId!=rules.coreActorId&&a.unlockedActionIds.Contains(P6BindId)),"invalid_v11_buff_party");
            }
            foreach(var t in s.rolls.Where(x=>x.v11!=null))Require(t.v11.guardRadiantDice>=0&&t.v11.guardRadiantDice<=1&&Finite(t.v11.startDeadline)&&t.v11.startDeadline>=0&&(!t.v11.customAction||V11AbilityCatalog.IsCustom(t.actionId)&&t.p5==null&&t.p6==null),"invalid_v11_ticket");
        }
        void TickV11(CombatState s,float dt)
        {
            if(Config.v11==null||dt<=0)return;
            foreach(var a in s.actors)
            {
                if(a.v11==null)continue;a.v11.shieldRemaining=Down(a.v11.shieldRemaining,dt);
                if(a.v11.shieldRemaining<=0||!a.Alive){a.p6.absorptionShield-=Math.Min(a.p6.absorptionShield,a.v11.shieldGranted);a.v11.shieldGranted=0;}
            }
            CleanupV11Sustain(s);
        }
        static int V11Absorb(ActorState target,int incoming)
        {
            if(target.v11==null||target.v11.shieldGranted<=0||target.v11.shieldRemaining<=0)return incoming;
            int absorb=Math.Min(incoming,Math.Min(target.v11.shieldGranted,target.p6.absorptionShield));target.p6.absorptionShield-=absorb;target.v11.shieldGranted-=absorb;return incoming-absorb;
        }
        static void V11GrantShield(ActorState actor,int amount,float seconds)
        {
            if(actor.v11==null)return;
            int extra=Math.Max(0,amount-actor.v11.shieldGranted);actor.p6.absorptionShield+=extra;actor.v11.shieldGranted+=extra;actor.v11.shieldRemaining=Math.Max(seconds,actor.v11.shieldRemaining);
        }
        static void V11Status(ActorState target,string id,string source,float seconds,int armor=0)
        {
            var status=target.statuses.Find(x=>x.id==id);
            if(status==null){status=new StatusState{id=id,sourceId=source};target.statuses.Add(status);}status.remaining=Math.Max(status.remaining,seconds);status.armorClassBonus=Math.Max(status.armorClassBonus,armor);
        }
        void V11Defense(CombatState s,PendingIncoming e)
        {
            if(Config.v11==null||V11Buff("core_oath_guard")<=0||e.defenderId!=Config.v11.coreActorId)return;
            var core=s.Actor(e.defenderId);if(core?.Alive==true)V11Status(core,"v11-oath-guard",e.rootId,8);
        }
        int V11ConfirmGuard(ActorState actor)
        {
            if(!V11Core(actor)||V11Buff("core_oath_guard")<=0)return 0;var status=actor.statuses.Find(x=>x.id=="v11-oath-guard"&&x.remaining>0);if(status==null)return 0;actor.statuses.Remove(status);return 1;
        }
        void V11AfterSmite(CombatState s,ActorState actor,ActorState target,P6DamageResult damage)
        {
            if(!V11Core(actor)||damage.actionId!=P6SmiteId||damage.totalDamage<=0)return;
            if(!target.Alive&&V11Buff("core_mercy")>0)V12HealExisting(s,Config,actor,V11Buff("core_mercy"));
            if(V11Buff("core_afterlight")>0)V11Status(actor,"v11-afterlight",damage.rootId,2,V11Buff("core_afterlight"));
        }
        void V11EncounterCleared(CombatState s)
        {
            if(Config.v11==null||!s.enemies.All(a=>!a.Alive)||!s.actors.Any(a=>a.Alive))return;
            string root="v11/clear/"+s.encounterId;if(s.rewardIds.Contains(root))return;s.rewardIds.Add(root);
            int healing=V11Buff("team_mending");foreach(var a in s.actors.Where(a=>a.Alive))V12HealExisting(s,Config,a,healing);
        }
    }
}
