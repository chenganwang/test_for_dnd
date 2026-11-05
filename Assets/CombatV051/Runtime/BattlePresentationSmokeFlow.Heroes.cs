using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CombatV051.Runtime
{
    public sealed partial class BattlePresentationSmokeFlow
    {
        readonly List<object> heroContacts=new List<object>();
        readonly HashSet<string> heroContactIds=new HashSet<string>();
        int heroLastHp; bool heroPlayerHeal,heroAiHeal;
        void ObserveHeroContact(CombatCommand command,CombatResult result)
        {
            try { ObserveHeroContactCore(command,result); }
            catch(Exception error) { observerFailure="Hero evidence observer: "+error; }
        }
        void ObserveHeroContactCore(CombatCommand command,CombatResult result)
        {
            if(!world.MainCharacterTraining||!result.success||result.duplicate)return;
            var finn=world.State.Actor("char_story_finn");
            var intent=finn?.basicAttackIntent;
            var effects=result.v11?.effects.Where(e=>e.kind=="micro_heal").ToArray();
            if((intent!=null&&intent.resolved&&heroContactIds.Add(intent.rootId))||effects?.Length>0)
            {
                bool controlled=world.State.activeActorId==finn.id;
                if(effects?.Length>0){if(controlled)heroPlayerHeal=true;else heroAiHeal=true;}
                heroContacts.Add(new {command=command.id,commandKind=command.kind.ToString(),simulation=world.State.simulationSeconds,controlled,
                    hpAtPreviousCommand=heroLastHp,hpAfter=finn.hp,maxHp=finn.maxHp,attack=CombatJson.Clone(intent),effects=CombatJson.Clone(result.v11),
                    sustain=CombatJson.Clone(finn.v11?.finnSustain),resolvedResult=CombatJson.Clone(result),enemies=world.State.enemies.Select(e=>new {e.id,e.hp}).ToArray()});
                Write("hero-contacts.json",new {heroPlayerHeal,heroAiHeal,contacts=heroContacts,training=true,naturalRng=true});
            }
            heroLastHp=finn.hp;
        }
        IEnumerator HeroTrainingRound(bool ai)
        {
            yield return FrontP47("campaign");yield return FrontP47("training-main");
            Require(world.MainCharacterTraining,"Existing arena did not select main-character roster.");
            string[] ids={"char_story_evan","char_story_mira","char_story_finn"};
            Require(world.State.actors.Select(a=>a.id).SequenceEqual(ids),"Training lost the three distinct IDs.");
            yield return null;world.RefreshArtPresentation();yield return null;
            var models=world.GetComponentsInChildren<HeroVisual>(true).Where(v=>ids.Contains(v.CharacterId)).ToArray();
            Require(models.Select(v=>v.CharacterId).Distinct().Count()==3,"Battle does not contain three independent actual Hero Prefabs.");
            yield return CaptureP47(ai?"12-heroes-ai-round.png":"08-three-heroes-battle.png");
            for(int i=0;i<3;i++)
            {
                yield return Press((Key)((int)Key.Digit1+i));Require(world.State.activeActorId==ids[i],"Controlled character input mapping changed.");
                Keys(Key.A);yield return new WaitForSecondsRealtime(.22f);Keys();
                yield return ClickWorld(BattleWorld.Position(Enemy()));
                yield return CaptureP47("09-controlled-"+ids[i]+(ai?"-ai":"")+".png");
            }
            heroLastHp=world.State.Actor(ids[2]).hp;
            yield return Press(Key.Digit3);
            // Establish missing HP by exposing Finn to the shipping opponent,
            // then use his ranged reach. No HP, dice or resources are assigned.
            stage="heroes-finn-natural-damage-setup";
            float setupDeadline=Time.realtimeSinceStartup+40;
            while(Time.realtimeSinceStartup<setupDeadline&&world.ActiveActor.Alive&&world.ActiveActor.hp==world.ActiveActor.maxHp)
            {
                if(world.State.mode==CombatMode.ReactionPrompt){yield return P6Click("reaction-decline");continue;}
                var target=Enemy(false);if(target==null)break;
                if(BattleGeometry.DistanceXZ(BattleWorld.Position(world.ActiveActor),BattleWorld.Position(target))>1.8f)DriveP4Combat(BattleWorld.Position(target));else Keys();
                yield return null;
            }
            Keys();
            Require(world.ActiveActor.Alive&&world.ActiveActor.hp<world.ActiveActor.maxHp,"Natural opponent did not create a surviving injured Finn; preserve attempt.");
            events.Add(new {kind="finn_naturally_injured",ai,hp=world.ActiveActor.hp,simulation=world.State.simulationSeconds});
            if(ai)yield return Press(Key.Digit2); // Mira takes over; Finn now uses unchanged teammate AI.
            stage=ai?"heroes-finn-ai-natural-contact":"heroes-finn-player-natural-contact";
            float deadline=Time.realtimeSinceStartup+65;
            while(Time.realtimeSinceStartup<deadline&&world.State.Actor(ids[2]).Alive&&world.State.enemies.Any(e=>e.Alive)&&!(ai?heroAiHeal:heroPlayerHeal))
            {
                if(world.State.mode==CombatMode.ReactionPrompt){yield return P6Click("reaction-decline");continue;}
                if(world.State.mode==CombatMode.RollFreeze){yield return null;continue;}
                if(world.State.mode==CombatMode.PostRollDecision){yield return P6Click("commit");continue;}
                if(world.State.mode==CombatMode.TacticalFocus){yield return P6Click("exit");continue;}
                var enemy=Enemy(false);if(enemy==null)break;
                float distance=BattleGeometry.DistanceXZ(BattleWorld.Position(world.ActiveActor),BattleWorld.Position(enemy));
                if(!ai&&distance<4.5f)
                {
                    var away=BattleGeometry.Ground(BattleWorld.Position(world.ActiveActor)-BattleWorld.Position(enemy)).normalized;
                    DriveP4Combat(BattleWorld.Position(enemy)+away*5.2f);yield return null;continue;
                }
                if(distance>(ai?2.4f:5.6f)){DriveP4Combat(BattleWorld.Position(enemy));yield return null;continue;}
                Keys();
                if(!ai)yield return ClickWorld(BattleWorld.Position(enemy));
                else
                {
                    AimAt(BattleWorld.Position(enemy));yield return null;MouseState(2);yield return new WaitForSecondsRealtime(.2f);MouseState(0);yield return null;
                }
            }
            Keys();MouseState(0);yield return CaptureP47(ai?"13-finn-ai-result.png":"10-finn-heal-result.png");
            Write(ai?"heroes-ai-final.json":"heroes-player-final.json",new {combat=world.State,heroPlayerHeal,heroAiHeal,contacts=heroContacts});
            yield return P6Click("p6-finish");Require(world.Menu&&!world.Training,"Training failed to restore original menu.");
        }
        IEnumerator FlowHeroes()
        {
            yield return null;yield return null;
            world.InitializeP6FrontendForTests(Path.Combine(directory,"Profiles","profile.json"));
            world.CommandPublished+=ObserveHeroContact;
            stage="heroes-main";yield return CaptureP47("00-new-main.png");
            yield return FrontP47("characters");yield return CaptureP47("01-new-roster.png");
            foreach(string id in new[]{"char_story_evan","char_story_mira","char_story_finn"})
            {
                stage="heroes-tree-"+id;yield return FrontP47("role:"+id);
                yield return FrontP47("node:"+id+"/active2");yield return V11ClickBlocked("front-node-confirm");
                yield return FrontP47("node:"+id+"/passive");yield return FrontP47("node-confirm");
                yield return FrontP47("node:"+id+"/active2");yield return FrontP47("node-confirm");
                yield return CaptureP47("02-new-tree-"+id+".png");yield return FrontP47("characters");
            }
            string progress=CombatJson.Write(world.Profile.State.meta.v11);
            Write("hero-unlocks-before-reload.json",world.Profile.State.meta.v11);
            yield return FrontP47("main");world.InitializeP6FrontendForTests(world.SavePath);yield return null;
            Require(CombatJson.Write(world.Profile.State.meta.v11)==progress,"Main trio purchases did not survive real disk reload.");
            Write("hero-unlocks-after-reload.json",world.Profile.State.meta.v11);
            yield return FrontP47("explore");yield return FrontP47("map-ashen");yield return CaptureP47("04-themed-preparation.png");
            yield return FrontP47("explore");yield return FrontP47("main");
            string actors=CombatJson.Write(world.Profile.ActiveCombat.State.actors);
            yield return HeroTrainingRound(false);yield return HeroTrainingRound(true);
            Require(CombatJson.Write(world.Profile.State.meta.v11)==progress,"Training modified permanent unlocks.");
            Require(CombatJson.Write(world.Profile.ActiveCombat.State.actors)==actors,"Training modified suspended adventure actors/resources.");
            Write("hero-final.json",new {heroPlayerHeal,heroAiHeal,contacts=heroContacts,permanent=world.Profile.State.meta.v11,
                scope="Actual input, isolated training with shipping 250HP target, natural RNG. No HP/resource/position/dice injected. No subjective human art/fun claim.",
                missingPlayerHeal=!heroPlayerHeal,missingAiHeal=!heroAiHeal});
            Require(heroPlayerHeal&&heroAiHeal,"Natural training did not demonstrate both controlled and AI Finn micro-healing; preserve this failed attempt.");
            completed=true;stage="heroes-complete";Record("hero_ui_model_sustain","Three trees purchased/reloaded, actual three Prefabs, each controller moved/attacked, Finn natural player/AI micro-heals, suspended adventure preserved.");
        }
    }
}
