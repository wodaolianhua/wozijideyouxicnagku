/**
 * 经营层 —— 基建 / 经济 / 民生 / 任务
 *
 * 对应文档 01 §5。这是与战斗并列的第二条主循环，不是后期内容。
 *
 * 核心约束（文档 §5.1）：
 *   粮为0 → 部队溃散。这条让「你能不能打这场仗」成为真实问题。
 * 核心耦合（文档 §5.5）：
 *   你在战场上做不了决策，不在战场上打不了胜仗。
 *
 * 全部为纯函数，随机性由调用方注入 rng。
 */

export const MAX_POP = 1_000_000;

/** 三资源初始量 */
export interface Resources {
  food: number; // 粮
  gold: number; // 金
  manpower: number; // 兵源（可用人口）
}

export interface Stronghold {
  level: number; // 1~5
  upgrading: boolean; // 升级中：防御下降、不可调兵
  upgradeDaysLeft: number;
}

export interface Settlement {
  population: number;
  morale: number; // 民心 0~100，初始 50
  food: number;
  gold: number;
  manpower: number; // 兵源（已征召的可用兵力）
  taxRate: number; // 税率 0.0~0.3
  stronghold: Stronghold;
}

export type TaskKind = 'military' | 'economy' | 'civic';

export interface Task {
  id: string;
  kind: TaskKind;
  name: string;
  desc: string;
  durationDays: number;
  reward: Partial<Resources>;
  moraleDelta: number;
  /** 需要的前置据点等级 */
  requiredLevel: number;
}

export interface EconomyState {
  day: number;
  settlement: Settlement;
  activeTaskIds: string[];
  /** 每个进行中任务的剩余天数（存 state，不修改任务表，保证纯函数） */
  taskDaysLeft: Record<string, number>;
  completedTaskIds: string[];
  availableTasks: Task[];
  /** 上阵期间后方仍在消耗 */
  atBattle: boolean;
  /** 在前线的部队人数（决定每日耗粮） */
  battleTroops: number;
  lastBattleFoodLoss: number;
  log: string[];
}

export const STARTING_POP = 200_000;
export const INITIAL_MORALE = 50;

/** 每 1000 人日耗 10 粮（与 combat.ts 的 supplyCapacity 一致） */
export const FOOD_PER_1000_PER_DAY = 10;

/** 前线作战粮耗倍率（高于后方驻军：战场消耗更大、无就地补给） */
export const FRONT_LINE_MULTIPLIER = 3;

export function createEconomy(): EconomyState {
  return {
    day: 0,
    settlement: {
      population: STARTING_POP,
      morale: INITIAL_MORALE,
      food: 3000,
      gold: 800,
      manpower: 0,
      taxRate: 0.1,
      stronghold: { level: 1, upgrading: false, upgradeDaysLeft: 0 },
    },
    activeTaskIds: [],
    taskDaysLeft: {},
    completedTaskIds: [],
    availableTasks: buildTaskPool(),
    atBattle: false,
    battleTroops: 0,
    lastBattleFoodLoss: 0,
    log: ['领地初定，兵未集，粮未备。'],
  };
}
function buildTaskPool(): Task[] {
  return [
    {
      id: 'm1', kind: 'military', name: '剿灭山匪',
      desc: '派兵清剿境内匪患', durationDays: 3,
      reward: { manpower: 4000, gold: 120 }, moraleDelta: +2, requiredLevel: 1,
    },
    {
      id: 'm2', kind: 'military', name: '守卫粮道',
      desc: '护送粮草辎重通过匪患区', durationDays: 4,
      reward: { food: 800 }, moraleDelta: +1, requiredLevel: 1,
    },
    {
      id: 'm3', kind: 'military', name: '边境巡哨',
      desc: '在边境线巡回，警戒入侵', durationDays: 5,
      reward: { gold: 200 }, moraleDelta: 0, requiredLevel: 2,
    },
    {
      id: 'e1', kind: 'economy', name: '修筑渠堰',
      desc: '兴修水利，增加常平粮产', durationDays: 4,
      reward: { food: 1200 }, moraleDelta: 0, requiredLevel: 1,
    },
    {
      id: 'e2', kind: 'economy', name: '开采盐井',
      desc: '开采盐井换取现钱', durationDays: 5,
      reward: { gold: 450 }, moraleDelta: 0, requiredLevel: 1,
    },
    {
      id: 'e3', kind: 'economy', name: '开辟屯田',
      desc: '开垦荒地，扩充粮源', durationDays: 6,
      reward: { food: 2000 }, moraleDelta: +1, requiredLevel: 2,
    },
    {
      id: 'c1', kind: 'civic', name: '整顿吏治',
      desc: '清理积弊，减少强征', durationDays: 3,
      reward: {}, moraleDelta: +8, requiredLevel: 1,
    },
    {
      id: 'c2', kind: 'civic', name: '轻徭薄赋',
      desc: '减免赋税以苏民困', durationDays: 2,
      reward: {}, moraleDelta: +12, requiredLevel: 1,
    },
    {
      id: 'c3', kind: 'civic', name: '征兵演练',
      desc: '整训兵卒，提高常备战力', durationDays: 5,
      reward: { manpower: 3000 }, moraleDelta: -4, requiredLevel: 2,
    },
  ];
}

// ---------------------------------------------------------------- 派生量

/**
 * 人才系统对经营产出的加成（由 talent.ts 的 totalBonus 提供）。
 * 未接入人才系统时传入 undefined，此时各倍率恒为 1。
 */
export interface EconomyModifiers {
  foodRate: number;    // 粮产倍率
  goldRate: number;    // 金收入倍率
  moraleRate: number;  // 民心变化倍率
  buildRate: number;   // 基建速度倍率
  combatRate: number;  // 战斗效率倍率（供战斗层使用，经营层不直接消费）
  foodUpkeep: number;  // 粮耗倍率（<1 更省）
  recruitRate: number; // 征兵效率倍率
  taskBonus: number;   // 任务成功率加成
}

const NO_MODS: EconomyModifiers = {
  foodRate: 1, goldRate: 1, moraleRate: 1, buildRate: 1,
  combatRate: 1, foodUpkeep: 1, recruitRate: 1, taskBonus: 0,
};

/** 每日粮产（受人口、税率与人才影响） */
export function dailyFoodProduction(s: Settlement, mods?: EconomyModifiers): number {
  const m = mods ?? NO_MODS;
  const labor = Math.max(0.5, s.population / STARTING_POP);
  const base = labor * 170;
  // 税率越高，种田的人越少
  const taxDrag = 1 - s.taxRate * 0.5;
  return base * taxDrag * m.foodRate * (1 + (s.stronghold.level - 1) * 0.15);
}

/** 每日金币收入（税率越高收入越高，但民心受损） */
export function dailyGoldIncome(s: Settlement, mods?: EconomyModifiers): number {
  const m = mods ?? NO_MODS;
  const labor = Math.max(0.5, s.population / STARTING_POP);
  return labor * 55 * s.taxRate * m.goldRate * (1 + (s.stronghold.level - 1) * 0.2);
}

/** 驻军上限（据点等级决定） */
export function garrisonCap(s: Settlement): number {
  return s.stronghold.level * 5000;
}

/** 可征兵数（文档 §5.3 民心公式，受人才 recruitRate 影响） */
export function recruitCap(s: Settlement, mods?: EconomyModifiers): number {
  const m = mods ?? NO_MODS;
  return s.population * 0.10 * (s.morale / 50) * m.recruitRate;
}

/** 强征是否已超限（超限会掉民心） */
export function isOverRecruiting(s: Settlement, currentTroops: number): boolean {
  return currentTroops > recruitCap(s);
}

/** 上阵所需粮（按距离与人数算） */
export function marchFoodCost(distanceKm: number, troops: number, terrainMoveCost: number): number {
  if (troops <= 0) return 0;
  return (distanceKm * terrainMoveCost * troops) / 1000;
}

// ---------------------------------------------------------------- 状态推进

/** 上阵：扣粮，标记后方仍在消耗 */
export function deployToBattle(
  state: EconomyState,
  distanceKm: number,
  troops: number,
  terrainMoveCost: number,
): EconomyState {
  const cost = marchFoodCost(distanceKm, troops, terrainMoveCost);
  return {
    ...state,
    settlement: { ...state.settlement, food: Math.max(0, state.settlement.food - cost) },
    atBattle: true,
    battleTroops: troops,
    lastBattleFoodLoss: cost,
    log: [...state.log, `率${troops} 人出征，行军耗粮${Math.round(cost)}。粮余 ${Math.round(state.settlement.food - cost)}。`],
  };
}

/** 凯旋：民心与奖励 */
export function returnFromBattle(state: EconomyState, won: boolean, survivors: number, losses: number): EconomyState {
  const s = state.settlement;
  // 胜利 + 低伤亡 = 民心涨；败绩或高伤亡 = 民心跌
  const lossRatio = losses / Math.max(1, survivors + losses);
  let moraleDelta = won ? 6 : -10;
  moraleDelta -= Math.round(lossRatio * 20);
  // 上阵期间后方消耗的粮
  const starvation = state.lastBattleFoodLoss * 0.05;
  return {
    ...state,
    settlement: {
      ...s,
      morale: clamp(s.morale + moraleDelta, 0, 100),
      food: Math.max(0, s.food - starvation),
    },
    atBattle: false,
    battleTroops: 0,
    log: [
      ...state.log,
      `${won ? '凯旋' : '败还'}：生还 ${survivors} 人，亡 ${Math.round(losses)} 人，民心${moraleDelta >= 0 ? '+' : ''}${moraleDelta}。`,
    ],
  };
}

/** 开始一个任务 */
export function startTask(state: EconomyState, taskId: string, maxConcurrent = 2): EconomyState {
  const task = state.availableTasks.find((t) => t.id === taskId);
  if (!task) return state;
  if (state.activeTaskIds.includes(taskId)) return state;
  if (state.activeTaskIds.length >= maxConcurrent) {
    return { ...state, log: [...state.log, '人手不够，同时只能推进 2 项任务。'] };
  }
  if (state.settlement.stronghold.level < task.requiredLevel) {
    return { ...state, log: [...state.log, `据点等级不足，需 ${task.requiredLevel} 级。`] };
  }
  // 拷贝任务表，避免原地修改调用方的state（纯函数要求）
  const availableTasks = state.availableTasks.map((t) =>
    t.id === taskId ? { ...t } : t,
  );
  return {
    ...state,
    availableTasks,
    activeTaskIds: [...state.activeTaskIds, taskId],
    taskDaysLeft: { ...state.taskDaysLeft, [taskId]: task.durationDays },
    log: [...state.log, `开始「${task.name}」，需 ${task.durationDays} 日。`],
  };
}

/** 推进一天（无随机，纯确定性）。传入人才加成 mods 让人才真正影响产出。 */
export function advanceDay(state: EconomyState, mods?: EconomyModifiers): EconomyState {
  const m = mods ?? NO_MODS;
  const s = state.settlement;
  let food = s.food + dailyFoodProduction(s, m) - dailyFoodUpkeep(s, m);
  let gold = s.gold + dailyGoldIncome(s, m);
  let morale = s.morale;
  let population = s.population;
  const log = [...state.log];

  // 强征超出上限持续掉民心
  const cap = recruitCap(s, m);
  if (s.manpower > cap) {
    const excess = (s.manpower - cap) / Math.max(1, STARTING_POP);
    const d = -Math.max(1, excess * 30) * m.moraleRate;
    morale += d;
    log.push(`强征过度，民心 ${d.toFixed(1)}。`);
  }

  // 上阵期间额外消耗（前线按人数持续吃粮，这才是真正的压力来源）
  let battleFoodLoss = state.lastBattleFoodLoss;
  if (state.atBattle) {
    const daily = (state.battleTroops / 1000) * FOOD_PER_1000_PER_DAY * FRONT_LINE_MULTIPLIER;
    food -= daily;
    log.push(`前线 ${Math.round(state.battleTroops)} 人就食，当日耗粮 ${Math.round(daily)}。`);
  }

  // 饥荒
  if (food <= 0) {
    food = 0;
    morale -= 6;
    log.push('粮尽，民心动摇（-6）。');
  }

  // 人口自然增长
  population = Math.min(MAX_POP, population * (1 + 0.0004 * (morale / 50)));

  // 任务推进：剩余天数存 state，逐日递减（保证纯函数，不修改任务表）
  // 基建速度倍率 >1 时，任务加速（每次推进多消耗一点进度）
  const buildRate = m.buildRate;
  const taskDaysLeft: Record<string, number> = { ...state.taskDaysLeft };
  const completedTaskIds = [...state.completedTaskIds];
  const availableTasks = state.availableTasks.map((t) => ({ ...t }));

  for (const id of state.activeTaskIds) {
    taskDaysLeft[id] = (taskDaysLeft[id] ?? 0) - buildRate;
  }
  const activeTaskIds = state.activeTaskIds.filter((id) => (taskDaysLeft[id] ?? 0) > 0);

  for (const id of state.activeTaskIds) {
    if ((taskDaysLeft[id] ?? 0) > 0) continue; // 还没到期
    const task = state.availableTasks.find((t) => t.id === id);
    if (!task) continue;
    food += task.reward.food ?? 0;
    gold += task.reward.gold ?? 0;
    s.manpower += task.reward.manpower ?? 0;
    morale += task.moraleDelta;
    // 正向民心任务带来人口回流
    if (task.moraleDelta > 0) {
      population = Math.min(MAX_POP, population * (1 + task.moraleDelta * 0.0006));
    }
    log.push(
      `完成「${task.name}」：粮+${task.reward.food ?? 0} 金+${task.reward.gold ?? 0} 兵源+${task.reward.manpower ?? 0} 民心${task.moraleDelta >= 0 ? '+' : ''}${task.moraleDelta}。`,
    );
    completedTaskIds.push(id);
    delete taskDaysLeft[id];
  }

  // 据点升级推进
  const stronghold = { ...s.stronghold };
  if (stronghold.upgrading) {
    stronghold.upgradeDaysLeft -= 1;
    if (stronghold.upgradeDaysLeft <= 0) {
      stronghold.level = Math.min(5, stronghold.level + 1);
      stronghold.upgrading = false;
      stronghold.upgradeDaysLeft = 0;
      log.push(`据点扩建至 ${stronghold.level} 级。`);
    }
  }

  return {
    ...state,
    day: state.day + 1,
    settlement: {
      population,
      morale: clamp(morale, 0, 100),
      food: Math.max(0, food),
      gold: Math.max(0, gold),
      taxRate: s.taxRate,
      manpower: Math.max(0, s.manpower),
      stronghold,
    },
    activeTaskIds,
    taskDaysLeft,
    completedTaskIds,
    availableTasks,
    lastBattleFoodLoss: battleFoodLoss,
    log: log.slice(-40),
  };
}

/** 开始升级据点 */
export function startUpgrade(state: EconomyState): EconomyState {
  const s = state.settlement;
  if (s.stronghold.level >= 5) return state;
  if (s.stronghold.upgrading) return state;
  const costGold = s.stronghold.level * 200;
  const costFood = s.stronghold.level * 150;
  if (s.gold < costGold || s.food < costFood) {
    return { ...state, log: [...state.log, `升级需金 ${costGold}、粮 ${costFood}，资源不足。`] };
  }
  return {
    ...state,
    settlement: {
      ...s,
      gold: s.gold - costGold,
      food: s.food - costFood,
      stronghold: {
        level: s.stronghold.level,
        upgrading: true,
        upgradeDaysLeft: 6,
      },
    },
    log: [...state.log, `开始扩建据点至 ${s.stronghold.level + 1} 级，需 6 日。升级期间防御下降。`],
  };
}

/** 守城战防御系数（升级中下降 20%） */
export function defenseModifier(s: Settlement): number {
  return s.stronghold.upgrading ? 0.8 : 1.0;
}

/** 调整税率 */
export function setTaxRate(state: EconomyState, rate: number): EconomyState {
  const r = Math.max(0, Math.min(0.3, rate));
  let morale = state.settlement.morale;
  if (r > 0.2) morale -= 1;
  if (r < 0.08) morale += 1;
  return {
    ...state,
    settlement: { ...state.settlement, taxRate: r, morale: clamp(morale, 0, 100) },
  };
}

function dailyFoodUpkeep(s: Settlement, mods?: EconomyModifiers): number {
  const m = mods ?? NO_MODS;
  // 驻军日耗（无驻军时为 0）
  const troops = s.manpower * 0.4;
  return (troops / 1000) * FOOD_PER_1000_PER_DAY * m.foodUpkeep;
}

function clamp(v: number, lo: number, hi: number): number {
  return Math.max(lo, Math.min(hi, v));
}