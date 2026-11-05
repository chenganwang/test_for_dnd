using System;
using System.Globalization;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        const string TacticalPrefix = "tacticalPreview/";
        Transform tacticalPreviewRoot;
        AreaPreview tacticalCurrent, tacticalTarget, tacticalRange, tacticalDestination;
        LineRenderer tacticalPath;
        TacticalPreviewFrame tacticalFrame = new TacticalPreviewFrame();

        sealed class TacticalPreviewFrame
        {
            public bool visible, hasTarget;
            public string actorId = "", targetId = "", commandTargetId = "", actionId = "", summary = "";
            public AreaShape current, target, range, destination;
            public Vector3 pathStart, pathEnd;
            public Vector3[] pathPoints;
        }

        public string TacticalSpatialSummary => tacticalFrame.visible ? tacticalFrame.summary : "";

        /// <summary>Read-only projection of the selected actor's actual state and retained action contract.</summary>
        public void RefreshTacticalPreview() { using (P46Performance.Measure(3)) RefreshTacticalPreviewMeasured(); }
        public void RefreshTacticalPreviewMeasured()
        {
            if (IsFrozen || Session == null) return;
            // An incoming reaction is not a new movement plan. In particular, a reaction
            // offered from real time must not manufacture a Focus preview on reveal/load.
            if (State.mode == CombatMode.ReactionPrompt ||
                (State.mode == CombatMode.PostRollDecision && State.pendingRoll?.purpose == "Reaction"))
            { if(tacticalPreviewRoot)tacticalPreviewRoot.gameObject.SetActive(false);return; }
            if (State.mode != CombatMode.TacticalFocus && State.mode != CombatMode.PostRollDecision)
            {
                tacticalFrame.visible = false;
                if (tacticalPreviewRoot) tacticalPreviewRoot.gameObject.SetActive(false);
                return;
            }

            var actor = State.Actor(SelectedActor);
            if (actor == null || !actor.Alive)
            {
                tacticalFrame.visible = false;
                if (tacticalPreviewRoot) tacticalPreviewRoot.gameObject.SetActive(false);
                return;
            }
            var queued = State.queue.FirstOrDefault(q => q.actorId == actor.id && (q.stage == "Queued" || q.stage == "Executing"));
            var ticket = queued == null ? null : State.rolls.FirstOrDefault(t => t.id == queued.ticketId);
            // A queued roll owns a frozen spec; changing the menu/config cannot rewrite it.
            var spec = ticket?.spec ?? (queued == null ? Session.Config.Action(SelectedAction) : null);
            string targetId = queued != null ? queued.targetId : V12Enabled||actor.id == State.activeActorId ? SelectedTarget : actor.aiTargetId;
            var target = State.Actor(targetId);
            bool hasTarget = target != null && target.Alive;
            Vector3 current = Position(actor), expected = current;
            float range = Mathf.Max(0, spec?.range ?? 0);
            string orderPlan=null;
            if(queued==null && CombatSession.HasPlayerOrder(actor))
            {
                var order=actor.basicOrder;
                expected=order.kind==BasicOrderKind.Move || order.kind==BasicOrderKind.Prepare&&actor.role==ActorRole.Rogue ? new Vector3(order.x,actor.heightY,order.z) : current;
                orderPlan=SelectedOrderSummary;
                if(order.kind==BasicOrderKind.FocusTarget){targetId=order.targetId;target=State.Actor(targetId);hasTarget=target!=null&&target.Alive;}
            }
            else if (queued?.stage == "Queued" && hasTarget && spec != null)
            {
                Vector3 direction = BattleGeometry.Ground(Position(target) - current);
                float distance = direction.magnitude;
                // Match AdvanceQueue's entry distance, capsule radius and collision mask.
                float travel = distance > range ? Mathf.Max(0, distance - range + .01f) : 0;
                expected = BattleGeometry.MoveAgainstWalls(current, direction.normalized * travel, .36f, 1 << WallLayer);
            }
            else if (queued == null && actor.id != State.activeActorId)
                expected = new Vector3(actor.aiMoveX, actor.heightY, actor.aiMoveZ);

            if(queued==null&&!CombatSession.HasPlayerOrder(actor)&&SelectedAction=="sneak")
            {
                if(TryGetRearDestination(out var rear,out var rearReason)){expected=rear;orderPlan="亮圈是可到达侧后位置；选择侧翼准备后真实移动，到位再检定";}
                else orderPlan=rearReason=="target_out_of_range"?"目标过远，先移动接近":rearReason=="target_facing_unknown"?"等待目标明确朝向":"后侧被地形遮挡，当前没有可到达落点";
            }

            string plan = queued == null ? (actor.id == State.activeActorId ? "当前无移动计划" : "队友当前移动意图") :
                queued.stage == "Executing" ? "动作已起手" : "队列接近射程";
            if(orderPlan!=null)plan=orderPlan;
            if (queued != null && hasTarget && !HasLineOfEffect(current, Position(target))) plan += "；目标遮挡，执行时会取消";
            string targetLabel = (queued != null ? "队列目标 " : actor.id == State.activeActorId ? "指令目标 " : "当前AI目标 ") + (hasTarget ? DisplayTarget(target.id) : "无");
            if (SelectedTarget != targetId) targetLabel += " · 指令 " + DisplayTarget(SelectedTarget);
            string actionId = ticket?.actionId ?? SelectedAction ?? "";
            tacticalFrame = new TacticalPreviewFrame
            {
                visible = true, hasTarget = hasTarget, actorId = actor.id, targetId = hasTarget ? target.id : "", commandTargetId = SelectedTarget ?? "", actionId = actionId,
                current = AreaShape.Circle(current, .42f), target = AreaShape.Circle(hasTarget ? Position(target) : current, .58f),
                range = AreaShape.Circle(current, range), destination = AreaShape.Circle(expected, .52f),
                pathStart = current + Vector3.up * .055f, pathEnd = expected + Vector3.up * .055f,
                pathPoints = queued==null ? OrderPreviewPath(actor,expected) : new[]{current+Vector3.up*.055f,expected+Vector3.up*.055f},
                summary = TacticalActorLabel(actor) + (DebugPresentation ? " · 当前 " + TacticalCoordinates(current) + " → 预计 " + TacticalCoordinates(expected) : " · "+(CombatSession.HasPlayerOrder(actor)?"玩家指令优先":"可下达免费基础指令")) +
                    "\n" + targetLabel + " | " + TacticalActionLabel(actionId) + " 射程 " + range.ToString("0.#", CultureInfo.InvariantCulture) + "m" +
                    "\n" + plan + " · 实圈=当前，浅圈/线=预计，细圈=射程"
            };
            if(V12Enabled&&SelectedAction=="v12_finn_grove"&&State.pendingRoll==null)
            {
                tacticalFrame.target=AreaShape.Circle(WorldAimPoint,Session.Config.v12.groveRadius);tacticalFrame.hasTarget=true;
                tacticalFrame.summary="森息 · 点击战场选择区域中心；确认支付1次，不进行攻击检定。";
            }
            if(V12Enabled&&SelectedAction=="v12_mira_chase"&&hasTarget&&queued==null)
            {
                bool legal=TryV12ChaseDestination(actor,target,out var landing);
                tacticalFrame.destination=AreaShape.Circle(landing,.45f);tacticalFrame.pathPoints=new[]{current+Vector3.up*.055f,landing+Vector3.up*.055f};
                tacticalFrame.summary=legal?"追身斩 · 浅圈是实际落点；移动后攻击，MISS不回退位移。":"追身斩 · 距离过远或路径遮挡；不会隔墙起手。";
            }
            ApplyTacticalFrame();
        }

        /// <summary>Called at the world's existing persistence boundary; writes only presentation keys.</summary>
        public void CaptureTacticalPreviewSnapshot()
        {
            if (Session == null) return;
            var numbers = State.world.numbers;
            if (IsFrozen && numbers.ContainsKey(TacticalPrefix + "version")) return;
            numbers[TacticalPrefix + "version"] = 1;
            numbers[TacticalPrefix + "visible"] = tacticalFrame.visible ? 1 : 0;
            numbers[TacticalPrefix + "hasTarget"] = tacticalFrame.hasTarget ? 1 : 0;
            CaptureTacticalCircle("current", tacticalFrame.current);
            CaptureTacticalCircle("target", tacticalFrame.target);
            CaptureTacticalCircle("range", tacticalFrame.range);
            CaptureTacticalCircle("destination", tacticalFrame.destination);
            CaptureTacticalPoint("lineStart", tacticalFrame.pathStart);
            CaptureTacticalPoint("lineEnd", tacticalFrame.pathEnd);
            var points=tacticalFrame.pathPoints??new[]{tacticalFrame.pathStart,tacticalFrame.pathEnd};
            numbers[TacticalPrefix+"pathCount"]=points.Length;
            for(int i=0;i<points.Length;i++)CaptureTacticalPoint("path/"+i,points[i]);
            var strings = State.world.strings;
            strings[TacticalPrefix + "actor"] = tacticalFrame.actorId;
            strings[TacticalPrefix + "target"] = tacticalFrame.targetId;
            strings[TacticalPrefix + "commandTarget"] = tacticalFrame.commandTargetId;
            strings[TacticalPrefix + "action"] = tacticalFrame.actionId;
            strings[TacticalPrefix + "summary"] = tacticalFrame.summary;
        }

        /// <summary>Restore the saved geometry verbatim, including an explicitly invisible real-time frame.</summary>
        public void RestoreTacticalPreviewSnapshot()
        {
            if (Session == null) return;
            var numbers = State.world.numbers;
            if (!numbers.TryGetValue(TacticalPrefix + "version", out float version) || version != 1)
            {
                tacticalFrame = new TacticalPreviewFrame();
                if (tacticalPreviewRoot) tacticalPreviewRoot.gameObject.SetActive(false);
                return;
            }
            tacticalFrame = new TacticalPreviewFrame
            {
                visible = TacticalNumber("visible") > .5f, hasTarget = TacticalNumber("hasTarget") > .5f,
                actorId = TacticalString("actor"), targetId = TacticalString("target"), commandTargetId = TacticalString("commandTarget"), actionId = TacticalString("action"),
                summary = TacticalString("summary"), current = RestoreTacticalCircle("current"), target = RestoreTacticalCircle("target"),
                range = RestoreTacticalCircle("range"), destination = RestoreTacticalCircle("destination"),
                pathStart = RestoreTacticalPoint("lineStart"), pathEnd = RestoreTacticalPoint("lineEnd")
            };
            int count=Mathf.Clamp(Mathf.RoundToInt(TacticalNumber("pathCount")),0,2202);
            if(count>=2)tacticalFrame.pathPoints=Enumerable.Range(0,count).Select(i=>RestoreTacticalPoint("path/"+i)).ToArray();
            ApplyTacticalFrame();
        }

        void ApplyTacticalFrame()
        {
            if (!tacticalFrame.visible && !tacticalPreviewRoot) return;
            EnsureTacticalObjects();
            tacticalCurrent.SetShape(tacticalFrame.current);
            tacticalRange.SetShape(tacticalFrame.range);
            tacticalDestination.SetShape(tacticalFrame.destination);
            tacticalTarget.SetShape(tacticalFrame.target);
            tacticalTarget.gameObject.SetActive(tacticalFrame.hasTarget);
            var points=tacticalFrame.pathPoints??new[]{tacticalFrame.pathStart,tacticalFrame.pathEnd};
            tacticalPath.positionCount=points.Length;tacticalPath.SetPositions(points);
            tacticalPreviewRoot.gameObject.SetActive(tacticalFrame.visible);
        }

        void EnsureTacticalObjects()
        {
            if (tacticalPreviewRoot) return;
            tacticalPreviewRoot = new GameObject("Tactical spatial preview").transform;
            tacticalPreviewRoot.SetParent(transform, false);
            tacticalCurrent = CreateTacticalCircle("Current position", new Color(.8f, 1, .92f, .95f), .06f);
            tacticalRange = CreateTacticalCircle("Action range", new Color(.2f, .82f, .88f, .62f), .025f);
            tacticalDestination = CreateTacticalCircle("Expected position", new Color(.78f, .88f, 1, .7f), .04f);
            tacticalTarget = CreateTacticalCircle("Current target", new Color(1, .72f, .32f, .82f), .035f);
            var path = new GameObject("Expected movement"); path.transform.SetParent(tacticalPreviewRoot, false);
            tacticalPath = path.AddComponent<LineRenderer>(); tacticalPath.useWorldSpace = true;
            tacticalPath.loop = false; tacticalPath.positionCount = 2;
            tacticalPath.startWidth = tacticalPath.endWidth = .025f;
            tacticalPath.sharedMaterial = BattleVisual.NewUnlitMaterial(new Color(.78f, .88f, 1, .6f));
            path.AddComponent<SpatialResourceOwner>().OwnedMaterial = tacticalPath.sharedMaterial;
        }

        AreaPreview CreateTacticalCircle(string label, Color color, float width)
        {
            var item = new GameObject(label); item.transform.SetParent(tacticalPreviewRoot, false);
            var preview = item.AddComponent<AreaPreview>(); preview.Color = color; preview.Width = width; preview.GroundLift = .045f;
            return preview;
        }

        static string TacticalCoordinates(Vector3 p) => "(" + p.x.ToString("0.0", CultureInfo.InvariantCulture) + ", " + p.z.ToString("0.0", CultureInfo.InvariantCulture) + ")";
        static string TacticalActorLabel(ActorState actor) => V11Catalog.FindCharacter(actor.id)?.displayName ?? (actor.role == ActorRole.Fighter ? "战士" : actor.role == ActorRole.Rogue ? "游荡者" : actor.role == ActorRole.Mage ? "法师" : actor.id);
        static string TacticalActionLabel(string id) => id!=null&&id.StartsWith("v12_")?V11AbilityCatalog.Name(id):id == "attack" ? "投枪" : id == "shove" ? "推击" : id == "sneak" ? "偷袭" : id == "control" ? "定身" : id;
        void CaptureTacticalCircle(string key, AreaShape shape)
        { CaptureTacticalPoint(key, shape.Origin); State.world.numbers[TacticalPrefix + key + "/radius"] = shape.Radius; }
        void CaptureTacticalPoint(string key, Vector3 p)
        {
            var numbers = State.world.numbers;
            numbers[TacticalPrefix + key + "/x"] = p.x; numbers[TacticalPrefix + key + "/y"] = p.y; numbers[TacticalPrefix + key + "/z"] = p.z;
        }
        float TacticalNumber(string key) => State.world.numbers.TryGetValue(TacticalPrefix + key, out float value) ? value : 0;
        string TacticalString(string key) => State.world.strings.TryGetValue(TacticalPrefix + key, out string value) ? value : "";
        Vector3 RestoreTacticalPoint(string key) => new Vector3(TacticalNumber(key + "/x"), TacticalNumber(key + "/y"), TacticalNumber(key + "/z"));
        AreaShape RestoreTacticalCircle(string key) => AreaShape.Circle(RestoreTacticalPoint(key), TacticalNumber(key + "/radius"));
    }
}
