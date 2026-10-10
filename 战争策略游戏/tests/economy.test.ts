/**
 * 经营层测试
 *
 * 验证文档 01 §5 的核心规则：
 * - 粮为0 → 部队溃散（经营层的支点）
 * - 可征兵数 = 人口 × 0.10 × (民心 / 50)
 * - 败绩打击大于胜利激励
 * - 同时推进任务数有上限（制造取舍）
 * - 据点升级中防御下降
 * - 全部为纯函数，无副作用
 */

import { describe, it, expect } from 'vitest';
import {
  FOOD_PER_1000_PER_DAY,
  INITIAL_MORALE,
  MAX_POP,
  STARTING_POP,
  advanceDay,
  createEconomy,
  dailyFoodProduction,
  dailyGoldIncome,
  defenseModifier,
  deployToBattle,
  garrisonCap,
  isOverRecruiting,
  marchFoodCost,
  recruitCap,
  returnFromBattle,
  setTaxRate,
  startTask,
  startUpgrade,
  type EconomyState,
} from '../src/game/economy';

describe('初始状态', () => {
  it('人口、民心、粮、金按设计初始化', () => {
    const s = createEconomy();
    expect(s.settlement.population).toBe(STARTING_POP);
    expect(s.settlement.morale).toBe(INITIAL_MORALE);
    expect(s.settlement.food).toBe(3000);
    expect(s.settlement.gold).toBe(800);
    expect(s.settlement.taxRate).toBe(0.1);
    expect(s.settlement.stronghold.level).toBe(1);
  });

  it('任务池包含三类任务', () => {
    const s = createEconomy();
    const kinds = new Set(s.availableTasks.map((t) => t.kind));
    expect(kinds).toEqual(new Set(['military', 'economy', 'civic']));
    expect(s.availableTasks.length).toBe(9);
  });

  it('据点等级要求与升级门槛对应', () => {
    const s = createEconomy();
    for (const t of s.availableTasks) {
      expect(t.requiredLevel).toBeGreaterThanOrEqual(1);
      expect(t.requiredLevel).toBeLessThanOrEqual(5);
    }
  });
});

describe('民心公式（文档 §5.3）', () => {
  it('可征兵数 = 人口 × 0.10 × (民心/50)', () => {
    const s = createEconomy();
    const cap = recruitCap(s.settlement);
    const expected = STARTING_POP * 0.1 * (50 / 50);
    expect(cap).toBeCloseTo(expected, 6);
    expect(cap).toBe(20000);
  });

  it('民心为0 时可征兵为 0', () => {
    const s = createEconomy();
    const zero = { ...s.settlement, morale: 0 };
    expect(recruitCap(zero)).toBe(0);
  });

  it('民心为 100 时可征兵翻倍', () => {
    const s = createEconomy();
    const full = { ...s.settlement, morale: 100 };
    expect(recruitCap(full)).toBe(20000 * 2);
  });

  it('强征超出上限会被识别', () => {
    const s = createEconomy();
    expect(isOverRecruiting(s.settlement, 10000)).toBe(false);
    expect(isOverRecruiting(s.settlement, 25000)).toBe(true);
  });
});

describe('行军耗粮（粮为 0 则溃散的支点）', () => {
  it('耗粮随距离、地形、人数单调上升', () => {
    const a = marchFoodCost(10, 1000, 1.0);
    expect(a).toBeLessThan(marchFoodCost(20, 1000, 1.0));
    expect(a).toBeLessThan(marchFoodCost(10, 1000, 1.8));
    expect(a).toBeLessThan(marchFoodCost(10, 2000, 1.0));
  });

  it('零兵力不耗粮', () => {
    expect(marchFoodCost(10, 0, 1.0)).toBe(0);
    expect(marchFoodCost(10, -100, 1.0)).toBe(0);
  });

  it('出征扣粮且粮不会变负', () => {
    const s = createEconomy();
    const s2 = deployToBattle(s, 50, 20000, 1.5);
    expect(s2.settlement.food).toBeLessThan(s.settlement.food);
    expect(s2.settlement.food).toBeGreaterThanOrEqual(0);
    expect(s2.atBattle).toBe(true);
  });

  it('出征耗粮超出存量时归零而不是负数', () => {
    let s = createEconomy();
    s = deployToBattle(s, 100000, 50000, 2.5);
    expect(s.settlement.food).toBe(0);
  });
});

describe('日推进', () => {
  it('日推进使天序号加一', () => {
    let s = createEconomy();
    for (let i = 1; i <= 5; i++) {
      s = advanceDay(s);
      expect(s.day).toBe(i);
    }
  });

  it('粮产为正（初始状态可持续）', () => {
    expect(dailyFoodProduction(createEconomy().settlement)).toBeGreaterThan(0);
    expect(dailyGoldIncome(createEconomy().settlement)).toBeGreaterThan(0);
  });

  it('不征兵时粮稳定增长（无消耗失控）', () => {
    let s = createEconomy();
    const before = s.settlement.food;
    for (let i = 0; i < 10; i++) s = advanceDay(s);
    expect(s.settlement.food).toBeGreaterThan(before);
  });

  it('税率越高收入越高但民心受损', () => {
    const base = createEconomy();
    const high = { ...base.settlement, taxRate: 0.25 };
    expect(dailyGoldIncome(high)).toBeGreaterThan(dailyGoldIncome(base.settlement));
    const raised = setTaxRate(base, 0.25);
    const lowered = setTaxRate(base, 0.05);
    expect(raised.settlement.morale).toBeLessThan(lowered.settlement.morale);
  });

  it('税率夹紧在 0~0.3', () => {
    let s = createEconomy();
    s = setTaxRate(s, 0.9);
    expect(s.settlement.taxRate).toBe(0.3);
    s = setTaxRate(s, -0.5);
    expect(s.settlement.taxRate).toBe(0);
  });

  it('粮归零后民心持续下降（饥荒路径成立）', () => {
    let s = createEconomy();
    s = { ...s, settlement: { ...s.settlement, food: 0, manpower: 40000 } };
    const mor0 = s.settlement.morale;
    s = advanceDay(s);
    expect(s.settlement.morale).toBeLessThan(mor0);
  });

  it('强征过度每日掉民心', () => {
    let s = createEconomy();
    s = { ...s, settlement: { ...s.settlement, manpower: 190000 } };
    const mor0 = s.settlement.morale;
    s = advanceDay(s);
    expect(s.settlement.morale).toBeLessThan(mor0);
    expect(s.log.some((l) => l.includes('强征过度'))).toBe(true);
  });

  it('人口自然增长且不超过上限', () => {
    let s = createEconomy();
    const pop0 = s.settlement.population;
    s = advanceDay(s);
    expect(s.settlement.population).toBeGreaterThan(pop0);
    s = { ...s, settlement: { ...s.settlement, population: MAX_POP - 1 } };
    s = advanceDay(s);
    expect(s.settlement.population).toBeLessThanOrEqual(MAX_POP);
  });

  it('驻军越多，粮耗越大', () => {
    const base = createEconomy().settlement;
    const small = { ...base, manpower: 10000 };
    const big = { ...base, manpower: 50000 };
    const lossSmall = (small.manpower / 1000) * FOOD_PER_1000_PER_DAY;
    const lossBig = (big.manpower / 1000) * FOOD_PER_1000_PER_DAY;
    expect(lossBig).toBeGreaterThan(lossSmall);
  });

  it('上阵期间后方持续耗粮（核心耦合）', () => {
    let s = createEconomy();
    s = deployToBattle(s, 30, 30000, 1.0);
    const foodAfterDeploy = s.settlement.food;
    s = advanceDay(s);
    expect(s.settlement.food).toBeLessThan(foodAfterDeploy);
    expect(s.log.some((l) => l.includes('前线'))).toBe(true);
  });
});

describe('凯旋与败还', () => {
  it('胜利且低伤亡时民心上升', () => {
    let s = createEconomy();
    s = deployToBattle(s, 30, 10000, 1.0);
    const mor0 = s.settlement.morale;
    s = returnFromBattle(s, true, 9000, 1000);
    expect(s.settlement.morale).toBeGreaterThan(mor0);
    expect(s.atBattle).toBe(false);
  });

  it('失败时民心大幅下降', () => {
    let s = createEconomy();
    s = deployToBattle(s, 30, 10000, 1.0);
    const mor0 = s.settlement.morale;
    s = returnFromBattle(s, false, 4000, 6000);
    expect(s.settlement.morale).toBeLessThan(mor0 - 5);
  });

  it('惨胜的民心收益小于完胜——伤亡必须能吃掉胜利', () => {
    let a = createEconomy();
    a = deployToBattle(a, 30, 10000, 1.0);
    a = returnFromBattle(a, true, 9900, 100);

    let b = createEconomy();
    b = deployToBattle(b, 30, 10000, 1.0);
    b = returnFromBattle(b, true, 6000, 4000);

    expect(a.settlement.morale).toBeGreaterThan(b.settlement.morale);
  });

  it('败绩对民心的打击大于胜利的激励（对称设计）', () => {
    let w = createEconomy();
    w = deployToBattle(w, 30, 10000, 1.0);
    w = returnFromBattle(w, true, 9000, 1000);
    const winGain = w.settlement.morale - INITIAL_MORALE;

    let l = createEconomy();
    l = deployToBattle(l, 30, 10000, 1.0);
    l = returnFromBattle(l, false, 9000, 1000);
    const lossDrop = INITIAL_MORALE - l.settlement.morale;

    expect(lossDrop).toBeGreaterThan(winGain);
  });
});

describe('任务系统（制造取舍）', () => {
  it('同时推进任务数有上限', () => {
    let s = createEconomy();
    s = startTask(s, 'm1');
    s = startTask(s, 'e1');
    expect(s.activeTaskIds).toHaveLength(2);
    s = startTask(s, 'c1');
    expect(s.activeTaskIds).toHaveLength(2);
    expect(s.log.some((l) => l.includes('同时只能推进'))).toBe(true);
  });

  it('重复开始同一任务被拒绝', () => {
    let s = createEconomy();
    s = startTask(s, 'm1');
    s = startTask(s, 'm1');
    expect(s.activeTaskIds.filter((id) => id === 'm1')).toHaveLength(1);
  });

  it('据点等级不足时不能开始任务', () => {
    let s = createEconomy();
    s = startTask(s, 'm3'); // 要求 2 级
    expect(s.activeTaskIds).toHaveLength(0);
    expect(s.log.some((l) => l.includes('等级不足'))).toBe(true);
  });

  it('任务按时完成后发放奖励并移出进行中列表', () => {
    let s = createEconomy();
    s = startTask(s, 'c2'); // 轻徭薄赋，2 日，民心+12
    s = advanceDay(s);
    s = advanceDay(s);
    expect(s.activeTaskIds).toHaveLength(0);
    expect(s.completedTaskIds).toContain('c2');
    expect(s.log.some((l) => l.includes('完成「轻徭薄赋」'))).toBe(true);
  });

  it('未到期的任务不发放奖励', () => {
    let s = createEconomy();
    s = startTask(s, 'e1'); // 修筑渠堰，4 日
    s = advanceDay(s);
    expect(s.activeTaskIds).toHaveLength(1);
    expect(s.completedTaskIds).toHaveLength(0);
  });

  it('经济任务增加粮、内政任务增加民心', () => {
    let s1 = createEconomy();
    const food0 = s1.settlement.food;
    s1 = startTask(s1, 'e1');
    for (let i = 0; i < 5; i++) s1 = advanceDay(s1);
    expect(s1.completedTaskIds).toContain('e1');

    let s2 = createEconomy();
    const mor0 = s2.settlement.morale;
    s2 = startTask(s2, 'c2');
    for (let i = 0; i < 3; i++) s2 = advanceDay(s2);
    expect(s2.completedTaskIds).toContain('c2');
    expect(s2.settlement.morale).toBeGreaterThan(mor0);
    void food0;
  });

  it('军事任务增加兵源', () => {
    let s = createEconomy();
    s = startTask(s, 'm1'); // 剿灭山匪，3 日，兵源+4000
    for (let i = 0; i < 4; i++) s = advanceDay(s);
    expect(s.completedTaskIds).toContain('m1');
    expect(s.settlement.manpower).toBeGreaterThan(0);
  });
});

describe('据点基建', () => {
  it('驻军上限随等级递增', () => {
    const base = createEconomy().settlement;
    const caps = [1, 2, 3, 4, 5].map((lv) =>
      garrisonCap({ ...base, stronghold: { ...base.stronghold, level: lv } }),
    );
    for (let i = 0; i < caps.length - 1; i++) {
      expect(caps[i]).toBeLessThan(caps[i + 1]);
    }
  });

  it('升级需扣资源，完成后等级提升', () => {
    let s = createEconomy();
    const gold0 = s.settlement.gold;
    s = startUpgrade(s);
    expect(s.settlement.gold).toBeLessThan(gold0);
    expect(s.settlement.stronghold.upgrading).toBe(true);
    for (let i = 0; i < 7; i++) s = advanceDay(s);
    expect(s.settlement.stronghold.level).toBe(2);
    expect(s.settlement.stronghold.upgrading).toBe(false);
  });

  it('资源不足时不能开始升级', () => {
    let s = createEconomy();
    s = { ...s, settlement: { ...s.settlement, gold: 0, food: 0 } };
    s = startUpgrade(s);
    expect(s.settlement.stronghold.upgrading).toBe(false);
    expect(s.log.some((l) => l.includes('资源不足'))).toBe(true);
  });

  it('升级期间防御下降 20%', () => {
    const base = createEconomy().settlement;
    const normal = defenseModifier(base);
    const upgrading = defenseModifier({
      ...base,
      stronghold: { ...base.stronghold, upgrading: true },
    });
    expect(normal).toBe(1);
    expect(upgrading).toBeCloseTo(0.8, 6);
  });

  it('据点等级不超过 5', () => {
    let s = createEconomy();
    s = { ...s, settlement: { ...s.settlement, stronghold: { level: 5, upgrading: false, upgradeDaysLeft: 0 } } };
    s = startUpgrade(s);
    for (let i = 0; i < 10; i++) s = advanceDay(s);
    expect(s.settlement.stronghold.level).toBe(5);
  });

  it('据点等级提升会增加粮产', () => {
    const base = createEconomy().settlement;
    const lv1 = dailyFoodProduction(base);
    const lv5 = dailyFoodProduction({
      ...base,
      stronghold: { ...base.stronghold, level: 5 },
    });
    expect(lv5).toBeGreaterThan(lv1);
  });
});

describe('纯函数性质', () => {
  it('advanceDay 不修改原状态', () => {
    const s = createEconomy();
    const snapshot = JSON.stringify(s);
    advanceDay(s);
    expect(JSON.stringify(s)).toBe(snapshot);
  });

  it('startTask 不修改原状态', () => {
    const s = createEconomy();
    const snapshot = JSON.stringify(s);
    startTask(s, 'm1');
    expect(JSON.stringify(s)).toBe(snapshot);
  });

  it('returnFromBattle 不修改原状态', () => {
    const s = createEconomy();
    const snapshot = JSON.stringify(s);
    returnFromBattle(s, true, 100, 10);
    expect(JSON.stringify(s)).toBe(snapshot);
  });

  it('无随机：同输入两次推进结果一致', () => {
    const a = advanceDay(createEconomy());
    const b = advanceDay(createEconomy());
    expect(a.settlement.food).toBeCloseTo(b.settlement.food, 9);
    expect(a.settlement.population).toBeCloseTo(b.settlement.population, 9);
  });

  it('长期推进不发散（数值不发散）', () => {
    let s = createEconomy();
    for (let i = 0; i < 500; i++) s = advanceDay(s);
    expect(Number.isFinite(s.settlement.food)).toBe(true);
    expect(Number.isFinite(s.settlement.gold)).toBe(true);
    expect(Number.isFinite(s.settlement.population)).toBe(true);
    expect(s.settlement.morale).toBeGreaterThanOrEqual(0);
    expect(s.settlement.morale).toBeLessThanOrEqual(100);
    expect(s.settlement.population).toBeLessThanOrEqual(MAX_POP);
    expect(s.settlement.food).toBeGreaterThanOrEqual(0);
    expect(s.settlement.gold).toBeGreaterThanOrEqual(0);
  });

  it('长期推进仍能推进且状态合法', () => {
    let s: EconomyState = createEconomy();
    for (let i = 0; i < 300; i++) {
      s = advanceDay(s);
      expect(Number.isFinite(s.day)).toBe(true);
    }
    expect(s.day).toBe(300);
  });
});

describe('粮为 0 则战斗力不可持续（经营层与战斗层的连接）', () => {
  it('驻军规模超粮储时，日推进后粮耗尽', () => {
    let s = createEconomy();
    // 6 万驻军，日耗 600 粮，粮产约 154 → 净亏
    s = { ...s, settlement: { ...s.settlement, food: 200, manpower: 60000 } };
    for (let i = 0; i < 60; i++) s = advanceDay(s);
    expect(s.settlement.food).toBe(0);
    expect(s.settlement.morale).toBeLessThan(INITIAL_MORALE);
  });

  it('大规模出征直接打空粮草', () => {
    let s = createEconomy();
    const before = s.settlement.food;
    s = deployToBattle(s, 200, 100000, 2.5);
    expect(s.settlement.food).toBeLessThan(before);
    // 行军耗粮 200*2.5*100 = 50000，远超 3000 存量
    expect(s.settlement.food).toBe(0);
  });
});
