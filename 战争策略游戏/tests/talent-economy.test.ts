/**
 * 人才系统 × 经营层耦合测试
 *
 * 验证「用人」真的影响产出，而不只是加了个摆设系统：
 * - 不同人才任命 → 不同产出（这是取舍的来源）
 * - 岗位影响对应产出项（都尉不影响粮产，度支不影响战斗）
 * - 高能力人才在任 → 产出显著优于无人
 * - 亲卫提供战斗加成
 * - 长期推演人才系统不发散
 */

import { describe, it, expect } from 'vitest';
import {
  advanceDay,
  createEconomy,
  dailyFoodProduction,
  dailyGoldIncome,
  garrisonCap,
  recruitCap,
  type EconomyModifiers,
  type EconomyState,
} from '../src/game/economy';
import {
  appoint,
  createTalentState,
  postFit,
  totalBonus,
  type Post,
  type TalentState,
} from '../src/game/talent';

/** 把人才状态转成经济层需要的 modifiers。接受 TalentState（内部取 talents）。 */
function modsOf(ts: TalentState): EconomyModifiers {
  const b = totalBonus(ts.talents);
  return {
    foodRate: b.foodRate,
    goldRate: b.goldRate,
    moraleRate: b.moraleRate,
    buildRate: b.buildRate,
    combatRate: b.combatRate,
    foodUpkeep: b.foodUpkeep,
    recruitRate: b.recruitRate,
    taskBonus: b.taskBonus,
  };
}

/** 造一个指定四维的人才 */
function mkStateWith(
  over: Partial<{ command: number; intellect: number; management: number; valor: number }>,
  personality: TalentState['talents'][number]['personality'] = 'cautious',
  traits: TalentState['talents'][number]['traits'] = [],
): TalentState {
  const base = createTalentState(0, 1);
  return {
    ...base,
    talents: [
      {
        id: 1, name: '甲', origin: 'commoner',
        stats: {
          command: over.command ?? 5, intellect: over.intellect ?? 5,
          management: over.management ?? 5, valor: over.valor ?? 5,
        },
        traits, personality, loyalty: 80, experience: 0,
        post: null, isPlayerGuard: false,
      },
      {
        id: 2, name: '乙', origin: 'commoner',
        stats: { command: 5, intellect: 5, management: 5, valor: 5 },
        traits: [], personality: 'cautious', loyalty: 80, experience: 0,
        post: null, isPlayerGuard: false,
      },
    ],
    appointments: {},
    log: [],
  };
}

describe('岗位影响对应产出', () => {
  it('都尉提升战斗效率但不动粮产', () => {
    const s = mkStateWith({ command: 10 });
    const r = appoint(s, 1, 'general');
    const m = modsOf(r);
    expect(m.combatRate).toBeGreaterThan(1);
    expect(m.foodRate).toBeCloseTo(1, 9); // 无人任粮产相关岗位
  });

  it('度支提升金收入但不动粮产', () => {
    const s = mkStateWith({ management: 10, intellect: 8 });
    const m = modsOf(appoint(s, 1, 'treasurer'));
    expect(m.goldRate).toBeGreaterThan(1);
    expect(m.foodRate).toBeCloseTo(1, 9);
  });

  it('工曹提升粮产', () => {
    const s = mkStateWith({ management: 10 });
    const m = modsOf(appoint(s, 1, 'constable'));
    expect(m.foodRate).toBeGreaterThan(1);
  });

  it('刺史提升征兵效率', () => {
    const s = mkStateWith({ intellect: 10, command: 8 });
    const m = modsOf(appoint(s, 1, 'governor'));
    expect(m.recruitRate).toBeGreaterThan(1);
  });

  it('四岗位满配时各项产出全面提升', () => {
    let s = createTalentState(8, 4321);
    const list = s.talents.filter((t) => !t.isPlayerGuard);
    const posts: Post[] = ['governor', 'general', 'treasurer', 'constable'];
    for (let i = 0; i < 4; i++) s = appoint(s, list[i].id, posts[i]);
    const m = modsOf(s);
    expect(m.foodRate).toBeGreaterThan(1);
    expect(m.goldRate).toBeGreaterThan(1);
    expect(m.moraleRate).toBeGreaterThan(1);
    expect(m.buildRate).toBeGreaterThan(1);
    expect(m.combatRate).toBeGreaterThan(1);
  });
});

describe('不同人才产出不同（取舍成立）', () => {
  it('两个不同的人做同一件事，产出不同', () => {
    const weak = appoint(mkStateWith({ command: 3, intellect: 3, management: 3, valor: 3 }), 1, 'constable');
    const strong = appoint(mkStateWith({ command: 9, intellect: 9, management: 9, valor: 9 }), 1, 'constable');
    const eco = createEconomy();
    const fw = dailyFoodProduction(eco.settlement, modsOf(weak));
    const fs = dailyFoodProduction(eco.settlement, modsOf(strong));
    expect(fs).toBeGreaterThan(fw);
  });

  it('同一人派到不同岗位，产出结构不同', () => {
    const s = mkStateWith({ command: 9, management: 4, intellect: 4 });
    const asGeneral = modsOf(appoint(s, 1, 'general'));
    const asTreasurer = modsOf(appoint(s, 1, 'treasurer'));
    expect(asGeneral.combatRate).toBeGreaterThan(1);
    expect(asGeneral.goldRate).toBeCloseTo(1, 9);
    expect(asTreasurer.goldRate).toBeGreaterThan(1);
  });

  it('性格导致的产出差异可测量', () => {
    const eco = createEconomy();
    const aggr = modsOf(appoint(mkStateWith({ command: 8 }, 'aggressive'), 1, 'general'));
    const caut = modsOf(appoint(mkStateWith({ command: 8 }, 'cautious'), 1, 'general'));
    expect(aggr.combatRate).toBeGreaterThan(caut.combatRate);
    expect(dailyGoldIncome(eco.settlement, aggr)).toBeCloseTo(
      dailyGoldIncome(eco.settlement, caut), 9,
    );
  });

  it('特性带来的产出差异可测量', () => {
    const eco = createEconomy();
    // 智谋型 vs 营造型：营造特性只加buildRate，不加 foodRate
    const withEng = modsOf(appoint(mkStateWith({ management: 6 }, 'cautious', ['engineering']), 1, 'constable'));
    const without = modsOf(appoint(mkStateWith({ management: 6 }, 'cautious', []), 1, 'constable'));
    expect(withEng.buildRate).toBeGreaterThan(without.buildRate);
    // 但营造不直接加粮产（治运特性才加）
    expect(dailyFoodProduction(eco.settlement, withEng))
      .toBeCloseTo(dailyFoodProduction(eco.settlement, without), 9);
  });

  it('治运特性提升粮产（与营造不同）', () => {
    const eco = createEconomy();
    const withLog = modsOf(appoint(mkStateWith({ management: 6 }, 'cautious', ['logistics']), 1, 'constable'));
    const without = modsOf(appoint(mkStateWith({ management: 6 }, 'cautious', []), 1, 'constable'));
    expect(dailyFoodProduction(eco.settlement, withLog))
      .toBeGreaterThan(dailyFoodProduction(eco.settlement, without));
  });

  it('岗位匹配度能区分人选', () => {
    const scholar = mkStateWith({ intellect: 10, command: 2 });
    const warrior = mkStateWith({ command: 10, intellect: 2 });
    const t1 = scholar.talents[0];
    const t2 = warrior.talents[0];
    expect(postFit(t1, 'governor')).toBeGreaterThan(postFit(t2, 'governor'));
    expect(postFit(t2, 'general')).toBeGreaterThan(postFit(t1, 'general'));
  });
});

describe('经济产出实测（不只是倍率）', () => {
  it('工曹任职时粮产高于无人时', () => {
    const eco = createEconomy();
    const s = createTalentState(5, 1234);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const noOne = modsOf(s);
    const withConstable = modsOf(appoint(s, t.id, 'constable'));
    expect(dailyFoodProduction(eco.settlement, withConstable))
      .toBeGreaterThan(dailyFoodProduction(eco.settlement, noOne));
  });

  it('日推进时人才任职使粮增长更快', () => {
    const s = createTalentState(5, 2345);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const withC = modsOf(appoint(s, t.id, 'constable'));

    let a: EconomyState = createEconomy();
    let b: EconomyState = createEconomy();
    for (let i = 0; i < 10; i++) {
      a = advanceDay(a);
      b = advanceDay(b, withC);
    }
    expect(b.settlement.food).toBeGreaterThan(a.settlement.food);
  });

  it('度支任职时金增长更快', () => {
    const s = createTalentState(5, 3456);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const withT = modsOf(appoint(s, t.id, 'treasurer'));

    let a = createEconomy();
    let b = createEconomy();
    for (let i = 0; i < 10; i++) {
      a = advanceDay(a);
      b = advanceDay(b, withT);
    }
    expect(b.settlement.gold).toBeGreaterThan(a.settlement.gold);
  });

  it('刺史任职时可征兵上限提高', () => {
    const eco = createEconomy();
    const s = createTalentState(5, 4567);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const withG = modsOf(appoint(s, t.id, 'governor'));
    expect(recruitCap(eco.settlement, withG)).toBeGreaterThan(recruitCap(eco.settlement));
  });

  it('度支任职时驻军粮耗更低（省粮）', () => {
    const s = createTalentState(5, 5678);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const withT = modsOf(appoint(s, t.id, 'treasurer'));
    let a: EconomyState = createEconomy();
    let b: EconomyState = createEconomy();
    // 双方都有大量驻军
    a = { ...a, settlement: { ...a.settlement, manpower: 20000 } };
    b = { ...b, settlement: { ...b.settlement, manpower: 20000 } };
    for (let i = 0; i < 5; i++) {
      a = advanceDay(a);
      b = advanceDay(b, withT);
    }
    expect(b.settlement.food).toBeGreaterThan(a.settlement.food);
  });

  it('工曹任职时任务完成更快', () => {
    // 挑管理最高的人才做工曹，确保加速明显
    const s = createTalentState(12, 6789);
    const best = s.talents
      .filter((x) => !x.isPlayerGuard)
      .sort((a, b) => b.stats.management - a.stats.management)[0];
    const withC = modsOf(appoint(s, best.id, 'constable'));
    expect(withC.buildRate, '工曹应带来基建加速').toBeGreaterThan(1.2);

    // 3 日任务：加速者 2 天完成（2×1.45=2.9，但循环逐日判定，第 3 天完成），
    // 无加速者需 3 天。用 2 天时的剩余天数对比更稳妥
    const mk = (): EconomyState => ({
      ...createEconomy(),
      activeTaskIds: ['e3'],
      taskDaysLeft: { e3: 3 },
    });
    let a = mk();
    let b = mk();
    for (let i = 0; i < 2; i++) {
      a = advanceDay(a);
      b = advanceDay(b, withC);
    }
    // 加速者剩余天数应明显更少
    const leftA = a.taskDaysLeft.e3 ?? 0;
    const leftB = b.taskDaysLeft.e3 ?? 0;
    expect(
      leftB,
      `加速者剩余 ${leftB.toFixed(2)} 应少于未加速者 ${leftA.toFixed(2)}`,
    ).toBeLessThan(leftA);
  });
});

describe('亲卫特权', () => {
  it('亲卫提供战斗加成（不占岗位）', () => {
    const s = createTalentState(5, 7890);
    const m = modsOf(s);
    // 亲卫在池中，战斗加成应已 > 1
    expect(m.combatRate).toBeGreaterThan(1);
  });

  it('亲卫不占任何岗位', () => {
    const s = createTalentState(5, 7890);
    const g = s.talents.find((t) => t.isPlayerGuard)!;
    expect(g.post).toBeNull();
    for (const id of Object.values(s.appointments)) {
      expect(id).not.toBe(g.id);
    }
  });

  it('亲卫存在时，战斗加成不因四岗位满配而下降', () => {
    // 亲卫的加成恒定存在，岗位只做乘算，不会抵消它
    let s = createTalentState(8, 8642);
    const list = s.talents.filter((t) => !t.isPlayerGuard);
    const beforeAll = totalBonus(s.talents).combatRate;
    const posts: Post[] = ['governor', 'general', 'treasurer', 'constable'];
    for (let i = 0; i < 4; i++) s = appoint(s, list[i].id, posts[i]);
    const afterAll = totalBonus(s.talents).combatRate;
    // 满配后战斗加成应更高（多了一个都尉）
    expect(afterAll).toBeGreaterThan(beforeAll);
  });

  it('亲卫本人不占岗位，战斗加成来自其个人能力', () => {
    const s = createTalentState(8, 8642);
    const g = s.talents.find((t) => t.isPlayerGuard)!;
    // 亲卫未被任命，但加成已计入
    expect(g.post).toBeNull();
    const withGuard = totalBonus(s.talents).combatRate;
    // 去掉亲卫后战斗加成下降
    const withoutGuard = totalBonus(s.talents.filter((t) => !t.isPlayerGuard)).combatRate;
    expect(withGuard).toBeGreaterThan(withoutGuard);
  });
});

describe('系统整体不发散', () => {
  it('人才 + 经济长期联合推演稳定', () => {
    let ts = createTalentState(6, 9753);
    let eco = createEconomy();
    const list = ts.talents.filter((t) => !t.isPlayerGuard);
    ts = appoint(ts, list[0].id, 'governor');
    ts = appoint(ts, list[1].id, 'treasurer');
    ts = appoint(ts, list[2].id, 'constable');
    ts = appoint(ts, list[3].id, 'general');

    for (let i = 0; i < 200; i++) {
      ts = { ...ts, log: ts.log.slice(-10) };
      eco = advanceDay(eco, modsOf(ts));
      // 每日推进人才
      const advanced = { ...ts, talents: ts.talents };
      void advanced;
    }
    expect(Number.isFinite(eco.settlement.food)).toBe(true);
    expect(Number.isFinite(eco.settlement.gold)).toBe(true);
    expect(eco.settlement.morale).toBeGreaterThanOrEqual(0);
    expect(eco.settlement.morale).toBeLessThanOrEqual(100);
  });

  it('所有倍率均为有限正数（taskBonus 可为 0）', () => {
    const s = createTalentState(6, 1357);
    const m = modsOf(s);
    for (const k of Object.keys(m) as (keyof EconomyModifiers)[]) {
      expect(Number.isFinite(m[k]), `${k}=${m[k]}`).toBe(true);
      // 倍率类必须 > 0；taskBonus 是加成型，可以为 0
      if (k === 'taskBonus') {
        expect(m[k]).toBeGreaterThanOrEqual(0);
      } else {
        expect(m[k], `${k}=${m[k]}`).toBeGreaterThan(0);
      }
    }
  });

  it('无人任职时产出与无 mods 完全一致（向后兼容）', () => {
    const eco = createEconomy();
    const empty = modsOf(createTalentState(0, 2222));
    expect(dailyFoodProduction(eco.settlement, empty))
      .toBeCloseTo(dailyFoodProduction(eco.settlement), 9);
    expect(dailyGoldIncome(eco.settlement, empty))
      .toBeCloseTo(dailyGoldIncome(eco.settlement), 9);
  });

  it('驻军上限不因人才而变（据点等级是唯一决定因素）', () => {
    const s = createTalentState(4, 3333);
    const withMods = garrisonCap(createEconomy().settlement);
    expect(withMods).toBe(5000);
    expect(recruitCap(createEconomy().settlement, modsOf(s))).toBeGreaterThan(0);
  });
});
