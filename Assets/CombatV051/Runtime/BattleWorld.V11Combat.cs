using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        public bool V11CombatEnabled=>Session?.Config.v11!=null;
        public IReadOnlyList<string> AvailableActions(string actorId)
        {
            var actor=State?.Actor(actorId);if(actor==null)return Array.Empty<string>();
            if(V11CombatEnabled)return Session.Config.v11.actors.Find(a=>a.actorId==actorId)?.unlockedActionIds??new List<string>();
            return Session.Config.Template(actor.role)?.actionIds??new List<string>();
        }
        public ActorState RadianceCaster=>State?.actors.Where(a=>a.Alive&&AvailableActions(a.id).Contains(CombatSession.P6BindId)).OrderBy(a=>a.p6?.bindCooldown??0).FirstOrDefault();
        public void SelectTacticalSlot(int index)
        {if(index>=0&&index<State.actors.Count)SelectCommandActor(State.actors[index].id);}
        public void SelectV11Action(string id)
        {
            if(!V11CombatEnabled||State.mode!=CombatMode.TacticalFocus||!AvailableActions(SelectedActor).Contains(id))return;
            p6QuickFocus=false;SelectedAction=id;SelectedOpportunity=null;
            NormalizeV11Target();
            if(id==CombatSession.P6BindId)
            {
                ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.RequestBindRadiance,actorId=SelectedActor,targetId=SelectedTarget,actionId=id});
            }
            else NormalizeV11Target();
            Hud.RefreshView();RefreshTacticalPreview();
        }
        void NormalizeV11Target()
        {
            if(V13Enabled&&SelectedAction=="v13_evan_correct"){if(!State.actors.Any(a=>a.id==SelectedTarget))SelectedTarget=State.activeActorId;return;}
            if(V13Enabled&&V13NodeAvailable(SelectedTarget))return;
            if(V13Enabled&&(SelectedAction=="v13_finn_shield"||SelectedAction=="v13_finn_prayer")){if(V13AssistMode||!State.actors.Any(a=>a.Alive&&a.id==SelectedTarget))SelectedTarget=State.activeActorId;return;}
            if(!V11CombatEnabled||State.pendingRoll!=null||State.mode!=CombatMode.TacticalFocus)return;
            if(V11AbilityCatalog.IsSelfTarget(SelectedAction))SelectedTarget=SelectedActor;
            else if(State.Actor(SelectedTarget)?.isEnemy!=true||State.Actor(SelectedTarget)?.Alive!=true)
                SelectedTarget=State.enemies.FirstOrDefault(e=>e.Alive)?.id;
        }
        void ObserveV11Command(CombatCommand command,CombatResult result)
        {
            if(!V11CombatEnabled||!result.success||result.duplicate||result.v11==null)return;
            var descriptions=new List<string>();
            foreach(var effect in result.v11.effects)
            {
                string name=V11Catalog.FindCharacter(effect.targetId)?.displayName??DisplayTarget(effect.targetId);
                if(effect.kind=="micro_heal")
                {
                    if(effect.amount>0){descriptions.Add(name+" +"+effect.amount+" · 自然感知");Feedback?.MicroHeal(Position(State.Actor(effect.targetId)));}
                    continue;
                }
                descriptions.Add(name+" · "+(effect.kind=="heal"?"回复 "+effect.amount:effect.kind=="absorption"?"吸收盾 "+effect.amount:effect.kind=="damage"?"伤害 "+effect.amount:effect.kind=="p6-root"?"短封缚 "+effect.seconds.ToString("0.#")+"s":"减速 "+effect.seconds.ToString("0.#")+"s"));
                if(effect.kind!="damage")NotifyArtCue("spell",effect.actorId,effect.targetId,command.id+"/v11");
            }
            Message=descriptions.Count>0?string.Join("；",descriptions):"本次检定未生效；已付战术次数不返还。";
            if(result.v11.effects.Any(e=>e.kind!="damage"&&e.kind!="micro_heal"))Audio?.PlayCue(AudioCue.SpellImpact,Position(State.Actor(command.actorId)),"v11-effect/"+command.id,.8f);
        }
        public string V11CoreSummary
        {
            get
            {
                if(!V11CombatEnabled)return "";
                var rules=Session.Config.v11;var effects=V11BuffCatalog.DisplayBuffs(rules);
                return "核心："+(V11Catalog.FindCharacter(rules.coreActorId)?.displayName??"未指定")+" · "+
                    (effects.Count==0?"尚无本局增益":string.Join(" / ",effects.Select(d=>d.name+(V11BuffCatalog.Stacks(rules,d.id)>1?" ×"+V11BuffCatalog.Stacks(rules,d.id):""))));
            }
        }
    }
}
