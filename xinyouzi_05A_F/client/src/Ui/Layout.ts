// ============================================================================
//  Ui/Layout.ts —— HUD DOM 骨架。只负责结构与元素获取，不含业务逻辑。
// ============================================================================

export interface HudLayout {
  root: HTMLDivElement;
  topbar: HTMLDivElement;
  date: HTMLDivElement;
  factionChips: HTMLDivElement;
  spacer: HTMLDivElement;
  btnGod: HTMLButtonElement;
  btnField: HTMLButtonElement;
  btnTick: HTMLButtonElement;
  btnNewGame: HTMLButtonElement;
  side: HTMLDivElement;
  toasts: HTMLDivElement;
  hoverCard: HTMLDivElement;
  bubble: HTMLDivElement;
  joy: HTMLDivElement;
  joyKnob: HTMLDivElement;
  fieldCorner: HTMLDivElement;
  btnInteract: HTMLButtonElement;
  fieldTip: HTMLDivElement;
  cinemaTop: HTMLDivElement;
  cinemaBottom: HTMLDivElement;
  cinemaText: HTMLDivElement;
  cinemaSkip: HTMLDivElement;
  overlayLayer: HTMLDivElement;
}

export function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
  text?: string,
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) {
    node.className = className;
  }
  if (text !== undefined) {
    node.textContent = text;
  }
  return node;
}

export function show(node: HTMLElement): void {
  node.classList.remove('sg-hidden');
}

export function hide(node: HTMLElement): void {
  node.classList.add('sg-hidden');
}

export function clearChildren(node: HTMLElement): void {
  while (node.firstChild) {
    node.removeChild(node.firstChild);
  }
}

export function buildLayout(rootId: string): HudLayout {
  const root = document.getElementById(rootId) as HTMLDivElement;

  const topbar = el('div', 'sg-topbar');
  const date = el('div', 'sg-date', '— 年 — 月');
  const factionChips = el('div', 'sg-factions');
  const spacer = el('div', 'sg-spacer');
  const btnGod = el('button', 'sg-btn sg-active', '上帝视角');
  btnGod.title = '宏观战略地图：全部势力信息 + 宏观指令';
  const btnField = el('button', 'sg-btn', '亲征视角');
  btnField.title = '第一/三人称操控君主行走，与城池/部队交互，军议进军';
  const btnTick = el('button', 'sg-btn sg-primary', '推进下月 ▶');
  btnTick.title = '结算本月队列中的全部指令，进入下一个月';
  const btnNewGame = el('button', 'sg-btn', '重新开局');
  btnNewGame.title = '以种子 12345 重开一局';
  topbar.append(date, factionChips, spacer, btnGod, btnField, btnTick, btnNewGame);

  const side = el('div', 'sg-side sg-hidden');

  const hoverCard = el('div', 'sg-hover-card sg-hidden');
  const bubble = el('div', 'sg-bubble sg-hidden');

  const joy = el('div', 'sg-joy sg-hidden');
  const joyKnob = el('div', 'sg-joy-knob');
  joy.appendChild(joyKnob);

  const fieldCorner = el('div', 'sg-field-corner sg-hidden');
  const btnInteract = el('button', 'sg-interact', '交互');
  btnInteract.disabled = true;
  const fieldTip = el('div', 'sg-field-tip', 'WASD/摇杆 行走 · 拖拽转视角 · 滚轮缩放');
  fieldCorner.append(btnInteract, fieldTip);

  const cinemaTop = el('div', 'sg-cinema-bar sg-top sg-hidden');
  const cinemaBottom = el('div', 'sg-cinema-bar sg-bottom sg-hidden');
  const cinemaText = el('div', 'sg-cinema-text sg-hidden');
  const cinemaSkip = el('div', 'sg-cinema-skipping sg-hidden', '战斗演出中……');

  const toasts = el('div', 'sg-toasts');
  const overlayLayer = el('div');

  root.append(
    topbar,
    side,
    hoverCard,
    bubble,
    joy,
    fieldCorner,
    cinemaTop,
    cinemaBottom,
    cinemaText,
    cinemaSkip,
    toasts,
    overlayLayer,
  );

  return {
    root,
    topbar,
    date,
    factionChips,
    spacer,
    btnGod,
    btnField,
    btnTick,
    btnNewGame,
    side,
    toasts,
    hoverCard,
    bubble,
    joy,
    joyKnob,
    fieldCorner,
    btnInteract,
    fieldTip,
    cinemaTop,
    cinemaBottom,
    cinemaText,
    cinemaSkip,
    overlayLayer,
  };
}
