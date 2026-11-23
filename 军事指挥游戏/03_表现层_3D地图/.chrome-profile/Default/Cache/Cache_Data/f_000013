'use strict';

/* ===== 契约枚举（数值必须与 Contracts.ts / Enums.cs 一致） ===== */
const TerrainType = { Plain: 0, Hill: 1, Mountain: 2, Forest: 3, River: 4 };
const Faction = { Player: 0, Enemy: 1, Neutral: 2 };
const OrderKind = { MoveTo: 0, MoveVia: 1, Hold: 2 };
const ResolveResultCode = { Ok: 0, NoPath: 1, InvalidTarget: 2, AlreadyArrived: 3 };

const TERRAIN = {
  0: { name: '平原', nodeFill: '#6f8b52', cell: '74,110,62', alpha: 0.30 },
  1: { name: '丘陵', nodeFill: '#a89563', cell: '126,112,62', alpha: 0.28 },
  2: { name: '山地', nodeFill: '#b7a98a', cell: '112,98,86', alpha: 0.32 },
  3: { name: '森林', nodeFill: '#46603a', cell: '48,92,62', alpha: 0.32 },
  4: { name: '河流', nodeFill: '#3fb6c9', cell: '44,100,132', alpha: 0.34 },
};

const UNIT_MARK = ['步', '骑', '弓', '器'];
const UNIT_NAME = ['步兵', '骑兵', '弓兵', '器械'];
const FACTION_STYLE = {
  0: { name: '我方', color: '#4b9dff', rgb: '75,157,255', tagFill: 'rgba(36,92,178,0.94)', tagFillStrong: 'rgba(46,116,214,0.98)' },
  1: { name: '敌军', color: '#ff5f5f', rgb: '255,95,95', tagFill: 'rgba(166,38,38,0.94)', tagFillStrong: 'rgba(198,48,48,0.98)' },
  2: { name: '中立', color: '#b8c4cf', rgb: '184,196,207', tagFill: 'rgba(86,96,106,0.92)', tagFillStrong: 'rgba(110,122,134,0.96)' },
};

const NODE_FONT = '12px "Microsoft YaHei", sans-serif';
const TAG_NAME_FONT = '700 12px "Microsoft YaHei", sans-serif';
const TAG_SUB_FONT = '10px "Microsoft YaHei", sans-serif';

/* ===== 地形高度 / 配色 / 山脊参数（固定种子，保证每次生成一致） ===== */
const TERRAIN_HEIGHT = { 0: 0.15, 1: 0.42, 2: 0.82, 3: 0.30, 4: 0.05 };
const TERRAIN_RAMP = [
  { h: 0.00, c: [44, 66, 40] },
  { h: 0.18, c: [64, 88, 52] },
  { h: 0.34, c: [88, 110, 66] },
  { h: 0.48, c: [118, 112, 74] },
  { h: 0.62, c: [142, 128, 92] },
  { h: 0.76, c: [158, 146, 116] },
  { h: 0.90, c: [170, 160, 134] },
  { h: 1.00, c: [182, 172, 148] },
];
const RIDGE_PARAMS = {
  N04: { ang: 0.35, len: 160, wid: 38, amp: 0.26 },
  N08: { ang: -0.22, len: 180, wid: 42, amp: 0.28 },
  N13: { ang: 0.12, len: 155, wid: 36, amp: 0.26 },
  N15: { ang: -0.38, len: 145, wid: 35, amp: 0.24 },
};

/* 河道（世界坐标，程序化平滑） */
const RIVER_MAIN = [[665, 42], [588, 76], [520, 130], [452, 172], [398, 196], [330, 236], [250, 272], [180, 300], [164, 380], [150, 448], [160, 490], [146, 566], [132, 642]];
const RIVER_BRANCH_A = [[742, 250], [655, 236], [575, 220], [505, 206], [452, 188], [420, 176]];
const RIVER_BRANCH_B = [[545, 596], [470, 580], [392, 560], [305, 536], [232, 514], [180, 500], [160, 490]];
const CODE_MESSAGE = {
  1: '路网不可达：该目标无法抵达',
  2: '目标节点无效',
  3: '部队已在目标节点',
};
const TAU = Math.PI * 2;

/* ===== DOM ===== */
const canvas = document.getElementById('map');
const ctx = canvas.getContext('2d');
const timeValueEl = document.getElementById('timeValue');
const clockEl = document.getElementById('clock');
const playerStatEl = document.getElementById('playerStat');
const enemyStatEl = document.getElementById('enemyStat');
const selectedNameEl = document.getElementById('selectedName');
const selectedDetailEl = document.getElementById('selectedDetail');
const waypointListEl = document.getElementById('waypointList');
const btnOrder = document.getElementById('btnOrder');
const btnClear = document.getElementById('btnClear');
const btnStep = document.getElementById('btnStep');
const btnFastForward = document.getElementById('btnFastForward');
const btnReset = document.getElementById('btnReset');
const btnPause = document.getElementById('btnPause');
const orderBanner = document.getElementById('orderBanner');
const toastLayer = document.getElementById('toastLayer');
const modalLayer = document.getElementById('modalLayer');

/* ===== 运行时状态 ===== */
let world = null;
let selectedArmyId = null;
let waypoints = [];
let previewPath = [];
let trailHistory = new Map();
let hoverArmyId = null;
let hoverNodeId = null;
let flashes = [];
let busy = false;
let hintShown = false;
let previewSeq = 0;
let loadError = '';
let cssW = 0;
let cssH = 0;
let dpr = 1;

/* ===== 行军动画 / 自动行军 / 状态反馈（第 3 轮） ===== */
const STEP_MS = 460;       // 单步行军动画时长（下令 / 推进 1 步）
const FF_STEP_MS = 170;    // 快进（加速播放）时每步动画时长
const AUTO_STEP_CAP = 60;  // 自动行军动画步数上限，超过则直达结果态并明确标注
const marchAnim = { moves: new Map(), onDone: null };
const autoMarch = { active: false, paused: false, running: false, mode: 'order', armyId: null, steps: 0 };
const lastArrival = new Map();      // armyId -> 抵达时刻（performance.now），用于「已抵达」状态
const encounterArmies = new Set();  // 遭遇中的部队（关闭战报、重新下令后清除）
let issuedOrder = null;             // { armyId, route: [nodeId] } 命令区「已下令」展示
let marchGeneration = 0;            // 重开时递增，用于中断进行中的自动行军循环
const STATUS_STYLE = {
  idle:    { text: '待命',   color: '#9fb2c4' },
  moving:  { text: '行军中', color: '#ffc36b' },
  arrived: { text: '已抵达', color: '#7ee2a8' },
  combat:  { text: '遭遇中', color: '#ff8080' },
};

const layout = { scale: 1, ox: 0, oy: 0, nodes: [], armies: [], byId: new Map() };

/* ===== 画布与地形底图 ===== */
const terrainRaster = document.createElement('canvas');
const trCtx = terrainRaster.getContext('2d');
let needTerrain = true;

function resize() {
  dpr = Math.min(2, window.devicePixelRatio || 1);
  cssW = Math.max(1, canvas.clientWidth);
  cssH = Math.max(1, canvas.clientHeight);
  canvas.width = Math.round(cssW * dpr);
  canvas.height = Math.round(cssH * dpr);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  terrainRaster.width = canvas.width;
  terrainRaster.height = canvas.height;
  trCtx.setTransform(dpr, 0, 0, dpr, 0, 0);
  computeLayout();
  needTerrain = true;
}

function computeLayout() {
  layout.nodes = [];
  layout.armies = [];
  layout.byId = new Map();
  if (!world) return;

  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const n of world.Map.Nodes) {
    if (n.X < minX) minX = n.X;
    if (n.X > maxX) maxX = n.X;
    if (n.Y < minY) minY = n.Y;
    if (n.Y > maxY) maxY = n.Y;
  }

  const padL = 44, padR = 44, padT = 92, padB = 132;
  const availW = Math.max(240, cssW - padL - padR);
  const availH = Math.max(240, cssH - padT - padB);
  const spanX = Math.max(1, maxX - minX);
  const spanY = Math.max(1, maxY - minY);
  const scale = Math.min(availW / spanX, availH / spanY);

  layout.scale = scale;
  layout.ox = padL + (availW - spanX * scale) / 2 - minX * scale;
  layout.oy = padT + (availH - spanY * scale) / 2 - minY * scale;

  layout.nodes = world.Map.Nodes.map(n => ({
    node: n,
    sx: n.X * scale + layout.ox,
    sy: n.Y * scale + layout.oy,
  }));
  layout.byId = new Map(layout.nodes.map(it => [it.node.Id, it]));

  const items = world.Armies.map(a => {
    const p = armyWorldPos(a);
    return {
      army: a,
      sx: p.x * scale + layout.ox,
      sy: p.y * scale + layout.oy,
      idle: isArmyIdle(a),
    };
  });

  const groups = new Map();
  for (const it of items) {
    if (!it.idle) continue;
    if (!groups.has(it.army.NodeId)) groups.set(it.army.NodeId, []);
    groups.get(it.army.NodeId).push(it);
  }

  for (const it of items) {
    if (!it.idle) continue;
    const group = groups.get(it.army.NodeId);
    const index = group.indexOf(it);
    if (group.length === 1) {
      it.sy -= 46;
    } else {
      const angle = -Math.PI / 2 + (index * TAU) / group.length;
      it.sx += Math.cos(angle) * 42;
      it.sy += Math.sin(angle) * 42 - 16;
    }
  }

  // 部队标记也不压住顶部/底部面板（优先小距离推出）
  const blockers = uiBlockers();
  for (const it of items) {
    for (let pass = 0; pass < 3; pass++) {
      let moved = false;
      for (const b of blockers) {
        const pad = 17;
        const left = b.x - pad;
        const right = b.x + b.w + pad;
        const top = b.y - pad;
        const bottom = b.y + b.h + pad;
        if (it.sx <= left || it.sx >= right || it.sy <= top || it.sy >= bottom) continue;
        const pushDown = bottom - it.sy;
        const pushUp = it.sy - top;
        const pushRight = right - it.sx;
        const pushLeft = it.sx - left;
        const min = Math.min(pushDown, pushUp, pushRight, pushLeft);
        if (min === pushDown) it.sy += pushDown + 1;
        else if (min === pushLeft) it.sx -= pushLeft + 1;
        else if (min === pushRight) it.sx += pushRight + 1;
        else it.sy -= pushUp + 1;
        moved = true;
      }
      if (!moved) break;
    }
  }

  layout.armies = items;
  layoutNodeLabels();
  layoutArmyTags();
}

function w2s(x, y) {
  return [x * layout.scale + layout.ox, y * layout.scale + layout.oy];
}

function nodeWorld(id) {
  const n = world && world.Map.Nodes.find(item => item.Id === id);
  return n ? { x: n.X, y: n.Y } : { x: 0, y: 0 };
}

/* ===== 弯曲路网：节点之间不是直线，而是带 S 弯 / 盘山弯的路径 ===== */
const roadCache = new Map();

function buildRoadGeometry(aId, bId) {
  const A = nodeWorld(aId);
  const B = nodeWorld(bId);
  const edge = world.Map.Edges.find(e =>
    (e.From === aId && e.To === bId) || (e.From === bId && e.To === aId));
  const type = edge ? edge.Type : 0;
  const dx = B.x - A.x;
  const dy = B.y - A.y;
  const L = Math.hypot(dx, dy) || 1;
  const nx = -dy / L;
  const ny = dx / L;
  // 弯的数量和幅度由边本身决定：山路更绕，道路轻微起伏
  const bends = L > 150 ? 3 : 2;
  const amp = type === 1 ? Math.min(44, L * 0.18) : Math.min(26, L * 0.11);
  const seed = aId.charCodeAt(1) * 131 + bId.charCodeAt(1) * 977 + aId.charCodeAt(2) * 37;
  const ctrl = [[A.x, A.y]];
  for (let i = 1; i <= bends; i++) {
    const t = i / (bends + 1);
    const sign = i % 2 === 1 ? 1 : -1;
    const jitter = 0.62 + 0.76 * hash2(i * 17, seed, 5);
    const off = sign * amp * jitter;
    ctrl.push([A.x + dx * t + nx * off, A.y + dy * t + ny * off]);
  }
  ctrl.push([B.x, B.y]);
  const pts = catmullRomPath(ctrl);
  const cum = [0];
  for (let i = 1; i < pts.length; i++) {
    cum.push(cum[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]));
  }
  return { pts, cum, total: cum[cum.length - 1] };
}

/* 返回有向路网几何：同一条路两个方向共用同一条曲线 */
function roadGeometry(aId, bId) {
  if (!aId || !bId || aId === bId) return null;
  const key = aId + '|' + bId;
  const hit = roadCache.get(key);
  if (hit) return hit;

  const canonA = aId < bId ? aId : bId;
  const canonB = aId < bId ? bId : aId;
  let base = roadCache.get(canonA + '|' + canonB);
  if (!base) {
    base = buildRoadGeometry(canonA, canonB);
    roadCache.set(canonA + '|' + canonB, base);
  }
  if (canonA === aId) return base;

  const pts = base.pts.slice().reverse();
  const cum = [0];
  for (let i = 1; i < pts.length; i++) {
    cum.push(cum[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]));
  }
  const rev = { pts, cum, total: cum[cum.length - 1] };
  roadCache.set(key, rev);
  return rev;
}

function roadPointAt(g, s) {
  const dist = Math.max(0, Math.min(g.total, s));
  let lo = 0, hi = g.cum.length - 1;
  while (lo < hi - 1) {
    const mid = (lo + hi) >> 1;
    if (g.cum[mid] <= dist) lo = mid;
    else hi = mid;
  }
  const seg = g.cum[hi] - g.cum[lo] || 1;
  const t = (dist - g.cum[lo]) / seg;
  return [
    g.pts[lo][0] + (g.pts[hi][0] - g.pts[lo][0]) * t,
    g.pts[lo][1] + (g.pts[hi][1] - g.pts[lo][1]) * t,
  ];
}

/* 沿路网取 s0→s1 的一段（s 为弧长，允许反向） */
function roadSlice(g, s0, s1) {
  const out = [roadPointAt(g, s0)];
  const forward = s1 >= s0;
  for (let i = 1; i < g.pts.length - 1; i++) {
    const s = g.cum[i];
    if (forward ? s > s0 && s < s1 : s < s0 && s > s1) out.push(g.pts[i]);
  }
  out.push(roadPointAt(g, s1));
  return out;
}

function projectToEdge(p, aId, bId) {
  const A = nodeWorld(aId);
  const B = nodeWorld(bId);
  const dx = B.x - A.x, dy = B.y - A.y;
  const len2 = dx * dx + dy * dy || 1;
  const t = ((p.X - A.x) * dx + (p.Y - A.y) * dy) / len2;
  return { t, dist: Math.hypot(p.X - (A.x + dx * t), p.Y - (A.y + dy * t)) };
}

/* 判断一小段轨迹落在哪条边上（后端轨迹点是沿直线边插值的） */
function findEdgeForSegment(p0, p1) {
  let best = null;
  for (const e of world.Map.Edges) {
    const a = projectToEdge(p0, e.From, e.To);
    const b = projectToEdge(p1, e.From, e.To);
    if (a.dist > 3 || b.dist > 3) continue;
    if (a.t < -0.02 || a.t > 1.02 || b.t < -0.02 || b.t > 1.02) continue;
    const dev = a.dist + b.dist;
    if (!best || dev < best.dev) best = { from: e.From, to: e.To, t0: a.t, t1: b.t, dev };
  }
  if (!best) return null;
  if (best.t0 > best.t1) {
    const t = best.from;
    best.from = best.to;
    best.to = t;
    const tt = best.t0;
    best.t0 = best.t1;
    best.t1 = tt;
  }
  return best;
}

function armyWorldPos(army) {
  const path = army.PlannedPath || [];
  const index = Math.max(0, Math.min(army.PathIndex | 0, path.length - 1));
  if (path.length === 0) return nodeWorld(army.NodeId);
  if (index + 1 >= path.length) return nodeWorld(path[index]);
  const g = roadGeometry(path[index], path[index + 1]);
  if (!g) return nodeWorld(path[index]);
  const p = Math.max(0, Math.min(1, army.EdgeProgress || 0));
  const pt = roadPointAt(g, p * g.total);
  return { x: pt[0], y: pt[1] };
}

function isArmyIdle(army) {
  const path = army.PlannedPath || [];
  return path.length === 0 || army.PathIndex + 1 >= path.length;
}

/* ===== 补间动画：沿弯曲路网从旧位置滑到新位置 ===== */
function easeInOutCubic(t) { return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; }

/* 屏幕/世界折线去重并预计算累计弧长 */
function finalizePath(pts) {
  const out = [];
  for (const p of pts) {
    if (out.length) {
      const last = out[out.length - 1];
      if (Math.hypot(p[0] - last[0], p[1] - last[1]) < 0.01) continue;
    }
    out.push([p[0], p[1]]);
  }
  if (out.length < 2) return null;
  const cum = [0];
  for (let i = 1; i < out.length; i++) {
    cum.push(cum[i - 1] + Math.hypot(out[i][0] - out[i - 1][0], out[i][1] - out[i - 1][1]));
  }
  return { pts: out, cum, total: cum[cum.length - 1] };
}

/* 由推进前后的部队状态，生成沿路网曲线的运动路径（世界坐标） */
function buildMovePath(before, after) {
  const bp = before.PlannedPath || [];
  const ap = after.PlannedPath || [];
  const bi = Math.max(0, Math.min(before.PathIndex | 0, Math.max(0, bp.length - 1)));
  const ai = Math.max(0, Math.min(after.PathIndex | 0, Math.max(0, ap.length - 1)));
  const bg = clamp01(before.EdgeProgress || 0);
  const ag = clamp01(after.EdgeProgress || 0);
  if (!ap.length) return null;

  const samePath = bp.length === ap.length && bp.length > 0 && bp.every((id, i) => id === ap[i]);
  if (samePath && ai === bi && Math.abs(ag - bg) < 1e-9) return null; // 本步没动

  const pts = [];
  const start = armyWorldPos(before);

  if (samePath && ai >= bi) {
    // 续走：从 (bi,bg) 沿当前边 → 中间整边 → 最后一段到 (ai,ag)
    if (ai === bi) {
      if (ai + 1 < ap.length) {
        const g = roadGeometry(ap[ai], ap[ai + 1]);
        if (g) pts.push(...roadSlice(g, bg * g.total, ag * g.total));
      }
    } else {
      if (bi + 1 < bp.length) {
        const g = roadGeometry(bp[bi], bp[bi + 1]);
        if (g) pts.push(...roadSlice(g, bg * g.total, g.total));
      }
      for (let i = bi + 1; i < ai; i++) {
        const g = roadGeometry(bp[i], bp[i + 1]);
        if (!g) continue;
        if (!pts.length) pts.push(...g.pts); else pts.push(...g.pts.slice(1));
      }
      if (ai + 1 < ap.length) {
        const g = roadGeometry(ap[ai], ap[ai + 1]);
        if (g) pts.push(...roadSlice(g, 0, ag * g.total));
      }
    }
    if (!pts.length) return null;
    pts[0] = [start.x, start.y];
    return finalizePath(pts);
  }

  // 新命令改写路径：旧位置先归位到新路径起点，再沿新路径走
  const first = nodeWorld(ap[0] || after.NodeId);
  pts.push([start.x, start.y], [first.x, first.y]);
  for (let i = 0; i < ai; i++) {
    const g = roadGeometry(ap[i], ap[i + 1]);
    if (!g) continue;
    pts.push(...g.pts.slice(1));
  }
  if (ai + 1 < ap.length) {
    const g = roadGeometry(ap[ai], ap[ai + 1]);
    if (g) pts.push(...roadSlice(g, 0, ag * g.total));
  }
  return finalizePath(pts);
}

/* 折线上弧长 dist 处的点 */
function pointAtDistance(pts, cum, dist) {
  const d = Math.max(0, Math.min(cum[cum.length - 1], dist));
  let lo = 0, hi = cum.length - 1;
  while (lo < hi - 1) {
    const mid = (lo + hi) >> 1;
    if (cum[mid] <= d) lo = mid;
    else hi = mid;
  }
  const seg = cum[hi] - cum[lo] || 1;
  const t = (d - cum[lo]) / seg;
  return [pts[lo][0] + (pts[hi][0] - pts[lo][0]) * t, pts[lo][1] + (pts[hi][1] - pts[lo][1]) * t];
}

/* 折线从起点到弧长 dist 的前缀（画「正在走」的轨迹） */
function pathPrefix(pts, cum, dist) {
  const d = Math.max(0, Math.min(cum[cum.length - 1], dist));
  const out = [];
  for (let i = 0; i < pts.length; i++) {
    if (cum[i] <= d) out.push(pts[i]);
    else {
      if (i > 0) out.push(pointAtDistance(pts, cum, d));
      break;
    }
  }
  if (!out.length) out.push(pts[0]);
  return out;
}

/* 动画进度（0..1，easeInOutCubic） */
function moveEased(move, now) {
  return easeInOutCubic(clamp01((now - move.t0) / move.dur));
}

/* 部队当前状态：待命 / 行军中 / 已抵达 / 遭遇中 */
function armyStatus(army, now) {
  if (encounterArmies.has(army.Id)) return { key: 'combat', ...STATUS_STYLE.combat };
  if (marchAnim.moves.has(army.Id)) return { key: 'moving', ...STATUS_STYLE.moving };
  const path = army.PlannedPath || [];
  if (path.length && army.PathIndex + 1 < path.length) return { key: 'moving', ...STATUS_STYLE.moving };
  const at = lastArrival.get(army.Id);
  if (at != null && now - at < 6000) return { key: 'arrived', ...STATUS_STYLE.arrived };
  return { key: 'idle', ...STATUS_STYLE.idle };
}

/*
 * 播放一步行军的补间动画：world 暂不提交，动画结束后由调用方 commitResult。
 * 路径首尾替换为部队实际的渲染位置（旧布局 → 新布局），保证滑动无缝、落地不跳。
 */
function playStepAnimation(res, dur) {
  const oldWorld = world;
  if (!oldWorld || !res || !res.State) return Promise.resolve();

  const startScreen = new Map(layout.armies.map(it => [it.army.Id, [it.sx, it.sy]]));
  world = res.State;
  computeLayout();
  const endScreen = new Map(layout.armies.map(it => [it.army.Id, [it.sx, it.sy]]));
  world = oldWorld;
  computeLayout();

  const moves = new Map();
  for (const after of res.State.Armies) {
    const before = oldWorld.Armies.find(a => a.Id === after.Id);
    if (!before) continue;
    const worldPts = buildMovePath(before, after);
    if (!worldPts) continue;
    const pts = worldPts.pts.map(p => w2s(p[0], p[1]));
    const s = startScreen.get(after.Id);
    const e = endScreen.get(after.Id);
    if (s) pts[0] = s;
    if (e) pts[pts.length - 1] = e;
    const fin = finalizePath(pts);
    if (!fin || fin.total < 1) continue;
    moves.set(after.Id, { pts: fin.pts, cum: fin.cum, total: fin.total, t0: performance.now(), dur });
  }
  marchAnim.moves = moves;
  if (!moves.size) return Promise.resolve();
  return new Promise(resolve => { marchAnim.onDone = resolve; });
}

/* ===== 地形浮雕底图（高度场 + 山体阴影 + 高程配色，静态缓存） ===== */
const terrainGrid = document.createElement('canvas');
const tgCtx = terrainGrid.getContext('2d');
const TERRAIN_LUT = buildTerrainLut();

function clamp01(v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

function buildTerrainLut() {
  const lut = new Uint8ClampedArray(256 * 3);
  for (let i = 0; i < 256; i++) {
    const h = i / 255;
    let c = TERRAIN_RAMP[TERRAIN_RAMP.length - 1].c;
    for (let k = 1; k < TERRAIN_RAMP.length; k++) {
      if (h <= TERRAIN_RAMP[k].h) {
        const a = TERRAIN_RAMP[k - 1];
        const b = TERRAIN_RAMP[k];
        const t = (h - a.h) / (b.h - a.h);
        c = [
          a.c[0] + (b.c[0] - a.c[0]) * t,
          a.c[1] + (b.c[1] - a.c[1]) * t,
          a.c[2] + (b.c[2] - a.c[2]) * t,
        ];
        break;
      }
    }
    lut[i * 3] = c[0];
    lut[i * 3 + 1] = c[1];
    lut[i * 3 + 2] = c[2];
  }
  return lut;
}

/* 固定种子哈希 / 值噪声 / fBm */
function hash2(ix, iy, seed) {
  let h = ix * 374761393 + iy * 668265263 + seed * 1442695041;
  h = (h ^ (h >> 13)) * 1274126177;
  h = h ^ (h >> 16);
  return (h >>> 0) / 4294967295;
}

function noise2(x, y, seed) {
  const x0 = Math.floor(x), y0 = Math.floor(y);
  const fx = x - x0, fy = y - y0;
  const sx = fx * fx * (3 - 2 * fx);
  const sy = fy * fy * (3 - 2 * fy);
  const v00 = hash2(x0, y0, seed);
  const v10 = hash2(x0 + 1, y0, seed);
  const v01 = hash2(x0, y0 + 1, seed);
  const v11 = hash2(x0 + 1, y0 + 1, seed);
  return (v00 * (1 - sx) + v10 * sx) * (1 - sy) + (v01 * (1 - sx) + v11 * sx) * sy;
}

function fbm2(x, y, octaves, seed) {
  let sum = 0, amp = 0.5, freq = 1, norm = 0;
  for (let i = 0; i < octaves; i++) {
    sum += amp * noise2(x * freq, y * freq, seed + i * 101);
    norm += amp;
    amp *= 0.5;
    freq *= 2.03;
  }
  return sum / norm;
}

function terrainHeightAt(wx, wy, nodes, ridges) {
  let wsum = 0, hsum = 0, forest = 0;
  for (let i = 0; i < nodes.length; i++) {
    const n = nodes[i];
    const dx = n.X - wx, dy = n.Y - wy;
    const d2 = dx * dx + dy * dy;
    const base = TERRAIN_HEIGHT[n.Terrain] !== undefined ? TERRAIN_HEIGHT[n.Terrain] : 0.2;
    const w = 1 / Math.pow(d2 + 1600, 1.35);
    wsum += w;
    hsum += w * base;
    if (n.Terrain === 3) forest += Math.exp(-d2 / 9000);
  }
  let h = wsum > 0 ? hsum / wsum : 0.2;

  // 山地脊线：沿固定方向的细长高斯，加强立体感
  let ridge = 0;
  for (let i = 0; i < ridges.length; i++) {
    const r = ridges[i];
    const dx = wx - r.x, dy = wy - r.y;
    const u = (dx * r.cos + dy * r.sin) / r.len;
    const v = (-dx * r.sin + dy * r.cos) / r.wid;
    const g = Math.exp(-(u * u) - (v * v) * 1.7);
    if (g > 0.02 && g * r.amp > ridge) ridge = g * r.amp;
  }
  h += ridge;

  const broad = fbm2(wx / 160, wy / 160, 4, 17);
  const ridgeN = 1 - Math.abs(fbm2(wx / 64, wy / 64, 4, 211) * 2 - 1);
  const crag = 1 - Math.abs(fbm2(wx / 44, wy / 44, 3, 303) * 2 - 1);
  const fine = fbm2(wx / 28, wy / 28, 3, 91);
  h += (broad - 0.5) * (0.09 + 0.18 * h);
  h += (ridgeN - 0.45) * 0.09 * (0.35 + h);
  h += (crag - 0.5) * 0.035 * (0.3 + h);
  h += (fine - 0.5) * (0.03 + 0.08 * h + 0.06 * Math.min(1, forest));
  return clamp01(h);
}

/* 可分离滑动窗口均值模糊：给沟谷 AO 提供邻域平均高度 */
function blurHeights(src, gw, gh, radius) {
  const tmp = new Float32Array(src.length);
  const out = new Float32Array(src.length);
  const span = radius * 2 + 1;

  for (let y = 0; y < gh; y++) {
    const row = y * gw;
    let sum = 0;
    for (let x = -radius; x <= radius; x++) sum += src[row + Math.min(gw - 1, Math.max(0, x))];
    for (let x = 0; x < gw; x++) {
      tmp[row + x] = sum / span;
      sum += src[row + Math.min(gw - 1, x + radius + 1)] - src[row + Math.max(0, x - radius)];
    }
  }

  for (let x = 0; x < gw; x++) {
    let sum = 0;
    for (let y = -radius; y <= radius; y++) sum += tmp[Math.min(gh - 1, Math.max(0, y)) * gw + x];
    for (let y = 0; y < gh; y++) {
      out[y * gw + x] = sum / span;
      sum += tmp[Math.min(gh - 1, y + radius + 1) * gw + x] - tmp[Math.max(0, y - radius) * gw + x];
    }
  }
  return out;
}

function renderTerrainRaster() {
  trCtx.clearRect(0, 0, cssW, cssH);

  if (!world || !layout.nodes.length) {
    trCtx.fillStyle = '#2b3a24';
    trCtx.fillRect(0, 0, cssW, cssH);
    return;
  }

  const STEP = 2;
  const gw = Math.max(2, Math.ceil(cssW / STEP) + 1);
  const gh = Math.max(2, Math.ceil(cssH / STEP) + 1);
  const heights = new Float32Array(gw * gh);

  const nodes = world.Map.Nodes;
  const ridges = nodes.filter(n => RIDGE_PARAMS[n.Id]).map(n => {
    const p = RIDGE_PARAMS[n.Id];
    return {
      x: n.X, y: n.Y,
      cos: Math.cos(p.ang), sin: Math.sin(p.ang),
      len: p.len, wid: p.wid, amp: p.amp,
    };
  });

  const invScale = 1 / layout.scale;
  for (let gy = 0; gy < gh; gy++) {
    const wy = (gy * STEP - layout.oy) * invScale;
    const row = gy * gw;
    for (let gx = 0; gx < gw; gx++) {
      const wx = (gx * STEP - layout.ox) * invScale;
      heights[row + gx] = terrainHeightAt(wx, wy, nodes, ridges);
    }
  }

  // 邻域平均高度：沟谷压暗、山脊提亮（软 AO）
  const blur = blurHeights(heights, gw, gh, 5);

  const img = tgCtx.createImageData(gw, gh);
  const data = img.data;
  const gradScale = 190;
  const az = 315 * Math.PI / 180;
  const alt = 45 * Math.PI / 180;
  const Lx = Math.sin(az) * Math.cos(alt);
  const Ly = -Math.cos(az) * Math.cos(alt);
  const Lz = Math.sin(alt);
  const minDim = Math.min(gw, gh);

  for (let gy = 0; gy < gh; gy++) {
    for (let gx = 0; gx < gw; gx++) {
      const idx = gy * gw + gx;
      const h = heights[idx];
      const hL = heights[idx - (gx > 0 ? 1 : 0)];
      const hR = heights[idx + (gx < gw - 1 ? 1 : 0)];
      const hU = heights[idx - (gy > 0 ? gw : 0)];
      const hD = heights[idx + (gy < gh - 1 ? gw : 0)];

      // 山体阴影：高度梯度 → 法线 → 西北 315° / 45° 光照
      const gradX = ((hR - hL) / (2 * STEP)) * gradScale;
      const gradY = ((hD - hU) / (2 * STEP)) * gradScale;
      const slope = Math.min(1, Math.hypot(gradX, gradY));
      const invLen = 1 / Math.sqrt(gradX * gradX + gradY * gradY + 1);
      const nx = -gradX * invLen;
      const ny = -gradY * invLen;
      const nz = invLen;
      let dot = nx * Lx + ny * Ly + nz * Lz;
      if (dot < 0) dot = 0;
      let shade = 0.5 + 0.68 * Math.pow(dot, 1.05);
      if (shade > 1.16) shade = 1.16;
      shade *= 1 - Math.min(0.12, slope * 0.12);

      // 沟谷软 AO：低于邻域均值压暗，脊线略提亮
      const valley = h - blur[idx];
      shade *= 1 + Math.max(-0.15, Math.min(0.06, valley * 2.6));

      // 投影阴影：西北来光，检查光照方向一侧是否有更高地形
      if (gx >= 3 && gy >= 3) {
        const castH = heights[(gy - 3) * gw + (gx - 3)];
        const cast = Math.max(0, Math.min(0.2, (castH - h - 0.012) * 2.4));
        shade *= 1 - cast;
      }

      // 受光面偏暖、背光面偏冷，强化体积感
      const sunT = Math.max(0, Math.min(1, (dot - 0.52) / 0.48));
      const rTint = 0.95 + 0.12 * sunT;
      const bTint = 1.06 - 0.12 * sunT;

      // 极淡等高线：每 0.08 高度一道，仅在缓坡上出现
      let contour = 1;
      const band = Math.floor(h / 0.08);
      if (gx > 0 && Math.floor(hL / 0.08) !== band && Math.abs(h - hL) < 0.024) contour = 0.85;
      if (gy > 0 && Math.floor(hU / 0.08) !== band && Math.abs(h - hU) < 0.024) contour = 0.85;

      const lutIdx = (h * 255) | 0;
      const grain = (hash2(gx, gy, 771) - 0.5) * 8;

      // 边缘轻微压暗（不做重度暗角）
      const edgeDist = Math.min(gx, gw - 1 - gx, gy, gh - 1 - gy) / minDim;
      const edgeDim = 0.86 + 0.14 * Math.min(1, edgeDist / 0.05);

      // 中频起伏 + 陡坡岩石混色：打散大块“云雾感”，让坡面更像岩石
      const rockMix = Math.max(0, Math.min(1, (slope - 0.18) * 1.8)) * 0.26;
      const micro = (fbm2(gx * 0.05, gy * 0.05, 3, 133) - 0.5) * 0.13 * (0.45 + slope);
      const light = shade * contour * edgeDim * (1 + micro);

      const p = idx * 4;
      data[p] = (TERRAIN_LUT[lutIdx * 3] * light * rTint) * (1 - rockMix) + 128 * rockMix + grain;
      data[p + 1] = (TERRAIN_LUT[lutIdx * 3 + 1] * light) * (1 - rockMix) + 116 * rockMix + grain;
      data[p + 2] = (TERRAIN_LUT[lutIdx * 3 + 2] * light * bTint) * (1 - rockMix) + 98 * rockMix + grain;
      data[p + 3] = 255;
    }
  }

  if (terrainGrid.width !== gw || terrainGrid.height !== gh) {
    terrainGrid.width = gw;
    terrainGrid.height = gh;
  }
  tgCtx.putImageData(img, 0, 0);

  trCtx.imageSmoothingEnabled = true;
  trCtx.drawImage(terrainGrid, 0, 0, gw, gh, 0, 0, cssW, cssH);

  drawRivers();
  drawTerrainFinish();
}

function drawRivers() {
  strokeRiver(RIVER_MAIN, 4.0, false);
  strokeRiver(RIVER_BRANCH_A, 2.6, false);
  strokeRiver(RIVER_BRANCH_B, 2.6, true);
}

function strokeRiver(rawPoints, width, taperSource) {
  const pts = catmullRomPath(rawPoints.map(p => w2s(p[0], p[1])));
  if (pts.length < 2) return;

  // 横向微摆动：河道避免长直线，接近参考图的自然蜿蜒
  const seed = rawPoints[0][0] * 0.017 + rawPoints[0][1] * 0.031;
  for (let i = 1; i < pts.length - 1; i++) {
    const ax = pts[i + 1][0] - pts[i - 1][0];
    const ay = pts[i + 1][1] - pts[i - 1][1];
    const l = Math.hypot(ax, ay) || 1;
    const wob = (Math.sin(i * 0.28 + seed) * 0.62 + Math.sin(i * 0.07 + seed * 1.7) * 0.38) * 16;
    pts[i] = [pts[i][0] - (ay / l) * wob, pts[i][1] + (ax / l) * wob];
  }

  const widths = pts.map((_, i) => {
    const t = i / (pts.length - 1);
    // 河宽随河段轻微变化，避免像等宽带子
    const vary = 0.88 + 0.24 * noise2(i * 0.09, seed * 0.7, 41);
    if (!taperSource) return width * vary;
    return width * vary * (0.3 + 0.7 * Math.min(1, t / 0.16));
  });

  trCtx.save();
  trCtx.lineCap = 'round';
  trCtx.lineJoin = 'round';

  // 1) 河谷暗带：宽而柔和，模拟河道下切和两岸阴影
  trCtx.shadowColor = 'rgba(8,22,20,0.55)';
  trCtx.shadowBlur = 10;
  riverPass(pts, widths, 9, 'rgba(12,32,26,0.30)');
  trCtx.shadowBlur = 0;

  // 2) 深色岸线 + 水体
  riverPass(pts, widths, 2.6, 'rgba(9,42,48,0.95)');
  riverPass(pts, widths, 0, '#319fb4');

  // 3) 水面高光：沿下游略宽，制造流动感
  const high = widths.map((w, i) => Math.max(0.6, w * (0.24 + 0.12 * (i / pts.length))));
  riverPass(pts, high, 0, 'rgba(206,246,252,0.26)');

  // 4) 青色反光，让水带从地形里“浮”起来
  trCtx.shadowColor = 'rgba(63,182,201,0.5)';
  trCtx.shadowBlur = 6;
  riverPass(pts, widths.map(w => w * 0.45), 0, 'rgba(120,226,236,0.16)');
  trCtx.shadowBlur = 0;

  trCtx.restore();
}

function riverPass(pts, widths, extra, color) {
  trCtx.strokeStyle = color;
  for (let i = 1; i < pts.length; i++) {
    trCtx.lineWidth = widths[i] + extra;
    trCtx.beginPath();
    trCtx.moveTo(pts[i - 1][0], pts[i - 1][1]);
    trCtx.lineTo(pts[i][0], pts[i][1]);
    trCtx.stroke();
  }
}

function drawTerrainFinish() {
  const g = trCtx.createRadialGradient(
    cssW / 2, cssH / 2, Math.min(cssW, cssH) * 0.36,
    cssW / 2, cssH / 2, Math.max(cssW, cssH) * 0.82);
  g.addColorStop(0, 'rgba(0,0,0,0)');
  g.addColorStop(1, 'rgba(26,22,12,0.16)');
  trCtx.fillStyle = g;
  trCtx.fillRect(0, 0, cssW, cssH);
}

function tracePolyline(context, pts) {
  for (let i = 0; i < pts.length; i++) {
    if (i === 0) context.moveTo(pts[i][0], pts[i][1]);
    else context.lineTo(pts[i][0], pts[i][1]);
  }
}

/* Catmull-Rom 平滑采样：把折线变成过点的平滑曲线 */
function catmullRomPath(pts) {
  if (pts.length < 2) return pts.slice();
  const out = [];
  const p = [pts[0], ...pts, pts[pts.length - 1]];
  for (let i = 1; i < p.length - 2; i++) {
    const p0 = p[i - 1], p1 = p[i], p2 = p[i + 1], p3 = p[i + 2];
    const segLen = Math.hypot(p2[0] - p1[0], p2[1] - p1[1]);
    const n = Math.max(3, Math.min(18, Math.round(segLen / 8)));
    for (let j = 0; j < n; j++) {
      const t = j / n, t2 = t * t, t3 = t2 * t;
      out.push([
        0.5 * (2 * p1[0] + (-p0[0] + p2[0]) * t + (2 * p0[0] - 5 * p1[0] + 4 * p2[0] - p3[0]) * t2 + (-p0[0] + 3 * p1[0] - 3 * p2[0] + p3[0]) * t3),
        0.5 * (2 * p1[1] + (-p0[1] + p2[1]) * t + (2 * p0[1] - 5 * p1[1] + 4 * p2[1] - p3[1]) * t2 + (-p0[1] + 3 * p1[1] - 3 * p2[1] + p3[1]) * t3),
      ]);
    }
  }
  out.push(pts[pts.length - 1]);
  return out;
}

function polylineLength(pts) {
  let len = 0;
  for (let i = 1; i < pts.length; i++) {
    len += Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]);
  }
  return len;
}

function rectsOverlap(a, b, pad) {
  return a.x < b.x + b.w + pad && b.x < a.x + a.w + pad
    && a.y < b.y + b.h + pad && b.y < a.y + a.h + pad;
}

function overlapArea(a, b, pad) {
  const ox = Math.min(a.x + a.w, b.x + b.w + pad) - Math.max(a.x, b.x - pad);
  const oy = Math.min(a.y + a.h, b.y + b.h + pad) - Math.max(a.y, b.y - pad);
  return ox > 0 && oy > 0 ? ox * oy : 0;
}

/* 顶部/底部面板占用的屏幕区域，地名与名条必须避开 */
function uiBlockers() {
  const rects = [];
  for (const sel of ['.topbar', '.panel.status', '.panel.help', '.cmdbar']) {
    const el = document.querySelector(sel);
    if (!el || el.classList.contains('hidden')) continue;
    const r = el.getBoundingClientRect();
    if (r.width > 0 && r.height > 0) {
      rects.push({ x: r.left - 4, y: r.top - 4, w: r.width + 8, h: r.height + 8 });
    }
  }
  return rects;
}

/* 地名常显布局：默认在节点下方，边缘/碰撞时翻到上方或左右 */
function layoutNodeLabels() {
  layout.labels = [];
  if (!world || !layout.nodes.length) return;

  const placed = uiBlockers();
  ctx.save();
  ctx.font = NODE_FONT;
  for (const item of layout.nodes) {
    const w = Math.ceil(ctx.measureText(item.node.Name).width);
    const h = 14;
    const candidates = [
      [item.sx - w / 2, item.sy + 15],
      [item.sx - w / 2, item.sy - 15 - h],
      [item.sx + 15, item.sy - h / 2],
      [item.sx - 15 - w, item.sy - h / 2],
      [item.sx + 10, item.sy + 12],
      [item.sx - 10 - w, item.sy + 12],
    ];

    let best = null;
    for (const [x, y] of candidates) {
      const rect = { x, y, w, h };
      if (x < 4 || y < 66 || x + w > cssW - 4 || y + h > cssH - 6) continue;
      if (placed.some(r => rectsOverlap(rect, r, 3))) continue;
      best = rect;
      break;
    }
    if (!best) {
      best = {
        x: Math.max(4, Math.min(cssW - w - 4, item.sx - w / 2)),
        y: Math.min(cssH - h - 6, item.sy + 15),
        w, h,
      };
    }
    placed.push({ x: best.x - 2, y: best.y - 2, w: best.w + 4, h: best.h + 4 });
    layout.labels.push({ item, rect: best, text: item.node.Name });
  }
  ctx.restore();
}

/* 军队名条布局：贴在标记上方/侧方，避让地名与彼此 */
function layoutArmyTags() {
  for (const it of layout.armies) it.tag = null;
  if (!layout.armies.length) return;

  const placed = uiBlockers().concat((layout.labels || []).map(l => ({
    x: l.rect.x - 2, y: l.rect.y - 2, w: l.rect.w + 4, h: l.rect.h + 4,
  })));

  for (const it of layout.armies) {
    const army = it.army;
    ctx.save();
    ctx.font = TAG_NAME_FONT;
    const nameW = ctx.measureText(army.Name).width;
    ctx.font = TAG_SUB_FONT;
    const status = armyStatus(army, performance.now());
    const subW = ctx.measureText(`${status.text} · ${army.Strength} 兵`).width;
    ctx.restore();

    const w = Math.ceil(Math.max(nameW, subW)) + 18;
    const h = 31;
    const cx = it.sx, cy = it.sy;
    const candidates = [];
    for (const dx of [-w / 2, -w / 2 + 12, -w / 2 - 12, -w / 2 + 24, -w / 2 - 24]) {
      candidates.push([cx + dx, cy - 21 - h]);
      candidates.push([cx + dx, cy + 21]);
      candidates.push([cx + dx, cy + 52]);
    }
    for (const dy of [-h / 2, -h / 2 + 10, -h / 2 - 10, -h / 2 + 22, -h / 2 - 22]) {
      candidates.push([cx + 21, cy + dy]);
      candidates.push([cx - 21 - w, cy + dy]);
    }

    let best = null;
    let bestOverlap = Infinity;
    for (const [x, y] of candidates) {
      const rect = { x, y, w, h };
      if (x < 4 || y < 66 || x + w > cssW - 4 || y + h > cssH - 6) continue;
      let overlap = 0;
      for (const r of placed) overlap += overlapArea(rect, r, 3);
      if (overlap === 0) {
        best = rect;
        break;
      }
      if (overlap < bestOverlap) {
        bestOverlap = overlap;
        best = rect;
      }
    }
    if (!best) {
      best = {
        x: Math.max(4, Math.min(cssW - w - 4, cx - w / 2)),
        y: Math.max(66, Math.min(cssH - h - 6, cy - 21 - h)),
        w, h,
      };
    }
    placed.push({ x: best.x - 2, y: best.y - 2, w: best.w + 4, h: best.h + 4 });
    it.tag = best;
  }
}


/* ===== 绘制 ===== */
function draw(now) {
  ctx.clearRect(0, 0, cssW, cssH);

  if (!world) {
    const message = loadError || '载入战场…';
    ctx.fillStyle = '#070b10';
    ctx.fillRect(0, 0, cssW, cssH);
    ctx.fillStyle = 'rgba(160,190,215,0.8)';
    ctx.font = '14px "Microsoft YaHei", sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(message, cssW / 2, cssH / 2);
    return;
  }

  if (needTerrain) {
    renderTerrainRaster();
    needTerrain = false;
  }
  ctx.drawImage(terrainRaster, 0, 0, cssW, cssH);

  drawEdges();
  drawTrails();
  drawMoveTrails(now);
  drawPlannedPaths();
  drawPreview(now);
  drawNodes();
  drawNodeLabels();
  drawArmies(now);
  drawFlashes(now);
  drawHoverTooltip();
}

/* ===== 路网 ===== */
function drawEdges() {
  for (const edge of world.Map.Edges) {
    const g = roadGeometry(edge.From, edge.To);
    if (!g) continue;
    const pts = g.pts.map(p => w2s(p[0], p[1]));

    ctx.save();
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    if (edge.Type === 1) {
      // 山路：深色压边 + 褐色虚线（曲线本身带盘山弯）
      strokeRoad(pts, 'rgba(38,30,20,0.42)', 5);
      ctx.setLineDash([8, 6]);
      strokeRoad(pts, 'rgba(178,138,92,0.8)', 2.6);
    } else {
      // 道路：深色压边 + 暖黄土黄实线 + 浅色高光
      strokeRoad(pts, 'rgba(40,32,18,0.42)', 6.2);
      strokeRoad(pts, 'rgba(214,190,134,0.7)', 3.4);
      strokeRoad(pts, 'rgba(248,236,204,0.2)', 1.2);
    }
    ctx.restore();
  }
}

function strokeRoad(pts, color, width) {
  ctx.beginPath();
  tracePolyline(ctx, pts);
  ctx.strokeStyle = color;
  ctx.lineWidth = width;
  ctx.stroke();
}

/* 后端给的是直线边上的坐标，渲染时贴合到弯曲路网 */
function trailWorldPath(points) {
  const out = [];
  for (let i = 1; i < points.length; i++) {
    const p0 = points[i - 1];
    const p1 = points[i];
    const hit = findEdgeForSegment(p0, p1);
    let seg = null;
    if (hit) {
      const g = roadGeometry(hit.from, hit.to);
      if (g) seg = roadSlice(g, Math.max(0, hit.t0) * g.total, Math.min(1, hit.t1) * g.total);
    }
    if (!seg) seg = [[p0.X, p0.Y], [p1.X, p1.Y]];
    if (out.length === 0) out.push(seg[0]);
    for (let k = 1; k < seg.length; k++) out.push(seg[k]);
  }
  return out;
}

/* 计划路径当前边剩余段 + 后续整条边，全部沿路网曲线 */
function plannedWorldPath(army) {
  const path = army.PlannedPath || [];
  const index = Math.max(0, Math.min(army.PathIndex | 0, path.length - 1));
  if (path.length < 2 || index + 1 >= path.length) return [];
  const p = Math.max(0, Math.min(1, army.EdgeProgress || 0));
  const g0 = roadGeometry(path[index], path[index + 1]);
  const out = g0 ? roadSlice(g0, p * g0.total, g0.total) : [];
  for (let i = index + 1; i + 1 < path.length; i++) {
    const g = roadGeometry(path[i], path[i + 1]);
    if (!g) continue;
    if (out.length === 0) out.push(...g.pts);
    else out.push(...g.pts.slice(1));
  }
  return out;
}

/* 节点序列 → 沿路网的曲线（预览用） */
function routeWorldPath(ids) {
  const out = [];
  for (let i = 0; i + 1 < ids.length; i++) {
    const g = roadGeometry(ids[i], ids[i + 1]);
    if (!g) continue;
    if (out.length === 0) out.push(...g.pts);
    else out.push(...g.pts.slice(1));
  }
  return out;
}

/* ===== 粗曲线行军箭头 ===== */
function drawTrails() {
  for (const [armyId, points] of trailHistory) {
    if (points.length < 2) continue;
    const army = getArmy(armyId);
    const faction = army ? army.Faction : Faction.Player;
    const style = FACTION_STYLE[faction] || FACTION_STYLE[2];
    const smooth = catmullRomPath(trailWorldPath(points).map(p => w2s(p[0], p[1])));
    const len = polylineLength(smooth);
    if (len < 22) continue;

    ctx.save();
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    ctx.save();
    ctx.shadowColor = `rgba(${style.rgb},0.55)`;
    ctx.shadowBlur = 12;
    strokeTapered(smooth, 5.2, 7.8, style.color);
    ctx.restore();

    ctx.globalAlpha = 0.24;
    strokeTapered(smooth, 1.8, 2.6, '#ffffff');
    ctx.restore();

    const head = Math.max(14, Math.min(18, 10 + len * 0.015));
    // 箭头略向回收，避免正好被部队标记圆环盖住
    const seg = pointBackAlongPath(smooth, Math.min(len * 0.45, 30));
    if (seg) drawArrowHead([seg.back, seg.point], head, style.color, 0.97);
  }
}

/* 动画中部队的「正在走」轨迹：沿已走过的弧长逐步生长，落地后并入历史轨迹 */
function drawMoveTrails(now) {
  for (const [armyId, move] of marchAnim.moves) {
    const army = getArmy(armyId);
    const faction = army ? army.Faction : Faction.Player;
    const style = FACTION_STYLE[faction] || FACTION_STYLE[2];
    const pts = pathPrefix(move.pts, move.cum, moveEased(move, now) * move.total).map(p => w2s(p[0], p[1]));
    if (pts.length < 2) continue;

    ctx.save();
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    ctx.save();
    ctx.shadowColor = `rgba(${style.rgb},0.55)`;
    ctx.shadowBlur = 12;
    strokeTapered(pts, 5.2, 7.8, style.color);
    ctx.restore();

    ctx.globalAlpha = 0.24;
    strokeTapered(pts, 1.8, 2.6, '#ffffff');
    ctx.restore();

    drawArrowHead(pts, 13, style.color, 0.97);
  }
}

/* 从路径末端回退 dist 像素，返回落点与它前一个点（用于取切线） */
function pointBackAlongPath(path, dist) {
  let remain = dist;
  for (let i = path.length - 1; i > 0; i--) {
    const ax = path[i - 1][0], ay = path[i - 1][1];
    const bx = path[i][0], by = path[i][1];
    const d = Math.hypot(bx - ax, by - ay);
    if (d >= remain) {
      const t = 1 - remain / (d || 1);
      const back = t < 0.02 && i >= 2 ? path[i - 2] : [ax, ay];
      return { point: [ax + (bx - ax) * t, ay + (by - ay) * t], back };
    }
    remain -= d;
  }
  return null;
}

function drawPlannedPaths() {
  const now = performance.now();
  for (const army of world.Armies) {
    const path = army.PlannedPath || [];
    const index = Math.max(0, Math.min(army.PathIndex | 0, path.length - 1));
    if (path.length < 2 || index + 1 >= path.length) continue;

    const style = FACTION_STYLE[army.Faction] || FACTION_STYLE[2];
    const selected = army.Id === selectedArmyId;
    const raw = plannedWorldPath(army).map(p => w2s(p[0], p[1]));
    if (raw.length < 2) continue;

    const smooth = catmullRomPath(raw);
    const len = polylineLength(smooth);
    const alpha = selected ? 0.95 : 0.3;
    const head = Math.min(18, Math.max(8, len * 0.12), Math.max(7, len * 0.45));

    ctx.save();
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.setLineDash([13, 9]);
    ctx.lineDashOffset = -((now / 90) % 22);
    ctx.lineWidth = selected ? 4.6 : 3;
    ctx.strokeStyle = `rgba(${style.rgb},${alpha})`;
    if (selected) {
      ctx.shadowColor = `rgba(${style.rgb},0.6)`;
      ctx.shadowBlur = 12;
    }
    ctx.beginPath();
    tracePolyline(ctx, smooth);
    ctx.stroke();
    ctx.restore();

    drawArrowHead(smooth, head, style.color, alpha);
  }
}

/* 渐变收窄的粗线（逐段画，圆头衔接） */
function strokeTapered(path, w0, w1, color) {
  ctx.save();
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  ctx.strokeStyle = color;
  const n = path.length - 1;
  for (let i = 0; i < n; i++) {
    const t = n <= 1 ? 0 : i / n;
    ctx.lineWidth = w0 + (w1 - w0) * t;
    ctx.beginPath();
    ctx.moveTo(path[i][0], path[i][1]);
    ctx.lineTo(path[i + 1][0], path[i + 1][1]);
    ctx.stroke();
  }
  ctx.restore();
}

/* 大箭头：末端切线方向，尾部内凹 */
function drawArrowHead(path, size, color, alpha) {
  const n = path.length;
  if (n < 2 || size < 3) return;
  const tip = path[n - 1];
  let back = path[n - 2];
  for (let i = n - 2; i >= 0; i--) {
    if (Math.hypot(path[i][0] - tip[0], path[i][1] - tip[1]) >= size * 0.9) {
      back = path[i];
      break;
    }
  }
  const ang = Math.atan2(tip[1] - back[1], tip[0] - back[0]);
  if (!isFinite(ang)) return;

  const half = size * 0.56;
  const bx = tip[0] - Math.cos(ang) * size;
  const by = tip[1] - Math.sin(ang) * size;
  const px = Math.cos(ang + Math.PI / 2) * half;
  const py = Math.sin(ang + Math.PI / 2) * half;

  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.shadowColor = color;
  ctx.shadowBlur = 9;
  ctx.beginPath();
  ctx.moveTo(tip[0], tip[1]);
  ctx.lineTo(bx + px, by + py);
  ctx.quadraticCurveTo(bx, by, bx - px, by - py);
  ctx.closePath();
  ctx.fillStyle = color;
  ctx.fill();
  ctx.restore();
}

function drawPreview(now) {
  if (!selectedArmyId) return;

  if (previewPath.length >= 2) {
    const points = routeWorldPath(previewPath).map(p => w2s(p[0], p[1]));
    if (points.length >= 2) {
      ctx.save();
      ctx.setLineDash([10, 7]);
      ctx.lineDashOffset = -(now / 55) % 17;
      ctx.strokeStyle = 'rgba(120,230,255,0.9)';
      ctx.lineWidth = 2.6;
      ctx.lineJoin = 'round';
      ctx.shadowColor = 'rgba(87,212,255,0.55)';
      ctx.shadowBlur = 10;
      ctx.beginPath();
      points.forEach((p, i) => (i === 0 ? ctx.moveTo(p[0], p[1]) : ctx.lineTo(p[0], p[1])));
      ctx.stroke();
      ctx.restore();
    }
  }

  waypoints.forEach((id, i) => {
    const item = layout.byId.get(id);
    if (!item) return;
    ctx.save();
    ctx.beginPath();
    ctx.arc(item.sx, item.sy, 10, 0, TAU);
    ctx.fillStyle = 'rgba(8,34,45,0.95)';
    ctx.fill();
    ctx.lineWidth = 1.6;
    ctx.strokeStyle = 'rgba(120,230,255,0.9)';
    ctx.stroke();
    ctx.fillStyle = '#bff2ff';
    ctx.font = '700 11px "Microsoft YaHei", sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(String(i + 1), item.sx, item.sy + 0.5);
    ctx.restore();
  });
}

function drawNodes() {
  for (const item of layout.nodes) {
    const terrain = TERRAIN[item.node.Terrain] || TERRAIN[0];
    const hovered = hoverNodeId === item.node.Id;

    if (hovered) {
      ctx.beginPath();
      ctx.arc(item.sx, item.sy, 17, 0, TAU);
      ctx.strokeStyle = 'rgba(140,240,255,0.38)';
      ctx.lineWidth = 2;
      ctx.stroke();
    }

    // 深色底影 + 米白圆点 + 地形色芯
    ctx.beginPath();
    ctx.arc(item.sx, item.sy, 6.8, 0, TAU);
    ctx.fillStyle = 'rgba(18,22,16,0.55)';
    ctx.fill();

    ctx.beginPath();
    ctx.arc(item.sx, item.sy, 4.6, 0, TAU);
    ctx.fillStyle = 'rgba(248,242,224,0.95)';
    ctx.fill();
    ctx.lineWidth = 1.2;
    ctx.strokeStyle = hovered ? '#bff2ff' : 'rgba(54,60,46,0.75)';
    ctx.stroke();

    ctx.beginPath();
    ctx.arc(item.sx, item.sy, 1.9, 0, TAU);
    ctx.fillStyle = terrain.nodeFill;
    ctx.fill();
  }
}

function drawNodeLabels() {
  if (!layout.labels || !layout.labels.length) return;
  ctx.save();
  ctx.font = NODE_FONT;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.lineJoin = 'round';
  for (const lab of layout.labels) {
    const cx = lab.rect.x + lab.rect.w / 2;
    const cy = lab.rect.y + lab.rect.h / 2 + 0.5;
    ctx.lineWidth = 3.4;
    ctx.strokeStyle = 'rgba(26,30,20,0.82)';
    ctx.strokeText(lab.text, cx, cy);
    ctx.fillStyle = 'rgba(250,246,232,0.97)';
    ctx.fillText(lab.text, cx, cy);
  }
  ctx.restore();
}

function drawArmies(now) {
  for (const item of layout.armies) {
    const army = item.army;
    const style = FACTION_STYLE[army.Faction] || FACTION_STYLE[2];
    const selected = army.Id === selectedArmyId;
    const hovered = army.Id === hoverArmyId;
    const move = marchAnim.moves.get(army.Id);
    const status = armyStatus(army, now);

    // 动画中：部队在补间位置，名条跟随偏移，保持贴附
    let sx = item.sx, sy = item.sy, tagDx = 0, tagDy = 0;
    if (move) {
      const p = pointAtDistance(move.pts, move.cum, moveEased(move, now) * move.total);
      sx = p[0] * layout.scale + layout.ox;
      sy = p[1] * layout.scale + layout.oy;
      tagDx = sx - item.sx;
      tagDy = sy - item.sy;
    }

    // 正在移动（播放动画）：脉冲环强调；中途停走（有计划未播）：旋转虚线环
    if (move) {
      const k = 0.5 + 0.5 * Math.sin(now / 150);
      ctx.save();
      ctx.beginPath();
      ctx.arc(sx, sy, 18 + k * 7, 0, TAU);
      ctx.strokeStyle = `rgba(${style.rgb},${0.45 + k * 0.45})`;
      ctx.lineWidth = 2.6;
      ctx.stroke();
      ctx.restore();
    } else if (status.key === 'moving') {
      ctx.save();
      ctx.beginPath();
      ctx.arc(sx, sy, 19.5, now / 550, now / 550 + TAU * 0.7);
      ctx.setLineDash([3, 7]);
      ctx.strokeStyle = `rgba(${style.rgb},0.55)`;
      ctx.lineWidth = 1.6;
      ctx.stroke();
      ctx.restore();
    }

    if (selected) {
      // 粗描边 + 外发光 + 脉冲圈：选中极其明显
      const k = 0.5 + 0.5 * Math.sin(now / 260);
      ctx.save();
      ctx.beginPath();
      ctx.arc(sx, sy, 24 + k * 5, 0, TAU);
      ctx.lineWidth = 3.4;
      ctx.strokeStyle = `rgba(${style.rgb},${0.75 + k * 0.25})`;
      ctx.shadowColor = `rgba(${style.rgb},0.9)`;
      ctx.shadowBlur = 16;
      ctx.stroke();
      ctx.restore();

      ctx.beginPath();
      ctx.arc(sx, sy, 20, 0, TAU);
      ctx.lineWidth = 2;
      ctx.strokeStyle = 'rgba(255,255,255,0.92)';
      ctx.stroke();

      ctx.save();
      ctx.beginPath();
      ctx.arc(sx, sy, 29, -now / 900, -now / 900 + TAU * 0.72);
      ctx.setLineDash([4, 6]);
      ctx.strokeStyle = `rgba(${style.rgb},0.6)`;
      ctx.lineWidth = 1.4;
      ctx.stroke();
      ctx.restore();
    } else if (hovered) {
      ctx.beginPath();
      ctx.arc(sx, sy, 20, 0, TAU);
      ctx.strokeStyle = 'rgba(255,255,255,0.6)';
      ctx.lineWidth = 1.5;
      ctx.stroke();
    }

    ctx.save();
    ctx.shadowColor = `rgba(${style.rgb},0.7)`;
    ctx.shadowBlur = selected ? 24 : (move ? 18 : 11);
    ctx.beginPath();
    ctx.arc(sx, sy, 15, 0, TAU);
    ctx.fillStyle = style.color;
    ctx.fill();
    ctx.restore();

    ctx.beginPath();
    ctx.arc(sx, sy, 15, 0, TAU);
    ctx.lineWidth = 1.6;
    ctx.strokeStyle = 'rgba(255,255,255,0.85)';
    ctx.stroke();

    ctx.fillStyle = '#ffffff';
    ctx.font = '700 12px "Microsoft YaHei", sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(UNIT_MARK[army.UnitType] || '?', sx, sy + 0.5);

    if (item.tag) drawArmyTag(item, style, selected, hovered, status, tagDx, tagDy);
  }
}

/* 常显彩色军名条：圆角底 + 白色军名 + 状态/兵力小字 */
function drawArmyTag(item, style, selected, hovered, status, dx, dy) {
  const t = item.tag;
  const tx = t.x + (dx || 0), ty = t.y + (dy || 0);
  const mx = item.sx + (dx || 0), my = item.sy + (dy || 0);
  const st = status || { key: 'idle', ...STATUS_STYLE.idle };

  // 细线指向部队标记
  const px = Math.max(tx + 4, Math.min(tx + t.w - 4, mx));
  const py = Math.max(ty + 4, Math.min(ty + t.h - 4, my));
  ctx.save();
  ctx.strokeStyle = `rgba(${style.rgb},0.6)`;
  ctx.lineWidth = 1.2;
  ctx.beginPath();
  ctx.moveTo(px, py);
  ctx.lineTo(mx, my);
  ctx.stroke();
  ctx.restore();

  ctx.save();
  ctx.shadowColor = `rgba(${style.rgb},${selected ? 0.9 : 0.4})`;
  ctx.shadowBlur = selected ? 16 : 7;
  ctx.beginPath();
  roundedRect(ctx, tx, ty, t.w, t.h, 5);
  ctx.fillStyle = selected ? style.tagFillStrong : style.tagFill;
  ctx.fill();
  ctx.shadowBlur = 0;
  ctx.lineWidth = selected ? 2.4 : (hovered ? 1.8 : 1);
  ctx.strokeStyle = selected ? '#f2fbff'
    : (st.key === 'moving' ? 'rgba(255,195,107,0.85)'
      : (hovered ? 'rgba(255,255,255,0.92)' : 'rgba(255,255,255,0.5)'));
  ctx.stroke();

  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillStyle = '#ffffff';
  ctx.font = TAG_NAME_FONT;
  ctx.fillText(item.army.Name, tx + t.w / 2, ty + 11);

  // 第二行：状态（按状态着色）+ 兵力
  const numLabel = `${item.army.Strength} 兵`;
  ctx.font = TAG_SUB_FONT;
  const numW = ctx.measureText(numLabel).width;
  const stW = ctx.measureText(st.text).width;
  const gap = 5;
  const x0 = tx + t.w / 2 - (stW + gap + numW) / 2;
  ctx.textAlign = 'left';
  ctx.fillStyle = st.color;
  ctx.fillText(st.text, x0, ty + 23.5);
  ctx.fillStyle = 'rgba(255,255,255,0.86)';
  ctx.fillText(numLabel, x0 + stW + gap, ty + 23.5);
  ctx.restore();
}

function drawFlashes(now) {
  flashes = flashes.filter(f => now - f.t0 < 700);
  for (const f of flashes) {
    const k = (now - f.t0) / 700;
    ctx.save();

    if (f.fromX != null) {
      ctx.beginPath();
      ctx.setLineDash([4, 4]);
      ctx.moveTo(f.fromX, f.fromY);
      ctx.lineTo(f.sx, f.sy);
      ctx.strokeStyle = `rgba(120,230,255,${0.55 * (1 - k)})`;
      ctx.lineWidth = 1.4;
      ctx.stroke();
    }

    ctx.beginPath();
    ctx.arc(f.sx, f.sy, 13 + k * 26, 0, TAU);
    ctx.setLineDash([]);
    ctx.strokeStyle = `rgba(120,230,255,${0.9 * (1 - k)})`;
    ctx.lineWidth = 2;
    ctx.stroke();
    ctx.restore();
  }
}

function drawHoverTooltip() {
  if (!hoverArmyId) return;
  const item = layout.armies.find(it => it.army.Id === hoverArmyId);
  if (!item) return;
  const army = item.army;
  const style = FACTION_STYLE[army.Faction] || FACTION_STYLE[2];
  const text = `${army.Name} · ${UNIT_NAME[army.UnitType]} · ${army.Strength} 兵`;

  ctx.save();
  ctx.font = '12px "Microsoft YaHei", sans-serif';
  const width = ctx.measureText(text).width + 20;
  const height = 26;
  let x = item.sx - width / 2;
  let y = (item.tag ? item.tag.y : item.sy - 44) - 34;
  x = Math.max(8, Math.min(cssW - width - 8, x));
  y = Math.max(70, y);

  ctx.beginPath();
  roundedRect(ctx, x, y, width, height, 7);
  ctx.fillStyle = 'rgba(10,18,26,0.95)';
  ctx.fill();
  ctx.strokeStyle = `rgba(${style.rgb},0.6)`;
  ctx.lineWidth = 1;
  ctx.stroke();

  ctx.fillStyle = '#eaf5ff';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, x + width / 2, y + height / 2 + 0.5);
  ctx.restore();
}

function roundedRect(context, x, y, w, h, r) {
  context.moveTo(x + r, y);
  context.arcTo(x + w, y, x + w, y + h, r);
  context.arcTo(x + w, y + h, x, y + h, r);
  context.arcTo(x, y + h, x, y, r);
  context.arcTo(x, y, x + w, y, r);
  context.closePath();
}

/* ===== 数据访问 ===== */
async function api(path, body) {
  const options = { cache: 'no-store' };
  if (body !== undefined) {
    options.method = 'POST';
    options.headers = { 'Content-Type': 'application/json' };
    options.body = JSON.stringify(body);
  }
  const res = await fetch(path, options);
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return await res.json();
}

function getArmy(id) {
  return world ? world.Armies.find(a => a.Id === id) : null;
}

function nodeName(id) {
  const n = world && world.Map.Nodes.find(item => item.Id === id);
  return n ? n.Name : id;
}

function nodeTerrainName(id) {
  const n = world && world.Map.Nodes.find(item => item.Id === id);
  return n ? (TERRAIN[n.Terrain] || TERRAIN[0]).name : '';
}

function positionText(army) {
  const path = army.PlannedPath || [];
  const index = Math.max(0, Math.min(army.PathIndex | 0, path.length - 1));
  if (path.length === 0 || index + 1 >= path.length) {
    return `位于 ${nodeName(army.NodeId)}`;
  }
  const p = Math.round(Math.max(0, Math.min(1, army.EdgeProgress || 0)) * 100);
  return `行军中：${nodeName(path[index])} → ${nodeName(path[index + 1])}（${p}%）`;
}

/* ===== 交互 ===== */
function toLocal(ev) {
  const rect = canvas.getBoundingClientRect();
  return { x: ev.clientX - rect.left, y: ev.clientY - rect.top };
}

function pickArmy(x, y) {
  let best = null;
  let bestDist = 24;
  for (const item of layout.armies) {
    const dist = Math.hypot(item.sx - x, item.sy - y);
    if (dist <= bestDist) {
      bestDist = dist;
      best = item;
    }
  }
  if (best) return best;
  // 点击军名条也能选中部队
  for (const item of layout.armies) {
    const t = item.tag;
    if (t && x >= t.x && x <= t.x + t.w && y >= t.y && y <= t.y + t.h) return item;
  }
  return null;
}

function pickNode(x, y, threshold) {
  let best = null;
  let bestDist = threshold;
  for (const item of layout.nodes) {
    const dist = Math.hypot(item.sx - x, item.sy - y);
    if (dist <= bestDist) {
      bestDist = dist;
      best = item;
    }
  }
  return best;
}

/* 点击判定：标记离得太近时，节点中心优先，避免点节点误选贴着的部队 */
function pickAt(x, y) {
  let armyItem = null;
  let armyDist = 24;
  let tagItem = null;
  for (const item of layout.armies) {
    const dist = Math.hypot(item.sx - x, item.sy - y);
    if (dist <= armyDist) {
      armyDist = dist;
      armyItem = item;
    }
    const t = item.tag;
    if (t && x >= t.x && x <= t.x + t.w && y >= t.y && y <= t.y + t.h) tagItem = item;
  }
  const node = nearestNode(x, y);
  const nodeDist = node ? Math.hypot(node.sx - x, node.sy - y) : Infinity;
  if (node && nodeDist <= 14 && armyDist > nodeDist + 2) return { army: null, node };
  if (armyItem) return { army: armyItem, node: null };
  if (tagItem) return { army: tagItem, node: null };
  return { army: null, node };
}

function nearestNode(x, y) {
  let best = null;
  let bestDist = Infinity;
  for (const item of layout.nodes) {
    const dist = Math.hypot(item.sx - x, item.sy - y);
    if (dist < bestDist) {
      bestDist = dist;
      best = item;
    }
  }
  return best;
}

function flashNode(nodeItem, from) {
  flashes.push({
    sx: nodeItem.sx,
    sy: nodeItem.sy,
    fromX: from ? from.x : null,
    fromY: from ? from.y : null,
    t0: performance.now(),
  });
}

function onArmyClick(army) {
  if (army.Faction !== Faction.Player) {
    toast(`${army.Name} · ${UNIT_NAME[army.UnitType]} · ${army.Strength} 兵 · ${positionText(army)}`);
    return;
  }
  if (selectedArmyId === army.Id) {
    deselect();
    return;
  }
  selectedArmyId = army.Id;
  waypoints = [];
  previewPath = [];
  previewSeq++;
  hintShown = true;
  updateUI();
}

function onNodeClick(nodeItem, from) {
  flashNode(nodeItem, from);
  if (!selectedArmyId) {
    if (!hintShown) {
      hintShown = true;
      toast('先点选蓝色我方部队，再点地图节点下令', 'warn');
    }
    return;
  }
  addWaypoint(nodeItem.node.Id);
}

function addWaypoint(nodeId) {
  const army = getArmy(selectedArmyId);
  if (!army) {
    deselect();
    return;
  }
  if (waypoints.length && waypoints[waypoints.length - 1] === nodeId) {
    toast('路径末尾已经是该节点', 'warn');
    return;
  }
  if (!waypoints.length && nodeId === army.NodeId) {
    toast('该部队已在此节点，请点其他节点', 'warn');
    return;
  }
  waypoints.push(nodeId);
  refreshPreview();
}

async function refreshPreview() {
  if (!selectedArmyId || waypoints.length === 0) {
    previewPath = [];
    updateUI();
    return;
  }
  const seq = ++previewSeq;
  try {
    const res = await api('/api/preview', { ArmyId: selectedArmyId, Waypoints: waypoints.slice() });
    if (seq !== previewSeq) return;
    if (res.Code === ResolveResultCode.Ok && res.Path && res.Path.length > 0) {
      previewPath = res.Path;
    } else {
      previewPath = [];
      toast(CODE_MESSAGE[res.Code] || '该路线不可通行', 'warn');
      waypoints.pop();
      if (waypoints.length) {
        refreshPreview();
        return;
      }
    }
  } catch (err) {
    if (seq === previewSeq) {
      previewPath = [];
      toast(`路径预览失败：${err.message}`, 'bad');
    }
  }
  updateUI();
}

function undoWaypoint() {
  if (!waypoints.length) return;
  waypoints.pop();
  refreshPreview();
}

function clearWaypoints() {
  waypoints = [];
  previewPath = [];
  previewSeq++;
  updateUI();
}

function deselect() {
  selectedArmyId = null;
  clearWaypoints();
  updateUI();
}

function removeWaypointAt(index) {
  waypoints.splice(index, 1);
  refreshPreview();
}

/* ===== 请求动作 ===== */
async function issueOrder() {
  if (busy || !selectedArmyId || waypoints.length === 0) return;
  const army = getArmy(selectedArmyId);
  if (!army) return;

  const kind = waypoints.length > 1 ? OrderKind.MoveVia : OrderKind.MoveTo;
  const order = { ArmyId: selectedArmyId, Kind: kind, Waypoints: waypoints.slice() };
  const routeText = waypoints.map(id => nodeName(id)).join(' → ');

  busy = true;
  updateUI();
  try {
    const res = await api('/api/advance', { Orders: [order], Steps: 1 });
    if (res.Code === ResolveResultCode.Ok) {
      issuedOrder = { armyId: selectedArmyId, route: waypoints.slice() };
      waypoints = [];
      previewPath = [];
      encounterArmies.delete(selectedArmyId);
      toast(`${army.Name} 已下令：${routeText}，开始连续行军`);
      // 下令的第一步也纳入自动行军循环：暂停按钮立即可用，遇敌即停
      autoMarch.mode = 'order';
      autoMarch.armyId = selectedArmyId;
      autoMarch.steps = 0;
      await runAutoMarch('order', res);
    } else {
      commitResult(res);
      toast(CODE_MESSAGE[res.Code] || '下令失败', 'bad');
      if (res.Code === ResolveResultCode.NoPath || res.Code === ResolveResultCode.InvalidTarget) {
        previewPath = [];
      }
    }
  } catch (err) {
    toast(`下令失败：${err.message}`, 'bad');
  } finally {
    busy = false;
    updateUI();
  }
}

async function advanceStep() {
  if (busy || !world) return;
  const before = world.Time;
  busy = true;
  updateUI();
  try {
    const res = await api('/api/advance', { Orders: [], Steps: 1 });
    await playStepAnimation(res, STEP_MS);
    commitResult(res);
    if (!res.Encounters.length) {
      toast(res.State.Time > before
        ? `推进至时间步 ${res.State.Time}`
        : '所有部队已抵达或待命，时间未推进');
    }
  } catch (err) {
    toast(`推进失败：${err.message}`, 'bad');
  } finally {
    busy = false;
    updateUI();
  }
}

/* 快进 = 加速播放：每步短动画连续推进，遇敌/抵达停下，不瞬移糊满 */
async function fastForward() {
  if (busy || !world) return;
  if (!world.Armies.some(a => !isArmyIdle(a))) {
    toast('当前没有行军中的部队：先选中部队下令，再快进', 'warn');
    return;
  }
  busy = true;
  autoMarch.mode = 'ff';
  autoMarch.armyId = null;
  autoMarch.steps = 0;
  updateUI();
  await runAutoMarch('ff');
}

/*
 * 自动行军循环：逐步 advance + 短动画，直到抵达 / 遭遇 / 全部待命 / 玩家暂停 / 步数上限。
 * mode: 'order' 目标部队抵达即停；'ff' 全部待命才停。
 * firstRes: 下令那一刻已拿到的本步结果，直接播放，不再重复请求。
 */
async function runAutoMarch(mode, firstRes) {
  if (autoMarch.running) return;
  autoMarch.running = true;
  autoMarch.active = true;
  autoMarch.paused = false;
  const dur = mode === 'ff' ? FF_STEP_MS : STEP_MS;
  const gen = marchGeneration;
  let res = firstRes || null;
  try {
    while (autoMarch.active && !autoMarch.paused) {
      if (!world || gen !== marchGeneration) break;
      const beforeTime = world.Time;
      if (!res) {
        try {
          res = await api('/api/advance', { Orders: [], Steps: 1 });
        } catch (err) {
          toast(`行军失败：${err.message}`, 'bad');
          break;
        }
      }
      const stepRes = res;
      res = null;
      autoMarch.steps += 1;
      await playStepAnimation(stepRes, dur);
      if (gen !== marchGeneration) break;
      commitResult(stepRes);
      if (stepRes.Encounters.length) break; // 遭遇敌军：停下，战报已弹出
      if (stepRes.State.Time <= beforeTime) {
        toast('所有部队已抵达或待命，行军结束');
        break;
      }
      if (mode === 'order') {
        const army = getArmy(autoMarch.armyId);
        if (!army || isArmyIdle(army)) {
          if (army) toast(`${army.Name} 已抵达 ${nodeName(army.NodeId)}，待命中`);
          break;
        }
      }
      if (autoMarch.steps >= AUTO_STEP_CAP) {
        await jumpToResultState();
        break;
      }
    }
  } finally {
    autoMarch.running = false;
    if (!autoMarch.paused) autoMarch.active = false;
    busy = false;
    updateUI();
  }
}

/* 步数过多：直达结果态，并清晰标注停在哪、为什么停、走了多少步 */
async function jumpToResultState() {
  const beforeTime = world ? world.Time : 0;
  let res;
  try {
    res = await api('/api/fastforward', { Orders: [], MaxSteps: 2000 });
  } catch (err) {
    toast(`直达结果态失败：${err.message}`, 'bad');
    return;
  }
  commitResult(res);
  const total = autoMarch.steps + Math.max(0, res.State.Time - beforeTime);
  const reason = (res.Encounters && res.Encounters.length)
    ? `遭遇敌军（${res.Encounters.length} 场）`
    : '全部抵达/待命';
  toast(`已动画演出 ${autoMarch.steps} 步，剩余路程直达结果态：共 ${total} 步，停于时间步 ${res.State.Time}（${reason}）`, 'warn', 6000);
}

function commitResult(res) {
  const before = world;
  world = res.State;
  appendTrails(res.Trails || []);
  if (before) {
    for (const after of res.State.Armies) {
      const b = before.Armies.find(a => a.Id === after.Id);
      if (!b) continue;
      if (!isArmyIdle(b) && isArmyIdle(after)) lastArrival.set(after.Id, performance.now());
      if ((b.PlannedPath || []).join('|') !== (after.PlannedPath || []).join('|')) {
        encounterArmies.delete(after.Id);
      }
    }
  }
  if (res.Encounters) {
    for (const enc of res.Encounters) {
      for (const id of enc.ArmyIds || []) encounterArmies.add(id);
    }
  }
  computeLayout();
  previewPath = [];
  updateUI();
  flashClock();
  if (res.Encounters && res.Encounters.length) {
    showEncounterModal(res);
  }
}

function appendTrails(trails) {
  for (const trail of trails) {
    const points = trail.Points || [];
    if (!points.length) continue;

    let list = trailHistory.get(trail.ArmyId);
    if (!list) {
      list = [];
      trailHistory.set(trail.ArmyId, list);
    }

    for (const p of points) {
      const last = list[list.length - 1];
      if (last && Math.abs(last.X - p.X) < 1e-6 && Math.abs(last.Y - p.Y) < 1e-6) continue;
      list.push({ X: p.X, Y: p.Y, Time: p.Time });
    }
  }
}

function flashClock() {
  clockEl.classList.add('flash');
  setTimeout(() => clockEl.classList.remove('flash'), 420);
}

/* ===== UI ===== */
function updateUI() {
  timeValueEl.textContent = world ? String(world.Time) : '—';

  if (world) {
    const players = world.Armies.filter(a => a.Faction === Faction.Player);
    const enemies = world.Armies.filter(a => a.Faction === Faction.Enemy);
    const playerTotal = players.reduce((sum, a) => sum + a.Strength, 0);
    const enemyTotal = enemies.reduce((sum, a) => sum + a.Strength, 0);
    playerStatEl.textContent = `${players.length} 支 · ${playerTotal} 兵`;
    enemyStatEl.textContent = `${enemies.length} 支 · ${enemyTotal} 兵`;
  }

  const army = getArmy(selectedArmyId);
  if (!army) {
    selectedArmyId = null;
    selectedNameEl.textContent = '未选中部队';
    selectedDetailEl.textContent = '点选蓝色部队后，点击地图节点下令';
  } else {
    selectedNameEl.textContent = `${army.Name} · ${UNIT_NAME[army.UnitType]} · ${army.Strength} 兵`;
    selectedDetailEl.textContent = positionText(army);
  }

  renderWaypoints();

  btnOrder.disabled = busy || !army || waypoints.length === 0;
  btnClear.disabled = busy || waypoints.length === 0;
  btnStep.disabled = busy || !world;
  btnFastForward.disabled = busy || !world;
  btnReset.disabled = busy || !world;

  // 自动行军中的暂停/继续
  btnPause.hidden = !autoMarch.active;
  btnPause.disabled = !autoMarch.active;
  btnPause.textContent = autoMarch.paused ? '继续行军' : '暂停行军';

  // 命令区：已下令路线一览
  if (issuedOrder) {
    const ordered = getArmy(issuedOrder.armyId);
    const route = issuedOrder.route.map(id => nodeName(id)).join(' → ');
    const suffix = ordered ? (isArmyIdle(ordered) ? ' · 已抵达' : ' · 行军中') : '';
    orderBanner.textContent = `已下令：${route}${suffix}`;
    orderBanner.classList.remove('hidden');
  } else {
    orderBanner.classList.add('hidden');
  }

  if (army) {
    const lastNode = waypoints.length ? nodeName(waypoints[waypoints.length - 1]) : null;
    const label = waypoints.length > 1 ? `下令行军（${waypoints.length} 点折线）`
      : waypoints.length === 1 ? `下令行军（前往${lastNode}）` : '下令行军';
    btnOrder.textContent = label;
  } else {
    btnOrder.textContent = '下令行军';
  }
}

function renderWaypoints() {
  waypointListEl.innerHTML = '';
  waypoints.forEach((id, index) => {
    if (index > 0) {
      const arrow = document.createElement('span');
      arrow.className = 'wp-arrow';
      arrow.textContent = '›';
      waypointListEl.appendChild(arrow);
    }
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = 'wp-chip';
    chip.title = '点击移除该目标点';
    const badge = document.createElement('span');
    badge.className = 'idx';
    badge.textContent = String(index + 1);
    const label = document.createElement('span');
    label.textContent = `${nodeName(id)}·${nodeTerrainName(id)}`;
    chip.append(badge, label);
    chip.addEventListener('click', () => removeWaypointAt(index));
    waypointListEl.appendChild(chip);
  });
}

function toast(message, tone, duration) {
  const el = document.createElement('div');
  el.className = `toast${tone ? ` ${tone}` : ''}`;
  el.textContent = message;
  toastLayer.appendChild(el);
  requestAnimationFrame(() => el.classList.add('show'));
  const life = duration || 2600;
  setTimeout(() => {
    el.classList.remove('show');
    setTimeout(() => el.remove(), 260);
  }, life);
}

/* ===== 模态框 ===== */
function showModal({ title, subtitle, html, actions }) {
  modalLayer.innerHTML = '';

  const backdrop = document.createElement('div');
  backdrop.className = 'modal-backdrop';

  const modal = document.createElement('div');
  modal.className = 'modal';
  modal.setAttribute('role', 'dialog');
  modal.setAttribute('aria-modal', 'true');

  const head = document.createElement('div');
  head.className = 'modal-head';
  const titleEl = document.createElement('div');
  titleEl.className = 'modal-title';
  titleEl.textContent = title;
  head.appendChild(titleEl);
  if (subtitle) {
    const subEl = document.createElement('div');
    subEl.className = 'modal-sub';
    subEl.textContent = subtitle;
    head.appendChild(subEl);
  }

  const body = document.createElement('div');
  body.className = 'modal-body';
  body.innerHTML = html;

  const actionBar = document.createElement('div');
  actionBar.className = 'modal-actions';

  (actions || []).forEach(action => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'btn';
    if (action.primary) button.classList.add('primary');
    if (action.danger) button.classList.add('danger');
    button.textContent = action.label;
    button.addEventListener('click', () => {
      closeModal();
      if (action.onClick) action.onClick();
    });
    actionBar.appendChild(button);
  });

  backdrop.addEventListener('click', closeModal);
  modal.append(head, body, actionBar);
  modalLayer.append(backdrop, modal);
  modalLayer.classList.remove('hidden');
}

function closeModal() {
  modalLayer.classList.add('hidden');
  modalLayer.innerHTML = '';
}

function escapeHtml(text) {
  return String(text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function showEncounterModal(res) {
  const armies = new Map(res.State.Armies.map(a => [a.Id, a]));
  const nodeNameOf = id => {
    const node = res.State.Map.Nodes.find(n => n.Id === id);
    return node ? `${node.Name}（${(TERRAIN[node.Terrain] || TERRAIN[0]).name}）` : id;
  };

  const cards = res.Encounters.map(enc => {
    const ids = enc.ArmyIds || [];
    const players = ids.map(id => armies.get(id)).filter(a => a && a.Faction === Faction.Player);
    const enemies = ids.map(id => armies.get(id)).filter(a => a && a.Faction === Faction.Enemy);
    const lossByArmy = (enc.Loss && enc.Loss.LossByArmy) || {};

    const row = army => {
      const loss = lossByArmy[army.Id] || 0;
      return `<div class="loss-row">
        <span>${escapeHtml(army.Name)}<em>${UNIT_NAME[army.UnitType]}</em></span>
        <b class="loss">-${loss}</b>
        <span class="remain">余 ${army.Strength}</span>
      </div>`;
    };

    const ambusher = armies.get(enc.AmbusherId);
    const ambushLabel = enc.IsAmbush
      ? (ambusher && ambusher.Faction === Faction.Enemy ? '敌军伏击我方' : '我方伏击敌军')
      : '正面遭遇';

    const enemyLines = enemies.map(a =>
      `${escapeHtml(a.Name)} · ${UNIT_NAME[a.UnitType]} · ${a.Strength} 兵 · ${positionText(a)}`).join('<br>');

    return `<div class="encounter-card">
      <div class="enc-head">
        <span class="enc-place">${escapeHtml(nodeNameOf(enc.NodeId))}</span>
        <span class="enc-badge ${enc.IsAmbush ? '' : 'normal'}">${ambushLabel}</span>
        <span class="enc-time">时间步 ${enc.Time}</span>
      </div>
      <div class="enc-grid">
        <div>
          <div class="enc-col-title player">我方损失</div>
          ${players.map(row).join('') || '<div class="muted">无我方部队</div>'}
        </div>
        <div>
          <div class="enc-col-title enemy">敌方损失</div>
          ${enemies.map(row).join('') || '<div class="muted">无敌军部队</div>'}
        </div>
      </div>
      <div class="enc-enemy-list">敌军位置 / 规模：<br>${enemyLines || '—'}</div>
      <div class="enc-note">损失数字来自算法结算（EncounterEvent.Loss.LossByArmy）。关闭战报后由你重新点选部队、重新下令决定撤、绕还是顶。</div>
    </div>`;
  }).join('');

  showModal({
    title: `遭遇战报 · 时间步 ${res.State.Time}`,
    subtitle: res.Encounters.length > 1
      ? `本步触发 ${res.Encounters.length} 场遭遇，时间已停在事发点`
      : '时间已停在事发点',
    html: cards,
    actions: [{ label: '知道了，关闭战报', primary: true }],
  });
}

function confirmReset() {
  if (busy) return;
  showModal({
    title: '重开本局',
    subtitle: '世界回到初始状态：18 节点 / 29 条边 / 9 支部队',
    html: '<div class="confirm-text">当前时间步与所有行军轨迹都会清空，兵力恢复初始值。<div class="muted">地图与兵力将从 01_地图数据 的 JSON 重新读取。</div></div>',
    actions: [
      { label: '取消' },
      { label: '确认重开', primary: true, onClick: doReset },
    ],
  });
}

async function doReset() {
  busy = true;
  updateUI();
  try {
    const fresh = await api('/api/reset', {});
    world = fresh;
    trailHistory = new Map();
    selectedArmyId = null;
    waypoints = [];
    previewPath = [];
    previewSeq++;
    flashes = [];
    marchAnim.moves = new Map();
    if (marchAnim.onDone) {
      const done = marchAnim.onDone;
      marchAnim.onDone = null;
      done();
    }
    marchGeneration++;
    autoMarch.active = false;
    autoMarch.paused = false;
    autoMarch.running = false;
    autoMarch.steps = 0;
    autoMarch.armyId = null;
    lastArrival.clear();
    encounterArmies.clear();
    issuedOrder = null;
    computeLayout();
    needTerrain = true;
    updateUI();
    toast('已回到初始状态');
  } catch (err) {
    toast(`重开失败：${err.message}`, 'bad');
  } finally {
    busy = false;
    updateUI();
  }
}

/* ===== 事件绑定 ===== */
canvas.addEventListener('pointermove', ev => {
  const { x, y } = toLocal(ev);
  const hit = pickAt(x, y);
  hoverArmyId = hit.army ? hit.army.army.Id : null;
  hoverNodeId = hit.node ? hit.node.node.Id : null;
  canvas.style.cursor = hit.army || (hit.node && Math.hypot(hit.node.sx - x, hit.node.sy - y) < 26) ? 'pointer' : 'crosshair';
});

canvas.addEventListener('pointerleave', () => {
  hoverArmyId = null;
  hoverNodeId = null;
});

canvas.addEventListener('click', ev => {
  if (ev.button !== 0) return;
  const local = toLocal(ev);
  const hit = pickAt(local.x, local.y);
  if (hit.army) {
    onArmyClick(hit.army.army);
    return;
  }
  if (hit.node) onNodeClick(hit.node, local);
});

canvas.addEventListener('contextmenu', ev => {
  ev.preventDefault();
  undoWaypoint();
});

btnOrder.addEventListener('click', issueOrder);
btnClear.addEventListener('click', clearWaypoints);
btnStep.addEventListener('click', advanceStep);
btnFastForward.addEventListener('click', fastForward);
btnReset.addEventListener('click', confirmReset);

btnPause.addEventListener('click', () => {
  if (!autoMarch.active) return;
  if (autoMarch.paused) {
    autoMarch.paused = false;
    runAutoMarch(autoMarch.mode);
  } else {
    autoMarch.paused = true;
    toast('已暂停行军，随时可以继续');
  }
  updateUI();
});

document.querySelector('.panel.help').addEventListener('toggle', () => {
  if (!world) return;
  layoutNodeLabels();
  layoutArmyTags();
});

window.addEventListener('keydown', ev => {
  if (!modalLayer.classList.contains('hidden')) {
    if (ev.key === 'Escape') {
      ev.preventDefault();
      closeModal();
    }
    return;
  }
  if (ev.key === 'Enter') {
    ev.preventDefault();
    issueOrder();
  } else if (ev.key === 'Escape') {
    if (waypoints.length) clearWaypoints();
    else if (selectedArmyId) deselect();
  } else if (ev.key === 'Backspace') {
    if (waypoints.length) {
      ev.preventDefault();
      undoWaypoint();
    }
  }
});

window.addEventListener('resize', resize);

/* ===== 启动 ===== */
function tick(now) {
  // 补间动画播放完毕 → 解锁等待中的自动行军/推进流程
  if (marchAnim.moves.size) {
    let done = true;
    for (const m of marchAnim.moves.values()) {
      if (now - m.t0 < m.dur) { done = false; break; }
    }
    if (done) {
      marchAnim.moves = new Map();
      const cb = marchAnim.onDone;
      marchAnim.onDone = null;
      if (cb) cb();
    }
  }
  draw(now);
  requestAnimationFrame(tick);
}

async function boot() {
  resize();
  requestAnimationFrame(tick);
  try {
    world = await api('/api/world');
    computeLayout();
    needTerrain = true;
    updateUI();
  } catch (err) {
    loadError = `无法载入世界数据（${err.message}），请确认后端 /api/world`;
    toast(loadError, 'bad', 8000);
  }
}

boot();
