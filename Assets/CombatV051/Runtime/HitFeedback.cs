using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace CombatV051.Runtime
{
    public enum ImpactLevel { None, Light, Medium, Heavy }
    public enum FeedbackIntensity { Off, Low, Normal }
    [Serializable] public sealed class ImpactTuning
    {
        public float stopSeconds,shakeStrength,shakeSeconds,fxScale=1,flinch=1,audioGain=1;
    }
    [Serializable] public sealed class ImpactFeedbackConfig
    {
        public ImpactTuning[] levels={new ImpactTuning(),new ImpactTuning{fxScale=1,flinch=.8f,audioGain=.8f},new ImpactTuning{stopSeconds=.045f,shakeStrength=.035f,shakeSeconds=.14f,fxScale=1.4f,flinch=1.3f,audioGain=1.05f},new ImpactTuning{stopSeconds=.08f,shakeStrength=.09f,shakeSeconds=.19f,fxScale=2,flinch=2,audioGain=1.35f}};
        public float cooldownSeconds=.16f,maximumStopPerSecond=.20f,bossStopSeconds=.10f,bossMonochromeSeconds=.16f,bossShakeMultiplier=1.3f;
        public static ImpactFeedbackConfig Load()
        {
            var asset=Resources.Load<TextAsset>("ImpactFeedbackConfig");var config=asset?JsonUtility.FromJson<ImpactFeedbackConfig>(asset.text):new ImpactFeedbackConfig();
            if(config==null||config.levels==null||config.levels.Length!=4||config.levels.Any(x=>x==null))return new ImpactFeedbackConfig();
            foreach(var x in config.levels){x.stopSeconds=Mathf.Clamp(x.stopSeconds,0,.1f);x.shakeStrength=Mathf.Clamp(x.shakeStrength,0,.14f);x.fxScale=Mathf.Clamp(x.fxScale,.5f,3);x.flinch=Mathf.Clamp(x.flinch,0,3);x.audioGain=Mathf.Clamp(x.audioGain,0,1.5f);}
            config.cooldownSeconds=Mathf.Max(.1f,config.cooldownSeconds);config.maximumStopPerSecond=Mathf.Clamp(config.maximumStopPerSecond,0,.25f);return config;
        }
    }
    // Presentation observer of published facts. It never rolls RNG, edits combat DTOs, or owns Time.timeScale.
    public sealed class HitFeedback : MonoBehaviour
    {
        BattleWorld world;readonly HashSet<string> seen=new HashSet<string>();readonly Dictionary<string,int> hp=new Dictionary<string,int>();readonly Dictionary<string,float> stagger=new Dictionary<string,float>();
        readonly HashSet<string> handled=new HashSet<string>();readonly Dictionary<string,int> queuedDamage=new Dictionary<string,int>();string context;double stopUntil,shakeUntil,shakeStart,lastStop=-100,windowStart,monoUntil;float windowStop,shakeAmplitude;
        Volume volume;VolumeProfile profile;ColorAdjustments color;
        public ImpactFeedbackConfig Config {get;private set;}
        public ImpactLevel LastLevel {get;private set;}
        public string LastEvent {get;private set;}
        public int EventCount {get;private set;}
        public readonly List<string> RecentEvents=new List<string>();
        public int BossKillCount {get;private set;}
        public float LastStopSeconds {get;private set;}
        public float LastShakeStrength {get;private set;}
        public bool MonochromeActive=>color!=null&&volume.weight>0;
        public float StopRemaining=>(float)Math.Max(0,stopUntil-Now);
        public bool Holding=>world!=null&&!world.P6Enabled&&!world.IsFrozen&&!world.IsFocus&&StopRemaining>0;
        static double Now=>System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
        static float Strength(FeedbackIntensity setting)=>setting==FeedbackIntensity.Off?0:setting==FeedbackIntensity.Low?.45f:1;
        public void Initialize(BattleWorld owner)
        {
            world=owner;Config=ImpactFeedbackConfig.Load();
            var node=new GameObject("P46 impact color volume");node.transform.SetParent(transform,false);
            volume=node.AddComponent<Volume>();volume.isGlobal=true;volume.priority=90;volume.weight=0;
            profile=ScriptableObject.CreateInstance<VolumeProfile>();
            color=profile.Add<ColorAdjustments>(true);color.saturation.Override(-100);volume.sharedProfile=profile;
            world.BattleCamera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            ResetObservation();
        }
        public void ResetObservation(bool preserveBossTail=false)
        {
            if(world?.Session==null)return;context=world.State.modeInstanceId+"/"+world.State.encounterId;seen.Clear();hp.Clear();stagger.Clear();queuedDamage.Clear();
            foreach(var a in world.State.actors.Concat(world.State.enemies)){hp[a.id]=a.hp;stagger[a.id]=a.staggerRemaining;}
            foreach(var e in world.State.incomingEvents)if(e.damageApplied)seen.Add("incoming/"+e.id);
            foreach(var q in world.State.queue)if(q.effectApplied)seen.Add("queue/"+q.id);
            if(!preserveBossTail||monoUntil<=Now)CancelTransient();
        }
        public void CancelTransient(){stopUntil=shakeUntil=monoUntil=0;if(volume)volume.weight=0;if(world?.Rig!=null)world.Rig.FeedbackOffset=Vector3.zero;}
        bool Boss(ActorState actor)=>actor!=null&&actor.isEnemy&&(world.Profile?.EnemyTemplateFor(actor.id)?.id=="boss"||world.State.world.strings.TryGetValue("enemyTemplate/"+actor.id,out var t)&&t=="boss");
        bool Elite(ActorState actor)=>Boss(actor)||actor!=null&&world.Profile?.EnemyTemplateFor(actor.id)?.id=="elite";
        public void ObserveCommand(CombatCommand command,CombatResult result)
        {
            if(!result.success||result.duplicate)return;
            if(command.kind==CommandKind.ResolveQueued&&command.queueId!=null)queuedDamage[command.queueId]=result.damage;
            var source=world.State.Actor(command.actorId);var target=world.State.Actor(command.targetId);
            if(source!=null&&target!=null&&result.reason=="basic_swing_missed")world.Feedback.Impact(BattleWorld.Position(source),BattleWorld.Position(target),result,false);
            else if(!world.State.IsPartyP1&&source!=null&&target!=null&&result.damage==0&&(command.kind==CommandKind.BasicAttack||command.kind==CommandKind.IncomingAttack||command.kind==CommandKind.IncomingSave))Emit("legacy/"+command.id,target,source,ImpactLevel.Light,"deflect",0);
            Observe();
        }
        public void Observe()
        {
            if(world?.Session==null)return;var state=world.State;
            if(context!=state.modeInstanceId+"/"+state.encounterId){ResetObservation(context!=null&&context.StartsWith(state.modeInstanceId+"/",StringComparison.Ordinal)&&!state.inCombat&&LastEvent=="boss-kill");return;}
            if(world.IsFrozen)return;
            handled.Clear();
            foreach(var e in state.incomingEvents)
            {
                if(!e.damageApplied||!seen.Add("incoming/"+e.id)||e.stage!="Applied")continue;
                var target=state.Actor(e.defenderId);var source=state.Actor(e.attackerId);if(target==null||source==null)continue;handled.Add(target.id);
                if(Boss(target)&&!target.Alive)continue;
                string kind=e.reason=="avoided_by_dodge"?"dodge":e.finalDamage<=0?"deflect":"hit";
                var level=e.finalDamage>0&&e.critical?ImpactLevel.Heavy:e.finalDamage>0&&!target.isEnemy&&(Elite(source)||e.finalDamage>=target.maxHp*.25f)?ImpactLevel.Heavy:ImpactLevel.Light;
                int originalDamage=e.baseDamage*(e.critical?2:1);
                bool defended=e.finalDamage<originalDamage&&(e.reason=="reaction_shield"||e.reason=="reaction_parry"||e.reason=="parried"||e.reason=="protected_by_preparation"||e.guarded);
                var threat=state.pendingThreatEvents.Find(g=>g.id==e.reactionEventId);
                bool importantDefense=defended&&(originalDamage>=target.maxHp*world.Session.Config.importantReactionDamageFraction||threat!=null&&(threat.important||threat.lethal));
                // Successful protection can leave critical damage. Its confirmed reduction takes
                // presentation priority; a failed Parry has no reduction and remains a critical hit.
                if(importantDefense){level=ImpactLevel.Medium;kind="reaction";}
                else if(e.finalDamage>0&&e.critical)kind="critical";else if(level==ImpactLevel.Heavy)kind="heavy";
                Emit("incoming/"+e.id,target,source,level,kind,e.finalDamage);
            }
            foreach(var q in state.queue)
            {
                if(!q.effectApplied||q.stage!="Resolved"||!seen.Add("queue/"+q.id))continue;
                var t=state.rolls.Find(x=>x.id==q.ticketId);var target=state.Actor(q.targetId);var source=state.Actor(q.actorId);if(t==null||target==null||source==null)continue;handled.Add(target.id);
                // Support effects have no damaging contact: their published effect list
                // owns a spell cue, never a hit flinch or armor-deflection spark on allies.
                if(t.v11?.customAction==true&&t.actionId!="v11_quick_shot")continue;
                if(Boss(target)&&!target.Alive)continue;
                var level=!t.hit?ImpactLevel.Light:t.p6!=null||t.critical||t.actionId=="shove"||t.actionId=="v12_mira_break"?ImpactLevel.Heavy:ImpactLevel.Medium;
                Emit("queue/"+q.id,target,source,level,!t.hit?"deflect":t.p6!=null?(t.p6.overchargePaid?"smite-full":"smite"):t.critical?"critical":t.actionId=="shove"?"heavy":"skill",queuedDamage.TryGetValue(q.id,out int actualDamage)?actualDamage:t.hit?Mathf.Max(0,(hp.TryGetValue(target.id,out int oldHp)?oldHp:target.hp)-target.hp):0);
            }
            foreach(var a in state.actors.Concat(state.enemies))
            {
                if(hp.TryGetValue(a.id,out int prior)&&prior>0&&a.hp<=0&&Boss(a))Emit("death/"+context+"/"+a.id,a,null,ImpactLevel.Heavy,"boss-kill",prior);
                else if(prior>a.hp&&!handled.Contains(a.id))Emit("hp/"+context+"/"+state.revision+"/"+a.id+"/"+a.hp,a,null,!a.isEnemy&&prior-a.hp>=a.maxHp*.25f?ImpactLevel.Heavy:ImpactLevel.Light,!a.isEnemy&&prior-a.hp>=a.maxHp*.25f?"heavy":"hit",prior-a.hp);
                if(stagger.TryGetValue(a.id,out float old)&&old<=0&&a.staggerRemaining>0)Emit("break/"+context+"/"+state.revision+"/"+a.id,a,null,ImpactLevel.Heavy,"break",0);
                hp[a.id]=a.hp;stagger[a.id]=a.staggerRemaining;
            }
        }
        void Emit(string id,ActorState target,ActorState source,ImpactLevel level,string kind,int damage)
        {
            // Incoming/queue facts are already remembered; distinct derived death/break facts use their own key.
            if((id.StartsWith("death/")||id.StartsWith("break/")||id.StartsWith("hp/"))&&!seen.Add(id))return;
            var tuning=Config.levels[(int)level];LastLevel=level;LastEvent=kind;EventCount++;RecentEvents.Add(id+" / "+kind+" / "+damage+" / frame "+Time.frameCount);if(RecentEvents.Count>32)RecentEvents.RemoveAt(0);
            bool boss=kind=="boss-kill";if(boss)BossKillCount++;
            var position=BattleWorld.Position(target);var from=source!=null?BattleWorld.Position(source):position+Vector3.back;
            world.Feedback.Impact(from,position,new CombatResult{success=true,damage=damage,critical=kind=="critical"||boss},level>=ImpactLevel.Medium,tuning.fxScale,kind);
            world.EmitImpactArt(kind,target.id,tuning.fxScale,tuning.flinch,id);
            AudioCue cue=kind=="smite"||kind=="smite-full"?AudioCue.SpellImpact:boss||kind=="break"?AudioCue.Break:kind=="critical"?AudioCue.Critical:kind=="reaction"?AudioCue.Parry:kind=="deflect"?AudioCue.Block:kind=="dodge"?AudioCue.PerfectDodge:target.isEnemy?AudioCue.EnemyHit:AudioCue.Hit;
            world.Audio?.PlayCue(cue,position,"impact/"+id,tuning.audioGain);
            double now=Now;LastStopSeconds=0;LastShakeStrength=0;
            // Focus and rule pauses take precedence. Never queue a surprise pause for after a dice decision.
            if(!world.IsFrozen&&!world.IsFocus)
            {
                float stop=Mathf.Clamp(boss?Config.bossStopSeconds:tuning.stopSeconds,0,.10f)*Strength(world.HitStopSetting);
                if(now-windowStart>=1){windowStart=now;windowStop=0;}
                if(world.P6Enabled && stop>0){world.HoldP6Postures(target.id,source?.id,stop);LastStopSeconds=stop;stop=0;}
                if(stop>0&&(boss||now-lastStop>=Config.cooldownSeconds))
                {stop=Mathf.Min(stop,Mathf.Max(0,Config.maximumStopPerSecond+(boss?Config.bossStopSeconds:0)-windowStop));stopUntil=Math.Max(stopUntil,now+stop);windowStop+=stop;lastStop=now;LastStopSeconds=stop;}
                float shake=tuning.shakeStrength*Strength(world.ShakeSetting)*(boss?Config.bossShakeMultiplier:1);
                if(shake>0){shakeAmplitude=Mathf.Min(.14f,shake);shakeStart=now;shakeUntil=now+Mathf.Min(.25f,tuning.shakeSeconds);LastShakeStrength=shakeAmplitude;}
            }
            if(boss){monoUntil=now+Mathf.Clamp(Config.bossMonochromeSeconds,.1f,.2f);volume.weight=1;}
        }
        public void RefreshVisualClock()
        {
            if(world==null)return;double now=Now;
            volume.weight=monoUntil>now?1:0;
            if(world.IsFrozen||world.IsFocus){stopUntil=shakeUntil=0;world.Rig.FeedbackOffset=Vector3.zero;return;}
            float phase=(float)(now-shakeStart);float fade=shakeUntil>now?Mathf.Clamp01((float)((shakeUntil-now)/Math.Max(.01,shakeUntil-shakeStart))):0;
            world.Rig.FeedbackOffset=(world.BattleCamera.transform.right*Mathf.Sin(phase*145)+world.BattleCamera.transform.up*Mathf.Sin(phase*113+.9f))*shakeAmplitude*fade;
            volume.weight=monoUntil>now?1:0;
        }
        void Update()=>RefreshVisualClock();
        void OnDestroy(){if(color)Destroy(color);if(profile)Destroy(profile);}
    }
}
