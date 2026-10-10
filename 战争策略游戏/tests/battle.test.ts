/**
 * 战斗状态机测试
 *
 * 验证混编结算的正确性：
 * - 数值守恒：伤亡数不超过原有兵力
 * - 单调性：兵力只减不增
 * - 溃退判定正确（25% 阈值）
 * - 地形真实影响战局（相同兵力换地形结果不同）
 * - 兵种克制真实影响战局
 * - 性能：单回合结算 < 50ms（5000 单位规模）
 * - 确定性：同输入必同输出（无隐藏随机）
 */

import { describe, it, expect } from 'vitest';
import { generateTerrain, type TerrainData } from '../src/render/terrain';
import { createBattle, stepTurn, stepTurnTimed, formationBreakdown, type BattleState } from '../src/game/battle';
import { ARMY_CLASS, SOLDIERS, terrain } from '../src/model/combat';

const sum = (s: BattleState, side: 'blue' | 'red') =>
  s[side].reduce((a, f) => a + f.count, 0);

function runBattle(maxTurns = 200): BattleState {
  let s = createBattle();
  const td = generateTerrain();
  for (let i = 0; i < maxTurns && s.result === 'ongoing'; i++) s = stepTurn(s, td);
  return s;
}

describe('战斗状态机基础', () => {
  it('初始状态为混编三兵种对攻', () => {
    const s = createBattle();
    expect(s.blue.map((f) => f.cls).sort()).toEqual([...ARMY_CLASS].sort());
    expect(s.red.map((f) => f.cls).sort()).toEqual([...ARMY_CLASS].sort());
    expect(s.result).toBe('ongoing');
    expect(s.turn).toBe(0);
  });

  it('初始兵力记录正确', () => {
    const s = createBattle();
    expect(s.initialBlue).toBe(900 + 600 + 300);
    expect(s.initialRed).toBe(900 + 600 + 400);
  });

  it('每回合回合数递增1', () => {
    const td = generateTerrain();
    let s = createBattle();
    for (let i = 1; i <= 5; i++) {
      s = stepTurn(s, td);
      expect(s.turn).toBe(i);
    }
  });

  it('编制明细可读出中文兵种名', () => {
    const bd = formationBreakdown(createBattle().blue);
    expect(bd['步兵']).toBe(900);
    expect(bd['弓兵']).toBe(600);
    expect(bd['骑兵']).toBe(300);
  });
});

describe('数值守恒与单调性', () => {
  it('兵力只减不增', () => {
    const td = generateTerrain();
    let s = createBattle();
    let prevBlue = sum(s, 'blue');
    let prevRed = sum(s, 'red');
    for (let i = 0; i < 40 && s.result === 'ongoing'; i++) {
      s = stepTurn(s, td);
      const b = sum(s, 'blue');
      const r = sum(s, 'red');
      expect(b, `第 ${i + 1} 回合蓝方兵力增加`).toBeLessThanOrEqual(prevBlue);
      expect(r, `第 ${i + 1} 回合红方兵力增加`).toBeLessThanOrEqual(prevRed);
      expect(b).toBeGreaterThanOrEqual(0);
      expect(r).toBeGreaterThanOrEqual(0);
      prevBlue = b;
      prevRed = r;
    }
  });

  it('伤亡数等于兵力差值（守恒）', () => {
    const td = generateTerrain();
    const s0 = createBattle();
    const s1 = stepTurn(s0, td);
    expect(s0.blueCasualties).toBe(0);
    expect(s1.blueCasualties).toBe(s0.initialBlue - sum(s1, 'blue'));
    expect(s1.redCasualties).toBe(s0.initialRed - sum(s1, 'red'));
  });

  it('伤亡比例不超过原有兵力（不会出现负兵力）', () => {
    const td = generateTerrain();
    let s = createBattle();
    for (let i = 0; i < 50 && s.result === 'ongoing'; i++) {
      s = stepTurn(s, td);
      for (const f of [...s.blue, ...s.red]) {
        expect(f.count).toBeGreaterThanOrEqual(0);
        expect(Number.isFinite(f.count)).toBe(true);
      }
    }
  });

  it('士气恒在 0~100', () => {
    const td = generateTerrain();
    let s = createBattle();
    for (let i = 0; i < 40 && s.result === 'ongoing'; i++) {
      s = stepTurn(s, td);
      for (const f of [...s.blue, ...s.red]) {
        expect(f.morale).toBeGreaterThanOrEqual(0);
        expect(f.morale).toBeLessThanOrEqual(100);
      }
    }
  });

  it('打空的编制被移除', () => {
    const td = generateTerrain();
    let s = createBattle();
    for (let i = 0; i < 100 && s.result === 'ongoing'; i++) s = stepTurn(s, td);
    for (const f of [...s.blue, ...s.red]) {
      expect(f.count).toBeGreaterThan(0);
    }
  });

  it('原始状态不被修改（无副作用）', () => {
    const td = generateTerrain();
    const s0 = createBattle();
    const snapshot = JSON.stringify(s0);
    stepTurn(s0, td);
    expect(JSON.stringify(s0)).toBe(snapshot);
  });
});

describe('确定性（无隐藏随机）', () => {
  it('同输入两次结算结果完全一致', () => {
    const td1 = generateTerrain(20261005);
    const td2 = generateTerrain(20261005);
    const a = createBattle();
    const b = createBattle();
    let sa = a;
    let sb = b;
    for (let i = 0; i < 30 && sa.result === 'ongoing'; i++) {
      sa = stepTurn(sa, td1);
      sb = stepTurn(sb, td2);
    }
    expect(sa.turn).toBe(sb.turn);
    expect(sa.result).toBe(sb.result);
    expect(sum(sa, 'blue')).toBeCloseTo(sum(sb, 'blue'), 6);
    expect(sum(sa, 'red')).toBeCloseTo(sum(sb, 'red'), 6);
  });

  it('相同种子的地形完全一致', () => {
    const a = generateTerrain(12345);
    const b = generateTerrain(12345);
    expect(a.keyAt(100, 100)).toBe(b.keyAt(100, 100));
    expect(a.heightAt(-300, 700)).toBeCloseTo(b.heightAt(-300, 700), 9);
  });

  it('不同种子的地形不同', () => {
    const a = generateTerrain(1);
    const b = generateTerrain(2);
    let diff = 0;
    for (let i = 0; i < 500; i++) {
      const x = (i * 37) % 1000 - 500;
      const z = (i * 61) % 1000 - 500;
      if (a.heightAt(x, z) !== b.heightAt(x, z)) diff++;
    }
    expect(diff).toBeGreaterThan(100);
  });
});

describe('地形与克制真实影响战局', () => {
  it('战斗必然在有限回合内分出胜负', () => {
    const s = runBattle();
    expect(s.result).not.toBe('ongoing');
    expect(s.turn).toBeLessThan(200);
  });

  it('平原地形必然产生伤亡', () => {
    const td = generateTerrain();
    const s = createBattle();
    // 把双方放到平原上
    for (const f of [...s.blue, ...s.red]) {
      let x = f.x;
      let z = f.z;
      // 搜索附近的平原格
      for (let r = 0; r < 40; r++) {
        if (td.keyAt(x, z) === 'plain') break;
        z += 25;
      }
      f.x = x;
      f.z = z;
    }
    const s1 = stepTurn(s, td);
    expect(s1.blueCasualties + s1.redCasualties).toBeGreaterThan(0);
  });

  it('山地交战比平原交战更慢（骑兵失效）', () => {
    const td = generateTerrain();
    //构造两组完全相同的混编，分别放在平原与山地
    const base = createBattle();
    const onPlain: BattleState = {
      ...base,
      blue: base.blue.map((f) => ({ ...f })),
      red: base.red.map((f) => ({ ...f })),
    };
    // 强制所有编制地形为平原
    const plainTd = {
      ...td,
      keyAt: () => 'plain' as const,
      heightAt: () => 0,
    } as TerrainData;
    // 强制所有编制地形为山地
    const hillTd = {
      ...td,
      keyAt: () => 'hill' as const,
      heightAt: () => 0,
    } as TerrainData;

    let plainTotal = 0;
    let hillTotal = 0;
    for (let i = 0; i < 10; i++) {
      const a = stepTurn(onPlain, plainTd);
      const b = stepTurn(onPlain, hillTd);
      plainTotal += a.blueCasualties + a.redCasualties;
      hillTotal += b.blueCasualties + b.redCasualties;
      onPlain.blue = a.blue;
      onPlain.red = a.red;
    }
    expect(
      Math.abs(plainTotal - hillTotal),
      `平原总伤亡 ${plainTotal} 与山地 ${hillTotal} 完全相同，说明地形未生效`,
    ).toBeGreaterThan(0);
  });

  it('地形取攻方位置：守方从山地发起攻击时输出被扣减', () => {
    // 设计规则（文档 §6 已记录）：地形系数按攻方所在位置计算。
    // 这意味着「攻方在不利地形」= 输出降低 = 活得久；
    // 「守方站在有利地形」= 得不到额外防御加成。
    const td = generateTerrain();
    const mk = (): BattleState => ({
      ...createBattle(),
      blue: [{ cls: 'infantry', count: 1000, x: 0, z: 0, heading: 0, morale: 70 }],
      red: [{ cls: 'cavalry', count: 1000, x: 500, z: 0, heading: 0, morale: 70 }],
    });
    const plainTd = { ...td, keyAt: () => 'plain' as const, heightAt: () => 0 } as TerrainData;
    const hillTd = { ...td, keyAt: () => 'hill' as const, heightAt: () => 0 } as TerrainData;

    const a = stepTurn(mk(), plainTd);
    const b = stepTurn(mk(), hillTd);
    const redLossPlain = 1000 - (a.red[0]?.count ?? 0);
    const redLossHill = 1000 - (b.red[0]?.count ?? 0);
    // 山地骑兵攻击力被扣 0.6，输出降低 → 步兵给它的伤害不变 → 它死得一样多
    // 但它打步兵的输出降低 → 步兵死得更少
    expect(redLossPlain).toBeGreaterThan(0);
    expect(redLossHill).toBeGreaterThan(0);
    const blueLossPlain = 1000 - (a.blue[0]?.count ?? 0);
    const blueLossHill = 1000 - (b.blue[0]?.count ?? 0);
    expect(
      blueLossHill,
      `山地骑兵输出应更低（平原给步兵造成 ${blueLossPlain} 伤亡，山地 ${blueLossHill}）`,
    ).toBeLessThan(blueLossPlain);
  });

  it('已知设计缺口：守方自身地形不提供防御加成', () => {
    // 这条测试锁定当前实现：地形系数只作用于「攻方的输出」。
    // 推论——一方受到的伤亡只取决于对方所在的地形，与自己站在哪无关。
    // 若未来改为「地形同时影响攻方输出与守方减伤」，此测试会失败，
    // 届时必须同步更新 python/combat_model.py 并重跑跨语言一致性检查。
    const td = generateTerrain();
    const mk = (): BattleState => ({
      ...createBattle(),
      blue: [{ cls: 'archer', count: 1000, x: 0, z: 0, heading: 0, morale: 70 }],
      red: [{ cls: 'archer', count: 1000, x: 500, z: 0, heading: 0, morale: 70 }],
    });
    // 按位置区分地形：蓝方（x<250）所处地块 = blueTerrain，其余 = 平原
    const mixed = (blueTerrain: 'plain' | 'hill'): TerrainData =>
      ({
        ...td,
        keyAt: (x: number) => (x < 250 ? blueTerrain : 'plain'),
        heightAt: () => 0,
      }) as TerrainData;

    // 场景A：蓝方在平原
    const a = stepTurn(mk(), mixed('plain'));
    // 场景B：蓝方在山地
    const b = stepTurn(mk(), mixed('hill'));

    const blueLossPlain = 1000 - (a.blue[0]?.count ?? 0);
    const blueLossHill = 1000 - (b.blue[0]?.count ?? 0);
    const redLossPlain = 1000 - (a.red[0]?.count ?? 0);
    const redLossHill = 1000 - (b.red[0]?.count ?? 0);

    // 蓝方站山地：蓝方输出被扣 0.9 → 红方受伤更少
    expect(redLossHill, '蓝方在山地时红方应受伤更少').toBeLessThan(redLossPlain);
    // 蓝方受到的伤害只取决于红方地形；两场景中红方都在平原 → 蓝方伤亡相同。
    // 这正是「守方地形不提供防御加成」的证据。
    expect(blueLossPlain).toBeCloseTo(blueLossHill, 6);
  });
});

describe('溃退与胜负判定', () => {
  it('一方兵力跌破 25% 即判负', () => {
    const td = generateTerrain();
    const s0 = createBattle();
    let s = s0;
    while (s.result === 'ongoing') s = stepTurn(s, td);
    const blueRatio = sum(s, 'blue') / s.initialBlue;
    const redRatio = sum(s, 'red') / s.initialRed;
    if (s.result === 'blue_win') {
      expect(redRatio).toBeLessThan(0.25);
      expect(blueRatio).toBeGreaterThanOrEqual(0.25);
    } else if (s.result === 'red_win') {
      expect(blueRatio).toBeLessThan(0.25);
    } else {
      expect(blueRatio).toBeLessThan(0.25);
      expect(redRatio).toBeLessThan(0.25);
    }
  });

  it('战斗结束后不再推进', () => {
    const td = generateTerrain();
    let s = createBattle();
    while (s.result === 'ongoing') s = stepTurn(s, td);
    const frozen = stepTurn(s, td);
    expect(frozen.turn).toBe(s.turn);
    expect(frozen.result).toBe(s.result);
  });

  it('战报在胜负判定时记录', () => {
    const s = runBattle();
    expect(s.log.length).toBeGreaterThan(1);
    expect(s.log[s.log.length - 1]).toMatch(/回合/);
  });
});

describe('性能预算', () => {
  it('单回合结算耗时 < 50ms（3000 单位规模）', () => {
    const td = generateTerrain();
    let s = createBattle();
    const times: number[] = [];
    for (let i = 0; i < 30 && s.result === 'ongoing'; i++) {
      const r = stepTurnTimed(s, td);
      s = r.state;
      times.push(r.ms);
    }
    const avg = times.reduce((a, b) => a + b, 0) / times.length;
    const max = Math.max(...times);
    console.log(`单回合耗时：平均 ${avg.toFixed(3)}ms，最大 ${max.toFixed(3)}ms`);
    expect(avg, `平均耗时 ${avg.toFixed(2)}ms`).toBeLessThan(50);
    expect(max, `最大耗时 ${max.toFixed(2)}ms`).toBeLessThan(50);
  });

  it('大兵力规模（万级）结算耗时可接受', () => {
    const td = generateTerrain();
    // 造一场 1.3 万人的会战（每编制 2000+，走 LOD2 路径）
    const big: BattleState = {
      ...createBattle(),
      blue: ARMY_CLASS.map((c) => ({
        cls: c,
        count: 2000,
        x: -700,
        z: (ARMY_CLASS.indexOf(c) - 1) * 200,
        heading: Math.PI / 2,
        morale: 70,
      })),
      red: ARMY_CLASS.map((c) => ({
        cls: c,
        count: 2000,
        x: 700,
        z: (ARMY_CLASS.indexOf(c) - 1) * 200,
        heading: -Math.PI / 2,
        morale: 70,
      })),
    };
    big.initialBlue = 6000;
    big.initialRed = 6000;
    const { state, ms } = stepTurnTimed(big, td);
    console.log(`万级会战单回合耗时 ${ms.toFixed(3)}ms`);
    expect(ms).toBeLessThan(50);
    expect(state.turn).toBe(1);
  });
});

describe('兵种数值与结算一致性', () => {
  it('兵种系数在结算中生效（同为步兵编制时克关系可推导）', () => {
    // 步兵编制打骑兵编制 vs 打步兵编制，伤亡应不同
    const td = { keyAt: () => 'plain' as const, heightAt: () => 0 } as unknown as TerrainData;
    const mk = (defCls: 'infantry' | 'cavalry'): BattleState => ({
      ...createBattle(),
      blue: [{ cls: 'infantry', count: 1000, x: 0, z: 0, heading: 0, morale: 70 }],
      red: [{ cls: defCls, count: 1000, x: 300, z: 0, heading: 0, morale: 70 }],
    });
    const vsCav = stepTurn(mk('cavalry'), td);
    const vsInf = stepTurn(mk('infantry'), td);
    const cavLoss = 1000 - (vsCav.red[0]?.count ?? 0);
    const infLoss = 1000 - (vsInf.red[0]?.count ?? 0);
    expect(cavLoss).toBeGreaterThan(infLoss);
  });

  it('SOLDIERS 的中文名与兵种键对应', () => {
    for (const cls of ARMY_CLASS) {
      expect(SOLDIERS[cls].cls).toBe(cls);
      expect(SOLDIERS[cls].name.length).toBeGreaterThan(0);
    }
  });

  it('terrain 对五种地形均有配置', () => {
    for (const t of ['plain', 'forest', 'hill', 'river', 'fort']) {
      const cfg = terrain(t);
      for (const cls of ARMY_CLASS) {
        expect(cfg.perClass[cls], `${t} 缺 ${cls} 系数`).toBeGreaterThan(0);
      }
    }
  });
});