using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace CombatV051.Runtime
{
    /// <summary>Opt-in Player evidence; never runs in normal play. It uses the actual scene, clock and GPU.</summary>
    public sealed class BattleSmokeFlow : MonoBehaviour
    {
        BattleWorld world;
        string directory;
        readonly List<object> checks = new List<object>();
        readonly List<string> errors = new List<string>();
        readonly List<string> screenshots = new List<string>();
        public void Begin(BattleWorld owner, string evidenceDirectory)
        {
            world = owner; directory = Path.GetFullPath(evidenceDirectory);
            // This explicit test process must advance while its launch window is hidden.
            // Normal launches retain the project's foreground-only setting.
            Application.runInBackground = true;
            world.IgnoreApplicationFocus = true;
            world.Controls.Map.Disable();
            world.Hud.Canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled = false;
            Application.logMessageReceived += Log;
            StartCoroutine(Run());
        }
        void Log(string condition, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(condition + "\n" + stack); }
        IEnumerator Run()
        {
            Exception failure = null;
            var enumerator = Flow();
            while (true)
            {
                object value = null; bool next = false;
                try { next = enumerator.MoveNext(); if (next) value = enumerator.Current; }
                catch (Exception error) { failure = error; }
                if (failure != null || !next) break;
                yield return value;
            }
            if (failure != null) errors.Add(failure.ToString());
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "summary.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                passed = failure == null && errors.Count == 0,
                phase = "V051-P0", engine = Application.unityVersion,
                utc = DateTime.UtcNow.ToString("o"), checks, errors, screenshots,
                captureMode = "OFFSCREEN_RENDER_FIXTURE", normalWindowPresentationVerified = false,
                humanPlayers = 0, humanRuns = 0,
                fixture = "Script invokes real game commands in an isolated P0 save. Initial position setup is explicit. No combat victory or human playability is implied.",
                graphicsDevice = SystemInfo.graphicsDeviceName,
                width = Screen.width, height = Screen.height
            }, Newtonsoft.Json.Formatting.Indented));
            Application.Quit(failure == null && errors.Count == 0 ? 0 : 2);
        }
        IEnumerator Flow()
        {
            Require(Path.IsPathRooted(directory), "Absolute evidence directory required.");
            Require(!Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any(), "Evidence directory must be fresh.");
            Directory.CreateDirectory(directory);
            // Let the real startup/initial UI frames finish before beginning the isolated encounter.
            yield return null;
            yield return null;
            world.InitializeForTests(Path.Combine(directory, "profile.json"));
            world.Controls.Map.Disable();
            world.Hud.Canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled = false;
            world.ActiveActor.hp = world.ActiveActor.maxHp = 30;
            world.State.Actor("enemy-1").hp = world.State.Actor("enemy-1").maxHp = 36;
            yield return new WaitForSecondsRealtime(.45f);
            Require(world.State.simulationSeconds > .1f, "Realtime simulation did not advance.");
            Record("realtime_scene", "Real generated scene running with one Fighter, enemy, 3D walls and fixed orthographic camera.");
            Capture("01-realtime.png");
            var rotation = world.BattleCamera.transform.rotation;
            Require(world.BattleCamera.orthographic, "Camera must be orthographic.");
            Require(Mathf.Abs(Mathf.DeltaAngle(rotation.eulerAngles.y,45)) < .05f && Mathf.Abs(Mathf.DeltaAngle(rotation.eulerAngles.x,55)) < .05f, "Camera must start yaw45/pitch55.");
            var focus = world.Command(CommandKind.EnterFocus); Require(focus.success, focus.reason);
            var simBefore = world.State.simulationSeconds;
            yield return new WaitForSecondsRealtime(.18f);
            Require(world.Session.Scale > 0 && world.Session.Scale <= .15f && world.State.simulationSeconds > simBefore, "Focus must keep world moving slowly.");
            Require(Quaternion.Angle(rotation,world.BattleCamera.transform.rotation)<.01f, "Focus rotated camera.");
            Capture("02-focus.png"); Record("focus", "Target slow scale and fixed yaw/pitch verified on the actual running scene.");
            var charges = world.State.charges;
            var confirmed = world.Command(CommandKind.ConfirmRoll);
            Require(confirmed.success && world.State.charges == charges-1, "ConfirmRoll did not atomically cost one shared charge.");
            Require(world.State.mode == CombatMode.RollFreeze && Time.timeScale == 0, "ConfirmRoll did not immediately freeze world.");
            var actorsBefore = CombatJson.Write(world.State.actors); var enemiesBefore = CombatJson.Write(world.State.enemies);
            var worldBefore = CombatJson.Write(world.State.world); var position = world.BattleCamera.transform.position;
            var rawBefore = CombatJson.Write(world.State.pendingRoll.rawFaces); var drawCount = world.State.random.visibleTactical.drawCount;
            Capture("03-roll-freeze.png");
            yield return new WaitForSecondsRealtime(.12f);
            Require(world.State.mode == CombatMode.RollFreeze, "Animation ended before its configured duration.");
            Require(actorsBefore==CombatJson.Write(world.State.actors) && enemiesBefore==CombatJson.Write(world.State.enemies) && worldBefore==CombatJson.Write(world.State.world), "Actors/AI/cooldowns/world moved during RollFreeze.");
            Require(Vector3.Distance(position,world.BattleCamera.transform.position)<.0001f, "Camera drifted during RollFreeze.");
            Require(world.RollElapsed>0, "Unscaled dice animation did not advance.");
            Record("roll_freeze", "All serialized actors, enemy AI timers and world state stayed identical; camera stayed fixed while unscaled dice animation advanced.");
            var deadline=Time.realtimeSinceStartup+5;
            while(world.State.mode==CombatMode.RollFreeze && Time.realtimeSinceStartup<deadline) yield return null;
            Require(world.State.mode==CombatMode.PostRollDecision && world.Session.Scale>0 && world.Session.Scale<=.15f, "Result did not restore slow time.");
            Require(rawBefore==CombatJson.Write(world.State.pendingRoll.rawFaces) && world.State.random.visibleTactical.drawCount==drawCount,"Reveal drew new dice.");
            Capture("04-post-roll.png");
            var paidTicket=world.State.pendingRoll.id;
            Require(world.Session.Save().success,"Postroll file save failed.");
            var entire=CombatJson.Write(world.State);
            world.Suspend();world.ContinueSaved();
            Require(entire==CombatJson.Write(world.State),"Full snapshot was not restored exactly from the file.");
            Record("postroll_file_restore","Full State JSON matched after actual save, suspend, file load; includes result, resources, positions, AI, queue, receipts and all RNG streams.");
            var inspiration=world.ActiveActor.inspiration;
            Require(world.Command(CommandKind.Modify).success,"Inspiration +2 failed.");
            Require(world.ActiveActor.inspiration==inspiration-1,"Inspiration modifier did not cost once.");
            var superiority=world.ActiveActor.superiority;
            Require(world.Command(CommandKind.Precision).success,"Fighter Precision failed.");
            Require(world.ActiveActor.superiority==superiority-1,"Class die did not cost once.");
            var rngBeforeReroll=world.State.random.visibleTactical.drawCount;
            Require(world.Command(CommandKind.Reroll).success,"Paid reroll failed.");
            Require(world.State.mode==CombatMode.RollFreeze && world.State.random.visibleTactical.drawCount>rngBeforeReroll,"Reroll must advance RNG and enter RollFreeze.");
            deadline=Time.realtimeSinceStartup+5;
            while(world.State.mode==CombatMode.RollFreeze && Time.realtimeSinceStartup<deadline)yield return null;
            Require(world.State.mode==CombatMode.PostRollDecision,"Reroll did not return to decision.");
            var committed=world.Command(CommandKind.Commit,id:"smoke-commit-once");Require(committed.success,committed.reason);
            Require(world.State.queue.Count(q=>q.ticketId==paidTicket)==1,"Commit missing unique queue entry.");
            var duplicate=world.Command(CommandKind.Commit,ticket:paidTicket,id:"smoke-commit-once");
            Require(duplicate.success&&duplicate.duplicate&&world.State.queue.Count(q=>q.ticketId==paidTicket)==1,"Repeated Commit duplicated action.");
            Require(world.Command(CommandKind.ExitFocus).success,"Could not exit Focus to execute queue.");
            deadline=Time.realtimeSinceStartup+5;
            while(world.State.queue.Any(q=>q.stage=="Queued"||q.stage=="Executing")&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(world.State.queue.Any(q=>q.ticketId==paidTicket&&q.stage=="Resolved"),"Queued action did not execute in the live scene.");
            Capture("05-committed.png");
            Record("paid_postroll_and_commit","Modify, class d6 and reroll paid correctly; Commit idempotent; real Update loop started and resolved queued action after returning to realtime.");
            Require(errors.Count==0,"Runtime emitted errors.");
        }
        void Record(string name,string detail){checks.Add(new{name,passed=true,detail});}
        static void Require(bool condition,string text){if(!condition)throw new InvalidOperationException(text);}
        void Capture(string filename)
        {
            world.Hud.RefreshView(); // Capture the actual current UI state, even within the command's frame.
            var cam=world.BattleCamera;var hud=world.Hud.Canvas;
            var previousMode=hud.renderMode;var previousCamera=hud.worldCamera;var previousDistance=hud.planeDistance;
            var previousSort=hud.sortingOrder;var previousOverride=hud.overrideSorting;
            var active=RenderTexture.active;var oldTarget=cam.targetTexture;
            var target=RenderTexture.GetTemporary(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            Texture2D texture=null;
            try
            {
                cam.targetTexture=target;hud.renderMode=RenderMode.ScreenSpaceCamera;hud.worldCamera=cam;hud.planeDistance=1;
                hud.overrideSorting=true;hud.sortingOrder=short.MaxValue;Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=target});
                RenderTexture.active=target;texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
                texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0,false);texture.Apply(false,false);
                var pixels=texture.GetPixels32();var distinct=new HashSet<int>();var bright=0;
                for(var i=0;i<pixels.Length;i+=13){var p=pixels[i];distinct.Add(p.r*65536+p.g*256+p.b);if(p.r>20||p.g>20||p.b>20)bright++;}
                File.WriteAllBytes(Path.Combine(directory,filename),texture.EncodeToPNG());
                Require(distinct.Count>20&&bright>pixels.Length/1000,"Offscreen frame lacks real rendered detail.");
                screenshots.Add(filename);
            }
            finally
            {
                cam.targetTexture=oldTarget;RenderTexture.active=active;
                hud.renderMode=previousMode;hud.worldCamera=previousCamera;hud.planeDistance=previousDistance;hud.overrideSorting=previousOverride;hud.sortingOrder=previousSort;
                RenderTexture.ReleaseTemporary(target);if(texture!=null)Destroy(texture);
            }
        }
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
