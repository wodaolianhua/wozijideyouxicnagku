// ============================================================================
//  Ui/Theme.ts —— 势力配色与全局 UI 样式。
//  势力配色：汉青 / 魏蓝 / 蜀红（按势力 Id 索引，色板与具体势力名解耦）。
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

/* ---------- 顶栏 ---------- */
.sg-topbar {
  position: absolute; top: 0; left: 0; right: 0;
  display: flex; align-items: center; gap: 10px;
  padding: 8px 12px;
  background: linear-gradient(180deg, rgba(11,14,20,0.92), rgba(11,14,20,0.55) 78%, transparent);
  pointer-events: none;
}
.sg-topbar > * { pointer-events: auto; }
.sg-date {
  font-size: 16px; font-weight: 700; color: ${UI.accent};
  padding: 6px 14px; border: 1px solid ${UI.borderGold}; border-radius: 8px;
  background: rgba(20, 26, 38, 0.85); white-space: nowrap;
}
.sg-factions { display: flex; gap: 6px; flex-wrap: wrap; }
.sg-faction-chip {
  display: flex; align-items: center; gap: 6px;
  padding: 4px 10px; border-radius: 8px; cursor: pointer;
  border: 1px solid ${UI.border}; background: rgba(20, 26, 38, 0.85);
  color: ${UI.text}; font-size: 12px; user-select: none;
}
.sg-faction-chip:hover { border-color: ${UI.accent}; }
.sg-faction-dot { width: 10px; height: 10px; border-radius: 50%; }
.sg-faction-chip .sg-player-tag { color: ${UI.accent}; font-weight: 700; }

.sg-spacer { flex: 1; }
.sg-btn {
  padding: 7px 14px; border-radius: 8px; border: 1px solid ${UI.border};
  background: #1c2536; color: ${UI.text}; font-size: 13px; cursor: pointer; user-select: none;
}
.sg-btn:hover { border-color: ${UI.accent}; color: ${UI.accent}; }
.sg-btn.sg-active { background: ${UI.accent}; color: #1a1508; border-color: ${UI.accent}; font-weight: 700; }
.sg-btn.sg-primary { background: ${UI.accent}; color: #1a1508; border-color: ${UI.accent}; font-weight: 700; }
.sg-btn.sg-primary:hover { filter: brightness(1.1); }
.sg-btn:disabled { opacity: 0.4; cursor: not-allowed; }
.sg-btn.sg-disabled { opacity: 0.4; cursor: not-allowed; }

/* ---------- 左侧信息面板 ---------- */
.sg-side {
  position: absolute; left: 12px; top: 64px; bottom: 76px; width: 320px;
  display: flex; flex-direction: column; gap: 10px;
  overflow: hidden auto; scrollbar-width: thin;
}
.sg-side::-webkit-scrollbar { width: 6px; }
.sg-side::-webkit-scrollbar-thumb { background: ${UI.border}; border-radius: 3px; }
.sg-card { padding: 10px 12px; }
.sg-card h3 {
  margin: 0 0 8px; font-size: 14px; color: ${UI.accent};
  border-bottom: 1px solid ${UI.borderGold}; padding-bottom: 6px;
  display: flex; justify-content: space-between; align-items: center;
}
.sg-card h3 .sg-close { cursor: pointer; color: ${UI.sub}; font-size: 16px; padding: 0 4px; }
.sg-card h3 .sg-close:hover { color: ${UI.danger}; }
.sg-kv { display: grid; grid-template-columns: auto 1fr; gap: 2px 10px; font-size: 12px; }
.sg-kv .k { color: ${UI.sub}; }
.sg-kv .v { text-align: right; }
.sg-row { display: flex; align-items: center; gap: 8px; font-size: 12px; padding: 4px 0; }
.sg-row + .sg-row { border-top: 1px dashed rgba(44, 58, 82, 0.6); }
.sg-grow { flex: 1; }
.sg-muted { color: ${UI.sub}; font-size: 11px; }
.sg-hint {
  margin-top: 8px; padding: 6px 8px; font-size: 11px; color: ${UI.sub};
  border: 1px dashed ${UI.border}; border-radius: 6px; line-height: 1.6;
}
.sg-input {
  width: 90px; padding: 4px 8px; border-radius: 6px;
  border: 1px solid ${UI.border}; background: #0e131d; color: ${UI.text}; font-size: 12px;
}
.sg-select {
  padding: 4px 8px; border-radius: 6px;
  border: 1px solid ${UI.border}; background: #0e131d; color: ${UI.text}; font-size: 12px;
  max-width: 180px;
}
.sg-actions { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 8px; }

/* ---------- 人才表 ---------- */
.sg-table { width: 100%; border-collapse: collapse; font-size: 11px; }
.sg-table th {
  color: ${UI.sub}; font-weight: 400; text-align: left;
  padding: 3px 4px; border-bottom: 1px solid ${UI.border}; white-space: nowrap;
}
.sg-table td { padding: 4px; border-bottom: 1px dashed rgba(44, 58, 82, 0.5); white-space: nowrap; }
.sg-table tr.sg-row-dead td { color: #666; text-decoration: line-through; }
.sg-table .sg-mini {
  padding: 2px 8px; font-size: 11px; border-radius: 5px;
  border: 1px solid ${UI.border}; background: #1c2536; color: ${UI.text}; cursor: pointer;
}
.sg-table .sg-mini:hover { border-color: ${UI.accent}; color: ${UI.accent}; }
.sg-table .sg-mini:disabled { opacity: 0.35; cursor: not-allowed; }

/* ---------- 右下角操作区（亲征） ---------- */
.sg-field-corner {
  position: absolute; right: 16px; bottom: 16px;
  display: flex; flex-direction: column; align-items: center; gap: 8px;
}
.sg-interact {
  width: 76px; height: 76px; border-radius: 50%;
  border: 2px solid ${UI.borderGold}; background: rgba(20, 26, 38, 0.9);
  color: ${UI.accent}; font-size: 15px; font-weight: 700; cursor: pointer;
}
.sg-interact:disabled { opacity: 0.3; }
.sg-field-tip {
  font-size: 11px; color: ${UI.sub}; background: rgba(11,14,20,0.8);
  padding: 4px 8px; border-radius: 6px; border: 1px solid ${UI.border};
}

/* ---------- 虚拟摇杆 ---------- */
.sg-joy {
  position: absolute; left: 28px; bottom: 28px;
  width: 120px; height: 120px; border-radius: 50%;
  border: 2px solid ${UI.border}; background: rgba(20, 26, 38, 0.55);
  touch-action: none;
}
.sg-joy-knob {
  position: absolute; left: 50%; top: 50%; width: 48px; height: 48px;
  border-radius: 50%; background: rgba(201, 164, 76, 0.55);
  border: 2px solid ${UI.accent}; transform: translate(-50%, -50%);
  pointer-events: none;
}

/* ---------- 交互气泡 ---------- */
.sg-bubble {
  position: absolute; transform: translate(-50%, -100%);
  padding: 5px 10px; border-radius: 8px; font-size: 12px;
  background: rgba(20, 26, 38, 0.95); border: 1px solid ${UI.borderGold};
  color: ${UI.accent}; white-space: nowrap; pointer-events: none;
}
.sg-bubble::after {
  content: ''; position: absolute; left: 50%; bottom: -5px; transform: translateX(-50%);
  border-left: 5px solid transparent; border-right: 5px solid transparent;
  border-top: 5px solid ${UI.borderGold};
}

/* ---------- 结算 / 全屏覆盖层 ---------- */
.sg-overlay {
  position: absolute; inset: 0; display: flex; align-items: center; justify-content: center;
  background: rgba(5, 8, 12, 0.72); z-index: 60;
}
.sg-modal {
  width: min(720px, 92vw); max-height: 86vh; overflow: hidden auto;
  padding: 16px 20px; scrollbar-width: thin;
}
.sg-modal::-webkit-scrollbar { width: 6px; }
.sg-modal::-webkit-scrollbar-thumb { background: ${UI.border}; border-radius: 3px; }
.sg-modal h2 {
  margin: 0 0 10px; font-size: 18px; color: ${UI.accent};
  border-bottom: 2px solid ${UI.borderGold}; padding-bottom: 8px;
}
.sg-section-title { margin: 12px 0 6px; font-size: 13px; color: ${UI.accent}; }
.sg-result-item {
  border: 1px solid ${UI.border}; border-radius: 8px;
  padding: 8px 10px; margin-bottom: 6px; font-size: 12px; line-height: 1.7;
}
.sg-result-item.sg-bad { border-color: ${UI.danger}; }
.sg-result-item.sg-good { border-color: ${UI.ok}; }
.sg-code-tag { font-weight: 700; }
.sg-delta-line { color: ${UI.sub}; }
.sg-event-item { font-size: 12px; padding: 3px 0; display: flex; gap: 8px; }
.sg-event-cat {
  flex: 0 0 auto; font-size: 10px; padding: 1px 6px; border-radius: 5px;
  border: 1px solid ${UI.border}; color: ${UI.sub}; margin-top: 1px;
}
.sg-modal-foot { display: flex; justify-content: flex-end; gap: 8px; margin-top: 14px; }

/* ---------- 提示（toast） ---------- */
.sg-toasts {
  position: absolute; top: 64px; left: 50%; transform: translateX(-50%);
  display: flex; flex-direction: column; gap: 6px; align-items: center; z-index: 80;
  pointer-events: none;
}
.sg-toast {
  padding: 8px 16px; border-radius: 8px; font-size: 13px;
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
  font-size: 20px; color: ${UI.text}; text-shadow: 0 2px 8px #000;
  letter-spacing: 2px; z-index: 56; pointer-events: none;
}
.sg-cinema-skipping { position:absolute; right: 20px; bottom: 13vh; font-size: 11px; color: ${UI.sub}; z-index:56; }

/* ---------- 悬停信息卡 ---------- */
.sg-hover-card {
  position: absolute; pointer-events: none; z-index: 40;
  padding: 6px 10px; border-radius: 8px; font-size: 12px; line-height: 1.6;
  background: rgba(20, 26, 38, 0.95); border: 1px solid ${UI.border}; color: ${UI.text};
  transform: translate(12px, -50%);
}
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
};

export function fieldLabel(field: string): string {
  return FIELD_LABELS[field] ?? field;
}
