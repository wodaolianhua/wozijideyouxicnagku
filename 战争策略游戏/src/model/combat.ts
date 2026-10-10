/**
 * 战斗数值模型 —— TypeScript 实现
 *
 * 本文件是 python/combat_model.py 的移植版本。公式、常量、测试断言三者必须与Python 版一致。
 *
 * 验收标准：Vitest 全部通过，且关键数值与 Python 版实测一致。
 * 一旦两边分叉，以 Python 版为参考实现，本文件改为对齐。
 */

export const SOLDIER_HP = 10.0;
export const DAMAGE_SCALE = 0.115;
export const ROUT_MORALE = 20.0;
export const ROUT_STRENGTH = 0.25;
export const TURN_SECONDS = 10.0;

export const ARMY_CLASS = ['infantry', 'archer', 'cavalry'] as const;
export type ArmyClass = (typeof ARMY_CLASS)[number];

export interface Soldier {
  cls: ArmyClass;
  name: string;
  str: number;
  mor: number;
  sta: number;
  baseHit: number;
  deff: number;
  cost: number;
}

export interface Terrain {
  name: string;
  moveCost: number;
  hitMod: number;
  perClass: Record<ArmyClass, number>;
}

export const SOLDIERS: Record<ArmyClass, Soldier> = {
  infantry: {
    cls: 'infantry',
    name: '步兵',
    str: 10,
    mor: 60,
    sta: 50,
    baseHit: 0.45,
    deff: 8,
    cost: 1,
  },
  archer: {
    cls: 'archer',
    name: '弓兵',
    str: 7,
    mor: 52,
    sta: 40,
    baseHit: 0.55,
    deff: 5,
    cost: 1,
  },
  cavalry: {
    cls: 'cavalry',
    name: '骑兵',
    str: 12,
    mor: 72,
    sta: 80,
    baseHit: 0.4,
    deff: 7,
    cost: 2,
  },
};

export const TERRAINS: Record<string, Terrain> = {
  plain: {
    name: '平原',
    moveCost: 1.0,
    hitMod: 1.0,
    perClass: { infantry: 1.0, archer: 1.0, cavalry: 1.0 },
  },
  forest: {
    name: '林地',
    moveCost: 1.4,
    hitMod: 0.95,
    perClass: { infantry: 0.95, archer: 1.25, cavalry: 0.8 },
  },
  hill: {
    name: '山地',
    moveCost: 1.8,
    hitMod: 0.9,
    perClass: { infantry: 1.1, archer: 0.9, cavalry: 0.6 },
  },
  river: {
    name: '河谷',
    moveCost: 1.5,
    hitMod: 0.95,
    perClass: { infantry: 1.0, archer: 1.0, cavalry: 0.9 },
  },
  fort: {
    name: '城池',
    moveCost: 2.5,
    hitMod: 0.85,
    perClass: { infantry: 1.0, archer: 1.0, cavalry: 0.7 },
  },
};

export const COUNTER: Record<ArmyClass, Record<ArmyClass, number>> = {
  infantry: { infantry: 1.0, archer: 1.0, cavalry: 1.3 },
  archer: { infantry: 1.3, archer: 1.0, cavalry: 1.0 },
  cavalry: { infantry: 1.0, archer: 1.3, cavalry: 1.0 },
};

export function soldier(cls: string): Soldier {
  const s = SOLDIERS[cls as ArmyClass];
  if (!s) throw new Error(`未知兵种: ${cls}`);
  return s;
}

export function terrain(name: string): Terrain {
  const t = TERRAINS[name];
  if (!t) throw new Error(`未知地形: ${name}`);
  return t;
}

export function effectivePower(s: Soldier, t: Terrain, generalBonus = 1.0): number {
  return (
    s.str * (1 + s.mor / 100) * (1 + s.sta / 200) * t.perClass[s.cls] * generalBonus
  );
}

export function hitRate(a: Soldier, t: Terrain): number {
  const raw = a.baseHit * t.hitMod;
  return Math.max(0.05, Math.min(0.95, raw));
}

export function damageMitigation(d: Soldier): number {
  return (1 - d.deff / 200) * (1 - d.mor / 200);
}

export function counterFactor(a: ArmyClass, d: ArmyClass): number {
  const gain = COUNTER[a][d];
  if (gain > 1.0) return gain;
  const reverse = COUNTER[d][a];
  if (reverse > 1.0) return 1.0 / reverse;
  return 1.0;
}

export function damagePerTurn(
  a: Soldier,
  d: Soldier,
  t: Terrain,
  generalBonus = 1.0,
): number {
  const eff = effectivePower(a, t, generalBonus) * counterFactor(a.cls, d.cls);
  const hit = hitRate(a, t);
  const mitig = damageMitigation(d);
  return eff * hit * mitig * DAMAGE_SCALE;
}

export function casualtyRate(a: Soldier, d: Soldier, t: Terrain, generalBonus = 1.0): number {
  return damagePerTurn(a, d, t, generalBonus) / SOLDIER_HP;
}

export function turnsToKill(a: Soldier, d: Soldier, t: Terrain, generalBonus = 1.0): number {
  const perTurn = damagePerTurn(a, d, t, generalBonus);
  if (perTurn <= 0) return Number.POSITIVE_INFINITY;
  return SOLDIER_HP / perTurn;
}

export function isRouted(morale: number): boolean {
  return morale < ROUT_MORALE;
}

export function moraleAfterCombat(
  morale: number,
  casualty: number,
  won: boolean,
  generalMoraleBonus = 0.0,
): number {
  let delta = -casualty * 30.0;
  delta += won ? 5.0 : -12.0;
  delta += generalMoraleBonus;
  return Math.max(0, Math.min(100, morale + delta));
}

export function marchCost(distance: number, t: Terrain, troops: number): number {
  if (troops <= 0) return 0.0;
  return (distance * t.moveCost * troops) / 1000.0;
}

export function siegeModifier(besiegerCls: ArmyClass, fortLevel: number): number {
  const level = Math.max(1, Math.min(5, fortLevel));
  let base = 1.0 - 0.12 * (level - 1);
  if (besiegerCls === 'cavalry') base *= 0.85;
  return Math.max(0.2, base);
}

export function supplyCapacity(surplusFood: number, troops: number): boolean {
  return surplusFood >= (troops / 1000.0) * 10.0;
}

export type Winner = 'attacker' | 'defender' | 'draw' | 'timeout';

export interface DuelResult {
  winner: Winner;
  turns: number;
  attackerLeft: number;
  defenderLeft: number;
  seconds: number;
  attackerRouted: boolean;
  defenderRouted: boolean;
}

export function resolveDuel(
  a: Soldier,
  d: Soldier,
  t: Terrain,
  attackerCount: number,
  defenderCount: number,
  maxTurns = 300,
  generalBonus = 1.0,
): DuelResult {
  const aRate = casualtyRate(a, d, t, generalBonus);
  const dRate = casualtyRate(d, a, t, generalBonus);

  let aLeft = attackerCount;
  let dLeft = defenderCount;
  const aStart = attackerCount || 1.0;
  const dStart = defenderCount || 1.0;
  let turns = 0;

  while (turns < maxTurns) {
    if (aLeft / aStart < ROUT_STRENGTH || dLeft / dStart < ROUT_STRENGTH) break;
    const aCasualties = dRate * dLeft;
    const dCasualties = aRate * aLeft;
    aLeft = Math.max(0, aLeft - aCasualties);
    dLeft = Math.max(0, dLeft - dCasualties);
    turns += 1;
  }

  const aOut = aLeft / aStart < ROUT_STRENGTH;
  const dOut = dLeft / dStart < ROUT_STRENGTH;

  let winner: Winner;
  if (aOut && dOut) winner = 'draw';
  else if (dOut) winner = 'attacker';
  else if (aOut) winner = 'defender';
  else winner = 'timeout';

  return {
    winner,
    turns,
    attackerLeft: round2(aLeft),
    defenderLeft: round2(dLeft),
    seconds: turns * TURN_SECONDS,
    attackerRouted: aOut,
    defenderRouted: dOut,
  };
}

function round2(v: number): number {
  return Math.round(v * 100) / 100;
}

export function counterMatrix(): Record<ArmyClass, Record<ArmyClass, number>> {
  const out = {} as Record<ArmyClass, Record<ArmyClass, number>>;
  for (const a of ARMY_CLASS) {
    out[a] = {} as Record<ArmyClass, number>;
    for (const d of ARMY_CLASS) out[a][d] = counterFactor(a, d);
  }
  return out;
}

export function casualtyMatrix(tname: string): Record<ArmyClass, Record<ArmyClass, number>> {
  const t = terrain(tname);
  const out = {} as Record<ArmyClass, Record<ArmyClass, number>>;
  for (const a of ARMY_CLASS) {
    out[a] = {} as Record<ArmyClass, number>;
    for (const d of ARMY_CLASS) out[a][d] = casualtyRate(soldier(a), soldier(d), t);
  }
  return out;
}