// ============================================================================
//  Ui/Layout.ts —— HUD DOM 骨架。只负责结构与元素获取，不含业务逻辑。
//  桌面化（R2）：侧栏式面板 + 顶栏工具区 + 常驻快捷键角标（不藏快捷键）。
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
  btnHelp: HTMLButtonElement;
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
  hotkeyBadge: HTMLDivElement;
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

/** 快捷键角标条目：[键位, 说明] */
const HOTKEY_BADGE: ReadonlyArray<readonly [string, string]> = [
  ['空格 / 回车', '推进下月'],
  ['1 / 2', '上帝 / 亲征'],
  ['3', '人才'],
  ['Esc', '关闭'],
  ['WASD', '行走'],
  ['H', '帮助'],
];

export function buildLayout(rootId: string): HudLayout {
  const root = document.getElementById(rootId) as HTMLDivElement;

  // ---- 顶栏（桌面基准 ≥1280×720，单行不换行） ----
  const topbar = el('div', 'sg-topbar');
  const date = el('div', 'sg-date', '— 年 — 月');
  const factionChips = el('div', 'sg-factions');
  const spacer = el('div', 'sg-spacer');
  const btnGod = el('button', 'sg-btn sg-active', '上帝视角');
  btnGod.title = '宏观战略地图：全部势力信息 + 宏观指令（快捷键 1）';
  const btnField = el('button', 'sg-btn', '亲征视角');
  btnField.title = '第一/三人称操控君主行走，与城池/部队交互，军议进军（快捷键 2）';
  const btnTick = el('button', 'sg-btn sg-primary', '推进下月 ▶');
  btnTick.title = '结算本月队列中的全部指令，进入下一个月（快捷键 空格 / 回车）';
  const btnNewGame = el('button', 'sg-btn', '重新开局');
  btnNewGame.title = '以种子 12345 重开一局';
  const btnHelp = el('button', 'sg-btn sg-ghost', '快捷键 H');
  btnHelp.title = '查看全部鼠标操作与键盘快捷键';
  topbar.append(date, factionChips, spacer, btnGod, btnField, btnTick, btnNewGame, btnHelp);

  // ---- 左侧信息面板（桌面：侧栏，非移动端全屏遮罩） ----
  const side = el('div', 'sg-side sg-hidden');

  // ---- 悬停卡 / 交互气泡 ----
  const hoverCard = el('div', 'sg-hover-card sg-hidden');
  const bubble = el('div', 'sg-bubble sg-hidden');

  // ---- 虚拟摇杆（仅触摸设备出现，桌面端不显示） ----
  const joy = el('div', 'sg-joy sg-hidden');
  const joyKnob = el('div', 'sg-joy-knob');
  joy.appendChild(joyKnob);

  // ---- 右下操作区（亲征） ----
  const fieldCorner = el('div', 'sg-field-corner sg-hidden');
  const btnInteract = el('button', 'sg-interact', '交互 (E)');
  btnInteract.disabled = true;
  btnInteract.title = '与身旁的城池 / 军团交互（快捷键 E）';
  const fieldTip = el('div', 'sg-field-tip', '左键拖拽转视角 · 滚轮缩放 · WASD 行走 · E 交互');
  fieldCorner.append(btnInteract, fieldTip);

  // ---- 战斗演出 ----
  const cinemaTop = el('div', 'sg-cinema-bar sg-top sg-hidden');
  const cinemaBottom = el('div', 'sg-cinema-bar sg-bottom sg-hidden');
  const cinemaText = el('div', 'sg-cinema-text sg-hidden');
  const cinemaSkip = el('div', 'sg-cinema-skipping sg-hidden', '战斗演出中……');

  const toasts = el('div', 'sg-toasts');
  const overlayLayer = el('div');

  // ---- 常驻快捷键角标（桌面可见，不藏快捷键） ----
  const hotkeyBadge = el('div', 'sg-hotkey-badge');
  for (const [key, desc] of HOTKEY_BADGE) {
    const item = el('span', 'sg-hotkey-item');
    const kbd = el('kbd', 'sg-kbd', key);
    item.append(kbd, el('span', 'sg-hotkey-desc', desc));
    hotkeyBadge.appendChild(item);
  }

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
    hotkeyBadge,
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
    btnHelp,
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
    hotkeyBadge,
  };
}
