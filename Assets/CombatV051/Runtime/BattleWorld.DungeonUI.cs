using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        public string DungeonModeName => Profile?.State.activeMode==ProfileModeKind.Challenge ? "独立挑战" : "作者故事";
        public string DisplayTarget(string actorId)
        {
            if(string.IsNullOrEmpty(actorId))return "无";
            if(Profile==null)return actorId;
            var type=Profile.EnemyTemplateFor(actorId);
            if(type==null)return ActorName(actorId);
            switch(type.id) {case "guard":return "守卫";case "scout":return "斥候";case "elite":return "精英";case "boss":return "首领";case "mechanism":return "机关";default:return type.id;}
        }
        public void CycleTarget()
        {
            if(V13Enabled&&(SelectedAction=="v13_evan_correct"||!V13AssistMode&&(SelectedAction=="v13_finn_shield"||SelectedAction=="v13_finn_prayer"))){var allies=State.actors.Where(a=>a.Alive).Select(a=>a.id).ToArray();SelectedTarget=allies[(Array.IndexOf(allies,SelectedTarget)+1)%allies.Length];Hud.RefreshView();return;}
            if(V11CombatEnabled&&V11AbilityCatalog.IsSelfTarget(SelectedAction)){SelectedTarget=SelectedActor;return;}
            var targets=State.enemies.Where(e=>e.Alive).Select(e=>e.id).ToList();
            SelectedTarget=targets.Count==0 ? null : targets[(targets.IndexOf(SelectedTarget)+1)%targets.Count];
            if(SelectedTarget!=null&&IsFocus) { TutorialFact("selection.target",Guid.NewGuid().ToString("N"));PollPresentation(); }
        }
        public static string RoomName(string id)
        {
            switch(id){case "V12_R1":return "林门前庭";case "V12_R2":return "断桥庭院";case "V12_R3":return "旧抄写室";case "V12_R4":return "倒悬礼堂";case "V12_R5":return "露天苗圃";case "V12_R6":return "灰冠内庭";}
            switch(id) { case "R0":return "入口";case "R1":return "实战教学";case "R2":return "检修与岔路";case "R3":return "分支目标";case "R4":return "掩体遭遇";case "R5":return "精英";case "R6":return "短休与回环";case "R7":return "首领终点";default:return id; }
        }
        string BuildDescription => Profile==null ? "" : Profile.ActiveUsesV11 ? V11CoreSummary : "主骰律："+RuleName(Profile.ActiveMode.build.primaryLawId)+"；遗物："+string.Join(" / ",Profile.ActiveMode.build.relicSlots.Select(RuleName));
        string RuleName(string id)
        {
            var buff=V11BuffCatalog.Find(id);if(buff!=null)return buff.name+" · "+buff.description;
            switch(id) { case null:return "空位";case "BASE":return "基准";case "COMMAND":return "统筹（上限3/慢瞄准）";case "RESERVE":return "储备（准备间隔缩短）";case "FOCUS":return "定心（瞄准×0.65）";case "REACTION":return "敏捷（反应冷却×0.7）";default:return id; }
        }
        public string DungeonSummary
        {
            get
            {
                if(Profile==null)return "";
                if(Profile.UsesV12Dungeon)return V12DungeonSummary();
                string room=Profile.ActiveRoom.templateId;
                string direction=room=="R0" ? "沿右上方通道进入 R1" : room=="R1" ? "右上前往 R2" : room=="R2" ? (Profile.ActiveMode.Room("R3").resolved ? "上方岔路通往 R4；破障门前按 E" : "先去右上 R3，清场获得破障能力") : room=="R3" ? "返回左下 R2，再去上方 R4" : room=="R4" ? "左下通道去 R5 精英" : room=="R5" ? "左下通道去 R6 短休" : room=="R6" ? "下方 E 开捷径；左下前往 R7 首领" : "首领终点";
                if(room=="R2" && Profile.State.activeMode==ProfileModeKind.Challenge && !Profile.ActiveMode.HasAbility("SMASH"))
                    direction=Profile.ActiveMode.Room("R3").resolved ? "右侧外绕通道已开放，可去 R4，无需破障" : "先去右上 R3 完成分支目标，再返回此处走外绕路";
                else if(room=="R2" && Profile.IsPassageOpen("P24_SMASH"))direction="上方破障通道已开放，通往 R4";
                string interaction=NearbyDungeonInteraction();
                return DungeonModeName+" · "+room+" "+RoomName(room)+"   "+Mathf.FloorToInt(Profile.ActiveMode.elapsedRealSeconds/60)+":"+Mathf.FloorToInt(Profile.ActiveMode.elapsedRealSeconds%60).ToString("00")+"\n"+direction+"\n"+(interaction==null ? "WASD移动 · 1/2/3主控 · 靠近亮色标记 E 互动" : "E 互动："+InteractionName(interaction))+"\n"+BuildDescription;
            }
        }
        string InteractionName(string id) => id=="v12-beam"?V12BeamHint:id!=null&&id.StartsWith("supply:",StringComparison.Ordinal)?"一次补给 / 不复活":id=="event:V12_READ"?"阅读书页 / 免检定":id=="hub" ? "入口/选择模式" : id=="rest" ? "一次短休" : id=="gate" ? "破障能力门" : id=="shortcut" ? "打开双向捷径" : id=="cover" ? "花本角色次要行动推动掩体 / 关闭地面喷口" : id=="reward" ? "选择遗物" : "独立事件检定";
        public string DungeonDialogTitle
        {
            get
            {
                switch(dungeonDialog) {case "supply":return "一次补给";case "intro":return "故事入口 / 实战教学";case "hub":return "入口 / 故事与独立挑战";case "event-select":return "事件检定 / 选择队员";case "event":return "独立 d20 / "+EventTitle;case "reward":return Profile.ActiveUsesV11?"本局构筑 · 选择一个增益":"遗物选择 / 两个插槽";case "replace":return "明确替换一个遗物";case "rest":return "一次短休 / 二选一";case "defeat":return "小队倒下";case "victory":return "首领已击败 / 结算";case "settled":return "结算已保存";default:return "地牢";}
            }
        }
        string EventTitle => interactingEvent=="V12_READ"?"遗留书页":interactingEvent=="V12_SEARCH"?"旧书箱":interactingEvent=="EVENT_A" ? "检修掩体通路" : "侦察首领机关";
        string EventEffect => interactingEvent=="V12_READ"?"无需骰子：移开书架短挡，开启房内小回环；北侧主门始终可走。":interactingEvent=="V12_SEARCH"?"成功：获得一次少量治疗补给。失败：收益落空，主线与普通绕行路始终可走。":interactingEvent=="EVENT_A" ? "成功：北侧房间中央掩体移开，获得直通路线。失败：保留左侧绕行路。" : "成功：关闭首领房的额外机关。失败：机关保留，可躲开预警并将其破坏。";
        string AbilityName(string ability) => ability=="Intelligence" ? "智力" : ability=="Wisdom" ? "感知" : ability=="Strength" ? "力量" : ability=="Dexterity" ? "敏捷" : ability;
        string ActorName(string id) => V11Catalog.FindCharacter(id)?.displayName ?? (id=="char_demo_paladin"?"圣武士": State.Actor(id)?.role==ActorRole.Mage ? "法师" : State.Actor(id)?.role==ActorRole.Rogue ? "游荡者" : "战士");
        public string DungeonDialogBody
        {
            get
            {
                if(Profile==null)return "";
                switch(dungeonDialog)
                {
                    case "supply": return (V12SupplyPoint?.label??"补给")+"\n为存活队员恢复部分最大生命；不复活，不改核心/技能/冷却。此局该点只能领取一次。\n[作者文本待填写]";
                    case "intro": return StoryText.Load().introduction+"\n\n准备好后出发。提示会随你的实际行动逐步出现。";
                    case "hub": return Profile.State.activeMode==ProfileModeKind.Challenge ? "已返回探索入口。可暂存本局，或结束并结算当前收益。\n"+BuildDescription+"\n结束后本局增益不带入下一局；永久养成保留。" : !Profile.State.meta.challengeUnlocked ? "入口装置已调查。\n\n沿东侧通道前行，小队会跟随主控角色。\n\n[装置的正式说明待作者填写]" : "这里可以开始独立挑战，或继续当前故事。\n"+BuildDescription+"\n研究记录："+Profile.State.meta.research+"\n"+(Profile.State.meta.trainingLevel>0 ? "小队生命训练已完成。" : "可选训练：3研究记录，全队+2最大生命。故事推进不要求训练。");
                    case "event-select": return EventTitle+"\n"+EventEffect+"\n\n选择检定队员后，可预览属性、DC、概率和优势状态。\n事件使用独立随机流，不生成战斗 战术次数。";
                    case "event":
                    {
                        var ev=Profile.ActiveSafeEvent; if(ev==null)return "事件尚未打开。";
                        var q=Profile.QuoteEvent(ev.templateId,ev.actorId);
                        string preview=ActorName(ev.actorId)+" · "+AbilityName(ev.ability)+"检定 · 总修正 "+ev.bonus.ToString("+0;-0;0")+" 对 DC "+ev.dc+"\n"+(ev.advantage&&!ev.disadvantage ? "优势，掷2取高" : ev.disadvantage&&!ev.advantage ? "劣势，掷2取低" : "普通1d20")+" · 概率 "+(q.successProbability*100).ToString("0")+"%";
                        return preview+"\n"+EventEffect+"\n\n"+(ev.stage=="AwaitDecision" ? "原始骰："+string.Join(" / ",ev.rawFaces)+"；总值 "+ev.total+" → "+(ev.success ? "成功" : "失败")+"\n"+(ev.rerollUsed ? "已使用本事件唯一重掷。" : "失败可由原队员花1 Inspiration重掷一次。") : "确认检定后保存同一结果；关闭面板不会刷新。")+"\n[正式事件文案待作者填写]";
                    }
                    case "reward": if(Profile.ActiveUsesV11)return "本局构筑 · "+(Profile.PendingReward?.candidates.Count??0)+" 项合法候选\n"+V11CoreSummary+"\n选择一个，或跳过本次；形态升阶替换原效果。\n本局保存，下一局重新构筑。";return (Profile.ActiveRoom.templateId=="R3"&&Profile.State.activeMode==ProfileModeKind.Story ? "获得破障能力！返回岔路，在北门前按 E 开路。\n" : Profile.ActiveRoom.templateId=="R1" ? "遭遇完成，独立挑战入口已解锁。\n" : "")+"遗物从下一遭遇生效。\n"+BuildDescription+"\n\n选择一个遗物，或放弃本次奖励。满两格时需选择替换位置。";
                    case "replace": return "将装备："+RuleName(replacementCandidate)+"\n\n当前1号："+RuleName(Profile.ActiveMode.build.relicSlots[0])+"\n当前2号："+RuleName(Profile.ActiveMode.build.relicSlots[1])+"\n\n被替换的遗物直接移除，不返还资源、不生成新奖励。";
                    case "rest": return "每个模式实例只可使用一次。\n\n全队恢复各自35%最大生命，可扶起倒下队员；或选择一位队员恢复1点职业资源（不超过初始上限）。\n\n不会生成 战术次数。\n"+(Profile.ActiveMode.usedRestIds.Count>0 ? "此局短休已用。" : "此局短休尚未使用。");
                    case "defeat": return Profile.State.activeMode==ProfileModeKind.Story ? "故事可从本次遭遇开始的完整快照重试。双方生命、资源、位置、全部随机状态一同恢复。\n已花资源与有利结果不会单独保留。" : "本次挑战结束。结算已完成目标的收益一次，然后返回保存中的故事。";
                    case "victory": return StoryText.Load().ending+(Profile.UsesV12Dungeon?"\n\n六房探索完成。":"\n\n五场遭遇已完成。")+(Profile.State.activeMode==ProfileModeKind.Challenge ? "挑战待结算研究记录："+Profile.ActiveMode.runEarnings : "故事能力与通路保留；挑战与训练为可选内容。")+"\n活动用时 "+(Profile.ActiveMode.elapsedRealSeconds/60).ToString("0.0")+" 分钟。";
                    case "settled": return "此次结果已经写入档案，不会重复给奖。\n本次研究记录："+(Profile.State.meta.lastSettlement?.earned ?? 0)+"；累计："+Profile.State.meta.research+"\n\n当前模式："+DungeonModeName+"\n"+BuildDescription+"\n[后续剧情与局外成长内容待作者补充]";
                    default:return "";
                }
            }
        }
        public List<DungeonChoice> DungeonChoices
        {
            get
            {
                var list=new List<DungeonChoice>(); if(Profile==null)return list;
                switch(dungeonDialog)
                {
                    case "supply":
                        list.Add(new DungeonChoice("supply-claim","领取一次补给",V12SupplyPoint!=null&&!Profile.ActiveMode.usedRestIds.Contains(v12SupplyId)&&State.actors.Any(a=>a.Alive&&a.hp<a.maxHp)));
                        list.Add(new DungeonChoice("close","暂不领取"));break;
                    case "intro": list.Add(new DungeonChoice("close","进入故事 / 开始移动"));break;
                    case "hub":
                        // The unlock gates starting the optional challenge from Story.
                        // An already running Demo exploration must still be able to settle.
                        if(!Profile.State.meta.challengeUnlocked&&Profile.State.activeMode==ProfileModeKind.Story) { list.Add(new DungeonChoice("close","继续前行"));break; }
                        if(Profile.State.activeMode==ProfileModeKind.Story)
                        {
                            bool newRun=Profile.State.meta.challengeUnlocked&&(Profile.State.challenge==null||Profile.State.challenge.stage=="Settled");
                            list.Add(new DungeonChoice("start:BASE","新挑战 · 基准骰律",newRun));list.Add(new DungeonChoice("start:COMMAND","新挑战 · 统筹骰律",newRun));
                            if(Profile.State.challenge!=null&&Profile.State.challenge.stage!="Settled")list.Add(new DungeonChoice("continue-challenge","继续已暂存挑战"));
                            list.Add(new DungeonChoice("train-party",Profile.State.meta.trainingLevel>0 ? "小队训练已完成" : "3研究 · 一次小队生命训练",Profile.State.meta.trainingLevel==0&&Profile.State.meta.research>=3));
                        }
                        else {list.Add(new DungeonChoice("continue-story","暂存挑战 / 返回故事"));list.Add(new DungeonChoice("settle:abandon","结束挑战并结算当前收益"));}
                        list.Add(new DungeonChoice("close","继续探索"));break;
                    case "event-select":
                        foreach(var actor in State.actors)
                        {var quote=Profile.QuoteEvent(interactingEvent,actor.id);list.Add(new DungeonChoice("actor:"+actor.id,ActorName(actor.id)+" · "+AbilityName(quote.ability)+quote.bonus.ToString("+0;-0;0")+" · "+(quote.successProbability*100).ToString("0")+"%",quote.legal));}
                        list.Add(new DungeonChoice("close","暂不检定"));break;
                    case "event":
                        var ev=Profile.ActiveSafeEvent;
                        if(ev!=null && ev.stage=="Quoted"){if(Profile.UsesV12Dungeon&&ev.templateId=="V12_READ")list.Add(new DungeonChoice("event-free","阅读 / 开启房内小回环"));else list.Add(new DungeonChoice("event-roll","确认进行独立 d20 检定"));}
                        else if(ev!=null && ev.stage=="AwaitDecision")
                        {
                            list.Add(new DungeonChoice("event-accept",ev.success ? "确认成功 / 改变世界" : "接受失败 / 走替代路径"));
                            if(!ev.success)list.Add(new DungeonChoice("event-reroll","花1 Inspiration 重掷一次",!ev.rerollUsed&&State.Actor(ev.actorId)?.inspiration>0));
                        }
                        list.Add(new DungeonChoice("close","稍后决定 / 保存当前结果"));break;
                    case "reward":
                        if(Profile.PendingReward!=null)foreach(string item in Profile.PendingReward.candidates)list.Add(new DungeonChoice("reward:"+item,RuleName(item)));
                        list.Add(new DungeonChoice("skip-reward","放弃本次奖励"));break;
                    case "replace":list.Add(new DungeonChoice("replace:0","替换1号 · "+RuleName(Profile.ActiveMode.build.relicSlots[0])));list.Add(new DungeonChoice("replace:1","替换2号 · "+RuleName(Profile.ActiveMode.build.relicSlots[1])));list.Add(new DungeonChoice("back-reward","返回候选"));break;
                    case "rest":
                        bool unused=Profile.ActiveMode.usedRestIds.Count==0;
                        list.Add(new DungeonChoice("rest:heal","全队恢复35%生命 / 可扶起",unused));
                        foreach(var actor in State.actors)list.Add(new DungeonChoice("rest:"+actor.id,ActorName(actor.id)+" 恢复1点职业资源",unused&&actor.Alive));
                        list.Add(new DungeonChoice("close","暂不使用"));break;
                    case "defeat":
                        list.Add(Profile.State.activeMode==ProfileModeKind.Story ? new DungeonChoice("retry","恢复整个遭遇开始快照") : new DungeonChoice("settle:defeat","结束挑战 / 结算当前收益"));
                        list.Add(new DungeonChoice("save-menu","保存并回标题"));break;
                    case "victory":list.Add(new DungeonChoice("settle:victory","确认终点 / 一次结算"));break;
                    case "settled":
                        list.Add(new DungeonChoice("growth-menu","回到成长 / 准备下一次出发"));
                        if(Profile.State.activeMode==ProfileModeKind.Challenge)list.Add(new DungeonChoice("continue-story","返回原故事进度"));
                        else list.Add(new DungeonChoice("close","继续探索已连通地图"));
                        list.Add(new DungeonChoice("save-menu","保存并回标题"));break;
                }
                return list;
            }
        }

        public void ChooseDungeonOption(string id)
        {
            if(Profile==null || string.IsNullOrEmpty(id))return;
            // Public UI gateway: only an enabled, currently displayed action may mutate progress.
            if(id!="close" && !DungeonChoices.Any(c=>c.id==id&&c.enabled)) {Message="此选择当前不可用。";return;}
            ProfileCommand command=null;
            if(id=="close")
            {
                if(dungeonDialog=="defeat"||dungeonDialog=="victory"||dungeonDialog=="reward"||dungeonDialog=="replace")return;
                if(Profile.ActiveSafeEvent!=null)command=new ProfileCommand{kind=ProfileCommandKind.CloseEvent,eventId=Profile.ActiveSafeEvent.id};
                else dungeonDialog="";
            }
            else if(id=="save-menu") {Suspend();return;}
            else if(id=="growth-menu") {OpenGrowthFromSettlement();return;}
            else if(id=="back-reward") {replacementCandidate=null;dungeonDialog="reward";}
            else if(id.StartsWith("actor:",StringComparison.Ordinal))command=new ProfileCommand{kind=ProfileCommandKind.OpenEvent,eventId=interactingEvent,actorId=id.Substring(6)};
            else if(id=="supply-claim")command=new ProfileCommand{kind=ProfileCommandKind.ShortRest,selectionId="v12:"+v12SupplyId,actorId=State.activeActorId,qualified=true};
            else if(id=="event-free")command=new ProfileCommand{kind=ProfileCommandKind.UseEventAlternative,eventId=Profile.ActiveSafeEvent.id,actorId=Profile.ActiveSafeEvent.actorId};
            else if(id=="event-roll"||id=="event-reroll"||id=="event-accept")command=new ProfileCommand{kind=id=="event-roll" ? ProfileCommandKind.RollEvent : id=="event-reroll" ? ProfileCommandKind.RerollEvent : Profile.ActiveSafeEvent.success ? ProfileCommandKind.ConfirmEvent : ProfileCommandKind.UseEventAlternative,eventId=Profile.ActiveSafeEvent.id,actorId=Profile.ActiveSafeEvent.actorId};
            else if(id.StartsWith("reward:",StringComparison.Ordinal))
            {
                replacementCandidate=id.Substring(7);
                if(!Profile.ActiveUsesV11&&Profile.ActiveMode.build.relicSlots.All(s=>s!=null))dungeonDialog="replace";
                else command=new ProfileCommand{kind=ProfileCommandKind.ClaimReward,rewardId=Profile.PendingReward.id,selectionId=replacementCandidate};
            }
            else if(id.StartsWith("replace:",StringComparison.Ordinal))command=new ProfileCommand{kind=ProfileCommandKind.ClaimReward,rewardId=Profile.PendingReward.id,selectionId=replacementCandidate,replacementSlot=int.Parse(id.Substring(8))};
            else if(id=="skip-reward")command=new ProfileCommand{kind=ProfileCommandKind.SkipReward,rewardId=Profile.PendingReward?.id};
            else if(id.StartsWith("rest:",StringComparison.Ordinal))command=new ProfileCommand{kind=ProfileCommandKind.ShortRest,selectionId=id=="rest:heal" ? "heal" : "resource",actorId=id=="rest:heal" ? State.activeActorId : id.Substring(5),qualified=true};
            else if(id=="retry")command=new ProfileCommand{kind=ProfileCommandKind.RetryStoryEncounter};
            else if(id=="train-party")command=new ProfileCommand{kind=ProfileCommandKind.TrainParty};
            else if(id.StartsWith("start:",StringComparison.Ordinal))command=new ProfileCommand{kind=ProfileCommandKind.StartChallenge,selectionId=id.Substring(6)};
            else if(id=="continue-story"||id=="continue-challenge")command=new ProfileCommand{kind=id=="continue-story" ? ProfileCommandKind.ContinueStory : ProfileCommandKind.ContinueChallenge};
            else if(id.StartsWith("settle:",StringComparison.Ordinal))command=new ProfileCommand{kind=ProfileCommandKind.SettlementOnce,selectionId=id.Substring(7)};
            if(command!=null)
            {
                var result=ProfileAction(command);
                if(result.success)
                {
                    RestoreDungeonDialog();
                    if(command.kind==ProfileCommandKind.SettlementOnce)dungeonDialog="settled";
                    if(command.kind==ProfileCommandKind.StartChallenge)
                    {
                        Rig.Initialize(BattleCamera,()=>Position(ActiveActor),PartyCenter,()=>IsFrozen,()=>IsFocus);
                        Message="已开始独立挑战；故事位置、生命和资源保持在原档案中。";
                    }
                }
            }
            PollPresentation(); ApplyTime(); Hud.RefreshView();
        }
    }
}
