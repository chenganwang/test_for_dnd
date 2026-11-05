using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        V12CommandData v12Spatial;
        public bool V12BuildEnabled=>V12BuildEnabledFor(Config);
        static bool V12BuildEnabledFor(CombatConfig c)=>c?.v12?.build?.version=="v12-build-1"&&c.v11?.version=="v12-character-1"&&c.v11.v12BuildVersion=="v12-build-1";
        static bool V12HasBuff(CombatConfig c,string id)=>V12BuildEnabledFor(c)&&V11BuffCatalog.Stacks(c.v11,id)>0;
        bool V12Form(string id,string actorId)=>V12HasBuff(Config,id)&&Config.v11.coreActorId==actorId;
        bool V12FormUp(string id,string actorId)=>V12Form(id,actorId)&&V12HasBuff(Config,id+"_up");
        bool V12BuildLine(string from,string to)=>v12Spatial?.links?.Any(x=>x!=null&&x.fromId==from&&x.toId==to&&x.lineOfSight)==true;
        static double V12HealingScale(CombatConfig c)=>V12HasBuff(c,"v12_team_life")?c.v12.build.healingMultiplier:1;
        public static int V12HealExisting(CombatState s,CombatConfig c,ActorState target,double nominalHealing)
        {
            if(target==null||!target.Alive||!V13Physical(s,target)||target.isEnemy||!s.actors.Contains(target)||nominalHealing<=0)return 0;
            if(target.hp>=target.maxHp){if(target.v12!=null)target.v12.buildHealingRemainder=0;return 0;}
            if(!V12BuildEnabledFor(c)){int old=Math.Min(target.maxHp-target.hp,(int)Math.Floor(nominalHealing));target.hp+=old;return old;}
            int whole=V12AccumulateHealing(ref target.v12.buildHealingRemainder,nominalHealing*V12HealingScale(c)),heal=Math.Min(target.maxHp-target.hp,whole);
            target.hp+=heal;if(target.hp>=target.maxHp)target.v12.buildHealingRemainder=0;return heal;
        }
        static int V12AccumulateHealing(ref double remainder,double amount)
        {
            // Mono may retain a float product at a different precision than CoreCLR. Keep
            // fractional HP on a one-millionth-HP grid before flooring whole HP, so e.g.
            // 25 * (22 * .02f) yields 11 on both runtimes, never one HP per small trigger.
            const double unitsPerHp=1000000;
            double units=Math.Round(remainder*unitsPerHp,MidpointRounding.ToEven)+Math.Round(amount*unitsPerHp,MidpointRounding.ToEven);
            int whole=(int)Math.Floor(units/unitsPerHp);remainder=(units-whole*unitsPerHp)/unitsPerHp;return whole;
        }
        public static void InitializeV12BuildEncounter(CombatState s,CombatConfig c,string stableEncounterRoot)
        {
            if(!V12HasBuff(c,"v12_team_steadfast")||!s.inCombat||string.IsNullOrWhiteSpace(stableEncounterRoot))return;
            string id="v12/steadfast/"+stableEncounterRoot;if(s.world.strings.ContainsKey(id))return;s.world.strings[id]="1";
            foreach(var a in s.actors.Where(x=>x.Alive&&V13Physical(s,x)))V11GrantShield(a,(int)Math.Floor(a.maxHp*c.v12.build.shieldFraction+1e-6),c.v12.build.shieldSeconds);
        }
        public static void ApplyV12BuildReward(CombatState s,CombatConfig c,string rewardRoot,string selectionId)
        {
            if(!V12BuildEnabledFor(c)||string.IsNullOrWhiteSpace(rewardRoot)||selectionId!="v12_team_leeway"||!V12HasBuff(c,selectionId))return;
            var b=s.focusBudget;var p=b?.p6;if(p==null)return;
            // Capacity is a separate legal run bonus, not a rewrite of the saved P6 rules.
            p.capacityBonus=c.v12.build.focusCapacity;b.maximumSeconds=p.rules.maximumSeconds+p.capacityBonus;
            CreditP6Focus(s,"v12-capacity/"+rewardRoot,s.modeInstanceId,"v12-capacity",c.v12.build.focusRecovery,1,true);
        }
        public static float ApplyV12ExplorationReward(CombatState s,CombatConfig c,string registeredObjectiveRoot)
        {
            if(!V12HasBuff(c,"v12_team_pathfinder")||string.IsNullOrWhiteSpace(registeredObjectiveRoot)||s.inCombat||s.mode!=CombatMode.RealtimeCombat)return 0;
            return CreditP6Focus(s,"v12-exploration/"+registeredObjectiveRoot,s.modeInstanceId,"v12-exploration",c.v12.build.explorationFocus,c.v12.build.explorationLimit,true);
        }
        float V12DodgeRecovery()=>V12HasBuff(Config,"v12_team_lightfoot")?Math.Max(Config.v12.build.minimumDodgeCooldown,Config.dodgeRecovery*Config.v12.build.dodgeCooldownMultiplier):Config.dodgeRecovery;
        float V12FinalCoefficient(ActorState actor,ActorState target)
        {return Config.v12.finalCoefficient*(V12HasOpening(target)&&V12Form("v12_evan_conclusion",actor.id)?(V12FormUp("v12_evan_conclusion",actor.id)?Config.v12.build.conclusionUpgradeMultiplier:Config.v12.build.conclusionMultiplier):1);}
        void ValidateV12Build(CombatState s)
        {
            ValidateV14Core(s);
            var c=Config.v12?.build;
            if(c==null){Require(Config.v11?.v12BuildVersion==null&&(s.focusBudget?.p6?.capacityBonus??0)==0&&s.actors.All(a=>(a.v12?.buildHealingRemainder??0)==0)&&s.focusBudget?.p6?.rewards.Any(r=>r.category!=null&&r.category.StartsWith("v12-"))!=true,"v12_build_state_without_rules");return;}
            Require(V12BuildEnabled,"invalid_v12_build_version");
            Require(new[]{c.coauthorRange,c.coauthorMultiplier,c.coauthorUpgradeMultiplier,c.conclusionMultiplier,c.conclusionUpgradeMultiplier,c.sweepRange,c.sweepUpgradeRange,c.sweepAngle,c.sweepMultiplier,c.sweepUpgradeMultiplier,c.huntCooldownReduction,c.huntUpgradeRange,c.spreadRange,c.spreadUpgradeRange,c.bloomHealFraction,c.shieldFraction,c.shieldSeconds,c.dodgeCooldownMultiplier,c.minimumDodgeCooldown,c.focusCapacity,c.focusRecovery,c.edgeMomentum,c.healingMultiplier,c.explorationFocus}.All(x=>Finite(x)&&x>0),"invalid_v12_build_numbers");
            Require(c.sweepTargets>=1&&c.sweepTargets<=2&&c.spreadTargets>=1&&c.spreadTargets<=2&&c.explorationLimit>=1&&c.explorationLimit<=2&&c.sweepAngle<=180&&c.dodgeCooldownMultiplier<=1&&c.minimumDodgeCooldown>=.1f&&c.focusCapacity<=15&&c.focusRecovery<=15&&c.bloomHealFraction<=.05f&&c.shieldFraction<=.1f&&c.healingMultiplier<=2,"invalid_v12_build_limits");
            Require(new[]{c.coauthorRange,c.sweepRange,c.sweepUpgradeRange,c.huntUpgradeRange,c.spreadRange,c.spreadUpgradeRange}.All(x=>x<=12)&&new[]{c.coauthorMultiplier,c.coauthorUpgradeMultiplier,c.conclusionMultiplier,c.conclusionUpgradeMultiplier,c.sweepMultiplier,c.sweepUpgradeMultiplier}.All(x=>x<=5)&&c.huntCooldownReduction<=60&&c.shieldSeconds<=60&&c.edgeMomentum<=100&&c.explorationFocus<=15,"invalid_v12_build_bounds");
            Require(s.actors.All(a=>a.v12!=null&&V11Finite(a.v12.buildHealingRemainder)&&a.v12.buildHealingRemainder>=0&&a.v12.buildHealingRemainder<1),"invalid_v12_healing_remainder");
            Require((s.focusBudget?.p6?.capacityBonus??0)==(V12HasBuff(Config,"v12_team_leeway")?c.focusCapacity:0),"invalid_v12_focus_capacity_bonus");
            var rewards=s.focusBudget?.p6?.rewards;
            if(rewards!=null)Require(rewards.Count(r=>r.category=="v12-capacity")<=1&&rewards.Count(r=>r.category=="v12-exploration")<=c.explorationLimit&&rewards.Where(r=>r.category=="v12-capacity").All(r=>V12HasBuff(Config,"v12_team_leeway")&&r.offered==c.focusRecovery)&&rewards.Where(r=>r.category=="v12-exploration").All(r=>V12HasBuff(Config,"v12_team_pathfinder")&&r.offered==c.explorationFocus),"invalid_v12_focus_receipts");
            if(v12Spatial!=null)Require(v12Spatial.links!=null&&v12Spatial.links.All(x=>x!=null&&!string.IsNullOrWhiteSpace(x.fromId)&&!string.IsNullOrWhiteSpace(x.toId)&&s.Actor(x.fromId)!=null&&s.Actor(x.toId)!=null)&&v12Spatial.links.Select(x=>x.fromId+"/"+x.toId).Distinct().Count()==v12Spatial.links.Count,"invalid_v12_spatial_evidence");
        }
    }
}
