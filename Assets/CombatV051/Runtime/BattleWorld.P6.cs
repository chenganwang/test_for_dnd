using System;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        const string PaladinId="char_demo_paladin",SmiteId="ability_smite_recharge";
        bool p6RuntimeRules=true,p6QuickFocus,p6DepartAfterTraining,p6TutorialSeenInProcess;
        int p6Lesson=1;
        float p6TrainingStart,p6Moved,p6RechargeAt;
        bool p6TrainingDefended;
        public bool P6Enabled=>Session?.Config.p6!=null;
        public bool P6QuickFocus=>P6Enabled&&p6QuickFocus;
        public int P6Lesson=>p6Lesson;
        public float P6FirstSmiteSeconds {get;private set;}=-1;
        public string P6LastDamage {get;private set;}="";
        public string P6TrainingPrompt=>!Training||!P6Enabled?"":p6Lesson==1?"WASD：移动一小段，面向练习目标。":p6Lesson==2?"左键：靠近目标挥动大剑。":p6Lesson==3?"Q：选择至圣斩，开场已有 2 次。":p6Lesson==4?"选择目标→掷骰→释放。偏转可重试，练习可补充次数。":p6Lesson==5?"右键架防 / 空格闪避；有效交战会自动回充，普攻不充电。":p6Lesson==6?"F：请求法师裂光；印记让下一剑优势、光耀 +50%。":p6Lesson==7?"Q：命中后选全力，再用 1 次。可公开开启本段训练祝福。":p6Lesson==8?"选一次练习升级，观察下一剑。效果不带入正式探索。":"练习完成。现在出发，在原来的地图中试一场。";
        string P6TutorialPath=>Path.Combine(Path.GetDirectoryName(SavePath),"presentation","p6-smite-tutorial.txt");
        CombatConfig LoadP5Config()
        {
            var c=LoadLegacyConfig();var asset=Resources.Load<TextAsset>("P5CombatConfig");
            if(asset!=null)c.p5=CombatJson.Read<P5Config>(asset.text);return c;
        }
        public void InitializeP6FrontendForTests(string path)
        {
            legacyTestRules=false;p6RuntimeRules=true;Profile=null;SavePath=path;IgnoreApplicationFocus=true;
            expectDungeonProfile=expectPartyProfile=true;IsolateSyntheticInput();InitializeFrontend();Hud.RefreshView();
        }
        ReactionSettings EffectiveReactionPreferences()
        {
            var value=CombatJson.Clone(p4Preferences.reactions);
            if(P6Enabled){value.decisionSeconds=6;value.pauseDecision=false;}return value;
        }
        public CombatResult OpenSmite()
        {
            if(State.Actor(PaladinId)==null){Message="当前训练队伍没有圣武士；Tab 选择当前角色技能。";return new CombatResult{reason="paladin_not_in_party"};}
            if(!P6Enabled||Menu||IsFrozen||State.pendingRoll!=null||State.activePendingEvent!=null)return new CombatResult{reason="smite_unavailable"};
            SelectedActor=PaladinId;SelectedAction=SmiteId;
            if(State.Actor(PaladinId)?.guardRemaining>0)Command(CommandKind.EndGuard,actor:PaladinId);
            if(State.Actor(SelectedTarget)?.Alive!=true||State.Actor(SelectedTarget)?.isEnemy!=true)SelectedTarget=State.enemies.Where(e=>e.Alive).OrderBy(e=>BattleGeometry.DistanceXZ(Position(e),WorldAimPoint)).FirstOrDefault()?.id;
            var entered=State.mode==CombatMode.TacticalFocus?new CombatResult{success=true}:Command(CommandKind.EnterFocus,actor:PaladinId);
            p6QuickFocus=true;
            if(entered.success){SelectCommandActor(PaladinId);SelectedAction=SmiteId;if(Training&&p6Lesson==3)p6Lesson=4;}
            else Message="专注暂不可用。可预先接受结果，直接施放基础至圣斩；不追加全力或灵感。";
            Hud.RefreshView();return entered;
        }
        public CombatResult DirectSmite()
        {
            SelectedActor=PaladinId;SelectedAction=SmiteId;
            if(State.mode==CombatMode.TacticalFocus){var leave=Command(CommandKind.ExitFocus);if(!leave.success)return leave;}
            return ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.ConfirmRoll,actorId=PaladinId,targetId=SelectedTarget,actionId=SmiteId,p6=new P6CommandData{preacceptBaseResult=true}});
        }
        public CombatResult CommitSmite(bool overcharge)
        {
            if(State.pendingRoll?.p6==null)return new CombatResult{reason="smite_result_required"};
            var selected=ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.SelectSmiteOvercharge,actorId=State.pendingRoll.actorId,targetId=State.pendingRoll.targetId,ticketId=State.pendingRoll.id,flag=overcharge});
            if(!selected.success)return selected;
            var committed=Command(CommandKind.Commit);
            if(committed.success&&State.mode==CombatMode.TacticalFocus)Command(CommandKind.ExitFocus);
            p6QuickFocus=false;return committed;
        }
        public CombatResult RequestRadiance()
        {
            if(!P6Enabled)return new CombatResult{reason="p6_required"};
            if(State.Actor(SelectedTarget)?.Alive!=true||State.Actor(SelectedTarget)?.isEnemy!=true)SelectedTarget=State.enemies.Where(e=>e.Alive).OrderBy(e=>BattleGeometry.DistanceXZ(Position(e),WorldAimPoint)).FirstOrDefault()?.id;
            return ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.RequestBindRadiance,actorId=RadianceCaster?.id,targetId=SelectedTarget,actionId="ability_bind_radiance"});
        }
        void AdvanceP6Binding()
        {
            if(!P6Enabled)return;
            TryAdvanceP6Recharge();
            foreach(var actor in State.actors.Where(a=>a.p6?.bindIntent?.stage=="Casting"&&a.p6.bindIntent.remaining<=0).ToArray())
            {
                var intent=actor.p6.bindIntent;
                ExecuteWorldCommand(new CombatCommand{id="resolve/"+intent.id,kind=CommandKind.ResolveBindRadiance,actorId=actor.id,targetId=intent.targetId,rootId=intent.id,actionId="ability_bind_radiance",authority=CommandAuthority.System});
            }
        }
        void PopulateP6CommandEvidence(CombatCommand c)
        {
            if(!P6Enabled)return;
            var actor=State.Actor(c.actorId);if(actor==null)return;
            if(c.kind!=CommandKind.StartBasicAttack&&c.kind!=CommandKind.ResolveBasicAttack)return;
            if(actor.role!=ActorRole.Paladin)return;
            c.p6=c.p6??new P6CommandData();
            Vector3 direction=c.kind==CommandKind.ResolveBasicAttack&&actor.basicAttackIntent?.p6!=null?new Vector3(actor.basicAttackIntent.p6.directionX,0,actor.basicAttackIntent.p6.directionZ):(actor.id==State.activeActorId&&c.authority==CommandAuthority.Player?WorldAimPoint-Position(actor):Position(State.Actor(c.targetId))-Position(actor));
            direction=BattleGeometry.Ground(direction).normalized;c.p6.directionX=direction.x;c.p6.directionZ=direction.z;
            var shape=AreaShape.Cone(Position(actor),direction,Session.Config.p6.basicRange,Session.Config.p6.basicAngle*.5f);
            c.p6.targets=State.enemies.Where(e=>e.Alive).Select(e=>new P6TargetEvidence{targetId=e.id,inArea=BattleGeometry.Contains(shape,Position(e)),lineOfSight=HasLineOfEffect(Position(actor),Position(e))}).ToList();
        }
        bool ReservedSmiteTarget(string target,string ally)=>P6Enabled&&ally!=PaladinId&&State.queue.Any(q=>q.actionId==SmiteId&&q.targetId==target&&(q.stage=="Queued"||q.stage=="Executing"));
        float P6MovementMultiplier(ActorState actor)=>Session.P6MovementMultiplier(actor.id);
        public void HoldP6Postures(string target,string source,float seconds)
        {
            if(artActors.TryGetValue(target,out var victim)&&victim)victim.HoldPosture(seconds);
            if(source!=null&&artActors.TryGetValue(source,out var attacker)&&attacker)attacker.HoldPosture(seconds);
        }
        void ObserveP6Command(CombatCommand c,CombatResult result)
        {
            if(!P6Enabled||!result.success||result.duplicate)return;
            if(c.kind==CommandKind.EnterFocus&&!Controls.Smite.WasPressedThisFrame())p6QuickFocus=false;
            if(c.kind==CommandKind.RequestBindRadiance){Message="法师正在施放裂光；不切换主控，不花战术次数。";NotifyArtCue("spell",c.actorId,c.targetId,c.id);}
            if(result.p6?.bind!=null){var bind=result.p6.bind;Message=bind.markApplied?"裂光印 · 8秒：下一剑优势；实际光耀倍率见目标标记"+(bind.savePassed?"；封缚被抵抗。":"；短封缚生效。"):"裂光施放未完成。";if(Training&&p6Lesson==6&&bind.markApplied)p6Lesson=7;}
            if(c.kind==CommandKind.StartQueued&&State.queue.Find(q=>q.id==c.queueId)?.actionId==SmiteId)NotifyArtCue("spell",c.actorId,c.targetId,c.id);
            foreach(var d in result.p6?.damages??new System.Collections.Generic.List<P6DamageResult>())
            {
                if(d.actionId!=SmiteId)continue;
                P6LastDamage="至圣斩 "+d.totalDamage+" = 武器 "+d.physicalApplied+" + 光耀 "+d.radiantApplied+(d.critical?" · 暴击来源："+d.criticalSource:"")+(d.markConsumed?" · 裂光已消费":"");Message=P6LastDamage;
                // HitFeedback observes this same committed result and owns the single contact effect.
                if(Training&&d.totalDamage>0&&p6Lesson==4&&!d.overcharged){p6Lesson=5;p6RechargeAt=State.momentumLedger.totalEarned;P6FirstSmiteSeconds=Time.realtimeSinceStartup-p6TrainingStart;}
                if(Training&&d.totalDamage>0&&d.overcharged&&p6Lesson==7)p6Lesson=8;
            }
            if(!Training)return;
            if(p6Lesson==2&&c.kind==CommandKind.StartBasicAttack&&c.actorId==PaladinId)p6Lesson=3;
            if(p6Lesson==5&&(c.kind==CommandKind.StartGuard||c.kind==CommandKind.StartDodge)){p6TrainingDefended=true;TryAdvanceP6Recharge();}
            if(c.kind==CommandKind.ChoosePracticeUpgrade)p6Lesson=9;
        }
        void TryAdvanceP6Recharge(){if(Training&&p6Lesson==5&&p6TrainingDefended&&State.momentumLedger.totalEarned>p6RechargeAt)p6Lesson=6;}
        void ObserveP6Movement(float distance)
        {if(!Training||!P6Enabled||p6Lesson!=1)return;p6Moved+=distance;if(p6Moved>=1.2f)p6Lesson=2;}
        public void RefillP6Practice(bool blessing=false)=>ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.RefillPracticeResources,flag=!MainCharacterTraining&&blessing&&p6Lesson==7,actorId=MainCharacterTraining?State.activeActorId:PaladinId});
        public void ChooseP6PracticeUpgrade(string id)=>ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.ChoosePracticeUpgrade,actionId=id,actorId=PaladinId});
        public void FinishP6Training()
        {
            if(!Training||!P6Enabled)return;
            if(MainCharacterTraining){EndTraining();return;}
            p6TutorialSeenInProcess=true;string preferenceError=null;
            try{Directory.CreateDirectory(Path.GetDirectoryName(P6TutorialPath));File.WriteAllText(P6TutorialPath,p6Lesson>=9?"completed":"skipped");}catch(Exception e){preferenceError="教程偏好未保存："+e.Message;}
            bool depart=p6DepartAfterTraining;p6DepartAfterTraining=false;EndTraining();if(depart){FrontPage="prepare";DepartFrontend();}
            if(preferenceError!=null)Message=preferenceError;
        }
    }
}
