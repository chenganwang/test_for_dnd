using System;
using System.Linq;
using CombatV051.Domain;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        public bool PartyPresentation => expectPartyProfile || PartyMode;

        public void InitializePartyForTests(string path, bool startNew = true, bool useP5 = false)
        {
            legacyTestRules=!useP5; p6RuntimeRules=false;
            IgnoreApplicationFocus = true;
            SavePath = path; Profile = null; LoadP4Preferences(); IsolateSyntheticInput();
            expectDungeonProfile = false;
            expectPartyProfile = true;
            if (startNew) StartPartyNew();
        }

        public void StartPartyNew()
        {
            expectPartyProfile = true;
            var config = LoadConfig();
            BeginNew(() => CombatState.CreatePartyP1("profile-1", "party-p1/" + Guid.NewGuid().ToString("N"),
                unchecked((ulong)DateTime.UtcNow.Ticks), config));
            if (PartyMode) Message = "1 / 2 / 3 切换主控；专注中选择指挥队员。队友自行走位与基础攻击。";
        }

        public CombatResult SwitchActive(string actorId)
        {
            return ExecuteWorldCommand(new CombatCommand { id = Guid.NewGuid().ToString("N"),
                kind = CommandKind.SwitchActive, actorId = actorId });
        }

        public CombatResult SelectCommandActor(string actorId)
        {
            if (!PartyMode)
            {
                SelectedActor = actorId;
                return new CombatResult { success = true, reason = "ok" };
            }
            var result = ExecuteWorldCommand(new CombatCommand { id = Guid.NewGuid().ToString("N"),
                kind = CommandKind.SelectCommandActor, actorId = actorId });
            if (result.success)
            {
                SelectedOpportunity=null;
                SelectedActor = actorId;
                var options = AvailableActions(actorId);
                if (!options.Contains(SelectedAction)) SelectedAction = options.FirstOrDefault() ?? "attack";
                NormalizeV11Target();
                Hud.RefreshView();
            }
            return result;
        }

        public CombatResult React(CommandKind kind, string windowId = null)
        {
            var window = windowId == null ? State.activeReaction : State.reactionWindows.FirstOrDefault(w => w.id == windowId);
            if (kind == CommandKind.DeclineReaction && windowId == null)
                return ExecuteWorldCommand(new CombatCommand { id = Guid.NewGuid().ToString("N"), kind = kind, reactionEventId = State.activeReactionEventId });
            if (window == null) return new CombatResult { success = false, reason = "reaction_window_missing" };
            if (kind == CommandKind.BeginReaction)
            {
                var choice = Session.GetReactionChoices(window.reactionEventId).Where(q => q.legal && q.windowId == window.id).OrderBy(q => q.actorId == window.defenderId ? 0 : 1).FirstOrDefault();
                if (choice != null) return SelectReactionChoice(choice);
            }
            string reactor = window.reactorId ?? window.defenderId;
            var actor = State.Actor(reactor);
            return ExecuteWorldCommand(new CombatCommand { id = Guid.NewGuid().ToString("N"), kind = kind,
                actorId = reactor, targetId = window.defenderId, reactionWindowId = window.id, incomingId = window.incomingId,
                reactionEventId = window.reactionEventId,
                reactionId = window.reactionId ?? (actor.role == ActorRole.Mage ? "shield" : "parry"),
                actionId = window.reactionId ?? (actor.role == ActorRole.Mage ? "shield" : "parry"),
                lineOfSight = HasLineOfEffect(Position(actor), Position(State.Actor(window.defenderId))) });
        }

        public CombatResult SelectReactionChoice(ReactionQuote choice)
        {
            if (choice == null || !choice.legal) return new CombatResult { reason = "reaction_unavailable" };
            return ExecuteWorldCommand(new CombatCommand { id = Guid.NewGuid().ToString("N"), kind = CommandKind.BeginReaction,
                actorId = choice.actorId, targetId = choice.defenderId, reactionWindowId = choice.windowId,
                reactionEventId = choice.eventId, incomingId = choice.incomingId, reactionId = choice.reactionId, actionId = choice.reactionId,
                lineOfSight = HasLineOfEffect(Position(State.Actor(choice.actorId)), Position(State.Actor(choice.defenderId))) });
        }

        public CombatResult ConfirmCurrentDecision()
        {
            if(V13Enabled&&V13NodeAvailable(SelectedTarget))return V13AttackNode(SelectedActor,SelectedTarget,SelectedAction);
            if(V13Enabled&&SelectedAction=="v13_evan_correct")return V13Correct();
            if(V12Enabled&&State.mode==CombatMode.TacticalFocus&&State.pendingRoll==null&&V12ImmediateAction(SelectedAction))return RequestSelectedHeroAction();
            if (State.mode == CombatMode.ReactionPrompt)
            {
                var window = State.activeReaction;
                if (window == null) return new CombatResult { reason = "select_reaction_first" };
                var q = Session.QuoteReaction(window.id, window.reactorId ?? window.defenderId, window.reactionId);
                return React(q.requiresD20 ? CommandKind.ConfirmReactionRoll : CommandKind.CommitReaction);
            }
            if(P6Enabled && State.pendingRoll?.actionId=="ability_smite_recharge")return CommitSmite(false);
            return Command(State.pendingRoll == null ? CommandKind.ConfirmRoll : CommandKind.Commit);
        }

        public CombatResult CancelCurrentDecision()
        {
            return State.mode == CombatMode.ReactionPrompt ? React(CommandKind.DeclineReaction) : Command(CommandKind.Cancel);
        }
    }
}
