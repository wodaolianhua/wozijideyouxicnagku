// ============================================================================
//  Ui/SavePanel.ts —— 存档浮层（05A 第 5 轮）。
//
//  【边界】纯前端：只调后端已经就绪的四个端点
//    POST /api/save · GET /api/saves · POST /api/load · DELETE /api/saves/{id}
//    本层不新增/改动任何后端逻辑。
//
//  【铁律】只读 store（只拿当前年月做默认档名提示），不写状态、不做数值计算；
//    「哪些动作合法」全部由后端说了算，前端只把后端给的错误原样显示出来。
//
//  【形态】居中浮层（sg-overlay + sg-modal），挂在 HUD 的浮层容器里：
//    因此 Esc / 空格能关掉它，关掉后主界面照常可玩，不会留下半残状态。
// ============================================================================

import type { GameClient } from '../Core/GameClient.js';
import type { GameStateStore } from '../Core/GameStateStore.js';
import type { SaveInfo } from '../Core/Types.js';
import { clearChildren, el } from './Layout.js';

export interface SavePanelDeps {
  /** 只读状态镜像：只用来读当前年月（默认档名的提示文案） */
  store: GameStateStore;
  /** 存档 HTTP 接口封装 */
  client: GameClient;
  /** 保存当前进度（name 为空串 = 用后端默认名「存档X年Y月」） */
  onSave(name: string): Promise<void>;
  /** 读档：成功后由外层接住 state/meta 并重建场景 */
  onLoad(id: string): Promise<void>;
  /** 删除存档 */
  onDelete(id: string): Promise<void>;
  /** 提示（复用 Hud.toast，顺带进底部日志栏） */
  notify(message: string, kind: 'info' | 'ok' | 'error'): void;
}

/** 二次确认的动作种类 */
type ConfirmKind = 'load' | 'delete';

/** 字节数 → 人看的体积 */
function formatSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return '—';
  }
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }
  return `${(bytes / 1024 / 1024).toFixed(2)} MB`;
}

export class SavePanel {
  private overlay: HTMLDivElement | null = null;
  private listBox: HTMLDivElement | null = null;
  private listTitle: HTMLDivElement | null = null;
  private nameInput: HTMLInputElement | null = null;
  private saves: SaveInfo[] = [];
  /** 当前正在二次确认的行（读档 / 删除） */
  private confirming: { id: string; kind: ConfirmKind } | null = null;
  /** 防止连点重复提交 */
  private busy = false;

  constructor(private readonly deps: SavePanelDeps) {}

  get isOpen(): boolean {
    return this.overlay !== null && this.overlay.isConnected;
  }

  /**
   * 打开浮层。已打开则只刷新列表。
   * layer = HUD 的浮层容器（Hud.layout.overlayLayer）。
   */
  open(layer: HTMLElement): void {
    if (!this.overlay || !this.overlay.isConnected) {
      this.overlay = this.buildOverlay();
    }
    if (this.overlay.parentElement !== layer) {
      layer.appendChild(this.overlay);
    }
    void this.reload();
  }

  /** 关闭浮层（Esc / 空格 / 关闭按钮都走这里） */
  close(): void {
    this.overlay?.remove();
    this.confirming = null;
  }

  /** 重新拉存档列表（保存 / 删除之后调） */
  async reload(): Promise<void> {
    if (!this.isOpen) {
      return;
    }
    try {
      const resp = await this.deps.client.listSaves();
      this.saves = resp.saves ?? [];
      this.renderList();
    } catch (err) {
      this.deps.notify(`读取存档列表失败：${err instanceof Error ? err.message : String(err)}`, 'error');
    }
  }

  // ==========================================================================
  //  DOM
  // ==========================================================================

  private buildOverlay(): HTMLDivElement {
    const overlay = el('div', 'sg-overlay sg-save-overlay');
    const modal = el('div', 'sg-modal sg-panel sg-save-modal');
    modal.appendChild(el('h2', undefined, '存档'));

    // ---- 保存当前进度 ----
    modal.appendChild(el('div', 'sg-section-title', '保存当前进度'));

    const form = el('div', 'sg-save-form');
    const input = el('input', 'sg-input sg-save-input') as HTMLInputElement;
    input.type = 'text';
    input.maxLength = 40;
    input.placeholder = this.defaultName();
    input.title = '存档名（留空则用默认名「存档X年Y月」）';
    input.addEventListener('keydown', (ev) => {
      if (ev.key === 'Enter') {
        ev.preventDefault();
        void this.doSave();
      } else if (ev.key === 'Escape') {
        // 输入框吃掉 Esc 时，全局快捷键也吃不到，这里自己兜住
        ev.preventDefault();
        this.close();
      }
    });
    this.nameInput = input;

    const saveBtn = el('button', 'sg-btn sg-primary sg-save-do', '保存');
    saveBtn.title = '把当前进度存成新档（存档名可留空）';
    saveBtn.addEventListener('click', () => void this.doSave());

    form.append(input, saveBtn);
    modal.appendChild(form);
    modal.appendChild(
      el('div', 'sg-muted sg-save-tip', `名字可留空：留空则用默认名「${this.defaultName()}」。存档按保存时间倒序。`),
    );

    // ---- 已有存档 ----
    this.listTitle = el('div', 'sg-section-title', '已有存档（0）');
    modal.appendChild(this.listTitle);
    this.listBox = el('div', 'sg-save-list');
    modal.appendChild(this.listBox);

    const foot = el('div', 'sg-modal-foot');
    const close = el('button', 'sg-btn', '关闭');
    close.title = '关闭存档浮层（Esc / 空格）';
    close.addEventListener('click', () => this.close());
    foot.appendChild(close);
    modal.appendChild(foot);

    overlay.appendChild(modal);
    this.renderList();
    return overlay;
  }

  /** 默认档名（与后端口径一致：存档{年}年{月}月），只用于占位提示，不代替后端生成 */
  private defaultName(): string {
    if (!this.deps.store.ready) {
      return '存档';
    }
    const s = this.deps.store.state;
    return `存档${s.year}年${s.month}月`;
  }

  private renderList(): void {
    const box = this.listBox;
    if (!box) {
      return;
    }
    if (this.listTitle) {
      this.listTitle.textContent = `已有存档（${this.saves.length}）`;
    }
    clearChildren(box);
    if (this.saves.length === 0) {
      box.appendChild(el('div', 'sg-muted sg-save-empty', '还没有存档。上面存一个吧。'));
      return;
    }
    for (const s of this.saves) {
      box.appendChild(this.buildItem(s));
    }
  }

  /** 一行存档：名字 / 年月 / 城数 / 大小 / 保存时间 + 读档 / 删除 */
  private buildItem(s: SaveInfo): HTMLDivElement {
    const item = el('div', 'sg-save-item');

    const main = el('div', 'sg-save-main');
    main.appendChild(el('div', 'sg-save-name', s.name));
    main.appendChild(
      el('div', 'sg-save-meta', `${s.year}年${s.month}月 · ${s.cities} 城 · ${formatSize(s.size)} · ${s.savedAt}`),
    );

    const ops = el('div', 'sg-save-ops');
    const confirming = this.confirming?.id === s.id ? this.confirming.kind : null;

    if (confirming === 'load') {
      // 读档 = 用存档覆盖当前进度，所以也做一次二次确认（防手滑丢掉没存的几个月）
      main.appendChild(el('div', 'sg-save-warn', '读档会覆盖当前进度（未保存的月份会丢），确定？'));
      const ok = el('button', 'sg-mini sg-save-danger', '确认读档');
      ok.addEventListener('click', () => void this.doLoad(s.id));
      const cancel = el('button', 'sg-mini', '取消');
      cancel.addEventListener('click', () => this.cancelConfirm());
      ops.append(ok, cancel);
    } else if (confirming === 'delete') {
      main.appendChild(el('div', 'sg-save-warn sg-save-warn-danger', '删除后找不回来，确定？'));
      const ok = el('button', 'sg-mini sg-save-danger', '确认删除');
      ok.addEventListener('click', () => void this.doDelete(s.id));
      const cancel = el('button', 'sg-mini', '取消');
      cancel.addEventListener('click', () => this.cancelConfirm());
      ops.append(ok, cancel);
    } else {
      const load = el('button', 'sg-mini sg-save-load', '读档');
      load.title = `载入「${s.name}」（${s.year}年${s.month}月）`;
      load.addEventListener('click', () => this.startConfirm(s.id, 'load'));

      const del = el('button', 'sg-mini sg-save-del', '删除');
      del.title = `删除「${s.name}」`;
      del.addEventListener('click', () => this.startConfirm(s.id, 'delete'));

      ops.append(load, del);
    }

    item.append(main, ops);
    return item;
  }

  // ==========================================================================
  //  动作
  // ==========================================================================

  private startConfirm(id: string, kind: ConfirmKind): void {
    this.confirming = { id, kind };
    this.renderList();
  }

  private cancelConfirm(): void {
    this.confirming = null;
    this.renderList();
  }

  private async doSave(): Promise<void> {
    if (this.busy) {
      return;
    }
    const name = (this.nameInput?.value ?? '').trim();
    this.busy = true;
    try {
      await this.deps.onSave(name);
      if (this.nameInput) {
        this.nameInput.value = '';
      }
      await this.reload();
    } finally {
      this.busy = false;
    }
  }

  private async doLoad(id: string): Promise<void> {
    if (this.busy) {
      return;
    }
    this.busy = true;
    try {
      await this.deps.onLoad(id);
    } finally {
      this.busy = false;
      this.confirming = null;
    }
  }

  private async doDelete(id: string): Promise<void> {
    if (this.busy) {
      return;
    }
    this.busy = true;
    try {
      await this.deps.onDelete(id);
      this.confirming = null;
      await this.reload();
    } finally {
      this.busy = false;
    }
  }
}
