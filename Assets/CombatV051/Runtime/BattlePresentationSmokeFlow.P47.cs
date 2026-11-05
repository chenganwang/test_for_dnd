using System.Collections;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CombatV051.Runtime
{
    public sealed partial class BattlePresentationSmokeFlow
    {
        bool p47Mode;
        bool p47CompletedDungeon;
        public void BeginP47(BattleWorld owner,string evidenceDirectory){p47Mode=true;p4Mode=true;Begin(owner,evidenceDirectory);}
        IEnumerator FrontP47(string id)
        {
            Require(id=="settings"||world.FrontChoices.Any(c=>c.id==id&&c.enabled),"P47 enabled frontend option absent: "+id+" / "+world.FrontPage+" / "+world.Message);
            yield return ClickUi("front-"+id);yield return new WaitForSecondsRealtime(.22f);
        }
        IEnumerator CaptureP47(string filename)
        {
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-p47-perf-no-capture")>=0)yield break;
            if(world.Menu)yield return new WaitForSecondsRealtime(.20f);
            yield return new WaitForEndOfFrame();
            var texture=ScreenCapture.CaptureScreenshotAsTexture();
            try{File.WriteAllBytes(Path.Combine(directory,filename),texture.EncodeToPNG());screenshots.Add(filename);}
            finally{Destroy(texture);}
        }
        IEnumerator FlowP47()
        {
            yield return null;yield return null;
            world.InitializeFrontendForTests(Path.Combine(directory,"Profiles","profile.json"));
            stage="p47-main";Require(world.Menu&&world.FrontPage=="main","Ordinary main menu unavailable");
            Require(world.FrontChoices.Select(c=>c.id).SequenceEqual(new[]{"campaign","explore","characters"}),"Main menu must have exactly three core choices");
            yield return CaptureP47("00-main-three-entries.png");
            yield return FrontP47("campaign");Require(world.FrontPage=="campaign"&&world.FrontBody.Contains("正式剧情开发中"),"Campaign is not the explicit placeholder");
            Require(world.Menu,"Campaign entered old Demo");yield return CaptureP47("01-campaign-placeholder.png");yield return FrontP47("main");
            yield return FrontP47("settings");yield return CaptureP47("02-settings.png");yield return Press(Key.Escape);
            yield return FrontP47("characters");Require(world.Profile!=null,"Characters did not load the real profile");yield return FrontP47("role:rogue");yield return CaptureP47("03-three-characters.png");
            yield return FrontP47("explore");yield return CaptureP47("04-exploration-map.png");yield return FrontP47("map-ashen");
            Require(world.FrontPage=="prepare","Map did not open preparation");yield return CaptureP47("05-departure-preparation.png");yield return FrontP47("depart");
            Require(!world.Menu&&world.Profile.State.activeMode==ProfileModeKind.Challenge&&world.State.actors.Count==3,"Fresh exploration did not start actual existing Challenge");
            Require(world.Profile.ActiveMode.flags.Contains("DEMO_EXPLORATION"),"Wrong new-mode route");
            Record("p47_frontend_actual_input","Three-entry title, clickable Campaign placeholder and back, settings, three Characters, map, preparation and real Exploration all reached with pointer/keyboard events.");
            string departure=CombatJson.Write(world.Profile.ActiveMode.departurePreparation);
            yield return Press(Key.Escape);yield return ClickUi("save");
            string actors=CombatJson.Write(world.State.actors),run=world.Profile.ActiveMode.id;
            yield return FrontP47("characters");yield return FrontP47("role:mage");yield return FrontP47("explore");yield return FrontP47("continue-exploration");
            Require(!world.Menu&&world.Profile.ActiveMode.id==run,"Continue started another run");
            Require(CombatJson.Write(world.State.actors)==actors&&CombatJson.Write(world.Profile.ActiveMode.departurePreparation)==departure,"Continue changed original snapshot/resources");
            if(world.State.mode==CombatMode.CombatPaused)yield return Press(Key.Escape);
            yield return CaptureP47("06-continued-original-snapshot.png");Record("p47_continue_exact_snapshot","Actual save→Characters→Explore Continue preserved run ID, all actor resources and original preparation.");
            yield return P4Route();
            var finished=world.Profile.State.challenge;
            p47CompletedDungeon=new[]{"R1","R3","R4","R5","R7"}.All(id=>finished.Room(id).resolved);
            p4Runs.Add(new{kind="DemoExploration",activeRealSeconds=finished.elapsedRealSeconds,finished.explicitPauseSeconds,finished.focusLostSeconds,
                focus=world.State.focusBudget,observedThinkingRealSeconds=p4Encounters.Values.Sum(e=>e.focusThinkingRealSeconds),target6to8Minutes=finished.elapsedRealSeconds>=360&&finished.elapsedRealSeconds<=480});
            stage="p47-exploration-settlement";yield return CaptureP47("20-exploration-settlement.png");
            yield return ClickChoice("settle:victory");yield return ClickChoice("growth-menu");
            Require(world.Menu&&world.FrontPage=="characters","Settlement did not return to Characters");yield return CaptureP47("21-growth-after-settlement.png");
            yield return FrontP47("role:fighter");
            int research=world.Profile.State.meta.research;
            // Purchase only an actually available growth node using the run's earned research.
            var growth=world.FrontChoices.FirstOrDefault(c=>c.id=="unlock:F_PRESSURE"&&c.enabled);
            Require(growth!=null,"Completed run did not offer its earned fighter growth purchase");
            yield return FrontP47(growth.id);Require(world.Profile.State.meta.research<research,"Growth did not spend real research");Record("p47_real_growth_purchase",growth.id);
            yield return FrontP47("explore");yield return FrontP47("map-ashen");yield return FrontP47("cycle:fighter");yield return FrontP47("depart");
            Require(world.Profile.ActiveMode.id!=run&&!world.Menu,"Next departure did not create another run");
            Require(world.Profile.ActiveMode.departurePreparation.preparation.actors.First(a=>a.actorId=="fighter").specializationId=="F_PRESSURE","Purchased specialization was not equipped on next departure");
            Require(world.State.focusBudget.secondsRemaining==FocusBudgetRules.InitialSeconds,"New run did not receive P47 initial Focus");
            yield return CaptureP47("22-next-exploration.png");
            Require(p4FreezeSeen,"No actual RollFreeze observed");Require(p4ReactionSeen,"No legal automatic Reaction observed; cannot claim its Player UI verification");
            Write("focus-economy.json",new{runs=p4Runs,encounters=p4Encounters.Values,actualAutomatedInput=true,humanAcceptance=false,
                observationScope="Focus counters use actual persisted cumulative deltas including the final contact; thinking is observed unscaled active UI frame time, excludes RollFreeze/forced Reaction. Other momentum/command encounter samples may miss the final contact and are lower bounds, not complete ledgers."});
            Write("dice-sound-timeline.json",world.Audio.DiceSoundTimeline);
            Record("p47_complete_exploration_loop","Actual current dungeon completed, settled, returned to growth and began a distinct configured run; no HP, RNG, encounter or dice-time injection.");
            completed=true;
        }
    }
}
