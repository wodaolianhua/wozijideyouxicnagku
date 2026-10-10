/**
 * 人才系统 —— 招募 / 任命 / 培养
 *
 * 对应项目概览新增要求：「像皇帝一样用人，玩家可以挑选人才，让他们帮忙管理各种事物」。
 * 对应 docs/01 §3.4（将领 = 数值 + 标签）的扩展：单个将领 → 人才池 + 岗位任命。
 *
 * 核心设计（纯系统驱动，不写任何剧情）：
 * - 人才 = 四维数值 + 特性标签 + 性格标签，全部是可推导的规则输入
 * - 「出身」决定初始数值区间与特性倾向（寒门/ 世家 / 军伍 / 商贾 / 方外）
 * - 「性格」影响 AI 决策倾向与任命匹配度（不是影响对话，是影响规则分支）
 * - 岗位（Governor / General / Treasurer / Constable）提供加成
 * - 人才有成长：任职 + 经验；空闲会降低能力（怠惰）
 *
 * 这一层直接解决 docs/01 §7 自审第 4 条「简版任务系统可能变成每天点一下领奖励」：
 * 不同人才派去做同一件事，效果不同且不可互换，取舍才成立。
 */

import type { ArmyClass } from '../model/combat';

/** 出身：决定初始数值区间与特性倾向 */
export type Origin = 'commoner' | 'aristocrat' | 'soldier' | 'merchant' | 'monk';

export const ORIGINS: Record<Origin, { name: string; desc: string }> = {
  commoner: { name: '寒门', desc: '数值均衡，无特性加成，但成长最快' },
  aristocrat: { name: '世家', desc: '智力与统率起点高，但忠诚易动摇' },
  soldier: { name: '军伍', desc: '武力与统率高，民心操作差' },
  merchant: { name: '商贾', desc: '经济相关加成强，武力低' },
  monk: { name: '方外', desc: '智力极高，武力极低，任务成功率加成' },
};

/** 性格：影响 AI 决策倾向与任命匹配度（纯规则，非剧情） */
export type Personality = 'aggressive' | 'cautious' | 'cunning' | 'honest' | 'greedy';

export const PERSONALITIES: Record<Personality, { name: string; desc: string }> = {
  aggressive: { name: '激进', desc: '战斗加成高，经营加成低，任务快但可能失败' },
  cautious: { name: '持重', desc: '任务失败率低，各项加成平稳无短板' },
  cunning: { name: '狡黠', desc: '情报与任务成功率高，民心操作差' },
  honest: { name: '刚直', desc: '民心操作强，经济操作弱' },
  greedy: { name: '贪利', desc: '金收益高，民心掉得快' },
};

/** 特性：加成型标签 */
export type Trait = 'logistics' | 'tactics' | 'engineering' | 'diplomacy' | 'horsemanship';

export const TRAITS: Record<Trait, { name: string; desc: string }> = {
  logistics: { name: '治军', desc: '粮耗降低' },
  tactics: { name: '韬略', desc: '战斗效率提升' },
  engineering: { name: '营造', desc: '基建速度提升' },
  diplomacy: { name: '纵横', desc: '任务成功率提升' },
  horsemanship: { name: '骑射', desc: '骑兵相关提升' },
};

/** 岗位：人才被任命的位置 */
export type Post = 'governor' | 'general' | 'treasurer' | 'constable';

export const POSTS: Record<Post, {
  name: string;
  desc: string;
  /** 该岗位看重的数值维度 */
  keyStat: keyof Stats;
}> = {
  governor: { name: '刺史', desc: '治民理政，影响民心与人口', keyStat: 'intellect' },
  general: { name: '都尉', desc: '统军征战，影响战斗效率', keyStat: 'command' },
  treasurer: { name: '度支', desc: '理财货殖，影响金与税入', keyStat: 'management' },
  constable: { name: '工曹', desc: '营缮屯田，影响基建与粮产', keyStat: 'management' },
};

/** 人才四维（1~10）与文档 §3.4 的将领四维同源 */
export interface Stats {
  command: number;  // 统率
  intellect: number; // 智力
  management: number; // 管理（§3.4 的"体力"在经营场景下映射为此）
  valor: number;    // 武力
}

export interface Talent {
  id: number;
  name: string;
  origin: Origin;
  stats: Stats;
  traits: Trait[];
  personality: Personality;
  /** 忠诚 0~100，低了会叛走或消极怠工 */
  loyalty: number;
  /** 经验，任职累积，提升能力 */
  experience: number;
  /** 当前岗位，null = 未任命（闲职） */
  post: Post | null;
  /** 是否为玩家亲自统御的亲卫（不可被任命） */
  isPlayerGuard: boolean;
}

/**
 * 人才 id 计数器。
 * 用相对 id（从 1 开始，按需分配），保证同种子产出完全一致。
 * 若用全局递增，不同测试/不同调用顺序会产生不同 id，破坏可复现性。
 */
let nextTalentId = 1;

function allocTalentId(): number {
  return nextTalentId++;
}

/** 出身的数值区间 [min, max]，四维依次为 command/intellect/management/valor */
const ORIGIN_RANGES: Record<Origin, [number, number][]> = {
  commoner: [[4, 7], [4, 7], [4, 7], [4, 7]],
  aristocrat: [[5, 8], [6, 9], [3, 6], [2, 5]],
  soldier: [[6, 9], [3, 6], [3, 6], [6, 9]],
  merchant: [[3, 6], [4, 7], [6, 9], [2, 4]],
  monk: [[2, 5], [8, 10], [4, 7], [1, 3]],
};

/** 出身倾向的特性权重（提高该特性出现概率） */
const ORIGIN_TRAIT_BIAS: Record<Origin, Trait[]> = {
  commoner: [],
  aristocrat: ['diplomacy'],
  soldier: ['tactics', 'horsemanship'],
  merchant: ['logistics'],
  monk: ['engineering'],
};

export const NAMES = [
  '李恪', '王彦博', '赵元载', '陈子昂', '独孤彦', '慕容复',
  '裴中立', '崔明允', '卢承庆', '薛收', '韩弘', '萧瑀',
  '长孙俭', '宇文恪', '于谨', '杨庆', '苏敬', '柳仲达',
] as const;

let nameCursor = 0;
let rngState = 0x5f3a91;

/** 确定性随机（xorshift），保证同种子人才池可复现 */
function rand(): number {
  let x = rngState;
  x ^= x << 13; x >>>= 0;
  x ^= x >> 17;
  x ^= x << 5; x >>>= 0;
  rngState = x;
  return x / 4294967296;
}

function randInt(min: number, max: number): number {
  return Math.floor(min + rand() * (max - min + 1));
}

function pick<T>(arr: readonly T[]): T {
  return arr[Math.floor(rand() * arr.length)];
}

function clamp(v: number, lo: number, hi: number): number {
  return Math.max(lo, Math.min(hi, v));
}

/** 生成一个人才 */
export function generateTalent(seed?: number): Talent {
  if (seed !== undefined) rngState = seed >>> 0 || 1;
  const origin = pick(Object.keys(ORIGINS) as Origin[]);
  const ranges = ORIGIN_RANGES[origin];

  // 特性：0~2 个，出身有偏向
  const traitCount = randInt(0, 2);
  const traits: Trait[] = [];
  const bias = ORIGIN_TRAIT_BIAS[origin];
  for (let i = 0; i < traitCount; i++) {
    const t = bias.length > 0 && rand() < 0.6
      ? pick(bias)
      : pick(Object.keys(TRAITS) as Trait[]);
    if (!traits.includes(t)) traits.push(t);
  }

  // 忠诚：世家与方外更难留住
  const loyaltyBase: Record<Origin, [number, number]> = {
    commoner: [60, 90],
    aristocrat: [40, 70],
    soldier: [55, 85],
    merchant: [50, 80],
    monk: [45, 75],
  };
  const [lMin, lMax] = loyaltyBase[origin];

  nameCursor = (nameCursor + 1) % NAMES.length;

  return {
    id: allocTalentId(),
    name: NAMES[nameCursor],
    origin,
    stats: {
      command: randInt(ranges[0][0], ranges[0][1]),
      intellect: randInt(ranges[1][0], ranges[1][1]),
      management: randInt(ranges[2][0], ranges[2][1]),
      valor: randInt(ranges[3][0], ranges[3][1]),
    },
    traits,
    personality: pick(Object.keys(PERSONALITIES) as Personality[]),
    loyalty: randInt(lMin, lMax),
    experience: 0,
    post: null,
    isPlayerGuard: false,
  };
}

/** 生成初始人才池（重置 id 计数器，保证同种子完全可复现） */
export function generateTalentPool(size: number, seed = 987654): Talent[] {
  rngState = seed >>> 0 || 1;
  nameCursor = 0;
  nextTalentId = 1;
  const out: Talent[] = [];
  for (let i = 0; i < size; i++) out.push(generateTalent());
  return out;
}

// ---------------------------------------------------------------- 岗位加成

/** 岗位基础加成（0~1 的倍率差） */
export interface PostBonus {
  /** 民心日变化倍率 */
  moraleRate: number;
  /** 粮产倍率 */
  foodRate: number;
  /** 金收入倍率 */
  goldRate: number;
  /** 基建速度倍率 */
  buildRate: number;
  /** 战斗效率倍率（作用于有效战斗力） */
  combatRate: number;
  /** 粮耗倍率（<1 更省） */
  foodUpkeep: number;
  /** 任务成功率加成（0~0.3） */
  taskBonus: number;
  /** 征兵效率（>1 表示更容易征到兵） */
  recruitRate: number;
}

export const NEUTRAL_BONUS: PostBonus = {
  moraleRate: 1, foodRate: 1, goldRate: 1, buildRate: 1,
  combatRate: 1, foodUpkeep: 1, taskBonus: 0, recruitRate: 1,
};

/**
 * 计算某人才在某岗位的加成。
 *
 * 公式（全部可推导，无随机）：
 *   单项加成 = 该维度数值 / 10 × 0.5
 *   最终倍率 = 1 + 单项加成 + 特性加成 + 经验加成
 *   性格修正：对本岗位不利项 -10%
 */
export function postBonus(t: Talent, post: Post): PostBonus {
  const b: PostBonus = { ...NEUTRAL_BONUS };
  const v = t.stats;
  const exp = Math.min(0.2, t.experience / 500);

  switch (post) {
    case 'governor':
      b.moraleRate += (v.intellect / 10) * 0.5 + exp;
      b.recruitRate += (v.command / 10) * 0.2;
      if (t.traits.includes('diplomacy')) b.moraleRate += 0.15;
      if (t.personality === 'honest') b.moraleRate += 0.1;
      if (t.personality === 'aggressive') b.moraleRate -= 0.1;
      if (t.personality === 'greedy') b.moraleRate -= 0.08;
      break;
    case 'general':
      b.combatRate += (v.command / 10) * 0.5 + exp;
      if (t.traits.includes('tactics')) b.combatRate += 0.15;
      if (t.traits.includes('horsemanship')) b.combatRate += 0.08;
      if (t.personality === 'aggressive') b.combatRate += 0.12;
      if (t.personality === 'cautious') b.combatRate -= 0.05;
      if (t.personality === 'greedy') b.combatRate -= 0.06;
      break;
    case 'treasurer':
      b.goldRate += (v.management / 10) * 0.5 + exp;
      if (t.traits.includes('logistics')) b.goldRate += 0.1;
      b.foodUpkeep -= (v.intellect / 10) * 0.08; // 理财的人也会省粮
      if (t.personality === 'greedy') b.goldRate += 0.15;
      if (t.personality === 'greedy') b.moraleRate -= 0.06;
      if (t.personality === 'honest') b.goldRate -= 0.05;
      break;
    case 'constable':
      b.buildRate += (v.management / 10) * 0.5 + exp;
      b.foodRate += (v.management / 10) * 0.2;
      if (t.traits.includes('engineering')) b.buildRate += 0.15;
      if (t.traits.includes('logistics')) b.foodRate += 0.1;
      if (t.personality === 'cautious') b.buildRate += 0.08;
      if (t.personality === 'aggressive') b.buildRate -= 0.05;
      break;
  }

  return b;
}

/** 亲卫（玩家亲自统御）加成——固定，且高于任何任命 */
export function guardBonus(t: Talent): PostBonus {
  const b: PostBonus = { ...NEUTRAL_BONUS };
  b.combatRate += (t.stats.command / 10) * 0.5 + (t.stats.valor / 10) * 0.3;
  b.moraleRate += (t.stats.valor / 10) * 0.3;
  if (t.traits.includes('tactics')) b.combatRate += 0.15;
  if (t.traits.includes('horsemanship')) b.combatRate += 0.1;
  return b;
}

/** 全部岗位的综合加成（无人才任职时为NEUTRAL） */
export function totalBonus(talents: Talent[]): PostBonus {
  const b: PostBonus = { ...NEUTRAL_BONUS };
  for (const t of talents) {
    const pb = t.isPlayerGuard ? guardBonus(t) : t.post ? postBonus(t, t.post) : null;
    if (!pb) continue;
    b.moraleRate *= pb.moraleRate;
    b.foodRate *= pb.foodRate;
    b.goldRate *= pb.goldRate;
    b.buildRate *= pb.buildRate;
    b.combatRate *= pb.combatRate;
    b.foodUpkeep *= pb.foodUpkeep;
    b.taskBonus += pb.taskBonus;
    b.recruitRate *= pb.recruitRate;
  }
  return b;
}

/** 岗位匹配度 0~1（用于 UI 提示"这人适不适合这个位置"） */
export function postFit(t: Talent, post: Post): number {
  const key = POSTS[post].keyStat;
  const statPart = t.stats[key] / 10;
  let traitPart = 0;
  for (const tr of t.traits) {
    if (
      (post === 'governor' && tr === 'diplomacy') ||
      (post === 'general' && (tr === 'tactics' || tr === 'horsemanship')) ||
      (post === 'treasurer' && tr === 'logistics') ||
      (post === 'constable' && (tr === 'engineering' || tr === 'logistics'))
    ) {
      traitPart += 0.1;
    }
  }
  return clamp(statPart + traitPart, 0, 1);
}

// ---------------------------------------------------------------- 任命操作

export interface TalentState {
  talents: Talent[];
  /** 被任命过的岗位 → 人才 id */
  appointments: Record<string, number>;
  log: string[];
}

/** 任命人才到岗位 */
export function appoint(state: TalentState, talentId: number, post: Post): TalentState {
  const t = state.talents.find((x) => x.id === talentId);
  if (!t) return state;
  if (t.isPlayerGuard) {
    return { ...state, log: [...state.log, `${t.name}是亲卫，不可外派。`] };
  }

  // 先从所有岗位记录中移除此人才（改任时必须清掉旧岗位）
  const appointments: Record<string, number> = {};
  for (const [k, v] of Object.entries(state.appointments)) {
    if (v !== talentId) appointments[k] = v;
  }
  // 同岗位已有他人 → 顶替
  const incumbent = state.talents.find((x) => x.post === post && x.id !== talentId);

  const finalTalents = state.talents.map((x) => {
    if (x.id === talentId) return { ...x, post };
    if (incumbent && x.id === incumbent.id) return { ...x, post: null as Post | null };
    return { ...x };
  });
  appointments[post] = talentId;

  const log = [...state.log];
  if (incumbent) {
    log.push(`${incumbent.name}被免去${POSTS[post].name}，${t.name}接任。`);
  } else {
    log.push(`${t.name}受任${POSTS[post].name}。`);
  }

  return { talents: finalTalents, appointments, log };
}

/** 免职 */
export function dismiss(state: TalentState, talentId: number): TalentState {
  const t = state.talents.find((x) => x.id === talentId);
  if (!t || t.post === null) return state;
  const nextTalents = state.talents.map((x) =>
    x.id === talentId ? { ...x, post: null } : x,
  );
  const appointments = { ...state.appointments };
  for (const k of Object.keys(appointments)) {
    if (appointments[k] === talentId) delete appointments[k];
  }
  return {
    talents: nextTalents,
    appointments,
    log: [...state.log, `${t.name}被免职。`],
  };
}

/** 招募人才（消耗金） */
export function recruit(state: TalentState, cost: number, gold: number): { state: TalentState; gold: number } {
  if (gold < cost) {
    return {
      state: { ...state, log: [...state.log, `招募需金 ${cost}，库金不足。`] },
      gold,
    };
  }
  const t = generateTalent();
  return {
    state: {
      talents: [...state.talents, t],
      appointments: state.appointments,
      log: [...state.log, `${t.name}（${ORIGINS[t.origin].name}·${TRAITS_NAME(t)}）入幕。`],
    },
    gold: gold - cost,
  };
}

function TRAITS_NAME(t: Talent): string {
  return t.traits.length > 0
    ? t.traits.map((x) => TRAITS[x].name).join('·')
    : '无特长';
}

/** 每日前进：任职涨经验、空闲降忠诚、忠诚过低叛走 */
export function advanceTalents(state: TalentState): TalentState {
  const talents = state.talents.map((t) => {
    if (t.isPlayerGuard) {
      // 亲卫经验涨得更快
      return { ...t, experience: t.experience + 3, loyalty: clamp(t.loyalty + 1, 0, 100) };
    }
    if (t.post !== null) {
      // 任职：经验 +2，忠诚回升 1
      return { ...t, experience: t.experience + 2, loyalty: clamp(t.loyalty + 1, 0, 100) };
    }
    // 闲职：经验不涨，忠诚日降 1（怠惰）
    return { ...t, loyalty: clamp(t.loyalty - 1, 0, 100) };
  });

  // 忠诚归零者叛走
  const kept = talents.filter((t) => t.loyalty > 0 || t.isPlayerGuard);
  const deserted = talents.filter((t) => t.loyalty <= 0 && !t.isPlayerGuard);

  const appointments: Record<string, number> = {};
  for (const t of kept) {
    if (t.post) appointments[t.post] = t.id;
  }

  const log = [...state.log];
  for (const d of deserted) {
    log.push(`${d.name}另投他人，离去。`);
  }

  return { talents: kept, appointments, log: log.slice(-40) };
}

/** 人才在指定兵种上的战术加成（0~1） */
export function classBonus(t: Talent, cls: ArmyClass): number {
  let b = 0;
  if (cls === 'cavalry' && t.traits.includes('horsemanship')) b += 0.2;
  if (cls === 'archer' && t.traits.includes('tactics')) b += 0.1;
  return b;
}

/** 生成玩家亲卫（开局必有） */
export function createPlayerGuard(seed = 24680): Talent {
  rngState = seed >>> 0 || 1;
  const t = generateTalent();
  // 亲卫数值拉满一档
  return {
    ...t,
    name: '（你）',
    isPlayerGuard: true,
    post: null,
    stats: {
      command: clamp(t.stats.command + 2, 1, 10),
      intellect: clamp(t.stats.intellect + 1, 1, 10),
      management: clamp(t.stats.management, 1, 10),
      valor: clamp(t.stats.valor + 2, 1, 10),
    },
    loyalty: 100,
  };
}

/** 创建初始人才状态（重置计数器，保证同种子完全一致） */
export function createTalentState(poolSize = 6, seed = 987654): TalentState {
  const pool = generateTalentPool(poolSize, seed); // 这一步已重置 rng 与 id 计数
  const guard = createPlayerGuard(seed + 1);
  return {
    talents: [guard, ...pool],
    appointments: {},
    log: ['幕僚初至，各择其才。'],
  };
}
