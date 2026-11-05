using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CombatV051.Runtime
{
    /// <summary>The sole engine bridge. Domain DTOs own every gameplay timer and position.</summary>
    public sealed partial class BattleWorld : MonoBehaviour
    {
        CombatSession arenaSession;
        public CombatSession Session { get => Profile != null ? Profile.ActiveCombat : arenaSession; private set => arenaSession = value; }
        public CombatState State => Session.State;
        public BattlePresentationConfig Presentation { get; private set; }
        public FixedBattleCamera Rig { get; private set; }
        public Camera BattleCamera { get; private set; }
        public BattleHud Hud { get; private set; }
        public BattleInput Controls { get; private set; }
        public BattleFeedback Feedback { get; private set; }
        public HitFeedback ImpactFeedback {get;private set;}
        // Optional diagnostics receive detached copies after the rule transaction,
        // before encounter teardown can discard the just-completed result.
        public event Action<CombatCommand,CombatResult> CommandPublished;
        public string SelectedActor = "fighter", SelectedTarget = "enemy-1", SelectedAction = "attack";
        public string Message = "选择新灰盒，或继续上次完整战斗快照。";
        public bool Menu = true, IgnoreApplicationFocus;
        public float RollElapsed { get; private set; }
        public Vector3 WorldAimPoint { get; private set; }
        public string SavePath { get; private set; }
        public int SimulatedFrames { get; private set; }
        public bool PartyMode => State.actors.Count > 1;
        bool expectPartyProfile = true;
        public bool IsFocus => Session != null && (CombatSession.V13FateBusy(State)||State.mode == CombatMode.TacticalFocus || State.mode == CombatMode.PostRollDecision || State.mode == CombatMode.ReactionPrompt);
        public bool IsFrozen => Menu || DungeonModal || Session == null || State.v13?.fate?.stage=="Rolled" || State.mode == CombatMode.RollFreeze || State.mode == CombatMode.CombatPaused;
        public ActorState ActiveActor => State.Actor(State.activeActorId);
        readonly Dictionary<string, Transform> views = new Dictionary<string, Transform>();
        readonly Dictionary<string, AreaPreview> warnings = new Dictionary<string, AreaPreview>();
        readonly Dictionary<string, AreaPreview> statusMarkers = new Dictionary<string, AreaPreview>();
        readonly List<Renderer> walls = new List<Renderer>();
        Transform map, units;
        AreaPreview targeting;
        float saveElapsed;
        string rollingTicket;
        static readonly Color Teal = new Color(.18f, .86f, .76f), Amber = new Color(1f, .67f, .28f);
        const int WallLayer = 8;

        void Awake()
        {
            Presentation = BattlePresentationConfig.Load();
            Controls = new BattleInput();
            SavePath = Path.Combine(Application.persistentDataPath, "CombatV051", "V14-Demo", "profile-1", "profile.json");
            // Batch engine tests must never open the author's normal player profile during Awake.
            if(Application.isEditor&&Application.isBatchMode) SavePath=Path.Combine(Path.GetTempPath(),"ActionCRPG-Engine-Isolation-"+System.Diagnostics.Process.GetCurrentProcess().Id,"unused-profile.json");
            var args = Environment.GetCommandLineArgs();
            var previewIndex = Array.IndexOf(args, "-p3-playtest-profile");
            if (previewIndex >= 0 && previewIndex + 1 < args.Length && Path.IsPathRooted(args[previewIndex + 1]))
                SavePath = Path.GetFullPath(args[previewIndex + 1]);
            var p4Isolation = Array.IndexOf(args, "-v051-p4-smoke");
            if(Array.IndexOf(args,"-p47-smoke")>=0)p4Isolation=Array.IndexOf(args,"-p47-smoke");
            if(Array.IndexOf(args,"-p5-smoke")>=0)p4Isolation=Array.IndexOf(args,"-p5-smoke");
            if(Array.IndexOf(args,"-p6-smoke")>=0)p4Isolation=Array.IndexOf(args,"-p6-smoke");
            if(Array.IndexOf(args,"-v11-smoke")>=0)p4Isolation=Array.IndexOf(args,"-v11-smoke");
            if (p4Isolation >= 0 && p4Isolation + 1 < args.Length && Path.IsPathRooted(args[p4Isolation + 1]))
                SavePath = Path.Combine(Path.GetFullPath(args[p4Isolation + 1]), "Profiles", "profile.json");
            var startupConfig=LoadLegacyConfig();var startupState=CreateP0State(51051UL);
            CombatSession.InitializeP5State(startupState,startupConfig);
            Session = new CombatSession(startupState,startupConfig);
            CreateRoom();
            var cameraObject = new GameObject("Fixed 3-4 battle camera");
            cameraObject.transform.SetParent(transform, false);
            BattleCamera = cameraObject.AddComponent<Camera>();
            BattleCamera.tag = "MainCamera";
            BattleCamera.clearFlags = CameraClearFlags.SolidColor;
            BattleCamera.backgroundColor = new Color(.025f, .04f, .065f);
            BattleCamera.nearClipPlane = .05f;
            BattleCamera.farClipPlane = 120;
            cameraObject.AddComponent<AudioListener>();
            Rig = cameraObject.AddComponent<FixedBattleCamera>();
            Rig.Yaw = Presentation.yaw; Rig.Pitch = Presentation.pitch;
            Rig.BaseSize = Presentation.orthographicSize; Rig.FocusZoom = Presentation.focusZoom;
            Rig.Initialize(BattleCamera, () => Position(ActiveActor), PartyCenter, () => IsFrozen, () => IsFocus);
            var cutaway = map.gameObject.AddComponent<FrontWallCutaway>();
            cutaway.Configure(walls.ToArray(), BattleCamera, Vector3.zero);
            Feedback = gameObject.AddComponent<BattleFeedback>();
            Hud = gameObject.AddComponent<BattleHud>(); Hud.Initialize(this);
            InitializePresentation();
            ImpactFeedback=gameObject.AddComponent<HitFeedback>();ImpactFeedback.Initialize(this);
            InitializeFrontend();
            SynchronizeViews(); ApplyTime();
            var smokeIndex = Array.IndexOf(args, "-v051-smoke");
            if (smokeIndex >= 0 && smokeIndex + 1 < args.Length)
                gameObject.AddComponent<BattleSmokeFlow>().Begin(this, args[smokeIndex + 1]);
            var partySmokeIndex = Array.IndexOf(args, "-v051-party-smoke");
            if (partySmokeIndex >= 0 && partySmokeIndex + 1 < args.Length)
                gameObject.AddComponent<BattlePartySmokeFlow>().Begin(this, args[partySmokeIndex + 1]);
            var dungeonSmokeIndex = Array.IndexOf(args, "-v051-dungeon-smoke");
            if (dungeonSmokeIndex >= 0 && dungeonSmokeIndex + 1 < args.Length)
                gameObject.AddComponent<BattleDungeonSmokeFlow>().Begin(this, args[dungeonSmokeIndex + 1]);
            var presentationSmokeIndex = Array.IndexOf(args, "-v051-p3-smoke");
            if (presentationSmokeIndex >= 0 && presentationSmokeIndex + 1 < args.Length)
                gameObject.AddComponent<BattlePresentationSmokeFlow>().Begin(this, args[presentationSmokeIndex + 1]);
            var perfIndex = Array.IndexOf(args, "-p46-perf");
            if (perfIndex >= 0 && perfIndex + 1 < args.Length) gameObject.AddComponent<P46Performance>().Begin(this, args[perfIndex + 1]);
            var p4SmokeIndex = Array.IndexOf(args, "-v051-p4-smoke");
            if (p4SmokeIndex >= 0 && p4SmokeIndex + 1 < args.Length)
                gameObject.AddComponent<BattlePresentationSmokeFlow>().BeginP4(this, args[p4SmokeIndex + 1]);
            var p47SmokeIndex=Array.IndexOf(args,"-p47-smoke");
            if(p47SmokeIndex>=0&&p47SmokeIndex+1<args.Length)
                gameObject.AddComponent<BattlePresentationSmokeFlow>().BeginP47(this,args[p47SmokeIndex+1]);
            var p6SmokeIndex=Array.IndexOf(args,"-p6-smoke");
            if(p6SmokeIndex>=0&&p6SmokeIndex+1<args.Length)gameObject.AddComponent<BattlePresentationSmokeFlow>().BeginP6(this,args[p6SmokeIndex+1]);
            var v11SmokeIndex=Array.IndexOf(args,"-v11-smoke");
            if(v11SmokeIndex>=0&&v11SmokeIndex+1<args.Length)gameObject.AddComponent<BattlePresentationSmokeFlow>().BeginV11(this,args[v11SmokeIndex+1]);
            var p5SmokeIndex=Array.IndexOf(args,"-p5-smoke");
            if(p5SmokeIndex>=0&&p5SmokeIndex+1<args.Length)
                gameObject.AddComponent<BattlePresentationSmokeFlow>().BeginP5(this,args[p5SmokeIndex+1]);
        }

        CombatConfig LoadConfig()
        {
            var config=LoadLegacyConfig();
            if(!legacyTestRules)
            {
                var p5=Resources.Load<TextAsset>("P5CombatConfig");
                if(p5!=null)config.p5=CombatJson.Read<P5Config>(p5.text);
                if(p6RuntimeRules){var p6=Resources.Load<TextAsset>("P6CombatConfig");if(p6!=null)config.p6=CombatJson.Read<P6Config>(p6.text);}
            }
            return config;
        }
        CombatConfig LoadLegacyConfig()
        {
            var json = Resources.Load<TextAsset>("CombatConfig");
            return json == null ? new CombatConfig() : CombatJson.Read<CombatConfig>(json.text);
        }
        CombatState CreateP0State(ulong seed)
        {
            var state = CombatState.Create("profile-1", "campaign-p0/" + Guid.NewGuid().ToString("N"), seed);
            foreach (var actor in state.actors) actor.hp = actor.maxHp = Mathf.Max(1, Presentation.fighterHealth);
            foreach (var actor in state.enemies) actor.hp = actor.maxHp = Mathf.Max(1, Presentation.enemyHealth);
            return state;
        }
        public void InitializeForTests(string path, bool startNew = true, bool useP5 = false)
        {
            legacyTestRules=!useP5; p6RuntimeRules=false;
            IgnoreApplicationFocus = true;
            SavePath = path; Profile = null; LoadP4Preferences(); IsolateSyntheticInput();
            expectDungeonProfile = false;
            expectPartyProfile = false;
            if (startNew) StartNew();
        }
        public void StartNew()
        {
            BeginNew(() => CreateP0State(unchecked((ulong)DateTime.UtcNow.Ticks)));
        }
        void BeginNew(Func<CombatState> createState)
        {
            Profile = null; expectDungeonProfile = false; dungeonDialog = "";
            SetDungeonTerrainVisible(false);
            // A new identity cannot overwrite an existing authority. Archive the previous attempt first.
            var previousSession = Session;
            var archived = new List<KeyValuePair<string, string>>();
            try
            {
                var suffix = ".previous-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
                foreach (var path in new[] { SavePath, SavePath + ".bak", SavePath + ".tmp" })
                {
                    if (!File.Exists(path)) continue;
                    File.Move(path, path + suffix);
                    archived.Add(new KeyValuePair<string,string>(path, path + suffix));
                }
                var rules=LoadConfig();var initial=createState();CombatSession.InitializeP5State(initial,rules);
                var candidate = new CombatSession(initial,rules,SavePath);
                var saved = candidate.Save();
                if (!saved.success) throw new IOException(saved.reason);
                Session = candidate;
            }
            catch (Exception error)
            {
                foreach (var pair in archived)
                    if (!File.Exists(pair.Key) && File.Exists(pair.Value)) File.Move(pair.Value,pair.Key);
                Session = previousSession; Message = "新尝试未创建，旧快照保留：" + error.Message; return;
            }
            Menu = false; SelectedActor = "fighter"; SelectedTarget = "enemy-1"; SelectedAction = "attack";
            rollingTicket = null; RollElapsed = 0;
            QuickDice = false;
            Message = "WASD移动 · 左键近斩 · 右键格挡 · 空格闪避 · Shift瞄准 · Tab战术专注";
            SynchronizeViews(); ApplyTime(); RebindPresentation();
        }
        public void ContinueSaved()
        {
            if (expectDungeonProfile) { ContinueDungeonSaved(); return; }
            try
            {
                var restored = expectPartyProfile ? CombatSession.Load(SavePath, LoadConfig(), "p1-party") : CombatSession.Load(SavePath, LoadConfig());
                if (restored.State.IsPartyP1 != expectPartyProfile)
                    throw new InvalidOperationException("存档规则配置不匹配；请使用对应版本继续。");
                Session = restored;
                if (State.world.numbers.TryGetValue("focusScale", out float savedScale) &&
                    (Mathf.Approximately(savedScale, 0) || Mathf.Approximately(savedScale, .1f) || Mathf.Approximately(savedScale, .15f)))
                    Session.Config.focusScale = savedScale;
                RestoreDiceAnimationPreference();
                Menu = false; rollingTicket = null; RollElapsed = 0;
                SelectedActor = State.pendingRoll?.actorId ?? (PartyMode ? State.commandActorId : State.activeActorId);
                SelectedTarget = State.pendingRoll != null ? State.pendingRoll.targetId : State.enemies.FirstOrDefault(e => e.Alive)?.id;
                Message = "已恢复完整战斗快照；已付费结果、队列、双方位置和随机状态保持。";
                SynchronizeViews(); RestorePresentationSnapshot(); ApplyTime(); RebindPresentation();
            }
            catch (Exception error) { Message = "读取失败，原文件保留：" + error.Message; }
        }
        public void Suspend()
        {
            if (Training) { EndTraining(); return; }
            CapturePresentationSnapshot();
            var result = Session.Save();
            if (!result.success) { Message = "保存失败：" + result.reason; return; }
            Menu = true; FrontPage = "main"; FrontSettings = false; ApplyTime(); Message = "完整当前快照已保存。";
        }
        public void SetFocusScale(float scale)
        {
            if (!Mathf.Approximately(scale, 0) && !Mathf.Approximately(scale, .1f) && !Mathf.Approximately(scale, .15f))
                throw new ArgumentOutOfRangeException(nameof(scale));
            if (Profile != null)
            {
                ProfileAction(new ProfileCommand { kind = ProfileCommandKind.SetFocusScale, amount = scale });
                return;
            }
            float previous = Session.Config.focusScale;
            bool hadValue = State.world.numbers.TryGetValue("focusScale", out float previousSaved);
            Session.Config.focusScale = scale; State.world.numbers["focusScale"] = scale;
            var saved = Session.Save();
            if (!saved.success)
            {
                Session.Config.focusScale = previous;
                if (hadValue) State.world.numbers["focusScale"] = previousSaved; else State.world.numbers.Remove("focusScale");
                Message = "设置未保存：" + saved.reason;
            }
            ApplyTime();
        }
        public void SetQuickDice(bool value) => SetDiceAnimationSpeed(value ? DiceAnimationSpeed.Fast : DiceAnimationSpeed.Standard);
        public CombatResult Command(CommandKind kind, string actor = null, string target = null, string action = null, string ticket = null, string queue = null, string id = null, Vector3? direction = null, CommandAuthority authority = CommandAuthority.Player)
        {
            var ownedTicket = ticket == null ? State.pendingRoll : State.rolls.FirstOrDefault(t => t.id == ticket);
            bool ticketCommand = kind == CommandKind.RevealRoll || kind == CommandKind.Reroll || kind == CommandKind.Modify ||
                kind == CommandKind.Precision || kind == CommandKind.Commit || kind == CommandKind.Cancel;
            string actorId = actor ?? (ticketCommand && ownedTicket != null ? ownedTicket.actorId : SelectedActor);
            string targetId = target ?? (ticketCommand && ownedTicket != null ? ownedTicket.targetId : SelectedTarget);
            if (kind == CommandKind.StartQueued || kind == CommandKind.ResolveQueued)
            {
                var queued = State.queue.FirstOrDefault(q => q.id == queue);
                if (queued != null) { actorId = queued.actorId; targetId = queued.targetId; }
            }
            var source = State.Actor(actorId); var destination = State.Actor(targetId);
            bool lineOfSight = source == null || destination == null || HasLineOfEffect(Position(source), Position(destination));
            bool hasShoveEndpoint = false;
            Vector3 shoveEndpoint = Vector3.zero;
            if (kind == CommandKind.ResolveQueued && source != null && destination != null)
            {
                var queued = State.queue.FirstOrDefault(q => q.id == queue);
                var roll = queued == null ? null : State.rolls.FirstOrDefault(r => r.id == queued.ticketId);
                if (roll != null && roll.actionId == "shove")
                {
                    Vector3 shoveDirection = BattleGeometry.Ground(Position(destination) - Position(source)).normalized;
                    if (shoveDirection.sqrMagnitude < .01f) shoveDirection = Vector3.right;
                    shoveEndpoint = BattleGeometry.MoveAgainstWalls(Position(destination), shoveDirection * roll.spec.shoveDistance, .36f, 1 << WallLayer);
                    hasShoveEndpoint = true;
                }
            }
            return ExecuteWorldCommand(new CombatCommand
            {
                id = id ?? Guid.NewGuid().ToString("N"), kind = kind, authority = authority,
                actorId = actorId, targetId = targetId,
                actionId = action ?? (ticketCommand && ownedTicket != null ? ownedTicket.actionId : SelectedAction), ticketId = ticket ?? State.pendingRoll?.id, queueId = queue,
                lineOfSight = lineOfSight, qualified = hasShoveEndpoint,
                p5 = P5Enabled && kind==CommandKind.ConfirmRoll ? new P5CommandData{opportunityId=SelectedOpportunity} : null,
                x = kind == CommandKind.StartDodge && direction.HasValue ? direction.Value.x : shoveEndpoint.x,
                z = kind == CommandKind.StartDodge && direction.HasValue ? direction.Value.z : shoveEndpoint.z
            });
        }
        public CombatResult ExecuteWorldCommand(CombatCommand command)
        {
            using (P46Performance.Measure(11)) return ExecuteWorldCommandMeasured(command);
        }
        CombatResult ExecuteWorldCommandMeasured(CombatCommand command)
        {
            // A replay uses its original canonical spatial evidence. Recomputing it after
            // the target moves would mutate the already accepted command fingerprint.
            if(command!=null&&!string.IsNullOrEmpty(command.id)&&State.receipts.Any(r=>r.id==command.id))
                return Profile!=null?Profile.ExecuteCombat(command):Session.Execute(command);
            if(V11CombatEnabled&&command.kind==CommandKind.ConfirmRoll&&V11AbilityCatalog.IsSelfTarget(command.actionId))command.targetId=command.actorId;
            Hud?.FocusFeedback?.Refresh();
            CapturePresentationSnapshot();
            string previousActive=State.activeActorId, previousPurpose=State.pendingRoll?.purpose;
            int previousCharges=State.charges;
            int previousOpportunities=State.p5?.opportunities.Count??0;
            var source = State.Actor(command.actorId); var destination = State.Actor(command.targetId);
            if (State.activeReaction != null && (command.kind == CommandKind.Commit || command.kind == CommandKind.CommitReaction || command.kind == CommandKind.ConfirmReactionRoll))
                destination = State.Actor(State.activeReaction.defenderId);
            if (source != null && destination != null)
                command.lineOfSight = HasLineOfEffect(CastOrigin(source), Position(destination));
            if(V12Enabled&&command.actionId=="v12_finn_grove"&&command.kind==CommandKind.RequestHeroAction&&source!=null)
                command.lineOfSight=HasLineOfEffect(CastOrigin(source),new Vector3(command.x,source.heightY,command.z));
            if(V12Enabled&&command.actionId=="v12_finn_grove"&&command.kind==CommandKind.ResolveHeroAction&&source?.v12?.intent!=null)
                command.lineOfSight=HasLineOfEffect(Position(source),new Vector3(source.v12.intent.destinationX,source.heightY,source.v12.intent.destinationZ));
            PopulateP5CommandEvidence(command);
            PopulateP6CommandEvidence(command);
            PopulateV12BuildEvidence(command);
            var kind = command.kind;
            var result = Profile != null ? Profile.ExecuteCombat(command) : Session.Execute(command);
            if(result.success&&!result.duplicate)Hud?.FocusFeedback?.Refresh();
            // A successful durable command has just saved the latest complete world. Keep
            // the two-simulation-second fallback relative to that save, not to an older one.
            if (result.success && !result.duplicate) saveElapsed = 0;
            bool silentAi = command.flag;
            if (!silentAi) Message = result.success ? Describe(kind, result) : "未执行：" + BattleHud.HumanReason(result.reason);
            if(result.success&&!result.duplicate&&P5Enabled&&State.p5.opportunities.Count>previousOpportunities)
            {
                var opportunity=State.p5.opportunities.LastOrDefault(t=>!t.consumed&&t.remaining>0&&t.p5.status=="Available");
                if(opportunity!=null)Message=DisplayTarget(opportunity.targetId)+" · "+OpportunityName(opportunity.p5.kind)+"！另一位队员可接应 [F]，不花战术次数。";
            }
            if (result.success && kind == CommandKind.EnterFocus && PartyMode)
            {
                SelectedActor = State.commandActorId;
                var options = AvailableActions(SelectedActor);
                if (!options.Contains(SelectedAction)) SelectedAction = options.FirstOrDefault() ?? "attack";
            }
            if (result.success && PartyMode)
            {
                if (State.pendingRoll != null && State.pendingRoll.purpose == "Tactical")
                {
                    SelectedActor = State.pendingRoll.actorId; SelectedTarget = State.pendingRoll.targetId;
                    SelectedAction = State.pendingRoll.actionId;
                }
                else if (State.mode == CombatMode.TacticalFocus)
                {
                    SelectedActor = State.commandActorId;
                    var options = AvailableActions(SelectedActor);
                    if (!options.Contains(SelectedAction)) SelectedAction = options.FirstOrDefault() ?? "attack";
                }
            }
            NormalizeV11Target();
            if (result.success && (kind == CommandKind.ConfirmRoll || kind == CommandKind.ConfirmReactionRoll || kind == CommandKind.Reroll ||
                kind == CommandKind.RequestHeroAction && command.actionId == "v13_evan_correct" && command.v14CorrectionChoice == "reroll" && State.mode == CombatMode.RollFreeze))
            { rollingTicket = null; RollElapsed = 0; SuspendRollAnimationClock(); }
            if(result.success&&(kind==CommandKind.ConfirmRoll||kind==CommandKind.StartBasicAttack&&command.actionId=="basic_followup"))SelectedOpportunity=null;
            if (result.success && (kind == CommandKind.Pause || kind == CommandKind.Resume)) SuspendRollAnimationClock();
            ObservePresentationCommand(command,result,previousActive,previousPurpose,previousCharges);
            ObserveP6Command(command,result);
            ObserveV11Command(command,result);
            ObserveV12Command(command,result);
            if(result.success&&!result.duplicate&&CommandPublished!=null)
                CommandPublished.Invoke(CombatJson.Clone(command),CombatJson.Clone(result));
            if(result.success&&!result.duplicate)ImpactFeedback?.ObserveCommand(command,result);
            if(result.success&&!result.duplicate)RefreshP5EnvironmentVisuals();
            if(result.success && kind==CommandKind.Resume) ApplyP4Preferences();
            ApplyTime(); SynchronizeViews();
            UpdateTargetPreview();
            RefreshTacticalPreview();
            if (Hud != null) Hud.RefreshView();
            if(result.success) SynchronizeDecisionClockBoundary();
            return result;
        }
        string Describe(CommandKind kind, CombatResult result)
        {
            if (result.damageApplied && !string.IsNullOrEmpty(result.reactionWindowId))
                return result.damage == 0 ? "反应成功挡住了本次来袭。" : "本次来袭结算：受到 " + result.damage + " 点伤害。";
            switch (kind)
            {
                case CommandKind.ConfirmRoll: return "战术次数 -1；本次结果已写入存档。掷骰期间世界完全暂停。";
                case CommandKind.RevealRoll: return "世界恢复子弹时间。可接受、投入资源、重掷或放弃 Commit。";
                case CommandKind.Commit: return "已入队。可继续安排指令；按 Tab 返回实时执行。";
                case CommandKind.Cancel: return "放弃执行；已付 战术次数 和骰后资源不返还。";
                case CommandKind.CancelQueued: return "已取消排队行动；未起手的行动与长期资源占用已释放，战术次数 不返还。";
                case CommandKind.StartBasicAttack: return "攻击起手。";
                case CommandKind.ResolveBasicAttack:
                case CommandKind.BasicAttack: return result.reason == "basic_swing_missed" ? "挥空：接触时目标离开范围或被遮挡。" : result.hit ? (result.critical ? "暴击" : "命中") + " · " + result.total + " 对 AC，伤害 " + result.damage : "护甲偏转 · 攻击总值 " + result.total;
                case CommandKind.StartAim: return "稳住1秒：移动或受击会打断瞄准，期间不能闪避。";
                case CommandKind.EnterFocus: return "选择角色、行动和目标；确认前不扣任何费用。";
                case CommandKind.ExitFocus: return "已返回实时战斗；队列中的行动会走到射程内执行。";
                case CommandKind.EndEncounter: return State.actors.Any(a => a.Alive) ? "遭遇完成。可保存结果并返回标题。" : "队伍倒下。可保留快照或重新尝试。";
                case CommandKind.BeginReaction: return "选择反应；确认前查看角色费用。战术次数 不消耗。";
                case CommandKind.ConfirmReactionRoll: return "反应检定：世界完全暂停；本次不花 战术次数。";
                case CommandKind.SwitchActive: return "主控已切换；专注中的指挥对象与既有队列保持。";
                case CommandKind.SelectCommandActor: return "已选择指挥队员；请选择其行动和目标。";
                default: return BattleHud.HumanReason(result.reason);
            }
        }
        void Update() { using (P46Performance.Measure(1)) UpdateMeasured(); }
        void UpdateMeasured()
        {
            if (Session == null) return;
            bool windowFocused = IgnoreApplicationFocus || Application.isFocused;
            if (!windowFocused)
            {
                SuspendRollAnimationClock();SuspendDecisionClock();
                if (!Menu && !DungeonModal && State.mode != CombatMode.CombatPaused) Command(CommandKind.Pause);
                if (!Menu) Profile?.RecordActivity(Time.unscaledDeltaTime, focusLost: true);
                ApplyTime(); return;
            }
            ProcessInput();
            if(AdvanceV13Decision())return;
            if (Menu) { SuspendRollAnimationClock(); SuspendDecisionClock(); ApplyTime(); return; }
            if (DungeonModal) { SuspendRollAnimationClock(); SuspendDecisionClock(); Profile?.RecordActivity(Time.unscaledDeltaTime, focusLost: !IgnoreApplicationFocus && !Application.isFocused); ApplyTime(); return; }
            if(State.mode==CombatMode.RollFreeze)
            {
                SuspendDecisionClock();
                if(Profile!=null)Profile.Tick(Time.unscaledDeltaTime,windowFocused,0);else Session.Tick(Time.unscaledDeltaTime,windowFocused,0);
                AdvanceAutomaticRollAnimation();ApplyTime();return;
            }
            SuspendRollAnimationClock();
            CapturePresentationSnapshot();
            var unsettledIncoming = State.incomingEvents.Where(e => !e.damageApplied).Select(e => e.id).ToArray();
            RefreshEngagementEvidence();
            float decisionDelta=SampleDecisionDelta();
            bool presentationHold=ImpactFeedback!=null&&ImpactFeedback.Holding;
            float simulationInput=presentationHold?0:Time.unscaledDeltaTime;
            CaptureDodgeMotionWindow();
            var dt = Profile != null ? Profile.Tick(simulationInput, windowFocused, decisionDelta) : Session.Tick(simulationInput, windowFocused, decisionDelta);
            if(presentationHold)Profile?.RecordActivity(Time.unscaledDeltaTime); // Active human time is still counted during a cosmetic hold.
            ImpactFeedback?.Observe();
            SynchronizeDecisionClockBoundary();
            if (!string.IsNullOrEmpty(Session.LastTickError)) Message = "保存受阻，战斗状态未推进：" + Session.LastTickError;
            foreach (var incoming in State.incomingEvents.Where(e => e.damageApplied && unsettledIncoming.Contains(e.id)))
            {
                var attacker = State.Actor(incoming.attackerId); var defender = State.Actor(incoming.defenderId);
                Message = "反应窗口结束，本次来袭结算：" + incoming.finalDamage + " 点伤害。";
            }
            ApplyTime();
            if (State.mode == CombatMode.RollFreeze) AdvanceAutomaticRollAnimation();
            if (dt > 0)
            {
                SimulatedFrames++;
                // A contact in any stage can start a cosmetic hold during this very frame.
                // Finish that contact atomically, then leave later simulation stages for resume.
                AdvanceP6Binding();
                if(ImpactFeedback==null||!ImpactFeedback.Holding)AdvanceV12Actions(dt);
                if(ImpactFeedback==null||!ImpactFeedback.Holding)AdvanceBasicOrders(dt);
                if(ImpactFeedback==null||!ImpactFeedback.Holding)MovePlayer(dt);
                if(ImpactFeedback==null||!ImpactFeedback.Holding)AdvanceEnemies(dt);
                if(ImpactFeedback==null||!ImpactFeedback.Holding)AdvanceParty(dt);
                if(ImpactFeedback==null||!ImpactFeedback.Holding)AdvanceQueue(dt);
                if(ImpactFeedback==null||!ImpactFeedback.Holding)AdvanceV12Environment(dt);
                AdvanceV13Fate(dt);
                saveElapsed += dt;
                if (saveElapsed >= 2) { saveElapsed = 0; var saved = Session.Save(); if (!saved.success) Message = "自动存档失败：" + saved.reason; }
            }
            // Commands can settle damage while Focus is configured at zero. End-state UI
            // must not require a positive simulation step or another Tab press.
            if ((ImpactFeedback==null||!ImpactFeedback.Holding) && State.mode != CombatMode.CombatPaused && State.inCombat && State.pendingRoll == null && !Session.V12BeamPending &&
                !State.reactionWindows.Any(w => w.stage == "Offered" || w.stage == "Choosing" || w.stage == "Rolling" || w.stage == "AwaitDecision") &&
                (!CombatSession.V13PartyAlive(State) || !State.enemies.Any(a => a.Alive)))
            {
                if (Profile != null) ResolveDungeonEncounter();
                else Command(CommandKind.EndEncounter);
            }
            SynchronizeViews();
            UpdateTargetPreview();
            RefreshTacticalPreview();
            AdvanceDungeonExploration();
            RefreshV12Presentation();
            RefreshV12EnvironmentPresentation();
        }
        public void AdvanceRollAnimation(float realDelta)
        {
            // Explicit fixture/manual advancement owns this sample. Automatic Update must
            // establish its next baseline rather than adding elapsed time a second time.
            SuspendRollAnimationClock();
            AdvanceRollPresentation(realDelta);
        }
        void AdvanceRollPresentation(float realDelta)
        {
            if (State.mode != CombatMode.RollFreeze || State.pendingRoll == null) return;
            if (rollingTicket != State.pendingRoll.id) { rollingTicket = State.pendingRoll.id; RollElapsed = 0; }
            RollElapsed += realDelta;
            Audio?.ObserveRollAnimation(State.pendingRoll,RollElapsed/CurrentRollDuration);
            if (RollElapsed >= CurrentRollDuration)
                Command(CommandKind.RevealRoll, ticket: State.pendingRoll.id);
        }
        void IsolateSyntheticInput()
        {
            var devices=UnityEngine.InputSystem.InputSystem.devices.Where(d=>!d.native).ToArray();
            Controls.Map.devices=devices;
            if(UnityEngine.EventSystems.EventSystem.current!=null)
            {
                var module=UnityEngine.EventSystems.EventSystem.current.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                if(module!=null&&module.actionsAsset!=null)module.actionsAsset.devices=devices;
            }
        }
        void ProcessInput()
        {
            if (ProcessDungeonInput()) return;
            if (Controls.Pause.WasPressedThisFrame())
            {
                if (Menu && HandleFrontendBack()) return;
                if (Menu) { if (FrontSettings) CloseFrontendSettings(); return; }
                Command(State.mode == CombatMode.CombatPaused ? CommandKind.Resume : CommandKind.Pause);
            }
            if (Menu || IsFrozen || ImpactFeedback!=null&&ImpactFeedback.Holding) return;
            if(CombatSession.V13FateBusy(State)){if(Controls.Confirm.WasPressedThisFrame())V13ChooseFate(false);return;}
            if (Controls.Reaction.WasPressedThisFrame() && State.activeReaction == null)
            {
                var offered = State.reactionWindows.FirstOrDefault(w => w.stage == "Offered");
                if (offered != null) { React(CommandKind.BeginReaction, offered.id); return; }
            }
            if(P6Enabled && Controls.Smite.WasPressedThisFrame()){ OpenCurrentKeyAction(); return; }
            if(P6Enabled && Controls.Followup.WasPressedThisFrame()){ RequestCurrentBasicAction(); return; }
            if (Controls.Focus.WasPressedThisFrame())
            {
                p6QuickFocus=false;
                if (State.mode == CombatMode.TacticalFocus || State.mode == CombatMode.PostRollDecision) Command(CommandKind.ExitFocus);
                else Command(CommandKind.EnterFocus);
                return; // A mode-changing key cannot also dispatch a realtime action in this frame.
            }
            for (var i = 0; i < State.actors.Count && i < 3; i++)
                if (Controls.Characters[i].WasPressedThisFrame())
                {
                    if (State.mode == CombatMode.TacticalFocus || FreeOrdersVisible) SelectCommandActor(State.actors[i].id);
                    else if(V13AssistMode){if(i<2)RequestAssist(i);}
                    else if (PartyMode) SwitchActive(State.actors[i].id);
                    return;
                }
            var overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Vector3 aim;
            if (!overUI && Rig.TryGroundAim(Controls.Pointer.ReadValue<Vector2>(), out aim)) WorldAimPoint = aim;
            if(P5Enabled&&Controls.Followup.WasPressedThisFrame()) { HandleP5FollowupInput(); return; }
            if(P5Enabled&&Controls.Aim.WasPressedThisFrame()&&(State.mode==CombatMode.TacticalFocus||FreeOrdersVisible))
            { RequestBasicOrder(IssuePreparationOrder,"职业准备");Hud.RefreshView();return; }
            if (IsFocus || FreeOrdersVisible)
            {
                if (!overUI && Controls.Attack.WasPressedThisFrame())
                {
                    if(V13TryClickNode())return;
                    if (HandleTacticalGroundClick()) return;
                    var closest = State.enemies.Where(e => e.Alive).OrderBy(e => BattleGeometry.DistanceXZ(Position(e), WorldAimPoint)).FirstOrDefault();
                    if (closest != null) { SelectedTarget = closest.id; NormalizeV11Target(); TutorialFact("selection.target",Guid.NewGuid().ToString("N")); PollPresentation(); }
                }
                if (IsFocus && Controls.Confirm.WasPressedThisFrame())
                    ConfirmCurrentDecision();
                return;
            }
            if (Controls.Aim.WasPressedThisFrame())
            {
                string previousSelected = SelectedActor; SelectedActor = State.activeActorId;
                IssuePreparationOrder(true); SelectedActor = previousSelected;
            }
            if (Controls.Guard.WasPressedThisFrame() && !overUI) Command(CommandKind.StartGuard, actor: State.activeActorId);
            if (Controls.Guard.WasReleasedThisFrame() && ActiveActor.guardRemaining > 0) Command(CommandKind.EndGuard, actor: State.activeActorId);
            if (Controls.Dodge.WasPressedThisFrame())
            {
                var direction = Rig.ScreenMove(Controls.Move.ReadValue<Vector2>());
                if (direction.sqrMagnitude < .01f) direction = (WorldAimPoint - Position(ActiveActor)).normalized;
                Command(CommandKind.StartDodge, actor: State.activeActorId, direction: direction);
            }
            if (Controls.Attack.WasPressedThisFrame() && !overUI)
            {
                if(V13TryClickNode())return;
                var basic = Session.Config.BasicFor(ActiveActor);
                var swing = AreaShape.Cone(Position(ActiveActor), WorldAimPoint - Position(ActiveActor), basic.range, P6Enabled&&ActiveActor.role==ActorRole.Paladin?Session.Config.p6.basicAngle*.5f:basic.ranged ? 16 : 55);
                var closest = State.enemies.Where(e => e.Alive && BattleGeometry.Contains(swing, Position(e)))
                    .OrderBy(e => BattleGeometry.DistanceXZ(Position(e), WorldAimPoint)).FirstOrDefault();
                if (closest != null) { SelectedTarget = closest.id; BeginBasicAttack(State.activeActorId, closest.id); }
                else Message = basic.ranged ? "瞄准方向内没有法术目标。" : "瞄准方向内没有近战目标。靠近后攻击，或进入战术专注。";
            }
        }
        void MovePlayer(float dt)
        {
            var actor = ActiveActor;
            if (!DirectMovementMayOverrideOrder(actor, Controls.Move.ReadValue<Vector2>().sqrMagnitude > .01f)) return;
            actor = ActiveActor;
            if (!actor.Alive || actor.heldBy != null && actor.heldBy.Length > 0) return;
            Vector3 direction;
            float speed;
            float dodgeMotion=DodgeMovementSeconds(actor,dt);
            if (dodgeMotion > 0) { direction = new Vector3(actor.lockedX, 0, actor.lockedZ); speed = Presentation.dodgeSpeed;dt=dodgeMotion; }
            else
            {
                if (IsFocus || actor.windupRemaining > 0 || CombatSession.V12ActionActive(actor)) return;
                direction = Rig.ScreenMove(Controls.Move.ReadValue<Vector2>());
                speed = Presentation.moveSpeed * (actor.guardRemaining > 0 && (!P5Enabled||actor.p5?.preparation?.actionId!="f_brace") ? .3f : 1);
                if (actor.aimRemaining > 0 && direction.sqrMagnitude > .01f) { Command(CommandKind.CancelAim, actor: actor.id); actor = ActiveActor; }
            }
            var previousPosition=Position(actor);
            MoveActor(actor, direction * (speed * dt));
            if(Controls.Move.ReadValue<Vector2>().sqrMagnitude>.01f){float moved=BattleGeometry.DistanceXZ(previousPosition,Position(actor));ObservePlayerMovement(moved);ObserveP6Movement(moved);}
        }
        public void MoveActor(ActorState actor, Vector3 delta)
        {
            if (actor == null || IsFrozen || actor.staggerRemaining > 0 || actor.statuses.Any(s => s.id == "restrained" && s.remaining > 0)) return;
            if(P5Enabled)delta*=Session.P5MovementMultiplier(actor.id);
            if(P6Enabled)delta*=V12Enabled?Mathf.Min(P6MovementMultiplier(actor),Session.V12MovementMultiplier(actor.id)):P6MovementMultiplier(actor);
            var point = BattleGeometry.MoveAgainstWalls(Position(actor), delta, .36f, 1 << WallLayer);
            actor.x = point.x; actor.z = point.z; actor.heightY = point.y;
        }
        public static Vector3 Position(ActorState actor) => actor == null ? Vector3.zero : new Vector3(actor.x, actor.heightY, actor.z);
        public bool HasLineOfEffect(Vector3 sourceFeet, Vector3 targetFeet)
        {
            if(P5Enabled)return P5LineOfEffect(sourceFeet,targetFeet);
            return !Physics.Linecast(sourceFeet + Vector3.up * .55f, targetFeet + Vector3.up * .55f,
                1 << WallLayer, QueryTriggerInteraction.Ignore);
        }
        Vector3 PartyCenter()
        {
            if(V13AssistMode)return Position(ActiveActor);
            var alive = State.actors.Where(a => a.Alive).ToArray();
            return alive.Length == 0 ? Vector3.zero : alive.Aggregate(Vector3.zero, (sum, actor) => sum + Position(actor)) / alive.Length;
        }
        public void AdvanceEnemies(float dt)
        {
            if (dt <= 0 || IsFrozen || ImpactFeedback!=null&&ImpactFeedback.Holding) return;
            if (Profile != null) { AdvanceDungeonEnemies(dt); return; }
            foreach (var id in State.enemies.Select(e => e.id).ToArray())
            {
                if(ImpactFeedback!=null&&ImpactFeedback.Holding)break;
                var enemy = State.Actor(id);
                if (!enemy.Alive || enemy.staggerRemaining > 0 || enemy.statuses.Any(s => s.id == "restrained" && s.remaining > 0)) continue;
                var target = State.actors.Where(a => a.Alive).OrderBy(a => BattleGeometry.DistanceXZ(Position(a), Position(enemy))).FirstOrDefault();
                if (target == null) continue;
                enemy.aiRemaining -= dt;
                if (enemy.aiState == 1)
                {
                    if (enemy.aiRemaining > 0) continue;
                    var shape = EnemyShape(enemy);
                    var damageTargets = State.actors.Where(a => a.Alive && BattleGeometry.Contains(shape, Position(a)) &&
                        HasLineOfEffect(shape.Origin, Position(a))).Select(a => a.id).ToArray();
                    if (damageTargets.Length > 0)
                    {
                        var result = ExecuteWorldCommand(new CombatCommand
                        {
                            id = "contact-batch/" + enemy.attackGroupId, kind = CommandKind.IncomingBatch, actorId = id,
                            rootId = enemy.attackGroupId, qualified = true, authority = CommandAuthority.System,
                            incomingContacts = damageTargets.Select(v => new IncomingContact
                            {
                                targetId = v, damage = Presentation.enemyDamage, savingThrow = enemy.attackSequence % 3 == 0,
                                dc = 12, parryable = true,
                                reactionEligibleActorIds = State.actors.Where(a => a.Alive && HasLineOfEffect(Position(a), Position(State.Actor(v)))).Select(a => a.id).ToList()
                            }).ToList()
                        });
                        if (!result.success) { Message = "来袭尚未提交：" + result.reason; return; }
                    }
                    enemy = State.Actor(id);
                    enemy.aiState = 2; enemy.aiRemaining = Presentation.enemyRecoverySeconds;
                    // The whole AoE batch and this attacker's recovery are committed before stopping.
                    if(ImpactFeedback!=null&&ImpactFeedback.Holding)break;
                    continue;
                }
                if (enemy.aiRemaining > 0) continue;
                var distance = BattleGeometry.DistanceXZ(Position(enemy), Position(target));
                if (distance > 3.8f || !HasLineOfEffect(Position(enemy), Position(target)))
                {
                    enemy.lockedX = target.x; enemy.lockedZ = target.z;
                    MoveActor(enemy, (Position(target) - Position(enemy)).normalized * (Presentation.enemyMoveSpeed * dt));
                    enemy.aiState = 0;
                }
                else
                {
                    enemy.attackSequence++;
                    enemy.attackGroupId = State.encounterId + "/" + enemy.id + "/" + enemy.attackSequence;
                    enemy.attackTargetId = target.id;
                    enemy.lockedX = target.x; enemy.lockedZ = target.z;
                    enemy.aiState = 1; enemy.aiRemaining = Presentation.enemyTelegraphSeconds;
                }
            }
        }
        public AreaShape EnemyShape(ActorState enemy)
        {
            if (Profile != null) return DungeonEnemyShape(enemy);
            var locked = new Vector3(enemy.lockedX, enemy.heightY, enemy.lockedZ);
            if (enemy.attackSequence % 3 == 0) return AreaShape.Circle(locked, 2.4f);
            return AreaShape.Cone(Position(enemy), locked - Position(enemy), 4.4f, 35f);
        }
        void AdvanceQueue(float dt)
        {
            if ((State.mode != CombatMode.RealtimeCombat && State.mode != CombatMode.QueuedExecution) || ImpactFeedback!=null&&ImpactFeedback.Holding) return;
            foreach (var id in State.queue.Where(q => q.stage == "Queued" || q.stage == "Executing").Select(q => q.id).ToArray())
            {
                if(ImpactFeedback!=null&&ImpactFeedback.Holding)break;
                var queued = State.queue.First(q => q.id == id);
                var actor = State.Actor(queued.actorId); var target = State.Actor(queued.targetId);
                if (actor == null || target == null || !actor.Alive || !target.Alive)
                { Command(CommandKind.CancelQueued, queue: id); continue; }
                if (!HasLineOfEffect(Position(actor), Position(target)))
                {
                    Command(CommandKind.CancelQueued, queue: id);
                    Message = "队列已取消：目标被墙体或掩体阻挡；战术次数不退，还未消费的长期资源已释放。";
                    continue;
                }
                if (queued.stage == "Queued")
                {
                    var ticket=State.rolls.FirstOrDefault(t=>t.id==queued.ticketId);
                    if(ticket?.v12!=null)
                    {
                        // V12 rolls require a legal position before rolling. If the battlefield
                        // changes after Commit, the authority cancels; never carry a rolled hit
                        // while pursuing a target, nor bypass its one-second start deadline.
                        Command(CommandKind.StartQueued,queue:id);continue;
                    }
                    var distance = BattleGeometry.DistanceXZ(Position(actor), Position(target));
                    var spec = State.rolls.FirstOrDefault(t => t.id == queued.ticketId)?.spec;
                    if (spec == null) { Command(CommandKind.CancelQueued, queue: id); continue; }
                    var range = spec.range;
                    if (distance > range)
                    {
                        Vector3 before = Position(actor);
                        float travel = Mathf.Min(Presentation.moveSpeed * dt, Mathf.Max(0, distance - range + .01f));
                        MoveActor(actor, BattleGeometry.Ground(Position(target) - before).normalized * travel);
                        if (BattleGeometry.DistanceXZ(before, Position(actor)) < .0001f)
                        {
                            Command(CommandKind.CancelQueued, queue: id);
                            Message = "队列已取消：无法移动到合法施放位置；已付战术次数不退。";
                        }
                    }
                    else Command(CommandKind.StartQueued, queue: id);
                }
                else if (queued.windupRemaining <= 0) Command(CommandKind.ResolveQueued, queue: id, authority: CommandAuthority.System);
            }
        }
        void CreateRoom()
        {
            map = new GameObject("P0 XZ combat room").transform; map.SetParent(transform);
            units = new GameObject("Actor views").transform; units.SetParent(transform);
            Box("Ground", new Vector3(0, -.18f, 0), new Vector3(24, .3f, 20), new Color(.11f, .17f, .22f), 0, map);
            for (var x = -10; x <= 10; x += 2) Box("Grid X", new Vector3(x, -.018f, 0), new Vector3(.025f, .01f, 18), new Color(.16f, .23f, .29f), -1, map);
            for (var z = -8; z <= 8; z += 2) Box("Grid Z", new Vector3(0, -.017f, z), new Vector3(22, .01f, .025f), new Color(.16f, .23f, .29f), -1, map);
            walls.Add(Box("North wall", new Vector3(0, 1.2f, 10), new Vector3(24, 2.4f, .4f), new Color(.26f, .34f, .43f), WallLayer, map).GetComponent<Renderer>());
            walls.Add(Box("South wall", new Vector3(0, 1.2f, -10), new Vector3(24, 2.4f, .4f), new Color(.26f, .34f, .43f), WallLayer, map).GetComponent<Renderer>());
            walls.Add(Box("East wall", new Vector3(12, 1.2f, 0), new Vector3(.4f, 2.4f, 20), new Color(.26f, .34f, .43f), WallLayer, map).GetComponent<Renderer>());
            walls.Add(Box("West wall", new Vector3(-12, 1.2f, 0), new Vector3(.4f, 2.4f, 20), new Color(.26f, .34f, .43f), WallLayer, map).GetComponent<Renderer>());
            Box("Low cover", new Vector3(-3, .5f, 4), new Vector3(3, 1, 1), new Color(.32f, .43f, .49f), WallLayer, map);
            targeting = BattleGeometry.CreatePreview(AreaShape.Circle(Vector3.zero, 1), map, Teal).AddComponentIfMissing<AreaPreview>();
            targeting.gameObject.SetActive(false);
        }
        static GameObject Box(string name, Vector3 position, Vector3 size, Color color, int layer, Transform parent)
        {
            return BattleVisual.Box(name, position, size, color, parent, Mathf.Max(0, layer), layer >= 0);
        }
        public static Material ColoredMaterial(Color color)
        {
            return BattleVisual.NewUnlitMaterial(color);
        }
        void SynchronizeViews() { using (P46Performance.Measure(2)) SynchronizeViewsMeasured(); }
        void SynchronizeViewsMeasured()
        {
            if (units == null) return;
            foreach (var pair in views)
                if (State.Actor(pair.Key) == null) pair.Value.gameObject.SetActive(false);
            foreach (var pair in warnings)
                if (State.Actor(pair.Key) == null) pair.Value.gameObject.SetActive(false);
            foreach (var marker in statusMarkers.Values) marker.gameObject.SetActive(false);
            foreach (var actor in State.actors.Concat(State.enemies))
            {
                Transform view;
                if (!views.TryGetValue(actor.id, out view))
                {
                    var obj = GameObject.CreatePrimitive(PrimitiveType.Capsule); obj.name = actor.id;
                    obj.transform.SetParent(units); obj.transform.localScale = new Vector3(.75f, .8f, .75f);
                    obj.GetComponent<Collider>().enabled = false;
                    obj.GetComponent<Renderer>().sharedMaterial = ColoredMaterial(actor.isEnemy ? new Color(.88f, .32f, .28f) : actor.role == ActorRole.Rogue ? new Color(.85f,.65f,.28f) : actor.role == ActorRole.Mage ? new Color(.44f,.62f,.96f) : Teal);
                    obj.AddComponent<SpatialResourceOwner>().OwnedMaterial = obj.GetComponent<Renderer>().sharedMaterial;
                    view = obj.transform; views.Add(actor.id, view);
                    Box("Facing crest", new Vector3(0, 0, 0), new Vector3(.18f, .5f, .7f), Amber, -1, view).transform.localPosition = new Vector3(0, .45f, .45f);
                }
                view.gameObject.SetActive(V13Visible(actor)&&(actor.Alive || (!actor.isEnemy && HeroVisual.ResourceKey(actor.id) != null))); view.position = V13PresentationPosition(actor) + Vector3.up * .8f;
                foreach (var status in actor.statuses.Where(s => s.remaining > 0 && (s.id == "shield" || s.id == "restrained")))
                {
                    string key = actor.id + "/" + status.id;
                    if (!statusMarkers.TryGetValue(key, out var marker))
                    {
                        marker = BattleGeometry.CreatePreview(AreaShape.Circle(Position(actor), .68f), map,
                            status.id == "shield" ? new Color(.4f,.7f,1f) : Amber).GetComponent<AreaPreview>();
                        statusMarkers.Add(key, marker);
                    }
                    marker.SetShape(AreaShape.Circle(Position(actor), .68f)); marker.gameObject.SetActive(actor.Alive);
                }
                if (actor.Alive && !actor.isEnemy)
                {
                    var direction = actor.id == State.activeActorId ? WorldAimPoint - Position(actor) :
                        (State.Actor(actor.aiTargetId) != null ? Position(State.Actor(actor.aiTargetId)) : new Vector3(actor.aiMoveX, actor.heightY, actor.aiMoveZ)) - Position(actor);
                    if(V13Enabled&&!IsPhysical(actor)){var assistTarget=State.Actor(actor.v12?.intent?.targetId)??State.Actor(SelectedTarget);if(assistTarget!=null)direction=Position(assistTarget)-V13PresentationPosition(actor);}
                    direction.y = 0;
                    if (direction.sqrMagnitude > .01f && !IsFrozen) view.rotation = Quaternion.LookRotation(direction);
                }
                if (actor.isEnemy)
                {
                    if (!IsFrozen)
                    {
                        var facing = new Vector3(actor.lockedX - actor.x, 0, actor.lockedZ - actor.z);
                        if (facing.sqrMagnitude > .01f) view.rotation = Quaternion.LookRotation(facing);
                    }
                    AreaPreview preview;
                    if (!warnings.TryGetValue(actor.id, out preview))
                    {
                        preview = BattleGeometry.CreatePreview(EnemyShape(actor), map, new Color(1, .28f, .15f)).GetComponent<AreaPreview>();
                        warnings[actor.id] = preview;
                    }
                    preview.gameObject.SetActive(actor.Alive && actor.aiState == 1 && actor.staggerRemaining <= 0);
                    if (preview.gameObject.activeSelf) preview.SetShape(EnemyShape(actor));
                }
            }
            RefreshV13Boss();RefreshV13Terrain();Physics.SyncTransforms();
        }
        void UpdateTargetPreview()
        {
            if (targeting == null) return;
            if (IsFrozen) return; // Preserve the existing ground target throughout RollFreeze and pause.
            var target = State.Actor(SelectedTarget);
            targeting.gameObject.SetActive(IsFocus && target != null && target.Alive);
            if (targeting.gameObject.activeSelf)
            {
                var actor=State.Actor(SelectedActor);
                if(P5Enabled&&SelectedAction=="control"&&actor!=null)
                {
                    var context=State.pendingRoll?.p5;
                    var direction=context==null?Position(target)-Position(actor):new Vector3(context.directionX,0,context.directionZ);
                    targeting.SetShape(AreaShape.Cone(Position(actor),direction,Session.Config.Action("control").range,Session.Config.p5.thunderAngle*.5f));
                }
                else targeting.SetShape(AreaShape.Circle(Position(target), .8f));
            }
        }
        public void ApplyTime() { Time.timeScale = Menu || DungeonModal || Session == null || ImpactFeedback!=null&&ImpactFeedback.Holding ? 0 : Session.Scale; Time.fixedDeltaTime = .02f; }
        void OnApplicationFocus(bool focused)
        {
            // The OS may stop Update immediately after this callback. Stop local clocks
            // even if persisting Pause fails and the authoritative combat mode rolls back.
            if(!focused&&!IgnoreApplicationFocus){SuspendDecisionClock();SuspendRollAnimationClock();}
            if (!focused && !IgnoreApplicationFocus && !Menu && !DungeonModal && Session != null && State.mode != CombatMode.CombatPaused) Command(CommandKind.Pause);
        }
        void OnApplicationQuit() { if (!Menu && Session != null) { CapturePresentationSnapshot(); Session.Save(); } }
        void OnDestroy() { Controls?.Dispose(); Time.timeScale = 1; Time.fixedDeltaTime = .02f; }
    }

    internal static class ComponentHelper
    {
        internal static T AddComponentIfMissing<T>(this GameObject obj) where T : Component => obj.GetComponent<T>() ?? obj.AddComponent<T>();
    }
}

