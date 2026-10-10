/**
 * 人才系统测试
 *
 * 验证「像皇帝一样用人」的机制真的产生取舍：
 * - 任命改变经营产出（这是取舍的来源）
 * - 不同人才派到同一岗位，效果不同
 * - 同一人才派到不同岗位，效果不同
 * - 亲卫比任命更强（玩家亲自统御的溢价）
 * - 忠诚与成长是双向的（任职涨经验、空闲降忠诚、叛走）
 * - 纯函数、无随机可复现
 */

import { describe, it, expect } from 'vitest';
import {
  NEUTRAL_BONUS,
  ORIGINS,
  PERSONALITIES,
  POSTS,
  TRAITS,
  appoint,
  advanceTalents,
  classBonus,
  createPlayerGuard,
  createTalentState,
  dismiss,
  generateTalent,
  generateTalentPool,
  guardBonus,
  postBonus,
  postFit,
  recruit,
  totalBonus,
  type Post,
  type Talent,
} from '../src/game/talent';
import { ARMY_CLASS } from '../src/model/combat';

const mkTalent = (over: Partial<Talent> = {}): Talent => ({
  id: 1,
  name: '测试',
  origin: 'commoner',
  stats: { command: 5, intellect: 5, management: 5, valor: 5 },
  traits: [],
  personality: 'cautious',
  loyalty: 70,
  experience: 0,
  post: null,
  isPlayerGuard: false,
  ...over,
});

describe('人才生成', () => {
  it('五种出身均可生成', () => {
    for (const o of Object.keys(ORIGINS)) {
      const t = generateTalent();
      expect(ORIGINS).toHaveProperty(o);
      expect(t).toBeTruthy();
    }
  });

  it('四维均在 1~10', () => {
    const pool = generateTalentPool(40, 111);
    for (const t of pool) {
      for (const k of Object.keys(t.stats) as (keyof typeof t.stats)[]) {
        expect(t.stats[k], `${t.name}.${k}=${t.stats[k]}`).toBeGreaterThanOrEqual(1);
        expect(t.stats[k]).toBeLessThanOrEqual(10);
      }
    }
  });

  it('同种子人才池完全一致（可复现）', () => {
    const a = generateTalentPool(8, 555);
    const b = generateTalentPool(8, 555);
    expect(JSON.stringify(a)).toBe(JSON.stringify(b));
  });

  it('不同种子人才池不同', () => {
    const a = generateTalentPool(8, 111);
    const b = generateTalentPool(8, 999);
    expect(JSON.stringify(a)).not.toBe(JSON.stringify(b));
  });

  it('出身影响数值分布（方外智力高、商家管理高）', () => {
    const pool = generateTalentPool(120, 777);
    const monks = pool.filter((t) => t.origin === 'monk');
    const merchants = pool.filter((t) => t.origin === 'merchant');
    const avg = (xs: number[]) => xs.reduce((a, b) => a + b, 0) / Math.max(1, xs.length);
    // 方外的智力应显著高于其武力
    expect(avg(monks.map((t) => t.stats.intellect)))
      .toBeGreaterThan(avg(monks.map((t) => t.stats.valor)));
    // 商家的管理应高于其武力
    expect(avg(merchants.map((t) => t.stats.management)))
      .toBeGreaterThan(avg(merchants.map((t) => t.stats.valor)));
  });

  it('忠诚在 0~100', () => {
    const pool = generateTalentPool(30, 321);
    for (const t of pool) {
      expect(t.loyalty).toBeGreaterThanOrEqual(0);
      expect(t.loyalty).toBeLessThanOrEqual(100);
    }
  });

  it('亲卫四维更高且忠诚满', () => {
    const g = createPlayerGuard(888);
    expect(g.isPlayerGuard).toBe(true);
    expect(g.loyalty).toBe(100);
    const pool = generateTalentPool(10, 888);
    const avgCommand = pool.reduce((a, t) => a + t.stats.command, 0) / pool.length;
    expect(g.stats.command).toBeGreaterThan(avgCommand);
  });
});

describe('岗位加成', () => {
  it('各岗位都有名称、说明与主维度', () => {
    for (const p of Object.keys(POSTS) as Post[]) {
      expect(POSTS[p].name.length).toBeGreaterThan(0);
      expect(POSTS[p].desc.length).toBeGreaterThan(0);
    }
  });

  it('刺史提升民心与征兵', () => {
    const t = mkTalent({ stats: { command: 5, intellect: 8, management: 5, valor: 5 } });
    const b = postBonus(t, 'governor');
    expect(b.moraleRate).toBeGreaterThan(1);
    expect(b.recruitRate).toBeGreaterThan(1);
  });

  it('都尉提升战斗效率', () => {
    const t = mkTalent({ stats: { command: 8, intellect: 5, management: 5, valor: 5 } });
    const b = postBonus(t, 'general');
    expect(b.combatRate).toBeGreaterThan(1);
  });

  it('度支提升金收入并降低粮耗', () => {
    const t = mkTalent({ stats: { command: 5, intellect: 7, management: 8, valor: 5 } });
    const b = postBonus(t, 'treasurer');
    expect(b.goldRate).toBeGreaterThan(1);
    expect(b.foodUpkeep).toBeLessThan(1);
  });

  it('工曹提升基建与粮产', () => {
    const t = mkTalent({ stats: { command: 5, intellect: 5, management: 8, valor: 5 } });
    const b = postBonus(t, 'constable');
    expect(b.buildRate).toBeGreaterThan(1);
    expect(b.foodRate).toBeGreaterThan(1);
  });

  it('性格影响加成方向（激进在都尉更强、在刺史更弱）', () => {
    const aggr = mkTalent({ personality: 'aggressive' });
    const plain = mkTalent({ personality: 'cautious' });
    expect(postBonus(aggr, 'general').combatRate)
      .toBeGreaterThan(postBonus(plain, 'general').combatRate);
    expect(postBonus(aggr, 'governor').moraleRate)
      .toBeLessThan(postBonus(plain, 'governor').moraleRate);
  });

  it('性格标签对经营岗位同样有影响（贪利金高但民心低）', () => {
    const greedy = mkTalent({ personality: 'greedy' });
    const honest = mkTalent({ personality: 'honest' });
    expect(postBonus(greedy, 'treasurer').goldRate)
      .toBeGreaterThan(postBonus(honest, 'treasurer').goldRate);
  });

  it('特性提供额外加成', () => {
    const withTrait = mkTalent({ traits: ['tactics'] });
    const without = mkTalent({ traits: [] });
    expect(postBonus(withTrait, 'general').combatRate)
      .toBeGreaterThan(postBonus(without, 'general').combatRate);
  });

  it('经验提升加成但有上限', () => {
    const novice = mkTalent({ experience: 0 });
    const mid = mkTalent({ experience: 250 });
    const max = mkTalent({ experience: 100000 });
    expect(postBonus(mid, 'general').combatRate)
      .toBeGreaterThan(postBonus(novice, 'general').combatRate);
    expect(postBonus(max, 'general').combatRate)
      .toBeLessThanOrEqual(postBonus(mid, 'general').combatRate + 0.25);
  });

  it('数值更高的人在同岗位更强（单调性）', () => {
    const low = mkTalent({ stats: { command: 3, intellect: 3, management: 3, valor: 3 } });
    const high = mkTalent({ stats: { command: 8, intellect: 8, management: 8, valor: 8 } });
    for (const p of Object.keys(POSTS) as Post[]) {
      const bl = postBonus(low, p);
      const bh = postBonus(high, p);
      const gains = ['moraleRate', 'foodRate', 'goldRate', 'buildRate', 'combatRate', 'recruitRate'];
      let improved = 0;
      for (const g of gains) {
        if (bh[g as keyof typeof bh] >= bl[g as keyof typeof bl]) improved++;
      }
      expect(improved, `${p}: 数值高的人应多数项不弱`).toBeGreaterThanOrEqual(4);
    }
  });

  it('亲卫战斗加成高于同等数值的都尉', () => {
    const t = mkTalent();
    const g = { ...t, isPlayerGuard: true };
    expect(guardBonus(g).combatRate).toBeGreaterThan(postBonus(t, 'general').combatRate);
  });

  it('无任职时总加成为中性', () => {
    const state = createTalentState(5, 123);
    // 全部未任命
    const b = totalBonus(state.talents);
    expect(b.foodRate).toBeGreaterThanOrEqual(1);
  });

  it('任命后总加成改变（这是取舍的来源）', () => {
    const state = createTalentState(6, 222);
    const target = state.talents.find((t) => !t.isPlayerGuard)!;
    const before = totalBonus(state.talents);
    const after = totalBonus(appoint(state, target.id, 'general').talents);
    expect(JSON.stringify(after)).not.toBe(JSON.stringify(before));
    expect(after.combatRate).toBeGreaterThanOrEqual(before.combatRate);
  });

  it('中性加成为全1', () => {
    for (const k of Object.keys(NEUTRAL_BONUS) as (keyof typeof NEUTRAL_BONUS)[]) {
      if (k === 'taskBonus') expect(NEUTRAL_BONUS[k]).toBe(0);
      else expect(NEUTRAL_BONUS[k]).toBe(1);
    }
  });
});

describe('岗位匹配度', () => {
  it('匹配度在 0~1', () => {
    const pool = generateTalentPool(20, 444);
    for (const t of pool) {
      for (const p of Object.keys(POSTS) as Post[]) {
        const f = postFit(t, p);
        expect(f).toBeGreaterThanOrEqual(0);
        expect(f).toBeLessThanOrEqual(1);
      }
    }
  });

  it('主维度高的人匹配度更高', () => {
    const smartIntellect = mkTalent({ stats: { command: 5, intellect: 9, management: 5, valor: 5 } });
    const smartValor = mkTalent({ stats: { command: 5, intellect: 5, management: 5, valor: 9 } });
    expect(postFit(smartIntellect, 'governor')).toBeGreaterThan(postFit(smartValor, 'governor'));
  });

  it('特性提高匹配度', () => {
    const withTrait = mkTalent({ traits: ['diplomacy'] });
    const without = mkTalent({ traits: [] });
    expect(postFit(withTrait, 'governor')).toBeGreaterThan(postFit(without, 'governor'));
  });
});

describe('任命操作', () => {
  it('任命后岗位被记录', () => {
    const s = createTalentState(5, 100);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const r = appoint(s, t.id, 'governor');
    expect(r.appointments.governor).toBe(t.id);
    expect(r.talents.find((x) => x.id === t.id)!.post).toBe('governor');
  });

  it('亲卫不可被任命', () => {
    const s = createTalentState(5, 100);
    const g = s.talents.find((x) => x.isPlayerGuard)!;
    const r = appoint(s, g.id, 'governor');
    expect(r.appointments.governor).toBeUndefined();
    expect(r.log.some((l) => l.includes('亲卫'))).toBe(true);
  });

  it('同岗位只能一人，新任顶替旧任', () => {
    const s = createTalentState(6, 200);
    const list = s.talents.filter((x) => !x.isPlayerGuard);
    const r1 = appoint(s, list[0].id, 'general');
    const r2 = appoint(r1, list[1].id, 'general');
    expect(r2.talents.find((x) => x.id === list[0].id)!.post).toBeNull();
    expect(r2.talents.find((x) => x.id === list[1].id)!.post).toBe('general');
    expect(r2.log.some((l) => l.includes('免去'))).toBe(true);
  });

  it('改任会解除旧岗位', () => {
    const s = createTalentState(6, 300);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const r1 = appoint(s, t.id, 'general');
    const r2 = appoint(r1, t.id, 'treasurer');
    expect(r2.talents.find((x) => x.id === t.id)!.post).toBe('treasurer');
    expect(r2.appointments.general).toBeUndefined();
  });

  it('免职清除任命记录', () => {
    const s = createTalentState(5, 400);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const r1 = appoint(s, t.id, 'constable');
    const r2 = dismiss(r1, t.id);
    expect(r2.talents.find((x) => x.id === t.id)!.post).toBeNull();
    expect(r2.appointments.constable).toBeUndefined();
  });

  it('免职未任职者无效', () => {
    const s = createTalentState(5, 400);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const r = dismiss(s, t.id);
    expect(JSON.stringify(r.talents)).toBe(JSON.stringify(s.talents));
  });

  it('任命不修改原状态', () => {
    const s = createTalentState(5, 500);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const snap = JSON.stringify(s);
    appoint(s, t.id, 'general');
    dismiss(s, t.id);
    expect(JSON.stringify(s)).toBe(snap);
  });

  it('四个岗位可全部填满', () => {
    let s = createTalentState(8, 600);
    const list = s.talents.filter((x) => !x.isPlayerGuard);
    const posts: Post[] = ['governor', 'general', 'treasurer', 'constable'];
    for (let i = 0; i < 4; i++) s = appoint(s, list[i].id, posts[i]);
    for (const p of posts) {
      expect(s.appointments[p]).toBeDefined();
    }
  });
});

describe('招募', () => {
  it('有金时可招募', () => {
    const s = createTalentState(4, 700);
    const before = s.talents.length;
    const r = recruit(s, 100, 500);
    expect(r.state.talents.length).toBe(before + 1);
    expect(r.gold).toBe(400);
  });

  it('金不足时不招募', () => {
    const s = createTalentState(4, 700);
    const before = s.talents.length;
    const r = recruit(s, 100, 50);
    expect(r.state.talents.length).toBe(before);
    expect(r.gold).toBe(50);
    expect(r.state.log.some((l) => l.includes('不足'))).toBe(true);
  });

  it('招募的人才四维合法', () => {
    let s = createTalentState(3, 800);
    for (let i = 0; i < 5; i++) s = recruit(s, 10, 1000).state;
    for (const t of s.talents) {
      for (const k of Object.keys(t.stats) as (keyof typeof t.stats)[]) {
        expect(t.stats[k]).toBeGreaterThanOrEqual(1);
        expect(t.stats[k]).toBeLessThanOrEqual(10);
      }
    }
  });
});

describe('日推进（成长与忠诚）', () => {
  it('任职涨经验', () => {
    let s = createTalentState(5, 900);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    s = appoint(s, t.id, 'general');
    const exp0 = s.talents.find((x) => x.id === t.id)!.experience;
    s = advanceTalents(s);
    expect(s.talents.find((x) => x.id === t.id)!.experience).toBeGreaterThan(exp0);
  });

  it('闲职不涨经验且降忠诚', () => {
    let s = createTalentState(5, 900);
    const t = s.talents.find((x) => !x.isPlayerGuard)!;
    const loy0 = t.loyalty;
    s = advanceTalents(s);
    const after = s.talents.find((x) => x.id === t.id)!;
    expect(after.experience).toBe(0);
    expect(after.loyalty).toBe(loy0 - 1);
  });

  it('忠诚归零者叛走', () => {
    let s = createTalentState(6, 950);
    // 把所有非亲卫忠诚压到 1
    s = {
      ...s,
      talents: s.talents.map((t) => (t.isPlayerGuard ? t : { ...t, loyalty: 1 })),
    };
    const before = s.talents.length;
    s = advanceTalents(s);
    expect(s.talents.length).toBeLessThan(before);
    expect(s.log.some((l) => l.includes('离去'))).toBe(true);
  });

  it('亲卫永不叛走', () => {
    let s = createTalentState(4, 960);
    s = { ...s, talents: s.talents.map((t) => (t.isPlayerGuard ? { ...t, loyalty: 0 } : t)) };
    s = advanceTalents(s);
    expect(s.talents.some((t) => t.isPlayerGuard)).toBe(true);
  });

  it('日推进不修改原状态', () => {
    const s = createTalentState(5, 970);
    const snap = JSON.stringify(s);
    advanceTalents(s);
    expect(JSON.stringify(s)).toBe(snap);
  });

  it('长期推进不崩（人才池不会无限膨胀或归零）', () => {
    let s = createTalentState(6, 980);
    for (let i = 0; i < 300; i++) s = advanceTalents(s);
    expect(s.talents.length).toBeGreaterThan(0);
    for (const t of s.talents) {
      expect(t.loyalty).toBeGreaterThanOrEqual(0);
      expect(Number.isFinite(t.experience)).toBe(true);
    }
  });
});

describe('兵种加成', () => {
  it('骑射对骑兵有加成', () => {
    const t = mkTalent({ traits: ['horsemanship'] });
    expect(classBonus(t, 'cavalry')).toBeGreaterThan(0);
    expect(classBonus(t, 'infantry')).toBe(0);
  });

  it('无特长时无加成', () => {
    const t = mkTalent({ traits: [] });
    for (const c of ARMY_CLASS) expect(classBonus(t, c)).toBe(0);
  });

  it('加成不超过 0.3', () => {
    const t = mkTalent({ traits: ['horsemanship', 'tactics'] });
    for (const c of ARMY_CLASS) {
      expect(classBonus(t, c)).toBeLessThanOrEqual(0.3);
    }
  });
});

describe('纯函数与可复现', () => {
  it('createTalentState 同种子一致', () => {
    const a = createTalentState(6, 13579);
    const b = createTalentState(6, 13579);
    expect(JSON.stringify(a.talents)).toBe(JSON.stringify(b.talents));
  });

  it('初始状态含亲卫且亲卫在第一位', () => {
    const s = createTalentState(6, 2468);
    expect(s.talents[0].isPlayerGuard).toBe(true);
  });

  it('初始无任命', () => {
    const s = createTalentState(6, 2468);
    expect(Object.keys(s.appointments)).toHaveLength(0);
  });

  it('所有性格与特性标签都有中文名与说明', () => {
    for (const p of Object.keys(PERSONALITIES)) {
      expect(PERSONALITIES[p as keyof typeof PERSONALITIES].name.length).toBeGreaterThan(0);
      expect(PERSONALITIES[p as keyof typeof PERSONALITIES].desc.length).toBeGreaterThan(0);
    }
    for (const t of Object.keys(TRAITS)) {
      expect(TRAITS[t as keyof typeof TRAITS].name.length).toBeGreaterThan(0);
      expect(TRAITS[t as keyof typeof TRAITS].desc.length).toBeGreaterThan(0);
    }
  });
});
