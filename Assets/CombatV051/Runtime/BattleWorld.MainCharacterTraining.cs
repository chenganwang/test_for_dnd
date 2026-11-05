using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        public bool MainCharacterTraining => Training && Session != null &&
            State.world.strings.TryGetValue("trainingRoster", out var value) && value == "MAIN_CHARACTERS";

        // A roster variant of the existing isolated arena. Adventure snapshots and
        // core eligibility remain owned by Profile; no training reward is exported.
        public void StartMainCharacterTraining()
        {
            if (Training || !EnsureFrontendProfile()) return;
            var progress = Profile.V11Progress;
            StartTraining();
            if (!Training) return;
            var state = State.Clone();
            var config = CombatJson.Clone(Session.Config);
            config.v11 = new V11CombatRules { coreActorId = null, version="v12-character-1" };
            var v12Parameters=Resources.Load<TextAsset>("V12CombatConfig");
            config.v12=v12Parameters==null?V12Defaults.Create():CombatJson.Read<V12CombatConfig>(v12Parameters.text);
            config.v11.v12BuildVersion=config.v12.build?.version;
            foreach (var definition in V11Catalog.Characters.Where(c => c.category == "Main"))
            {
                var nodes = V11Catalog.NodesFor(definition.id);
                config.v11.actors.Add(new V11ActorLoadout {
                    actorId = definition.id, passiveId = definition.passiveId,
                    passiveUnlocked = nodes.Any(n => n.kind == "Passive" && progress.unlockedNodeIds.Contains(n.id)),
                    unlockedActionIds = nodes.Where(n => n.kind == "Active").Select(n => n.abilityId).ToList(),
                    finnSustain = CombatJson.Clone(definition.finnSustain)
                });
            }
            state.world.strings["context"] = "TRAINING";
            state.world.strings["trainingRoster"] = "MAIN_CHARACTERS";
            CombatState.ApplyV11Party(state, config, true);
            state.activeActorId = state.commandActorId = config.v11.actors[0].actorId;
            CombatSession.InitializeP5State(state, config);
            CombatSession.InitializeP6State(state, config, true);
            CombatSession.InitializeV12State(state, config);
            Session = new CombatSession(state, config);
            P6LastDamage = ""; p6QuickFocus = false; p6DepartAfterTraining = false;
            SelectedActor = state.activeActorId;
            SelectedAction = config.v11.actors[0].unlockedActionIds[0];
            SelectedTarget = state.enemies.FirstOrDefault(e => e.Alive)?.id;
            SynchronizeViews();
            Rig.Initialize(BattleCamera, () => Position(ActiveActor), PartyCenter, () => IsFrozen, () => IsFocus); Rig.Step(10);
            ResetCombatPresentationObservation(); ApplyTime(); Hud.RefreshView();
            Message = "独立三主角练习：1/2/3 切换；F 免费基础技，Q 关键技，Tab 全队指挥。两个主动本次练习可用，被动沿用已保存养成。";
        }
    }
}
