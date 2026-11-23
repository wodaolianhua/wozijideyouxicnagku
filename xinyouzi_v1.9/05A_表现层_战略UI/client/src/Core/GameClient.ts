// ============================================================================
//  Core/GameClient.ts —— 与后端通信 + R2 指令唯一入口（OrderSender）。
//
//  【OrderSender 落位说明】提示词 §2 目录树没有独立 OrderSender 文件槽，
//  且「不创建规定之外的文件」，故 R2 的唯一入口组件并入本通信模块导出。
//  全项目 UI 一律经 OrderSender.send() → GameClient → /api/order，无旁路。
// ============================================================================

import type {
  ActionType,
  DeleteSaveResponse,
  LoadSaveResponse,
  OrderResult,
  SaveListResponse,
  SaveResponse,
  StateResponse,
  TickResponse,
} from './Types.js';

export interface OrderPayload {
  action: ActionType;
  targetId: number;
  /** Dispatch=出发城；AssignPost/AppointDepartment=岗位所在城或部门枚举；Build=建筑类型（0农田1兵营2城墙3市集） */
  secondaryTargetId?: number;
  /** Recruit=征兵数；Dispatch=兵力；Fortify/Develop=资源投入量；RecruitOfficer=花费银 */
  amount?: number;
}

export class GameClient {
  constructor(private readonly baseUrl: string = '') {}

  private async getJson<T>(url: string): Promise<T> {
    let resp: Response;
    try {
      resp = await fetch(this.baseUrl + url, { method: 'GET' });
    } catch (err) {
      throw new Error(`网络不可用：${String(err)}`);
    }
    return this.readBody<T>(resp);
  }

  private async postJson<T>(url: string, body: unknown): Promise<T> {
    let resp: Response;
    try {
      resp = await fetch(this.baseUrl + url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body ?? {}),
      });
    } catch (err) {
      throw new Error(`网络不可用：${String(err)}`);
    }
    return this.readBody<T>(resp);
  }

  /** DELETE（存档删除用）。错误处理与 GET/POST 共用 readBody，提示口径一致。 */
  private async deleteJson<T>(url: string): Promise<T> {
    let resp: Response;
    try {
      resp = await fetch(this.baseUrl + url, { method: 'DELETE' });
    } catch (err) {
      throw new Error(`网络不可用：${String(err)}`);
    }
    return this.readBody<T>(resp);
  }

  private async readBody<T>(resp: Response): Promise<T> {
    const text = await resp.text();
    if (!resp.ok) {
      // 后端 400/500 附带 { error } 或 ProblemDetails
      let detail = text;
      try {
        const parsed = JSON.parse(text) as { error?: string; title?: string; detail?: string };
        detail = parsed.error ?? parsed.detail ?? parsed.title ?? text;
      } catch {
        // 保留原文
      }
      throw new Error(`HTTP ${resp.status}：${detail}`);
    }
    if (!text) {
      throw new Error(`HTTP ${resp.status}：空响应`);
    }
    return JSON.parse(text) as T;
  }

  newGame(seed: number = 12345): Promise<StateResponse> {
    return this.postJson<StateResponse>('/api/new-game', { seed });
  }

  getState(): Promise<StateResponse> {
    return this.getJson<StateResponse>('/api/state');
  }

  order(payload: OrderPayload): Promise<OrderResult> {
    return this.postJson<OrderResult>('/api/order', {
      action: payload.action,
      targetId: payload.targetId,
      secondaryTargetId: payload.secondaryTargetId ?? -1,
      amount: payload.amount ?? 0,
    });
  }

  tick(): Promise<TickResponse> {
    return this.postJson<TickResponse>('/api/tick', {});
  }

  /** 赐名（一人一次）。成功后返回新状态，调用方直接吃进去重画。 */
  renameOfficer(
    officerId: number,
    name: string,
  ): Promise<{ ok: boolean; oldName: string; name: string; state: StateResponse['state']; meta: StateResponse['meta'] }> {
    return this.postJson('/api/rename-officer', { officerId, name });
  }

  // ==========================================================================
  //  存档（第 5 轮）：四个端点后端已就绪，前端只照着调，不改任何后端逻辑。
  // ==========================================================================

  /**
   * 保存当前进度。
   * name 留空（或只填空白）时后端用默认名「存档X年Y月」——所以这里原样传空串，
   * 不在前端自己拼默认名（默认名的口径归后端，前端只做展示提示）。
   */
  save(name?: string): Promise<SaveResponse> {
    const trimmed = (name ?? '').trim();
    return this.postJson<SaveResponse>('/api/save', trimmed ? { name: trimmed } : {});
  }

  /** 存档列表（按保存时间倒序，后端已排好序） */
  listSaves(): Promise<SaveListResponse> {
    return this.getJson<SaveListResponse>('/api/saves');
  }

  /**
   * 读档。返回的 state / meta 与 /api/new-game、/api/tick 同构，
   * 调用方直接 store.applySnapshot(state, meta) 再重建场景即可。
   */
  load(id: string): Promise<LoadSaveResponse> {
    return this.postJson<LoadSaveResponse>('/api/load', { id });
  }

  /** 删除存档。id 用列表接口给的原值（后端有 IsSafeSaveId 校验）。 */
  deleteSave(id: string): Promise<DeleteSaveResponse> {
    // id 里可能含中文（后端把显示名直接写进文件名），必须转义后再拼 URL
    return this.deleteJson<DeleteSaveResponse>(`/api/saves/${encodeURIComponent(id)}`);
  }
}

// ============================================================================
//  R2 指令唯一入口：所有玩家操作都从这里走。UI 不得绕过它直接调 GameClient.order。
// ============================================================================
export class OrderSender {
  constructor(
    private readonly client: GameClient,
    private readonly notify: (message: string, kind: 'info' | 'ok' | 'error') => void,
    private readonly onQueued?: (pendingCount: number) => void,
    /** 即时指令成功后回调（部门任命用：需要立刻用新 state 重画面板） */
    private readonly onImmediate?: (r: OrderResult) => void,
  ) {}

  async send(payload: OrderPayload): Promise<boolean> {
    try {
      const resp = await this.client.order(payload);

      // 即时指令（部门任命）：不进队列，当场结算。queued=false 在这里不是失败，
      // 而是「已经结算完了」——早期版本把它当成拒绝，任命点了没反应。
      if (resp.queued === false && 'immediate' in resp && resp.immediate) {
        const ok = resp.code === 'Success';
        this.notify(
          ok
            ? `【${actionLabel(payload.action)}】已生效。`
            : `【${actionLabel(payload.action)}】失败：${resp.message ?? resp.code}`,
          ok ? 'ok' : 'error',
        );
        if (ok) {
          this.onImmediate?.(resp);
        }
        return ok;
      }

      if (!resp.queued) {
        this.notify(`指令未被接受（action=${payload.action}）`, 'error');
        return false;
      }

      const pendingCount = 'pendingCount' in resp ? resp.pendingCount : 0;
      this.onQueued?.(pendingCount);
      this.notify(
        `已下令【${actionLabel(payload.action)}】入队（第 ${pendingCount} 条待结算）。` +
          `下令成功 ≠ 执行成功：成败看下月结算的 code/message。`,
        'ok',
      );
      return true;
    } catch (err) {
      this.notify(`下令失败：${err instanceof Error ? err.message : String(err)}`, 'error');
      return false;
    }
  }
}

// ---- UI 文案（仅展示层标签，不含业务判断） ----
export function actionLabel(action: ActionType): string {
  switch (action) {
    case 'Recruit':
      return '征兵';
    case 'Fortify':
      return '修城';
    case 'Develop':
      return '屯田';
    case 'Dispatch':
      return '进军';
    case 'AssignPost':
      return '任命';
    case 'Rest':
      return '休整';
    case 'Reward':
      return '赏赐';
    case 'Build':
      return '兴建';
    case 'RecruitOfficer':
      return '招贤';
    case 'AppointDepartment':
      return '部门任命';
    default:
      return action;
  }
}

/** 建筑类型枚举 → 中文名。索引即 BuildResolver 的 SecondaryTargetId。 */
export const BUILDING_NAMES = ['农田', '兵营', '城墙', '市集'] as const;

/** 建筑类型 → 它影响的东西（用于面板上的说明，不做任何数值计算） */
export const BUILDING_EFFECTS = [
  '粮产 +15%/级',
  '兵源 +15%/级',
  '城防上限 +',
  '银收入 +15%/级',
] as const;
