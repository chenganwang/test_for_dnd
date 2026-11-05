using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed partial class BattleHud
    {
        Text v11CoreText;
        void CreateV11Combat(Transform root)
        {
            for(int i=0;i<2;i++)
            {
                int slot=i;
                Button(focusPanel.transform,"v11-action-"+i,"",18+i*154,141,142,42,()=>
                {
                    var actions=world.AvailableActions(world.SelectedActor);
                    if(slot<actions.Count)world.SelectV11Action(actions[slot]);
                });
                buttons["v11-action-"+i].GetComponentInChildren<Text>().fontSize=14;
            }
            v11CoreText=Label(root,"",18,156,462,23,12,Gold);
            Button(basicTacticsPanel.transform,"v12-assist","队友免费辅助：开",12,206,276,25,()=>world.ToggleV12FreeAssistance());
        }
        void RefreshV11Combat()
        {
            bool enabled=world.V11CombatEnabled;
            v11CoreText.gameObject.SetActive(enabled&&!world.Menu&&!world.DungeonModal&&world.State.activePendingEvent==null);
            v11CoreText.text=world.V11CoreSummary;
            bool visible=enabled&&world.State.mode==CombatMode.TacticalFocus&&!world.P6QuickFocus;
            var definition=V11Catalog.FindCharacter(world.SelectedActor);
            var actions=world.AvailableActions(world.SelectedActor);
            buttons["v12-assist"].gameObject.SetActive(world.V12Enabled&&!world.V13AssistMode&&world.FreeOrdersVisible);
            SetButtonTitle("v12-assist",world.V12FreeAssistance?"队友免费辅助：开":"队友免费辅助：关");
            for(int i=0;i<2;i++)
            {
                string key="v11-action-"+i;
                buttons[key].gameObject.SetActive(visible&&definition!=null&&i<actions.Count);
                if(!buttons[key].gameObject.activeSelf)continue;
                string action=actions[i];bool unlocked=true;
                SetButtonTitle(key,V11AbilityCatalog.Name(action)+(unlocked?"":" · 未解锁"));
                buttons[key].interactable=unlocked;
                buttons[key].GetComponent<Image>().color=world.SelectedAction==action?new Color(.1f,.35f,.35f):new Color(.13f,.21f,.28f);
            }
            if(!enabled){buttons["target"].interactable=true;return;}
            foreach(string key in new[]{"attack","shove","sneak","control"})buttons[key].gameObject.SetActive(false);
            string[] teamButtons={"fighter","rogue","mage"};
            for(int i=0;i<teamButtons.Length;i++)
            {
                string key=teamButtons[i];bool present=i<world.State.actors.Count;
                buttons[key].gameObject.SetActive(visible&&present);
                MoveButton(key,18+i*101,88,92,36);
                if(present)SetButtonTitle(key,(i+1)+" "+ActorName(world.State.actors[i].id));
            }
            bool self=V11AbilityCatalog.IsSelfTarget(world.SelectedAction);
            buttons["target"].interactable=!self;
            SetButtonTitle("target",self?"目标：自身 / 存活小队":"切换目标（也可点击战场）");
            if(visible&&V11AbilityCatalog.IsCustom(world.SelectedAction))
            {
                var q=world.SelectedActionQuote();
                quote.text=V11AbilityCatalog.Name(world.SelectedAction)+"\n"+V11AbilityCatalog.Description(world.SelectedAction)+"\n"+
                    (q.rollType=="Save"?"敌人豁免":"己方检定")+" d20 + "+q.bonus+" / "+(q.rollType=="Attack"?"AC ":"DC ")+q.targetNumber+
                    "\n"+(q.legal?"成功率 "+(q.successProbability*100).ToString("0.#")+"%":HumanReason(q.reason));
                SetButtonTitle("confirm","确认检定 · 支付1次");
            }
            if(visible&&world.SelectedAction==CombatSession.P6BindId)
            {quote.text="裂光协助 · 免费\n点击技能即请求短施法；冷却16秒。\n合法施放附着裂光印；WIS豁免只决定短封缚。";buttons["confirm"].interactable=false;}
            if(visible&&world.V12Enabled&&(world.SelectedAction.StartsWith("v12_")||world.SelectedAction.StartsWith("v13_")||world.SelectedAction.StartsWith("v14_")))
            {
                var q=world.SelectedActionQuote(); bool immediate=BattleWorld.V12ImmediateAction(world.SelectedAction);
                bool free=immediate&&world.SelectedAction!="v12_finn_grove"&&world.SelectedAction!="v13_finn_prayer"&&world.SelectedAction!="v14_finn_detonate";
                quote.text=V11AbilityCatalog.Name(world.SelectedAction)+"\n"+V11AbilityCatalog.Description(world.SelectedAction)+"\n"+
                    (q.legal?(immediate?"合法施放；不播放额外掷骰":"攻击 d20 + "+q.bonus+" 对 AC "+q.targetNumber+(q.advantage?" · 优势":"")):HumanReason(q.reason));
                SetButtonTitle("confirm",free?"施放 · 免费":immediate?"施放区域 · 支付1次":"确认掷骰 · 支付1次");
                buttons["confirm"].interactable=q.legal&&(free||world.State.charges>0);
                if(world.V14Enabled&&world.SelectedAction=="v12_finn_seed")quote.text=quote.text.Replace("8秒",world.Session.Config.v12.seedCooldown.ToString("0.#")+"秒");
                if(world.V14Enabled&&world.SelectedAction=="v14_finn_detonate")
                {
                    var a=world.State.Actor(world.SelectedActor);var target=world.State.Actor(world.SelectedTarget);
                    var preview=world.Session.PreviewV14Detonation(world.SelectedActor,world.SelectedTarget,a!=null&&target!=null&&world.HasLineOfEffect(world.CastOrigin(a),BattleWorld.Position(target)));
                    quote.text="寄生爆绽 · 支付1指挥次数\n"+(preview.legal?"自己寄生的剩余 "+preview.remainingTicks+" 跳 · 来源伤害 "+preview.sourceDamage.ToString("0.##")+"\n结算时应用目标防御；移除未来跳伤。":"需要7米内可见且仍有自己寄生的敌人。")+"\n0.35秒施放 · 不掷骰、不暴击";
                    SetButtonTitle("confirm","引爆剩余寄生 · 1次");buttons["confirm"].interactable=q.legal&&preview.legal;
                }
            }
        }
    }
}
