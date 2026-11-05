using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed partial class BattleHud
    {
        GameObject v13FatePanel,v13AssistPayment;
        Text v13FateText,v13FateDice,v13Notice;
        FrontendSilhouette[] v13AssistSilhouettes=new FrontendSilhouette[2];
        void CreateV13(Transform root)
        {
            v13Notice=Label(root,"",390,169,490,32,15,Gold,TextAnchor.MiddleCenter);
            for(int i=0;i<2;i++)
            {
                int slot=i;var button=Button(root,"v13-assist-"+i,"",18+i*224,620,214,46,()=>world.RequestAssist(slot));
                var silhouette=new GameObject("连携剪影",typeof(RectTransform),typeof(CanvasRenderer),typeof(FrontendSilhouette));silhouette.transform.SetParent(button.transform,false);Rect(silhouette.GetComponent<RectTransform>(),4,3,33,40);v13AssistSilhouettes[i]=silhouette.GetComponent<FrontendSilhouette>();v13AssistSilhouettes[i].raycastTarget=false;
                button.GetComponentInChildren<Text>().fontSize=14;
            }
            Button(postPanel.transform,"v13-correct","埃文校正 +2 · 灵感1 / Reaction",18,328,312,32,()=>world.V13Correct());
            Button(postPanel.transform,"v14-review","埃文复核 · 强制采用整组新骰",18,364,312,32,()=>world.V13Correct("reroll"));
            Button(reactionPanel.transform,"v14-reaction-correct","埃文校正 +2",14,250,220,34,()=>world.V13Correct());
            Button(reactionPanel.transform,"v14-reaction-review","埃文复核 · 新骰",240,250,220,34,()=>world.V13Correct("reroll"));
            buttons["v14-reaction-correct"].GetComponentInChildren<Text>().fontSize=14;buttons["v14-reaction-review"].GetComponentInChildren<Text>().fontSize=14;
            v13AssistPayment=Panel(root,"连携费用确认",420,330,440,166,Ink);
            Label(v13AssistPayment.transform,"至圣斩支援 · 消耗1指挥次数\n按现有原始骰与伤害结算，不购买全力。",16,14,408,62,18,Gold);
            Button(v13AssistPayment.transform,"v13-paid-yes","授权并施放 · 1次",16,99,198,42,()=>world.ConfirmPaidAssist());
            Button(v13AssistPayment.transform,"v13-paid-no","返回",224,99,198,42,()=>world.CancelPaidAssist());
            v13FatePanel=Panel(root,"命运潮汐",345,190,590,350,Ink);
            var canvas=v13FatePanel.AddComponent<Canvas>();canvas.overrideSorting=true;canvas.sortingOrder=8;v13FatePanel.AddComponent<GraphicRaycaster>();
            Label(v13FatePanel.transform,"命运潮汐 · D100",18,12,554,34,23,Gold,TextAnchor.MiddleCenter);
            v13FateDice=Label(v13FatePanel.transform,"",18,48,554,68,39,Color.white,TextAnchor.MiddleCenter);
            v13FateText=Label(v13FatePanel.transform,"",24,120,542,151,17,Color.white);
            Button(v13FatePanel.transform,"v13-fate-accept","接受原结果",22,289,263,42,()=>world.V13ChooseFate(false));
            Button(v13FatePanel.transform,"v13-fate-replace","用回声替换 · 灵感1",302,289,263,42,()=>world.V13ChooseFate(true));
        }
        void RefreshV13()
        {
            bool playing=world.V13Enabled&&!world.Menu&&!world.DungeonModal;
            var state=world.State;var f=state.v13?.fate;bool deciding=playing&&f?.stage=="Deciding";bool rolling=playing&&f?.stage=="Rolled";
            v13FatePanel.SetActive(deciding||rolling);v13AssistPayment.SetActive(playing&&world.V13PaidAssist!=null);
            v13Notice.gameObject.SetActive(playing&&!world.IsFrozen&&!world.IsFocus&&state.activePendingEvent==null&&!deciding&&!rolling);
            v13Notice.text=playing?(f?.stage=="Scheduled"?"◇ 潮汐将至 · "+f.warning.ToString("0.0")+"s · 等待安全空隙":state.v13.ruleUntil>state.simulationSeconds&&state.v13.activeRule>0?"◇ "+CombatSession.FateName(new[]{0,1,9,17,25,35,45,55,65,75,83,91,97}[state.v13.activeRule])+" · "+(state.v13.ruleUntil-state.simulationSeconds).ToString("0.0")+"s":"1 / 2 连携 · Tab 精确指挥 · 本局核心固定"):"";
            for(int i=0;i<2;i++)
            {
                string key="v13-assist-"+i;buttons[key].gameObject.SetActive(playing&&world.V13AssistMode&&!world.IsFocus&&!world.IsFrozen);
                if(!playing||!world.V13AssistMode)continue;string id=world.V13Assists[i];var a=state.Actor(id);float cooldown=a.v12?.basicCooldown??0;
                SetButtonTitle(key,(i+1)+"  "+ActorName(id)+(cooldown>0?"  "+cooldown.ToString("0.0")+"s":id==P6CharacterCatalog.PaladinId?"  1次":"  连携"));
                buttons[key].interactable=cooldown<=0&&a.windupRemaining<=0&&!CombatSession.V12ActionActive(a)&&state.activePendingEvent==null;
                v13AssistSilhouettes[i].Key=V11Catalog.FindCharacter(id)?.silhouetteId??id;
            }
            bool correction=playing&&state.mode==CombatMode.PostRollDecision&&state.Actor(CombatSession.V12Evan)!=null&&state.pendingRoll!=null;
            buttons["v13-correct"].gameObject.SetActive(correction);
            buttons["v14-review"].gameObject.SetActive(correction&&world.V14Enabled&&world.Session.V14CanReview);
            bool reactionCorrection=correction&&world.V14Enabled&&state.pendingRoll.purpose=="Reaction";
            buttons["v14-reaction-correct"].gameObject.SetActive(reactionCorrection);
            buttons["v14-reaction-review"].gameObject.SetActive(reactionCorrection&&world.Session.V14CanReview);
            if(world.V14Enabled){Rect(postPanel.GetComponent<RectTransform>(),913,108,353,540);MoveButton("v13-correct",18,343,312,32);MoveButton("v14-review",18,380,312,32);MoveButton("commit",18,422,312,40);MoveButton("cancel",18,470,312,36);}
            else{Rect(postPanel.GetComponent<RectTransform>(),913,108,353,478);MoveButton("commit",18,correction?369:343,312,44);MoveButton("cancel",18,correction?420:402,312,43);MoveButton("v13-correct",18,328,312,32);}
            if(correction){var t=state.pendingRoll;var evan=state.Actor(CombatSession.V12Evan);SetButtonTitle("v13-correct","校正 "+t.total+" → "+(t.total+2)+" · "+(t.selectedFace==1&&t.rollType=="Attack"?"自然1仍失败":t.selectedFace==20&&t.rollType=="Attack"?"自然20成功":t.total+2>=t.targetNumber?"成功":"仍失败")+" · 灵感1");buttons["v13-correct"].interactable=state.actors.Any(a=>a.id==t.rollOwnerId)&&(t.rollType=="Attack"||t.rollType=="Defense"||t.rollType=="Check")&&!t.modifyUsed&&!t.precisionUsed&&evan.inspiration>0&&evan.reactionRemaining<=0;}
            if(playing&&world.IsFocus){var selected=state.Actor(world.SelectedActor);if(selected!=null&&!world.IsPhysical(selected))actorInfo.text=ActorName(selected.id)+" · 连携\n灵感 "+selected.inspiration+" · 反应 "+selected.reactionRemaining.ToString("0.0")+"s\n技能CD "+selected.v12.basicCooldown.ToString("0.0")+"s"+(selected.id==CombatSession.V12Finn&&world.Session.Config.v13.finnTree=="guard"?" · 祈愿 "+world.Session.Member(state,selected.id).prayers:"");}
            if(playing){if(world.V13AssistMode){basicTacticsPanel.SetActive(false);tacticalSpatialPanel.SetActive(false);}if(world.SelectedAction=="v13_evan_correct"&&state.mode==CombatMode.TacticalFocus){quote.text="校正 · 授权消耗埃文灵感1与Reaction\n目标："+ActorName(world.SelectedTarget)+"；8秒内下次合格检定+2；到期不退。\n骰后可改用同一校正；自然1/20规则不变。";SetButtonTitle("confirm","授权预校正 · 灵感1");buttons["confirm"].interactable=state.Actor(CombatSession.V12Evan)?.inspiration>0;}if(world.V13NodeAvailable(world.SelectedTarget)){quote.text="祷歌节点 · 合法直接攻击即可破坏\n减少一条光带；两节点全破打断祷歌。";buttons["confirm"].interactable=true;}}
            if(rolling)
            {int tens=(int)(Time.unscaledTime*23)%10*10,units=(int)(Time.unscaledTime*37)%10;v13FateDice.text=tens.ToString("00")+"  +  "+units;v13FateText.text="百分骰落定中 · 世界冻结\n00 + 0 = 100；原结果已经保存。";}
            if(deciding)
            {
                int tens=f.original==100?0:f.original/10*10,units=f.original%10;v13FateDice.text=tens.ToString("00")+" + "+units+" = "+f.original;
                v13FateText.text=CombatSession.FateName(f.original)+"\n"+CombatSession.FateDetail(f.original)+"\n"+(f.echoId!=null?"回声原始面 "+f.echoRaw+" → "+(f.echoRaw*5)+"「"+CombatSession.FateName(f.echoRaw*5)+"」 · 核心灵感1":"暂无可用的已执行付费攻击回声")+"\n"+f.decision.ToString("0.0")+"秒后接受原结果";
            }
            buttons["v13-fate-accept"].interactable=deciding;buttons["v13-fate-replace"].interactable=deciding&&f.echoId!=null&&state.v13.echo?.id==f.echoId&&state.Actor(state.v13.coreId).inspiration>0;
            if(playing&&world.V14Enabled)
            {
                var correctionQuote=world.Session.QuoteV14Correction(world.SelectedTarget);
                string cost=correctionQuote.free?"本场首次免灵感 · Reaction":"灵感1 · Reaction";
                if(correction){buttons["v13-correct"].interactable=correctionQuote.legal;SetButtonTitle("v13-correct","校正 +2 · "+cost);buttons["v14-review"].interactable=correctionQuote.legal&&correctionQuote.canReroll;}
                if(reactionCorrection){Rect(reactionPanel.GetComponent<RectTransform>(),788,185,474,300);SetButtonTitle("v14-reaction-correct","埃文校正 +2 · "+(correctionQuote.free?"免灵感":"灵感1"));buttons["v14-reaction-correct"].interactable=correctionQuote.legal;buttons["v14-reaction-review"].interactable=correctionQuote.legal&&correctionQuote.canReroll;}
                if(state.pendingRoll!=null&&(state.pendingRoll.modifyUsed||state.pendingRoll.rerollUsed||state.pendingRoll.precisionUsed))foreach(var key in new[]{"modify","reroll","precision","reaction-modify","reaction-reroll"})buttons[key].interactable=false;
                if(world.IsFocus&&world.SelectedAction=="v13_evan_correct"){quote.text="埃文校正 · "+cost+"\n目标："+ActorName(world.SelectedTarget)+"；8秒内下次合格检定+2。\n同一次检定只允许一次人工干预。";SetButtonTitle("confirm","授权预校正 · "+cost);buttons["confirm"].interactable=correctionQuote.legal;}
                if(!world.V13AssistMode&&f?.stage!="Scheduled"&&state.v13.ruleUntil<=state.simulationSeconds)v13Notice.text="1 / 2 / 3 切角色 · Tab 精确指挥 · 核心："+ActorName(state.v13.coreId);
            }
        }
    }
}

