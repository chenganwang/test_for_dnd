using System.Collections.Generic;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    /// <summary>Presentation only: effective hits and visible armor deflections share the contact point.</summary>
    public sealed class BattleFeedback : MonoBehaviour
    {
        sealed class Effect
        {
            public GameObject root;
            public LineRenderer line;
            public GameObject spark, shield;
            public float remaining;
        }
        readonly List<Effect> effects = new List<Effect>();
        readonly Stack<Effect> pool = new Stack<Effect>();
        public int CreatedEffectCount { get; private set; }
        void Awake()
        {
            // Warm the bounded contact pool while the title scene loads, before the
            // first actual hit can pay for meshes, renderers or materials.
            for(int i=0;i<24;i++){var effect=CreateEffect();effect.root.SetActive(false);pool.Push(effect);}
        }
        Effect CreateEffect()
        {
            var obj=new GameObject("P47 pooled contact");obj.transform.SetParent(transform);
            var lr=obj.AddComponent<LineRenderer>();lr.useWorldSpace=true;lr.positionCount=25;
            lr.sharedMaterial=BattleVisual.NewUnlitMaterial(Color.white);obj.AddComponent<SpatialResourceOwner>().OwnedMaterial=lr.sharedMaterial;
            CreatedEffectCount++;
            return new Effect{root=obj,line=lr,
                shield=BattleVisual.Disc("Visible defense contact",Vector3.zero,.42f,Color.white,obj.transform),
                spark=BattleVisual.Sphere("Hit spark",Vector3.zero,1,Color.white,obj.transform)};
        }
        public void Impact(Vector3 source, Vector3 target, CombatResult result, bool tactical, float feedbackScale=1, string impactKind=null)
        {
            if (!result.success) return;
            bool missed = result.reason == "basic_swing_missed";
            bool effective = result.damage > 0 || impactKind=="break" || impactKind=="heavy" || impactKind=="skill";
            var color = effective ? (result.critical ? new Color(1,.8f,.3f) : new Color(.25f,1,.84f)) : new Color(.9f,.75f,.42f);
            if(effects.Count>=24){var oldest=effects[0];oldest.root.SetActive(false);pool.Push(oldest);effects.RemoveAt(0);}
            Effect effect;
            if(pool.Count>0)effect=pool.Pop();
            else effect=CreateEffect();
            var root=effect.root;var line=effect.line;line.positionCount=2;root.SetActive(true);root.name=missed?"Swing missed":effective?"Contact hit":"Armor deflection";
            line.SetPosition(0, source + Vector3.up*.75f); line.SetPosition(1,(missed ? source + (target-source).normalized*.7f : target) + Vector3.up*.75f);
            line.startWidth = (tactical ? .12f : .055f)*feedbackScale; line.endWidth = .025f;
            line.sharedMaterial.color=color;
            effect.shield.SetActive(!effective&&!missed);effect.spark.SetActive(effective&&!missed);
            effect.shield.transform.position=target+Vector3.up*.65f;effect.shield.transform.rotation=Quaternion.Euler(55,45,0);
            effect.spark.transform.position=target+Vector3.up*.8f;effect.spark.transform.localScale=Vector3.one*(result.critical?.3f:.19f)*feedbackScale;
            effect.shield.GetComponent<Renderer>().sharedMaterial.color=color;effect.spark.GetComponent<Renderer>().sharedMaterial.color=color;
            effect.remaining=.22f;
            if(impactKind=="boss-kill")
            {
                root.name="Boss death burst";effect.remaining=.55f;line.positionCount=25;line.loop=false;
                for(int i=0;i<25;i++){float angle=(i/2)*Mathf.PI/6;line.SetPosition(i,i%2==0?target+Vector3.up*.85f:target+new Vector3(Mathf.Cos(angle)*1.7f,.85f+Mathf.Sin(angle)*.4f,Mathf.Sin(angle)*1.7f));}
            }
            effects.Add(effect);
        }
        public void MicroHeal(Vector3 target)
        {
            // The same warmed, bounded contact pool; no Instantiate, material copy
            // or sound peak for the tiny sustain proc. Rules publish only real HP gain.
            if(effects.Count>=24){var oldest=effects[0];oldest.root.SetActive(false);pool.Push(oldest);effects.RemoveAt(0);}
            if(pool.Count==0)return;
            var effect=pool.Pop();var color=new Color(.48f,.87f,.57f);
            effect.root.name="Micro heal leaf";effect.root.SetActive(true);
            effect.line.positionCount=3;effect.line.loop=false;
            effect.line.SetPosition(0,target+new Vector3(-.045f,1.31f,0));
            effect.line.SetPosition(1,target+new Vector3(.045f,1.43f,0));
            effect.line.SetPosition(2,target+new Vector3(0,1.31f,0));
            effect.line.startWidth=.025f;effect.line.endWidth=.008f;
            effect.line.sharedMaterial.color=color;
            effect.shield.SetActive(false);effect.spark.SetActive(true);
            effect.spark.transform.position=target+Vector3.up*1.30f;
            effect.spark.transform.localScale=Vector3.one*.075f;
            effect.spark.GetComponent<Renderer>().sharedMaterial.color=color;
            effect.remaining=.25f;effects.Add(effect);
        }
        void Update() { using (P46Performance.Measure(10)) UpdateMeasured(); }
        void UpdateMeasured()
        {
            // timeScale is owned by BattleWorld; zero delta freezes these effects with the battlefield.
            float dt=Time.deltaTime;
            if(Time.timeScale<=0 || dt<=0)return;
            for(int i=effects.Count-1;i>=0;i--)
            {
                effects[i].remaining-=dt;
                if(effects[i].remaining<=0){effects[i].root.SetActive(false);pool.Push(effects[i]);effects.RemoveAt(i);}
            }
        }
    }
}
