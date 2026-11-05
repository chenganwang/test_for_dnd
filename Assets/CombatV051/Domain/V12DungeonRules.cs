using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class ProfileSession
    {
        V12DungeonSnapshot nextV12Dungeon=V12DungeonDefaults.Create();
        public bool UsesV12Dungeon=>ActiveMode?.v12Run!=null;
        public DungeonConfig ActiveDungeonConfig=>ConfigFor(ActiveMode);
        public string ActiveDungeonKey=>ActiveMode.v12Run?.hash??State.configHash;
        public void SetNextV12Dungeon(V12DungeonSnapshot rules){nextV12Dungeon=CombatJson.Clone(rules);}
        DungeonConfig ConfigFor(ModeState mode)=>mode?.v12Run?.dungeon??Config;
        static bool HasV12Map(ModeState mode)=>mode.v12Run!=null;
        static string V12MapHash(V12DungeonSnapshot map)=>Hash(new{map.version,map.dungeon,map.purposes,map.obstacles,map.points,map.layoutSlots});
        public V12RoomPurpose ActiveV12Purpose=>ActiveMode.v12Run?.purposes.Find(p=>p.roomId==ActiveRoom.templateId);
        public bool V12EncounterTriggerReached()
        {
            var purpose=ActiveV12Purpose;if(purpose==null||!purpose.optionalEncounter)return true;
            return Near(ActiveCombat.State.Actor(ActiveCombat.State.activeActorId),purpose.triggerX,purpose.triggerZ,purpose.triggerRadius);
        }
        void SnapshotV12Dungeon(ModeState mode)
        {
            if(mode.v11Departure?.version!="v12-departure-1"||nextV12Dungeon==null)return;
            var map=CombatJson.Clone(nextV12Dungeon);map.layoutSlots.Clear();
            for(int i=0;i<3;i++)map.layoutSlots.Add(RandomStreams.NextInt(mode.random.layout,0,2));
            var archer=map.dungeon.Room("V12_R2").enemies.Find(e=>e.id=="archer");archer.z=map.layoutSlots[0]==0?4:-4;
            var cache=map.points.Find(p=>p.id=="V12_R3_CACHE");cache.x=map.layoutSlots[1]==0?47:58;cache.z=map.layoutSlots[1]==0?-4:7;
            var support=map.dungeon.Room("V12_R4").enemies.Find(e=>e.id=="support");support.z=map.layoutSlots[2]==0?18:27;
            map.hash=V12MapHash(map);mode.v12Run=map;
            // This is a newly created departure only; older paid snapshots retain null beam rules.
            mode.v11Departure.v12.beam=new V12BeamConfig();
            mode.v11Departure.hash=V11DepartureHash(mode.v11Departure);
        }
        bool V12PassageOpen(ModeState mode,PassageTemplate passage)
        {
            // Side exploration in R3 never gates the ordinary route.
            switch(passage.id)
            {
                case "V12_P12":return mode.Room("V12_R1").resolved;
                case "V12_P23":return mode.Room("V12_R2").resolved;
                case "V12_P34":return mode.Room("V12_R2").resolved;
                case "V12_P45":return mode.Room("V12_R4").resolved;
                case "V12_P56":return mode.Room("V12_R5").visited&&mode.claimedRewardIds.Contains(mode.Room("V12_R5").id+"/reward");
                default:return false;
            }
        }
        void V12RoomEntered(ModeState mode,RoomState room)
        {
            if(!HasV12Map(mode))return;
            if(room.templateId=="V12_R5")CreateV11Reward(mode,room);
        }
        void ValidateV12Dungeon(ModeState mode)
        {
            var map=mode.v12Run;if(map==null)return;
            Require(map.version=="v12-six-rooms-1"&&mode.v11Departure?.version=="v12-departure-1"&&map.hash==V12MapHash(map),"unsupported_or_changed_v12_map");
            Require(map.layoutSlots!=null&&map.layoutSlots.Count==3&&map.layoutSlots.All(i=>i==0||i==1),"v12_layout_slots_invalid");
            var config=map.dungeon;Require(config!=null&&config.contentVersion=="dungeon-v12-six-1"&&config.rooms?.Count==6&&Enumerable.Range(1,6).All(i=>config.Room("V12_R"+i)!=null),"v12_six_rooms_required");
            Require(config.passages?.Count==5&&config.events?.Count==2&&config.enemies!=null&&config.rules!=null,"v12_map_domains_missing");
            int[] caps={2,4,2,4,0,3};
            foreach(var room in config.rooms)
            {
                int number=int.Parse(room.id.Substring(5));Require(Finite(room.x)&&Finite(room.z)&&room.width>=12&&room.depth>=12&&room.enemies!=null&&room.enemies.Count<=caps[number-1],"v12_room_geometry_or_cap");
                Require(room.enemies.Select(e=>e.id).Distinct().Count()==room.enemies.Count,"v12_duplicate_spawn");
                foreach(var spawn in room.enemies)Require(config.Enemy(spawn.templateId)!=null&&Math.Abs(spawn.x-room.x)<room.width/2-1&&Math.Abs(spawn.z-room.z)<room.depth/2-1,"v12_spawn_outside_room");
            }
            Require(config.rooms.Select(r=>r.id).Distinct().Count()==6&&config.Room("V12_R5").enemies.Count==0&&config.Room("V12_R6").encounterKind=="Boss","v12_room_purpose_invalid");
            Require(config.passages.Select(p=>p.id).Distinct().Count()==5,"v12_duplicate_passage");
            foreach(var p in config.passages)Require(config.Room(p.roomA)!=null&&config.Room(p.roomB)!=null&&p.roomA!=p.roomB&&p.width>=3&&Finite(p.x)&&Finite(p.z),"v12_invalid_passage");
            foreach(var enemy in config.enemies)Require(enemy.hp>0&&enemy.hp<=10000&&enemy.armorClass>0&&enemy.speed>=0&&enemy.patterns!=null&&enemy.patterns.Count>0&&enemy.patterns.All(p=>(p.shape=="Cone"||p.shape=="Circle")&&p.windup>.1f&&p.recovery>.1f&&p.damage>=0),"v12_invalid_enemy");
            Require(map.purposes!=null&&map.purposes.Count==6&&map.purposes.Select(p=>p.roomId).Distinct().Count()==6&&map.purposes.All(p=>config.Room(p.roomId)!=null&&!string.IsNullOrEmpty(p.name)),"v12_room_purposes_missing");
            Require(map.points!=null&&map.points.Select(p=>p.id).Distinct().Count()==map.points.Count&&map.points.All(p=>config.Room(p.roomId)!=null&&p.range>0&&p.healFraction>=0&&p.healFraction<=1),"v12_points_invalid");
            Require(map.obstacles!=null&&map.obstacles.Select(p=>p.id).Distinct().Count()==map.obstacles.Count&&map.obstacles.All(p=>config.Room(p.roomId)!=null&&p.width>0&&p.depth>0&&p.height>0),"v12_obstacles_invalid");
            Require(config.events.Select(e=>e.id).Distinct().Count()==2&&config.events.All(e=>e.roomId=="V12_R3"&&e.range>0&&e.dc>0&&(e.worldEffect=="V12_LIBRARY_READ"||e.worldEffect=="V12_LIBRARY_CACHE")),"v12_events_invalid");
            Require(mode.claimedRewardIds.Count<=4&&mode.claimedRewardIds.All(id=>new[]{"V12_R1","V12_R2","V12_R4","V12_R5"}.Any(r=>id==mode.id+"/"+r+"/reward")),"v12_reward_budget_exceeded");
        }
        ProfileResult V12Rest(ProfileState profile,ProfileCommand command)
        {
            var mode=profile.ActiveMode;EnsureSafe(mode);EnsureRewardFinished(mode);
            Require(command.qualified&&command.selectionId!=null&&command.selectionId.StartsWith("v12:",StringComparison.Ordinal),"v12_supply_fact_required");
            string id=command.selectionId.Substring(4);var point=mode.v12Run.points.Find(p=>p.id==id&&p.kind=="supply");
            Require(point!=null&&point.roomId==mode.Room(mode.currentRoomId).templateId,"v12_supply_wrong_room");
            Require(!mode.usedRestIds.Contains(id),"v12_supply_already_used");var combat=Current(mode);var actor=combat.Actor(combat.activeActorId);
            Require(actor!=null&&actor.Alive&&Near(actor,point.x,point.z,point.range),"v12_supply_out_of_reach");
            Require(combat.actors.Any(a=>a.Alive&&a.hp<a.maxHp),"nothing_to_heal");
            var healingConfig=BuildConfig(mode);
            foreach(var member in combat.actors.Where(a=>a.Alive))CombatSession.V12HealExisting(combat,healingConfig,member,Math.Max(1,(int)Math.Floor(member.maxHp*point.healFraction)));
            AddOnce(mode.usedRestIds,id);AddOnce(mode.flags,"V12/explored/"+id);
            if(id=="V12_R3_CACHE"&&UsesV12Build(mode))CombatSession.ApplyV12ExplorationReward(combat,healingConfig,mode.id+"/"+id);
            return new ProfileResult();
        }
        void ApplyV12EventEffect(ModeState mode,SafeEventState ev,bool freeRead)
        {
            if(!HasV12Map(mode)||(!freeRead&&!ev.success))return;
            if(freeRead)Require(ev.templateId=="V12_READ"&&ev.rollCount==0,"v12_free_read_unavailable");
            AddOnce(mode.flags,ev.worldEffect);AddOnce(mode.flags,"V12/explored/"+ev.templateId);
            // Read opens the actual shelf shortcut. Search grants one optional supply, never combat dice.
            if(ev.templateId=="V12_SEARCH")
            {var combat=Current(mode);var config=BuildConfig(mode);foreach(var actor in combat.actors.Where(a=>a.Alive))CombatSession.V12HealExisting(combat,config,actor,Math.Max(1,(int)Math.Floor(actor.maxHp*.1f)));}
        }
    }
}
