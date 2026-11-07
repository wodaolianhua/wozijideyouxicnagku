// ============================================================================
//  Ui/Theme.ts —— 势力配色与全局 UI 样式。
//  势力配色：汉青 / 魏蓝 / 蜀红（按势力 Id 索引，色板与具体势力名解耦）。
//
//  桌面化（R2）：以 ≥1280×720 桌面为基准 —— 面板为侧栏 / 浮层，控件尺寸适合鼠标点击
//  （按钮最小 30px 高、正文 13px 起），不出现「为手机拉伸」的布局；触屏摇杆只在触摸设备出现。
// ============================================================================

/** 势力色板：index = 势力 Id。0=汉(青) 1=魏(蓝) 2=蜀(红)，后续 Id 循环备用色。 */
export const FACTION_PALETTE: readonly string[] = [
  '#2fa38a', // 汉 · 青
  '#4f83d1', // 魏 · 蓝
  '#cf5a5a', // 蜀 · 红
  '#b8862f',
  '#8a6fc2',
  '#5aa0a8',
];

export function factionColor(factionId: number): string {
  const idx = ((factionId % FACTION_PALETTE.length) + FACTION_PALETTE.length) % FACTION_PALETTE.length;
  return FACTION_PALETTE[idx] ?? '#888888';
}

export function factionColorDim(factionId: number): string {
  return factionColor(factionId) + '55';
}

/** UI 色板 */
export const UI = {
  bg: 'rgba(11, 14, 20, 0.88)',
  panel: '#141a26',
  panelSoft: 'rgba(20, 26, 38, 0.92)',
  border: '#2c3a52',
  borderGold: '#8a713d',
  text: '#e8e4d8',
  sub: '#9aa7bd',
  accent: '#c9a44c',
  ok: '#5fbf7a',
  danger: '#d05a5a',
  shadow: '0 6px 24px rgba(0, 0, 0, 0.55)',
  radius: '10px',
} as const;

const GLOBAL_CSS = `
.sg-panel {
  background: ${UI.panelSoft};
  border: 1px solid ${UI.border};
  border-radius: ${UI.radius};
  box-shadow: ${UI.shadow};
  color: ${UI.text};
  backdrop-filter: blur(6px);
}
.sg-hidden { display: none !important; }

/* ---------- 顶栏（桌面单行） ---------- */
.sg-topbar {
  position: absolute; top: 0; left: 0; right: 0;
  display: flex; align-items: center; gap: 10px;
  padding: 10px 16px;
  background: linear-gradient(180deg, rgba(11,14,20,0.92), rgba(11,14,20,0.55) 78%, transparent);
  pointer-events: none;
}
.sg-topbar > * { pointer-events: auto; }
.sg-date {
  font-size: 17px; font-weight: 700; color: ${UI.accent};
  padding: 8px 16px; border: 1px solid ${UI.borderGold}; border-radius: 8px;
  background: rgba(20, 26, 38, 0.85); white-space: nowrap;
}
.sg-factions { display: flex; gap: 8px; flex-wrap: nowrap; min-width: 0; overflow: hidden; }
.sg-faction-chip {
  display: flex; align-items: center; gap: 6px;
  padding: 6px 12px; border-radius: 8px; cursor: pointer;
  border: 1px solid ${UI.border}; background: rgba(20, 26, 38, 0.85);
  color: ${UI.text}; font-size: 13px; user-select: none; white-space: nowrap;
}
.sg-faction-chip:hover { border-color: ${UI.accent}; color: ${UI.accent}; }
.sg-faction-dot { width: 11px; height: 11px; border-radius: 50%; flex: 0 0 auto; }
.sg-faction-chip .sg-player-tag { color: ${UI.accent}; font-weight: 700; }

.sg-spacer { flex: 1; min-width: 8px; }
.sg-btn {
  padding: 9px 16px; border-radius: 8px; border: 1px solid ${UI.border};
  background: #1c2536; color: ${UI.text}; font-size: 14px; cursor: pointer; user-select: none;
  min-height: 34px; white-space: nowrap; line-height: 1.2;
}
.sg-btn:hover { border-color: ${UI.accent}; color: ${UI.accent}; }
.sg-btn.sg-active { background: ${UI.accent}; color: #1a1508; border-color: ${UI.accent}; font-weight: 700; }
.sg-btn.sg-primary { background: ${UI.accent}; color: #1a1508; border-color: ${UI.accent}; font-weight: 700; }
.sg-btn.sg-primary:hover { filter: brightness(1.1); }
.sg-btn.sg-ghost { background: rgba(20, 26, 38, 0.6); color: ${UI.sub}; }
.sg-btn.sg-ghost:hover { color: ${UI.accent}; }
.sg-btn:disabled { opacity: 0.4; cursor: not-allowed; }
.sg-btn.sg-disabled { opacity: 0.4; cursor: not-allowed; }

/* ---------- 左侧信息面板（桌面侧栏） ---------- */
.sg-side {
  position: absolute; left: 16px; top: 72px; bottom: 96px; width: 380px;
  display: flex; flex-direction: column; gap: 12px;
  overflow: hidden auto; scrollbar-width: thin;
}
.sg-side::-webkit-scrollbar { width: 8px; }
.sg-side::-webkit-scrollbar-thumb { background: ${UI.border}; border-radius: 4px; }
.sg-card { padding: 14px 16px; }
.sg-card h3 {
  margin: 0 0 10px; font-size: 16px; color: ${UI.accent};
  border-bottom: 1px solid ${UI.borderGold}; padding-bottom: 8px;
  display: flex; justify-content: space-between; align-items: center;
}
.sg-card h3 .sg-close { cursor: pointer; color: ${UI.sub}; font-size: 18px; padding: 0 6px; }
.sg-card h3 .sg-close:hover { color: ${UI.danger}; }
.sg-kv { display: grid; grid-template-columns: auto 1fr; gap: 6px 12px; font-size: 13px; }
.sg-kv .k { color: ${UI.sub}; }
.sg-kv .v { text-align: right; }
.sg-row { display: flex; align-items: center; gap: 10px; font-size: 13px; padding: 7px 0; }
.sg-row + .sg-row { border-top: 1px dashed rgba(44, 58, 82, 0.6); }
.sg-grow { flex: 1; }
.sg-muted { color: ${UI.sub}; font-size: 12px; }
.sg-hint {
  margin-top: 10px; padding: 8px 10px; font-size: 12px; color: ${UI.sub};
  border: 1px dashed ${UI.border}; border-radius: 6px; line-height: 1.7;
}
.sg-input {
  width: 116px; padding: 7px 10px; border-radius: 6px;
  border: 1px solid ${UI.border}; background: #0e131d; color: ${UI.text}; font-size: 13px;
  min-height: 30px;
}
.sg-select {
  padding: 7px 10px; border-radius: 6px;
  border: 1px solid ${UI.border}; background: #0e131d; color: ${UI.text}; font-size: 13px;
  min-height: 30px; max-width: 230px;
}
.sg-actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }
.sg-actions .sg-btn { min-height: 32px; }

/* ---------- 人才表 ---------- */
.sg-table { width: 100%; border-collapse: collapse; font-size: 12px; }
.sg-table th {
  color: ${UI.sub}; font-weight: 400; text-align: left;
  padding: 6px 6px; border-bottom: 1px solid ${UI.border}; white-space: nowrap;
}
.sg-table td { padding: 6px 6px; border-bottom: 1px dashed rgba(44, 58, 82, 0.5); white-space: nowrap; }
.sg-table tr.sg-row-dead td { color: #666; text-decoration: line-through; }
.sg-table .sg-mini {
  padding: 4px 12px; font-size: 12px; border-radius: 5px;
  border: 1px solid ${UI.border}; background: #1c2536; color: ${UI.text}; cursor: pointer;
  min-height: 28px;
}
.sg-table .sg-mini:hover { border-color: ${UI.accent}; color: ${UI.accent}; }
.sg-table .sg-mini:disabled { opacity: 0.35; cursor: not-allowed; }
.sg-mini {
  padding: 5px 12px; font-size: 12px; border-radius: 6px;
  border: 1px solid ${UI.border}; background: #1c2536; color: ${UI.text}; cursor: pointer;
  min-height: 28px;
}
.sg-mini:hover { border-color: ${UI.accent}; color: ${UI.accent}; }

/* ---------- 右下角操作区（亲征） ---------- */
.sg-field-corner {
  position: absolute; right: 20px; bottom: 20px;
  display: flex; flex-direction: column; align-items: flex-end; gap: 10px;
}
.sg-interact {
  min-width: 132px; height: 44px; border-radius: 22px; padding: 0 20px;
  border: 2px solid ${UI.borderGold}; background: rgba(20, 26, 38, 0.92);
  color: ${UI.accent}; font-size: 15px; font-weight: 700; cursor: pointer;
}
.sg-interact:hover:not(:disabled) { background: rgba(201, 164, 76, 0.18); }
.sg-interact:disabled { opacity: 0.35; cursor: not-allowed; }
.sg-field-tip {
  font-size: 12px; color: ${UI.sub}; background: rgba(11,14,20,0.85);
  padding: 7px 12px; border-radius: 8px; border: 1px solid ${UI.border};
}

/* ---------- 虚拟摇杆（仅触摸设备出现） ---------- */
.sg-joy {
  position: absolute; left: 32px; bottom: 32px;
  width: 128px; height: 128px; border-radius: 50%;
  border: 2px solid ${UI.border}; background: rgba(20, 26, 38, 0.55);
  touch-action: none;
}
.sg-joy-knob {
  position: absolute; left: 50%; top: 50%; width: 52px; height: 52px;
  border-radius: 50%; background: rgba(201, 164, 76, 0.55);
  border: 2px solid ${UI.accent}; transform: translate(-50%, -50%);
  pointer-events: none;
}

/* ---------- 交互气泡 ---------- */
.sg-bubble {
  position: absolute; transform: translate(-50%, -100%);
  padding: 7px 12px; border-radius: 8px; font-size: 13px;
  background: rgba(20, 26, 38, 0.95); border: 1px solid ${UI.borderGold};
  color: ${UI.accent}; white-space: nowrap; pointer-events: none;
}
.sg-bubble::after {
  content: ''; position: absolute; left: 50%; bottom: -5px; transform: translateX(-50%);
  border-left: 5px solid transparent; border-right: 5px solid transparent;
  border-top: 5px solid ${UI.borderGold};
}

/* ---------- 结算 / 全屏覆盖层（桌面浮层，不做移动端全屏遮罩观感） ---------- */
.sg-overlay {
  position: absolute; inset: 0; display: flex; align-items: center; justify-content: center;
  background: rgba(5, 8, 12, 0.66); z-index: 60;
}
.sg-modal {
  width: min(920px, 88vw); max-height: 84vh; overflow: hidden auto;
  padding: 20px 26px; scrollbar-width: thin; font-size: 13px;
}
.sg-modal::-webkit-scrollbar { width: 8px; }
.sg-modal::-webkit-scrollbar-thumb { background: ${UI.border}; border-radius: 4px; }
.sg-modal h2 {
  margin: 0 0 12px; font-size: 20px; color: ${UI.accent};
  border-bottom: 2px solid ${UI.borderGold}; padding-bottom: 10px;
}
.sg-section-title { margin: 16px 0 8px; font-size: 14px; color: ${UI.accent}; }
.sg-result-item {
  border: 1px solid ${UI.border}; border-radius: 8px;
  padding: 10px 12px; margin-bottom: 8px; font-size: 13px; line-height: 1.8;
}
.sg-result-item.sg-bad { border-color: ${UI.danger}; }
.sg-result-item.sg-good { border-color: ${UI.ok}; }
.sg-code-tag { font-weight: 700; }
.sg-delta-line { color: ${UI.sub}; }
.sg-event-item { font-size: 13px; padding: 4px 0; display: flex; gap: 10px; }
.sg-event-cat {
  flex: 0 0 auto; font-size: 11px; padding: 2px 8px; border-radius: 5px;
  border: 1px solid ${UI.border}; color: ${UI.sub}; margin-top: 2px;
}
.sg-modal-foot { display: flex; justify-content: flex-end; gap: 10px; margin-top: 16px; }
.sg-modal-foot .sg-btn { min-height: 36px; padding: 9px 20px; }
.sg-modal details summary { cursor: pointer; color: ${UI.sub}; font-size: 13px; }

/* ---------- 快捷键帮助（桌面浮层） ---------- */
.sg-help-grid {
  display: grid; grid-template-columns: max-content 1fr; gap: 10px 18px;
  font-size: 13px; align-items: center;
}
.sg-help-group {
  grid-column: 1 / -1; margin-top: 6px; color: ${UI.accent};
  font-size: 13px; border-bottom: 1px solid ${UI.borderGold}; padding-bottom: 4px;
}
.sg-help-desc { color: ${UI.text}; }
.sg-help-desc .sg-help-note { color: ${UI.sub}; font-size: 12px; }

/* ---------- 提示（toast） ---------- */
.sg-toasts {
  position: absolute; top: 72px; left: 50%; transform: translateX(-50%);
  display: flex; flex-direction: column; gap: 8px; align-items: center; z-index: 80;
  pointer-events: none;
}
.sg-toast {
  padding: 10px 18px; border-radius: 8px; font-size: 13px;
  background: rgba(20, 26, 38, 0.95); border: 1px solid ${UI.border}; color: ${UI.text};
  box-shadow: ${UI.shadow}; max-width: 70vw;
}
.sg-toast.sg-ok { border-color: ${UI.ok}; }
.sg-toast.sg-error { border-color: ${UI.danger}; color: #f0c0c0; }

/* ---------- 战斗演出 ---------- */
.sg-cinema-bar {
  position: absolute; left: 0; right: 0; height: 11vh;
  background: #000; z-index: 55; transition: transform 0.5s ease;
}
.sg-cinema-bar.sg-top { top: 0; transform-origin: top; }
.sg-cinema-bar.sg-bottom { bottom: 0; transform-origin: bottom; }
.sg-cinema-text {
  position: absolute; bottom: 12vh; left: 0; right: 0; text-align: center;
  font-size: 22px; color: ${UI.text}; text-shadow: 0 2px 8px #000;
  letter-spacing: 2px; z-index: 56; pointer-events: none;
}
.sg-cinema-skipping { position:absolute; right: 24px; bottom: 13vh; font-size: 12px; color: ${UI.sub}; z-index:56; }

/* ---------- 悬停信息卡 ---------- */
.sg-hover-card {
  position: absolute; pointer-events: none; z-index: 40;
  padding: 8px 12px; border-radius: 8px; font-size: 13px; line-height: 1.7;
  background: rgba(20, 26, 38, 0.95); border: 1px solid ${UI.border}; color: ${UI.text};
  transform: translate(14px, -50%); max-width: 340px;
}

/* ---------- 常驻快捷键角标（桌面常显） ---------- */
.sg-hotkey-badge {
  position: absolute; left: 16px; bottom: 16px;
  display: flex; align-items: center; gap: 14px; flex-wrap: wrap;
  padding: 8px 14px; border-radius: 8px;
  background: rgba(11, 14, 20, 0.8); border: 1px solid ${UI.border};
  font-size: 12px; color: ${UI.sub}; z-index: 30; pointer-events: none;
}
.sg-hotkey-item { display: inline-flex; align-items: center; gap: 6px; }
.sg-kbd {
  display: inline-block; padding: 2px 7px; border-radius: 5px;
  border: 1px solid ${UI.borderGold}; background: rgba(20, 26, 38, 0.95);
  color: ${UI.accent}; font-family: 'Microsoft YaHei', system-ui, sans-serif;
  font-size: 11px; line-height: 1.6; white-space: nowrap;
}
.sg-hotkey-desc { white-space: nowrap; }

/* ---------- 指针穿透修正（覆盖 #hud-root > * 的 pointer-events: auto） ---------- */
#hud-root > .sg-hover-card,
#hud-root > .sg-toasts,
#hud-root > .sg-bubble,
#hud-root > .sg-cinema-text,
#hud-root > .sg-cinema-skipping,
#hud-root > .sg-hotkey-badge { pointer-events: none; }
`;

/* 触屏设备：隐藏桌面向的快捷键角标（摇杆占位），并让 HUD 侧栏更贴近拇指 */
const TOUCH_CSS = `
body.sg-touch .sg-hotkey-badge { display: none; }
body.sg-touch .sg-side { bottom: 172px; }
`;

let injected = false;

export function injectGlobalStyle(): void {
  if (injected) {
    return;
  }
  const style = document.createElement('style');
  style.id = 'sg-global-style';
  style.textContent = GLOBAL_CSS;
  document.head.appendChild(style);
  injected = true;
}

/** 触摸设备：追加移动端向样式（摇杆可见、角标隐藏） */
export function injectTouchStyle(): void {
  if (document.getElementById('sg-touch-style')) {
    return;
  }
  const style = document.createElement('style');
  style.id = 'sg-touch-style';
  style.textContent = TOUCH_CSS;
  document.head.appendChild(style);
  document.body.classList.add('sg-touch');
}

/** 常用契约字段的展示名（仅展示层标签；未收录字段原样显示契约英文名） */
const FIELD_LABELS: Readonly<Record<string, string>> = {
  Grain: '粮',
  Silver: '银',
  Soldier: '兵源',
  Morale: '民心',
  Stability: '稳定',
  Population: '人口',
  Defense: '城防',
  MaxDefense: '城防上限',
  FortifyLevel: '城防等级',
  FarmLevel: '屯田等级',
  OwnerFactionId: '归属',
  Soldiers: '兵力',
  OfficerId: '统帅',
  Post: '岗位',
  PostCityId: '任职城',
  Loyalty: '忠诚',
  Command: '统率',
  Governance: '政务',
  // 势力资源（结算面板 deltas 的 field 形如 Resources.Silver）
  'Resources.Grain': '粮',
  'Resources.Silver': '银',
  'Resources.Soldier': '兵源',
  'Resources.Morale': '民心',
  'Resources.Stability': '稳定',
};

export function fieldLabel(field: string): string {
  return FIELD_LABELS[field] ?? field;
}
