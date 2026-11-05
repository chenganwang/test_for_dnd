using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        const string EnemyPatternKey = "enemyPattern/";
        const string EnemyContactGroupKey = "enemyContactGroup/";
        const string EnemyContactsKey = "enemyContacts/";

        public void AdvanceDungeonEnemies(float dt) { using (P46Performance.Measure(4)) AdvanceDungeonEnemiesMeasured(dt); }
        public void AdvanceDungeonEnemiesMeasured(float dt)
        {
            if (dt <= 0 || IsFrozen || DungeonModal || Profile == null || !State.inCombat || ImpactFeedback!=null&&ImpactFeedback.Holding) return;
            foreach (string id in State.enemies.Select(a => a.id).ToArray())
            {
                if(ImpactFeedback!=null&&ImpactFeedback.Holding)break;
                var enemy = State.Actor(id);
                if (enemy == null || !enemy.Alive) continue;
                if(AdvanceV13Boss(enemy,dt))continue;
                if (Profile.ActiveRoom.templateId == "R5" && Profile.ActiveMode.HasFlag("COVER_SHIFTED") && enemy.aiState == 1 && LockedDungeonPattern(enemy)?.shape == "Circle")
                {
                    enemy.aiState = 2; enemy.aiRemaining = 1.1f; enemy.attackGroupId = null;
                    Message = "掩体下的喷口已关闭，这次地面攻击取消。";
                }
                var template = Profile.EnemyTemplateFor(id);
                if (template == null || template.patterns == null || template.patterns.Count == 0)
                { Message = "敌人缺少已登记招式：" + id; continue; }
                if (enemy.staggerRemaining > 0 || enemy.statuses.Any(s => s.id == "restrained" && s.remaining > 0))
                {
                    // A real break/control interrupts a pending warning; it cannot resume
                    // an already interrupted swing immediately when the control wears off.
                    if (enemy.aiState == 1)
                    {
                        enemy.aiState = 2; enemy.aiRemaining = LockedDungeonPattern(enemy)?.recovery ?? 1;
                        enemy.attackGroupId = null;
                    }
                    continue;
                }
                enemy.aiRemaining = Mathf.Max(0, enemy.aiRemaining - dt);
                if (enemy.aiState == 1)
                {
                    if (enemy.aiRemaining > 0) continue;
                    var locked = LockedDungeonPattern(enemy);
                    if (locked == null || string.IsNullOrEmpty(enemy.attackGroupId))
                    { Message = "敌方攻击缺少原始预告，等待恢复有效状态。"; continue; }
                    if (!ResolveDungeonContacts(enemy, locked)) return;
                    enemy = State.Actor(id); // The unified transaction replaces every ActorState instance.
                    if (enemy != null) { enemy.aiState = 2; enemy.aiRemaining = Mathf.Max(0, locked.recovery); }
                    if(V13Enabled)State.v13.enemyRestUntil=Mathf.Max(State.v13.enemyRestUntil,State.simulationSeconds+Session.Config.v13.threatGap);
                    // ResolveDungeonContacts settled the full retained contact set. Keep recovery
                    // valid before deferring later enemies, so this strike cannot replay on resume.
                    if(ImpactFeedback!=null&&ImpactFeedback.Holding)break;
                    continue;
                }
                if (enemy.aiRemaining > 0) continue;

                if(V13Enabled&&(CombatSession.V13FateBusy(State)||State.simulationSeconds<State.v13.enemyRestUntil||State.v13.fate?.stage=="Scheduled"&&State.v13.fate.warning<=0||State.enemies.Any(a=>a.id!=enemy.id&&a.Alive&&a.aiState==1)))continue;
                var pattern = NextDungeonPattern(enemy, template);
                if (Profile.ActiveRoom.templateId == "R5" && Profile.ActiveMode.HasFlag("COVER_SHIFTED") && pattern?.shape == "Circle")
                    pattern = template.patterns.FirstOrDefault(p => p.shape != "Circle");
                if (pattern == null) { Message = "敌人当前阶段没有可用招式：" + template.id; continue; }
                float acquisitionRange = pattern.shape == "Circle" ? Mathf.Max(pattern.radius, Session.Config.effectiveCombatRange) : pattern.size;
                var candidates = State.actors.Where(a => a.Alive&&IsPhysical(a))
                    .OrderBy(a => pattern.targetPriority == "Backline" ? (a.role == ActorRole.Mage ? 0 : a.role == ActorRole.Rogue ? 1 : 2) : 0)
                    .ThenBy(a => BattleGeometry.DistanceXZ(Position(a), Position(enemy))).ToArray();
                var target = candidates.FirstOrDefault(a => BattleGeometry.DistanceXZ(Position(a), Position(enemy)) <= acquisitionRange && HasLineOfEffect(Position(enemy), Position(a))) ?? candidates.FirstOrDefault();
                if (target == null) continue;
                float distance = BattleGeometry.DistanceXZ(Position(enemy), Position(target));
                if (distance > acquisitionRange || !HasLineOfEffect(Position(enemy), Position(target)))
                {
                    enemy.aiState = 0; enemy.attackTargetId = target.id;
                    enemy.lockedX = target.x; enemy.lockedZ = target.z;
                    if (template.speed > 0) MoveDungeonEnemy(enemy, Position(target), template.speed * dt, dt);
                    continue; // speed 0 mechanisms wait at their actual position and have a finite acquisition range.
                }

                enemy.attackSequence++;
                enemy.attackGroupId = State.encounterId + "/" + id + "/" + enemy.attackSequence;
                enemy.attackTargetId = target.id; enemy.lockedX = target.x; enemy.lockedZ = target.z;
                enemy.aiState = 1; enemy.aiRemaining = Mathf.Max(0, pattern.windup);
                State.world.strings[EnemyPatternKey + id] = pattern.id;
                if(V14Enabled&&State.momentumLedger.registeredHighThreats.Contains(enemy.id))
                {if(!State.momentumLedger.registeredDangerousThreats.Contains(enemy.attackGroupId))State.momentumLedger.registeredDangerousThreats.Add(enemy.attackGroupId);V14CaptureEvadeThreat(enemy,enemy.attackGroupId,DungeonEnemyShape(enemy));}
                // Choice is deterministic within this encounter. No phase transition creates
                // a new encounter, restores charges, heals the enemy, or rerolls a pattern.
            }
        }

        public AreaShape DungeonEnemyShape(ActorState enemy)
        {
            if (enemy == null) return AreaShape.Circle(Vector3.zero, 0);
            if(V13Enabled&&State.v13.boss?.actorId==enemy.id)return V13BossShape(enemy);
            var pattern = LockedDungeonPattern(enemy);
            if (pattern == null && enemy.aiState != 1 && Profile != null)
            {
                var template = Profile.EnemyTemplateFor(enemy.id);
                if (template != null) pattern = NextDungeonPattern(enemy, template);
            }
            if (pattern == null) return AreaShape.Circle(Position(enemy), 0);
            Vector3 lockedPoint = new Vector3(enemy.lockedX, enemy.heightY, enemy.lockedZ);
            return pattern.shape == "Circle" ? AreaShape.Circle(lockedPoint, pattern.radius) :
                AreaShape.Cone(Position(enemy), lockedPoint - Position(enemy), pattern.size, pattern.angle * .5f);
        }

        EnemyPattern LockedDungeonPattern(ActorState enemy)
        {
            if (Profile == null || enemy == null || !State.world.strings.TryGetValue(EnemyPatternKey + enemy.id, out string id)) return null;
            return Profile.EnemyTemplateFor(enemy.id)?.patterns.FirstOrDefault(p => p.id == id);
        }

        static EnemyPattern NextDungeonPattern(ActorState enemy, EnemyTemplate template)
        {
            float health = enemy.maxHp > 0 ? (float)enemy.hp / enemy.maxHp : 0;
            var eligible = template.patterns.Where(p => health <= p.phaseThreshold + .0001f).ToArray();
            return eligible.Length == 0 ? null : eligible[Mathf.Max(0, enemy.attackSequence) % eligible.Length];
        }

        bool ResolveDungeonContacts(ActorState enemy, EnemyPattern pattern)
        {
            string enemyId = enemy.id, group = enemy.attackGroupId;
            List<string> contacts;
            if (State.world.strings.TryGetValue(EnemyContactGroupKey + enemyId, out string retainedGroup) && retainedGroup == group &&
                State.world.strings.TryGetValue(EnemyContactsKey + enemyId, out string retainedContacts))
                contacts = CombatJson.Read<List<string>>(retainedContacts);
            else
            {
                AreaShape shape = DungeonEnemyShape(enemy);
                contacts = State.actors.Where(a => a.Alive && IsPhysical(a) && BattleGeometry.Contains(shape, Position(a)) &&
                    HasLineOfEffect(shape.Origin, Position(a)) && HasLineOfEffect(Position(enemy), Position(a))).Select(a => a.id).ToList();
                // The first successful contact transaction also stores the whole contact set.
                // A crash after one victim must resume this strike, not sample a new shape/target list.
                State.world.strings[EnemyContactGroupKey + enemyId] = group;
                State.world.strings[EnemyContactsKey + enemyId] = CombatJson.Write(contacts);
            }
            string commandId = "contact-batch/" + group;
            if (State.receipts.Any(r => r.id == commandId)) return true;
            V14SettleEvadeThreat(enemy,group,DungeonEnemyShape(enemy));
            if (contacts.Count == 0) return true;
            var result = ExecuteWorldCommand(new CombatCommand
            {
                id = commandId, kind = CommandKind.IncomingBatch, actorId = enemyId, rootId = group,
                qualified = true, authority = CommandAuthority.System,
                incomingContacts = contacts.Where(v => State.Actor(v)?.Alive == true).Select(v => new IncomingContact
                {
                    targetId = v, damage = pattern.damage, dc = pattern.dc, savingThrow = pattern.savingThrow,
                    parryable = pattern.parryable,
                    reactionEligibleActorIds = State.actors.Where(a => a.Alive && HasLineOfEffect(Position(a), Position(State.Actor(v)))).Select(a => a.id).ToList()
                }).ToList()
            });
            if (!result.success) Message = "敌方攻击尚未提交，保留同组接触：" + result.reason;
            return result.success;
        }

        void MoveDungeonEnemy(ActorState actor, Vector3 goal, float travel, float dt)
        {
            if(!TryAiWalkingWaypoint(actor,goal,dt,out goal))return;
            Vector3 origin = Position(actor), delta = BattleGeometry.Ground(goal - origin);
            travel = Mathf.Min(Mathf.Max(0, travel), delta.magnitude);
            if (travel <= .0001f) return;
            Vector3 best = origin; float score = float.PositiveInfinity;
            foreach (float angle in new[] { 0f, 35f, -35f, 70f, -70f, 105f, -105f })
            {
                Vector3 step = Quaternion.AngleAxis(angle, Vector3.up) * delta.normalized * travel;
                Vector3 point = BattleGeometry.MoveAgainstWalls(origin, step, .36f, 1 << WallLayer);
                if (BattleGeometry.DistanceXZ(origin, point) <= .0001f) continue;
                float value = BattleGeometry.DistanceXZ(point, goal) + Mathf.Abs(angle) * .0005f;
                if (value < score) { score = value; best = point; }
            }
            MoveActor(actor, best - origin);
        }
    }
}
