// ============================================================================
//  Ui/Hud.ts —— DOM 覆盖层：顶栏 / 势力·城池·部队·人才面板 / 军议 / 回合结算 /
//  交互气泡与摇杆 / 战斗演出字幕 / 游戏终局 / 快捷键帮助。
//  铁律：只读 store，只经 OrderSender 下令；不做数值计算、不判合法性。
//
//  桌面化（R2）：
//  · 面板为左侧栏（城池/部队/势力/人才），结算与帮助为居中浮层；非移动端全屏遮罩。
//  · 全部操作可用鼠标完成；键盘快捷键（空格/回车推进、Esc 关闭、1/2/3 切视角与人才面板、
//    E 交互、WASD 行走、H 帮助）由 Controls 上报意图 → 本层执行。
// ============================================================================

import type { GameStateStore } from '../Core/GameStateStore.js';
import { actionLabel, BUILDING_EFFECTS, BUILDING_NAMES } from '../Core/GameClient.js';
import type { GameClient, OrderSender } from '../Core/GameClient.js';
import type { Controls } from '../Player/Controls.js';
import type { ShortcutEvent } from '../Player/Controls.js';
import type { HoverTarget, ViewMode } from '../Scene/WorldScene.js';
import type { WorldScene } from '../Scene/WorldScene.js';
import { cityHoverText } from '../Scene/CityText.js';
import { markBuildPending } from '../Scene/CityInterior.js';
import { buildLayout, clearChildren, el, hide, show, type HudLayout } from './Layout.js';
import { SavePanel } from './SavePanel.js';
import { fieldLabel, factionColor, injectGlobalStyle, injectTouchStyle, UI } from './Theme.js';
import { DEPARTMENT_INFO } from '../Core/Types.js';
import type {
  Army,
  City,
  DepartmentType,
  GameMeta,
  Officer,
  SettledOrder,
  StateDelta,
  TickResponse,
} from '../Core/Types.js';

/** 自动运行：每月结算完成后再等多久推进下一月（含战斗演出之外的喘息时间） */
const AUTO_TICK_GAP_MS = 7000;

const STANCE_LABEL: Readonly<Record<string, string>> = {
  Idle: '待命',
  Moving: '行军',
  Besieging: '围城',
  Retreating: '撤退',
};

const SPECIALTY_LABEL: Readonly<Record<string, string>> = {
  None: '—',
  Agriculture: '农',
  Siege: '攻城',
  Cavalry: '骑兵',
  Logistics: '后勤',
  Diplomacy: '外交',
};

const POST_LABEL: Readonly<Record<string, string>> = {
  None: '在闲',
  CityGovernor: '城守',
  FortifyOverseer: '军师佐政',
  FarmOverseer: '屯田使',
  General: '将军',
  Scoutmaster: '斥候',
};

const CATEGORY_LABEL: Readonly<Record<string, string>> = {
  Economy: '经济',
  Military: '军事',
  Personnel: '人事',
  Disaster: '灾情',
  Diplomacy: '外交',
  Internal: '内政',
};

export interface HudDeps {
  store: GameStateStore;
  sender: OrderSender;
  scene: WorldScene;
  controls: Controls;
  onSetViewMode(mode: ViewMode): void;
  /** 推进一个月；返回 Promise，自动运行要等它（含战斗演出）彻底结束再排下一月 */
  onTick(): Promise<void>;
  onNewGame(): void;
  /**
   * 城内视图（第 3 轮）：从城池侧栏进入 / 退出。
   * 由 main 接线：进入时盖住疆域图并暂停它的渲染，退出时恢复。
   */
  onEnterCityInterior(cityId: number): void;
  onExitCityInterior(): void;
  /**
   * 赐名（一人一次）。
   * 单独一条通道而不是走 OrderSender：改名不是「指令」，它不进延迟队列、
   * 不消耗行动力、也不产生 StateDelta（人名是字符串，Delta 只装 int）。
   */
  onRenameOfficer(officerId: number, name: string): Promise<boolean>;
  /**
   * 存档（第 5 轮）。三个动作都不是「指令」——不排队、不结算、不产生 StateDelta，
   * 所以走独立回调，由 main 接线到后端现成的四个存档端点。
   * · onSaveGame：保存当前进度（name 空 = 后端默认名「存档X年Y月」）
   * · onLoadSave：读档（成功后由 main 负责 applySnapshot + 重建场景）
   * · onDeleteSave：删除存档
   */
  client: GameClient;
  onSaveGame(name: string): Promise<void>;
  onLoadSave(id: string): Promise<void>;
  onDeleteSave(id: string): Promise<void>;
}

type PanelTarget = { kind: 'city' | 'army' | 'faction' | 'officers' | 'reports'; id: number };

/** 战报条目：一条战斗记录 + 可选的回放动作 */
export interface BattleReportItem {
  title: string;
  detail: string;
  play: () => void;
}

export class Hud {
  private layout: HudLayout;
  private openPanel: PanelTarget | null = null;
  private onInteractRequest: (() => void) | null = null;
  private lastMouse = { x: 0, y: 0 };
  private ticking = false;
  /** 城内视图是否开着（Esc 的优先级靠它决定） */
  private interiorOpen = false;
  /** 底部日志条数（R4），清空时归零；不数 DOM 是因为有上限裁剪 */
  private logCount = 0;
  /** 存档浮层（第 5 轮）：点顶栏「存档」打开，Esc / 空格关闭 */
  private savePanel: SavePanel | null = null;
  /** 自动运行：时间是否在自动流逝（默认开启，玩家嫌手动点推进） */
  private autoOn = true;
  /** 自动运行的下一跳定时器 */
  private autoTimer = 0;
  /** 战报：本局发生过的战斗，最新在前 */
  private battleReports: BattleReportItem[] = [];

  constructor(private readonly deps: HudDeps) {
    injectGlobalStyle();
    this.layout = buildLayout('hud-root');

    // 顶栏按钮
    this.layout.btnTick.addEventListener('click', () => this.requestTick());
    this.layout.btnAuto.addEventListener('click', () => this.toggleAutoTick());
    this.layout.btnReports.addEventListener('click', () => {
      if (this.openPanel?.kind === 'reports') {
        this.closeSide();
      } else {
        this.openBattleReports();
      }
    });
    this.layout.btnSave.addEventListener('click', () => this.openSavePanel());
    this.layout.btnNewGame.addEventListener('click', () => deps.onNewGame());
    this.layout.btnHelp.addEventListener('click', () => this.toggleHelp());
    this.layout.btnInteract.addEventListener('click', () => this.onInteractRequest?.());

    // 底部日志栏（R4）：展开/收起 + 清空
    this.layout.logToggle.addEventListener('click', () => this.toggleLog());
    this.layout.logClear.addEventListener('click', () => this.clearLog());

    // 快捷键意图（由 Controls 上报）
    deps.controls.onShortcut((ev) => this.handleShortcut(ev));

    // 鼠标点过按钮后主动失焦：避免「空格推进下月」被上一次点击的按钮截胡
    this.layout.root.addEventListener('click', (ev) => {
      const t = ev.target;
      if (t instanceof HTMLElement) {
        const btn = t.closest('button');
        if (btn) {
          btn.blur();
        }
      }
    });

    // 悬停卡跟随（贴边时收进视口内，避免被窗口右缘裁掉）
    window.addEventListener('pointermove', (ev) => {
      this.lastMouse.x = ev.clientX;
      this.lastMouse.y = ev.clientY;
      if (!this.layout.hoverCard.classList.contains('sg-hidden')) {
        this.positionHoverCard(ev.clientX, ev.clientY);
      }
    });

    this.bindJoystick();
    deps.store.onChange(() => this.renderVolatile());

    // 默认开启自动运行：时间自己走，玩家不用一次次点「推进下月」
    if (this.autoOn) {
      this.autoTick();
    }
  }

  setInteractHandler(fn: () => void): void {
    this.onInteractRequest = fn;
  }

  /** 城内视图开关（main 在进出城内时同步，用于 Esc 优先级） */
  setInteriorOpen(open: boolean): void {
    this.interiorOpen = open;
  }

  /**
   * 场景重建后同步新的 scene 引用。
   *
   * 【为什么需要】重新开局 / 读档都会 dispose 旧场景并新建一个，HUD 若还攥着
   * 旧实例，军议箭头与「镜头飞向」就会画到已经移除的画布上（点了没反应）。
   */
  setScene(scene: WorldScene): void {
    this.deps.scene = scene;
  }

  // ==========================================================================
  //  存档浮层（第 5 轮）
  // ==========================================================================

  /** 打开存档浮层（顶栏「存档」按钮） */
  openSavePanel(): void {
    if (!this.savePanel) {
      this.savePanel = new SavePanel({
        store: this.deps.store,
        client: this.deps.client,
        onSave: (name) => this.deps.onSaveGame(name),
        onLoad: (id) => this.deps.onLoadSave(id),
        onDelete: (id) => this.deps.onDeleteSave(id),
        notify: (message, kind) => this.toast(message, kind),
      });
    }
    this.savePanel.open(this.layout.overlayLayer);
  }

  /** 关闭存档浮层（读档成功后由 main 调用） */
  closeSavePanel(): void {
    this.savePanel?.close();
  }

  /** 存档列表变了（保存 / 删除之后）→ 刷新浮层里的列表 */
  refreshSavePanel(): void {
    void this.savePanel?.reload();
  }

  // ==========================================================================
  //  快捷键
  // ==========================================================================
  private handleShortcut(ev: ShortcutEvent): void {
    switch (ev.intent) {
      case 'tick': {
        // 浮层打开时，空格/回车 = 关闭浮层（等价于点「继续」），不再推进月份
        if (this.overlayCount() > 0) {
          this.closeTopOverlay();
          return;
        }
        this.requestTick();
        return;
      }
      case 'escape': {
        // 城内视图优先：Esc 先回疆域图，别顺手把城池面板也关了
        if (this.interiorOpen) {
          this.deps.onExitCityInterior();
          return;
        }
        if (this.overlayCount() > 0) {
          this.closeTopOverlay();
          return;
        }
        if (this.openPanel) {
          this.closeSide();
          return;
        }
        return;
      }
      case 'officers':
        if (this.overlayCount() > 0) {
          this.closeTopOverlay();
        }
        this.openOfficersPanel();
        return;
      case 'help':
        this.toggleHelp();
        return;
    }
  }

  /** 推进下月（按钮与快捷键共用同一入口） */
  private requestTick(): void {
    if (this.ticking) {
      return;
    }
    if (this.deps.store.meta?.isFinished) {
      this.toast('本局已终局，无法再推进。', 'error');
      return;
    }
    this.ticking = true;
    void this.deps.onTick().finally(() => {
      this.ticking = false;
    });
  }

  // ==========================================================================
  //  自动运行：时间自己往下走，不用手动点「推进下月」
  // ==========================================================================
  /** 自动运行是否开启（main.ts 据此决定不弹结算面板） */
  isAutoTickOn(): boolean {
    return this.autoOn;
  }

  private toggleAutoTick(): void {
    this.autoOn = !this.autoOn;
    this.layout.btnAuto.textContent = this.autoOn ? '⏸ 暂停时间' : '▶ 继续时间';
    this.layout.btnAuto.classList.toggle('sg-active', this.autoOn);
    if (this.autoOn) {
      this.toast('时间自动流动：每月结算后会停几秒给你看，打开面板下令时自动暂停。', 'info');
      this.autoTick();
    } else {
      window.clearTimeout(this.autoTimer);
      this.autoTimer = 0;
      this.toast('时间已暂停。', 'info');
    }
  }

  private autoTick(delay = AUTO_TICK_GAP_MS): void {
    if (!this.autoOn) {
      return;
    }
    window.clearTimeout(this.autoTimer);
    this.autoTimer = window.setTimeout(() => {
      void (async () => {
        if (!this.autoOn) {
          return;
        }
        if (this.deps.store.meta?.isFinished) {
          this.autoOn = false;
          this.layout.btnAuto.textContent = '▶ 继续时间';
          this.layout.btnAuto.classList.remove('sg-active');
          return;
        }
        // 玩家在办事（侧栏/浮层打开）或正在结算：这一跳让路，稍后再来
        if (this.openPanel || this.overlayCount() > 0 || this.ticking) {
          this.autoTick(1200);
          return;
        }
        // 等这一个月彻底结算完（含战斗演出）再开始下一轮计时
        this.ticking = true;
        try {
          await this.deps.onTick();
        } finally {
          this.ticking = false;
        }
        this.autoTick();
      })();
    }, delay);
  }

  // ==========================================================================
  //  浮层（结算 / 终局 / 帮助）
  // ==========================================================================
  private overlayCount(): number {
    return this.layout.overlayLayer.childElementCount;
  }

  /** 关闭最上层浮层（Esc / 空格） */
  private closeTopOverlay(): void {
    const layer = this.layout.overlayLayer;
    if (layer.lastElementChild) {
      layer.lastElementChild.remove();
    }
  }

  private toggleHelp(): void {
    if (this.overlayCount() > 0 && this.layout.overlayLayer.lastElementChild?.classList.contains('sg-help-overlay')) {
      this.closeTopOverlay();
      return;
    }
    this.showHelp();
  }

  /** 快捷键 / 鼠标操作说明（桌面浮层） */
  private showHelp(): void {
    const overlay = el('div', 'sg-overlay sg-help-overlay');
    const modal = el('div', 'sg-modal sg-panel');
    modal.appendChild(el('h2', undefined, '操作说明 · 鼠标 + 键盘'));

    const grid = el('div', 'sg-help-grid');
    const row = (keys: string, desc: string, note?: string): void => {
      const k = el('span', 'sg-kbd', keys);
      const d = el('span', 'sg-help-desc', desc);
      if (note) {
        d.appendChild(el('span', 'sg-help-note', `　${note}`));
      }
      grid.append(k, d);
    };
    const group = (title: string): void => {
      grid.appendChild(el('div', 'sg-help-group', title));
    };

    group('疆域图操作');
    row('左键单击', '选中城池 / 军团：打开侧栏信息与指令面板，弹出信息卡');
    row('左键拖拽', '旋转镜头');
    row('滚轮', '缩放');
    row('右键拖拽', '平移');

    group('全局');
    row('空格 / 回车', '推进下月（结算本月指令）');
    row('顶栏「暂停时间」', '时间默认自动流动；打开面板时自动暂停');
    row('3', '打开人才任命面板');
    row('Esc', '关闭当前面板 / 浮层');
    row('H', '打开 / 关闭本说明');

    group('城内视图（第 3 轮）');
    row('进入城内 →', '在自己的城池侧栏点这个：俯瞰农田 / 兵营 / 城墙 / 市集');
    row('Esc', '返回疆域图');

    group('存档（第 5 轮）');
    row('顶栏「存档」', '保存当前进度 / 读档 / 删档（删档与读档都有二次确认）');
    row('回车', '在存档名输入框里回车 = 直接保存');
    row('Esc / 空格', '关闭存档浮层');

    modal.appendChild(grid);

    const foot = el('div', 'sg-modal-foot');
    const close = el('button', 'sg-btn sg-primary', '知道了');
    close.addEventListener('click', () => overlay.remove());
    foot.appendChild(close);
    modal.appendChild(foot);

    overlay.appendChild(modal);
    this.layout.overlayLayer.appendChild(overlay);
  }

  // ==========================================================================
  //  顶栏 / 常驻
  // ==========================================================================
  renderVolatile(): void {
    if (!this.deps.store.ready) {
      return;
    }
    const state = this.deps.store.state;
    const meta = this.deps.store.meta;
    this.layout.date.textContent = `第 ${state.year} 年 ${state.month} 月 · 待结算 ${state.pendingOrders.length} 条`;

    // 势力 chips
    clearChildren(this.layout.factionChips);
    for (const f of state.factions) {
      const chip = el('div', 'sg-faction-chip');
      const dot = el('span', 'sg-faction-dot');
      (dot as HTMLSpanElement).style.background = factionColor(f.id);
      chip.appendChild(dot);
      chip.appendChild(el('span', undefined, `${f.name}${f.isPlayer ? '（玩家）' : ''} · ${state.cities.filter((c) => c.ownerFactionId === f.id).length} 城`));
      chip.addEventListener('click', () => this.openFactionPanel(f.id));
      this.layout.factionChips.appendChild(chip);
    }

    if (meta) {
      this.layout.btnTick.disabled = meta.isFinished;
    }

    // 侧栏内容随状态刷新
    if (this.openPanel) {
      this.renderSidePanel();
    }
  }

  setViewModeUI(_mode: ViewMode): void {
    // 【第 8 轮】亲征视角从未实现（WorldScene.setMode 会把 field 强制回落 god），
    // 对应按钮已删除。方法保留是为了不动 main.ts 的接线，这里只确保行走 UI 不出现。
    hide(this.layout.joy);
    hide(this.layout.fieldCorner);
    hide(this.layout.bubble);
    this.layout.btnInteract.disabled = true;
  }

  // ==========================================================================
  //  气泡 / 悬停卡
  // ==========================================================================
  showBubble(text: string, x: number, y: number): void {
    this.layout.bubble.textContent = text;
    this.layout.bubble.style.left = `${x}px`;
    this.layout.bubble.style.top = `${y}px`;
    show(this.layout.bubble);
  }

  hideBubble(): void {
    hide(this.layout.bubble);
  }

  setInteractEnabled(enabled: boolean): void {
    this.layout.btnInteract.disabled = !enabled;
  }

  /** 点击选中后显示信息卡；鼠标一移开就消失（不再常驻） */
  pinHoverCard(target: { kind: 'city' | 'army'; id: number }): void {
    this.showHoverCard(target);
  }

  unpinHoverCard(): void {
    hide(this.layout.hoverCard);
  }

  showHoverCard(target: { kind: 'city' | 'army'; id: number } | null): void {
    const card = this.layout.hoverCard;
    const t = target;
    if (!t || !this.deps.store.ready) {
      hide(card);
      return;
    }
    const state = this.deps.store.state;
    let html = '';
    if (t.kind === 'city') {
      const city = this.deps.store.city(t.id);
      if (!city) {
        hide(card);
        return;
      }
      html = cityHoverText(city, state);
    } else {
      const army = this.deps.store.army(t.id);
      if (!army) {
        hide(card);
        return;
      }
      const owner = state.factions.find((f) => f.id === army.ownerFactionId);
      const commander =
        army.officerId >= 0
          ? this.deps.store.officer(army.officerId)?.name ?? `#${army.officerId}`
          : army.ownerFactionId === (this.deps.store.playerFaction?.id ?? -1) &&
              this.deps.store.state.player.isPersonallyCommanding &&
              this.deps.store.state.player.commandingArmyId === army.id
            ? '君主亲征'
            : '无';
      html = [
        `<b>军团 #${army.id}</b>（${owner?.name ?? '无主'}）`,
        `兵力 ${army.soldiers} · 士气 ${army.morale}/${army.moraleMax}`,
        `统帅 ${commander} · 状态 ${STANCE_LABEL[army.stance] ?? army.stance}`,
        `所在城 ${this.deps.store.city(army.positionCityId)?.name ?? army.positionCityId}`,
      ].join('<br/>');
    }
    card.innerHTML = html;
    this.positionHoverCard(this.lastMouse.x, this.lastMouse.y);
    show(card);
  }

  /** 信息卡定位：贴屏幕右缘 / 下缘时收进视口（桌面大屏 + 侧栏布局下必做） */
  private positionHoverCard(x: number, y: number): void {
    const card = this.layout.hoverCard;
    const w = card.offsetWidth || 220;
    const h = card.offsetHeight || 60;
    const maxX = Math.max(8, window.innerWidth - w - 20);
    const maxY = Math.max(44, window.innerHeight - h - 16);
    card.style.left = `${Math.max(8, Math.min(x, maxX))}px`;
    card.style.top = `${Math.max(44, Math.min(y, maxY))}px`;
  }

  // ==========================================================================
  //  Toast + 底部常驻日志（R4）
  // ==========================================================================
  toast(message: string, kind: 'info' | 'ok' | 'error' = 'info'): void {
    const t = el('div', `sg-toast${kind === 'ok' ? ' sg-ok' : kind === 'error' ? ' sg-error' : ''}`, message);
    this.layout.toasts.appendChild(t);
    while (this.layout.toasts.children.length > 4) {
      this.layout.toasts.removeChild(this.layout.toasts.firstChild!);
    }
    window.setTimeout(() => t.remove(), 4200);
    // toast 即即时反馈，同时也记进底部日志，供事后回看
    this.log(message, kind);
  }

  /** 日志条数上限（防无限增长；超出裁剪最旧一条） */
  private static readonly LOG_MAX = 200;

  /**
   * 往底部日志栏追加一条（R4）。
   * 时间戳用「游戏年月」而非实际时钟：玩家关心的是「这件事发生在第几年几月」，
   * 与结算/顶栏口径一致；同月的多条按先后顺序堆叠。
   */
  log(message: string, kind: 'info' | 'ok' | 'error' = 'info'): void {
    const state = this.deps.store.ready ? this.deps.store.state : null;
    const stamp = state ? `${state.year}年${state.month}月` : '——';
    const line = el('div', `sg-log-line sg-log-${kind}`);
    const time = el('span', 'sg-log-time', stamp);
    const text = el('span', 'sg-log-text', message);
    line.append(time, text);
    this.layout.logList.appendChild(line);
    // 上限裁剪：去掉最旧条目
    while (this.layout.logList.children.length > Hud.LOG_MAX) {
      this.layout.logList.removeChild(this.layout.logList.firstChild!);
      this.logCount = Math.max(0, this.logCount - 1);
    }
    this.logCount += 1;
    this.layout.logCount.textContent = `${this.logCount} 条`;
    // 自动滚动到底，保持最新一条可见
    this.layout.logList.scrollTop = this.layout.logList.scrollHeight;
  }

  /** 展开 / 收起日志栏 */
  private toggleLog(): void {
    const expanded = this.layout.log.classList.toggle('sg-collapsed');
    this.layout.logToggle.textContent = expanded ? '展开 ▲' : '收起 ▼';
    if (!expanded) {
      // 展开时滚到最新
      this.layout.logList.scrollTop = this.layout.logList.scrollHeight;
    }
  }

  /** 清空日志栏 */
  private clearLog(): void {
    clearChildren(this.layout.logList);
    this.logCount = 0;
    this.layout.logCount.textContent = '0 条';
  }

  // ==========================================================================
  //  侧栏面板
  // ==========================================================================
  closeSide(): void {
    this.openPanel = null;
    clearChildren(this.layout.side);
    hide(this.layout.side);
  }

  /**
   * 重画当前打开的面板。
   *
   * 【为什么需要】即时指令（部门任命）当场改了状态，若面板不重画，
   * 玩家会看到「点了任命但粮产数字没动」——而这恰好是核心玩法的验证点。
   */
  refreshPanel(): void {
    this.renderVolatile();
    if (this.openPanel) {
      this.renderSidePanel();
    }
  }

  /** 亲征目标变化/丢失时：不再指向当前面板实体 → 收起 */
  closeEntityPanelUnless(target: { kind: 'city' | 'army'; id: number } | null): void {
    if (!this.openPanel) {
      return;
    }
    if (this.openPanel.kind !== 'city' && this.openPanel.kind !== 'army') {
      return;
    }
    if (!target || this.openPanel.kind !== target.kind || this.openPanel.id !== target.id) {
      this.closeSide();
    }
  }

  openCityPanel(cityId: number): void {
    this.openPanel = { kind: 'city', id: cityId };
    this.renderSidePanel();
  }

  openArmyPanel(armyId: number): void {
    this.openPanel = { kind: 'army', id: armyId };
    this.renderSidePanel();
  }

  openFactionPanel(factionId: number): void {
    this.openPanel = { kind: 'faction', id: factionId };
    this.renderSidePanel();
  }

  openOfficersPanel(): void {
    this.openPanel = { kind: 'officers', id: -1 };
    this.renderSidePanel();
  }

  /** 结算时把本月战斗推入战报（不自动播放，玩家自己点） */
  addBattleReports(items: BattleReportItem[]): void {
    if (items.length === 0) {
      return;
    }
    this.battleReports = [...items, ...this.battleReports].slice(0, 40);
    this.layout.btnReports.textContent = `战报 · ${this.battleReports.length}`;
  }

  openBattleReports(): void {
    this.openPanel = { kind: 'reports', id: -1 };
    this.renderSidePanel();
  }

  private buildReportsCard(): HTMLDivElement {
    const card = this.cardHeader('战报 · 战斗记录', () => this.closeSide());
    if (this.battleReports.length === 0) {
      this.hintLine(card, '本局还没有发生战斗。发生战斗后会记录在这里——点「观看回放」才播放，绝不自动弹出。');
      return card;
    }
    this.hintLine(card, '最新在最上面。只有你点「观看回放」才会播放战斗演出。');
    for (const item of this.battleReports) {
      const row = el('div', 'sg-row');
      row.appendChild(el('span', 'sg-grow', item.title));
      const btn = el('button', 'sg-btn sg-primary', '观看回放');
      btn.addEventListener('click', () => item.play());
      row.appendChild(btn);
      card.appendChild(row);
      card.appendChild(el('div', 'sg-muted', item.detail));
    }
    return card;
  }

  private renderSidePanel(): void {
    if (!this.openPanel || !this.deps.store.ready) {
      return;
    }
    show(this.layout.side);
    clearChildren(this.layout.side);
    switch (this.openPanel.kind) {
      case 'city':
        this.layout.side.appendChild(this.buildCityCard(this.openPanel.id));
        break;
      case 'army':
        this.layout.side.appendChild(this.buildArmyCard(this.openPanel.id));
        break;
      case 'faction':
        this.layout.side.appendChild(this.buildFactionCard(this.openPanel.id));
        break;
      case 'officers':
        this.layout.side.appendChild(this.buildOfficersCard());
        break;
      case 'reports':
        this.layout.side.appendChild(this.buildReportsCard());
        break;
    }
  }

  private cardHeader(title: string, onClose: () => void): HTMLDivElement {
    const card = el('div', 'sg-card sg-panel');
    const h = el('h3');
    const t = el('span', undefined, title);
    const close = el('span', 'sg-close', '✕');
    close.title = '关闭（Esc）';
    close.addEventListener('click', onClose);
    h.append(t, close);
    card.appendChild(h);
    return card;
  }

  private kv(card: HTMLElement, rows: Array<[string, string]>): void {
    const grid = el('div', 'sg-kv');
    for (const [k, v] of rows) {
      const kEl = el('span', 'k', k);
      const vEl = el('span', 'v');
      vEl.innerHTML = v;
      grid.append(kEl, vEl);
    }
    card.appendChild(grid);
  }

  private hintLine(card: HTMLElement, text: string): void {
    card.appendChild(el('div', 'sg-hint', text));
  }

  // ---- 城池面板 + 宏观指令（征兵/屯田/修城/休整/任命入口） ----
  private buildCityCard(cityId: number): HTMLDivElement {
    const state = this.deps.store.state;
    const city = this.deps.store.city(cityId);
    const pf = this.deps.store.playerFaction;
    const card = this.cardHeader(`${city?.isCapital ? '★' : ''}${city?.name ?? `城 #${cityId}`}`, () => this.closeSide());
    if (!city) {
      card.appendChild(el('div', 'sg-muted', '城池不存在'));
      return card;
    }
    const owner = state.factions.find((f) => f.id === city.ownerFactionId);
    this.kv(card, [
      ['归属', `<span style="color:${factionColor(city.ownerFactionId)}">${owner?.name ?? '无主'}</span>`],
      ['人口', `${city.population} / ${city.maxPopulation}`],
      ['城防', `${city.defense} / ${city.maxDefense}（等级 ${city.fortifyLevel}）`],
      ['屯田等级', `${city.farmLevel}`],
      ['相邻', city.adjacentCityIds.map((id) => this.deps.store.city(id)?.name ?? id).join('、')],
    ]);

    if (pf && city.ownerFactionId === pf.id) {
      const actions = el('div', 'sg-actions');

      // ---- 城内视图（第 3 轮）：一眼看见「我这城建到什么程度了」----
      const enterCity = el('button', 'sg-btn sg-primary', '进入城内 · 查看建筑 →');
      enterCity.title = '俯瞰城内：农田 / 兵营 / 城墙 / 市集 按当前等级呈现，正在兴建的那一处会闪金框';
      enterCity.addEventListener('click', () => this.deps.onEnterCityInterior(city.id));
      actions.appendChild(enterCity);
      const mkAmountRow = (label: string, action: 'Recruit' | 'Develop' | 'Fortify', def: number): void => {
        const row = el('div', 'sg-row');
        row.appendChild(el('span', 'sg-grow', label));
        const input = el('input', 'sg-input') as HTMLInputElement;
        input.type = 'number';
        input.value = String(def);
        input.min = '0';
        input.title = `${label}数量`;
        row.appendChild(input);
        const btn = el('button', 'sg-btn', '下令') as HTMLButtonElement;
        btn.title = `下达「${label}」指令`;
        btn.addEventListener('click', () => {
          const amount = Math.max(0, Math.floor(Number(input.value) || 0));
          this.sendWithFeedback(btn, { action, targetId: city.id, amount });
        });
        row.appendChild(btn);
        actions.appendChild(row);
      };
      mkAmountRow('征兵（人数）', 'Recruit', 100);
      mkAmountRow('屯田（投银）', 'Develop', 100);
      mkAmountRow('修城（投银）', 'Fortify', 100);

      // ---- 建筑升级（v1.7）：点击即开工，下月起逐月推进，到期 +1 级 ----
      this.renderBuildingRows(actions, city);

      // ---- 招贤（v1.7）：花银从在野人才里抽一人 ----
      this.renderRecruitOfficerRow(actions, city);
      // 驻军 + 出征 + 休整
      for (const army of state.armies.filter((a) => a.positionCityId === city.id)) {
        const row = el('div', 'sg-row');
        row.appendChild(el('span', 'sg-grow', `驻军 #${army.id} · ${army.soldiers} 人 · 士气 ${army.morale}/${army.moraleMax}`));

        // 【第 8 轮】出征入口直接放城池面板里。原先只能在「军团」图标上点开军议，
        // 玩家点城池根本找不到怎么打人，等于核心玩法不可达。
        const march = el('button', 'sg-btn sg-primary', '出征');
        march.title = '选择相邻目标城 → 下令进军（Dispatch）';
        march.addEventListener('click', () => this.renderMarchFlow(army, card));
        row.appendChild(march);

        const rest = el('button', 'sg-btn', '休整');
        if (army.hasRestedThisMonth) {
          rest.disabled = true;
          rest.title = '本月已休整过（下月可再休整）';
        }
        rest.addEventListener('click', () => {
          void this.deps.sender.send({ action: 'Rest', targetId: army.id });
        });
        row.appendChild(rest);
        actions.appendChild(row);
      }

      const appoint = el('button', 'sg-btn', '人才任命 / 换任 →');
      appoint.title = '打开人才任命面板（快捷键 3）';
      appoint.addEventListener('click', () => this.openOfficersPanel());
      actions.appendChild(appoint);

      card.appendChild(actions);
      this.hintLine(card, '出征：点驻军行的「出征」→ 选相邻目标城 → 下令进军。');
      this.hintLine(card, '下令成功 ≠ 执行成功：指令入队后于下月结算，成败与原因以结算结果的 code / message 为准。');
    } else {
      this.hintLine(card, '非本势力城池：仅可查看。攻取需先点自己的城，用驻军「出征」。');
    }
    return card;
  }

  // ---- 建筑升级行（v1.7） ----
  /**
   * 下令按钮统一反馈：点下去立刻显示「已下令 · 下月结算」并锁定，
   * 失败则回滚。以前下发后按钮毫无变化，玩家会以为没点成功又重复点。
   */
  private sendWithFeedback(
    btn: HTMLButtonElement,
    payload: Parameters<OrderSender['send']>[0],
    doneText = '已下令',
  ): void {
    const original = btn.textContent ?? '';
    btn.disabled = true;
    btn.textContent = `${doneText} · 下月结算`;
    void this.deps.sender.send(payload).then((ok) => {
      if (!ok) {
        btn.disabled = false;
        btn.textContent = original;
      }
    });
  }

  /**
   * 画四类建筑的当前等级与「兴建」按钮。
   *
   * 【语义】下令只是开工：扣银、记下工程、开始倒计时；等级由下月月初的
   * BuildingSystem 完工时才 +1。所以这里显示的是「在建 X，剩 N 月」，
   * 而不是点了立刻变强——等待成本是显式的。
   */
  private renderBuildingRows(host: HTMLElement, city: Readonly<City>): void {
    const title = el('div', 'sg-muted', '建筑（上限 5 级，工期 1-2 月）');
    host.appendChild(title);

    const levels = [city.farmLevel, city.barracksLevel, city.fortifyLevel, city.marketLevel];
    const inProgress = city.upgradingBuilding >= 0;

    for (let i = 0; i < BUILDING_NAMES.length; i++) {
      const row = el('div', 'sg-row');
      const level = levels[i] ?? 0;
      row.appendChild(
        el('span', 'sg-grow', `${BUILDING_NAMES[i]} Lv${level} · ${BUILDING_EFFECTS[i]}`),
      );

      const btn = el('button', 'sg-btn', level >= 5 ? '已满级' : '兴建') as HTMLButtonElement;
      if (inProgress) {
        btn.disabled = true;
        btn.title = `本城正在兴建${BUILDING_NAMES[city.upgradingBuilding] ?? ''}，剩 ${city.upgradeMonthsRemaining} 月，先等它完工`;
      } else if (level >= 5) {
        btn.disabled = true;
      } else {
        btn.title = `下令兴建${BUILDING_NAMES[i]}：扣银并开工，${BUILDING_NAMES[i] === '城墙' ? '2' : '1'} 个月后完工 +1 级`;
      }
      btn.addEventListener('click', () => {
        const htmlBtn = btn as HTMLButtonElement;
        const original = htmlBtn.textContent ?? '';
        htmlBtn.disabled = true;
        htmlBtn.textContent = '已开工 · 下月结算';
        void this.deps.sender
          .send({ action: 'Build', targetId: city.id, secondaryTargetId: i })
          .then((ok) => {
            if (!ok) {
              htmlBtn.disabled = false;
              htmlBtn.textContent = original;
              return;
            }
            // 下令成功：城内视图立刻把农夫/工匠动画演起来，不等下月结算
            markBuildPending(city.id, i);
          });
      });
      row.appendChild(btn);
      host.appendChild(row);
    }

    if (inProgress) {
      host.appendChild(
        el(
          'div',
          'sg-hint',
          `在建：${BUILDING_NAMES[city.upgradingBuilding] ?? '?'}，还需 ${city.upgradeMonthsRemaining} 个月完工。`,
        ),
      );
    }
  }

  // ---- 招贤（v1.7） ----
  /** 花银招一名在野人才。名额上限与每月一次由结算端校验，这里只负责下指令。 */
  private renderRecruitOfficerRow(host: HTMLElement, city: Readonly<City>): void {
    const pf = this.deps.store.playerFaction;
    const mine = this.deps.store.state.officers.filter(
      (o) => pf && o.ownerFactionId === pf.id && o.isAlive,
    ).length;
    const wild = this.deps.store.state.officers.filter(
      (o) => o.ownerFactionId === -1 && o.isAlive,
    ).length;

    const row = el('div', 'sg-row');
    row.appendChild(el('span', 'sg-grow', `招贤 · 门下 ${mine} 人 · 在野 ${wild} 人`));
    const btn = el('button', 'sg-btn', '招贤（200 银）') as HTMLButtonElement;
    if (wild === 0) {
      btn.disabled = true;
      btn.title = '在野已无人可招';
    } else if (mine >= 10) {
      btn.disabled = true;
      btn.title = '门下已满 10 人';
    } else {
      btn.title = '花 200 银从在野人才中随机招揽一人（每月一次）';
    }
    btn.addEventListener('click', () => {
      void this.deps.sender.send({ action: 'RecruitOfficer', targetId: city.id, amount: 200 });
    });
    row.appendChild(btn);
    host.appendChild(row);
  }

  // ---- 部队面板（亲征） + 军议 ----
  private buildArmyCard(armyId: number): HTMLDivElement {
    const state = this.deps.store.state;
    const army = this.deps.store.army(armyId);
    const pf = this.deps.store.playerFaction;
    const card = this.cardHeader(`军团 #${armyId}`, () => this.closeSide());
    if (!army || !pf) {
      card.appendChild(el('div', 'sg-muted', '部队不存在'));
      return card;
    }
    const commander =
      army.officerId >= 0
        ? this.deps.store.officer(army.officerId)?.name ?? `#${army.officerId}`
        : state.player.isPersonallyCommanding && state.player.commandingArmyId === army.id
          ? '君主亲征'
          : '无';
    const targetName = army.targetCityId >= 0 ? this.deps.store.city(army.targetCityId)?.name ?? String(army.targetCityId) : '—';
    this.kv(card, [
      ['归属', `<span style="color:${factionColor(army.ownerFactionId)}">${state.factions.find((f) => f.id === army.ownerFactionId)?.name ?? ''}</span>`],
      ['统帅', commander],
      ['兵力', `${army.soldiers}`],
      ['士气', `${army.morale} / ${army.moraleMax}`],
      ['状态', STANCE_LABEL[army.stance] ?? army.stance],
      ['所在城', this.deps.store.city(army.positionCityId)?.name ?? String(army.positionCityId)],
      ['行军目标', targetName],
      ['连续未战', `${army.monthsWithoutBattle} 月`],
    ]);

    if (army.ownerFactionId === pf.id) {
      const actions = el('div', 'sg-actions');

      const rest = el('button', 'sg-btn', '休整');
      if (army.hasRestedThisMonth) {
        rest.disabled = true;
        rest.title = '本月已休整过（下月可再休整）';
      }
      rest.addEventListener('click', () => {
        void this.deps.sender.send({ action: 'Rest', targetId: army.id });
      });
      actions.appendChild(rest);

      const march = el('button', 'sg-btn sg-primary', '军议 · 进军');
      march.title = '选择目标城 → 地面升起行军箭头 → 下令进军';
      march.addEventListener('click', () => this.renderMarchFlow(army, card));
      actions.appendChild(march);

      card.appendChild(actions);
      this.hintLine(card, '军议：目标城必须与本军所在城相邻（不相邻的会置灰标注）→ 地面升起行军箭头 → 「下令进军」。');
    } else {
      this.hintLine(card, '非本势力部队：仅可查看。');
    }
    return card;
  }

  /** 军议流程（展开在部队卡片底部） */
  private renderMarchFlow(army: Readonly<Army>, card: HTMLDivElement): void {
    // 防重复展开
    const existing = card.querySelector('.sg-march-flow');
    if (existing) {
      existing.remove();
      this.deps.scene.removeMarchArrow();
      return;
    }
    const flow = el('div', 'sg-march-flow');
    (flow as HTMLElement).style.cssText = `margin-top:10px;padding:10px;border:1px solid ${UI.borderGold};border-radius:8px;`;

    const state = this.deps.store.state;
    const here = this.deps.store.city(army.positionCityId);
    flow.appendChild(el('div', 'sg-section-title', `军议：自 ${here?.name ?? army.positionCityId} 出兵`));

    // 目标城：全图城池都列出来，非相邻的置灰并标注原因。
    // 【第 8 轮】原来只塞 adjacentCityIds，玩家看到下拉里就一两个城，
    // 以为「地图上明明有别的城，为什么不让我选」——规则没被看见。
    const adj = new Set(here?.adjacentCityIds ?? []);
    if (adj.size === 0) {
      flow.appendChild(el('div', 'sg-muted', '所在城没有相邻城池，无法进军。'));
      card.appendChild(flow);
      return;
    }

    const options = state.cities.filter((c) => c.id !== army.positionCityId);
    const select = el('select', 'sg-select') as HTMLSelectElement;
    let firstValid = '';
    for (const c of options) {
      const ok = adj.has(c.id);
      const opt = el('option') as HTMLOptionElement;
      opt.value = String(c.id);
      opt.disabled = !ok;
      opt.textContent = `${c.name}（${state.factions.find((f) => f.id === c.ownerFactionId)?.name ?? '无主'}）${ok ? '' : ' · 不相邻'}`;
      if (ok && !firstValid) {
        firstValid = String(c.id);
      }
      select.appendChild(opt);
    }
    select.value = firstValid;
    select.addEventListener('change', () => this.previewMarch(army.positionCityId, Number(select.value)));
    const selRow = el('div', 'sg-row');
    selRow.appendChild(el('span', 'sg-grow', '目标城'));
    selRow.appendChild(select);
    flow.appendChild(selRow);

    // 兵力
    const amount = el('input', 'sg-input') as HTMLInputElement;
    amount.type = 'number';
    amount.value = String(army.soldiers);
    amount.min = '1';
    amount.title = '出征兵力';
    const amountRow = el('div', 'sg-row');
    amountRow.appendChild(el('span', 'sg-grow', '兵力'));
    amountRow.appendChild(amount);
    flow.appendChild(amountRow);

    this.previewMarch(army.positionCityId, Number(select.value));

    const btnRow = el('div', 'sg-actions');
    const go = el('button', 'sg-btn sg-primary', '下令进军');
    go.addEventListener('click', () => {
      const toCity = Number(select.value);
      void this.deps.sender.send({
        action: 'Dispatch',
        targetId: toCity,
        secondaryTargetId: army.positionCityId,
        amount: Math.max(1, Math.floor(Number(amount.value) || army.soldiers)),
      });
      this.deps.scene.removeMarchArrow();
      flow.remove();
    });
    const cancel = el('button', 'sg-btn', '取消');
    cancel.addEventListener('click', () => {
      this.deps.scene.removeMarchArrow();
      flow.remove();
    });
    btnRow.append(go, cancel);
    flow.appendChild(btnRow);
    card.appendChild(flow);
  }

  private previewMarch(fromCityId: number, toCityId: number): void {
    if (Number.isFinite(toCityId) && toCityId >= 0) {
      this.deps.scene.showMarchArrow(fromCityId, toCityId);
    }
  }

  // ---- 势力面板 ----
  private buildFactionCard(factionId: number): HTMLDivElement {
    const state = this.deps.store.state;
    const f = this.deps.store.faction(factionId);
    const card = this.cardHeader(`势力 · ${f?.name ?? factionId}`, () => this.closeSide());
    if (!f) {
      return card;
    }
    this.kv(card, [
      ['身份', f.isPlayer ? '玩家' : 'AI'],
      ['都城', this.deps.store.city(f.capitalCityId)?.name ?? String(f.capitalCityId)],
      ['粮 / 银', `${f.resources.grain} / ${f.resources.silver}`],
      ['兵源池', `${f.resources.soldier}`],
      ['民心', `${f.resources.morale}`],
      ['稳定', `${f.resources.stability}`],
      ['声望', `${f.prestige ?? 0}（尺度 ${f.unlockedScale ?? 'S0_Local'}）`],
    ]);

    // ---- 五部门用人（v1.7 核心）----
    this.renderDepartments(card, f.id, f.isPlayer);

    const cities = state.cities.filter((c) => c.ownerFactionId === f.id);
    const cityRow = el('div', 'sg-row');
    cityRow.appendChild(el('span', 'sg-grow', `城池 ${cities.length} 座`));
    for (const c of cities) {
      const b = el('button', 'sg-mini', c.name) as HTMLButtonElement;
      b.style.borderColor = factionColor(f.id);
      b.title = `镜头飞向 ${c.name}`;
      b.addEventListener('click', () => {
        this.deps.scene.focusCity(c.id);
        this.deps.scene.setSelectedCity(c.id);
      });
      cityRow.appendChild(b);
    }
    card.appendChild(cityRow);
    const officers = state.officers.filter((o) => o.ownerFactionId === f.id && o.isAlive);
    card.appendChild(el('div', 'sg-muted', `人才 ${officers.length} 名 · 部队 ${state.armies.filter((a) => a.ownerFactionId === f.id).length} 支`));
    return card;
  }

  // ---- 五部门用人（v1.7） ----
  /**
   * 画五个部门：主官是谁、部门效率多少、以及「换人」下拉。
   *
   * 【为什么这是核心】部门效率直接从主官属性算出来，效率乘在粮产/兵源/银收/工期上。
   * 换一个内政官，粮产当月就变——这是「用人」这件事唯一可被验证的地方。
   * 效率的数值由后端 /api/preview-income 给出，本层不自己乘。
   */
  private renderDepartments(card: HTMLElement, factionId: number, isPlayer: boolean): void {
    const state = this.deps.store.state;
    const faction = this.deps.store.faction(factionId);
    if (!faction) {
      return;
    }

    card.appendChild(el('div', 'sg-muted', '五部门 · 用人'));

    const mine = state.officers.filter(
      (o) => o.ownerFactionId === factionId && o.isAlive,
    );
    const heads = faction.departmentHead ?? {};
    const departments = Object.keys(DEPARTMENT_INFO) as DepartmentType[];

    for (const dept of departments) {
      const info = DEPARTMENT_INFO[dept];
      const headId = heads[dept];
      const head = headId !== undefined && headId >= 0 ? this.deps.store.officer(headId) : undefined;

      const row = el('div', 'sg-row');
      const label = el('span', 'sg-grow');
      label.innerHTML = head
        ? `<b>${info.label}</b> · ${head.name}（${info.attr} ${this.attrOf(head, dept)}）`
        : `<b>${info.label}</b> · <span class="sg-muted">空岗（效率 80%）</span>`;
      label.title = `${info.label}部门影响${info.effect}，效率来自主官${info.attr}`;
      row.appendChild(label);

      if (isPlayer) {
        const select = el('select', 'sg-input') as HTMLSelectElement;
        select.title = `换${info.label}主官（点完立刻生效，不用等结算）`;
        const optNone = el('option', undefined, '空岗') as HTMLOptionElement;
        optNone.value = '-1';
        select.appendChild(optNone);
        for (const o of mine) {
          const opt = el('option', undefined, `${o.name}（${this.attrOf(o, dept)}）`) as HTMLOptionElement;
          opt.value = String(o.id);
          select.appendChild(opt);
        }
        select.value = head ? String(head.id) : '-1';
        select.addEventListener('change', () => {
          const officerId = Number(select.value);
          void this.deps.sender.send({
            action: 'AppointDepartment',
            targetId: departments.indexOf(dept),
            secondaryTargetId: officerId,
          });
        });
        row.appendChild(select);
      }

      card.appendChild(row);
    }

    this.hintLine(
      card,
      '部门效率 = 10000 + (主官属性 − 50) × 200（万分比）。空岗按 8000 计。' +
        '换人立刻生效：粮产/兵源/银收当月就变，不用等结算。',
    );
  }

  /** 取人才在某个部门口径下的属性值（经济取治理与魅力均值，与后端同口径的展示） */
  private attrOf(o: Readonly<Officer>, dept: DepartmentType): number {
    switch (dept) {
      case 'Military':
        return o.command;
      case 'Interior':
        return o.governance;
      case 'Economy':
        return Math.floor((o.governance + o.charisma) / 2);
      case 'Science':
        return o.intellect ?? 0;
      case 'Diplomacy':
        return o.charisma ?? 0;
      default:
        return 0;
    }
  }

  // ---- 人才面板（R6） ----
  private buildOfficersCard(): HTMLDivElement {
    const state = this.deps.store.state;
    const pf = this.deps.store.playerFaction;
    const card = this.cardHeader('人才 · 任命', () => this.closeSide());

    const reward = el('button', 'sg-btn sg-disabled', '赏赐（待契约扩展）');
    reward.disabled = true;
    reward.title = 'ActionType.Reward=101 为 P1 保留位，Resolver 未实现（R6 本轮不做）';
    const headRow = el('div', 'sg-row');
    headRow.appendChild(el('span', 'sg-grow', `共 ${state.officers.length} 人`));
    headRow.appendChild(reward);
    card.appendChild(headRow);

    const table = el('table', 'sg-table');
    const thead = el('thead');
    const trh = el('tr');
    for (const h of ['人才', '专长', '统率', '政务', '忠诚', '岗位', '任职城', '操作']) {
      trh.appendChild(el('th', undefined, h));
    }
    thead.appendChild(trh);
    const tbody = el('tbody');
    const officers = [...state.officers].sort((a, b) => {
      const pa = a.ownerFactionId === pf?.id ? 0 : 1;
      const pb = b.ownerFactionId === pf?.id ? 0 : 1;
      return pa - pb || a.id - b.id;
    });
    for (const o of officers) {
      const tr = el('tr');
      if (!o.isAlive) {
        tr.className = 'sg-row-dead';
      }
      const owner = state.factions.find((f) => f.id === o.ownerFactionId);
      tr.appendChild(el('td', undefined, `${o.name}${o.ownerFactionId === pf?.id ? ' ◆' : ''}`));
      tr.appendChild(el('td', undefined, SPECIALTY_LABEL[o.specialty] ?? o.specialty));
      tr.appendChild(el('td', undefined, String(o.command)));
      tr.appendChild(el('td', undefined, String(o.governance)));
      tr.appendChild(el('td', undefined, o.isAlive ? String(o.loyalty) : '—'));
      tr.appendChild(el('td', undefined, o.isAlive ? POST_LABEL[o.post] ?? o.post : '—'));
      tr.appendChild(el('td', undefined, o.postCityId >= 0 ? this.deps.store.city(o.postCityId)?.name ?? String(o.postCityId) : '—'));

      const tdOp = el('td');
      if (pf && o.isAlive && o.ownerFactionId === pf.id) {
        const btn = el('button', 'sg-mini', o.post === 'None' ? '任命' : '换任');
        btn.addEventListener('click', () => this.renderAssignRow(o, tdOp, btn));
        tdOp.appendChild(btn);

        // 赐名：一人一次。改过之后按钮消失，替换成不可点的「已赐名」。
        const nameBtn = el('button', 'sg-mini', o.nameChangeUsed ? '已赐名' : '赐名') as HTMLButtonElement;
        if (o.nameChangeUsed) {
          nameBtn.disabled = true;
          nameBtn.title = '改名只有一次机会，已经用掉了';
        } else {
          nameBtn.title = '为这名人才赐名（只有一次机会，改后不可再改）';
          nameBtn.addEventListener('click', () => {
            const input = window.prompt(`为「${o.name}」赐名（最多 8 个字，仅此一次）：`, o.name);
            if (input === null) {
              return;
            }
            void this.deps.onRenameOfficer(o.id, input.trim());
          });
        }
        tdOp.appendChild(nameBtn);
      } else {
        tdOp.appendChild(el('span', 'sg-muted', owner ? '他势力' : '在野'));
      }
      tr.appendChild(tdOp);
      tbody.appendChild(tr);
    }
    table.append(thead, tbody);
    card.appendChild(table);
    this.hintLine(card, '任命语义（照 04 范例）：AssignPost，TargetId=人才 Id，Amount=-1，SecondaryTargetId=岗位所在城 Id。岗位由结算端按专长推导。');
    return card;
  }

  private renderAssignRow(officer: Readonly<Officer>, td: HTMLElement, btn: HTMLButtonElement): void {
    // 已展开 → 收起
    const existing = td.querySelector('.sg-assign');
    if (existing) {
      existing.remove();
      btn.textContent = officer.post === 'None' ? '任命' : '换任';
      return;
    }
    const wrap = el('div', 'sg-assign');
    (wrap as HTMLElement).style.cssText = 'display:flex;gap:6px;align-items:center;margin-top:6px;';
    const select = el('select', 'sg-select') as HTMLSelectElement;
    const cities = this.deps.store.playerCities;
    for (const c of cities) {
      const opt = el('option') as HTMLOptionElement;
      opt.value = String(c.id);
      opt.textContent = c.name;
      select.appendChild(opt);
    }
    const ok = el('button', 'sg-mini', '确定');
    ok.addEventListener('click', () => {
      const cityId = Number(select.value);
      if (!Number.isFinite(cityId)) {
        this.toast('没有可任命的城池', 'error');
        return;
      }
      void this.deps.sender.send({ action: 'AssignPost', targetId: officer.id, secondaryTargetId: cityId, amount: -1 });
      wrap.remove();
      btn.textContent = officer.post === 'None' ? '任命' : '换任';
    });
    wrap.append(select, ok);
    td.appendChild(wrap);
  }

  // ==========================================================================
  //  R5 回合结算覆盖层
  // ==========================================================================
  showSettlement(tick: TickResponse): void {
    const overlay = el('div', 'sg-overlay');
    const modal = el('div', 'sg-modal sg-panel');
    modal.appendChild(el('h2', undefined, `结算 · 第 ${tick.year} 年 ${tick.month} 月`));

    const pfId = this.deps.store.playerFaction?.id ?? 0;

    // 玩家指令成败
    modal.appendChild(el('div', 'sg-section-title', '玩家指令'));
    const playerResults = tick.results.filter((r) => r.actorType === 'Player');
    if (playerResults.length === 0) {
      modal.appendChild(el('div', 'sg-muted', '本月没有玩家指令。'));
    }
    for (const r of playerResults) {
      modal.appendChild(this.buildResultItem(r, pfId));
    }

    // 全部指令（含 AI）
    const aiResults = tick.results.filter((r) => r.actorType !== 'Player');
    if (aiResults.length > 0) {
      const details = el('details');
      details.appendChild(el('summary', undefined, `全部指令结算（含 AI ${aiResults.length} 条）`));
      const box = el('div');
      (box as HTMLElement).style.marginTop = '8px';
      for (const r of aiResults) {
        box.appendChild(this.buildResultItem(r, pfId));
      }
      details.appendChild(box);
      modal.appendChild(details);
    }

    // 本月事件
    modal.appendChild(el('div', 'sg-section-title', `本月事件（${tick.events.length}）`));
    if (tick.events.length === 0) {
      modal.appendChild(el('div', 'sg-muted', '本月无事件。'));
    }
    for (const e of tick.events) {
      const row = el('div', 'sg-event-item');
      row.appendChild(el('span', 'sg-event-cat', CATEGORY_LABEL[e.category] ?? e.category));
      const msg = el('span', undefined, e.message);
      if (e.factionId === pfId) {
        (msg as HTMLElement).style.color = UI.accent;
      }
      row.appendChild(msg);
      modal.appendChild(row);
    }

    // 终局提示
    if (tick.meta.isFinished) {
      modal.appendChild(el('div', 'sg-section-title', '终局'));
      const winner = tick.meta.winnerFactionId >= 0 ? this.deps.store.faction(tick.meta.winnerFactionId)?.name ?? `势力 ${tick.meta.winnerFactionId}` : '无（僵持）';
      modal.appendChild(el('div', undefined, `胜方：${winner} · 原因：${tick.meta.gameOverReason}`));
    }

    const foot = el('div', 'sg-modal-foot');
    const pending = tick.state.pendingOrders.length;
    const cont = el('button', 'sg-btn sg-primary', pending > 0 ? `继续（下月待结算 ${pending} 条）` : '继续');
    cont.title = '关闭结算面板（空格 / 回车 / Esc）';
    cont.addEventListener('click', () => overlay.remove());
    foot.append(cont);
    modal.appendChild(foot);

    overlay.appendChild(modal);
    this.layout.overlayLayer.appendChild(overlay);
    // 焦点给「继续」：空格 / 回车直接关闭结算面板，不误触发全局「推进下月」
    cont.focus();
  }

  private buildResultItem(r: SettledOrder, pfId: number): HTMLElement {
    const ok = r.code === 'Success';
    const item = el('div', `sg-result-item ${ok ? 'sg-good' : 'sg-bad'}`);
    const head = el('div');
    const who = r.actorType === 'Player' ? '玩家' : r.actorType === 'AiFaction' ? this.deps.store.faction(r.actorId)?.name ?? `势力 ${r.actorId}` : `人才 ${this.deps.store.officer(r.actorId)?.name ?? r.actorId}`;
    head.innerHTML =
      `<span class="sg-code-tag" style="color:${ok ? UI.ok : UI.danger}">${ok ? '✓ 成功' : '✗ 失败'}</span>` +
      ` ${who} · ${actionLabel(r.action)}` +
      ` · 目标 ${this.describeTarget(r)}` +
      ` · 第 ${r.executeYear} 年 ${r.executeMonth} 月`;
    item.appendChild(head);
    if (r.message) {
      const m = el('div');
      (m as HTMLElement).style.color = ok ? UI.sub : '#f0c0c0';
      m.textContent = r.message;
      item.appendChild(m);
    }
    if (r.deltas.length > 0) {
      const d = el('div', 'sg-delta-line');
      d.innerHTML = r.deltas.map((x) => this.describeDelta(x, pfId)).join('<br/>');
      item.appendChild(d);
    }
    return item;
  }

  private describeTarget(r: SettledOrder): string {
    switch (r.action) {
      case 'Recruit':
      case 'Fortify':
      case 'Develop':
        return this.deps.store.city(r.targetId)?.name ?? String(r.targetId);
      case 'Dispatch':
        return `${this.deps.store.city(r.secondaryTargetId)?.name ?? r.secondaryTargetId} → ${this.deps.store.city(r.targetId)?.name ?? r.targetId}（${r.amount} 人）`;
      case 'AssignPost':
        return `${this.deps.store.officer(r.targetId)?.name ?? r.targetId} → ${this.deps.store.city(r.secondaryTargetId)?.name ?? r.secondaryTargetId}`;
      case 'Rest':
        return `军团 #${r.targetId}`;
      default:
        return String(r.targetId);
    }
  }

  private describeDelta(d: StateDelta, _pfId: number): string {
    const target =
      d.targetType === 'City'
        ? this.deps.store.city(d.targetId)?.name ?? `城 #${d.targetId}`
        : d.targetType === 'Army'
          ? `军团 #${d.targetId}`
          : d.targetType === 'Faction'
            ? this.deps.store.faction(d.targetId)?.name ?? `势力 #${d.targetId}`
            : d.targetType === 'Officer'
              ? this.deps.store.officer(d.targetId)?.name ?? `人才 #${d.targetId}`
              : d.targetType;
    return `${target} · ${fieldLabel(d.field)}：${d.oldValue} → ${d.newValue}`;
  }

  // ==========================================================================
  //  终局覆盖层
  // ==========================================================================
  showGameOver(meta: GameMeta): void {
    const overlay = el('div', 'sg-overlay');
    const modal = el('div', 'sg-modal sg-panel');
    modal.appendChild(el('h2', undefined, '终局'));
    const winner = meta.winnerFactionId >= 0 ? this.deps.store.faction(meta.winnerFactionId)?.name ?? `势力 ${meta.winnerFactionId}` : '无（僵持）';
    modal.appendChild(el('div', undefined, `胜方：${winner}`));
    modal.appendChild(el('div', 'sg-muted', `原因：${meta.gameOverReason} · 已推进 ${meta.monthsElapsed} 个月`));
    const foot = el('div', 'sg-modal-foot');
    const restart = el('button', 'sg-btn sg-primary', '重新开局');
    restart.addEventListener('click', () => {
      overlay.remove();
      this.deps.onNewGame();
    });
    foot.appendChild(restart);
    modal.appendChild(foot);
    overlay.appendChild(modal);
    this.layout.overlayLayer.appendChild(overlay);
    restart.focus();
  }

  // ==========================================================================
  //  战斗演出字幕（Cinema 用）
  // ==========================================================================
  showCinema(initialText: string): void {
    show(this.layout.cinemaTop);
    show(this.layout.cinemaBottom);
    this.setCinemaText(initialText);
    show(this.layout.cinemaSkip);
  }

  setCinemaText(text: string): void {
    this.layout.cinemaText.textContent = text;
    show(this.layout.cinemaText);
  }

  hideCinema(): void {
    hide(this.layout.cinemaTop);
    hide(this.layout.cinemaBottom);
    hide(this.layout.cinemaText);
    hide(this.layout.cinemaSkip);
  }

  // ==========================================================================
  //  虚拟摇杆（仅触摸设备出现；桌面端不显示、不拦截鼠标）
  // ==========================================================================
  private bindJoystick(): void {
    const joy = this.layout.joy;
    const knob = this.layout.joyKnob;
    let active = false;
    let cx = 0;
    let cy = 0;
    const R = 48;

    const reset = (): void => {
      active = false;
      knob.style.transform = 'translate(-50%, -50%)';
      this.deps.controls.setJoystick(0, 0);
    };

    joy.addEventListener('pointerdown', (ev) => {
      active = true;
      const rect = joy.getBoundingClientRect();
      cx = rect.left + rect.width / 2;
      cy = rect.top + rect.height / 2;
      joy.setPointerCapture(ev.pointerId);
    });
    joy.addEventListener('pointermove', (ev) => {
      if (!active) {
        return;
      }
      let dx = ev.clientX - cx;
      let dy = ev.clientY - cy;
      const len = Math.hypot(dx, dy);
      if (len > R) {
        dx = (dx / len) * R;
        dy = (dy / len) * R;
      }
      knob.style.transform = `translate(calc(-50% + ${dx}px), calc(-50% + ${dy}px))`;
      this.deps.controls.setJoystick(dx / R, -dy / R);
    });
    joy.addEventListener('pointerup', reset);
    joy.addEventListener('pointercancel', reset);
  }
}
