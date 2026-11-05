using System;
using CombatV051.Domain;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        void V14CaptureEvadeThreat(ActorState enemy,string root,AreaShape shape)
        {V14CaptureEvadeThreat(enemy,root,a=>BattleGeometry.Contains(shape,Position(a))&&HasLineOfEffect(shape.Origin,Position(a)));}
        void V14CaptureEvadeThreat(ActorState enemy,string root,Func<ActorState,bool> inside)
        {
            if(!V14Enabled||enemy==null)return;
            foreach(var actor in State.actors)
                Session.V14CaptureEvadeWarning(State,enemy.id,root,actor.id,actor.Alive&&IsPhysical(actor)&&inside(actor)&&HasLineOfEffect(Position(enemy),Position(actor)));
        }
        void V14SettleEvadeThreat(ActorState enemy,string root,AreaShape shape)
        {V14SettleEvadeThreat(enemy,root,a=>BattleGeometry.Contains(shape,Position(a)));}
        void V14SettleEvadeThreat(ActorState enemy,string root,Func<ActorState,bool> inside)
        {
            if(!V14Enabled||enemy==null)return;
            foreach(var actor in State.actors)
                Session.V14SettleEvadeWarning(State,enemy.id,root,actor.id,inside(actor));
        }
    }
}
