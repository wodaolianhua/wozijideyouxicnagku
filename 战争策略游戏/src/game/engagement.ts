/**
 * 交战逻辑 —— 单位间的实时对抗
 *
 * 对应文档 01 §3.2 循环 A（战术层）的后半段：接敌、交战、脱离。
 * 数值全部来自 model/combat.ts（已与 Python 参考实现逐位对齐，218 项一致）。
 *
 * 关键设计：
 * - 交战判定用「攻击距离」，弓兵 140 米，近战 12~16 米
 * - 每次交战结算固定 10 秒（与 combat.ts 的 TURN_SECONDS 一致）
 * - 近战单位会主动向目标逼近，弓兵在射程边缘停下射击
 * - 士气跌破阈值即溃退，溃退单位不再交战
 */

import {
  CLASS_ATTACK_RANGE,
  ENGAGE_TURN_RATE,
  type ControllableUnit,
  type UnitState,
  passability,
  stepUnit,
} from './unit';
import {
  ROUT_MORALE,
  SOLDIER_HP,
  SOLDIERS,
  casualtyRate,
  moraleAfterCombat,
  soldier,
  terrain,
} from '../model/combat';
import type { TerrainData } from '../render/terrain';

export const TURN_SECONDS = 10.0;

/**
 * 索敌范围倍数（相对攻击距离）。
 * 1.5倍意味着「进入射程 1.5 倍内就会主动接敌并逼近」，
 * 保证接敌后有足够时间进入实际射程。
 */
export const SIGHT_MULTIPLIER = 1.5;

/** 交战结果：返回新的双方单位 */
export interface EngagementResult {
  attacker: ControllableUnit;
  defender: ControllableUnit;
  attackerCasualties: number;
  defenderCasualties: number;
  /** 攻击方是否打赢了这一轮（守方溃退或兵力损失过半） */
  attackerWon: boolean;
}

/** 两单位距离 */
export function distance(a: ControllableUnit, b: ControllableUnit): number {
  return Math.hypot(a.x - b.x, a.z - b.z);
}

/** 是否在攻击距离内 */
export function inAttackRange(a: ControllableUnit, b: ControllableUnit): boolean {
  return distance(a, b) <= CLASS_ATTACK_RANGE[a.cls];
}

/**
 * 结算一次交战（10 秒）。
 *
 * 伤害计算沿用 combat.ts 的 casualtyRate：
 *   守方伤亡比例 = casualtyRate(攻方兵种, 守方兵种, 攻方所在地形)
 *   守方伤亡数 = 守方伤亡比例 × 守方兵力
 *
 * 同时应用士气变化（败方打击大于胜方激励）。
 */
export function resolveEngagement(
  attacker: ControllableUnit,
  defender: ControllableUnit,
  td: TerrainData,
): EngagementResult {
  const t = terrain(td.keyAt(attacker.x, attacker.z));
  const sA = soldier(attacker.cls);
  const sD = soldier(defender.cls);

  // 攻方对守方造成的伤亡比例
  const rateA = casualtyRate(sA, sD, t);
  const defenderCasualties = Math.min(defender.strength, rateA * defender.strength);

  // 守方反击（近战才有反击能力；弓兵在远处无法反击）
  const defenderCanCounter = distance(attacker, defender) <= CLASS_ATTACK_RANGE[defender.cls];
  let attackerCasualties = 0;
  if (defenderCanCounter) {
    const t2 = terrain(td.keyAt(defender.x, defender.z));
    const rateD = casualtyRate(sD, sA, t2);
    attackerCasualties = Math.min(attacker.strength, rateD * attacker.strength);
  }

  const aLost = attackerCasualties / Math.max(1, attacker.strength);
  const dLost = defenderCasualties / Math.max(1, defender.strength);

  // 判定胜负：守方损失更惨则攻方胜
  const attackerWon = dLost > aLost;

  const newAttacker: ControllableUnit = {
    ...attacker,
    strength: Math.max(0, attacker.strength - attackerCasualties),
    morale: moraleAfterCombat(attacker.morale, aLost, attackerWon),
  };
  const newDefender: ControllableUnit = {
    ...defender,
    strength: Math.max(0, defender.strength - defenderCasualties),
    morale: moraleAfterCombat(defender.morale, dLost, !attackerWon),
  };

  return {
    attacker: newAttacker,
    defender: newDefender,
    attackerCasualties,
    defenderCasualties,
    attackerWon,
  };
}

/** 单位是否已溃退（兵力耗尽或士气崩溃） */
export function isRouting(u: ControllableUnit): boolean {
  return u.strength <= 0 || u.morale < ROUT_MORALE;
}

/** 更新单位状态标记 */
export function withState(u: ControllableUnit, state: UnitState): ControllableUnit {
  if (u.state === state) return u;
  return { ...u, state };
}

/** 溃退处理：丢弃武器，状态转为 routing，清除交战目标 */
export function applyRouting(u: ControllableUnit): ControllableUnit {
  return {
    ...u,
    state: 'routing',
    engageTargetId: null,
    targetX: null,
    targetZ: null,
    selected: false,
  };
}

/**
 * 推进整个战场一个时间步。
 *
 * 流程：
 * 1. 找交战配对（同阵营外的敌对单位在攻击距离内）
 * 2. 结算交战，双方扣兵与士气
 * 3. 近战单位向目标逼近（进入攻击距离内）
 * 4. 弓兵若超出射程则向目标移动
 * 5. 溃退单位退出战斗
 *
 * 返回新的单位数组与本步产生的战报。
 */
export function stepEngagement(
  units: ControllableUnit[],
  td: TerrainData,
  dt: number,
): { units: ControllableUnit[]; events: string[] } {
  const events: string[] = [];
  // 用 Map 便于按 id 更新
  const map = new Map(units.map((u) => [u.id, { ...u }]));

  // 1. 交战配对：贪心配对，一个单位同时只能打一个
  const engaged = new Set<number>();
  const pairs: Array<[ControllableUnit, ControllableUnit]> = [];
  for (const a of units) {
    if (a.side !== 'blue') continue; // 只由我方主动发起，降低 O(n²) 开销
    if (isRouting(a)) continue;
    let best: ControllableUnit | null = null;
    let bestD = Infinity;
    for (const b of units) {
      if (b.side === a.side) continue;
      if (isRouting(b)) continue;
      if (engaged.has(b.id)) continue;
      const d = distance(a, b);
      // 主动索敌范围：弓兵看得远，近战只看近处
      const sight = CLASS_ATTACK_RANGE[a.cls] * SIGHT_MULTIPLIER;
      if (d <= sight && d < bestD) {
        best = b;
        bestD = d;
      }
    }
    if (best) {
      engaged.add(a.id);
      engaged.add(best.id);
      pairs.push([a, best]);
    }
  }

  // 2. 结算交战
  for (const [a, b] of pairs) {
    const r = resolveEngagement(a, b, td);
    map.set(a.id, { ...r.attacker, state: 'engaging', engageTargetId: b.id });
    map.set(b.id, { ...r.defender, state: 'engaging', engageTargetId: a.id });
    if (r.attackerCasualties > 0 || r.defenderCasualties > 0) {
      events.push(
        `${SOLDIERS[a.cls].name} 与 ${SOLDIERS[b.cls].name} 交战（10 秒）：我方亡 ${Math.round(r.attackerCasualties)}，敌方亡 ${Math.round(r.defenderCasualties)}。`,
      );
    }
  }

  // 3~4. 移动：交战单位贴近目标，待命单位执行原移动指令
  const next: ControllableUnit[] = [];
  for (const orig of units) {
    let u = map.get(orig.id)!;
    if (isRouting(u)) {
      next.push(applyRouting(u));
      continue;
    }
    const targetId = u.engageTargetId;
    if (targetId !== null) {
      const target = map.get(targetId);
      if (target && !isRouting(target)) {
        const range = CLASS_ATTACK_RANGE[u.cls];
        const d = distance(u, target);
        if (d > range) {
          // 向目标逼近。目标移动时不重置 path——保留已算好的路径，
          // 只在目标明显偏离时才由外部重新寻径。
          const prevPath = u.path;
          u = {
            ...u,
            targetX: target.x,
            targetZ: target.z,
            state: 'moving' as UnitState,
            path: prevPath,
          };
          u = stepUnit(u, td, dt);
        } else {
          // 已在射程内：站定，转向目标
          u = { ...u, targetX: null, targetZ: null };
          u = faceTarget(u, target, dt, ENGAGE_TURN_RATE);
          u = { ...u, state: 'engaging', engageTargetId: targetId };
        }
      } else {
        // 目标已溃退或消失
        u = { ...u, state: 'idle', engageTargetId: null };
      }
    } else if (u.targetX !== null || u.targetZ !== null) {
      u = stepUnit(u, td, dt);
      if (u.targetX === null && u.targetZ === null) {
        u = { ...u, state: 'idle' };
      }
    } else {
      // 待命且无交战目标：若敌方进入索敌范围，主动接敌。
      // 缺少这一步会导致「两军隔 30 米各自待命，永远不打起来」——
      // 步兵索敌仅 18 米，超过就不会主动接近（实测缺陷）。
      const foe = findNearestFoe(u, units);
      if (foe && distance(u, foe) <= CLASS_ATTACK_RANGE[u.cls] * SIGHT_MULTIPLIER) {
        u = { ...u, state: 'engaging', engageTargetId: foe.id };
      }
    }
    next.push(u);
  }

  // 5. 溃退标记
  for (const u of next) {
    if (isRouting(u) && u.state !== 'routing') {
      events.push(`${SOLDIERS[u.cls].name}（${u.side === 'blue' ? '我方' : '敌方'}）溃退！`);
    }
  }

  return { units: next, events };
}

/** 找最近的敌方单位（不要求在射程内，只要在索敌范围内即可） */
export function findNearestFoe(
  u: ControllableUnit,
  units: ControllableUnit[],
): ControllableUnit | null {
  const sight = CLASS_ATTACK_RANGE[u.cls] * SIGHT_MULTIPLIER;
  let best: ControllableUnit | null = null;
  let bestD = sight;
  for (const o of units) {
    if (o.side === u.side) continue;
    if (isRouting(o)) continue;
    const d = distance(u, o);
    if (d <= bestD) {
      best = o;
      bestD = d;
    }
  }
  return best;
}

/** 平滑转向目标 */
export function faceTarget(
  u: ControllableUnit,
  target: ControllableUnit,
  dt: number,
  rate: number,
): ControllableUnit {
  const want = Math.atan2(target.x - u.x, target.z - u.z);
  let diff = want - u.heading;
  while (diff > Math.PI) diff -= Math.PI * 2;
  while (diff < -Math.PI) diff += Math.PI * 2;
  const step = Math.max(-rate * dt, Math.min(rate * dt, diff));
  return { ...u, heading: u.heading + step };
}

/** 下令攻击指定目标（清除移动目标，转为交战） */
export function orderAttack(
  units: ControllableUnit[],
  targetId: number,
): ControllableUnit[] {
  return units.map((u) =>
    u.selected
      ? { ...u, state: 'engaging' as UnitState, engageTargetId: targetId, targetX: null, targetZ: null }
      : u,
  );
}

/** 下令追击移动到某点（会清除交战目标） */
export function orderPursue(
  units: ControllableUnit[],
  x: number,
  z: number,
): ControllableUnit[] {
  return units.map((u) =>
    u.selected
      ? { ...u, state: 'moving' as UnitState, engageTargetId: null, targetX: x, targetZ: z }
      : u,
  );
}

/** 下令脱离战斗（撤出，停止交战） */
export function orderDisengage(units: ControllableUnit[]): ControllableUnit[] {
  return units.map((u) =>
    u.selected
      ? { ...u, state: 'idle' as UnitState, engageTargetId: null, targetX: null, targetZ: null }
      : u,
  );
}

/** 单位当前正在交战的敌方单位（用于 UI 高亮） */
export function engagedTarget(u: ControllableUnit, units: ControllableUnit[]): ControllableUnit | null {
  if (u.engageTargetId === null) return null;
  return units.find((x) => x.id === u.engageTargetId) ?? null;
}

/** 战场统计 */
export function battleStats(units: ControllableUnit[]): {
  blue: number; red: number; blueRouting: number; redRouting: number; engaged: number;
} {
  let blue = 0, red = 0, blueRouting = 0, redRouting = 0, engaged = 0;
  for (const u of units) {
    if (u.side === 'blue') blue += u.strength;
    else red += u.strength;
    if (isRouting(u)) {
      if (u.side === 'blue') blueRouting++;
      else redRouting++;
    }
    if (u.state === 'engaging') engaged++;
  }
  return { blue, red, blueRouting, redRouting, engaged };
}

export { SOLDIER_HP, passability, ROUT_MORALE };
