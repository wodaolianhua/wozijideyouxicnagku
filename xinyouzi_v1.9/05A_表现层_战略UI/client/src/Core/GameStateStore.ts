// ============================================================================
//  Core/GameStateStore.ts —— R1 状态镜像（只读）。
//  从后端拉取 WorldState 快照，deep-freeze 后缓存；所有组件/场景从 store 读。
//  组件不得持有状态副本自行修改——冻结后任何写入在严格模式下直接抛错。
// ============================================================================

import type { GameClient } from './GameClient.js';
import type {
  Army,
  City,
  Faction,
  GameMeta,
  Officer,
  StateResponse,
  TickResponse,
  WorldState,
} from './Types.js';

function deepFreeze<T>(value: T): T {
  if (value && typeof value === 'object') {
    for (const key of Object.getOwnPropertyNames(value)) {
      deepFreeze((value as Record<string, unknown>)[key]);
    }
    Object.freeze(value);
  }
  return value;
}

export class GameStateStore {
  private _state: Readonly<WorldState> | null = null;
  private _meta: Readonly<GameMeta> | null = null;
  private readonly listeners: Array<() => void> = [];

  get ready(): boolean {
    return this._state !== null;
  }

  /** 只读状态镜像。未开局时抛错（调用方先用 ready 判断）。 */
  get state(): Readonly<WorldState> {
    if (this._state === null) {
      throw new Error('尚未开局：state 镜像为空');
    }
    return this._state;
  }

  get meta(): Readonly<GameMeta> | null {
    return this._meta;
  }

  /** 订阅状态更新（HUD / 场景刷新） */
  onChange(fn: () => void): void {
    this.listeners.push(fn);
  }

  applySnapshot(state: WorldState, meta: GameMeta): void {
    this._state = deepFreeze(state);
    this._meta = deepFreeze(meta);
    for (const fn of this.listeners) {
      fn();
    }
  }

  applyTick(tick: TickResponse): void {
    this.applySnapshot(tick.state, tick.meta);
  }

  async refresh(client: GameClient): Promise<StateResponse> {
    const resp = await client.getState();
    this.applySnapshot(resp.state, resp.meta);
    return resp;
  }

  // ---- 只读查找助手 ----

  city(id: number): Readonly<City> | undefined {
    return this._state?.cities.find((c) => c.id === id);
  }

  faction(id: number): Readonly<Faction> | undefined {
    return this._state?.factions.find((f) => f.id === id);
  }

  army(id: number): Readonly<Army> | undefined {
    return this._state?.armies.find((a) => a.id === id);
  }

  officer(id: number): Readonly<Officer> | undefined {
    return this._state?.officers.find((o) => o.id === id);
  }

  get playerFaction(): Readonly<Faction> | undefined {
    return this._state?.factions.find((f) => f.isPlayer);
  }

  /** 玩家势力拥有的城池（按 Id 排序，展示稳定） */
  get playerCities(): Readonly<City>[] {
    const pf = this.playerFaction;
    if (!pf || !this._state) {
      return [];
    }
    return this._state.cities.filter((c) => c.ownerFactionId === pf.id).slice().sort((a, b) => a.id - b.id);
  }

  /** 玩家势力的部队 */
  get playerArmies(): Readonly<Army>[] {
    const pf = this.playerFaction;
    if (!pf || !this._state) {
      return [];
    }
    return this._state.armies.filter((a) => a.ownerFactionId === pf.id).slice().sort((a, b) => a.id - b.id);
  }
}
