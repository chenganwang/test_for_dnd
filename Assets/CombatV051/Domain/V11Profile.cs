using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class ProfileSession
    {
        V12CombatConfig nextV12Rules=V12Defaults.Create();
        bool nextV13;
        bool nextV14;
        public void EnableV13Departures(bool enabled=true){nextV13=enabled;if(!enabled)nextV14=false;}
        public void EnableV14Departures(bool enabled=true){nextV14=enabled;if(enabled)nextV13=true;}
        // Changes the next departure only. Passing null is an explicit legacy fixture/tool choice.
        public void SetNextV12Rules(V12CombatConfig rules){nextV12Rules=CombatJson.Clone(rules);}
        public V11ProgressState V11Progress {get{return CombatJson.Clone(State.meta.v11);}}
        public V11PreparationDraft V11PreparationDraft {get{return CombatJson.Clone(State.meta.v11?.preparation??new V11PreparationDraft());}}
        public IReadOnlyList<string> V11UnknownNodeIds {get{return State.meta.v11==null?new List<string>():State.meta.v11.unlockedNodeIds.Where(id=>V11Catalog.FindNode(id)==null).ToList();}}
        public bool ActiveUsesV11 {get{return ActiveMode.v11Departure!=null;}}
        public PartyCombatMode ActivePartyCombatMode=>ActiveMode?.v11Departure?.preparation?.partyCombatMode??PartyCombatMode.PersistentTrio;
        public ProfileResult EnsureV11Progress(string commandId)
        {return Execute(new ProfileCommand{id=commandId,kind=ProfileCommandKind.EnsureV11Progress});}
        public ProfileResult UnlockV11Node(string characterId,string nodeId,string commandId)
        {return Execute(new ProfileCommand{id=commandId,kind=ProfileCommandKind.UnlockV11Node,actorId=characterId,selectionId=nodeId});}
        public ProfileResult SaveV11Preparation(V11PreparationDraft draft,string commandId)
        {return Execute(new ProfileCommand{id=commandId,kind=ProfileCommandKind.SaveV11Preparation,v11Preparation=draft});}
        public V11NodeQuote QuoteV11Node(string characterId,string nodeId)
        {return QuoteV11NodeInternal(State.meta.v11,characterId,nodeId);}
        static V11NodeQuote QuoteV11NodeInternal(V11ProgressState progress,string characterId,string nodeId)
        {
            var node=V11Catalog.FindNode(nodeId);var q=new V11NodeQuote{status="Invalid",remainingPoints=progress?.points??0,cost=node?.cost??0};
            if(node==null){q.reason="v11_unknown_node:"+nodeId;return q;}
            if(node.characterId!=characterId){q.reason="v11_node_character_mismatch";return q;}
            if(progress==null){q.status="Locked";q.reason="v11_progress_not_initialized";return q;}
            if(progress.unlockedNodeIds.Contains(node.id)){q.status="Unlocked";q.reason="already_unlocked";return q;}
            q.status="Locked";
            string missing=node.prerequisiteIds.FirstOrDefault(id=>!progress.unlockedNodeIds.Contains(id));
            if(missing!=null){q.reason="v11_prerequisite_required:"+missing;return q;}
            if(progress.points<node.cost){q.reason="v11_demo_points_unavailable";return q;}
            q.status="Available";q.canUnlock=true;q.reason="ok";return q;
        }
        public V11DepartureQuote QuoteV11Departure(V11PreparationDraft draft=null)
        {
            try{Require(nextDepartureP6Rules!=null,"v11_new_departure_unavailable");Require(State.meta.v11!=null,"v11_progress_not_initialized");ValidateV11Preparation(draft??State.meta.v11.preparation,true,nextV12Rules==null?"v11-character-1":"v12-character-1");return new V11DepartureQuote{legal=true,reason="ok"};}
            catch(Exception ex){return new V11DepartureQuote{reason=ex.Message};}
        }
        static void ValidateV11Preparation(V11PreparationDraft draft,bool departure,string rulesVersion=null)
        {
            Require(draft!=null&&draft.teamIds!=null&&draft.teamIds.Count==3&&draft.teamIds.Distinct().Count()==3,"v11_three_distinct_characters_required");
            PartyCombatModePolicy.RequireImplemented(draft.partyCombatMode);
            Require(draft.finnGrowthAction==null||draft.finnGrowthAction=="v12_finn_grove"||draft.finnGrowthAction=="v14_finn_detonate","v14_growth_action_unknown");
            foreach(string id in draft.teamIds)Require(V11Catalog.CharacterExists(id),"v11_unknown_character:"+id);
            Require(draft.teamIds.Contains(draft.defaultActiveActorId),"v11_active_not_in_team");
            Require(string.IsNullOrEmpty(draft.coreId)||draft.teamIds.Contains(draft.coreId),"v11_core_not_in_team");
            if(departure){Require(!string.IsNullOrEmpty(draft.coreId),"v11_core_required");Require(rulesVersion==null?V11Catalog.SupportsCore(draft.coreId):V11Catalog.SupportsCoreForRules(draft.coreId,rulesVersion),"v11_core_pool_unavailable");}
        }
        static V11ProgressState NewV11Progress()
        {
            return new V11ProgressState{points=V11Catalog.DemoAllowance,demoAllowanceClaimed=true,
                unlockedNodeIds=V11Catalog.Characters.SelectMany(c=>V11Catalog.NodesFor(c.id)).Where(n=>n.initialUnlocked).Select(n=>n.id).ToList()};
        }
        ProfileResult ApplyV11ProfileCommand(ProfileState profile,ProfileCommand command)
        {
            if(command.kind==ProfileCommandKind.EnsureV11Progress)
            {
                if(profile.meta.v11==null)profile.meta.v11=NewV11Progress();
                // Loading/viewing an existing progress object never grants a second allowance.
                return new ProfileResult{reason="v11_progress_ready"};
            }
            var progress=profile.meta.v11;Require(progress!=null,"v11_progress_not_initialized");
            if(command.kind==ProfileCommandKind.UnlockV11Node)
            {
                var quote=QuoteV11NodeInternal(progress,command.actorId,command.selectionId);
                if(quote.status=="Unlocked")return new ProfileResult{reason="already_unlocked"};
                Require(quote.canUnlock,quote.reason);progress.points-=quote.cost;AddOnce(progress.unlockedNodeIds,command.selectionId);
                return new ProfileResult{reason="v11_node_unlocked"};
            }
            Require(command.kind==ProfileCommandKind.SaveV11Preparation,"unsupported_v11_profile_command");
            ValidateV11Preparation(command.v11Preparation,false);progress.preparation=CombatJson.Clone(command.v11Preparation);
            return new ProfileResult{reason="v11_preparation_saved"};
        }
        static void ValidateV11Progress(ProfileMeta meta)
        {
            var progress=meta.v11;if(progress==null)return;
            Require(progress.version=="v11-tree-1"&&progress.demoAllowanceClaimed&&progress.points>=0&&progress.points<=V11Catalog.DemoAllowance&&progress.unlockedNodeIds!=null&&progress.unlockedNodeIds.All(id=>!string.IsNullOrWhiteSpace(id))&&progress.unlockedNodeIds.Distinct().Count()==progress.unlockedNodeIds.Count,"v11_progress_invalid");
            // Unknown old/future node IDs are retained verbatim and exposed as issues;
            // they confer no ability. Do not clear or reinterpret the author's data.
            string issue=V11Catalog.SavedPrerequisiteIssue(progress.unlockedNodeIds);Require(issue==null,"v11_saved_prerequisite_missing:"+issue);
            ValidateV11Preparation(progress.preparation,false);
        }
        static string V11DepartureHash(V11DepartureSnapshot d)
        {return d.version=="v11-departure-1"?Hash(new{d.version,d.catalogHash,d.preparation,d.actors}):d.v14!=null?Hash(new{d.version,d.catalogHash,d.preparation,d.actors,d.v12,d.v13,d.v14}):d.v13==null?Hash(new{d.version,d.catalogHash,d.preparation,d.actors,d.v12}):Hash(new{d.version,d.catalogHash,d.preparation,d.actors,d.v12,d.v13});}
        void SnapshotV11Departure(ProfileState profile,ModeState mode)
        {
            if(mode.kind!=ProfileModeKind.Challenge||profile.meta.v11==null)return;
            Require(UsesP6(mode),"v11_requires_p6_rules");ValidateV11Preparation(profile.meta.v11.preparation,true,nextV12Rules==null?"v11-character-1":"v12-character-1");
            bool modern=nextV12Rules!=null;
            Require(nextV12Rules?.build==null||nextV12Dungeon!=null,"v12_build_requires_six_room_reward_route");
            var progress=profile.meta.v11;var snapshot=new V11DepartureSnapshot{preparation=CombatJson.Clone(progress.preparation),catalogHash=modern?V11Catalog.Hash:V11Catalog.LegacyHash,
                version=modern?"v12-departure-1":"v11-departure-1",v12=CombatJson.Clone(nextV12Rules)};
            if(nextV14)
            {
                Require(modern,"v14_requires_current_character_rules");
                snapshot.v14=new V14Rules{partyCombatMode=snapshot.preparation.partyCombatMode,finnGrowthAction=snapshot.preparation.finnGrowthAction??"v14_finn_detonate"};
                snapshot.v13=new V13Rules{finnTree=snapshot.preparation.finnTree??"growth"};
                snapshot.preparation.defaultActiveActorId=snapshot.preparation.coreId;
                snapshot.v12.seedCooldown=4;
            }
            else if(nextV13||snapshot.preparation.partyCombatMode==PartyCombatMode.CoreAndAssist){snapshot.preparation.partyCombatMode=PartyCombatMode.CoreAndAssist;snapshot.preparation.defaultActiveActorId=snapshot.preparation.coreId;snapshot.v13=new V13Rules{finnTree=snapshot.preparation.finnTree??"growth"};}
            foreach(string id in snapshot.preparation.teamIds)
            {
                var character=V11Catalog.FindCharacterForRules(id,modern?"v12-character-1":"v11-character-1");var nodes=V11Catalog.NodesFor(id);
                snapshot.actors.Add(new V11ActorLoadout{actorId=id,passiveId=character.passiveId,finnSustain=CombatJson.Clone(character.finnSustain),passiveUnlocked=nodes.Any(n=>n.kind=="Passive"&&progress.unlockedNodeIds.Contains(n.id)),
                    // V12 exploration has a usable basic and key action without a metagame grind.
                    // This run-only grant does not purchase nodes or debit/refund permanent points.
                    unlockedActionIds=modern&&character.category=="Main"?new List<string>(character.actionIds):nodes.Where(n=>n.kind=="Active"&&progress.unlockedNodeIds.Contains(n.id)).Select(n=>character.actionIds[n.id.EndsWith("/active1",StringComparison.Ordinal)?0:1]).ToList()});
            }
            if(snapshot.v13!=null){if(progress.unlockedNodeIds.Contains("v13/finn/growth/upgrade1")&&snapshot.v13.finnTree=="growth")snapshot.v12.parasiteSeconds=7;if(progress.unlockedNodeIds.Contains("v13/finn/growth/upgrade2")&&snapshot.v13.finnTree=="growth")snapshot.v12.groveRadius=2.8f;if(progress.unlockedNodeIds.Contains("v13/finn/guard/upgrade1"))snapshot.v13.shieldFraction=.08f;if(progress.unlockedNodeIds.Contains("v13/finn/guard/upgrade2"))snapshot.v13.prayerFraction=.20f;}
            if(snapshot.v13!=null)foreach(var a in snapshot.actors){if(a.actorId==CombatSession.V12Evan)a.unlockedActionIds=new List<string>{"v12_evan_mark","v13_evan_correct"};if(a.actorId==CombatSession.V12Finn&&snapshot.v13.finnTree=="guard")a.unlockedActionIds=new List<string>{"v13_finn_shield","v13_finn_prayer"};}
            if(snapshot.v14!=null&&snapshot.v13.finnTree=="growth")foreach(var a in snapshot.actors.Where(a=>a.actorId==CombatSession.V12Finn))a.unlockedActionIds=new List<string>{"v12_finn_seed",snapshot.v14.finnGrowthAction};
            snapshot.hash=V11DepartureHash(snapshot);mode.v11Departure=snapshot;mode.v11Build=new V11BuildState();
        }
        PreparationSnapshot MakeV11PreparationSnapshot(ModeState mode,string law)
        {
            var d=mode.v11Departure;var prepared=new PartyPreparation{defaultActiveActorId=d.preparation.defaultActiveActorId,primaryLawId=law,actors=new List<ActorPreparation>()};
            var snapshot=new PreparationSnapshot{rulesVersion="v11-preparation-1",preparation=prepared};
            foreach(var loadout in d.actors)
            {
                var definition=V11Catalog.FindCharacter(loadout.actorId);
                prepared.actors.Add(new ActorPreparation{actorId=definition.id,specializationId="v11-suite",weaponId=definition.weaponId,preparedActionIds=new List<string>(loadout.unlockedActionIds)});
                snapshot.actorOverrides.Add(new ActorRuleOverride{actorId=definition.id,specializationId="v11-suite"});
            }
            snapshot.hash=PreparationHash(snapshot);return snapshot;
        }
        static V11CombatRules V11RulesFor(ModeState mode)
        {
            if(mode.v11Departure==null)return null;
            return new V11CombatRules{version=mode.v11Departure.version=="v12-departure-1"?"v12-character-1":"v11-character-1",v12BuildVersion=mode.v11Departure.v12?.build?.version,v13FinnTree=mode.v11Departure.v13?.finnTree,v14BuildVersion=mode.v11Departure.v14!=null?"v14-core-builds-1":null,v14FinnGrowthAction=mode.v11Departure.v14?.finnGrowthAction,coreActorId=mode.v11Departure.preparation.coreId,actors=CombatJson.Clone(mode.v11Departure.actors),buffs=CombatJson.Clone(mode.v11Build.buffs)};
        }
        static void ValidateV11Departure(ModeState mode)
        {
            var d=mode.v11Departure;if(d==null){Require(mode.v11Build==null,"v11_build_without_departure");return;}
            bool modern=d.version=="v12-departure-1";
            Require(UsesP6(mode)&&(modern||d.version=="v11-departure-1")&&(modern?d.v12!=null:d.v12==null)&&!string.IsNullOrEmpty(d.catalogHash)&&d.hash==V11DepartureHash(d),"v11_departure_changed");
            ValidateV11Preparation(d.preparation,true);
            Require(d.v14!=null?d.v13!=null:(d.preparation.partyCombatMode==PartyCombatMode.CoreAndAssist)==(d.v13!=null),"v13_adapter_missing");
            if(d.v14!=null)Require(d.v14.version=="v14-dual-builds-1"&&d.v14.partyCombatMode==d.preparation.partyCombatMode&&(d.v14.finnGrowthAction=="v12_finn_grove"||d.v14.finnGrowthAction=="v14_finn_detonate")&&d.v14.playerDamageScale==1.10&&d.v14.finnPeriodicScale==2,"v14_snapshot_invalid");
            if(d.v13!=null)Require(d.v13.version=="v13-core-assist-1"&&(d.v13.finnTree=="growth"||d.v13.finnTree=="guard")&&d.preparation.defaultActiveActorId==d.preparation.coreId,"v13_snapshot_invalid");
            Require(V11Catalog.SupportsCoreForRules(d.preparation.coreId,modern?"v12-character-1":"v11-character-1"),"v11_core_pool_unavailable");
            Require(d.actors!=null&&d.actors.Count==3&&d.actors.Select(a=>a.actorId).SequenceEqual(d.preparation.teamIds),"v11_departure_actor_ids");
            foreach(var a in d.actors)
            {var definition=V11Catalog.FindCharacterForRules(a.actorId,modern?"v12-character-1":"v11-character-1");Require(definition!=null&&a.passiveId==definition.passiveId&&a.unlockedActionIds!=null&&a.unlockedActionIds.Count>=1&&a.unlockedActionIds.Count<=2&&a.unlockedActionIds.Distinct().Count()==a.unlockedActionIds.Count&&a.unlockedActionIds.All(id=>definition.actionIds.Contains(id)||d.v13!=null&&CombatSession.V13OwnedAction(a.actorId,id,d.v13.finnTree)||d.v14!=null&&a.actorId==CombatSession.V12Finn&&d.v13.finnTree=="growth"&&id=="v14_finn_detonate"),"v11_departure_loadout_invalid:"+a.actorId);}
            Require(mode.v11Build!=null&&mode.v11Build.buffs!=null&&mode.v11Build.buffs.Select(b=>b.id).Distinct().Count()==mode.v11Build.buffs.Count,"v11_build_invalid");
            var rules=V11RulesFor(mode);
            foreach(var buff in mode.v11Build.buffs)
            {var definition=V11BuffCatalog.Find(buff.id);Require(definition!=null&&V11BuffCatalog.AvailableInRules(rules,buff.id)&&buff.stacks>0&&buff.stacks<=definition.maxStacks&&definition.prerequisites.All(id=>V11BuffCatalog.Stacks(rules,id)>0)&&!definition.exclusives.Any(id=>V11BuffCatalog.Stacks(rules,id)>0),"v11_saved_buff_invalid:"+buff.id);}
            var prepared=mode.departurePreparation;
            Require(prepared.preparation.defaultActiveActorId==d.preparation.defaultActiveActorId&&prepared.preparation.actors.Select(a=>a.actorId).SequenceEqual(d.preparation.teamIds),"v11_preparation_snapshot_mismatch");
            foreach(var a in prepared.preparation.actors)
            {var loadout=d.actors.Find(x=>x.actorId==a.actorId);Require(a.specializationId=="v11-suite"&&a.weaponId==V11Catalog.FindCharacter(a.actorId).weaponId&&a.preparedActionIds.SequenceEqual(loadout.unlockedActionIds),"v11_preparation_ability_mismatch:"+a.actorId);}
        }
    }
}
