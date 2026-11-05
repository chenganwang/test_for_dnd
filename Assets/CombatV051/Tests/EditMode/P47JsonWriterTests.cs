using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using CombatV051.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;

namespace CombatV051.Tests
{
#if !UNITY_5_3_OR_NEWER
    [NonParallelizable]
#endif
    public sealed class P47JsonContractTests
    {
        public static IEnumerable<Type> Types {get{return P47CloneTests.DtoTypes;}}
        static string Oracle(object value){return JsonConvert.SerializeObject(value,Formatting.None,CombatJson.Settings);}
        static string Name(Type t){return t.IsArray?Name(t.GetElementType())+"[]":t.IsGenericType?t.GetGenericTypeDefinition().FullName.Split('`')[0]+"<"+string.Join(",",t.GetGenericArguments().Select(Name))+">":t.FullName.Replace('+','.');}
        static string Signature(Type type)
        {
            var c=new DefaultContractResolver().ResolveContract(type) as JsonObjectContract;
            if(c==null)return "unsupported:"+new DefaultContractResolver().ResolveContract(type).GetType().FullName;
            return JsonConvert.SerializeObject(new{kind=c.GetType().FullName,converter=c.Converter==null?null:c.Converter.GetType().FullName,internalConverter=c.InternalConverter==null?null:c.InternalConverter.GetType().FullName,c.IsReference,c.MemberSerialization,onSerializing=c.OnSerializingCallbacks.Count,onSerialized=c.OnSerializedCallbacks.Count,onError=c.OnErrorCallbacks.Count,extension=c.ExtensionDataGetter!=null,c.ItemRequired,c.ItemNullValueHandling,itemConverter=c.ItemConverter==null?null:c.ItemConverter.GetType().FullName,c.ItemIsReference,c.ItemReferenceLoopHandling,c.ItemTypeNameHandling,properties=c.Properties.Select(p=>new{p.PropertyName,p.UnderlyingName,type=Name(p.PropertyType),p.Ignored,p.Readable,p.Writable,p.Order,p.Required,p.NullValueHandling,p.DefaultValueHandling,p.DefaultValue,p.ReferenceLoopHandling,p.TypeNameHandling,p.IsReference,p.ItemIsReference,p.ItemReferenceLoopHandling,p.ItemTypeNameHandling,converter=p.Converter==null?null:p.Converter.GetType().FullName,itemConverter=p.ItemConverter==null?null:p.ItemConverter.GetType().FullName,shouldSerialize=p.ShouldSerialize!=null,isSpecified=p.GetIsSpecified!=null}).ToArray()},Formatting.None);
        }
        static object Filled(Type t,int depth,string key)
        {
            if(t==typeof(string))return "中文/\ud83c\udfb2/\"\\\n\t\u0000/"+key;
            if(t==typeof(bool))return true;if(t.IsEnum){var values=Enum.GetValues(t);return values.GetValue(values.Length-1);}
            if(t==typeof(float))return 1.23456776f;if(t==typeof(double))return 1.23456789012345;
            if(t.IsValueType)return Convert.ChangeType(17,t);if(depth>=7)return null;
            if(t.IsArray){var a=Array.CreateInstance(t.GetElementType(),2);for(int i=0;i<2;i++)a.SetValue(Filled(t.GetElementType(),depth+1,key+i),i);return a;}
            if(t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(List<>)){var a=(IList)Activator.CreateInstance(t);a.Add(Filled(t.GetGenericArguments()[0],depth+1,key+"/0"));a.Add(Filled(t.GetGenericArguments()[0],depth+1,key+"/1"));return a;}
            if(t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(Dictionary<,>)){var a=(IDictionary)Activator.CreateInstance(t);a.Add("z/\"\\\n",Filled(t.GetGenericArguments()[1],depth+1,key+"/0"));a.Add("a/中文",Filled(t.GetGenericArguments()[1],depth+1,key+"/1"));return a;}
            var value=Activator.CreateInstance(t);foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.Instance))f.SetValue(value,Filled(f.FieldType,depth+1,key+"/"+f.Name));return value;
        }
        static void Exact(object value,string label)
        {
            string expected=null,actual=null;Exception expectedError=null,actualError=null;
            try{expected=Oracle(value);}catch(Exception ex){expectedError=ex;}
            try{actual=CombatJson.Write(value);}catch(Exception ex){actualError=ex;}
            if(expectedError!=null||actualError!=null)
            {Assert.That(actualError,Is.Not.Null,label);Assert.That(expectedError,Is.Not.Null,label);Assert.That(actualError.GetType(),Is.EqualTo(expectedError.GetType()),label);Assert.That(actualError.Message,Is.EqualTo(expectedError.Message),label);return;}
            Assert.That(actual,Is.EqualTo(expected),label);Assert.That(Encoding.UTF8.GetBytes(actual),Is.EqualTo(Encoding.UTF8.GetBytes(expected)),label+" UTF8");
        }
        [TestCaseSource(nameof(Types))]
        public void GeneratedContractAndAllReferenceNullCasesMatchOriginalNewtonsoftExactly(Type type)
        {
            Assert.That(CombatTypedJson.ContractSignature(type),Is.EqualTo(Signature(type)),"DTO serialization changed: run Tools/P47StoreBench generate-json.");
            var empty=Activator.CreateInstance(type);string text;Assert.That(CombatTypedJson.TryWrite(empty,CombatJson.Settings,out text),Is.True,"Current DTO default should use fast path: "+type.Name);Exact(empty,type.Name+" default");
            var full=Filled(type,0,type.Name);Exact(full,type.Name+" filled");
            foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance).Where(f=>!f.FieldType.IsValueType))
            {var old=field.GetValue(full);field.SetValue(full,null);Exact(full,type.Name+" null "+field.Name);field.SetValue(full,old);}
        }
        [Test] public void DictionaryComparerInsertionOrderAndFloatBitPatternsRemainIdentical()
        {
            var map=new Dictionary<string,float>(StringComparer.OrdinalIgnoreCase){{"Z",float.MaxValue},{"a",float.Epsilon}};map["z"]=-0f;Exact(map,"case-insensitive insertion order");
            foreach(float n in new[]{0f,-0f,float.Epsilon,float.MinValue,float.MaxValue,float.NaN,float.PositiveInfinity,float.NegativeInfinity,1f,1e-20f,1e20f})Exact(new WorldState{numbers=new Dictionary<string,float>{{"z",n},{"a",-n}}},"float "+n);
            Exact(new RngState{state=ulong.MaxValue,increment=ulong.MaxValue,drawCount=ulong.MaxValue},"ulong maximum");
        }
        [Test] public void RequiredNullFallsBackAndPreservesOriginalExceptionMessage()
        {
            var state=CombatState.Create("required","null",1);state.focusBudget=null;string text;
            Assert.That(CombatTypedJson.TryWrite(state,CombatJson.Settings,out text),Is.False);Exact(state,"required null");
        }
        [Test] public void OmittedScalarExtensionsKeepExactOracleBytesForZeroNonzeroAndExceptionalFloats()
        {
            foreach(double value in new[]{0d,-0d,double.Epsilon,.25d,-1d,double.MaxValue,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            {
                var state=new V12ActorState{buildHealingRemainder=value};string text;
                Assert.That(CombatTypedJson.TryWrite(state,CombatJson.Settings,out text),Is.True,"double fast path "+value);
                Exact(state,"ignored-default double "+value);
                if(value==0)Assert.That(text,Does.Not.Contain("buildHealingRemainder"));
            }
            foreach(float value in new[]{0f,-0f,float.Epsilon,15f,-1f,float.MaxValue,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {
                var state=new P6FocusState{capacityBonus=value};string text;
                Assert.That(CombatTypedJson.TryWrite(state,CombatJson.Settings,out text),Is.True,"float fast path "+value);
                Exact(state,"ignored-default float "+value);
                if(value==0)Assert.That(text,Does.Not.Contain("capacityBonus"));
            }
            foreach(var value in new[]{PartyCombatMode.PersistentTrio,PartyCombatMode.CoreAndAssist,(PartyCombatMode)(-1)})
            {
                var draft=new V11PreparationDraft{partyCombatMode=value};string text;
                Assert.That(CombatTypedJson.TryWrite(draft,CombatJson.Settings,out text),Is.True,"enum fast path "+value);
                Exact(draft,"ignored-default enum "+value);
                if(value==PartyCombatMode.PersistentTrio)Assert.That(text,Does.Not.Contain("partyCombatMode"));
            }
        }
        sealed class CallbackValue
        {
            public string value="before";[JsonIgnore]public int calls;
            [OnSerializing]void Serialize(StreamingContext context){calls++;value="after";}
        }
        sealed class ActorListWithCallback:List<ActorState>
        {
            [JsonIgnore]public int calls;
            [OnSerializing]void Serialize(StreamingContext context){calls++;}
        }
        sealed class ActorConverter:JsonConverter
        {
            public int writes;
            public override bool CanConvert(Type objectType){return objectType==typeof(ActorState);}
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer){writes++;writer.WriteValue("actor:"+((ActorState)value).id);}
            public override object ReadJson(JsonReader reader,Type objectType,object existingValue,JsonSerializer serializer){throw new NotSupportedException();}
        }
        [Test] public void CustomConverterUsesOriginalCodecAndRunsOnce()
        {
            var converter=new ActorConverter();CombatJson.Settings.Converters.Add(converter);
            try
            {
                var actor=new ActorState{id="custom"};string text;Assert.That(CombatTypedJson.TryWrite(actor,CombatJson.Settings,out text),Is.False);
                Assert.That(CombatJson.Write(actor),Is.EqualTo("\"actor:custom\""));Assert.That(converter.writes,Is.EqualTo(1));
            }
            finally{CombatJson.Settings.Converters.Remove(converter);}
        }
        [Test] public void AnonymousExternalTypesAndNestedCollectionCallbacksUseOriginalCodecOnce()
        {
            string text;var value=new CallbackValue();Assert.That(CombatTypedJson.TryWrite(value,CombatJson.Settings,out text),Is.False);
            Assert.That(CombatJson.Write(value),Is.EqualTo(Oracle(new CallbackValue())));Assert.That(value.calls,Is.EqualTo(1));
            var actors=new ActorListWithCallback{new ActorState{id="fighter"}};var state=CombatState.Create("callbacks","nested",1);state.actors=actors;
            Assert.That(CombatTypedJson.TryWrite(state,CombatJson.Settings,out text),Is.False);Assert.That(actors.calls,Is.Zero);
            CombatJson.Write(state);Assert.That(actors.calls,Is.EqualTo(1));Exact(new{anonymous="fallback",number=17.2},"anonymous");
        }
        [Test] public void ChangedSettingsAndGlobalDefaultsFallBackWithoutLosingBehavior()
        {
            var s=CombatJson.Settings;var value=new ActorState{id="<tag>"};string text;
            var oldNull=s.NullValueHandling;var oldDefault=s.DefaultValueHandling;var oldEscape=s.StringEscapeHandling;var oldResolver=s.ContractResolver;var oldCulture=s.Culture;var oldGlobal=JsonConvert.DefaultSettings;
            try
            {
                s.NullValueHandling=NullValueHandling.Ignore;Assert.That(CombatTypedJson.TryWrite(value,s,out text),Is.False);Exact(value,"ignore null");s.NullValueHandling=oldNull;
                s.DefaultValueHandling=DefaultValueHandling.Ignore;Assert.That(CombatTypedJson.TryWrite(value,s,out text),Is.False);Exact(value,"ignore default");s.DefaultValueHandling=oldDefault;
                s.StringEscapeHandling=StringEscapeHandling.EscapeHtml;Assert.That(CombatTypedJson.TryWrite(value,s,out text),Is.False);Exact(value,"html escape");s.StringEscapeHandling=oldEscape;
                s.ContractResolver=new DefaultContractResolver{NamingStrategy=new SnakeCaseNamingStrategy()};Assert.That(CombatTypedJson.TryWrite(value,s,out text),Is.False);Exact(value,"custom names");s.ContractResolver=oldResolver;
                s.Culture=(CultureInfo)CultureInfo.InvariantCulture.Clone();Assert.That(CombatTypedJson.TryWrite(value,s,out text),Is.False);Exact(value,"culture");s.Culture=oldCulture;
                JsonConvert.DefaultSettings=()=>new JsonSerializerSettings{StringEscapeHandling=StringEscapeHandling.EscapeHtml};Assert.That(CombatTypedJson.TryWrite(value,s,out text),Is.False);Exact(value,"global defaults");
            }
            finally{s.NullValueHandling=oldNull;s.DefaultValueHandling=oldDefault;s.StringEscapeHandling=oldEscape;s.ContractResolver=oldResolver;s.Culture=oldCulture;JsonConvert.DefaultSettings=oldGlobal;}
        }
    }
    public sealed class P47JsonPaidStoreTests:ProfileTestFixture
    {
        [Test] public void OriginalNewtonsoftPaidSaveLoadsAndDuplicateDrawRemainsIdenticalWithNewWriter()
        {
            Travel("R1");Activate();Core(CommandKind.EnterFocus);var enemy=CState.enemies[0];enemy.x=CState.actors[0].x+1;enemy.z=CState.actors[0].z;
            var command=new CombatCommand{id="oracle-paid-once",kind=CommandKind.ConfirmRoll,actorId="fighter",targetId=enemy.id,actionId="attack"};
            var paid=profile.ExecuteCombat(command);Assert.That(paid.success,Is.True,paid.reason);
            string payload=JsonConvert.SerializeObject(profile.State,Formatting.None,CombatJson.Settings);
            Assert.That(CombatJson.Write(profile.State),Is.EqualTo(payload));
            Assert.That(profile.State.configHash,Is.EqualTo(CombatStore.Digest(JsonConvert.SerializeObject(profile.Config,Formatting.None,CombatJson.Settings))));
            Assert.That(profile.State.baseCombatConfigHash,Is.EqualTo(CombatStore.Digest(JsonConvert.SerializeObject(profile.BaseCombatConfig,Formatting.None,CombatJson.Settings))));
            var envelope=new ProfileEnvelope{profileId=profile.State.profileId,profileInstanceId=profile.State.profileInstanceId,revision=profile.State.revision,payload=payload,integrity=CombatStore.Digest(payload)};
            string directory=Path.Combine(Path.GetTempPath(),"P47JsonPaid",Guid.NewGuid().ToString("N")),path=Path.Combine(directory,"profile.json");Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(path,JsonConvert.SerializeObject(envelope,Formatting.None,CombatJson.Settings),new UTF8Encoding(false));
                var loaded=ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config);int charges=loaded.ActiveCombat.State.charges;
                var retry=loaded.ExecuteCombat(command);Assert.That(retry.success,Is.True,retry.reason);Assert.That(retry.duplicate,Is.True);Assert.That(retry.rawFaces,Is.EqualTo(paid.rawFaces));Assert.That(loaded.ActiveCombat.State.charges,Is.EqualTo(charges));
                var saved=loaded.SaveCurrent("new-writer");Assert.That(saved.success,Is.True,saved.reason);
                var disk=JsonConvert.DeserializeObject<ProfileEnvelope>(File.ReadAllText(path),CombatJson.Settings);
                Assert.That(disk.payload,Is.EqualTo(JsonConvert.SerializeObject(loaded.State,Formatting.None,CombatJson.Settings)));
                Assert.That(CombatJson.Write(ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config).ActiveCombat.State),Is.EqualTo(CombatJson.Write(loaded.ActiveCombat.State)));
            }
            finally{Directory.Delete(directory,true);}
        }
    }
}
