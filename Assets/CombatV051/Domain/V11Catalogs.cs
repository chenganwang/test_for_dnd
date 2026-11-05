using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V11BuffDefinition
    {
        public string id,name,description,pool,coreActorId;
        public int maxStacks=1;
        public float value;
        public List<string> prerequisites=new List<string>(),exclusives=new List<string>();
    }
    public static class V11BuffCatalog
    {
        static V11BuffDefinition D(string id,string name,string text,float value,int maximum=1,string prerequisite=null,bool generic=false)
        {return new V11BuffDefinition{id=id,name=name,description=text,value=value,maxStacks=maximum,pool=generic?"Generic":"Core",coreActorId=generic?null:P6CharacterCatalog.PaladinId,prerequisites=prerequisite==null?new List<string>():new List<string>{prerequisite}};}
        static V11BuffDefinition V(string id,string name,string text,string core=null,string prerequisite=null,string exclusive=null)
        {return new V11BuffDefinition{id=id,name=name,description=text,pool=core==null?"Generic":"Core",coreActorId=core,prerequisites=prerequisite==null?new List<string>():new List<string>{prerequisite},exclusives=exclusive==null?new List<string>():new List<string>{exclusive}};}
        // Parameter source. Definitions are returned as copies so UI cannot mutate rules.
        static readonly V11BuffDefinition[] Definitions=new[]{
            V("v14_evan_firstproof","初校","每遭遇第一次合法校正免灵感，仍花Reaction；取消不退，不跨房累积。","char_story_evan"),
            V("v14_evan_review","复核","校正可选择+2或强制采用整组新d20；两者共用一次手动干预与费用。","char_story_evan"),
            V("v14_evan_settle","定局","校正将失败改为成功并实际生效后回30战机、2秒Focus；每遭遇最多2次，同根取高。","char_story_evan"),
            V("v14_mira_opening","乘隙","真实避险后4秒内一次手动命中：普攻总倍率1.75，追身斩1.25。空滚不触发，MISS不消费。","char_story_mira"),
            V("v14_mira_breakflow","断敌攻势","真实避险后4秒内，手动断势命中消费强化并额外削韧2；无需其他卡。","char_story_mira"),
            V("v14_mira_press","乘胜","真实避险反击：普攻总倍率2.00，追身/断势1.50；命中后追身剩余CD减2秒，4秒限一次。","char_story_mira"),
            V("v14_finn_quickgrowth","疾生","新附着寄生跳伤间隔×0.75，每跳不降伤；寄生芽可额外附着2.5米内另一合法可见敌人。","char_story_finn"),
            V("v14_finn_densegrowth","浓生","之后附着或刷新的寄生每跳来源伤害×1.25；既有计划不补历史跳伤。","char_story_finn"),
            V("v14_finn_bloom","盛放","爆绽按各自剩余寄生伤害×1.25，最多引爆主目标及附近2.5米内另外两名自有寄生敌人。","char_story_finn"),
            V("v13_finn_shelter","护林","护生护盾提高2%最大生命；不增加持续时间。","char_story_finn",exclusive:"v13_finn_prayer"),
            V("v13_finn_shelter_up","护林·升阶","护生护盾加成由2%替换为4%。","char_story_finn","v13_finn_shelter"),
            V("v13_finn_prayer","惜愿","回春治疗提高4%最大生命；不增祈愿次数。","char_story_finn",exclusive:"v13_finn_shelter"),
            V("v13_finn_prayer_up","惜愿·升阶","回春治疗加成由4%替换为8%。","char_story_finn","v13_finn_prayer"),
            D("core_smite_dice","炽光","核心至圣斩光耀 +1d8；最多2层。原始骰面与暴击条件不变。",1,2),
            D("core_full_dice","决意","全力追加的光耀再 +1d8；需要炽光。",1,1,"core_smite_dice"),
            D("core_smite_accuracy","誓锋校准","仅核心至圣斩命中总加值 +1；最多2层，不改变原始面。",1,2),
            D("core_radiant_resonance","圣痕共振","裂光印对核心至圣斩的光耀倍率由1.5提高至1.75；需要已解锁裂光的队友。",.25f),
            D("core_oath_guard","守誓","核心合格防御后8模拟秒内，下一次确认至圣斩 +1d8光耀；确认即消费，失败或取消不退。",1),
            D("core_devotion","不息祷言","存在有效交战威胁时，全队战机回充每模拟秒 +2；最多2层，满额丢弃。",2,2),
            D("core_mercy","余晖抚慰","核心至圣斩实际击杀目标时自身回复4生命；每次动作一次，不复活。",4),
            D("core_afterlight","誓光余韵","核心至圣斩造成实际伤害后，自身AC +2，持续2模拟秒；刷新不叠加。",2),
            D("team_ward","守望相助","合格防御的战机收益 +5；最多2层，沿用4模拟秒节流及同根去重。",5,2,null,true),
            D("team_reaction","迅捷应对","全队Reaction恢复时长乘0.85；最多2层。独立反应限制不改变。",.85f,2,null,true),
            D("team_mending","战后整备","真正完成一场胜利后，每位存活队员回复2生命；最多2层，每遭遇一次。",2,2,null,true),
            V("v12_evan_coauthor","合著","本局启用旁注；回响波及3米内另一敌人，造成原回响60%伤害，不递归。","char_story_evan",exclusive:"v12_evan_conclusion"),
            V("v12_evan_coauthor_up","合著·升阶","合著波及比例由60%替换为85%；仍只波及另一人。","char_story_evan","v12_evan_coauthor"),
            V("v12_evan_conclusion","定论","对破绽目标的定稿基础伤害提高30%。","char_story_evan",exclusive:"v12_evan_coauthor"),
            V("v12_evan_conclusion_up","定论·升阶","定论加成由30%替换为45%，不连乘。","char_story_evan","v12_evan_conclusion"),
            V("v12_mira_sweep","横扫","断势实际命中后波及2米小扇形内至多两敌，基础伤害60%。","char_story_mira",exclusive:"v12_mira_hunt"),
            V("v12_mira_sweep_up","横扫·升阶","横扫范围2→2.8米，基础比例60%→75%；至多两敌。","char_story_mira","v12_mira_sweep"),
            V("v12_mira_hunt","追猎","断势实际击杀后，追身斩剩余冷却减少3秒；每行动一次。","char_story_mira",exclusive:"v12_mira_sweep"),
            V("v12_mira_hunt_up","追猎·升阶","追身斩最远距离4→5.5米；不增加无敌。","char_story_mira","v12_mira_hunt"),
            V("v12_finn_spread","蔓生","寄生目标死亡时，将剩余寿命传给3米内至多两敌，仅传播一代。","char_story_finn",exclusive:"v12_finn_bloom"),
            V("v12_finn_spread_up","蔓生·升阶","蔓生范围3→4.5米；仍不刷新寄生寿命。","char_story_finn","v12_finn_spread"),
            V("v12_finn_bloom","绽放","森息落地对区内已寄生目标立即触发一跳，不递归。","char_story_finn",exclusive:"v12_finn_spread"),
            V("v12_finn_bloom_up","绽放·升阶","绽放实际伤害后，区内一名最低血存活队员回复2%最大HP，每次施放一次。","char_story_finn","v12_finn_bloom"),
            V("v12_team_steadfast","稳阵","新遭遇全队获得5%最大HP护盾，持续6模拟秒；同遭遇一次。"),
            V("v12_team_lightfoot","轻履","普通闪避冷却×0.85，保留最低冷却；不延长无敌。"),
            V("v12_team_leeway","余裕","本局Focus容量+15并恢复至多15秒；计入原恢复总上限，满额不暗存。"),
            V("v12_team_edge","回锋","每遭遇首个付费主战技实际击杀获得25战机；多杀仅一次。"),
            V("v12_team_life","生机","实际治疗量提高15%；不新增来源，不复活，过量治疗不换资源。"),
            V("v12_team_pathfinder","寻路","本局前两次不同登记探索目标完成各恢复2秒Focus；不含菜单或重复交互。")
        };
        public static IReadOnlyList<V11BuffDefinition> All=>Definitions.Select(Copy).ToArray();
        static V11BuffDefinition Copy(V11BuffDefinition d)=>d==null?null:new V11BuffDefinition{id=d.id,name=d.name,description=d.description,pool=d.pool,coreActorId=d.coreActorId,maxStacks=d.maxStacks,value=d.value,prerequisites=new List<string>(d.prerequisites),exclusives=new List<string>(d.exclusives)};
        internal static V11BuffDefinition Raw(string id){foreach(var d in Definitions)if(d.id==id)return d;return null;}
        public static V11BuffDefinition Find(string id)=>Copy(Raw(id));
        public static int Stacks(V11CombatRules rules,string id){if(rules?.buffs==null)return 0;foreach(var b in rules.buffs)if(b.id==id)return b.stacks;return 0;}
        public static float Value(V11CombatRules rules,string id)=>Stacks(rules,id)*(Raw(id)?.value??0);
        public static bool UsesV12Build(V11CombatRules rules)=>rules?.version=="v12-character-1"&&rules.v12BuildVersion=="v12-build-1";
        public static IReadOnlyList<V11BuffDefinition> DisplayBuffs(V11CombatRules rules)
        {
            if(rules?.buffs==null)return new List<V11BuffDefinition>();
            return rules.buffs.Where(b=>b.stacks>0&&!(b.id.StartsWith("v12_",StringComparison.Ordinal)&&!b.id.EndsWith("_up",StringComparison.Ordinal)&&Stacks(rules,b.id+"_up")>0))
                .Select(b=>Find(b.id)).Where(d=>d!=null).Take(4).ToList();
        }
        public static bool AvailableInRules(V11CombatRules rules,string id)
        {
            var d=Raw(id);if(rules==null||d==null)return false;
            if(id.StartsWith("v14_",StringComparison.Ordinal))return V14BuildCatalog.Available(rules,id);
            if(V14BuildCatalog.Enabled(rules)&&d.pool=="Core"&&id.StartsWith("v12_",StringComparison.Ordinal))return false;
            // New Evan prepares correction instead of Final Draft. Keep legacy IDs and
            // snapshots intact, but never offer a reward for an unavailable new-run skill.
            if(rules.v13FinnTree!=null&&(id=="v12_evan_conclusion"||id=="v12_evan_conclusion_up"))return false;
            if(id.StartsWith("v13_",StringComparison.Ordinal))return rules.v13FinnTree=="guard"&&rules.coreActorId==CombatSession.V12Finn;
            if(rules.v13FinnTree=="guard"&&id.StartsWith("v12_finn_",StringComparison.Ordinal))return false;
            if(UsesV12Build(rules))return id.StartsWith("v12_",StringComparison.Ordinal)?d.pool=="Generic"||d.coreActorId==rules.coreActorId:d.pool=="Core"&&d.coreActorId==rules.coreActorId;
            return !id.StartsWith("v12_",StringComparison.Ordinal)&&(d.pool=="Generic"||d.coreActorId==rules.coreActorId);
        }
        public static IReadOnlyList<V11BuffDefinition> Eligible(V11CombatRules rules)
        {
            if(rules==null||rules.actors==null||!rules.actors.Any(a=>a.actorId==rules.coreActorId))return new List<V11BuffDefinition>();
            return All.Where(d=>AvailableInRules(rules,d.id)&&Stacks(rules,d.id)<d.maxStacks&&d.prerequisites.All(p=>Stacks(rules,p)>0)&&!d.exclusives.Any(p=>Stacks(rules,p)>0)&&
                (d.id!="core_radiant_resonance"||rules.actors.Any(a=>a.actorId!=rules.coreActorId&&a.unlockedActionIds.Contains(CombatSession.P6BindId)))).ToList();
        }
        public static float ReactionMultiplier(V11CombatRules rules)=>(float)Math.Pow(.85,Stacks(rules,"team_reaction"));
    }
    public static class V11AbilityCatalog
    {
        public static readonly string[] CustomIds={"v11_support_ward","v11_nature_bind","v11_nature_mend","v11_quick_shot","v11_oath_aegis"};
        public static bool IsCustom(string id)=>Array.IndexOf(CustomIds,id)>=0;
        public static bool IsSelfTarget(string id)=>id=="v11_support_ward"||id=="v11_nature_mend"||id=="v11_oath_aegis";
        public static string Name(string id)
        {
            if(id=="v14_finn_detonate")return "寄生爆绽";
            switch(id){case "v13_evan_correct":return "校正";case "v13_finn_shield":return "护生";case "v13_finn_prayer":return "回春祈愿";case "v12_evan_mark":return "弱点标注";case "v12_evan_final":return "定稿";case "v12_mira_chase":return "追身斩";case "v12_mira_break":return "断势";case "v12_finn_seed":return "寄生芽";case "v12_finn_grove":return "森息";}
            switch(id){case "v11_support_ward":return "支援结界";case "v11_nature_bind":return "缚根";case "v11_nature_mend":return "林息回复";case "v11_quick_shot":return "速射";case "v11_oath_aegis":return "圣誓护盾";case "ability_smite_recharge":return "至圣斩";case "ability_bind_radiance":return "裂光协助";case "attack":return "重斩";case "shove":return "推撞";case "sneak":return "突袭";case "control":return "雷鸣";default:return V11BuffCatalog.Find(id)?.name??id;}
        }
        public static string Description(string id)
        {
            if(id=="v14_finn_detonate")return "1指挥；7米内可见的自有寄生目标，0.35模拟秒后提前结算其剩余跳伤并移除未来计划；不掷d20、不暴击。";
            switch(id)
            {
                case "v13_finn_shield":return "免费；核心获得6%最大HP护盾6秒；10秒CD，与Reaction共用。";
                case "v13_finn_prayer":return "1指挥次数＋1祈愿；回复16%生命并解除缠绕。每局2次，不复活。";
                case "v13_evan_correct":return "埃文灵感1＋Reaction；己方合格检定总值+2，不改变原始骰面。";
                case "v12_evan_mark":return "免费；7米可见目标破绽6模拟秒、承伤×1.20；0.35秒施作，8秒冷却，同源仅一目标。";
                case "v12_evan_final":return "1战术次数；7米远程攻击约2.5倍普攻基准；掷骰时目标有破绽获得优势。";
                case "v12_mira_chase":return "免费；合法路径接近最多4米并斩击，6秒冷却；不穿墙、不附无敌。";
                case "v12_mira_break":return "1战术次数；2.6米重斩约3倍普攻基准、削韧2；无前置状态也可用。";
                case "v12_finn_seed":return "免费；7米附寄生6秒，每秒一次，共6跳；8秒冷却，无额外d20。";
                case "v12_finn_grove":return "1战术次数；6米落区、半径2.5米，持续6秒；最多4敌寄生并减速，区内存活队友每秒恢复1%最大生命。";
            }
            switch(id){case "v11_support_ward":return "1战术次数；INT+熟练对DC10，成功全队3点吸收盾，6模拟秒。";case "v11_nature_bind":return "1战术次数；7米敌WIS豁免，失败短封缚；Boss改短减速。";case "v11_nature_mend":return "1战术次数；WIS+熟练对DC10，成功每位存活队员回复4生命。";case "v11_quick_shot":return "1战术次数；7米攻击对AC，命中6伤害，原始20暴击12。";case "v11_oath_aegis":return "1战术次数；CHA+熟练对DC10，成功自身6点吸收盾，6模拟秒。";default:return Name(id);}
        }
        public static ActionSpec Action(string id)
        {
            if(!IsCustom(id))return null;
            return new ActionSpec{id=id,range=IsSelfTarget(id)?1:7,windup=.35f,rollType=id=="v11_nature_bind"?"Save":id=="v11_quick_shot"?"Attack":"Check",saveAbility="Wisdom",ranged=!IsSelfTarget(id),damage=id=="v11_quick_shot"?6:0};
        }
    }
}
