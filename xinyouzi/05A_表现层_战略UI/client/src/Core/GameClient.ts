// ============================================================================
//  Core/GameClient.ts —— 与后端通信 + R2 指令唯一入口（OrderSender）。
//
//  【OrderSender 落位说明】提示词 §2 目录树没有独立 OrderSender 文件槽，
//  且「不创建规定之外的文件」，故 R2 的唯一入口组件并入本通信模块导出。
//  全项目 UI 一律经 OrderSender.send() → GameClient → /api/order，无旁路。
// ============================================================================

import type { ActionType, OrderResponse, StateResponse, TickResponse } from './Types.js';

export interface OrderPayload {
  action: ActionType;
  targetId: number;
  /** Dispatch=出发城；AssignPost=岗位所在城；其余 -1 */
  secondaryTargetId?: number;
  /** Recruit=征兵数；Dispatch=兵力；Fortify/Develop=资源投入量 */
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

  order(payload: OrderPayload): Promise<OrderResponse> {
    return this.postJson<OrderResponse>('/api/order', {
      action: payload.action,
      targetId: payload.targetId,
      secondaryTargetId: payload.secondaryTargetId ?? -1,
      amount: payload.amount ?? 0,
    });
  }

  tick(): Promise<TickResponse> {
    return this.postJson<TickResponse>('/api/tick', {});
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
  ) {}

  async send(payload: OrderPayload): Promise<boolean> {
    try {
      const resp = await this.client.order(payload);
      if (!resp.queued) {
        this.notify(`指令未被接受（action=${payload.action}）`, 'error');
        return false;
      }
      this.onQueued?.(resp.pendingCount);
      this.notify(
        `已下令【${actionLabel(payload.action)}】入队（第 ${resp.pendingCount} 条待结算）。` +
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
    default:
      return action;
  }
}
