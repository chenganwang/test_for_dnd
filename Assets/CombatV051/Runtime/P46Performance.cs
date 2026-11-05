using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace CombatV051.Runtime
{
    // Opt-in measurement only; never changes game clocks, resources or RNG.
    [DefaultExecutionOrder(31000)]
    public sealed class P46Performance : MonoBehaviour
    {
        public static bool Active;
        const int ScopeCount=27;
        static readonly double[] milliseconds = new double[ScopeCount];
        static readonly long[] bytes = new long[ScopeCount];
        static readonly int[] calls = new int[ScopeCount];
        static readonly string[] names = {"HUD", "World", "Views", "Preview", "EnemyAI", "PartyAI", "Orders", "Path", "Presentation", "Audio", "Feedback", "Command", "Domain.Clone", "Domain.Apply", "Domain.Validate", "Profile.Capture", "Profile.Validate", "Store.ReadCAS", "Store.Encode", "Store.WriteFlush", "Store.ReplaceVerify", "Store.UTF8Encode", "Store.TempWrite", "Store.DurableFlush", "Store.TempReadback", "Store.AtomicReplace", "Store.MainReadback"};
        public struct Scope : IDisposable
        {
            int index; long ticks, alloc;
            public Scope(int i) {index=i; ticks=System.Diagnostics.Stopwatch.GetTimestamp(); alloc=GC.GetAllocatedBytesForCurrentThread();}
            public void Dispose() { if(ticks==0)return; milliseconds[index]+=(System.Diagnostics.Stopwatch.GetTimestamp()-ticks)*1000.0/System.Diagnostics.Stopwatch.Frequency; bytes[index]+=GC.GetAllocatedBytesForCurrentThread()-alloc; calls[index]++; }
        }
        public static Scope Measure(int index) => Active ? new Scope(index) : default;
        sealed class Record { public string name, unit; public ProfilerRecorder recorder; }
        readonly List<Record> recorders=new List<Record>();
        readonly List<string> unavailable=new List<string>();
        readonly List<object> aggregates=new List<object>();
        sealed class Bucket
        {
            public int consecutiveOver20,maxConsecutiveOver20;
            public readonly List<float> frames=new List<float>(10000);
            public readonly double[] ms=new double[ScopeCount], gc=new double[ScopeCount], count=new double[ScopeCount];
            public readonly Dictionary<string,List<long>> native=new Dictionary<string,List<long>>();
        }
        readonly Dictionary<string,Bucket> buckets=new Dictionary<string,Bucket>();
        readonly Dictionary<string,List<float>> roomFrames=new Dictionary<string,List<float>>();
        struct ActualFrame
        {
            public int frame,width,height; public float realSeconds,wallMs;
            public string previousMode,mode,room;
            public bool transition;
            public double commandMs; public int commands,storeCalls;
            public double? cpuFrameMs,gpuFrameMs;
            public string v13Fate,v13Boss,v13Terrain;
            public int visibleAssists;
            public string v14CombatMode;
            public int activeParasites;
            public bool detonationCasting;
        }
        readonly FrameTiming[] frameTimings=new FrameTiming[1];
        readonly List<ActualFrame> actualFrames=new List<ActualFrame>(65536);
        readonly HashSet<string> firstActionKeys=new HashSet<string>();
        readonly List<object> firstActions=new List<object>();
        readonly List<double>[] operationSamples=new List<double>[ScopeCount];
        readonly List<object> slowFrames=new List<object>(512);
        readonly List<object> profileSaves=new List<object>(2048);
        int lastProfileSaveFrame=-1,profileSavesInFrame;
        BattleWorld world; string path; float start, limit=125; string lastMode; bool written,lastFrameStable; long lastClock;
        public void Begin(BattleWorld owner,string output)
        {
            world=owner; path=Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path));
            start=Time.realtimeSinceStartup; Active=true;
            world.CommandPublished+=ObserveFirstAction;
            for(int i=0;i<ScopeCount;i++)operationSamples[i]=new List<double>(2048);
            DomainTiming.Sample=(name,ms)=>{int i=Array.IndexOf(names,name);if(i>=0){milliseconds[i]+=ms;calls[i]++;operationSamples[i].Add(ms);}};
            DomainTiming.ProfileSave=sample=>
            {
                int frame=Time.frameCount;if(frame!=lastProfileSaveFrame){lastProfileSaveFrame=frame;profileSavesInFrame=0;}
                profileSaves.Add(new
                {
                    sequence=profileSaves.Count+1,frame,saveInFrame=++profileSavesInFrame,realSeconds=Time.realtimeSinceStartup-start,
                    stage="encoded_attempt_not_commit_outcome",sample.profileId,sample.profileRevision,sample.modeInstanceId,
                    sample.roomId,sample.roomTemplateId,sample.encounterId,sample.combatMode,sample.combatRevision,
                    sample.payloadBytes,sample.encodedBytes,sample.profileReceipts,sample.currentCombatReceipts,
                    sample.currentCombatIncomingEvents,sample.currentCombatPendingThreatEvents,sample.currentCombatReactionWindows,
                    sample.currentCombatFocusRewardEvents,sample.currentCombatFocusRewardRoots,sample.currentCombatFocusRecoveryScopes
                });
            };
            var args=Environment.GetCommandLineArgs(); int ix=Array.IndexOf(args,"-p46-seconds");
            if(ix>=0&&ix+1<args.Length)float.TryParse(args[ix+1],out limit);
            var available=new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(available);
            foreach(string wanted in new[]{"Main Thread","Render Thread","GC Allocated In Frame","GC.Alloc","Canvas.BuildBatch","Canvas.SendWillRenderCanvases","UI.Layout","UI.Render","BehaviourUpdate","Instantiate","Destroy","RenderPipelineManager.DoRenderLoop_Internal","Shader.CreateGPUProgram","Shader.Parse","Shader.Warmup","Gfx.WaitForPresentOnGfxThread"})
            {
                bool found=false;
                foreach(var h in available)
                {
                    var d=ProfilerRecorderHandle.GetDescription(h); if(d.Name!=wanted)continue;
                    var recorder=ProfilerRecorder.StartNew(d.Category,d.Name,1);
                    if(recorder.Valid){recorders.Add(new Record{name=wanted,unit=d.UnitType.ToString(),recorder=recorder}); found=true;} break;
                }
                if(!found)unavailable.Add(wanted);
            }
        }
        void LateUpdate()
        {
            if(!Active||world==null)return;
            long clock=System.Diagnostics.Stopwatch.GetTimestamp(); float wallMs=lastClock==0?0:(float)((clock-lastClock)*1000.0/System.Diagnostics.Stopwatch.Frequency); lastClock=clock;
            string mode=world.Menu?"Menu":world.DungeonModal?"Dialog":world.State.inCombat?world.State.mode.ToString():"Exploration";
            FrameTimingManager.CaptureFrameTimings();uint timingCount=FrameTimingManager.GetLatestTimings(1,frameTimings);
            var v13=world.State.v13;
            if(wallMs>0)actualFrames.Add(new ActualFrame{frame=Time.frameCount,width=Screen.width,height=Screen.height,realSeconds=Time.realtimeSinceStartup-start,wallMs=wallMs,
                previousMode=lastMode,mode=mode,room=world.Profile?.ActiveRoom?.templateId,transition=mode!=lastMode,
                commandMs=milliseconds[11],commands=calls[11],storeCalls=calls[18],
                cpuFrameMs=timingCount>0&&frameTimings[0].cpuFrameTime>0?(double?)frameTimings[0].cpuFrameTime:null,
                gpuFrameMs=timingCount>0&&frameTimings[0].gpuFrameTime>0?(double?)frameTimings[0].gpuFrameTime:null,
                v13Fate=v13?.fate?.stage,v13Boss=v13?.boss==null?null:v13.boss.stage+"/"+v13.boss.move,
                v13Terrain=v13==null?null:string.Join(",",v13.terrain.Select(t=>t.kind+(t.warning>0?"-warning":"-active"))),
                visibleAssists=v13==null||!world.V13AssistMode?0:v13.members.Count(m=>m.actorId!=v13.coreId&&m.appearanceUntil>world.State.simulationSeconds),
                v14CombatMode=world.State.v14?.partyCombatMode.ToString(),
                activeParasites=world.State.enemies.Sum(e=>e.statuses.Count(x=>x.v14Dot!=null&&!x.v14Dot.detonated&&x.remaining>0)),
                detonationCasting=world.State.actors.Any(a=>CombatSession.V12ActionActive(a)&&a.v12.intent.actionId=="v14_finn_detonate")});
            if(mode!=lastMode&&lastMode!=null&&buckets.TryGetValue(lastMode,out var previousBucket))previousBucket.consecutiveOver20=0;
            // Skip warm-up and transition frames. Capture/export I/O remains in total frame times.
            if(Time.realtimeSinceStartup-start>8 && mode==lastMode)
            {
                if(!buckets.TryGetValue(mode,out var b)){b=new Bucket();buckets[mode]=b;}
                b.frames.Add(wallMs);
                if(!world.Training&&world.Profile?.ActiveRoom!=null)
                {
                    string roomKey=world.Profile.ActiveRoom.templateId+"/"+mode;
                    if(!roomFrames.TryGetValue(roomKey,out var samples)){samples=new List<float>(2048);roomFrames[roomKey]=samples;}
                    samples.Add(wallMs);
                }
                if(wallMs>20)slowFrames.Add(new {frame=Time.frameCount,mode,wallMs,commandMs=milliseconds[11],commands=calls[11],storeCalls=calls[18],encodeMs=milliseconds[18],flushMs=milliseconds[23],tempReadMs=milliseconds[24],replaceMs=milliseconds[25],mainReadMs=milliseconds[26]});
                b.consecutiveOver20=wallMs>20?b.consecutiveOver20+1:0;
                b.maxConsecutiveOver20=Math.Max(b.maxConsecutiveOver20,b.consecutiveOver20);
                for(int i=0;i<names.Length;i++){b.ms[i]+=milliseconds[i];b.gc[i]+=bytes[i];b.count[i]+=calls[i];}
                // Recorder LastValue belongs to a completed frame. Also reject the first
                // stable frame after a transition so the prior paid command is not
                // misleadingly attributed to steady Roll Freeze / Focus rendering.
                if(lastFrameStable) foreach(var r in recorders){if(!b.native.TryGetValue(r.name,out var list)){list=new List<long>(10000);b.native[r.name]=list;}list.Add(r.recorder.LastValue);}
            }
            Array.Clear(milliseconds,0,names.Length);Array.Clear(bytes,0,names.Length);Array.Clear(calls,0,names.Length);lastFrameStable=mode==lastMode;lastMode=mode;
            if(Time.realtimeSinceStartup-start>=limit){Write();Application.Quit(0);}
        }
        void Write()
        {
            if(written)return;written=true;
            foreach(var pair in buckets)
            {
                var b=pair.Value;var sorted=b.frames.OrderBy(x=>x).ToArray();int n=sorted.Length;if(n==0)continue;
                var native=b.native.ToDictionary(p=>p.Key,p=>new{samples=p.Value.Count,mean=p.Value.Average(x=>(double)x),max=p.Value.Max(),unit=recorders.First(r=>r.name==p.Key).unit});
                aggregates.Add(new{mode=pair.Key,frames=n,seconds=b.frames.Sum()/1000,meanFps=1000/b.frames.Average(),onePercentLowFps=1000/sorted.Skip(n-Math.Max(1,(int)Math.Ceiling(n*.01))).Average(),percentAtLeast60=100.0*b.frames.Count(x=>x<=16.667f)/n,p50ms=sorted[n/2],p95ms=sorted[Math.Min(n-1,(int)(n*.95))],p99ms=sorted[Math.Min(n-1,(int)(n*.99))],worstMs=sorted[n-1],over20ms=b.frames.Count(x=>x>20),maxConsecutiveOver20=b.maxConsecutiveOver20,over33ms=b.frames.Count(x=>x>33.33f),scopes=names.Select((name,i)=>new{name,meanMs=b.ms[i]/n,bytesPerFrame=(double?)null,callsPerFrame=b.count[i]/n}).ToArray(),native,frameMilliseconds=b.frames});
            }
            var transactions=names.Select((name,i)=>new{name,samples=operationSamples[i]}).Where(x=>x.samples.Count>0).Select(x=>{var a=x.samples.OrderBy(v=>v).ToArray();return new{name=x.name,calls=a.Length,meanMs=a.Average(),p95ms=a[Math.Min(a.Length-1,(int)(a.Length*.95))],p99ms=a[Math.Min(a.Length-1,(int)(a.Length*.99))],maxMs=a[a.Length-1]};}).ToArray();
            var roomAggregates=roomFrames.Select(pair=>{var a=pair.Value.OrderBy(v=>v).ToArray();return new{roomAndMode=pair.Key,frames=a.Length,meanMs=a.Average(),p95ms=a[Math.Min(a.Length-1,(int)(a.Length*.95))],p99ms=a[Math.Min(a.Length-1,(int)(a.Length*.99))],worstMs=a[a.Length-1],over33ms=a.Count(v=>v>33.3333f),over50ms=a.Count(v=>v>50),frameMilliseconds=pair.Value};}).ToArray();
            var actualGroups=actualFrames.Where(f=>f.realSeconds>8).GroupBy(f=>(f.room??"NoRoom")+"/"+f.width+"x"+f.height).Select(group=>
            {
                var sorted=group.Select(f=>f.wallMs).OrderBy(v=>v).ToArray();
                return new{room=group.Key,frames=sorted.Length,meanMs=sorted.Average(),p95ms=sorted[Math.Min(sorted.Length-1,(int)(sorted.Length*.95))],p99ms=sorted[Math.Min(sorted.Length-1,(int)(sorted.Length*.99))],worstMs=sorted[sorted.Length-1],over33ms=sorted.Count(v=>v>33.3333f),over50ms=sorted.Count(v=>v>50),transitionFrames=group.Count(f=>f.transition)};
            }).ToArray();
            File.WriteAllText(path+".all-frames.json",CombatJson.Write(new{classification="ACTUAL_PLAYER_ALL_FRAMES_INCLUDING_MODE_TRANSITIONS",engine=Application.unityVersion,width=Screen.width,height=Screen.height,
                scope="Every sampled wall frame, including Q/Commit/first-cast/room transitions. Aggregates exclude only first 8 real seconds; raw samples retain startup. Instrumented automated input, not human playtest. Compare screenshot-free runs separately.",actualGroups,firstActions,actualFrames}));
            File.WriteAllText(path+".rooms.json",CombatJson.Write(new{classification="ACTUAL_PLAYER_ROOM_FRAME_SAMPLES",engine=Application.unityVersion,width=Screen.width,height=Screen.height,roomAggregates,firstActions,
                scope="Same frame-clock samples as primary report; excludes its startup/mode-transition windows. firstActions are successful committed-command markers, not predicted damage; correlate their frame with the primary slow-frame trace. Other processes/capture/export can affect results."}));
            File.WriteAllText(path,CombatJson.Write(new{classification="ACTUAL_PLAYER_INSTRUMENTED_PERFORMANCE_NOT_HUMAN_PLAYTEST",utc=DateTime.UtcNow.ToString("o"),engine=Application.unityVersion,development=Debug.isDebugBuild,width=Screen.width,height=Screen.height,gpu=SystemInfo.graphicsDeviceName,cpu=SystemInfo.processorType,vsync=QualitySettings.vSyncCount,targetFps=Application.targetFrameRate,elapsed=Time.realtimeSinceStartup-start,audioInitializationMs=world.Audio?.InitializationMilliseconds,preloadedAudioClipCount=world.Audio?.ClipCount,scopeNotes="Inclusive wall-clock scopes; per-thread managed allocation API returned zero in this Unity Mono and is unavailable, use native GC Allocated In Frame; nested scopes must not be summed. Profiler unavailable counters are not zero. Screenshots/atomic saves and instrumentation can affect frame times; this is actual automated scene input, not a subjective smoothness verdict. Transaction samples include startup; slow-frame traces exclude warmup and mode transitions like frame buckets. profileSaves records encoded attempts before durable write, not commit outcomes; payloadBytes/encodedBytes are UTF-8 byte counts and all counts are candidate DTO list sizes, including completed history. Join profileSaves.frame to slowFrames.frame; saveInFrame distinguishes multiple saves. Enabled metadata adds one UTF-8 byte-count scan outside the timing scopes, included in total frame time.",unavailable,aggregates,transactions,slowFrames,profileSaves}));
        }
        void OnApplicationQuit(){Write();}
        void ObserveFirstAction(CombatCommand command,CombatResult result)
        {
            if(!result.success||result.duplicate||command.kind!=CommandKind.RequestHeroAction&&command.kind!=CommandKind.StartQueued&&command.kind!=CommandKind.RequestV12Beam)return;
            string action=command.kind==CommandKind.StartQueued?world.State.queue.FirstOrDefault(q=>q.id==command.queueId)?.actionId:command.actionId;
            string key=world.State.encounterId+"/"+action;
            if(firstActionKeys.Add(key))firstActions.Add(new{frame=Time.frameCount,room=world.Profile?.ActiveRoom?.templateId,world.State.encounterId,action,realSeconds=Time.realtimeSinceStartup-start});
        }
        void OnDestroy(){if(world!=null)world.CommandPublished-=ObserveFirstAction;DomainTiming.Sample=null;DomainTiming.ProfileSave=null;Active=false;foreach(var r in recorders)r.recorder.Dispose();}
    }
}
