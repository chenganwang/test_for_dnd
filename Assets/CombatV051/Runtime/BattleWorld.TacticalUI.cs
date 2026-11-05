using System;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        bool freeOrdersExpanded;
        public bool FreeOrdersVisible => freeOrdersExpanded && !Menu && !IsFrozen && !IsFocus && State.inCombat && State.activePendingEvent==null;
        public void ToggleFreeOrders() { freeOrdersExpanded=!freeOrdersExpanded; CancelBasicOrderSelection(); Hud.RefreshView(); }
        public bool MoveOrderArmed { get; private set; }
        public string PendingOrderLabel { get; private set; }
        Func<bool, CombatResult> pendingBasicOrder;
        string pendingBasicActor;

        public void ChooseBasicTactic(string kind)
        {
            if (Menu || State.mode != CombatMode.TacticalFocus && !FreeOrdersVisible) return;
            if (kind == "move") { MoveOrderArmed = true; Message = "点击可达地面，给所选队员下达移动；不会消耗战术次数。"; return; }
            MoveOrderArmed = false;
            if (kind == "rear")
            {
                if (!TryGetRearDestination(out var destination, out var reason)) { Message = reason; return; }
                RequestBasicOrder(replace => IssueMoveOrder(destination, replace), "移动到合法后侧");
            }
            else if (kind == "hold") RequestBasicOrder(IssueHoldOrder, "守位");
            else if (kind == "target") RequestBasicOrder(replace => IssueFocusTargetOrder(SelectedTarget, replace), "集中当前目标");
            else if (kind == "follow") RequestBasicOrder(IssueFollowOrder, "跟随主控");
            else if (kind == "prepare") RequestBasicOrder(IssuePreparationOrder, "职业准备");
            else if (kind == "followup") RequestBasicOrder(IssueFollowupOrder, "免费接应");
            else if (kind == "replace")
            {
                if (pendingBasicActor != SelectedActor) { CancelBasicOrderSelection(); Message = "角色已切换，请重新选择指令。"; return; }
                var command = pendingBasicOrder; CancelBasicOrderSelection(); command?.Invoke(true);
            }
            else if (kind == "cancel") { CancelBasicOrderSelection(); CancelSelectedOrder(); }
            Hud.RefreshView();
        }
        void RequestBasicOrder(Func<bool, CombatResult> issue, string label)
        {
            if (CombatSession.HasPlayerOrder(State.Actor(SelectedActor)))
            {
                pendingBasicOrder = issue; pendingBasicActor = SelectedActor; PendingOrderLabel = label;
                Message = "该队员已有主要指令。点击“替换指令”确认覆盖，或取消。";
            }
            else { CancelBasicOrderSelection(); issue(false); }
        }
        void CancelBasicOrderSelection()
        { pendingBasicOrder = null; pendingBasicActor = null; PendingOrderLabel = null; MoveOrderArmed = false; }
        bool HandleTacticalGroundClick()
        {
            if (!MoveOrderArmed) return false;
            Vector3 destination = WorldAimPoint;
            MoveOrderArmed = false;
            RequestBasicOrder(replace => IssueMoveOrder(destination, replace), "移动到新落点");
            return true;
        }
    }
}
