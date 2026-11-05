# P4.6 Focus budget / Reaction limit

## Runtime contract

- Use existing `ProfileSession.Tick(realDelta, decisionUiAvailable)` / `CombatSession.Tick(realDelta, decisionUiAvailable)`. `realDelta` is unscaled active frame time. Pass false when the decision UI cannot receive input, including loss of focus. Explicit Pause and RollFreeze exclude both decision clocks internally.
- Read `State.focusBudget.secondsRemaining`, `secondsSpent`, `secondsRecovered`, `secondsDiscarded`, `entryCount`, `entryDrainMultiplier`, `activeSecondsSinceExit`, `recoveryMultiplier`, `active`, `rewardRoots`. Do not update them from Runtime.
- `EnterFocus` rejects with `focus_budget_empty` at zero. Free basic orders retain their existing realtime legality. `ExitFocus` stops active spending. Re-entry before 1.2 active seconds applies 2x spending for that entry. Reaction UI never spends the active Focus budget.
- `SelectCommandActor` accepts TacticalFocus or NormalMode (RealtimeCombat / QueuedExecution) when there is no pending roll or active Reaction event. It selects a living party command recipient, preserving the active controlled actor, queues, costs and time scale. This enables the realtime free-order panel at zero budget.
- Selection and ordinary tactical PostRoll spend real time. Exhaustion exits to RealtimeCombat or QueuedExecution. An uncommitted paid tactical ticket becomes Canceled and releases its reservations, without refunding Charge/Inspiration/already paid resources. Committed queues remain intact.
- RollFreeze is never shortened. A tactical reveal at zero displays PostRollDecision for 0.15 active seconds before the default cancel. Do not immediately auto-Cancel/Exit on Reveal from Runtime. The domain handles it on Tick.
- `LastTickError` remains the failure signal. On a failed deadline save, the entire pre-tick state, budget, paid ticket, RNG, world and file authority are restored. Runtime must not run the returned zero-duration world step.

## Reaction contract

- Read `State.reactionOpportunityRoots.Count`; limit is `FocusBudgetRules.ReactionOpportunitiesPerEncounter` (6 shared events per encounter). Both manual and automatic slowtime entrances use this pool; multiple defenders / option changes within one event use one entry. This is separate from actor Reaction cooldown, Charge and Focus budget.
- At the limit, new attacks resolve their original result without opening a new decision event. Existing accounted-for events finish normally. Manual `BeginReaction` cannot bypass the cap (`reaction_opportunities_empty`). Save/load and SetReactionSettings never refill opportunities.
- `ReactionSettings.decisionSeconds`: >0 and <=2.5. `pauseDecision` must be false. `timeoutAcceptPaidRoll` defaults false; true explicitly accepts an already paid, still legal result on timeout. Default timeout declines; paid Reaction and paid roll edits stay spent. Class-resource holds are released unless committed.
- The event's `remainingUiSeconds` is not reset by selections, settings changes, rerolls or load. Freeze, explicit Pause and unavailable UI do not decrement it.

## Tuning / persistence

Central entry: `FocusBudgetRules` in `Domain/FocusBudget.cs`. Current constants: initial 8, cap 12, reentry window 1.2, multiplier 2, registered threat-kill recovery .75, real critical-defense recovery .5, FOCUS relic recovery multiplier 1.25, Reaction 2.5, shared opportunities 6. **Changing these constants requires recompilation.** No new CombatConfig/DungeonConfig fields invalidate historical configuration hashes.

Only real successful actions award budget: registered high-threat kills, or useful defense against important/lethal/registered dangerous attacks. Recovery deduplicates original source IDs. Full-cap overflow is recorded as discarded and cannot be claimed later. Reaction-origin defenses cannot generate budget.

Each CombatState owns its budget. Profile Activate clones exploration budget; Resolve transfers the completed encounter budget back. Same-run room changes/continue do not refill. Story and Challenge have independent states. New runs start at 8. RetryStory restores the entire original encounter-start snapshot, including its original budget and source records. The existing FOCUS relic modifies eligible future recovery; equipping it gives no seconds by itself.

Pre-budget files without `focusBudget` gain the documented 8-second default at deserialization; explicit null is rejected. Their old >2.5 Reaction timers clamp to 2.5, `pauseDecision` becomes false, existing event history consumes up to six opportunities, and autoMode is preserved. Resources, paid tickets, random streams, snapshots, configuration hashes and source bytes are unchanged on Load; the next normal atomic Save writes the new fields. Current files do not receive this migration again.

## Existing-test changes required by new rules

- P4 `DecisionBudgetUsesVisibleUiSecondsAndExcludesFreezePauseAndFocusLoss`: assertions use 2.5-second initial time, keeping freeze/pause/unavailable-input checks.
- Paid-timeout acceptance/fault tests explicitly opt in through `timeoutAcceptPaidRoll=true`; a new test covers default decline and retained payment.
- Old `PauseDecisionStopsWorldButKeepsSixActiveUiSeconds` becomes `PauseDecisionCannotBypassTheTwoPointFiveSecondDecisionCap`, because that assist conflicts with the new hard cap.
- `ExecutingMajorCanResolveDuringReactionWithoutClearingReactionHold` advances .2 realtime seconds before offering the reaction and 2 slow UI seconds after, so it tests concurrent holds within the new limit.
- `PassiveUsesSimulationSecondsAndFreezeDoesNotAccumulate` samples 5 seconds (2 momentum) within the initial 8-second budget instead of assuming unlimited Focus for 10 seconds.

Evidence remains DOTNET_RULE until the parent runs actual Unity EditMode/PlayMode/Player checks.

## P4.6.3 决策时钟分离

- `CombatSession.Tick(float realDelta, bool decisionUiAvailable, float? decisionDelta = null)` 与 `ProfileSession.Tick(float realDelta, bool decisionUiAvailable = true, float? decisionDelta = null)` 接受独立决策 delta。省略时沿用 `realDelta`，旧单参／双参调用不变。
- 世界模拟、冷却、战斗与模式活动统计仍只读 `realDelta`。Focus 消耗／重入间隔／已付结果截止及 Reaction UI 截止只读 `decisionDelta`；不可交互、暂停与 RollFreeze 仍不扣决策时间。两种 delta 均须有限且非负，非法参数在任何活动统计变更前拒绝。
- Runtime 使用 Stopwatch 单调时钟按 mode、modeInstance、encounter、Reaction 事件分段。成功命令完成或 Tick 内自动模式变化后才建立新段起点；同模式内选角色、同事件换方案、骰后编辑不重置段。Freeze／菜单／暂停／失焦期间停钟。
- 此接口不增加存档字段，不改变配置哈希；截止写盘仍先验证和提交候选，写入失败恢复原预算、票、资源、世界与时钟统计。
