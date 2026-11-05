using CombatV051.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    /// <summary>Read-only, pooled presentation of actual persisted Focus gains.</summary>
    public sealed class FocusBudgetFeedback:MonoBehaviour
    {
        BattleWorld world;Image fill;Text recovery;Outline edge;Color gold;
        string scope,modeId,encounterId;float observedRecovered,shineUntil,lastWidth=-1;
        public int RestorationCount {get;private set;}
        public float LastDisplayedRecovery {get;private set;}
        public bool RecoveryVisible=>Time.unscaledTime<shineUntil;
        public void Initialize(BattleWorld owner,Image bar,Text text,Color tint)
        {
            world=owner;fill=bar;recovery=text;gold=tint;
            edge=fill.gameObject.AddComponent<Outline>();edge.effectDistance=new Vector2(.7f,.7f);edge.effectColor=Color.clear;
            recovery.raycastTarget=false;recovery.gameObject.SetActive(false);
            Baseline();
        }
        void Baseline(bool keepCurrentFlash=false)
        {if(world?.Session==null)return;modeId=world.State.modeInstanceId;encounterId=world.State.encounterId;scope=modeId+"/"+encounterId;observedRecovered=world.State.focusBudget.secondsRecovered;if(!keepCurrentFlash)shineUntil=0;}
        void LateUpdate()=>Refresh();
        public void Refresh()
        {
            if(world?.Session==null||!fill)return;
            var state=world.State;var budget=state.focusBudget;
            if(modeId!=state.modeInstanceId||budget.secondsRecovered<observedRecovered)Baseline();
            else if(encounterId!=state.encounterId)Baseline(true);
            float gained=budget.secondsRecovered-observedRecovered;
            if(gained>.0001f)
            {
                observedRecovered=budget.secondsRecovered;LastDisplayedRecovery=gained;RestorationCount++;shineUntil=Time.unscaledTime+.85f;
                recovery.text="+"+gained.ToString("0.00").TrimEnd('0').TrimEnd('.')+"s";
                world.Audio?.PlayCue(AudioCue.FocusRecovered,sourceId:"focus/"+scope+"/"+budget.lastRecoverySequence);
            }
            float width=260*Mathf.Clamp01(budget.secondsRemaining/Mathf.Max(.01f,budget.maximumSeconds));
            if(!Mathf.Approximately(lastWidth,width)){fill.rectTransform.sizeDelta=new Vector2(width,5);lastWidth=width;}
            bool shining=Time.unscaledTime<shineUntil;
            float glow=shining?Mathf.Clamp01((shineUntil-Time.unscaledTime)/.85f):0;
            Color tint=budget.secondsRemaining<=1?new Color(.79f,.29f,.24f):budget.secondsRemaining<3?new Color(.84f,.54f,.31f):gold;
            fill.color=Color.Lerp(tint,new Color(.98f,.88f,.64f),glow*.55f);
            float pulse=budget.secondsRemaining>0&&budget.secondsRemaining<3?.15f+.17f*(.5f+.5f*Mathf.Sin(Time.unscaledTime*5.5f)):0;
            edge.effectColor=new Color(tint.r,tint.g,tint.b,Mathf.Max(pulse,glow*.40f));
            if(recovery.gameObject.activeSelf!=shining)recovery.gameObject.SetActive(shining);
            if(shining){recovery.color=new Color(gold.r,gold.g,gold.b,Mathf.Clamp01(glow*2));var p=recovery.rectTransform.anchoredPosition;p.y=-76+(1-glow)*8;recovery.rectTransform.anchoredPosition=p;}
        }
    }
}
