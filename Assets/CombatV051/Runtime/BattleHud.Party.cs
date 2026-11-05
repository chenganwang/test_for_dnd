using System.Linq;
using System.Collections.Generic;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed partial class BattleHud
    {
        GameObject partyRoster, reactionOfferPanel, reactionPanel;
        Text reactionOfferText, reactionInfo, reactionHeading;
        Image reactionTimeFill;
        public bool ReactionCompactVisible => reactionPanel!=null&&reactionPanel.activeInHierarchy;
        public string ReactionCompactText => ReactionCompactVisible?reactionInfo.text:"";
        string offeredWindowId;
        readonly List<ReactionQuote> renderedReactions = new List<ReactionQuote>();
        int reactionPage;
        string displayedReactionEvent;

        void CreatePartyPanels(Transform root)
        {
            partyRoster = Panel(root, "Party roster", 17, 103, 318, 142, Ink);
            var ids = V11Catalog.Characters.Select(c=>c.id).ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                string actorId = ids[i];
                Button(partyRoster.transform, "active-" + actorId, ActorName(actorId), 8, 5 + i * 45, 302, 40, () =>
                {
                    if (world.State.mode == CombatMode.TacticalFocus || world.FreeOrdersVisible) world.SelectCommandActor(actorId);
                    else if(world.V13AssistMode){int slot=System.Array.IndexOf(world.V13Assists,actorId);if(slot>=0)world.RequestAssist(slot);}else world.SwitchActive(actorId);
                });
            }
            reactionOfferPanel = Panel(root, "Reaction offered", 366, 447, 490, 142, Ink);
            reactionOfferText = Label(reactionOfferPanel.transform, "", 14, 10, 462, 64, 18, Gold);
            Button(reactionOfferPanel.transform, "reaction-offer-accept", "处理反应 [R]", 14, 86, 223, 40,
                () => world.React(CommandKind.BeginReaction, offeredWindowId));
            Button(reactionOfferPanel.transform, "reaction-offer-decline", "承受原攻击", 251, 86, 223, 40,
                () => world.React(CommandKind.DeclineReaction, offeredWindowId));

            reactionPanel = Panel(root, "Reaction decision", 788, 185, 474, 341, Ink);
            var reactionCanvas=reactionPanel.AddComponent<Canvas>();reactionCanvas.overrideSorting=true;reactionCanvas.sortingOrder=6;reactionPanel.AddComponent<GraphicRaycaster>();
            reactionHeading=Label(reactionPanel.transform, "REACTION", 14, 10, 446, 28, 21, Teal);
            Decor(reactionPanel.transform,"Reaction time track",14,43,446,4,new Color(.2f,.25f,.27f));
            reactionTimeFill=Decor(reactionPanel.transform,"Reaction time left",14,43,446,4,Gold).GetComponent<Image>();
            reactionInfo = Label(reactionPanel.transform, "", 14, 56, 446, 72, 14, Color.white);
            for (int i = 0; i < 6; i++)
            {
                int slot = i;
                Button(reactionPanel.transform, "reaction-choice-" + i, "", 14 + i % 2 * 226, 136 + i / 2 * 44, 220, 40,
                    () => { if (slot < renderedReactions.Count && world.SelectReactionChoice(renderedReactions[slot]).success) world.ConfirmCurrentDecision(); });
                buttons["reaction-choice-"+i].GetComponentInChildren<Text>().fontSize=13;
            }
            Button(reactionPanel.transform, "reaction-page", "更多合法方案", 14, 270, 446, 23, () => reactionPage++);
            Button(reactionPanel.transform, "reaction-confirm", "确认反应", 14, 299, 220, 32,
                () => world.ConfirmCurrentDecision());
            Button(reactionPanel.transform, "reaction-decline", "放弃剩余反应 / 承受原攻击", 240, 299, 220, 32,
                () => world.React(CommandKind.DeclineReaction));
            Button(reactionPanel.transform,"reaction-modify","灵感 +2 / 1点",14,155,220,34,()=>world.Command(CommandKind.Modify));
            Button(reactionPanel.transform,"reaction-reroll","重掷 / 灵感1",240,155,220,34,()=>world.Command(CommandKind.Reroll));
            Button(reactionPanel.transform,"reaction-commit","接受结果",14,205,220,36,()=>world.ConfirmCurrentDecision());
            Button(reactionPanel.transform,"reaction-cancel","放弃 / 承受来袭",240,205,220,36,()=>world.CancelCurrentDecision());
        }

        void RefreshPartyPanels()
        {
            var state = world.State;
            phaseLabel.text = world.PartyPresentation ? "v0.5.1 · P1 三人小队" : "v0.5.1 · P0 战斗试验场";
            menuTitle.text = world.PartyPresentation ? "P1 · 三人小队战斗" : "P0 · 战斗流程灰盒";
            menuInfo.text = world.PartyPresentation ?
                "固定 3/4 视角 · 战士 / 游荡者 / 法师\n\n1 / 2 / 3 切换主控；队友自行移动和基础攻击。\nTab 专注中选择队员、行动、目标；确认后掷骰。\nR 处理反应：护盾直接使用，格挡需要 D20。\n\n[剧情待作者填写]" :
                "固定 3/4 视角 · 一名 Fighter · 一名敌人\n\n近斩 / 格挡 / 闪避积累 Momentum。\nTab 专注中选择行动与目标，确认掷骰。\n骰后决策 → Commit → Tab 返回实时执行。\n\n[剧情待作者填写]";
            endTitle.text = state.actors.Any(a => a.Alive) ? "遭遇完成" : "小队倒下";
            endInfo.text = "战斗快照和本次结果已保留。\n可返回标题，继续保存的结果或开始新尝试。\n\n[后续故事待作者填写]";
            partyRoster.SetActive(!world.Menu && world.PartyMode);
            Rect(actorInfo.rectTransform, 25, world.PartyMode ? 256 : 110, 320, 163);
            Rect(enemyInfo.rectTransform, 25, world.PartyMode ? 434 : 281, 320, 140);
            foreach(string id in V11Catalog.Characters.Select(c=>c.id))buttons["active-"+id].gameObject.SetActive(state.Actor(id)!=null);
            foreach (var actor in state.actors)
            {
                string key = "active-" + actor.id;
                if (!buttons.ContainsKey(key)) continue;
                SetButtonTitle(key, ActorName(actor.id) + (state.activeActorId == actor.id ? " [主控]" : " [队友]") + (world.V13Enabled&&!world.IsPhysical(actor)?"  连携 CD "+(actor.v12?.basicCooldown??0).ToString("0.0")+"s":"  HP " + actor.hp + "/" + actor.maxHp));
                buttons[key].interactable = actor.Alive && !world.IsFrozen && state.mode != CombatMode.ReactionPrompt;
                buttons[key].GetComponent<Image>().color = state.activeActorId == actor.id ? new Color(.08f,.3f,.3f) : new Color(.13f,.21f,.28f);
            }
            buttons["focus"].interactable = !world.Menu && !world.IsFrozen && state.pendingRoll == null && state.activePendingEvent == null && state.inCombat && (state.mode==CombatMode.TacticalFocus || state.focusBudget.secondsRemaining>0);
            var offered = state.reactionWindows.FirstOrDefault(w => w.stage == "Offered");
            offeredWindowId = offered?.id;
            bool offerVisible = offered != null && state.activePendingEvent == null && !world.Menu && !world.IsFrozen;
            reactionOfferPanel.SetActive(offerVisible);
            if (offerVisible)
            {
                var actor = state.Actor(offered.defenderId);
                reactionOfferText.text = ActorName(offered.defenderId) + " 受到威胁 · R 查看合法反应" +
                    "\n由可用队员保护 · 不花战术次数";
            }
            RefreshCompactReaction();
        }
        void RefreshCompactReaction()
        {
            var state=world.State;var group=state.activePendingEvent;var window=state.activeReaction;
            bool post=state.mode==CombatMode.PostRollDecision&&state.pendingRoll?.purpose=="Reaction";
            bool show=!world.Menu&&!world.DungeonModal&&group!=null&&(state.mode==CombatMode.ReactionPrompt||post);
            reactionPanel.SetActive(show);if(!show)return;
            bool hasSelection=window!=null;
            reactionHeading.text=(post?"反应结果":"紧急反应")+"    "+group.remainingUiSeconds.ToString("0.0")+"s";
            reactionTimeFill.rectTransform.sizeDelta=new Vector2(446*Mathf.Clamp01(group.remainingUiSeconds/CombatSession.P6ReactionSeconds(state)),4);
            reactionTimeFill.color=group.remainingUiSeconds<.75f?new Color(1,.35f,.25f):Gold;
            var incoming=state.incomingEvents.Where(e=>e.reactionEventId==group.id&&!e.damageApplied).ToList();
            reactionInfo.text=string.Join("\n",incoming.Select(e=>world.DisplayTarget(e.attackerId)+" → "+ActorName(e.defenderId)+"   "+
                (e.sourceKind=="Save"?"豁免 / DC "+e.dc:e.total+" 对 AC "+e.effectiveArmorClass)+" · "+e.prospectiveDamage+"伤害"));
            foreach(string id in new[]{"reaction-modify","reaction-reroll","reaction-commit","reaction-cancel"})buttons[id].gameObject.SetActive(post);
            buttons["reaction-confirm"].gameObject.SetActive(!post&&hasSelection);
            buttons["reaction-decline"].gameObject.SetActive(!post);
            if(!post)
            {
                Rect(reactionPanel.GetComponent<RectTransform>(),788,185,474,341);
                if(displayedReactionEvent!=group.id){displayedReactionEvent=group.id;reactionPage=0;}
                var choices=world.Session.GetReactionChoices(group.id).Where(q=>q.legal).ToList();
                int pages=Mathf.Max(1,(choices.Count+5)/6);reactionPage%=pages;
                renderedReactions.Clear();renderedReactions.AddRange(choices.Skip(reactionPage*6).Take(6));
                for(int i=0;i<6;i++)
                {
                    var button=buttons["reaction-choice-"+i];button.gameObject.SetActive(i<renderedReactions.Count);
                    if(i>=renderedReactions.Count)continue;var q=renderedReactions[i];
                    SetButtonTitle("reaction-choice-"+i,ActorName(q.actorId)+" · "+(q.requiresD20?"格挡掷骰":ReactionName(q.reactionId))+
                        (q.actorId!=q.defenderId?" → "+ActorName(q.defenderId):"")+"\n反应1"+(q.spellCost>0?" + 法术位"+q.spellCost:q.superiorityCost>0?" + 战技骰"+q.superiorityCost:""));
                }
                buttons["reaction-page"].gameObject.SetActive(pages>1);
                float footer=144+Mathf.Ceil(renderedReactions.Count/2f)*44+(pages>1?27:0);
                Rect(reactionPanel.GetComponent<RectTransform>(),788,185,474,footer+46);
                MoveButton("reaction-page",14,footer-27,446,23);MoveButton("reaction-confirm",14,footer,220,32);
                MoveButton("reaction-decline",hasSelection?240:14,footer,hasSelection?220:446,32);
                SetButtonTitle("reaction-decline","不反应 · 承受上述伤害");
                if(hasSelection)
                {
                    var q=world.Session.QuoteReaction(window.id,window.reactorId??window.defenderId,window.reactionId);
                    SetButtonTitle("reaction-confirm",q.requiresD20?"确认格挡掷骰 [Enter]":"确认反应 [Enter]");buttons["reaction-confirm"].interactable=q.legal;
                }
            }
            else
            {
                Rect(reactionPanel.GetComponent<RectTransform>(),788,185,474,257);
                foreach(var id in new[]{"reaction-choice-0","reaction-choice-1","reaction-choice-2","reaction-choice-3","reaction-choice-4","reaction-choice-5","reaction-page"})buttons[id].gameObject.SetActive(false);
                var t=state.pendingRoll;var a=state.Actor(t.actorId);
                reactionInfo.text+="\n原始 "+string.Join(" / ",t.rawFaces)+" → 总值 "+t.total+" / 目标 "+t.targetNumber+"   "+(t.hit?"格挡成功":"格挡失败")+
                    "\n反应已付；接受时消耗战技骰1。超时"+(state.reactionSettings.timeoutAcceptPaidRoll?"接受已付结果":"放弃");
                buttons["reaction-modify"].interactable=a.inspiration>0&&!t.modifyUsed;
                buttons["reaction-reroll"].interactable=a.inspiration>0&&!t.rerollUsed;
            }
        }

        void SetButtonTitle(string id, string value) => buttons[id].GetComponentInChildren<Text>().text = value;
        static string Ready(float remaining) => remaining <= 0 ? "可用" : remaining.ToString("0.0") + "s";
        static string ReactionName(string id) => id == "shield" ? "护盾" : id == "protect" ? "保护队友" : "反应格挡";
        static string ActorName(string id)
        {
            var definition=V11Catalog.FindCharacter(id);if(definition!=null)return definition.displayName;
            switch (id) { case "char_demo_paladin": return "圣武士"; case "fighter": return "战士"; case "rogue": return "游荡者"; case "mage": return "法师"; default: return id ?? "未选择"; }
        }
        static string ActionName(string id)
        {
            switch (id) { case "attack": return "投枪"; case "shove": return "推击 / STR Save"; case "sneak": return "绕背突袭"; case "control": return "定身 / WIS Save"; default: return id; }
        }
        static string SourceName(string source)
        {
            if(source!=null&&source.StartsWith("协同："))return source.Replace("off_balance","失衡").Replace("exposed","破绽").Replace("bound","受制").Replace("fighter","战士").Replace("rogue","游荡者").Replace("mage","法师");
            if (source != null && source.StartsWith("Precision +")) return "战技骰 +" + source.Substring(11);
            switch (source)
            {
                case "Rear position": return "背后站位：优势";
                case "Aim": return "稳准瞄准：优势";
                case "Target staggered": return "目标失衡";
                case "Flanking": return "夹击：优势";
                case "Suppressed": return "受压制：劣势";
                case "Target save advantage": return "敌人豁免优势";
                case "Inspiration +2": return "灵感 +2";
                default: return source;
            }
        }
    }
}
