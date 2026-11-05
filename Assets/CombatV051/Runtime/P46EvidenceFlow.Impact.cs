using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
namespace CombatV051.Runtime
{
    public sealed partial class P46EvidenceFlow
    {
        void EvidenceAction(ProfileCommand c){var r=world.ProfileAction(c);Require(r.success,c.kind+" fixture preparation: "+r.reason);}
        void EvidenceEnter(string id)
        {
            var room=world.DungeonConfig.Room(id);int i=0;
            foreach(var a in world.State.actors){a.x=room.x+i*.6f;a.z=room.z-i*.5f;i++;}
            EvidenceAction(new ProfileCommand{kind=ProfileCommandKind.EnterRoom,roomId=id,qualified=true});
            if(!world.Profile.ActiveRoom.resolved&&!string.IsNullOrEmpty(room.encounterKind))EvidenceAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,roomId=id,qualified=true});
        }
        void EvidenceClearPriorRoom()
        {
            // Setup-only skip for earlier rooms. Never counted as a played clear, combat win or Boss kill.
            foreach(var e in world.State.enemies)e.hp=0;
            EvidenceAction(new ProfileCommand{kind=ProfileCommandKind.ResolveEncounter,encounterId=world.Profile.ActiveEncounter.id});
            if(world.Profile.PendingReward!=null)EvidenceAction(new ProfileCommand{kind=ProfileCommandKind.SkipReward,rewardId=world.Profile.PendingReward.id});
        }
        void EvidenceBackgroundFace(int face)
        {for(ulong seed=1;seed<10000;seed++){var initial=RandomStreams.Create(seed).backgroundCombat;var probe=CombatJson.Clone(initial);if(RandomStreams.D20(probe)==face){world.State.random.backgroundCombat=initial;return;}}throw new Exception("No PCG fixture");}
        IEnumerator CaptureImpact(string name)
        {world.Hud.RefreshView();world.Hud.HealthOverlay.Refresh();yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,name+".png"));}
        IEnumerator ImpactEvidence()
        {
            world.InitializeDungeonForTests(Path.Combine(directory,"Profiles","impact.json"),"p46-impact",true);world.Controls.Map.Disable();world.IgnoreApplicationFocus=true;world.ChooseDungeonOption("close");world.enabled=false;
            EvidenceEnter("R1");var target=world.State.enemies.First(e=>e.Alive);var f=world.State.Actor("fighter");
            for(int i=0;i<world.State.actors.Count;i++){var a=world.State.actors[i];a.x=target.x-2-i;a.z=target.z-i;a.majorRemaining=100;a.reactionRemaining=100;}
            foreach(var e in world.State.enemies){e.aiState=2;e.aiRemaining=100;}
            world.SelectedTarget=target.id;world.Rig.Step(10);Physics.SyncTransforms();world.ImpactFeedback.ResetObservation();
            f.hp=8;yield return CaptureImpact("low-hp-under-30");Require(world.Hud.HealthOverlay.Severity==1,"Low HP mild threshold");
            f.hp=4;yield return CaptureImpact("low-hp-under-15");Require(world.Hud.HealthOverlay.Severity==2,"Low HP critical threshold");
            f.hp=f.maxHp;world.State.Actor("rogue").hp=2;yield return CaptureImpact("teammate-low-hp-only");Require(world.Hud.HealthOverlay.Severity==0,"Teammate HP must not tint full screen");
            world.State.Actor("rogue").hp=world.State.Actor("rogue").maxHp;world.ImpactFeedback.ResetObservation();
            EvidenceBackgroundFace(12);target.attackBonus=f.armorClass;
            Good(world.ExecuteWorldCommand(new CombatCommand{id="visual-player-heavy",kind=CommandKind.IncomingBatch,actorId=target.id,rootId="visual-player-heavy",authority=CommandAuthority.System,qualified=true,incomingContacts=new List<IncomingContact>{new IncomingContact{targetId="fighter",damage=9,parryable=false}}}).success,"real heavy contact");
            Require(world.State.Actor("fighter").hp==f.maxHp-9&&world.ImpactFeedback.LastLevel==ImpactLevel.Heavy,"Published heavy player injury");
            float stop=world.ImpactFeedback.LastStopSeconds;float simulation=world.State.simulationSeconds;int stoppedFrames=0;world.enabled=true;
            yield return CaptureImpact("player-heavy-impact");
            while(world.ImpactFeedback.Holding){Require(world.State.simulationSeconds==simulation,"Cosmetic hold must freeze domain simulation too");stoppedFrames++;yield return null;}
            yield return new WaitForSecondsRealtime(.25f);Require(Time.timeScale==1,"Hit Stop restores realtime");
            checks.Add(new{name="impact-player",hp=world.State.Actor("fighter").hp,stop,stoppedFrames,ordinaryWorldRestored=true,lowHpUiUsesDeclaredFixture=true});
            world.enabled=false;EvidenceClearPriorRoom();EvidenceEnter("R2");EvidenceEnter("R3");EvidenceClearPriorRoom();EvidenceEnter("R2");
            world.State.Actor("fighter").x=48;world.State.Actor("fighter").z=8;EvidenceAction(new ProfileCommand{kind=ProfileCommandKind.OpenGate,gateId="P24_SMASH",qualified=true});
            EvidenceEnter("R4");EvidenceClearPriorRoom();EvidenceEnter("R5");EvidenceClearPriorRoom();EvidenceEnter("R6");EvidenceEnter("R7");
            var boss=world.State.enemies.Single(e=>world.Profile.EnemyTemplateFor(e.id)?.id=="boss");
            // Isolate the last Boss strike; auxiliary mechanism removal is setup, never a claimed player kill.
            foreach(var e in world.State.enemies){e.aiState=2;e.aiRemaining=100;if(e!=boss)e.hp=0;}
            boss.hp=1;
            f=world.State.Actor("fighter");f.hp=f.maxHp;f.x=boss.x-1.5f;f.z=boss.z;f.majorRemaining=0;f.reactionRemaining=100;f.attackBonus=30;
            foreach(var a in world.State.actors.Where(a=>a.id!="fighter")){a.x=f.x-2;a.z=f.z-2;a.majorRemaining=100;a.reactionRemaining=100;}
            world.SelectedTarget=boss.id;world.Rig.Step(10);Physics.SyncTransforms();world.ImpactFeedback.ResetObservation();EvidenceBackgroundFace(12);
            int kills=world.ImpactFeedback.BossKillCount;
            Good(world.BeginBasicAttack("fighter",boss.id).success,"real last strike start");world.enabled=true;
            float started=Time.realtimeSinceStartup;
            while(world.ImpactFeedback.BossKillCount==kills){Require(Time.realtimeSinceStartup-started<3,"Boss last strike not resolved");yield return null;}
            Require(world.ImpactFeedback.BossKillCount==kills+1&&world.ImpactFeedback.MonochromeActive,"Boss death must start its transient monochrome");
            yield return CaptureImpact("boss-kill-monochrome");
            yield return new WaitForSecondsRealtime(.3f);yield return CaptureImpact("boss-kill-color-restored");
            Require(!world.ImpactFeedback.MonochromeActive&&world.ImpactFeedback.BossKillCount==kills+1,"Boss color restores and kill feedback deduplicates");
            checks.Add(new{name="impact-boss",realContact=true,initialBossHpFixture=1,earlierRoomsSkippedForSetup=true,mechanismRemovedForSetup=true,bossFeedback=world.ImpactFeedback.BossKillCount-kills,monochromeSeconds=world.ImpactFeedback.Config.bossMonochromeSeconds,restored=true});
        }
    }
}
