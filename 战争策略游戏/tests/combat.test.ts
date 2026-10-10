/**
 * 战斗数值模型 TypeScript 版验证测试
 *
 * 断言从python/test_combat_model.py 逐条翻译，验证范围与Python 版一致。
 * 额外增加跨语言一致性检查所需的导出。
 */

import { describe, it, expect } from 'vitest';
import {
  ARMY_CLASS,
  DAMAGE_SCALE,
  ROUT_MORALE,
  ROUT_STRENGTH,
  SOLDIER_HP,
  SOLDIERS,
  TERRAINS,
  type ArmyClass,
  type Soldier,
  casualtyMatrix,
  casualtyRate,
  counterFactor,
  counterMatrix,
  damageMitigation,
  damagePerTurn,
  effectivePower,
  hitRate,
  isRouted,
  marchCost,
  moraleAfterCombat,
  resolveDuel,
  siegeModifier,
  soldier,
  supplyCapacity,
  terrain,
  turnsToKill,
} from '../src/model/combat';

function replace(base: Soldier, over: Partial<Soldier>): Soldier {
  return { ...base, ...over };
}

// ================================================================ 1. 单调性

describe('单调性', () => {
  it('士气越高，有效战斗力越高', () => {
    const base = soldier('infantry');
    const vals = [0, 20, 40, 60, 80, 100].map((m) =>
      effectivePower(replace(base, { mor: m }), terrain('plain')),
    );
    for (let i = 0; i < vals.length - 1; i++) {
      expect(vals[i]).toBeLessThan(vals[i + 1]);
    }
  });

  it('体能越高，有效战斗力越高', () => {
    const base = soldier('infantry');
    const vals = [0, 25, 50, 75, 100].map((s) =>
      effectivePower(replace(base, { sta: s }), terrain('plain')),
    );
    for (let i = 0; i < vals.length - 1; i++) {
      expect(vals[i]).toBeLessThan(vals[i + 1]);
    }
  });

  it('兵力越高，有效战斗力越高', () => {
    const base = soldier('infantry');
    const vals = [5, 10, 15, 20].map((v) =>
      effectivePower(replace(base, { str: v }), terrain('plain')),
    );
    for (let i = 0; i < vals.length - 1; i++) {
      expect(vals[i]).toBeLessThan(vals[i + 1]);
    }
  });

  it('将领加成严格线性', () => {
    const s = soldier('infantry');
    const t = terrain('plain');
    const p1 = effectivePower(s, t, 1.0);
    const p2 = effectivePower(s, t, 1.1);
    expect(p2).toBeGreaterThan(p1);
    expect(p2 / p1).toBeCloseTo(1.1, 9);
  });

  it('地形命中修正严格跟随hitMod 排序', () => {
    const names = Object.keys(TERRAINS);
    const mods = names.map((n) => TERRAINS[n].hitMod);
    const rates = names.map((n) => hitRate(soldier('infantry'), terrain(n)));
    for (let i = 0; i < mods.length - 1; i++) {
      if (mods[i] >= mods[i + 1]) {
        expect(rates[i]).toBeGreaterThanOrEqual(rates[i + 1]);
      }
    }
    const plain = hitRate(soldier('infantry'), terrain('plain'));
    const fort = hitRate(soldier('infantry'), terrain('fort'));
    expect(plain).toBe(Math.max(...rates));
    expect(fort).toBe(Math.min(...rates));
  });

  it('士气不影响命中率（防重复计算回归）', () => {
    const base = soldier('infantry');
    const ref = hitRate(base, terrain('plain'));
    for (const m of [0, 50, 100]) {
      expect(hitRate(replace(base, { mor: m }), terrain('plain'))).toBe(ref);
    }
  });
});

// ================================================================ 2. 量级

describe('量级', () => {
  it('同兵种单兵对决在 8~20 回合内分胜负', () => {
    for (const cls of ARMY_CLASS) {
      const tt = turnsToKill(soldier(cls), soldier(cls), terrain('plain'));
      expect(tt, `${cls} 对决 ${tt} 回合`).toBeGreaterThanOrEqual(8);
      expect(tt).toBeLessThanOrEqual(20);
    }
  });

  it('同兵种单回合伤亡在 4%~25%', () => {
    for (const cls of ARMY_CLASS) {
      for (const tname of Object.keys(TERRAINS)) {
        const r = casualtyRate(soldier(cls), soldier(cls), terrain(tname));
        expect(r, `${cls}@${tname}`).toBeGreaterThanOrEqual(0.04);
        expect(r).toBeLessThanOrEqual(0.25);
      }
    }
  });

  it('100v100 在 5~25 回合内结束', () => {
    const r = resolveDuel(soldier('infantry'), soldier('infantry'), terrain('plain'), 100, 100);
    expect(r.winner).not.toBe('timeout');
    expect(r.turns).toBeGreaterThanOrEqual(5);
    expect(r.turns).toBeLessThanOrEqual(25);
  });

  it('局部交战时长在 1~6 分钟', () => {
    const r = resolveDuel(soldier('infantry'), soldier('infantry'), terrain('plain'), 1000, 1000);
    expect(r.seconds).toBeGreaterThanOrEqual(60);
    expect(r.seconds).toBeLessThanOrEqual(360);
  });

  it('战斗结果与兵力规模无关', () => {
    const small = resolveDuel(soldier('infantry'), soldier('infantry'), terrain('plain'), 50, 50);
    const large = resolveDuel(soldier('infantry'), soldier('infantry'), terrain('plain'), 50000, 50000);
    expect(large.winner).toBe(small.winner);
    expect(large.turns).toBe(small.turns);
  });

  it('伤亡比例与规模无关', () => {
    const m = casualtyMatrix('plain');
    for (const a of ARMY_CLASS) {
      for (const d of ARMY_CLASS) {
        expect(m[a][d]).toBeCloseTo(
          casualtyRate(soldier(a), soldier(d), terrain('plain')),
          12,
        );
      }
    }
  });
});

// ================================================================ 3. 方向性

describe('方向性（克制与地形）', () => {
  const inf: ArmyClass = 'infantry';
  const arc: ArmyClass = 'archer';
  const cav: ArmyClass = 'cavalry';

  it('三角克制三向成立', () => {
    expect(counterFactor(inf, cav)).toBeGreaterThan(1);
    expect(counterFactor(cav, arc)).toBeGreaterThan(1);
    expect(counterFactor(arc, inf)).toBeGreaterThan(1);
  });

  it('反向受罚且互为倒数', () => {
    expect(counterFactor(cav, inf)).toBeLessThan(1);
    expect(counterFactor(arc, cav)).toBeLessThan(1);
    expect(counterFactor(inf, arc)).toBeLessThan(1);
    expect(counterFactor(cav, inf) * counterFactor(inf, cav)).toBeCloseTo(1, 9);
  });

  it('克制矩阵满足 f(a,d)*f(d,a)=1', () => {
    const m = counterMatrix();
    for (const a of ARMY_CLASS) {
      for (const d of ARMY_CLASS) {
        expect(m[a][d] * m[d][a]).toBeCloseTo(1, 9);
      }
    }
  });

  it('克制净收益≥25%', () => {
    const m = counterMatrix();
    const gains: number[] = [];
    const losses: number[] = [];
    for (const a of ARMY_CLASS) {
      for (const d of ARMY_CLASS) {
        if (m[a][d] > 1) gains.push(m[a][d]);
        if (m[a][d] < 1) losses.push(m[a][d]);
      }
    }
    expect(gains).toHaveLength(3);
    expect(losses).toHaveLength(3);
    expect(Math.min(...gains)).toBeGreaterThanOrEqual(1.25);
    expect(Math.max(...losses)).toBeLessThanOrEqual(0.8);
  });

  it('克制收益大于同兵种对战', () => {
    const t = terrain('plain');
    expect(casualtyRate(soldier(inf), soldier(cav), t)).toBeGreaterThan(
      casualtyRate(soldier(inf), soldier(inf), t),
    );
    expect(casualtyRate(soldier(cav), soldier(arc), t)).toBeGreaterThan(
      casualtyRate(soldier(cav), soldier(cav), t),
    );
    expect(casualtyRate(soldier(arc), soldier(inf), t)).toBeGreaterThan(
      casualtyRate(soldier(arc), soldier(arc), t),
    );
  });

  it('克制不被守方面板抵消（回归）', () => {
    const t = terrain('plain');
    const base = casualtyRate(soldier(inf), soldier(inf), t);
    const vsCav = casualtyRate(soldier(inf), soldier(cav), t);
    expect(vsCav / base).toBeGreaterThanOrEqual(1.15);
    expect(counterFactor(inf, cav)).toBeCloseTo(1.3, 9);
  });

  it('地形偏好正确', () => {
    expect(terrain('hill').perClass.cavalry).toBeLessThan(1);
    expect(terrain('hill').perClass.infantry).toBeGreaterThan(1);
    expect(terrain('forest').perClass.archer).toBeGreaterThan(1);
    expect(terrain('forest').perClass.cavalry).toBeLessThan(1);
    expect(terrain('hill').perClass.cavalry).toBeLessThanOrEqual(0.7);
  });

  it('地形可逆转克制方向', () => {
    expect(casualtyRate(soldier(cav), soldier(inf), terrain('hill'))).toBeLessThan(
      casualtyRate(soldier(inf), soldier(cav), terrain('hill')),
    );
  });

  it('克制真实改变战斗结局', () => {
    const t = terrain('plain');
    expect(resolveDuel(soldier(inf), soldier(cav), t, 1000, 1000).winner).toBe('attacker');
    expect(resolveDuel(soldier(cav), soldier(inf), t, 1000, 1000).winner).toBe('defender');
  });

  it('地形真实改变战斗结局', () => {
    const r = resolveDuel(soldier(inf), soldier(cav), terrain('hill'), 1000, 1000);
    expect(r.winner).toBe('attacker');
  });

  it('兵力优势可抵消地形劣势', () => {
    expect(
      resolveDuel(soldier(cav), soldier(inf), terrain('plain'), 4000, 1000).winner,
    ).toBe('attacker');
    expect(
      resolveDuel(soldier(cav), soldier(inf), terrain('hill'), 8000, 1000).winner,
    ).toBe('attacker');
  });

  it('溃退条件触发', () => {
    const r = resolveDuel(soldier(cav), soldier(inf), terrain('hill'), 500, 2000);
    expect(r.attackerRouted).toBe(true);
    expect(r.winner).toBe('defender');
    expect(r.turns).toBeLessThan(10);
  });
});

// ================================================================ 4. 边界鲁棒性

describe('边界与鲁棒性', () => {
  it('任何组合下伤害非负', () => {
    for (const a of ARMY_CLASS) {
      for (const d of ARMY_CLASS) {
        for (const t of Object.keys(TERRAINS)) {
          expect(damagePerTurn(soldier(a), soldier(d), terrain(t))).toBeGreaterThanOrEqual(0);
        }
      }
    }
  });

  it('命中率不越界', () => {
    for (const a of ARMY_CLASS) {
      for (const t of Object.keys(TERRAINS)) {
        const h = hitRate(soldier(a), terrain(t));
        expect(h).toBeGreaterThanOrEqual(0.05);
        expect(h).toBeLessThanOrEqual(0.95);
      }
    }
  });

  it('减伤系数严格在 (0,1]', () => {
    for (const cls of ARMY_CLASS) {
      for (const mor of [0, 50, 100]) {
        const m = damageMitigation(replace(soldier(cls), { mor }));
        expect(m).toBeGreaterThan(0);
        expect(m).toBeLessThanOrEqual(1);
      }
    }
  });

  it('士气夹紧在 0~100', () => {
    expect(moraleAfterCombat(5, 0.9, false)).toBe(0);
    expect(moraleAfterCombat(99, 0, true)).toBeLessThanOrEqual(100);
  });

  it('士气随伤亡单调下降', () => {
    const vals = [0, 0.1, 0.3, 0.5].map((c) => moraleAfterCombat(70, c, false));
    for (let i = 0; i < vals.length - 1; i++) {
      expect(vals[i]).toBeGreaterThan(vals[i + 1]);
    }
  });

  it('败绩打击大于胜利激励', () => {
    const start = 60;
    const won = moraleAfterCombat(start, 0.1, true);
    const lost = moraleAfterCombat(start, 0.1, false);
    expect(start - lost).toBeGreaterThan(won - start);
  });

  it('溃退阈值判定正确', () => {
    expect(isRouted(ROUT_MORALE - 1)).toBe(true);
    expect(isRouted(ROUT_MORALE + 1)).toBe(false);
  });

  it('零兵力行军不产生负粮耗', () => {
    expect(marchCost(10, terrain('plain'), 0)).toBe(0);
    expect(marchCost(10, terrain('plain'), -5)).toBe(0);
  });

  it('行军粮耗随距离地形人数单调上升', () => {
    const p = terrain('plain');
    const h = terrain('hill');
    const c1 = marchCost(10, p, 1000);
    expect(c1).toBeLessThan(marchCost(20, p, 1000));
    expect(c1).toBeLessThan(marchCost(10, h, 1000));
    expect(c1).toBeLessThan(marchCost(10, p, 2000));
    expect(marchCost(10, h, 1000)).toBeGreaterThanOrEqual(1.5 * c1);
  });

  it('攻城效率随据点等级递减并夹紧', () => {
    const vals = [1, 2, 3, 4, 5].map((l) => siegeModifier('infantry', l));
    for (let i = 0; i < vals.length - 1; i++) {
      expect(vals[i]).toBeGreaterThan(vals[i + 1]);
    }
    expect(siegeModifier('infantry', 0)).toBe(siegeModifier('infantry', 1));
    expect(siegeModifier('infantry', 99)).toBe(siegeModifier('infantry', 5));
    expect(siegeModifier('infantry', 99)).toBeGreaterThan(0.2);
  });

  it('骑兵攻城劣于步兵', () => {
    for (let lvl = 1; lvl <= 5; lvl++) {
      expect(siegeModifier('cavalry', lvl)).toBeLessThan(siegeModifier('infantry', lvl));
    }
  });

  it('就地取给阈值明确', () => {
    expect(supplyCapacity(0, 1000)).toBe(false);
    expect(supplyCapacity(9.9, 1000)).toBe(false);
    expect(supplyCapacity(10, 1000)).toBe(true);
    expect(supplyCapacity(100, 1000)).toBe(true);
  });

  it('全组合无 NaN/Inf', () => {
    for (const a of ARMY_CLASS) {
      for (const d of ARMY_CLASS) {
        for (const t of Object.keys(TERRAINS)) {
          expect(Number.isFinite(damagePerTurn(soldier(a), soldier(d), terrain(t)))).toBe(true);
          expect(Number.isFinite(casualtyRate(soldier(a), soldier(d), terrain(t)))).toBe(true);
          expect(Number.isFinite(effectivePower(soldier(a), terrain(t)))).toBe(true);
        }
      }
    }
  });

  it('未知配置显式报错', () => {
    expect(() => soldier('dragon')).toThrow();
    expect(() => terrain('lava')).toThrow();
  });

  it('兵种面板设计意图一致', () => {
    expect(SOLDIERS.cavalry.str).toBeGreaterThan(SOLDIERS.infantry.str);
    expect(SOLDIERS.infantry.str).toBeGreaterThan(SOLDIERS.archer.str);
    expect(SOLDIERS.infantry.deff).toBeGreaterThan(SOLDIERS.cavalry.deff);
    expect(SOLDIERS.cavalry.deff).toBeGreaterThan(SOLDIERS.archer.deff);
    expect(SOLDIERS.archer.baseHit).toBeGreaterThan(SOLDIERS.infantry.baseHit);
    expect(SOLDIERS.infantry.baseHit).toBeGreaterThan(SOLDIERS.cavalry.baseHit);
    expect(SOLDIERS.cavalry.sta).toBeGreaterThan(SOLDIERS.infantry.sta);
    expect(SOLDIERS.infantry.sta).toBeGreaterThan(SOLDIERS.archer.sta);
    expect(SOLDIERS.cavalry.cost).toBe(SOLDIERS.infantry.cost * 2);
  });

  it('弓兵是脆皮输出', () => {
    const arch = soldier('archer');
    const cav = soldier('cavalry');
    const t = terrain('plain');
    expect(hitRate(arch, t)).toBeGreaterThan(hitRate(cav, t));
    expect(casualtyRate(cav, arch, t)).toBeGreaterThan(casualtyRate(arch, cav, t));
  });

  it('DAMAGE_SCALE 与 SOLDIER_HP 同源一致', () => {
    const a = soldier('infantry');
    const t = terrain('plain');
    const perTurn = damagePerTurn(a, a, t);
    expect(casualtyRate(a, a, t)).toBeCloseTo(perTurn / SOLDIER_HP, 12);
    expect(turnsToKill(a, a, t)).toBeCloseTo(1 / casualtyRate(a, a, t), 9);
    expect(DAMAGE_SCALE).toBeGreaterThan(0);
    expect(ROUT_STRENGTH).toBeGreaterThan(0);
    expect(ROUT_STRENGTH).toBeLessThan(1);
  });
});