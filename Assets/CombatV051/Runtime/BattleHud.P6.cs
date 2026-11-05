using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed partial class BattleHud
    {
        GameObject p6DirectPanel,p6PracticePanel;
        Text p6DirectText,p6PracticeText,p6DamageText;
        bool p6Details;
        public string P6VisibleSummary=>p6PracticePanel!=null&&p6PracticePanel.activeSelf?p6PracticeText.text:resultText.text;
        void CreateP6Panels(Transform root)
        {
            Button(root,"p6-smite","Q  至圣斩",790,674,250,34,()=>world.OpenCurrentKeyAction());
            Button(root,"p6-bind","F  裂光协助",520,674,255,34,()=>world.RequestCurrentBasicAction());
            Button(postPanel.transform,"p6-full","全力至圣斩 · 再用1次",18,285,312,43,()=>world.CommitSmite(true));
            Button(postPanel.transform,"p6-details","调整 / 公式详情",18,235,312,33,()=>p6Details=!p6Details);
            p6DirectPanel=Panel(root,"Smite prior acceptance",901,340,360,235,Ink);
            p6DirectText=Label(p6DirectPanel.transform,"",16,15,328,116,17,Color.white);
            Button(p6DirectPanel.transform,"p6-direct","预先接受结果 · 基础斩1次",16,143,328,40,()=>world.DirectSmite());
            p6PracticePanel=Panel(root,"Isolated Smite practice",16,445,355,209,Ink);
            p6PracticeText=Label(p6PracticePanel.transform,"",12,10,331,93,17,Gold);
            Button(p6PracticePanel.transform,"p6-refill","补充次数 / 生命 / 重置练习目标",12,110,331,30,()=>world.RefillP6Practice(world.P6Lesson==7));
            buttons["p6-refill"].GetComponentInChildren<Text>().fontSize=13;
            Button(p6PracticePanel.transform,"p6-finish","跳过训练 · 进入探索",12,151,331,32,()=>world.FinishP6Training());
            Button(p6PracticePanel.transform,"p6-up-radiant","圣光 +2d8",12,111,104,32,()=>world.ChooseP6PracticeUpgrade("practice_radiant"));
            Button(p6PracticePanel.transform,"p6-up-accuracy","攻击总加值 +2",123,111,105,32,()=>world.ChooseP6PracticeUpgrade("practice_accuracy"));
            Button(p6PracticePanel.transform,"p6-up-bind","裂光冷却 12s",235,111,108,32,()=>world.ChooseP6PracticeUpgrade("practice_bind"));
            foreach(var id in new[]{"p6-up-radiant","p6-up-accuracy","p6-up-bind"})buttons[id].GetComponentInChildren<Text>().fontSize=12;
            p6DamageText=Label(root,"",365,579,535,49,18,Gold,TextAnchor.MiddleCenter);
        }
        void RefreshP6Panels()
        {
            bool p6=world.P6Enabled,playing=p6&&!world.Menu&&!world.DungeonModal;
            bool reacting=world.State.activePendingEvent!=null;
            bool realtime=playing&&!reacting&&!world.IsFrozen&&!world.IsFocus;
            bool main=world.V12Enabled&&BattleWorld.V12Hero(world.State.activeActorId);
            SetButtonTitle("p6-smite",main?"Q  "+V11AbilityCatalog.Name(world.CoreKeyAction)+(world.CoreKeyAction=="v13_evan_correct"?" · 灵感1 / 反应":""):"Q  至圣斩");
            SetButtonTitle("p6-bind",main?"F  "+V11AbilityCatalog.Name(world.CoreBasicAction)+" · 免费":"F  裂光协助");
            buttons["p6-smite"].gameObject.SetActive(realtime&&world.State.inCombat&&(main||world.State.Actor("char_demo_paladin")!=null));
            buttons["p6-bind"].gameObject.SetActive(realtime&&world.State.inCombat&&(main||world.RadianceCaster!=null)&&(!world.Training||world.MainCharacterTraining||world.P6Lesson>=6));
            p6PracticePanel.SetActive(playing&&world.Training&&!reacting&&!world.IsFrozen);
            p6DamageText.gameObject.SetActive(realtime&&world.P6LastDamage.Length>0);
            p6DamageText.text=world.P6LastDamage;
            bool post=playing&&world.State.pendingRoll?.p6!=null&&world.State.mode==CombatMode.PostRollDecision&&!reacting;
            buttons["p6-full"].gameObject.SetActive(post);
            buttons["p6-details"].gameObject.SetActive(post&&!p6Details);
            p6DirectPanel.SetActive(false);
            bool quick=p6&&world.P6QuickFocus&&world.State.mode==CombatMode.TacticalFocus;
            focusHeading.text=quick?"至圣斩":"TACTICAL FOCUS";
            focusInstruction.text=quick?"选目标 → 掷骰 → 释放":"选择角色 → 行动 → 目标";
            foreach(var id in new[]{"fighter","mage","rogue"})buttons[id].gameObject.SetActive(!quick);
            queueText.gameObject.SetActive(!quick);buttons["cancel-queue"].gameObject.SetActive(!quick);
            SetButtonTitle("fighter",p6?"① 圣武士":"① 战士");
            SetButtonTitle("mage",p6?"② 法师":"③ 法师");SetButtonTitle("rogue",p6?"③ 游荡者":"② 游荡者");
            MoveButton("mage",p6?119:220,88,92,36);MoveButton("rogue",p6?220:119,88,92,36);
            if(!p6)return;
            SetButtonTitle("fighter","① 圣武士");
            foreach(var id in new[]{"scale00","scale10","scale15"})buttons[id].interactable=false;
            SetButtonTitle("scale10","专注固定 ×0.12");
            if(playing)
            {
                actionStrip.SetActive(false);
                tutorialPanel.SetActive(false); // The isolated lesson has one dedicated short instruction panel.
                if(quick){basicTacticsPanel.SetActive(false);tacticalSpatialPanel.SetActive(false);actorInfo.gameObject.SetActive(false);enemyInfo.gameObject.SetActive(false);}
                foreach(var id in new[]{"attack","shove","sneak","control"})buttons[id].gameObject.SetActive(!quick&&world.SelectedActor!="char_demo_paladin"&&world.AvailableActions(world.SelectedActor).Contains(id));
                if(world.SelectedActor=="char_demo_paladin"&&world.SelectedAction=="ability_smite_recharge"&&world.State.mode==CombatMode.TacticalFocus)
                {
                    var q=world.Session.QuoteSmite("char_demo_paladin",world.SelectedTarget);
                    quote.text="至圣斩 · 1次\n"+SmiteFormulaPresentation.Formula(world.Session.Config,world.State,"char_demo_paladin")+"\n"+(q.attack?.legal==true?"合法目标 · 命中后可全力，再用1次":HumanReason(q.attack?.reason??q.reason));
                    if(q.markRemaining>0)quote.text+="\n裂光：优势 · 光耀×"+SmiteFormulaPresentation.Multiplier(SmiteFormulaPresentation.MarkMultiplier(world.State,world.SelectedTarget))+" · "+q.markRemaining.ToString("0.0")+"s";
                    bool clearSight=world.State.Actor(world.SelectedTarget)!=null&&world.HasLineOfEffect(BattleWorld.Position(world.State.Actor("char_demo_paladin")),BattleWorld.Position(world.State.Actor(world.SelectedTarget)));
                    buttons["confirm"].interactable=q.interactiveAllowed&&q.availableCharges>0&&clearSight;
                    if(!clearSight)quote.text+="\n视线被遮挡，请换位后施放。";
                    SetButtonTitle("confirm","掷骰 · 支付1次");
                    if(!q.interactiveAllowed&&q.attack?.legal==true&&q.availableCharges>0&&clearSight){p6DirectPanel.SetActive(true);p6DirectText.text="专注不足以进行完整骰后决策。\n预先接受基础结果：照常付费、播骰和执行；无骰后全力/修改。";}
                }
                if(realtime&&world.P6QuickFocus)
                {
                    var q=world.Session.QuoteSmite("char_demo_paladin",world.SelectedTarget);
                    p6DirectPanel.SetActive(true);p6DirectText.text="直接施放基础至圣斩\n预先接受结果，支付1次；无骰后调整。\n"+(q.directAllowed?"可施放":HumanReason(q.attack?.reason??q.reason));buttons["p6-direct"].interactable=q.directAllowed;
                }
                if(main)
                {
                    var actor=world.ActiveActor;float cooldown=actor.v12?.basicCooldown??0;
                    SetButtonTitle("p6-bind",V11AbilityCatalog.Name(world.CoreBasicAction)+(cooldown>0?" · "+cooldown.ToString("0.0")+"s":" · F 免费"));
                    // F itself selects a valid aimed target. Do not disable it merely because the
                    // old RadianceCaster is absent from this three-hero party.
                    buttons["p6-bind"].interactable=actor.Alive&&cooldown<=0&&!CombatSession.V12ActionActive(actor);
                }
                else
                {
                    var mage=world.RadianceCaster;
                    SetButtonTitle("p6-bind",mage?.p6?.bindCooldown>0?"裂光冷却 "+mage.p6.bindCooldown.ToString("0.0")+"s":"F  裂光协助 · 免费");
                    buttons["p6-bind"].interactable=mage!=null&&mage.Alive&&mage.p6?.bindCooldown<=0;
                }
                var statuses=world.State.Actor(world.SelectedTarget)?.statuses;
                var mark=statuses?.FirstOrDefault(s=>s.p6!=null&&s.remaining>0);
                string family=world.V12Enabled&&statuses!=null?string.Join(" · ",statuses.Where(s=>s.v12!=null&&s.remaining>0).Select(s=>(s.v12.family=="opening"?"破绽":s.v12.family=="entangle"?"缠绕":"寄生")+" "+s.remaining.ToString("0.0")+"s")):"";
                compactOpportunities.text=(mark==null?"":"✦ 裂光 "+mark.remaining.ToString("0.0")+"s · 光耀×"+mark.p6.multiplier.ToString("0.##"))+(mark!=null&&family.Length>0?"\n":"")+family;
                bool hasStatus=compactOpportunities.text.Length>0;compactOpportunities.gameObject.SetActive(hasStatus);
                Rect(compactTarget.GetComponent<RectTransform>(),269,14,211,hasStatus?82:57);
                var budget=world.State.focusBudget;focusBudgetText.text=budget.p6?.unlimitedTraining==true?"训练：专注不限时（不带入探索）":"专注 "+budget.secondsRemaining.ToString(world.IsFocus?"0.0":"0")+" / "+budget.maximumSeconds.ToString("0")+"s"+(world.IsFocus?" · 本次 "+CombatSession.P6VisitRemaining(world.State).ToString("0.0")+"s":"");
            }
            if(post)
            {
                var t=world.State.pendingRoll;
                resultText.text=(t.hit?(t.critical?"暴击 · "+t.p6.criticalSource:"命中"):"未命中")+"\n"+SmiteFormulaPresentation.AttackBreakdown(t)+"\n"+SmiteFormulaPresentation.Formula(world.Session.Config,world.State,t.actorId,t);
                buttons["p6-full"].interactable=t.hit&&(t.p6.overchargeHeld||world.Session.AvailableSharedCharges>0);
                SetButtonTitle("commit","释放基础至圣斩");
                buttons["precision"].gameObject.SetActive(false);buttons["post-more"].gameObject.SetActive(false);
                buttons["modify"].gameObject.SetActive(p6Details);buttons["reroll"].gameObject.SetActive(p6Details);
                MoveButton("modify",18,233,150,35);MoveButton("reroll",180,233,150,35);
                actorInfo.gameObject.SetActive(false);enemyInfo.gameObject.SetActive(false);
            }
            if(world.Training)
            {
                if(world.MainCharacterTraining)
                {
                    p6PracticeText.text="三主角练习 · 被动沿用已保存养成\n1/2/3 切换 · F 免费技 · Q 关键技\n两主动练习可用；Tab 指挥任一队员。";
                    foreach(var id in new[]{"p6-up-radiant","p6-up-accuracy","p6-up-bind"})buttons[id].gameObject.SetActive(false);
                    buttons["p6-refill"].gameObject.SetActive(true);
                    SetButtonTitle("p6-refill","补充生命与次数 / 重置目标 · 仅训练");
                    buttons["p6-refill"].interactable=world.State.pendingRoll==null&&world.State.activePendingEvent==null&&!world.State.queue.Any(q=>q.stage=="Queued"||q.stage=="Executing");
                    SetButtonTitle("p6-finish","结束练习 · 返回主界面");
                    return;
                }
                p6PracticeText.text="独立训练 · T"+world.P6Lesson.ToString("00")+"\n"+world.P6TrainingPrompt;
                bool upgrade=world.P6Lesson==8;
                foreach(var id in new[]{"p6-up-radiant","p6-up-accuracy","p6-up-bind"})buttons[id].gameObject.SetActive(upgrade);
                buttons["p6-refill"].gameObject.SetActive(!upgrade);
                SetButtonTitle("p6-refill",world.P6Lesson==7?"练习补充 + 首次命中全力暴击祝福":"补充次数 / 生命 / 重置练习目标 · 仅训练");
                buttons["p6-refill"].interactable=world.State.pendingRoll==null&&!world.State.queue.Any(q=>q.stage=="Queued"||q.stage=="Executing");
                SetButtonTitle("p6-finish",world.P6Lesson>=9?"练习完成 · 进入探索":"跳过训练 · 进入探索");
            }
        }
    }
}

