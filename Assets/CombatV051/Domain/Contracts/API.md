# P0 Domain API（实现合同，2026-09-16）

Namespace `CombatV051.Domain`。DTO为唯一游戏权威；Runtime不要缓存跨Execute的Actor引用，成功事务会替换State，使用`session.State.Actor(id)`重新获取。

```csharp
var config = new CombatConfig();
var state = CombatState.Create("profile-1", "campaign-p0", 123UL);
var session = new CombatSession(state, config, optionalAbsoluteSavePath);
CombatResult r = session.Execute(new CombatCommand { id="stable-id", kind=CommandKind.EnterFocus });
float simulationDelta = session.Tick(realDelta); // Freeze/Paused返回0，Focus按config.focusScale
session.Save(); // CombatResult，持久保存完整State
var loaded = CombatSession.Load(absoluteSavePath, config); // 完整当前快照，不回起点
```

`CombatState.Create`给Fighter和一个Enemy，XZ位置，Charge1/Momentum0。`State.actors`玩家成员1–3，`State.enemies`敌人；`State.Actor(id)`查两边。`State.activeActorId`默认`fighter`；敌人ID`enemy-1`。`State.mode`为`CombatMode`，`session.Scale`当前时间倍率。模式：RealtimeCombat/TacticalFocus/RollFreeze/PostRollDecision/QueuedExecution/CombatPaused。

统一命令字段：`id, kind, actorId="fighter", targetId="enemy-1", actionId="attack", expectedRevision=-1, ticketId, queueId, flag, factId, rootId, templateId, amount, qualified, lineOfSight=true, x,z, dc=12`。id必须稳定；相同id+载荷返回同结果，相同id换载荷拒绝；expectedRevision=-1表示即时重新验证，否则严格比较。命令绑定当前Session；保存路径同时验证profile/mode身份，不得跨会话复用命令实例。

命令枚举：`EnterFocus, ExitFocus, ConfirmRoll, RevealRoll, Reroll, Modify, Precision, Commit, Cancel, BasicAttack, StartAim, CancelAim, StartGuard, EndGuard, StartDodge, AwardFact, StartQueued, ResolveQueued, CancelQueued, Pause, Resume, EndEncounter, IncomingAttack, IncomingSave`。

- `ConfirmRoll`需要Focus。actionId=`attack`或`shove`；actor/target合法、Major可用、距离合法、Charge≥1才确认；结果ticketId在`r.ticketId`，`State.pendingRoll`是该票。Attack原始面属于Fighter；Shove是敌方STR Save。Attack自然1失败/20暴击，Save只看总值。
- `RevealRoll(ticketId)`由0.4–0.8秒unscaled UI动画结束发送；从Freeze回PostRoll，绝不抽样。
- `Reroll(ticketId)`花发令Actor的1 Inspiration，P0每票一次，重新进入RollFreeze，UI再次Reveal后回PostRoll；`Modify(ticketId)`花同池1 Inspiration加2总值、每票一次；`Precision(ticketId)`花1 Superiority并掷d6加总值，每票一次。三者均只允许自己的Attack，不能改敌方Save；取消不退上述资源/Charge。Modify/Precision不改变selectedFace或自然1/20。
- `Commit(ticketId)`仅排队，返回queueId，回Focus；`Cancel(ticketId)`释放holds并回Focus。`ExitFocus`有未处理pendingRoll时拒绝，否则实时/队列模式。
- `StartQueued(queueId)`在Realtime/Queued合法起手才扣Major及长期资源，设置windup；`ResolveQueued(queueId)`在windup结束后由Runtime提交一次。目标移动先由Runtime用DTO移动到范围，不能开始不合法动作。失效目标可`CancelQueued(queueId)`，释放未消费holds，Charge不退。Confirm/Basic/Start/Resolve使用Runtime提供的真实lineOfSight。Shove Resolve的`qualified=true`表示Runtime已经碰撞裁剪，`x/z`为目标最终合法位置，域校验位移不超过spec.shoveDistance并在同一保存事务应用；纯规则测试可不传该标志使用完整位移。起手后目标被阻挡/移出射程会取消而不退已消费长期资源。
- `BasicAttack(actorId,targetId)`在实时可用Major/射程合法时执行后台D20和伤害，不扣Charge、不改时间。结果`r.rawFaces/r.total/r.hit/r.critical/r.damage`。敌人/未来AI普通Attack同入口；`flag=true`表示AI请求，规则禁止AI ConfirmRoll/骰后资源等贵重操作。
- `StartAim`静止准备1模拟秒，Tick完成给下一次相关战术远程Attack优势+Momentum；Runtime在真实移动时调用`CancelAim`，准备期间不能闪避。`StartGuard`有持续时间、减少移动，`AwardFact`的`factId/rootId/templateId/qualified`由Runtime证明实际挡到威胁；`actionId`事实类型为`basic-hit/perfect-dodge/parry/weakpoint/flank/interrupt/environment/threat-kill/guard`。
- `StartDodge`占基础dodgeCooldown与dodgeRemaining，若Aim中拒绝。方向通过命令`x/z`输入，成功事务内部规范化写入Actor.lockedX/lockedZ；零向量默认(0,1)，非有限方向拒绝。Runtime不得确认前修改locked字段。世界伤害/Save使用`ResolveIncoming(string commandId,string attackerId,string defenderId,int damage,bool savingThrow=false,int dc=12,string rootId=null)`返回CombatResult。Runtime先用真实范围/LOS证明危险相交，域内检查Dodge/Guard、后台Attack/Save、伤害、根去重；不应Runtime直接扣HP。受到实际伤害清除Aim；成功跑出AoE不调用此接口，因而不掷Save。

自愿StartAim/StartGuard/StartDodge在heldBy非空、windupRemaining>0或staggerRemaining>0时统一拒绝`actor_busy`。若要放弃队列再自由移动，先显式CancelQueued；已花费用遵原政策。普通Major冷却本身不阻止合法闪避。StartQueued也拒绝活跃Aim/Guard/Dodge，防旧保存组合或同帧输入同时授予保护和攻击起手。

Actor字段见`CombatModel.cs`；含`id,isEnemy,x,z,heightY,hp,maxHp,armorClass,attackBonus,saveBonus,proficiency,strengthModifier,majorRemaining,bonusRemaining,reactionRemaining,heldBy,inspiration,superiority,spellSlots,items,heldSpellSlots,heldItems,aimRemaining,aimPreparing,aimReady,guardRemaining,guardElapsed,dodgeRemaining,dodgeCooldown,windupRemaining,aiState,aiRemaining,attackSequence,attackGroupId,attackTargetId,lockedX,lockedZ,breakValue,breakMaximum,staggerRemaining,statuses`。`Alive => hp>0`。移动、AI意图、预警/弹体位置与计时由Runtime写DTO；通用冷却/状态/Aim/Guard/Dodge及queue.windupRemaining由Tick统一推进。

`State.queue`为QueuedAction列表，字段`id,ticketId,actorId,targetId,actionId,stage,windupRemaining,resourcesSpent,effectApplied,executionId,reason`；stage为`Queued/Executing/Resolved/Canceled`字符串。`State.rolls`存全部票据，`pendingRoll`按pendingRollId查同一列表成员。票还含baseBonus、flatBonus、modifyUsed、spec动作快照和付费收据，已入队动作不会受热改ActionSpec价格影响。

P0存档schema独立、JSON+SHA256+临时Flush+原子replace+备份；不读写旧v04档。自动有路径时所有Execute成功都持久保存（包括世界伤害事务），失败不发布候选。Tick/移动会更新运行状态，下一持久事务包含它们；定期Save可保存当前完整世界。存档恢复模式/已付费票/队列/双方AI与位置/RNG全部原样。UI不能先播放结果再检查Execute.success。

已有保存文件必须`Load`，新CombatSession不能偷偷覆盖；新尝试由调用方归档旧文件后创建。Store.Load采用main→bak，忽略tmp。每个Store还比较上次已提交修订+摘要，拒绝另一个已加载会话覆盖较新的权威。`Store.WriteFault`支持BeforeWrite/AfterFlush/BeforeReplace/AfterReplace/AfterVerify故障测试。

`session.Quote(actorId,targetId,actionId)`返回ActionQuote（legal/reason、rollOwnerId、rollType、bonus、targetNumber、range、spellCost/itemCost、advantage/disadvantage、sources、successProbability），无副作用。API中的Save型probability/hit指战术Shove成功，即敌人Save失败；IncomingSave的reason则明确`save_success_half`或`save_failed`。

Config精确字段在CombatConfig.cs，根可加载`CombatConfig.json`。默认focusScale=.15（辅助设置允许0或.10–.15）、Major1.2秒、Bonus4秒、Reaction6秒、基础范围2.2/伤害2、Aim1秒、Guard1.4秒/Parry前.3秒、Dodge.3秒/CD1秒；passive2/秒、每遭遇passive上限120、总Momentum收益上限300。有效战斗要求12m内有存活玩家与有意义敌人；唯一敌人≤25%HP停止补给，死亡/远离不补给。上述默认数据是P0实施决定，不声称已平衡。

`Tick(realDelta)`仅推进世界模拟和通用计时；realCombatSeconds是独立局长统计，除CombatPaused外按真实时间增长，RollFreeze/Focus0也增长。验证世界Freeze时排除此统计并单独断言；不能用它推动AI或冷却。P0没有Reaction主动流程或三人AI，相关资源/容器仅为后续接口。
