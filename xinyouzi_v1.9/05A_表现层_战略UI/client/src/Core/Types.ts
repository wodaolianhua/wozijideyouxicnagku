// ============================================================================
//  Core/Types.ts —— 与后端 JSON（camelCase + 枚举字符串）对齐的手写 interface。
//  字段名一律对照 接口契约_共享类型/*.cs，不引入代码生成。
// ============================================================================

export interface Resources {
  grain: number;
  silver: number;
  soldier: number;
  /** 势力民心 0-100（契约字段名 Morale；注意不是部队士气） */
  morale: number;
  /** 稳定度 0-100 */
  stability: number;
}

export type TerrainType = 'Plain' | 'Hill' | 'Forest' | 'Mountain' | 'River' | 'Desert';

export interface City {
  id: number;
  name: string;
  ownerFactionId: number;
  population: number;
  maxPopulation: number;
  defense: number;
  maxDefense: number;
  fortifyLevel: number;
  farmLevel: number;
  /** 兵营等级 0-5，影响兵源产出（v1.7） */
  barracksLevel: number;
  /** 市集等级 0-5，影响银收入（v1.7） */
  marketLevel: number;
  /** 所属州 Id，S1 尺度分组用（v1.7） */
  provinceId: number;
  /** 正在升级的建筑：-1 无，0 农田 1 兵营 2 城墙 3 市集（v1.7） */
  upgradingBuilding: number;
  /** 在建剩余月数（v1.7） */
  upgradeMonthsRemaining: number;
  isCapital: boolean;
  terrain: TerrainType;
  adjacentCityIds: number[];
}

export type ArmyStance = 'Idle' | 'Moving' | 'Besieging' | 'Retreating';

export interface Army {
  id: number;
  ownerFactionId: number;
  /** 统帅 OfficerId，-1 表示无 */
  officerId: number;
  soldiers: number;
  /** 部队士气 0-100（注意不是势力民心） */
  morale: number;
  moraleMax: number;
  positionCityId: number;
  /** 移动目的地城池 Id，-1 表示无移动计划 */
  targetCityId: number;
  stance: ArmyStance;
  hasRestedThisMonth: boolean;
  monthsWithoutBattle: number;
  hasMutiniedThisMonth: boolean;
  hasEngagedThisMonth: boolean;
}

export interface PlayerProfile {
  id: number;
  name: string;
  command: number;
  isPersonallyCommanding: boolean;
  commandingArmyId: number;
}

export type PostType = 'None' | 'CityGovernor' | 'FortifyOverseer' | 'FarmOverseer' | 'General' | 'Scoutmaster';

export type OfficerSpecialty = 'None' | 'Agriculture' | 'Siege' | 'Cavalry' | 'Logistics' | 'Diplomacy';

export interface Officer {
  id: number;
  name: string;
  /** -1 = 在野 */
  ownerFactionId: number;
  /** -1 = 未任职 */
  postCityId: number;
  post: PostType;
  postedSinceMonths: number;
  command: number;
  governance: number;
  /** 智略 20-95（v1.7），科技部门效率的唯一依据 */
  intellect: number;
  /** 魅力 20-95（v1.7），外交部门与经济的依据之一 */
  charisma: number;
  loyalty: number;
  age: number;
  isAlive: boolean;
  specialty: OfficerSpecialty;
  /** 是否已用过唯一一次改名机会（v1.7） */
  nameChangeUsed: boolean;
}

/** 五部门（v1.7）——「用人」的核心 */
export type DepartmentType = 'Military' | 'Interior' | 'Economy' | 'Science' | 'Diplomacy';

/** 部门中文名与它看的人才属性 */
export const DEPARTMENT_INFO: Record<DepartmentType, { label: string; attr: string; effect: string }> = {
  Military: { label: '军事', attr: '统率', effect: '兵源产出' },
  Interior: { label: '内政', attr: '治理', effect: '粮产' },
  Economy: { label: '经济', attr: '治理+魅力', effect: '银收入' },
  Science: { label: '科技', attr: '智略', effect: '建筑工期' },
  Diplomacy: { label: '外交', attr: '魅力', effect: '声望' },
};

export interface Faction {
  id: number;
  name: string;
  isPlayer: boolean;
  capitalCityId: number;
  resources: Resources;
  /** 声望（v1.7），解锁地图尺度用 */
  prestige: number;
  /** 已解锁尺度 S0_Local / S1_Region / S2_Realm / S3_World */
  unlockedScale: string;
  /** 部门 → 主官人才 Id（未任命则无该键） */
  departmentHead: Record<string, number>;
  /** 最后一次招贤的绝对月序号，0 = 从未 */
  lastRecruitMonth: number;
}

export type ActionType =
  | 'Recruit'
  | 'Fortify'
  | 'Develop'
  | 'Dispatch'
  | 'AssignPost'
  | 'Rest'
  | 'RecruitOfficer'
  | 'Reward'
  | 'Build'
  | 'AppointDepartment'
  | 'Trade'
  | 'FormAlliance'
  | 'Siege';

export type ActorType = 'Player' | 'Officer' | 'AiFaction';

export interface PendingOrder {
  orderId: number;
  actorType: ActorType;
  actorId: number;
  action: ActionType;
  targetId: number;
  secondaryTargetId: number;
  amount: number;
  executeYear: number;
  executeMonth: number;
}

export type EventCategory = 'Economy' | 'Military' | 'Personnel' | 'Disaster' | 'Diplomacy' | 'Internal';

export interface EventRecord {
  year: number;
  month: number;
  category: EventCategory;
  message: string;
  factionId: number;
  eventKey: string;
}

export type DifficultyLevel = 'Easy' | 'Normal' | 'Hard';

export interface WorldState {
  year: number;
  /** 月份 1-12 */
  month: number;
  seed: number;
  difficulty: DifficultyLevel;
  /** key=势力 Id（JSON 对象键为字符串） */
  actionsThisMonthByFaction: Record<string, number>;
  player: PlayerProfile;
  factions: Faction[];
  cities: City[];
  armies: Army[];
  officers: Officer[];
  pendingOrders: PendingOrder[];
  eventLog: EventRecord[];
}

export interface StateDelta {
  targetType: string;
  targetId: number;
  field: string;
  oldValue: number;
  newValue: number;
}

export type ResolveResultCode =
  | 'Success'
  | 'RejectedInvalidActor'
  | 'RejectedNoResources'
  | 'RejectedInvalidTarget'
  | 'RejectedTargetOccupied'
  | 'RejectedNoPost'
  | 'RejectedNotAdjacent'
  | 'RejectedCooldown'
  | 'RejectedNotImplemented'
  | 'RejectedInsufficientCapacity'
  | 'RejectedTargetSieged'
  | 'InternalError';

export interface ResolveResult {
  code: ResolveResultCode;
  /** 失败原因（中文），成功时为 null */
  message: string | null;
  deltas: StateDelta[];
  events: EventRecord[];
}

/** /api/tick 返回的单条结算结果 = 快照队列指令 + 对应 ResolveResult（顺序一一对应） */
export interface SettledOrder extends PendingOrder, ResolveResult {}

export interface GameMeta {
  monthsElapsed: number;
  isFinished: boolean;
  winnerFactionId: number;
  gameOverReason: string;
  upstreamGameOverMonth: number;
  upstreamGameOverReason: string | null;
  pendingCount: number;
  /** 月度产出与部门效率（v1.7）。顶栏的「+240」必须来自这里，不许前端自己乘。 */
  income?: {
    grain: number;
    silver: number;
    soldier: number;
    departmentEfficiency: {
      interior: number;
      military: number;
      economy: number;
    };
  };
}

export interface StateResponse {
  state: WorldState;
  meta: GameMeta;
}

export interface TickResponse {
  year: number;
  month: number;
  results: SettledOrder[];
  events: EventRecord[];
  meta: GameMeta;
  state: WorldState;
}

export interface OrderResponse {
  queued: boolean;
  pendingCount: number;
}

// ============================================================================
//  存档（v1.9 · 第 5 轮）
//  与后端 Program.cs 的四个存档端点对齐：
//    POST   /api/save            { name?: string } → SaveResponse
//    GET    /api/saves                              → SaveListResponse
//    POST   /api/load            { id: string }    → LoadSaveResponse
//    DELETE /api/saves/{id}                        → { deleted, id }
// ============================================================================

/** 存档列表里的一条（GET /api/saves） */
export interface SaveInfo {
  /** 形如 20261114-095430-存档名，原样回传给 /api/load 与 DELETE 即可 */
  id: string;
  name: string;
  year: number;
  month: number;
  cities: number;
  /** 字节数 */
  size: number;
  /** 后端已格式化为 "yyyy-MM-dd HH:mm:ss"（本机时间） */
  savedAt: string;
}

export interface SaveListResponse {
  saves: SaveInfo[];
  total: number;
}

/** POST /api/save 的返回 */
export interface SaveResponse {
  id: string;
  name: string;
  file: string;
  size: number;
  year: number;
  month: number;
  cities: number;
}

/**
 * POST /api/load 的返回。
 * state / meta 与 /api/new-game、/api/tick 完全同构，
 * 可直接交给 store.applySnapshot(state, meta)。
 */
export interface LoadSaveResponse {
  loaded: boolean;
  id: string;
  name: string;
  state: WorldState;
  meta: GameMeta;
}

export interface DeleteSaveResponse {
  deleted: boolean;
  id: string;
}

/**
 * 即时指令响应（部门任命等）。
 * 与 OrderResponse 的区别：它不进延迟队列，当场结算并把新状态一并回传。
 */
export interface ImmediateOrderResponse {
  queued: false;
  immediate: true;
  code: ResolveResultCode;
  message: string | null;
  deltas: StateDelta[];
  state: WorldState;
  meta: GameMeta;
}

export type OrderResult = OrderResponse | ImmediateOrderResponse;

/** 判断是否为即时指令响应（结构性判别，不依赖业务字段） */
export function isImmediate(r: OrderResult): r is ImmediateOrderResponse {
  return (r as ImmediateOrderResponse).immediate === true;
}
