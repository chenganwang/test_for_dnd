using System.Diagnostics;
using System.Security.Cryptography;
using CombatV051.Domain;
using Newtonsoft.Json;

if(args.Length>0&&args[0]=="export-v13") { V13ContentExporter.Run(args[1],args[2]); return; }
if(args.Length>0&&args[0]=="export-v14") { V14ContentExporter.Run(args[1],args[2]); return; }

if(args.Length>0&&args[0]=="verify-p5-legacy") { P5LegacyVerifier.Run(args[1],args[2],args.Skip(3)); return; }
if(args.Length>0&&args[0]=="generate") { CloneGenerator.Generate(args[1]); return; }
if(args.Length>0&&args[0]=="generate-json") { JsonWriterGenerator.Generate(args[1]); return; }
if(args.Length>0&&args[0]=="verify-json-file") { JsonFileVerifier.Run(args[1],args[2]); return; }
var root=Path.GetFullPath(args.Length>0?args[0]:"../..");
var output=Path.GetFullPath(args.Length>1?args[1]:Path.Combine(root,"Reports/P47_STORE_BASELINE.json"));
var files=Directory.GetFiles(Path.Combine(root,"Assets/CombatV051/Domain"),"*.cs").OrderBy(x=>x).ToArray();
var sources=files.Select(p=>new {path=Path.GetRelativePath(root,p),sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}).ToArray();
var config=CombatJson.Read<CombatConfig>(File.ReadAllText(Path.Combine(root,"Assets/CombatV051/Resources/CombatConfig.json")));
var dungeon=CombatJson.Read<DungeonConfig>(File.ReadAllText(Path.Combine(root,"Assets/CombatV051/Resources/DungeonConfig.json")));
var profile=ProfileSession.CreateNew("p47-benchmark",41,config,dungeon);
var room=dungeon.Room("R1");foreach(var actor in profile.ActiveCombat.State.actors){actor.x=room.x;actor.z=room.z;}
void Require(bool result,string message){if(!result)throw new InvalidOperationException(message);}
var enter=profile.Execute(new ProfileCommand{id="enter",kind=ProfileCommandKind.EnterRoom,roomId="R1",qualified=true});Require(enter.success,enter.reason);
var activate=profile.Execute(new ProfileCommand{id="activate",kind=ProfileCommandKind.ActivateEncounter,qualified=true});Require(activate.success,activate.reason);
// A fixed 200-receipt graph models accumulated ordinary commands. It is a benchmark fixture, not gameplay evidence.
for(int i=0;i<200;i++)profile.ActiveCombat.State.receipts.Add(new CommandReceipt{id="bench-history-"+i,fingerprint="fixture",result=new CombatResult{success=true,revision=i,rawFaces=new List<int>{12}}});
var captured=profile.SaveCurrent("capture-fixture");Require(captured.success,captured.reason);
var state=profile.State;var combat=profile.ActiveCombat.State;
string payload=CombatJson.Write(state);var encoded=CombatJson.Write(new ProfileEnvelope{profileId=state.profileId,profileInstanceId=state.profileInstanceId,revision=state.revision,payload=payload,integrity=CombatStore.Digest(payload)});
var directory=Path.Combine(Path.GetTempPath(),"ActionCrpgP47Bench",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
var store=new ProfileStore(Path.Combine(directory,"profile.json"),profile.Validate);store.Save(state);
var rows=new List<object>();object sink=null;
void Measure(string name,int iterations,Action action){for(int i=0;i<8;i++)action();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();long startBytes=GC.GetAllocatedBytesForCurrentThread();var times=new double[iterations];for(int i=0;i<iterations;i++){long t=Stopwatch.GetTimestamp();action();times[i]=(Stopwatch.GetTimestamp()-t)*1000.0/Stopwatch.Frequency;}long bytes=GC.GetAllocatedBytesForCurrentThread()-startBytes;Array.Sort(times);double At(double q)=>times[Math.Min(iterations-1,(int)Math.Ceiling(iterations*q)-1)];rows.Add(new{name,iterations,meanMs=times.Average(),p50ms=At(.5),p95ms=At(.95),p99ms=At(.99),maxMs=times[^1],allocatedBytesPerOperation=bytes/(double)iterations});}
Measure("Combat.Clone",200,()=>sink=combat.Clone());
Measure("Profile.Clone",100,()=>sink=state.Clone());
Measure("Profile.Validate",100,()=>profile.Validate(state));
Measure("Profile.Serialize",100,()=>sink=CombatJson.Write(state));
Measure("Profile.Deserialize",100,()=>sink=CombatJson.Read<ProfileState>(payload));
Measure("CAS.ReadParseValidateDigest",60,()=>{var e=CombatJson.Read<ProfileEnvelope>(File.ReadAllText(store.MainPath));var p=CombatJson.Read<ProfileState>(e.payload);profile.Validate(p);sink=CombatStore.Digest(CombatJson.Write(p));});
Measure("ProfileStore.SaveDurable",60,()=>store.Save(state));
int sequence=0;var diskProfile=ProfileSession.Load(store.MainPath,config,dungeon);
Measure("Profile.SaveCurrent.EndToEnd",40,()=>{var r=diskProfile.SaveCurrent("save-"+(++sequence));Require(r.success,r.reason);});
bool sourceChanged=sources.Any(f=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,f.path))))!=f.sha256);
var result=new{classification="DOTNET_MICROBENCHMARK_NOT_UNITY_OR_PLAYER",utc=DateTime.UtcNow.ToString("o"),runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,process=Environment.ProcessId,fixture="Actual Resources configuration; R1 activated using production profile commands; 200 explicit synthetic historical receipts",combatReceipts=combat.receipts.Count,payloadBytes=System.Text.Encoding.UTF8.GetByteCount(payload),envelopeBytes=System.Text.Encoding.UTF8.GetByteCount(encoded),durableFlush=true,iterationsAreNotGameplayFrames=true,unityStarted=false,sourceChangedDuringRun=sourceChanged,sources,results=rows};
Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonConvert.SerializeObject(result,Formatting.Indented));Console.WriteLine(JsonConvert.SerializeObject(new{output,sourceChangedDuringRun=sourceChanged,results=rows},Formatting.Indented));
GC.KeepAlive(sink);
// This directory was freshly allocated by this process and is never an author profile.
Directory.Delete(directory,true);
if(sourceChanged)Environment.ExitCode=3;
