using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CombatV051.Domain;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class P47CloneTests
    {
        public static IEnumerable<Type> DtoTypes
        {
            get{return typeof(CombatState).Assembly.GetTypes().Where(t=>t.Namespace=="CombatV051.Domain"&&t.IsPublic&&t.IsClass&&t.IsDefined(typeof(SerializableAttribute),false)).OrderBy(t=>t.Name);}
        }
        static string TypeName(Type t)
        {
            if(t.IsArray)return TypeName(t.GetElementType())+"[]";
            if(t.IsGenericType)return t.GetGenericTypeDefinition().FullName.Split('`')[0]+"<"+string.Join(",",t.GetGenericArguments().Select(TypeName))+">";
            return t.FullName.Replace('+','.');
        }
        static FieldInfo[] Fields(Type t){return t.GetFields(BindingFlags.Public|BindingFlags.Instance).OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray();}
        static object Filled(Type t,int depth,string key)
        {
            if(t==typeof(string))return "seed/"+key;
            if(t==typeof(bool))return true;
            if(t.IsEnum){var values=Enum.GetValues(t);return values.GetValue(values.Length-1);}
            if(t==typeof(float))return 1.23456776f;
            if(t==typeof(double))return 1.23456789012345;
            if(t.IsValueType)return Convert.ChangeType(17,t);
            if(depth>=7)return null;
            if(t.IsArray){var a=Array.CreateInstance(t.GetElementType(),2);for(int i=0;i<2;i++)a.SetValue(Filled(t.GetElementType(),depth+1,key+i),i);return a;}
            if(t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(List<>))
            {var a=(IList)Activator.CreateInstance(t);a.Add(Filled(t.GetGenericArguments()[0],depth+1,key+"/0"));a.Add(Filled(t.GetGenericArguments()[0],depth+1,key+"/1"));return a;}
            if(t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(Dictionary<,>))
            {var a=(IDictionary)Activator.CreateInstance(t);a.Add("key",Filled(t.GetGenericArguments()[1],depth+1,key+"/value"));return a;}
            var value=Activator.CreateInstance(t);
            foreach(var field in Fields(t))field.SetValue(value,Filled(field.FieldType,depth+1,key+"/"+field.Name));
            return value;
        }
        static void Independent(object a,object b,string path)
        {
            if(a==null){Assert.That(b,Is.Null,path);return;}
            Type type=a.GetType();if(type.IsValueType||type==typeof(string))return;
            Assert.That(b,Is.Not.SameAs(a),path);
            var dictionary=a as IDictionary;
            if(dictionary!=null){foreach(var key in dictionary.Keys)Independent(dictionary[key],((IDictionary)b)[key],path+"/"+key);return;}
            var list=a as IList;
            if(list!=null){for(int i=0;i<list.Count;i++)Independent(list[i],((IList)b)[i],path+"/"+i);return;}
            foreach(var field in Fields(type))Independent(field.GetValue(a),field.GetValue(b),path+"/"+field.Name);
        }
        [TestCaseSource(nameof(DtoTypes))]
        public void EveryDtoFieldIsGeneratedAndCloneIsJsonEquivalentWithIndependentReferences(Type type)
        {
            string signature=string.Join(";",Fields(type).Select(f=>f.Name+":"+TypeName(f.FieldType)));
            Assert.That(CombatDeepClone.FieldSignature(type),Is.EqualTo(signature),"DTO changed: regenerate Tools/P47StoreBench before shipping.");
            object value=Filled(type,0,type.Name),clone;
            Assert.That(CombatDeepClone.TryClone(value,out clone),Is.True);
            Assert.That(CombatJson.Write(clone),Is.EqualTo(CombatJson.Write(value)),type.Name);
            Independent(value,clone,type.Name);
            foreach(var field in Fields(type).Where(f=>!f.FieldType.IsValueType))
            {
                field.SetValue(value,null);
                Assert.That(CombatDeepClone.TryClone(value,out clone),Is.True);
                Assert.That(field.GetValue(clone),Is.Null,type.Name+"."+field.Name);
            }
        }
        [Test] public void CloneDoesNotRunLoadCompatibilityCallbacksOrRepairInvalidLiveState()
        {
            var state=CombatState.CreatePartyP1("p47","clone",3,new CombatConfig());state.focusBudget=null;
            var copy=state.Clone();Assert.That(copy.focusBudget,Is.Null);Assert.That(state.focusBudget,Is.Null);
        }
        [Test] public void CollectionClonePreservesNullSlotsAndIndependentNestedValues()
        {
            var values=new List<RollTicket>{null,new RollTicket{rawFaces=new List<int>{3,17}}};
            var copy=CombatJson.Clone(values);Assert.That(copy[0],Is.Null);copy[1].rawFaces[0]=20;
            Assert.That(values[1].rawFaces[0],Is.EqualTo(3));
            var map=new Dictionary<string,float>(StringComparer.OrdinalIgnoreCase){{"Timer",1.25f}};
            var mapped=CombatDeepClone.Copy(map);Assert.That(mapped.ContainsKey("timer"));mapped["TIMER"]=7;Assert.That(map["timer"],Is.EqualTo(1.25f));
        }
    }

    public sealed class P47StorePerformanceTests:ProfileTestFixture
    {
        string directory,path;
        [SetUp] public void CreateFiles()
        {
            directory=Path.Combine(Path.GetTempPath(),"CombatP47StoreTests",Guid.NewGuid().ToString("N"));path=Path.Combine(directory,"profile.json");
            profile=new ProfileSession(profile.State,profile.BaseCombatConfig,profile.Config,path);profile.Store.Save(profile.State);
        }
        [TearDown] public void Cleanup()
        {DomainTiming.Sample=null;if(Directory.Exists(directory))Directory.Delete(directory,true);}
        [Test] public void ReplacingCurrentCloneMatchesFullJsonCopyAndDoesNotAliasTheEncounterStart()
        {
            Travel("R1");Activate();CState.Actor("fighter").hp-=2;
            var expected=CombatJson.Read<ProfileState>(CombatJson.Write(profile.State));
            expected.ActiveMode.encounters.Single(e=>e.id==expected.ActiveMode.activeEncounterId).currentCombat=CombatJson.Read<CombatState>(CombatJson.Write(CState));
            var actual=CombatDeepClone.CopyReplacingCurrent(profile.State,CState);
            Assert.That(CombatJson.Write(actual),Is.EqualTo(CombatJson.Write(expected)));
            var record=actual.ActiveMode.encounters.Single(e=>e.id==actual.ActiveMode.activeEncounterId);
            record.currentCombat.Actor("fighter").hp=1;
            Assert.That(record.encounterStartSnapshot.Actor("fighter").hp,Is.EqualTo(30));
            Assert.That(CState.Actor("fighter").hp,Is.Not.EqualTo(1));
        }
        [Test] public void ReplacingExplorationDoesNotModifyInactiveChallengeOrAnyMutableBranch()
        {
            First();StartChallenge();Run(ProfileCommandKind.ContinueStory);CState.actors[0].x+=.25f;
            var actual=CombatDeepClone.CopyReplacingCurrent(profile.State,CState);
            Assert.That(CombatJson.Write(actual.challenge),Is.EqualTo(CombatJson.Write(profile.State.challenge)));
            actual.challenge.rooms[0].enemySpawns.Clear();actual.meta.lastPreparation.actors[0].specializationId="changed";
            Assert.That(profile.State.meta.lastPreparation.actors[0].specializationId,Is.Not.EqualTo("changed"));
            Assert.That(actual.story.explorationCombat,Is.Not.SameAs(CState));
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)]
        [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void CachedAuthorityPaidRollFailureKeepsDiskInnerOuterAndRetryDrawIdentical(CombatWriteStage stage)
        {
            Travel("R1");Activate();Core(CommandKind.EnterFocus);
            var enemy=CState.enemies[0];enemy.x=CState.actors[0].x+1;enemy.z=CState.actors[0].z;
            Assert.That(profile.SaveCurrent("before-roll").success);
            string outer=CombatJson.Write(profile.State),inner=CombatJson.Write(CState),disk=File.ReadAllText(path);
            var command=new CombatCommand{id="paid-once",kind=CommandKind.ConfirmRoll,actorId="fighter",targetId=enemy.id,actionId="attack"};
            var oracle=new ProfileSession(CombatJson.Read<ProfileState>(outer),profile.BaseCombatConfig,profile.Config);var expected=oracle.ExecuteCombat(command);Assert.That(expected.success,Is.True,expected.reason);
            profile.Store.WriteFault=s=>{if(s==stage)throw new IOException("p47-fault");};
            var failed=profile.ExecuteCombat(command);Assert.That(failed.success,Is.False);
            Assert.That(CombatJson.Write(profile.State),Is.EqualTo(outer));Assert.That(CombatJson.Write(CState),Is.EqualTo(inner));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
            profile.Store.WriteFault=null;var result=profile.ExecuteCombat(command);Assert.That(result.success,Is.True,result.reason);
            Assert.That(result.rawFaces,Is.EqualTo(expected.rawFaces));Assert.That(profile.ExecuteCombat(command).duplicate,Is.True);
            var loaded=ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config);
            Assert.That(CombatJson.Write(loaded.ActiveCombat.State),Is.EqualTo(CombatJson.Write(CState)));
        }
        [Test] public void ExactImageCacheStillRejectsRechecksummedExternalMutationAtTheSameRevision()
        {
            string old=File.ReadAllText(path);var envelope=CombatJson.Read<ProfileEnvelope>(old);var state=CombatJson.Read<ProfileState>(envelope.payload);
            state.meta.research++;envelope.payload=CombatJson.Write(state);envelope.integrity=CombatStore.Digest(envelope.payload);string changed=CombatJson.Write(envelope);File.WriteAllText(path,changed);
            var result=profile.SaveCurrent("stale-cache");Assert.That(result.success,Is.False);Assert.That(result.reason,Does.Contain("stale_profile_authority"));Assert.That(File.ReadAllText(path),Is.EqualTo(changed));
        }
        [Test] public void ExactImageCacheDoesNotSkipValidationOfAnInvalidNewCandidate()
        {
            Travel("R1");Activate();string disk=File.ReadAllText(path);CState.charges=CState.chargeCap;CState.momentum=100;
            int writes=0;profile.Store.WriteFault=s=>writes++;Assert.That(profile.SaveCurrent("invalid").success,Is.False);Assert.That(writes,Is.Zero);Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
        [Test] public void TimingCallbackFailureCannotChangeDurableCommandOutcome()
        {
            DomainTiming.Sample=(name,ms)=>{throw new Exception("observer failure");};
            var result=profile.SaveCurrent("observed");Assert.That(result.success,Is.True,result.reason);DomainTiming.Sample=null;
            Assert.That(ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config).State.revision,Is.EqualTo(profile.State.revision));
        }
        [TestCase(CombatWriteStage.AfterFlush,"same-length")]
        [TestCase(CombatWriteStage.AfterFlush,"truncate")]
        [TestCase(CombatWriteStage.AfterFlush,"append")]
        [TestCase(CombatWriteStage.AfterFlush,"bom")]
        [TestCase(CombatWriteStage.AfterReplace,"same-length")]
        [TestCase(CombatWriteStage.AfterReplace,"truncate")]
        [TestCase(CombatWriteStage.AfterReplace,"append")]
        [TestCase(CombatWriteStage.AfterReplace,"bom")]
        public void BothByteReadbacksRejectCorruptionAndKeepPaidRollAtomic(CombatWriteStage stage,string corruption)
        {
            Travel("R1");Activate();Core(CommandKind.EnterFocus);
            var enemy=CState.enemies[0];enemy.x=CState.actors[0].x+1;enemy.z=CState.actors[0].z;
            Assert.That(profile.SaveCurrent("before-byte-fault").success);
            string outer=CombatJson.Write(profile.State),inner=CombatJson.Write(CState);byte[] disk=File.ReadAllBytes(path);
            var command=new CombatCommand{id="byte-paid-once",kind=CommandKind.ConfirmRoll,actorId="fighter",targetId=enemy.id,actionId="attack"};
            var oracle=new ProfileSession(CombatJson.Read<ProfileState>(outer),profile.BaseCombatConfig,profile.Config);
            var expected=oracle.ExecuteCombat(command);Assert.That(expected.success,Is.True,expected.reason);
            bool injected=false;
            profile.Store.WriteFault=s=>
            {
                if(s!=stage)return;
                injected=true;string target=stage==CombatWriteStage.AfterFlush?profile.Store.TemporaryPath:path;
                byte[] bytes=File.ReadAllBytes(target);
                if(corruption=="same-length")bytes[bytes.Length/2]^=1;
                else if(corruption=="truncate")Array.Resize(ref bytes,bytes.Length-1);
                else if(corruption=="append"){Array.Resize(ref bytes,bytes.Length+1);bytes[bytes.Length-1]=32;}
                else{var withBom=new byte[bytes.Length+3];withBom[0]=239;withBom[1]=187;withBom[2]=191;Buffer.BlockCopy(bytes,0,withBom,3,bytes.Length);bytes=withBom;}
                File.WriteAllBytes(target,bytes);
            };
            var failed=profile.ExecuteCombat(command);
            Assert.That(injected,Is.True);Assert.That(failed.success,Is.False);Assert.That(failed.reason,Does.Contain("bytes_changed"));
            Assert.That(CombatJson.Write(profile.State),Is.EqualTo(outer));Assert.That(CombatJson.Write(CState),Is.EqualTo(inner));Assert.That(File.ReadAllBytes(path),Is.EqualTo(disk));
            profile.Store.WriteFault=null;
            var retried=profile.ExecuteCombat(command);Assert.That(retried.success,Is.True,retried.reason);Assert.That(retried.rawFaces,Is.EqualTo(expected.rawFaces));
            Assert.That(profile.ExecuteCombat(command).duplicate,Is.True);
            var loaded=ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config);
            Assert.That(CombatJson.Write(loaded.ActiveCombat.State),Is.EqualTo(CombatJson.Write(CState)));
        }
        [Test] public void UnicodeImagesAcrossBufferBoundariesAndChangingLengthsRoundTripExactly()
        {
            string unit="\u4e2d\u6587\ud83c\udfb2/\"\\\n";
            var values=new[]{string.Concat(Enumerable.Repeat(unit,4096)),unit,string.Concat(Enumerable.Repeat(unit,8193))};
            for(int i=0;i<values.Length;i++)
            {
                CState.world.strings["p47/unicode"]=values[i];
                var result=profile.SaveCurrent("unicode-"+i);Assert.That(result.success,Is.True,result.reason);
                byte[] bytes=File.ReadAllBytes(path);Assert.That(bytes[0],Is.EqualTo((byte)'{'),"UTF8 file has no BOM.");
                Assert.That(bytes,Is.EqualTo(new UTF8Encoding(false).GetBytes(File.ReadAllText(path,Encoding.UTF8))));
                Assert.That(ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config).ActiveCombat.State.world.strings["p47/unicode"],Is.EqualTo(values[i]));
            }
        }
        [Test] public void DurableWriteReportsEachChildProbeOnceInProtocolOrder()
        {
            var samples=new List<string>();DomainTiming.Sample=(name,ms)=>{Assert.That(ms,Is.GreaterThanOrEqualTo(0));samples.Add(name);};
            var result=profile.SaveCurrent("probe-order");DomainTiming.Sample=null;Assert.That(result.success,Is.True,result.reason);
            var expected=new[]{"Store.UTF8Encode","Store.TempWrite","Store.DurableFlush","Store.TempReadback","Store.AtomicReplace","Store.MainReadback"};
            Assert.That(samples.Where(expected.Contains).ToArray(),Is.EqualTo(expected));
            Assert.That(samples,Does.Contain("Store.WriteFlush"));Assert.That(samples,Does.Contain("Store.ReplaceVerify"));
        }
    }
}
