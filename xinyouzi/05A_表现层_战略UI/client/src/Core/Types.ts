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
  loyalty: number;
  age: number;
  isAlive: boolean;
  specialty: OfficerSpecialty;
}

export interface Faction {
  id: number;
  name: string;
  isPlayer: boolean;
  capitalCityId: number;
  resources: Resources;
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
