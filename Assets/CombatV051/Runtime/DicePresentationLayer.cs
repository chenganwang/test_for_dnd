using System;
using System.Text;
using System.Globalization;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    /// <summary>Read-only dice presentation on its own Canvas. It never executes commands or draws RNG.</summary>
    public sealed class DicePresentationLayer : MonoBehaviour
    {
        public const float SettleFraction=.82f, ModifiersFraction=.865f, TotalFraction=.90f, DefenseFraction=.93f, BreakdownFraction=.96f;
        public static readonly float[] BounceFractions={SettleFraction*.22f,SettleFraction*.52f,SettleFraction*.8f};
        BattleWorld world;
        Func<string,string> actorName,actionName,sourceName;
        GameObject content;
        RectTransform tray;
        Image dim,decisionPaper;
        Text title,kind,rawText,modifiers,total,defense,outcome,damageFormula;
        readonly D20Graphic[] dice=new D20Graphic[2];
        readonly Text[] adoption=new Text[2];
        readonly StringBuilder builder=new StringBuilder(240);
        string ticketId;
        int face0=-1,face1=-1,lastTotal=int.MinValue,lastStage=-1;
        bool lastFrozen;float lastMarkMultiplier=-1;
        public Canvas Canvas {get;private set;}
        public float Progress {get;private set;}
        public bool Settled => Progress>=SettleFraction;
        public bool BreakdownVisible => content!=null&&content.activeSelf&&Progress>=BreakdownFraction;
        public bool IsVisible => content!=null&&content.activeSelf;
        public int SelectedDieIndex {get;private set;}
        public int VisibleDiceCount {get;private set;}
        public string BreakdownText => rawText.text+"\n"+modifiers.text+"\n"+total.text+"\n"+defense.text+"\n"+outcome.text+(damageFormula.gameObject.activeSelf?"\n"+damageFormula.text:"");
        public D20Graphic DieAt(int index)=>dice[index];

        public void Initialize(BattleWorld owner,Font font,Func<string,string> actor,Func<string,string> action,Func<string,string> source)
        {
            world=owner;actorName=actor;actionName=action;sourceName=source;
            var root=new GameObject("Dice animation canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));root.transform.SetParent(transform,false);
            Canvas=root.GetComponent<Canvas>();Canvas.renderMode=RenderMode.ScreenSpaceOverlay;Canvas.sortingOrder=8;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            content=new GameObject("Dice presentation pool",typeof(RectTransform));content.transform.SetParent(root.transform,false);Stretch(content.GetComponent<RectTransform>());
            var shade=new GameObject("Subtle battlefield shade",typeof(RectTransform),typeof(Image));shade.transform.SetParent(content.transform,false);Stretch(shade.GetComponent<RectTransform>());
            dim=shade.GetComponent<Image>();dim.color=new Color(.027f,.023f,.035f,.34f);dim.raycastTarget=false;
            var panel=new GameObject("Dice tray",typeof(RectTransform));panel.transform.SetParent(content.transform,false);tray=panel.GetComponent<RectTransform>();
            var paper=new GameObject("Retained result paper",typeof(RectTransform),typeof(Image),typeof(Outline));paper.transform.SetParent(tray,false);
            decisionPaper=paper.GetComponent<Image>();decisionPaper.color=new Color(.065f,.057f,.045f,.88f);decisionPaper.raycastTarget=false;
            paper.GetComponent<Outline>().effectColor=new Color(.45f,.35f,.18f,.65f);paper.GetComponent<Outline>().effectDistance=new Vector2(1,-1);
            title=Label(tray,"Action",font,23);kind=Label(tray,"Roll type",font,17);
            for(int i=0;i<2;i++)
            {
                var die=new GameObject("Projected d20 "+i,typeof(RectTransform),typeof(CanvasRenderer),typeof(D20Graphic));die.transform.SetParent(tray,false);
                dice[i]=die.GetComponent<D20Graphic>();dice[i].Initialize(font);
                adoption[i]=Label(tray,"Adopted raw "+i,font,17);
            }
            rawText=Label(tray,"Raw faces",font,18);modifiers=Label(tray,"Modifiers",font,17);total=Label(tray,"Final total",font,22);defense=Label(tray,"AC or DC",font,18);outcome=Label(tray,"Outcome",font,21);damageFormula=Label(tray,"Smite damage formula",font,16);
            title.color=new Color(.94f,.82f,.58f);kind.color=new Color(.72f,.69f,.60f);
            content.SetActive(false);
        }
        void LateUpdate()=>RefreshPresentation();
        public void RefreshPresentation()
        {
            if(world==null||world.Session==null)return;
            var s=world.State;var t=s.pendingRoll;
            bool frozen=s.mode==CombatMode.RollFreeze;
            bool show=!world.Menu&&!world.DungeonModal&&t!=null&&(frozen||(s.mode==CombatMode.PostRollDecision&&t.purpose!="Reaction"));
            if(content.activeSelf!=show)content.SetActive(show);if(!show)return;
            Progress=frozen?Mathf.Clamp01(world.RollElapsed/world.CurrentRollDuration):1;
            bool pair=t.rawFaces.Count==2;VisibleDiceCount=pair?2:1;
            int first=t.rawFaces.Count>0?t.rawFaces[0]:1,second=pair?t.rawFaces[1]:-1;
            float markMultiplier=t.p6==null?0:SmiteFormulaPresentation.MarkMultiplier(s,t.targetId,t.p6.markId);
            bool newResult=ticketId!=t.id||face0!=first||face1!=second||lastTotal!=t.total||lastMarkMultiplier!=markMultiplier;
            if(newResult)
            {
                ticketId=t.id;face0=first;face1=second;lastTotal=t.total;lastMarkMultiplier=markMultiplier;
                dice[0].SetRawFace(first);dice[0].SetResultAppearance(t.hit);if(pair){dice[1].SetRawFace(second);dice[1].SetResultAppearance(t.hit);}
                SelectedDieIndex=first==t.selectedFace?0:1;
                SetText(title,actorName(t.actorId)+" · "+(t.purpose=="Reaction"?"反应格挡":actionName(t.actionId)));
                string roll=t.purpose=="Reaction"?"Reaction · 反应格挡":t.rollType=="Save"?"Save · 敌方豁免":t.rollType=="Check"?(t.v11?.customAction==true?"Check · 施法检定":"Check · 运动检定"):"Attack · 攻击检定";
                string choice=pair?(t.disadvantage&&!t.advantage?"劣势 · 两骰取低":"优势 · 两骰取高"):"单骰";
                SetText(kind,roll+"  /  "+choice);
                SetText(rawText,"原始骰 "+first+(pair?" / "+second:"")+"  →  采用 "+t.selectedFace);
                builder.Clear();builder.Append("基础修正 ").Append(Signed(t.baseBonus));
                if(t.flatBonus!=0)builder.Append("  ·  骰后修正 ").Append(Signed(t.flatBonus));
                if(t.precisionBonus!=0)builder.Append("  ·  战技骰 ").Append(Signed(t.precisionBonus));
                if(t.modifierSources.Count>0){builder.Append("\n");for(int i=0;i<t.modifierSources.Count;i++){if(i>0)builder.Append(" · ");builder.Append(sourceName(t.modifierSources[i]));}}
                SetText(modifiers,builder.ToString());
                SetText(total,"最终  "+t.total);
                SetText(defense,(t.purpose=="Reaction"?"格挡 DC  ":t.rollType=="Save"||t.rollType=="Check"?"DC  ":"AC  ")+t.targetNumber);
                SetText(outcome,t.rollType=="Save"?(t.hit?"敌方豁免失败 · 技能生效":t.p5!=null&&t.actionId=="control"?"敌方豁免成功 · 半伤、不推":"敌方豁免成功 · 技能抵抗"):
                    t.rollType=="Check"?(t.v11?.customAction==true?(t.hit?"施法检定成功":"施法检定失败 · 不产生效果"):(t.hit?"运动检定成功":t.actionId=="shove"&&t.targetNumber-t.total<=2?"未推开 · 仅削韧1":"运动检定失败")):
                    t.purpose=="Reaction"?(t.hit?"格挡成功":"格挡失败 · 仍承受来袭"):
                    t.hit?(t.critical?(t.p6!=null?"暴击来源："+t.p6.criticalSource:"自然 20 · 暴击"):"命中成功"):t.selectedFace==1?"自然 1 · 攻击未命中":"攻击未命中");
                SetText(damageFormula,t.p6!=null?SmiteFormulaPresentation.Formula(world.Session.Config,s,t.actorId,t):"");
                outcome.color=t.rollType=="Attack"&&t.selectedFace==20?new Color(1,.8f,.34f):t.hit?new Color(.88f,.76f,.48f):new Color(.74f,.38f,.37f);
            }
            int stage=Progress<SettleFraction?0:Progress<ModifiersFraction?1:Progress<TotalFraction?2:Progress<DefenseFraction?3:Progress<BreakdownFraction?4:5;
            if(lastFrozen!=frozen||newResult) Layout(frozen,pair);
            dim.enabled=frozen;lastFrozen=frozen;
            if(decisionPaper.gameObject.activeSelf==frozen)decisionPaper.gameObject.SetActive(!frozen);
            if(dice[1].gameObject.activeSelf!=pair)dice[1].gameObject.SetActive(pair);
            for(int i=0;i<VisibleDiceCount;i++)
            {
                dice[i].SetPose(Progress,i,Settled&&pair&&i!=SelectedDieIndex?.30f:1);
                adoption[i].gameObject.SetActive(Settled);
                if(stage!=lastStage||newResult)SetText(adoption[i],!pair?"自然面 "+first:i==SelectedDieIndex?"采用  "+t.rawFaces[i]:"弃用  "+t.rawFaces[i]);
                adoption[i].color=pair&&i!=SelectedDieIndex?new Color(.42f,.40f,.36f):new Color(.93f,.8f,.5f);
            }
            if(!pair)adoption[1].gameObject.SetActive(false);
            if(stage!=lastStage||newResult)
            {rawText.gameObject.SetActive(stage>=1);modifiers.gameObject.SetActive(stage>=2);total.gameObject.SetActive(stage>=3);defense.gameObject.SetActive(stage>=4);outcome.gameObject.SetActive(stage>=5);damageFormula.gameObject.SetActive(stage>=5&&t.p6!=null);lastStage=stage;}
        }
        void Layout(bool frozen,bool pair)
        {
            Place(tray,frozen?340:439,115,570,frozen?530:430);
            tray.localScale=Vector3.one*(frozen?1:.68f);
            Place(decisionPaper.rectTransform,-10,-7,590,447);
            Place(title.rectTransform,0,0,570,31);Place(kind.rectTransform,0,33,570,27);
            float size=frozen?196:116,y=frozen?69:64;
            for(int i=0;i<2;i++)
            {
                float center=pair?(i==0?168:402):285;
                Place(dice[i].rectTransform,center-size/2,y,size,size);
                Place(adoption[i].rectTransform,center-80,y+size-3,160,24);
            }
            float rows=y+size+23;
            Place(rawText.rectTransform,0,rows,570,25);Place(modifiers.rectTransform,0,rows+26,570,44);Place(total.rectTransform,0,rows+73,570,29);Place(defense.rectTransform,0,rows+104,570,24);Place(outcome.rectTransform,0,rows+132,570,29);Place(damageFormula.rectTransform,0,rows+164,570,64);
        }
        static string Signed(int value)=>value>=0?"+"+value:value.ToString();
        static void SetText(Text label,string text){if(label.text!=text)label.text=text;}
        static Text Label(Transform parent,string name,Font font,int size)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(Text),typeof(Outline));obj.transform.SetParent(parent,false);
            var label=obj.GetComponent<Text>();label.font=font;label.fontSize=size;label.color=new Color(.94f,.98f,1);label.alignment=TextAnchor.MiddleCenter;label.raycastTarget=false;label.verticalOverflow=VerticalWrapMode.Overflow;
            var outline=obj.GetComponent<Outline>();outline.effectColor=new Color(.015f,.025f,.045f,.9f);outline.effectDistance=new Vector2(1,-1);return label;
        }
        static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
        static void Place(RectTransform r,float x,float y,float width,float height){r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);}
    }
    /// <summary>Formatting only. Reads real tickets/statuses/config, never rolls or pays.</summary>
    public static class SmiteFormulaPresentation
    {
        static bool Core(CombatConfig c,string actor)=>c.v11!=null&&c.v11.coreActorId==actor;
        static int Buff(CombatConfig c,string actor,string id)=>Core(c,actor)?(int)V11BuffCatalog.Value(c.v11,id):0;
        static bool PracticeRadiant(CombatState s)=>s.p6?.training==true&&s.p6.trainingUpgradeId=="practice_radiant"&&s.world.strings.TryGetValue("context",out var value)&&value=="TRAINING";
        public static int BaseRadiantDice(CombatConfig c,CombatState s,string actor,RollTicket ticket=null)
        {
            int guard=ticket?.v11?.guardRadiantDice??0;
            if(ticket==null&&Buff(c,actor,"core_oath_guard")>0)
            {var owner=s.Actor(actor);if(owner!=null)foreach(var status in owner.statuses)if(status.id=="v11-oath-guard"&&status.remaining>0){guard=1;break;}}
            return c.p6.smiteRadiantDice+Buff(c,actor,"core_smite_dice")+guard+(PracticeRadiant(s)?2:0);
        }
        public static int FullRadiantDice(CombatConfig c,string actor)=>c.p6.overchargeRadiantDice+Buff(c,actor,"core_full_dice");
        public static float MarkMultiplier(CombatState s,string targetId,string requiredId=null)
        {
            var target=s.Actor(targetId);float multiplier=1;if(target==null)return multiplier;
            foreach(var mark in target.statuses)if(mark.id=="p6-radiant-mark"&&mark.p6!=null&&mark.remaining>0&&(requiredId==null||mark.p6.id==requiredId)&&s.Actor(mark.p6.casterId)?.Alive==true)multiplier=Math.Max(multiplier,mark.p6.multiplier);
            return multiplier;
        }
        public static string Multiplier(float value)=>value.ToString("0.##",CultureInfo.InvariantCulture);
        public static string Formula(CombatConfig c,CombatState s,string actor,RollTicket ticket=null)
        {
            if(c?.p6==null||s==null)return "";int critical=ticket?.critical==true?2:1;int strength=s.Actor(actor)?.strengthModifier??0;
            string first=(critical==2?"基础（暴击骰） ":"基础 ")+(c.p6.weaponDice*critical)+"d"+c.p6.weaponSides+" + 力量"+(strength>=0?"+":"")+strength+" + "+(BaseRadiantDice(c,s,actor,ticket)*critical)+"d"+c.p6.radiantSides+"光耀";
            string full="全力另加 "+(FullRadiantDice(c,actor)*critical)+"d"+c.p6.radiantSides+"光耀 · 再用1次";
            if(ticket?.p6?.markId!=null){float multiplier=MarkMultiplier(s,ticket.targetId,ticket.p6.markId);full+="\n"+(multiplier>1?"裂光：光耀×"+Multiplier(multiplier):"裂光已失效：光耀×1");}
            return first+"\n"+full;
        }
        public static string AttackBreakdown(RollTicket ticket)
        {return "原始 "+ticket.selectedFace+" + 总修正 "+(ticket.total-ticket.selectedFace)+" = "+ticket.total+" / AC "+ticket.targetNumber;}
    }

}
