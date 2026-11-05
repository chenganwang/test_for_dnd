using System;
using System.Collections.Generic;

namespace CombatV051.Domain
{
    [Serializable] public sealed class EnemyPattern
    {
        public string id="strike",shape="Cone",targetPriority="Nearest";
        public float windup=1.1f,recovery=1.1f,size=3.5f,radius=2.2f,angle=65,phaseThreshold=1;
        public int damage=3,dc=12;
        public bool savingThrow,parryable=true;
    }
    [Serializable] public sealed class EnemyTemplate
    {
        public string id;
        public int hp=65,armorClass=13,attackBonus=3,saveBonus=2,dexSaveBonus=2,wisSaveBonus=1;
        public float breakMaximum=3,speed=2;
        public List<EnemyPattern> patterns=new List<EnemyPattern>{new EnemyPattern()};
    }
    [Serializable] public sealed class EnemySpawn { public string id,templateId;public float x,z;public bool bossMechanism; }
    [Serializable] public sealed class RoomTemplate
    {
        public string id,encounterKind;
        public float x,z,width=24,depth=20;
        public List<string> eventIds=new List<string>();
        public List<EnemySpawn> enemies=new List<EnemySpawn>();
    }
    [Serializable] public sealed class PassageTemplate { public string id,roomA,roomB;public float x,z,width=6; }
    [Serializable] public sealed class EventTemplate
    {
        public string id,roomId,ability="Intelligence",worldEffect;
        public float x,z,range=5;
        public int dc=13,preparation;
        public bool advantage,disadvantage;
    }
    [Serializable] public sealed class BuildRule
    {
        public string id,kind="Relic",description;
        public int chargeCapDelta,startChargeDelta;
        public float aimMultiplier=1,reactionMultiplier=1,preparationRepeatMultiplier=1;
    }
    [Serializable] public sealed class DungeonConfig
    {
        public string contentVersion="dungeon-v051-p2-1";
        public float restHealFraction=.35f;
        public List<RoomTemplate> rooms=new List<RoomTemplate>{
            new RoomTemplate{id="R0",x=0,z=0},
            new RoomTemplate{id="R1",x=26,z=0,encounterKind="Normal",enemies=new List<EnemySpawn>{new EnemySpawn{id="guard",templateId="guard",x=29,z=1}}},
            new RoomTemplate{id="R2",x=52,z=0,eventIds=new List<string>{"EVENT_A"}},
            new RoomTemplate{id="R3",x=78,z=0,encounterKind="Normal",eventIds=new List<string>{"EVENT_B"},enemies=new List<EnemySpawn>{new EnemySpawn{id="guard",templateId="guard",x=80,z=2},new EnemySpawn{id="scout",templateId="scout",x=83,z=-3}}},
            new RoomTemplate{id="R4",x=52,z=22,encounterKind="Normal",enemies=new List<EnemySpawn>{new EnemySpawn{id="guard",templateId="guard",x=55,z=21},new EnemySpawn{id="scout",templateId="scout",x=49,z=25}}},
            new RoomTemplate{id="R5",x=26,z=22,encounterKind="Elite",enemies=new List<EnemySpawn>{new EnemySpawn{id="elite",templateId="elite",x=29,z=23}}},
            new RoomTemplate{id="R6",x=0,z=22},
            new RoomTemplate{id="R7",x=-26,z=22,encounterKind="Boss",enemies=new List<EnemySpawn>{new EnemySpawn{id="boss",templateId="boss",x=-29,z=24},new EnemySpawn{id="mechanism",templateId="mechanism",x=-32,z=18,bossMechanism=true}}}
        };
        public List<PassageTemplate> passages=new List<PassageTemplate>{
            new PassageTemplate{id="P01",roomA="R0",roomB="R1",x=13,z=0},new PassageTemplate{id="P12",roomA="R1",roomB="R2",x=39,z=0},new PassageTemplate{id="P23",roomA="R2",roomB="R3",x=65,z=0},
            new PassageTemplate{id="P24_SMASH",roomA="R2",roomB="R4",x=48,z=11,width=4},new PassageTemplate{id="P24_OUTER",roomA="R2",roomB="R4",x=56,z=11,width=4},
            new PassageTemplate{id="P45",roomA="R4",roomB="R5",x=39,z=22},new PassageTemplate{id="P56",roomA="R5",roomB="R6",x=13,z=22},new PassageTemplate{id="P67",roomA="R6",roomB="R7",x=-13,z=22},new PassageTemplate{id="P60_SHORTCUT",roomA="R6",roomB="R0",x=0,z=11}
        };
        public List<EnemyTemplate> enemies=new List<EnemyTemplate>{
            new EnemyTemplate{id="guard",hp=75},new EnemyTemplate{id="scout",hp=45,armorClass=12,speed=2.6f,patterns=new List<EnemyPattern>{new EnemyPattern{id="scout-cone",damage=2,windup=.9f,recovery=1.2f,size=3}}},
            new EnemyTemplate{id="elite",hp=150,armorClass=14,breakMaximum=5,patterns=new List<EnemyPattern>{new EnemyPattern{id="elite-cone",damage=4,windup=1.2f,size=4},new EnemyPattern{id="elite-circle",shape="Circle",damage=4,dc=12,savingThrow=true,parryable=false,radius=2.5f,windup=1.5f,recovery=1.5f}}},
            new EnemyTemplate{id="boss",hp=230,armorClass=14,breakMaximum=7,speed=1.8f,patterns=new List<EnemyPattern>{new EnemyPattern{id="boss-cone",damage=4,windup=1.3f,size=4.5f,angle=70},new EnemyPattern{id="boss-ground",shape="Circle",damage=4,savingThrow=true,parryable=false,radius=2.7f,windup=1.5f,recovery=1.4f},new EnemyPattern{id="boss-phase2",shape="Cone",damage=5,windup=1.1f,size=5,angle=90,phaseThreshold=.5f}}},
            new EnemyTemplate{id="mechanism",hp=30,armorClass=10,speed=0,breakMaximum=2,patterns=new List<EnemyPattern>{new EnemyPattern{id="mechanism-ground",shape="Circle",damage=2,savingThrow=true,parryable=false,windup=1.7f,recovery=2,radius=2}}}
        };
        public List<EventTemplate> events=new List<EventTemplate>{new EventTemplate{id="EVENT_A",roomId="R2",x=52,z=2,worldEffect="ROUTE_OPEN",dc=13},new EventTemplate{id="EVENT_B",roomId="R3",x=77,z=5,worldEffect="BOSS_MECHANISM_DISABLED",dc=14,ability="Wisdom"}};
        public List<BuildRule> rules=new List<BuildRule>{new BuildRule{id="BASE",kind="Law",description="基准指挥规则"},new BuildRule{id="COMMAND",kind="Law",chargeCapDelta=1,aimMultiplier=1.25f,description="上限3，瞄准准备更慢"},new BuildRule{id="RESERVE",preparationRepeatMultiplier=5f/6,description="职业准备重复间隔缩短至基准的5/6；不增加起战次数"},new BuildRule{id="FOCUS",aimMultiplier=.65f,description="瞄准准备时间缩短"},new BuildRule{id="REACTION",reactionMultiplier=.7f,description="反应恢复时间缩短"}};
        public RoomTemplate Room(string id){return rooms.Find(r=>r.id==id);}
        public EnemyTemplate Enemy(string id){return enemies.Find(e=>e.id==id);}
        public EventTemplate Event(string id){return events.Find(e=>e.id==id);}
        public BuildRule Rule(string id){return rules.Find(r=>r.id==id);}
    }
}
