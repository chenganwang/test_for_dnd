using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        const string V14EvadePrefix="v14/evade/";
        // A boss contact is a sub-event of its registered warning, not a new roll
        // or a newly invented dangerous threat. Prefer the most specific real root.
        public static string V14RegisteredThreatRoot(CombatState s,string contactRoot)
        {
            if(s?.v14==null||string.IsNullOrEmpty(contactRoot))return null;
            return s.momentumLedger.registeredDangerousThreats.Where(root=>contactRoot==root||contactRoot.StartsWith(root+"/",StringComparison.Ordinal))
                .OrderByDescending(root=>root.Length).FirstOrDefault();
        }
        public static void V14ResetEvadeWorld(CombatState s)
        {
            if(s?.world==null)return;
            foreach(string key in s.world.strings.Keys.Where(k=>k.StartsWith(V14EvadePrefix,StringComparison.Ordinal)).ToList())s.world.strings.Remove(key);
            foreach(string key in s.world.numbers.Keys.Where(k=>k.StartsWith(V14EvadePrefix,StringComparison.Ordinal)).ToList())s.world.numbers.Remove(key);
        }
        static string V14EvadeKey(CombatState s,string root,string actor)=>V14EvadePrefix+s.encounterId+"/"+root+"/"+actor;
        bool V14EvadeSubject(CombatState s,ActorState actor)
        {return actor?.Alive==true&&V13Physical(s,actor)&&(actor.id==V12Mira||!V14Trio(s)&&actor.id==s.v13.coreId&&s.Actor(V12Mira)?.Alive==true);}
        // Called only by the world at the initial, visible warning. Even initial
        // exclusion is retained, so entering a safe old warning cannot farm rewards.
        public void V14CaptureEvadeWarning(CombatState s,string attackerId,string warningRoot,string actorId,bool initiallyCovered)
        {
            if(!V14Enabled||!s.inCombat||s.encounterResolved)return;string root=V14RegisteredThreatRoot(s,warningRoot);var actor=s.Actor(actorId);var enemy=s.Actor(attackerId);
            if(root==null||!V14EvadeSubject(s,actor)||enemy?.isEnemy!=true||!enemy.Alive||enemy.aiState!=1||
                !(enemy.attackGroupId==root||enemy.attackGroupId?.StartsWith(root+"/",StringComparison.Ordinal)==true))return;
            string key=V14EvadeKey(s,root,actor.id);if(s.world.strings.ContainsKey(key))return;
            s.world.strings[key]=initiallyCovered?"watch":"outside";s.world.strings[key+"/source"]=enemy.id;
            if(!initiallyCovered)return;
            s.world.numbers[key+"/x"]=actor.x;s.world.numbers[key+"/z"]=actor.z;s.world.numbers[key+"/hp"]=actor.hp;s.world.numbers[key+"/time"]=s.simulationSeconds;
        }
        // Invoked at actual contact/charge completion, never merely on disappearing
        // VFX, canceled windups, broken nodes or an unregistered empty dodge.
        public bool V14SettleEvadeWarning(CombatState s,string attackerId,string contactRoot,string actorId,bool stillCovered)
        {
            if(!V14Enabled||!s.inCombat||s.encounterResolved)return false;string root=V14RegisteredThreatRoot(s,contactRoot);var actor=s.Actor(actorId);var enemy=s.Actor(attackerId);
            if(root==null||!V14EvadeSubject(s,actor)||enemy?.isEnemy!=true||!enemy.Alive)return false;
            string key=V14EvadeKey(s,root,actor.id);
            if(!s.world.strings.TryGetValue(key,out string status)||status!="watch"||!s.world.strings.TryGetValue(key+"/source",out string owner)||owner!=attackerId)return false;
            if(!s.world.numbers.TryGetValue(key+"/x",out float x)||!s.world.numbers.TryGetValue(key+"/z",out float z)||!s.world.numbers.TryGetValue(key+"/hp",out float hp)||!s.world.numbers.TryGetValue(key+"/time",out float time))return false;
            s.world.strings[key]="settled";
            if(stillCovered||s.simulationSeconds<time||actor.hp<hp||(actor.x-x)*(actor.x-x)+(actor.z-z)*(actor.z-z)<=.16f)return false;
            // A contact, including a MISS, is not proof that movement avoided this
            // strike. In-volume invulnerability already follows the incoming path.
            if(s.incomingEvents.Any(e=>e.defenderId==actorId&&V14RegisteredThreatRoot(s,e.rootId)==root)||
                s.deferredIncomingBatches.Any(b=>V14RegisteredThreatRoot(s,b.rootId)==root&&b.contacts.Any(c=>c.targetId==actorId)))return false;
            bool awarded=V14GrantVerifiedAvoid(s,actor,root);if(awarded)s.world.strings[key]="avoided";return awarded;
        }
        bool V14GrantVerifiedAvoid(CombatState s,ActorState actor,string root)
        {
            if(!V14Enabled||!V14EvadeSubject(s,actor)||string.IsNullOrEmpty(root))return false;
            if(actor.id==V12Mira)
            {
                var e=V14Encounter(s,actor);if(e.dodgeRoots.Contains(root))return false;e.dodgeRoots.Add(root);
                if(V12Passive(actor)){actor.v12.lastDodgeRoot=root;actor.v12.readinessRemaining=Config.v12.readinessSeconds;}
                if(Config.v11.coreActorId==V12Mira)
                {var data=V14Core(actor);data.dodgeEncounter=s.encounterId;data.dodgeRoot=root;data.dodgeUntil=s.simulationSeconds+4;}
                return true;
            }
            var member=Member(s,V12Mira);if(member==null||member.lastAvoidedRoot==root)return false;member.lastAvoidedRoot=root;member.assistRewardUntil=s.simulationSeconds+4;return true;
        }
    }
}
