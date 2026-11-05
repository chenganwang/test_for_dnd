using System;
using System.Collections.Generic;

namespace CombatV051.Domain
{
    [Serializable] public sealed class ActorRuleOverride
    {
        public string actorId,specializationId;
        public float protectionRange=3,flankWindowSeconds=4,preparationMoveMultiplier=.55f,studySeconds=1;
        public bool firstShoveBonus,nearMissCorrection,firstExposeExtension,allyShield,concentrationProtection;
        public int firstShoveBreakBonus=1,nearMissMaximumDeficit=2,nearMissBonus=2;
    }
    public sealed partial class CombatConfig
    {
        public List<ActorRuleOverride> actorOverrides=new List<ActorRuleOverride>();
        public ActorRuleOverride RulesFor(string actorId){return actorOverrides.Find(x=>x.actorId==actorId)??new ActorRuleOverride{actorId=actorId};}
    }
}
