using System.Linq;
using CombatV051.Domain;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        void PopulateV12BuildEvidence(CombatCommand command)
        {
            if(!V12Enabled||!Session.V12BuildEnabled||!Session.Config.v11.buffs.Any(b=>b.id.StartsWith("v12_evan_coauthor")||b.id.StartsWith("v12_mira_sweep")||b.id.StartsWith("v12_finn_spread")||b.id.StartsWith("v12_finn_bloom")||V14Enabled&&(b.id=="v14_finn_quickgrowth"||b.id=="v14_finn_bloom")))return;
            switch(command.kind)
            {
                case CommandKind.BasicAttack:case CommandKind.ResolveBasicAttack:case CommandKind.ResolveQueued:
                case CommandKind.ResolveHeroAction:case CommandKind.ResolveHeroTick:case CommandKind.ResolveV12Beam:
                case CommandKind.IncomingAttack:case CommandKind.IncomingSave:case CommandKind.IncomingBatch:break;
                default:return;
            }
            // Small, command-time evidence; replay keeps its original payload. No per-frame
            // scan or engine instance IDs. Dead/missing sources never create permissive links.
            var data=new V12CommandData();
            foreach(var source in State.actors.Concat(State.enemies))
            {
                if(!source.Alive)continue;
                foreach(var target in State.enemies)
                    if(target.Alive&&target.id!=source.id)data.links.Add(new V12LineEvidence{fromId=source.id,toId=target.id,lineOfSight=HasLineOfEffect(Position(source),Position(target))});
            }
            command.v12=data;
        }
    }
}
