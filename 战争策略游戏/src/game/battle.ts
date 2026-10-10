/**
 * 战斗状态机 —— 逐回合推进两军交战
 *
 * 对应文档 §4.7 的战斗结算。这里管理：
 * - 双方编制（混编：多兵种配比）
 * - 每回合伤亡结算（用 combat.ts 的 casualtyRate）
 * - 溃退判定与胜负
 *
 * 混编处理：每个编制独立结算对敌对方每个编制的伤害，最后汇总。
 * 这是 resolve_engagement 的实现，见下一阶段。
 */

import {
  ROUT_STRENGTH,
  SOLDIERS,
  casualtyRate,
  moraleAfterCombat,
  soldier,
  terrain,
  type ArmyClass,
} from '../model/combat';
import type { Formation } from '../render/army';
import type { TerrainData } from '../render/terrain';

export interface BattleState {
  turn: number;
  blue: Formation[];
  red: Formation[];
  initialBlue: number;
  initialRed: number;
  blueCasualties: number;
  redCasualties: number;
  result: 'ongoing' | 'blue_win' | 'red_win' | 'draw';
  log: string[];
}

/** 每编制的初始人数（原型规模） */
const FORMATION_SIZES: Record<ArmyClass, number> = {
  infantry: 900,
  archer: 600,
  cavalry: 300,
};

export function createBattle(): BattleState {
  const blue: Formation[] = [
    { cls: 'infantry', count: FORMATION_SIZES.infantry, x: -700, z: -180, heading: Math.PI / 2, morale: 70 },
    { cls: 'archer', count: FORMATION_SIZES.archer, x: -880, z: 40, heading: Math.PI / 2, morale: 65 },
    { cls: 'cavalry', count: FORMATION_SIZES.cavalry, x: -620, z: 240, heading: Math.PI / 2, morale: 75 },
  ];
  const red: Formation[] = [
    { cls: 'infantry', count: FORMATION_SIZES.infantry, x: 700, z: -150, heading: -Math.PI / 2, morale: 70 },
    { cls: 'archer', count: FORMATION_SIZES.archer, x: 880, z: 60, heading: -Math.PI / 2, morale: 62 },
    { cls: 'cavalry', count: FORMATION_SIZES.cavalry + 100, x: 600, z: 260, heading: -Math.PI / 2, morale: 78 },
  ];
  return {
    turn: 0,
    blue,
    red,
    initialBlue: sum(blue),
    initialRed: sum(red),
    blueCasualties: 0,
    redCasualties: 0,
    result: 'ongoing',
    log: ['两军在河谷两岸列阵，接战在即。'],
  };
}

function sum(fs: Formation[]): number {
  return fs.reduce((a, f) => a + f.count, 0);
}

/**
 * 推进一个回合。
 *
 * 结算规则（混编）：
 * 1. 每个我方编制对每个敌方编制计算单兵伤亡率
 * 2. 总伤亡 = Σ(对方编制人数 × 我方对该编制兵种的伤亡率)
 * 3. 双方同时结算，用回合开始时的兵力
 * 4. 士气按伤亡比例更新
 * 5. 任一方总兵力低于初始的 25% 即溃退
 */
export function stepTurn(state: BattleState, td: TerrainData): BattleState {
  if (state.result !== 'ongoing') return state;

  const next: BattleState = {
    ...state,
    turn: state.turn + 1,
    blue: state.blue.map((f) => ({ ...f })),
    red: state.red.map((f) => ({ ...f })),
    log: [...state.log],
  };

  // 计算每个编制的地形（取编制中心点）
  const keyOf = (f: Formation) => td.keyAt(f.x, f.z);

  // 计算每个编制受到的伤害（双方同时结算，用回合开始时的兵力）
  const blueDamageTo: Record<string, number> = {};
  const redDamageTo: Record<string, number> = {};

  for (const bf of next.blue) {
    // 地形取攻方所在位置（你在什么地形打别人，这才是有效的战术信息）
    const t = terrain(keyOf(bf));
    const s = soldier(bf.cls);
    for (const rf of next.red) {
      const d = soldier(rf.cls);
      const dmg = casualtyRate(s, d, t) * rf.count;
      const key = rf.cls;
      blueDamageTo[key] = (blueDamageTo[key] || 0) + dmg;
    }
  }
  for (const rf of next.red) {
    const t = terrain(keyOf(rf));
    const s = soldier(rf.cls);
    for (const bf of next.blue) {
      const d = soldier(bf.cls);
      const dmg = casualtyRate(s, d, t) * bf.count;
      const key = bf.cls;
      redDamageTo[key] = (redDamageTo[key] || 0) + dmg;
    }
  }

  // 应用伤亡
  const blueBefore = sum(next.blue);
  const redBefore = sum(next.red);

  for (const f of next.blue) {
    const dmg = redDamageTo[f.cls] || 0;
    const casualtyRatio = Math.min(1, dmg / f.count);
    f.count = Math.max(0, f.count - dmg);
    f.morale = moraleAfterCombat(f.morale, casualtyRatio, false);
  }
  for (const f of next.red) {
    const dmg = blueDamageTo[f.cls] || 0;
    const casualtyRatio = Math.min(1, dmg / f.count);
    f.count = Math.max(0, f.count - dmg);
    f.morale = moraleAfterCombat(f.morale, casualtyRatio, false);
  }

  // 过滤打空的编制
  next.blue = next.blue.filter((f) => f.count > 0);
  next.red = next.red.filter((f) => f.count > 0);

  const blueAfter = sum(next.blue);
  const redAfter = sum(next.red);
  next.blueCasualties = blueBefore - blueAfter;
  next.redCasualties = redBefore - redAfter;

  // 溃退判定
  const blueRatio = blueAfter / state.initialBlue;
  const redRatio = redAfter / state.initialRed;
  const blueOut = blueRatio < ROUT_STRENGTH;
  const redOut = redRatio < ROUT_STRENGTH;

  if (blueOut && redOut) next.result = 'draw';
  else if (redOut) next.result = 'blue_win';
  else if (blueOut) next.result = 'red_win';

  // 战报
  if (next.result !== 'ongoing') {
    const msg = {
      blue_win: `第 ${next.turn} 回合：敌军溃散，我军取胜。`,
      red_win: `第 ${next.turn} 回合：我军溃散，败了。`,
      draw: `第 ${next.turn} 回合：双方同时溃散，战场陷入混乱。`,
    }[next.result];
    next.log.push(msg);
  } else if (next.turn % 5 === 0) {
    next.log.push(
      `第 ${next.turn} 回合：我军剩 ${blueAfter}（${Math.round(blueRatio * 100)}%），敌军剩 ${redAfter}（${Math.round(redRatio * 100)}%）。`,
    );
  }

  return next;
}

/** 单回合耗时测试（性能预算：5000 单位规模 < 50ms） */
export function stepTurnTimed(state: BattleState, td: TerrainData): { state: BattleState; ms: number } {
  const t0 = performance.now();
  const ns = stepTurn(state, td);
  const ms = performance.now() - t0;
  return { state: ns, ms };
}

/** 兵种明细（供 HUD 显示） */
export function formationBreakdown(fs: Formation[]): Record<string, number> {
  const out: Record<string, number> = {};
  for (const f of fs) out[SOLDIERS[f.cls].name] = Math.round(f.count);
  return out;
}

export type { Formation };