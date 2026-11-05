using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed partial class BattleHud : MonoBehaviour
    {
        BattleWorld world;
        Canvas canvas;
        Font font;
        Text resources, stateLabel, message, actorInfo, enemyInfo, quote, diceText, resultText, queueText, menuInfo;
        Text phaseLabel, menuTitle, endTitle, endInfo, rollKindText, tacticalSpatialText,focusHeading,focusInstruction;
        GameObject tacticalSpatialPanel;
        GameObject focusPanel, rollPanel, postPanel, menuPanel, pausePanel, endPanel;
        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        readonly Color Ink = new Color(.065f,.068f,.075f,.97f), Teal = new Color(.23f,.66f,.62f), Gold = new Color(.84f,.68f,.40f);
        public Canvas Canvas => canvas;
        public LowHealthOverlay HealthOverlay {get;private set;}
        public void Initialize(BattleWorld owner)
        {
            world = owner;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
            var root = new GameObject("Battle UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (EventSystem.current == null)
            {
                var events = new GameObject("Battle EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(root.transform, false);
                // Enter/Escape belong to BattleWorld. A UI Submit must not issue a second command.
                events.GetComponent<EventSystem>().sendNavigationEvents = false;
            }
            Panel(root.transform, "Header", 0, 0, 1280, 94, Ink);
            Label(root.transform, "ACTION / CRPG", 24, 12, 240, 30, 23, Teal);
            phaseLabel = Label(root.transform, "v0.5.1 · 战斗试验场", 25, 47, 260, 25, 16, Color.white);
            resources = Label(root.transform, "", 300, 14, 480, 66, 23, Color.white);
            stateLabel = Label(root.transform, "", 900, 15, 350, 64, 20, Gold, TextAnchor.UpperRight);
            actorInfo = Label(root.transform, "", 25, 110, 320, 135, 19, Color.white);
            enemyInfo = Label(root.transform, "", 25, 270, 320, 130, 18, Color.white);
            Panel(root.transform, "Footer", 0, 604, 1280, 116, Ink);
            message = Label(root.transform, "", 24, 615, 1232, 40, 18, Gold);
            Label(root.transform, "WASD 移动   左键 近斩   右键 格挡   空格 闪避   Shift 静止瞄准   Tab 战术 / 执行", 24, 662, 1120, 25, 17, Color.white);
            Button(root.transform, "focus", "Tab 专注", 1110, 662, 144, 35, () => world.Command(world.IsFocus ? CommandKind.ExitFocus : CommandKind.EnterFocus));

            focusPanel = Panel(root.transform, "Tactical commands", 923, 108, 333, 478, Ink);
            focusHeading=Label(focusPanel.transform, "TACTICAL FOCUS", 18, 15, 290, 28, 22, Teal);
            focusInstruction=Label(focusPanel.transform, "选择角色 → 行动 → 目标", 18, 52, 290, 26, 17, Color.white);
            Button(focusPanel.transform, "fighter", "① 战士", 18, 88, 92, 36, () => { if(world.V11CombatEnabled)world.SelectTacticalSlot(0);else world.SelectCommandActor(world.P6Enabled?"char_demo_paladin":"fighter"); });
            Button(focusPanel.transform, "rogue", "② 游荡者", 119, 88, 92, 36, () => { if(world.V11CombatEnabled)world.SelectTacticalSlot(1);else world.SelectCommandActor("rogue"); });
            Button(focusPanel.transform, "mage", "③ 法师", 220, 88, 92, 36, () => { if(world.V11CombatEnabled)world.SelectTacticalSlot(2);else world.SelectCommandActor("mage"); });
            Button(focusPanel.transform, "target", "切换目标（也可点击战场）", 18, 190, 296, 28, () => world.CycleTarget());
            Button(focusPanel.transform, "attack", "投枪", 18, 141, 142, 42, () => world.SelectPresentationAction("attack"));
            Button(focusPanel.transform, "shove", "推击", 172, 141, 142, 42, () => world.SelectPresentationAction("shove"));
            Button(focusPanel.transform, "sneak", "绕背突袭", 18, 141, 296, 42, () => world.SelectPresentationAction("sneak"));
            Button(focusPanel.transform, "control", "定身术 · 1 法术位", 18, 141, 296, 42, () => world.SelectPresentationAction("control"));
            quote = Label(focusPanel.transform, "", 18, 230, 296, 128, 16, Color.white);
            Button(focusPanel.transform, "confirm", "确认掷骰 · 消耗 1 战术次数", 18, 362, 296, 42, () => world.ConfirmCurrentDecision());
            queueText = Label(focusPanel.transform, "", 18, 409, 155, 24, 14, Gold);
            Button(focusPanel.transform, "cancel-queue", "取消该角色队列", 177, 409, 137, 25, () =>
            {
                var queued = world.State.queue.FirstOrDefault(q => q.actorId == world.SelectedActor && q.stage == "Queued");
                if (queued != null) world.Command(CommandKind.CancelQueued, queue: queued.id);
            });
            Button(focusPanel.transform, "exit", "返回实时 / 执行队列  [Tab]", 18, 439, 296, 32, () => world.Command(CommandKind.ExitFocus));

            tacticalSpatialPanel = Panel(root.transform, "Tactical spatial information", 354, 108, 548, 126, Ink);
            Label(tacticalSpatialPanel.transform, "空间信息 · 1 / 2 / 3 查看队员", 14, 8, 520, 24, 18, Teal);
            tacticalSpatialText = Label(tacticalSpatialPanel.transform, "", 14, 36, 520, 84, 16, Color.white);

            rollPanel = Panel(root.transform, "Roll freeze", 365, 106, 490, 120, Ink);
            Label(rollPanel.transform, "ROLL FREEZE · 世界完全暂停", 14, 5, 462, 24, 18, Gold, TextAnchor.MiddleCenter);
            diceText = Label(rollPanel.transform, "", 14, 29, 462, 51, 36, Color.white, TextAnchor.MiddleCenter);
            rollKindText = Label(rollPanel.transform, "", 14, 85, 462, 29, 18, Color.white, TextAnchor.MiddleCenter);

            postPanel = Panel(root.transform, "Post roll decision", 913, 108, 353, 478, Ink);
            Label(postPanel.transform, "骰后决策 · 子弹时间", 18, 15, 317, 32, 22, Teal);
            resultText = Label(postPanel.transform, "", 18, 57, 317, 165, 18, Color.white);
            Button(postPanel.transform, "modify", "灵感 +2", 18, 233, 150, 40, () => world.Command(CommandKind.Modify));
            Button(postPanel.transform, "reroll", "灵感重掷", 180, 233, 150, 40, () => world.Command(CommandKind.Reroll));
            Button(postPanel.transform, "precision", "战技骰 d6 · 精准攻击", 18, 286, 312, 40, () => world.Command(CommandKind.Precision));
            Button(postPanel.transform, "commit", "接受结果并 Commit", 18, 343, 312, 44, () => world.ConfirmCurrentDecision());
            Button(postPanel.transform, "cancel", "放弃 Commit · 已付费用不返还", 18, 402, 312, 43, () => world.CancelCurrentDecision());

            menuPanel = Panel(root.transform, "Main menu", 310, 154, 660, 420, Ink);
            menuTitle = Label(menuPanel.transform, "战斗流程灰盒", 35, 28, 590, 42, 30, Teal);
            menuInfo = Label(menuPanel.transform, "固定3/4视角 · 一名 Fighter · 一名敌人\n\n近斩 / 格挡 / 闪避积累 Momentum。\nTab 进入专注，选择 Attack 或 Shove 并确认掷骰。\n结果出现后决策，Commit 入队，Tab 返回实时执行。\n\n[剧情待作者填写]", 35, 90, 590, 220, 20, Color.white);
            Button(menuPanel.transform, "new", "新战斗", 35, 339, 285, 48, () => { if (world.DungeonPresentation) world.StartDungeonNew(); else if (world.PartyPresentation) world.StartPartyNew(); else world.StartNew(); });
            Button(menuPanel.transform, "continue", "继续保存的战斗", 340, 339, 285, 48, () => world.ContinueSaved());

            pausePanel = Panel(root.transform, "Pause", 375, 194, 530, 337, Ink);
            Label(pausePanel.transform, "战斗已暂停", 32, 22, 466, 36, 28, Teal);
            Button(pausePanel.transform, "resume", "返回 · 恢复原时间状态", 32, 84, 466, 42, () => { if (world.FrontSettings) world.CloseFrontendSettings(); else world.Command(CommandKind.Resume); });
            Button(pausePanel.transform, "quick", "切换快速骰子动画", 32, 142, 466, 36, () => world.ConfigureQuickDice());
            Button(pausePanel.transform, "scale15", "专注 ×0.15", 32, 187, 148, 35, () => world.ConfigureFocus(.15f));
            Button(pausePanel.transform, "scale10", "专注 ×0.10", 191, 187, 148, 35, () => world.ConfigureFocus(.10f));
            Button(pausePanel.transform, "scale00", "辅助：暂停", 350, 187, 148, 35, () => world.ConfigureFocus(0));
            Button(pausePanel.transform, "save", "保存完整快照并返回标题", 32, 235, 466, 40, () => world.Suspend());
            Label(pausePanel.transform, "Esc 继续；保持已付费结果和原时间状态。", 32, 290, 466, 35, 16, Color.white);

            endPanel = Panel(root.transform, "Encounter result", 353, 215, 575, 285, Ink);
            endTitle = Label(endPanel.transform, "本次遭遇结束", 32, 26, 511, 43, 28, Teal);
            endInfo = Label(endPanel.transform, "可返回标题开始新尝试。", 32, 87, 511, 115, 19, Color.white);
            Button(endPanel.transform, "end", "保存结果 / 返回标题", 32, 217, 511, 43, () => world.Suspend());
            CreatePartyPanels(root.transform);
            CreateDungeonPanels(root.transform);
            CreatePolishPanels(root.transform);
            CreateFrontend(root.transform);
            CreateBasicTactics(root.transform);
            CreateDicePresentation();
            CreateP6Panels(root.transform);
            CreateV11Combat(root.transform);CreateV13(root.transform);
            HealthOverlay=gameObject.AddComponent<LowHealthOverlay>();HealthOverlay.Initialize(world);
        }

        float nextHudRefresh; CombatMode lastHudMode; bool lastHudMenu,lastDebug; int lastHudCommandCount=-1;
        void Update()
        {
            if(world==null||world.Session==null)return;
            // TickParty can publish an equivalent cloned DTO each frame. Reference identity
            // is not a UI change. TickCore also increments revision every simulation frame;
            // use accepted command receipts/modes for immediate refresh, live meters at 10 Hz.
            if(Time.unscaledTime>=nextHudRefresh||lastHudMode!=world.State.mode||lastHudMenu!=world.Menu||lastHudCommandCount!=world.State.receipts.Count||lastDebug!=world.DebugPresentation)
                RefreshView();
        }
        public void RefreshView()
        {
            if(world==null||world.Session==null)return;
            nextHudRefresh=Time.unscaledTime+.1f;lastHudMode=world.State.mode;lastHudMenu=world.Menu;lastHudCommandCount=world.State.receipts.Count;lastDebug=world.DebugPresentation;
            using (P46Performance.Measure(0)) RefreshViewMeasured();
        }
        public void RefreshViewMeasured()
        {
            if (world == null || world.Session == null) return;
            var state = world.State;
            var actor = state.Actor(state.pendingRoll?.actorId ?? state.activeReaction?.defenderId ?? (world.IsFocus ? world.SelectedActor : state.activeActorId)) ?? world.ActiveActor;
            resources.text = "战术次数  " + new string('◆', state.charges) + new string('◇', Mathf.Max(0, state.chargeCap - state.charges)) +
                "\n战机  " + Mathf.FloorToInt(state.momentum) + " / 100";
            stateLabel.text = ModeName(state.mode) + "\n世界时间 × " + (world.Menu ? "0" : world.Session.Scale.ToString("0.00"));
            message.text = world.Message;
            actorInfo.text = ActorName(actor.id) + "  /  " + (actor.Alive ? "存活" : "倒下") + "\nHP " + actor.hp + " / " + actor.maxHp + "   AC " + (actor.armorClass + actor.statuses.Where(s => s.remaining > 0).Sum(s => s.armorClassBonus)) +
                "\nMajor " + (string.IsNullOrEmpty(actor.heldBy) ? (actor.majorRemaining <= 0 ? "可用" : actor.majorRemaining.ToString("0.0") + "s") : "已占用") +
                "\nBonus " + Ready(actor.bonusRemaining) + "  Reaction " + (string.IsNullOrEmpty(actor.reactionHeldBy) ? Ready(actor.reactionRemaining) : "占用") +
                "\n灵感 " + actor.inspiration + (actor.role == ActorRole.Fighter ? "  战技骰 " + actor.superiority : actor.role == ActorRole.Mage ? "  法术位 " + actor.spellSlots : "") +
                "\n" + (actor.statuses.Any(s => s.id == "shield" && s.remaining > 0) ? "护盾生效 · AC 提升" : !string.IsNullOrEmpty(actor.concentrationId) ? "维持定身 · 受伤会中断" : actor.aimPreparing ? "正在瞄准 " + actor.aimRemaining.ToString("0.0") + "s" : actor.aimReady ? "稳准：下次投枪优势" : actor.guardRemaining > 0 ? "格挡中 · 移动减慢" : "实时攻击也使用 D20 对 AC");
            var target = state.Actor(world.SelectedTarget);
            enemyInfo.text = target == null ? "暂无有效目标" : "目标 / " + world.DisplayTarget(target.id) + "\nHP " + target.hp + " / " + target.maxHp + "   AC " + target.armorClass +
                "\nBreak " + target.breakValue.ToString("0") + " / " + target.breakMaximum.ToString("0") +
                (target.staggerRemaining > 0 ? "  失衡" : "") + "\n" + (target.aiState == 1 ? (target.attackSequence % 3 == 0 ? "圆形地火：留在范围需 DEX Save" : "锁定扇形：躲开 / 格挡") : "观察红色地面预警");
            var pending = state.pendingRoll;
            menuPanel.SetActive(world.Menu);
            pausePanel.SetActive(world.FrontSettings || !world.Menu && state.mode == CombatMode.CombatPaused);
            rollPanel.SetActive(false); // Kept as a compatibility field; the isolated dice canvas owns animation.
            postPanel.SetActive(!world.Menu && state.mode == CombatMode.PostRollDecision && pending != null && pending.purpose != "Reaction");
            focusPanel.SetActive(!world.Menu && state.mode == CombatMode.TacticalFocus);
            tacticalSpatialPanel.SetActive(!world.Menu && state.mode == CombatMode.TacticalFocus);
            tacticalSpatialText.text = world.TacticalSpatialSummary;
            endPanel.SetActive(!world.Menu && state.encounterResolved && state.pendingRoll == null && state.activeReaction == null);
            buttons["continue"].interactable = File.Exists(world.SavePath) || File.Exists(world.SavePath + ".bak");
            var exactQuote = world.SelectedActionQuote();
            var actionSpec = world.Session.Config.Action(world.SelectedAction);
            quote.text = target == null ? "点击战场选择存活敌人。" :
                !exactQuote.legal ? world.P5ActionName(world.SelectedAction) + " · " + ActorName(actor.id) + "\n" + HumanReason(exactQuote.reason) +
                    "\n射程 " + (actionSpec == null ? "—" : actionSpec.range.ToString("0.#") + "m") + " · 距离 " + BattleGeometry.DistanceXZ(BattleWorld.Position(actor), BattleWorld.Position(target)).ToString("0.0") + "m" :
                world.P5ActionName(world.SelectedAction) + " · " + ActorName(actor.id) + "\n" +
                (exactQuote.rollType == "Save" ? "敌人豁免 d20 + " : exactQuote.rollType=="Check"?"运动 d20 + ":"攻击 d20 + ") + exactQuote.bonus + " 对 " + (exactQuote.rollType == "Save"||exactQuote.rollType=="Check" ? "DC " : "AC ") + exactQuote.targetNumber +
                "\n射程 " + exactQuote.range.ToString("0.#") + "m · 距离 " + BattleGeometry.DistanceXZ(BattleWorld.Position(actor), BattleWorld.Position(target)).ToString("0.0") + "m" +
                "\n" + (exactQuote.sources.Count > 0 ? string.Join("、", exactQuote.sources.Select(SourceName)) : "标准检定") +
                "\n" + (exactQuote.legal ? "生效概率 " + (exactQuote.successProbability * 100).ToString("0.#") + "%" : HumanReason(exactQuote.reason));
            queueText.text = "队列：" + string.Join("、", state.queue.Where(q => q.stage == "Queued" || q.stage == "Executing").Select(q => ActorName(q.actorId)));
            buttons["cancel-queue"].interactable = state.queue.Any(q => q.actorId == world.SelectedActor && q.stage == "Queued");
            bool clearSight = target != null && world.HasLineOfEffect(BattleWorld.Position(actor), BattleWorld.Position(target));
            if (exactQuote.legal && !clearSight) quote.text += "\n墙体阻挡视线";
            buttons["confirm"].interactable = state.charges > 0 && exactQuote.legal && clearSight;
            if(world.P5Enabled)
            {
                quote.fontSize=14;
                if(world.SelectedAction=="control")quote.text+="\n"+world.P5ConeWarning;
                var choice=world.OpportunityChoices().FirstOrDefault(c=>c.id==world.SelectedOpportunity);
                quote.text+="\n"+(choice==null?"不用机会 · 1战术次数":BattleWorld.OpportunityName(choice.kind)+" → "+(choice.legal?choice.description:HumanReason(choice.reason))+" · 1次数＋机会");
                SetButtonTitle("shove","推撞 · 运动检定");SetButtonTitle("sneak","协同突袭");SetButtonTitle("control","雷鸣冲击 · 1法术位");
            }
            else {quote.fontSize=16;SetButtonTitle("shove","推击");SetButtonTitle("sneak","绕背突袭");SetButtonTitle("control","定身术 · 1 法术位");}
            buttons["attack"].GetComponent<Image>().color = world.SelectedAction == "attack" ? new Color(.1f,.35f,.35f) : new Color(.13f,.21f,.28f);
            buttons["shove"].GetComponent<Image>().color = world.SelectedAction == "shove" ? new Color(.1f,.35f,.35f) : new Color(.13f,.21f,.28f);
            foreach (var id in new[] {"attack","shove","sneak","control"})
                buttons[id].gameObject.SetActive(world.AvailableActions(actor.id).Contains(id));
            buttons["rogue"].gameObject.SetActive(world.PartyMode);
            buttons["mage"].gameObject.SetActive(world.PartyMode);
            if (pending != null)
            {
                // Cosmetic face is deterministic from animation progress; it never draws gameplay RNG.
                var animated = 1 + ((int)(world.RollElapsed * 45) % 20);
                diceText.text = pending.rawFaces.Count == 2 ? animated + "     " + (21 - animated) : animated.ToString();
                rollKindText.text = pending.advantage && !pending.disadvantage ? "▲ ▲ 优势：两骰取高" : pending.disadvantage && !pending.advantage ? "▼ ▼ 劣势：两骰取低" : "标准检定";
                bool reaction = pending.purpose == "Reaction";
                resultText.text = ActorName(pending.actorId) + (reaction ? " · 反应格挡" : pending.rollType == "Save" ? " · 敌方豁免" : pending.rollType=="Check"?" · 运动检定":" · 攻击") +
                    "\n原始骰 " + string.Join(" / ", pending.rawFaces) + " → 自然面 " + pending.selectedFace +
                    "\n总值 " + pending.total + " / " + (reaction ? "目标值 " : pending.rollType == "Save"||pending.rollType=="Check" ? "DC " : "AC ") + pending.targetNumber +
                    "\n" + (pending.hit ? (pending.critical ? "暴击" : reaction ? "可格挡本次攻击" : "行动将生效") : reaction ? "格挡未成功；将承受原伤害" : pending.rollType == "Save" ? "敌人抵抗成功" : pending.rollType=="Check" ? "运动检定未通过" : "护甲偏转") +
                    "\n" + string.Join("、", pending.modifierSources.Select(SourceName)) + "\n" + (reaction ? "反应已付；接受时消费占用的战技骰 1" : "已付 战术次数 " + pending.chargeSpent + "；起手消费行动/法术位");
                if(pending.p5!=null&&pending.stage=="AwaitDecision")resultText.text+="\n起手窗口 "+Mathf.Max(0,pending.p5.startDeadlineSimulationTime-state.simulationSeconds).ToString("0.0")+" 模拟秒";
                var rules = world.Session.Config.RulesFor(actor.id);
                bool correction = rules.nearMissCorrection && !actor.p4NearMissUsed && !pending.hit && pending.selectedFace != 1 && pending.targetNumber - pending.total > 0 && pending.targetNumber - pending.total <= rules.nearMissMaximumDeficit;
                SetButtonTitle("precision", correction ? "精准专精：战技骰 1 → 固定 +" + rules.nearMissBonus : "战技骰 d6 · 精准攻击");
                bool ownEdit = reaction || pending.rollType == "Attack" || pending.rollType=="Check";
                buttons["reroll"].interactable = actor.inspiration > 0 && !pending.rerollUsed && ownEdit;
                buttons["precision"].interactable = actor.superiority - actor.heldSuperiority > 0 && !pending.precisionUsed && pending.rollType == "Attack" && !reaction && actor.role == ActorRole.Fighter;
                buttons["modify"].interactable = actor.inspiration > 0 && !pending.modifyUsed && ownEdit;
            }
            RefreshPartyPanels();
            RefreshDungeonPanels();
            RefreshPolishPanels();
            RefreshFrontend();
            RefreshBasicTactics();
            RefreshP6Panels();
            RefreshV11Combat();RefreshV13();
            SetButtonTitle("quick", "骰子动画：" + world.DiceSpeedLabel + "  [点击切换]");
        }
        static string ModeName(CombatMode mode)
        {
            switch (mode)
            {
                case CombatMode.RealtimeCombat: return "实时战斗";
                case CombatMode.TacticalFocus: return "Tactical Focus";
                case CombatMode.RollFreeze: return "Roll Freeze";
                case CombatMode.PostRollDecision: return "骰后决策";
                case CombatMode.QueuedExecution: return "实时 / 队列执行";
                case CombatMode.ReactionPrompt: return "Reaction · 子弹时间";
                default: return "暂停";
            }
        }
        public static string HumanReason(string reason)
        {
            switch (reason)
            {
                case "core_locked_for_run": return "本局主控已固定；在战术界面选择连携能力";
                case "logical_member_has_no_body": return "连携队友没有常驻实体，请选择其已准备能力";
                case "correction_resource_or_reaction_unavailable": return "校正需要埃文灵感1、可用Reaction，并完成当前施作";
                case "correction_ineligible_roll": return "这次结果不能再校正；自然1、20仍按原规则";
                case "correction_already_prepared": return "校正已预备，等待下一次合格检定";
                case "prayer_empty": return "本局祈愿已用完；护生与普攻仍可用";
                case "physical_ally_required": return "只能作用于合法范围内的在场友军";
                case "fate_decision_only": return "先接受或替换当前潮汐结果";
                case "node_out_of_range_or_busy": return "节点超出主控锚点范围，或该角色仍在恢复";
                case "ability_cannot_hit_scene_object": return "此能力不能破坏祷歌节点；使用直接攻击";
                case "v12_chase_path_blocked": return "追身落点不可达：检查距离和墙体";
                case "v12_cooldown": return "基础技能正在冷却，可移动、普攻或指挥队友";
                case "hero_basic_cooldown": return "基础技能正在冷却；仍可移动和普通攻击";
                case "chase_destination_invalid": return "追身落点过远或被墙体遮挡";
                case "grove_destination_invalid": return "森息落点超出6米或被墙体遮挡";
                case "v12_point_invalid": return "区域落点超出范围或被墙体遮挡";
                case "p6_focus_reopen_cooldown": return "退出后需要2秒有效交战，仍可直接基础斩";
                case "focus_decision_time_insufficient": return "专注或本次窗口不足3秒，可预先接受基础结果";
                case "overcharge_requires_hit": return "只有已命中的结果可追加全力";
                case "bind_cooldown": return "裂光正在冷却，基础至圣斩仍可用";
                case "execution_position_illegal": return "起手位置或视线失效；基础费保留，未起手全力预留释放";
                case "p6_preset_required": return "当前旧存档保留原规则，新出发可用誓光大剑";
                case "practice_only": return "此操作只在独立训练可用";
                case "major_unavailable": return "该角色主要行动未就绪或已占用";
                case "target_out_of_range": return "目标超出射程";
                case "line_of_sight_blocked": return "视线被墙体阻挡";
                case "actor_unavailable": return "角色或目标已倒下";
                case "focus_budget_empty": return "专注时间耗尽；仍可使用免费基础指令与独立反应";
                case "fate_waiting_current_action": return "潮汐等待当前动作完成；稍后恢复新动作";
                case "no_charge": return "战术次数 不足";
                case "finish_pending_roll_first": return "请先 Commit 或放弃当前结果";
                case "cannot_modify_enemy_save": return "不能用我方修正修改敌方豁免";
                case "ok": return "已执行";
                case "actor_busy": return "该角色正在执行指令、闪避、格挡或失衡";
                case "sneak_position_required": return "需要移动到目标背后，才能发动突袭";
                case "sneak_requires_advantage_or_ally":
                case "sneak_eligibility_required": return "需要无最终劣势且有优势，或另一名队友在目标2米内牵制";
                case "opportunity_requires_other_actor": return "需要另一位队员接应；自己制造的机会不提供协同加成";
                case "opportunity_expired": return "机会已过期或对应减速已结束；请重新准备";
                case "opportunity_already_used": return "机会已被消费，不能重复使用";
                case "opportunity_wrong_target": return "该机会属于另一个目标，请重新选择";
                case "opportunity_missing":
                case "opportunity_required": return "先选择目标上的队友机会";
                case "opportunity_not_applicable": return "当前行动不能利用这个机会；可选择免费接应";
                case "action_start_expired": return "结果已超过2模拟秒起手窗口；已付次数与机会不退";
                case "ability_cooldown":
                case "action_cooldown": return "该行动正在冷却，可先下达免费指令";
                case "preparation_unavailable": return "准备冷却或正在防御/闪避，请稍后再试";
                case "basic_order_replacement_required": return "已有主要指令；请选择替换指令确认";
                case "followup_order_pending": return "已授权接应，将替代下一次普攻；可等待接近或取消指令";
                case "player_order_required": return "接应需要玩家明确授权";
                case "rear_destination_unreachable": return "后侧路径或落点被地形遮挡，请换位或换目标";
                case "order_destination_unreachable": return "落点超出20米或路径被地形阻挡";
                case "target_facing_unknown": return "目标尚无明确朝向；等待其锁定攻击方向";
                case "basic_order_replace_required": return "已有指令，请明确确认替换";
                case "select_reaction_first": return "先选择反应者、方案与受保护目标";
                case "role_action_forbidden": return "该角色没有此行动";
                case "long_term_resource_unavailable": return "法术位或道具不足，或已被其他指令占用";
                case "reaction_unavailable": return "该角色的反应未就绪或已占用";
                case "reaction_resource_unavailable": return "反应所需角色资源不足";
                case "reaction_window_unavailable": return "反应窗口已经结束";
                case "reaction_hold_lost": return "反应已失效，无法消费资源";
                case "shield_cannot_change_result": return "护盾无法挡住这次攻击";
                case "cannot_switch_active": return "当前时间状态不能切换主控";
                case "incoming_already_resolved": return "这次来袭已处理";
                case "realtime_required": return "请先返回实时状态";
                case "not_in_combat": return "遇到敌人后才能进入战术专注";
                case "combat_required": return "遇到敌人后才能使用此行动";
                default: return "当前条件尚未满足";
            }
        }
        GameObject Panel(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            Rect(obj.GetComponent<RectTransform>(), x,y,w,h); obj.GetComponent<Image>().color = color; return obj;
        }
        Text Label(Transform parent, string text, float x, float y, float w, float h, int size, Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            Rect(obj.GetComponent<RectTransform>(), x,y,w,h);
            var label = obj.GetComponent<Text>(); label.font = font; label.fontSize = size; label.color = color; label.text = text;
            label.alignment = align; label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
        Button Button(Transform parent, string id, string title, float x, float y, float w, float h, Action click)
        {
            var obj = Panel(parent, id, x,y,w,h, new Color(.13f,.21f,.28f)); var button = obj.AddComponent<Button>();
            button.targetGraphic = obj.GetComponent<Image>(); button.onClick.AddListener(() => { world.PresentationUICue("UiConfirm"); click(); });
            obj.AddComponent<PresentationButtonHover>().World = world;
            Label(obj.transform, title, 6, 3, w-12, h-6, 16, Color.white, TextAnchor.MiddleCenter);
            buttons.Add(id,button); return button;
        }
        static void Rect(RectTransform r, float x,float y,float w,float h)
        { r.anchorMin = r.anchorMax = new Vector2(0,1); r.pivot = new Vector2(0,1); r.anchoredPosition = new Vector2(x,-y); r.sizeDelta = new Vector2(w,h); }
    }
}
