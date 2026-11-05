using CombatV051.Domain;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed partial class BattleHud
    {
        GameObject basicTacticsPanel;
        Text basicOrderText;
        void CreateBasicTactics(Transform root)
        {
            Button(root,"free-orders","基础指令 / 实时",16,674,240,34,()=>world.ToggleFreeOrders());
            basicTacticsPanel = Panel(root, "Free squad orders", 16, 426, 300, 238, Ink);
            Label(basicTacticsPanel.transform, "基础战术 / 免费", 12, 8, 276, 25, 18, Teal);
            basicOrderText = Label(basicTacticsPanel.transform, "", 12, 39, 276, 54, 13, Color.white);
            string[] keys = { "move", "rear", "hold", "target", "follow", "prepare", "replace", "cancel" };
            string[] labels = { "移动 / 点地面", "前往合法后侧", "守位", "集中目标", "跟随主控", "职业准备", "替换指令", "取消指令" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                Button(basicTacticsPanel.transform, "order-" + key, labels[i], 12 + i % 2 * 142, 98 + i / 2 * 33, 134, 28, () => world.ChooseBasicTactic(key));
            }
            for(int i=0;i<3;i++)
            {
                int slot=i;
                Button(basicTacticsPanel.transform,"p5-opportunity-"+i,"—",12+i*94,70,88,30,()=>
                {var choices=world.OpportunityChoices();if(slot<choices.Count)world.ChooseOpportunity(choices[slot].id);});
                var label=buttons["p5-opportunity-"+i].GetComponentInChildren<Text>();label.fontSize=10;label.lineSpacing=.85f;Rect(label.rectTransform,2,0,84,30);
            }
            Button(basicTacticsPanel.transform,"order-followup","接应 [F] · 替代普攻",12,206,186,25,()=>world.ChooseBasicTactic("followup"));
            Button(basicTacticsPanel.transform,"p5-clear","不用机会",204,206,84,25,()=>world.ChooseOpportunity(null));
            buttons["p5-clear"].GetComponentInChildren<Text>().fontSize=13;
        }
        string P5PreparationSummary(ActorState actor)
        {
            var prep=actor.p5?.preparation;
            if(prep!=null&&!prep.used)
                return !prep.ready?"准备中 "+prep.remaining.ToString("0.0")+"s":actor.role==ActorRole.Fighter?"守势：化解承诺攻击→失衡":actor.role==ActorRole.Rogue?"侧翼就绪：下一击→破绽":"凝霜就绪：下一击→减速/受制";
            return actor.role==ActorRole.Fighter?"守势化解→失衡；队友 F 接应":actor.role==ActorRole.Rogue?"侧翼移动后命中→破绽":"凝霜命中→受制；队友推撞/接应";
        }
        void RefreshBasicTactics()
        {
            if (basicTacticsPanel == null) return;
            bool reaction=world.State.activePendingEvent!=null;
            bool visible = !world.Menu && !reaction && !world.IsFrozen && (world.State.mode == CombatMode.TacticalFocus || world.FreeOrdersVisible) && !world.DungeonModal;
            buttons["free-orders"].gameObject.SetActive(!world.V13AssistMode&&!world.Menu&&!reaction&&!world.IsFrozen&&!world.IsFocus&&world.State.inCombat&&!world.DungeonModal);
            SetButtonTitle("free-orders",world.FreeOrdersVisible?"收起指令 / 世界实时":"基础指令 / 实时");
            basicTacticsPanel.SetActive(visible);
            buttons["order-followup"].gameObject.SetActive(world.P5Enabled);
            buttons["p5-clear"].gameObject.SetActive(world.P5Enabled);
            for(int i=0;i<3;i++)buttons["p5-opportunity-"+i].gameObject.SetActive(world.P5Enabled);
            if (!visible) return;
            tacticalSpatialPanel.SetActive(false);
            string rearReason;
            bool rearLegal = world.TryGetRearDestination(out _, out rearReason);
            basicOrderText.text = world.PendingOrderLabel != null ? "待覆盖：" + world.PendingOrderLabel : world.MoveOrderArmed ? "点击地面选择可达落点。" :
                ActorName(world.SelectedActor)+" · "+world.SelectedOrderSummary + (world.SelectedAction == "sneak" ? "\n" + (rearLegal ? "后侧落点可达；实际到位才可掷骰。" : HumanReason(rearReason)) : "");
            buttons["order-rear"].interactable = rearLegal;
            buttons["order-replace"].interactable = world.PendingOrderLabel != null;
            SetButtonTitle("order-prepare", world.State.Actor(world.SelectedActor).role == ActorRole.Fighter ? "守势 / 护卫" : world.State.Actor(world.SelectedActor).role == ActorRole.Rogue ? "真实侧翼移动" : "观察 / 稳定");
            if(world.P5Enabled)
            {
                Rect(basicOrderText.rectTransform,12,35,276,32);basicOrderText.fontSize=12;
                string[] keys={"move","rear","hold","target","follow","prepare","replace","cancel"};
                for(int i=0;i<keys.Length;i++)MoveButton("order-"+keys[i],12+i%2*142,102+i/2*26,134,24);
                var choices=world.OpportunityChoices();
                for(int i=0;i<3;i++)
                {
                    var b=buttons["p5-opportunity-"+i];var c=i<choices.Count?choices[i]:null;
                    SetButtonTitle("p5-opportunity-"+i,c==null?"—":BattleWorld.OpportunityName(c.kind)+"\n"+(c.neutral?"环境":ActorName(c.ownerId))+" "+c.remaining.ToString("0.0")+"s");
                    // Illegal choices stay visible and explain themselves in the quote; no reservation here.
                    b.interactable=c!=null;
                    b.GetComponent<Image>().color=c!=null&&c.id==world.SelectedOpportunity?new Color(.18f,.40f,.34f):new Color(.13f,.21f,.28f);
                }
                var actor=world.State.Actor(world.SelectedActor);
                SetButtonTitle("order-prepare",actor.role==ActorRole.Fighter?"守势 [Shift]":actor.role==ActorRole.Rogue?"侧翼准备 [Shift]":"凝霜准备 [Shift]");
                buttons["order-followup"].interactable=world.OpportunityChoices("basic_followup").Any(c=>c.legal);
                basicOrderText.text=world.PendingOrderLabel!=null?"待替换："+world.PendingOrderLabel:
                    ActorName(world.SelectedActor)+" · "+world.SelectedOrderSummary+"\n"+P5PreparationSummary(actor);
            }
            else
            {
                Rect(basicOrderText.rectTransform,12,39,276,54);basicOrderText.fontSize=13;
                string[] keys={"move","rear","hold","target","follow","prepare","replace","cancel"};
                for(int i=0;i<keys.Length;i++)MoveButton("order-"+keys[i],12+i%2*142,98+i/2*33,134,28);
            }
        }
    }
}
