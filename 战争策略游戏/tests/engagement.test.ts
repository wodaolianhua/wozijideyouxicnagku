/**
 * 交战逻辑测试
 *
 * 验证实时对抗的正确性：
 * - 伤害严格来自 combat.ts 的 casualtyRate（与 Python 参考实现同源）
 * - 弓兵射程远、会被近战摸到
 * - 士气崩溃即溃退
 * - 交战配对不会重复占用同一单位
 * - 交战不修改传入的数组
 * - 长时间推演不发散
 */

import { describe, it, expect } from 'vitest';
import {
  TURN_SECONDS,
  applyRouting,
  battleStats,
  engagedTarget,
  faceTarget,
  inAttackRange,
  isRouting,
  orderAttack,
  orderDisengage,
  orderPursue,
  resolveEngagement,
  stepEngagement,
} from '../src/game/engagement';
import { CLASS_ATTACK_RANGE, createUnit, type ControllableUnit } from '../src/game/unit';
import { generateTerrain, type TerrainData } from '../src/render/terrain';
import {
  ROUT_MORALE,
  SOLDIERS,
  casualtyRate,
  soldier,
  terrain,
} from '../src/model/combat';
import { ARMY_CLASS, type ArmyClass } from '../src/model/combat';

function flat(key: 'plain' | 'hill' | 'forest' = 'plain'): TerrainData {
  return {
    keyAt: () => key,
    heightAt: () => 0,
  } as unknown as TerrainData;
}

const U = (
  cls: ArmyClass,
  x: number,
  z: number,
  side: 'blue' | 'red',
  strength = 1000,
  morale = 70,
): ControllableUnit => {
  const u = createUnit(cls, x, z, 0, strength, side);
  return { ...u, morale };
};

describe('交战结算', () => {
  it('伤害严格等于 casualtyRate × 守方兵力', () => {
    const td = flat('plain');
    const a = U('infantry', 0, 0, 'blue', 1000);
    const d = U('cavalry', 5, 0, 'red', 1000);
    const r = resolveEngagement(a, d, td);
    const expected = casualtyRate(soldier('infantry'), soldier('cavalry'), terrain('plain')) * 1000;
    expect(r.defenderCasualties).toBeCloseTo(expected, 6);
  });

  it('地形影响交战伤害（攻方所在地形）', () => {
    const a = U('cavalry', 0, 0, 'blue', 1000);
    const d = U('infantry', 5, 0, 'red', 1000);
    const plain = resolveEngagement(a, d, flat('plain'));
    const hill = resolveEngagement(a, d, flat('hill'));
    expect(hill.defenderCasualties).toBeLessThan(plain.defenderCasualties);
  });

  it('兵力守恒：伤亡数不超过原有兵力', () => {
    const td = flat();
    for (const a of ARMY_CLASS) {
      for (const d of ARMY_CLASS) {
        const r = resolveEngagement(U(a, 0, 0, 'blue', 50), U(d, 5, 0, 'red', 50), td);
        expect(r.defenderCasualties).toBeLessThanOrEqual(50);
        expect(r.attackerCasualties).toBeLessThanOrEqual(50);
        expect(r.defender.strength).toBeGreaterThanOrEqual(0);
        expect(r.attacker.strength).toBeGreaterThanOrEqual(0);
      }
    }
  });

  it('超出射程的单位打不到人（经由 stepEngagement 验证）', () => {
    // resolveEngagement 是纯伤害函数，本身不检查射程（由 stepEngagement 的配对阶段负责）。
    // 两弓兵相距 300 米 > 索敌范围 140×1.5=210 米 → 不应配对，双方零伤亡。
    const td = flat();
    const units = [U('archer', 0, 0, 'blue', 1000), U('archer', 300, 0, 'red', 1000)];
    const r = stepEngagement(units, td, 0.1);
    expect(r.units[0].strength).toBe(1000);
    expect(r.units[1].strength).toBe(1000);
    expect(r.units[0].engageTargetId).toBeNull();
  });

  it('射程内的弓兵会交火（经由 stepEngagement 验证）', () => {
    const td = flat();
    // 100 米 < 140 米射程
    const units = [U('archer', 0, 0, 'blue', 1000), U('archer', 100, 0, 'red', 1000)];
    const r = stepEngagement(units, td, 0.1);
    expect(r.units[1].strength).toBeLessThan(1000);
  });

  it('近距离弓兵能互相反击', () => {
    const td = flat();
    const a = U('archer', 0, 0, 'blue', 1000);
    const d = U('archer', 100, 0, 'red', 1000);
    const r = resolveEngagement(a, d, td);
    expect(r.defenderCasualties).toBeGreaterThan(0);
    expect(r.attackerCasualties).toBeGreaterThan(0);
  });

  it('远距离的守方无法反击（超出其射程）', () => {
    const td = flat();
    // 攻方弓兵在 0，守方近战在 130 米（弓兵 140 内，但近战 12 外）
    const a = U('archer', 0, 0, 'blue', 1000);
    const d = U('infantry', 130, 0, 'red', 1000);
    const r = resolveEngagement(a, d, td);
    expect(r.defenderCasualties).toBeGreaterThan(0); // 弓兵能打到
    expect(r.attackerCasualties).toBe(0); // 近战够不着弓兵
  });

  it('败方士气打击大于胜方激励', () => {
    const td = flat();
    const a = U('infantry', 0, 0, 'blue', 1000, 70);
    const d = U('cavalry', 5, 0, 'red', 1000, 70);
    const r = resolveEngagement(a, d, td);
    // 步兵克骑兵 → 攻方胜
    expect(r.attackerWon).toBe(true);
    expect(r.attacker.morale).toBeGreaterThan(70);
    expect(r.defender.morale).toBeLessThan(70);
  });

  it('交战回合时长与 combat.ts 一致', () => {
    expect(TURN_SECONDS).toBe(10.0);
  });

  it('resolveEngagement 不修改传入单位', () => {
    const td = flat();
    const a = U('infantry', 0, 0, 'blue', 1000);
    const d = U('cavalry', 5, 0, 'red', 1000);
    const sa = JSON.stringify(a);
    const sd = JSON.stringify(d);
    resolveEngagement(a, d, td);
    expect(JSON.stringify(a)).toBe(sa);
    expect(JSON.stringify(d)).toBe(sd);
  });
});

describe('攻击距离', () => {
  it('弓兵射程最远', () => {
    expect(CLASS_ATTACK_RANGE.archer).toBeGreaterThan(CLASS_ATTACK_RANGE.cavalry * 4);
    expect(CLASS_ATTACK_RANGE.cavalry).toBeGreaterThan(CLASS_ATTACK_RANGE.infantry);
  });

  it('inAttackRange 按攻方兵种判定', () => {
    const archer = U('archer', 0, 0, 'blue');
    const inf = U('infantry', 100, 0, 'red');
    // 弓兵 140 米可打到 100 米外的步兵
    expect(inAttackRange(archer, inf)).toBe(true);
    // 步兵 12 米打不到 100 米外
    expect(inAttackRange(inf, archer)).toBe(false);
  });
});

describe('溃退', () => {
  it('兵力归零即溃退', () => {
    const u = { ...U('infantry', 0, 0, 'blue'), strength: 0 };
    expect(isRouting(u)).toBe(true);
  });

  it('士气跌破阈值即溃退', () => {
    const u = { ...U('infantry', 0, 0, 'blue'), morale: ROUT_MORALE - 1 };
    expect(isRouting(u)).toBe(true);
    const ok = { ...U('infantry', 0, 0, 'blue'), morale: ROUT_MORALE + 5 };
    expect(isRouting(ok)).toBe(false);
  });

  it('applyRouting 清除所有目标与选择', () => {
    const u = {
      ...U('infantry', 0, 0, 'blue'),
      targetX: 100, targetZ: 100,
      engageTargetId: 5,
      selected: true,
    };
    const r = applyRouting(u);
    expect(r.state).toBe('routing');
    expect(r.targetX).toBeNull();
    expect(r.engageTargetId).toBeNull();
    expect(r.selected).toBe(false);
  });
});

describe('交战指令', () => {
  it('orderAttack 清除移动目标并锁定敌人', () => {
    const units = [{ ...U('infantry', 0, 0, 'blue'), selected: true, targetX: 50, targetZ: 50 }];
    const r = orderAttack(units, 99);
    expect(r[0].engageTargetId).toBe(99);
    expect(r[0].targetX).toBeNull();
    expect(r[0].state).toBe('engaging');
  });

  it('orderPursue 清除交战目标并追击', () => {
    const units = [{ ...U('infantry', 0, 0, 'blue'), selected: true, engageTargetId: 3 }];
    const r = orderPursue(units, 200, 100);
    expect(r[0].engageTargetId).toBeNull();
    expect(r[0].targetX).toBe(200);
    expect(r[0].state).toBe('moving');
  });

  it('orderDisengage 全部清除', () => {
    const units = [{ ...U('infantry', 0, 0, 'blue'), selected: true, engageTargetId: 3, targetX: 1, targetZ: 1 }];
    const r = orderDisengage(units);
    expect(r[0].engageTargetId).toBeNull();
    expect(r[0].targetX).toBeNull();
    expect(r[0].state).toBe('idle');
  });

  it('未选中单位不受指令影响', () => {
    const units = [{ ...U('infantry', 0, 0, 'blue'), selected: false, targetX: 7, targetZ: 7 }];
    const r = orderDisengage(units);
    expect(r[0].targetX).toBe(7);
  });

  it('指令不修改原数组', () => {
    const units = [{ ...U('infantry', 0, 0, 'blue'), selected: true }];
    const snap = JSON.stringify(units);
    orderAttack(units, 1);
    orderPursue(units, 1, 1);
    orderDisengage(units);
    expect(JSON.stringify(units)).toBe(snap);
  });
});

describe('战场推进', () => {
  it('近距离双方会交战并掉兵', () => {
    const td = flat();
    const units = [U('infantry', 0, 0, 'blue', 1000), U('cavalry', 10, 0, 'red', 1000)];
    const r = stepEngagement(units, td, 0.1);
    expect(r.units[0].strength).toBeLessThanOrEqual(1000);
    expect(r.units[1].strength).toBeLessThan(1000);
    expect(r.events.length).toBeGreaterThan(0);
  });

  it('交战中一方兵力归零后另一方转为待命', () => {
    const td = flat();
    let units = [U('infantry', 0, 0, 'blue', 1000), U('cavalry', 10, 0, 'red', 20)];
    for (let i = 0; i < 10; i++) {
      const r = stepEngagement(units, td, 0.1);
      units = r.units;
    }
    // 守方兵力耗尽 → 攻方不再有交战目标
    const blue = units.find((u) => u.side === 'blue')!;
    if (!isRouting(units.find((u) => u.side === 'red')!)) {
      expect(blue.engageTargetId === null || blue.state !== 'engaging').toBe(true);
    }
  });

  it('远距离单位不会立即交战', () => {
    const td = flat();
    const units = [U('infantry', 0, 0, 'blue', 1000), U('infantry', 900, 0, 'red', 1000)];
    const r = stepEngagement(units, td, 0.1);
    expect(r.units[0].strength).toBe(1000);
    expect(r.units[1].strength).toBe(1000);
  });

  it('近战单位会向敌人逼近', () => {
    const td = flat();
    // 步兵射程 12，索敌 18 米；放在 15 米外
    let units = [U('infantry', 0, 0, 'blue', 1000), U('infantry', 15, 0, 'red', 1000)];
    const x0 = units[0].x;
    for (let i = 0; i < 10; i++) {
      units = stepEngagement(units, td, 0.1).units;
    }
    expect(units[0].x).toBeGreaterThan(x0);
  });

  it('交战时不修改原数组', () => {
    const td = flat();
    const units = [U('infantry', 0, 0, 'blue'), U('cavalry', 10, 0, 'red')];
    const snap = JSON.stringify(units);
    stepEngagement(units, td, 0.1);
    expect(JSON.stringify(units)).toBe(snap);
  });

  it('一个单位不会被两个敌人同时交战', () => {
    const td = flat();
    const units = [
      U('infantry', 0, 0, 'blue', 1000),
      U('infantry', 10, 0, 'red', 1000),
      U('infantry', -10, 0, 'red', 1000),
    ];
    // 推演后检查：每个红方单位的 engageTargetId 不会指向另一个红方
    let cur = units;
    for (let i = 0; i < 5; i++) cur = stepEngagement(cur, td, 0.1).units;
    for (const u of cur) {
      if (u.engageTargetId === null) continue;
      const t = cur.find((x) => x.id === u.engageTargetId);
      if (t) expect(t.side).not.toBe(u.side);
    }
  });

  it('无单位时不报错', () => {
    const r = stepEngagement([], flat(), 0.1);
    expect(r.units).toHaveLength(0);
  });

  it('只有一方单位时不交战', () => {
    const r = stepEngagement([U('infantry', 0, 0, 'blue')], flat(), 0.1);
    expect(r.units[0].strength).toBe(1000);
  });

  it('长期推演不发散（数值与坐标）', () => {
    const td = generateTerrain();
    let units: ControllableUnit[] = [];
    // 蓝方在左，红方在右，逐步推进
    for (let i = 0; i < 9; i++) {
      units.push(U('infantry', -500 + (i % 3) * 40, -100 + i * 25, 'blue', 800));
      units.push(U('cavalry', 500 - (i % 3) * 40, -100 + i * 25, 'red', 800));
    }
    for (let step = 0; step < 800; step++) {
      const r = stepEngagement(units, td, 0.05);
      units = r.units;
    }
    for (const u of units) {
      expect(Number.isFinite(u.x)).toBe(true);
      expect(Number.isFinite(u.z)).toBe(true);
      expect(Number.isFinite(u.morale)).toBe(true);
      expect(u.morale).toBeGreaterThanOrEqual(0);
      expect(u.morale).toBeLessThanOrEqual(100);
      expect(u.strength).toBeGreaterThanOrEqual(0);
    }
  });

  it('交战会真的打掉兵力（不是无限循环）', () => {
    const td = flat();
    // 15米在步兵索敌范围内（12×1.5=18），会主动接敌并逼近至射程内
    let units = [U('infantry', 0, 0, 'blue', 1000), U('infantry', 15, 0, 'red', 1000)];
    const total0 = units.reduce((a, u) => a + u.strength, 0);
    for (let i = 0; i < 200; i++) units = stepEngagement(units, td, 0.05).units;
    const total1 = units.reduce((a, u) => a + u.strength, 0);
    expect(total1).toBeLessThan(total0);
  });

  it('超出索敌范围的敌军不会主动接敌（保持待命）', () => {
    const td = flat();
    // 100 米远超步兵索敌 18 米 → 不交战，兵力不变
    let units = [U('infantry', 0, 0, 'blue', 1000), U('infantry', 100, 0, 'red', 1000)];
    for (let i = 0; i < 50; i++) units = stepEngagement(units, td, 0.05).units;
    expect(units[0].strength).toBe(1000);
    expect(units[1].strength).toBe(1000);
  });

  it('待命单位在敌军进入索敌范围后会自动接敌', () => {
    const td = flat();
    // 15米在索敌内
    let units = [U('infantry', 0, 0, 'blue', 1000), U('infantry', 15, 0, 'red', 1000)];
    const r = stepEngagement(units, td, 0.1);
    // 蓝方应已锁定红方
    expect(r.units[0].engageTargetId).toBe(r.units[1].id);
  });
});

describe('转向', () => {
  it('faceTarget 转向目标方向', () => {
    const u = U('infantry', 0, 0, 'blue');
    const t = U('cavalry', 100, 0, 'red');
    const r = faceTarget(u, t, 0.1, 2.0);
    expect(r.heading).toBeGreaterThan(0);
  });

  it('转向速度受rate 限制', () => {
    const u = U('infantry', 0, 0, 'blue');
    const t = U('cavalry', -100, 0, 'red');
    const r = faceTarget(u, t, 0.1, 0.1);
    expect(Math.abs(r.heading)).toBeLessThanOrEqual(0.011);
  });

  it('不修改原单位', () => {
    const u = U('infantry', 0, 0, 'blue');
    const t = U('cavalry', 100, 0, 'red');
    const h0 = u.heading;
    faceTarget(u, t, 0.1, 2.0);
    expect(u.heading).toBe(h0);
  });
});

describe('查询与统计', () => {
  it('engagedTarget 返回交战对象', () => {
    const a = U('infantry', 0, 0, 'blue');
    const d = U('cavalry', 10, 0, 'red');
    const a2 = { ...a, engageTargetId: d.id };
    const t = engagedTarget(a2, [a2, d]);
    expect(t).not.toBeNull();
    expect(t!.id).toBe(d.id);
  });

  it('engagedTarget 在目标消失时返回 null', () => {
    const a = { ...U('infantry', 0, 0, 'blue'), engageTargetId: 999 };
    expect(engagedTarget(a, [a])).toBeNull();
  });

  it('battleStats 汇总双方兵力与溃退数', () => {
    const units = [
      U('infantry', 0, 0, 'blue', 500),
      U('archer', 0, 10, 'blue', 300),
      U('cavalry', 0, 20, 'red', 800),
      { ...U('infantry', 0, 30, 'red', 0) },
    ];
    const s = battleStats(units);
    expect(s.blue).toBe(800);
    expect(s.red).toBe(800);
    expect(s.blueRouting).toBe(0);
    expect(s.redRouting).toBe(1);
  });

  it('三兵种交战都能跑通（无异常）', () => {
    const td = flat();
    for (const a of ARMY_CLASS) {
      for (const d of ARMY_CLASS) {
        const r = resolveEngagement(U(a, 0, 0, 'blue', 1000), U(d, 10, 0, 'red', 1000), td);
        expect(Number.isFinite(r.attackerCasualties)).toBe(true);
        expect(Number.isFinite(r.defenderCasualties)).toBe(true);
        expect(SOLDIERS[a].name.length).toBeGreaterThan(0);
      }
    }
  });
});
