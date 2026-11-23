// ============================================================================
//  Ui/Hud.ts —— DOM 覆盖层：顶栏 / 势力·城池·部队·人才面板 / 军议 / 回合结算 /
//  交互气泡与摇杆 / 战斗演出字幕 / 游戏终局。
//  铁律：只读 store，只经 OrderSender 下令；不做数值计算、不判合法性。
// ============================================================================

import type { GameStateStore } from '../Core/GameStateStore.js';
import { actionLabel } from '../Core/GameClient.js';
import type { OrderSender } from '../Core/GameClient.js';
import type { Controls } from '../Player/Controls.js';
import type { HoverTarget, ViewMode } from '../Scene/WorldScene.js';
import type { WorldScene } from '../Scene/WorldScene.js';
import { cityHoverText } from '../Scene/CityMesh.js';
import { cityPosition } from '../Scene/Adjacency.js';
import { buildLayout, clearChildren, el, hide, show, type HudLayout } from './Layout.js';
import { fieldLabel, factionColor, injectGlobalStyle, UI } from './Theme.js';
import type {
  Army,
  GameMeta,
  Officer,
  SettledOrder,
  StateDelta,
  TickResponse,
} from '../Core/Types.js';

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
  onTick(): void;
  onNewGame(): void;
}

export class Hud {
  private layout: HudLayout;
  private openPanel: { kind: 'city' | 'army' | 'faction' | 'officers'; id: number } | null = null;
  private onInteractRequest: (() => void) | null = null;
  private lastMouse = { x: 0, y: 0 };
  private ticking = false;

  constructor(private readonly deps: HudDeps) {
    injectGlobalStyle();
    this.layout = buildLayout('hud-root');

    // 顶栏按钮
    this.layout.btnGod.addEventListener('click', () => deps.onSetViewMode('god'));
    this.layout.btnField.addEventListener('click', () => deps.onSetViewMode('field'));
    this.layout.btnTick.addEventListener('click', () => {
      if (!this.ticking) {
        this.ticking = true;
        deps.onTick();
        window.setTimeout(() => (this.ticking = false), 800);
      }
    });
    this.layout.btnNewGame.addEventListener('click', () => deps.onNewGame());
    this.layout.btnInteract.addEventListener('click', () => this.onInteractRequest?.());

    // 悬停卡跟随
    window.addEventListener('pointermove', (ev) => {
      this.lastMouse.x = ev.clientX;
      this.lastMouse.y = ev.clientY;
      if (!this.layout.hoverCard.classList.contains('sg-hidden')) {
        this.layout.hoverCard.style.left = `${ev.clientX}px`;
        this.layout.hoverCard.style.top = `${ev.clientY}px`;
      }
    });

    this.bindJoystick();
    deps.store.onChange(() => this.renderVolatile());
  }

  setInteractHandler(fn: () => void): void {
    this.onInteractRequest = fn;
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

  setViewModeUI(mode: ViewMode): void {
    this.layout.btnGod.classList.toggle('sg-active', mode === 'god');
    this.layout.btnField.classList.toggle('sg-active', mode === 'field');
    if (mode === 'field') {
      show(this.layout.joy);
      show(this.layout.fieldCorner);
    } else {
      hide(this.layout.joy);
      hide(this.layout.fieldCorner);
      hide(this.layout.bubble);
      this.layout.btnInteract.disabled = true;
    }
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

  showHoverCard(target: { kind: 'city' | 'army'; id: number } | null): void {
    const card = this.layout.hoverCard;
    if (!target || !this.deps.store.ready) {
      hide(card);
      return;
    }
    const state = this.deps.store.state;
    let html = '';
    if (target.kind === 'city') {
      const city = this.deps.store.city(target.id);
      if (!city) {
        hide(card);
        return;
      }
      html = cityHoverText(city, state);
    } else {
      const army = this.deps.store.army(target.id);
      if (!army) {
        hide(card);
        return;
      }
      const owner = state.factions.find((f) => f.id === army.ownerFactionId);
      const commander = army.officerId >= 0 ? this.deps.store.officer(army.officerId)?.name ?? `#${army.officerId}` : army.ownerFactionId === (this.deps.store.playerFaction?.id ?? -1) && this.deps.store.state.player.isPersonallyCommanding && this.deps.store.state.player.commandingArmyId === army.id ? '君主亲征' : '无';
      html = [
        `<b>军团 #${army.id}</b>（${owner?.name ?? '无主'}）`,
        `兵力 ${army.soldiers} · 士气 ${army.morale}/${army.moraleMax}`,
        `统帅 ${commander} · 状态 ${STANCE_LABEL[army.stance] ?? army.stance}`,
        `所在城 ${this.deps.store.city(army.positionCityId)?.name ?? army.positionCityId}`,
      ].join('<br/>');
    }
    card.innerHTML = html;
    card.style.left = `${this.lastMouse.x}px`;
    card.style.top = `${this.lastMouse.y}px`;
    show(card);
  }

  // ==========================================================================
  //  Toast
  // ==========================================================================
  toast(message: string, kind: 'info' | 'ok' | 'error' = 'info'): void {
    const t = el('div', `sg-toast${kind === 'ok' ? ' sg-ok' : kind === 'error' ? ' sg-error' : ''}`, message);
    this.layout.toasts.appendChild(t);
    while (this.layout.toasts.children.length > 4) {
      this.layout.toasts.removeChild(this.layout.toasts.firstChild!);
    }
    window.setTimeout(() => t.remove(), 4200);
  }

  // ==========================================================================
  //  侧栏面板
  // ==========================================================================
  closeSide(): void {
    this.openPanel = null;
    clearChildren(this.layout.side);
    hide(this.layout.side);
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
    }
  }

  private cardHeader(title: string, onClose: () => void): HTMLDivElement {
    const card = el('div', 'sg-card sg-panel');
    const h = el('h3');
    const t = el('span', undefined, title);
    const close = el('span', 'sg-close', '✕');
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
      const mkAmountRow = (label: string, action: 'Recruit' | 'Develop' | 'Fortify', def: number): void => {
        const row = el('div', 'sg-row');
        row.appendChild(el('span', 'sg-grow', label));
        const input = el('input', 'sg-input') as HTMLInputElement;
        input.type = 'number';
        input.value = String(def);
        input.min = '0';
        row.appendChild(input);
        const btn = el('button', 'sg-btn', '下令');
        btn.addEventListener('click', () => {
          const amount = Math.max(0, Math.floor(Number(input.value) || 0));
          void this.deps.sender.send({ action, targetId: city.id, amount });
        });
        row.appendChild(btn);
        actions.appendChild(row);
      };
      mkAmountRow('征兵（人数）', 'Recruit', 100);
      mkAmountRow('屯田（投银）', 'Develop', 100);
      mkAmountRow('修城（投银）', 'Fortify', 100);

      // 驻军 + 休整
      for (const army of state.armies.filter((a) => a.positionCityId === city.id)) {
        const row = el('div', 'sg-row');
        row.appendChild(el('span', 'sg-grow', `驻军 #${army.id} · ${army.soldiers} 人 · 士气 ${army.morale}/${army.moraleMax}`));
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
      appoint.addEventListener('click', () => this.openOfficersPanel());
      actions.appendChild(appoint);

      card.appendChild(actions);
      this.hintLine(card, '下令成功 ≠ 执行成功：指令入队后于下月结算，成败与原因以结算结果的 code / message 为准。');
    } else {
      this.hintLine(card, '非本势力城池：仅可查看。攻取需在亲征视角通过军议进军。');
    }
    return card;
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
      march.addEventListener('click', () => this.renderMarchFlow(army, card));
      actions.appendChild(march);

      card.appendChild(actions);
      this.hintLine(card, '军议：选目标城（须与本军所在城相邻）→ 地面升起行军箭头 → 点「下令进军」发出 Dispatch。');
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
    (flow as HTMLElement).style.cssText = `margin-top:8px;padding:8px;border:1px solid ${UI.borderGold};border-radius:8px;`;

    const state = this.deps.store.state;
    const here = this.deps.store.city(army.positionCityId);
    flow.appendChild(el('div', 'sg-section-title', `军议：自 ${here?.name ?? army.positionCityId} 出兵`));

    const targets = here?.adjacentCityIds ?? [];
    if (targets.length === 0) {
      flow.appendChild(el('div', 'sg-muted', '所在城没有相邻城池，无法进军。'));
      card.appendChild(flow);
      return;
    }

    // 目标城选择
    const select = el('select', 'sg-select') as HTMLSelectElement;
    for (const id of targets) {
      const c = this.deps.store.city(id);
      const opt = el('option') as HTMLOptionElement;
      opt.value = String(id);
      opt.textContent = `${c?.name ?? id}${c ? `（${state.factions.find((f) => f.id === c.ownerFactionId)?.name ?? '无主'}）` : ''}`;
      select.appendChild(opt);
    }
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
      ['身份', f.isPlayer ? '玩家（汉）' : 'AI'],
      ['都城', this.deps.store.city(f.capitalCityId)?.name ?? String(f.capitalCityId)],
      ['粮 / 银', `${f.resources.grain} / ${f.resources.silver}`],
      ['兵源池', `${f.resources.soldier}`],
      ['民心', `${f.resources.morale}`],
      ['稳定', `${f.resources.stability}`],
    ]);
    const cities = state.cities.filter((c) => c.ownerFactionId === f.id);
    const cityRow = el('div', 'sg-row');
    cityRow.appendChild(el('span', 'sg-grow', `城池 ${cities.length} 座`));
    for (const c of cities) {
      const b = el('button', 'sg-mini', c.name) as HTMLButtonElement;
      b.style.borderColor = factionColor(f.id);
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

  // ---- 人才面板（R6） ----
  private buildOfficersCard(): HTMLDivElement {
    const state = this.deps.store.state;
    const pf = this.deps.store.playerFaction;
    const card = this.cardHeader('人才 · 任命', () => this.closeSide());

    const reward = el('button', 'sg-btn sg-disabled', '赏赐（待契约扩展）');
    reward.disabled = true;
    reward.title = 'ActionType.Reward=101 为 P1 保留位，Resolver 未实现（R6 本轮不做）';
    const headRow = el('div', 'sg-row');
    headRow.appendChild(el('span', 'sg-grow', '共 ' + state.officers.length + ' 人'));
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
    (wrap as HTMLElement).style.cssText = 'display:flex;gap:4px;align-items:center;margin-top:4px;';
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
      (box as HTMLElement).style.marginTop = '6px';
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
    cont.addEventListener('click', () => overlay.remove());
    foot.appendChild(cont);
    modal.appendChild(foot);

    overlay.appendChild(modal);
    this.layout.overlayLayer.appendChild(overlay);
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
            ? this.deps.store.faction(d.targetId)?.name ?? `势力 ${d.targetId}`
            : d.targetType === 'Officer'
              ? this.deps.store.officer(d.targetId)?.name ?? `人才 ${d.targetId}`
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
  //  虚拟摇杆（触屏 / 鼠标均可拖）
  // ==========================================================================
  private bindJoystick(): void {
    const joy = this.layout.joy;
    const knob = this.layout.joyKnob;
    let active = false;
    let cx = 0;
    let cy = 0;
    const R = 44;

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
