/**
 * 主程序 —— 原型入口
 *
 * 阶段 B/C/D 合并实现：
 * - 3D 战场渲染（地形 + 装饰物）
 * - 双视角切换（战略俯视 / 个人贴地）
 * - 部队 LOD 三层渲染
 * - 逐回合战斗推进（接combat.ts 数值模型）
 */

import * as THREE from 'three';
import { Battlefield } from './render/battlefield';
import { ArmyRenderer, type Army } from './render/army';
import { UnitRenderer } from './render/units';
import { makeBanner } from './render/models';
import { MAP_SIZE } from './render/terrain';
import { createBattle, formationBreakdown, stepTurn, type BattleState } from './game/battle';
import { stepEngagement, orderAttack, orderDisengage, battleStats, isRouting } from './game/engagement';
import { findPath, makeGrid, simplifyPath } from './game/pathfinding';
import { EconPanel } from './ui/econ-panel';
import { detectWebGL, describeRenderFailure } from './render/capability';
import { Canvas2DRenderer } from './render/canvas2d';
import {
  createUnit,
  orderGroupMove,
  orderGroupStop,
  selectAll,
  unitsInRect,
  type ControllableUnit,
  type SelectionRect,
} from './game/unit';

type ViewMode = 'strategic' | 'tactical';

// ---------- DOM ----------
const $ = (id: string) => document.getElementById(id)!;
const hud = {
  turn: $('hud-turn'),
  view: $('hud-view'),
  troops: $('hud-troops'),
  enemy: $('hud-enemy'),
  turn2: $('hud-turn2'),
  state: $('hud-state'),
  casA: $('hud-cas-a'),
  casD: $('hud-cas-d'),
  sel: $('hud-sel'),
  liveA: $('hud-live-a'),
  liveD: $('hud-live-d'),
  engaged: $('hud-engaged'),
  routed: $('hud-routed'),
  fps: $('fps'),
  log: $('log'),
};

// ---------- 初始化 ----------
const app = $('app');

// 渲染能力检测：无 WebGL 时降级到 Canvas 2D 软件渲染
const renderMode = detectWebGL();
let renderer: THREE.WebGLRenderer | null = null;
let fallback: Canvas2DRenderer | null = null;
if (renderMode) {
  renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.setSize(window.innerWidth, window.innerHeight);
  app.appendChild(renderer.domElement);
} else {
  showRenderWarning(describeRenderFailure());
}

const camera = new THREE.PerspectiveCamera(
  55,
  window.innerWidth / window.innerHeight,
  0.5,
  6000,
);
// 相机初始位置必须显式设置——Three.js 默认是 (0,0,0)，
// 与 camTarget 同点会导致相机贴着自己脸看，全屏黑（实测缺陷）。
camera.position.set(0, 900, 0.001);
camera.lookAt(0, 0, 0);

let battle: BattleState;
let bf: Battlefield;
let armies: ArmyRenderer;
let unitRend: UnitRenderer;
let units: ControllableUnit[] = [];
let view: ViewMode = 'strategic';

/** 渲染降级提示条 */
function showRenderWarning(reason: string) {
  const bar = document.createElement('div');
  bar.id = 'render-warning';
  bar.style.cssText = [
    'position:absolute;top:0;left:0;right:0;z-index:100',
    'background:linear-gradient(90deg,#9a3412,#b45309);color:#fff',
    'padding:6px 14px;font-size:12px;line-height:1.6',
    'border-bottom:1px solid #7c2d12',
  ].join(';');
  bar.innerHTML =
    `<b>已切换到软件渲染模式</b>：本机浏览器未提供 WebGL（${reason}）。` +
    `战场改用俯视 2D 战棋视图显示，玩法与 3D 版完全一致。` +
    `<span style="opacity:.8">如需 3D 效果，请在本机浏览器直接打开开发服务器地址。</span>`;
  document.body.appendChild(bar);
  // 把 HUD 下移，避开警告条
  const hudEl = document.getElementById('hud');
  if (hudEl) hudEl.style.top = '42px';
  const help = document.getElementById('help');
  if (help) help.style.top = '172px';
  const econ = document.getElementById('econ');
  if (econ) econ.style.top = '172px';
  // 隐藏仅 3D 模式成立的按键说明
  const nav = document.getElementById('help-nav');
  if (nav) nav.style.display = 'none';
  // 隐藏视角切换按钮
  const vs = document.getElementById('view-switch');
  if (vs) {
    const strategic = document.getElementById('btn-strategic');
    const tactical = document.getElementById('btn-tactical');
    if (strategic) strategic.style.display = 'none';
    if (tactical) tactical.style.display = 'none';
  }
}

// 框选状态
let selecting = false;
let selectStart: { x: number; y: number } | null = null;
let selectRect: SelectionRect | null = null;

// 相机状态
const camTarget = new THREE.Vector3(0, 0, 0);
let camDist = 1400;
let camHeight = 900;

// ---------- 视角控制 ----------
function setView(mode: ViewMode) {
  if (!renderMode) {
    addLog('软件渲染模式固定为俯视视角，无法切换 3D 视角。');
    return;
  }
  view = mode;
  hud.view.textContent = mode === 'strategic' ? '战略' : '个人';
  $('btn-strategic').classList.toggle('active', mode === 'strategic');
  $('btn-tactical').classList.toggle('active', mode === 'tactical');
  if (mode === 'tactical') {
    camDist = 60;
    camHeight = 38;
  } else {
    camDist = 1400;
    camHeight = 900;
  }
}

// ---------- 战场构建 ----------
function buildBattle() {
  // WebGL 模式：释放旧的 3D 资源
  if (renderMode) {
    if (armies) {
      armies.dispose();
      bf.scene.remove(armies.group);
    }
    if (unitRend) {
      unitRend.dispose();
      bf.scene.remove(unitRend.group);
    }
    if (bf) bf.dispose();
  }

  // 软件渲染模式：只重建地形数据与战斗状态
  bf = new Battlefield(20261005);
  battle = createBattle();

  if (renderMode) {
    armies = new ArmyRenderer(bf.terrain);
    bf.scene.add(armies.group);
  } else {
    armies = null as unknown as ArmyRenderer;
    fallback = new Canvas2DRenderer({ terrain: bf.terrain, mapSize: MAP_SIZE });
    app.appendChild(fallback.canvas);
    // 复用同一个 canvas，避免多次重置触发重复插入
    const olds = app.querySelectorAll('canvas');
    if (olds.length > 1) olds[0].remove();
  }

  // 可操控单位：从编制拆分出代表单位（每编制 3 个战术单位）
  units = [];
  const perFormation = 3;
  for (const side of ['blue', 'red'] as const) {
    const list = side === 'blue' ? battle.blue : battle.red;
    for (const f of list) {
      for (let i = 0; i < perFormation; i++) {
        const a = (i / perFormation) * Math.PI * 2;
        const r = 60;
        const x = f.x + Math.cos(a) * r;
        const z = f.z + Math.sin(a) * r;
        units.push(
          createUnit(
            f.cls,
            x,
            z,
            f.heading,
            Math.round(f.count / perFormation),
            side,
          ),
        );
      }
    }
  }

  if (renderMode) {
    unitRend = new UnitRenderer(bf.terrain);
    bf.scene.add(unitRend.group);

    // 双方军旗
    const flagB = makeBanner('blue');
    flagB.position.set(-760, bf.terrain.heightAt(-760, 0), 0);
    bf.scene.add(flagB);
    const flagR = makeBanner('red');
    flagR.position.set(760, bf.terrain.heightAt(760, 0), 0);
    bf.scene.add(flagR);
  } else {
    unitRend = null as unknown as UnitRenderer;
  }

  camTarget.set(0, 20, 0);
  if (renderMode) {
    rebuildArmies();
  }
  updateHud();
  hud.log.innerHTML = '';
  for (const line of battle.log) addLog(line);
}

function toArmies(s: BattleState): Army[] {
  return [
    { side: 'blue', formations: s.blue },
    { side: 'red', formations: s.red },
  ];
}

function rebuildArmies() {
  if (!renderMode || !armies || !battle) return;
  armies.rebuild(toArmies(battle));
}

/** 同步单位到渲染层（WebGL 与软件渲染分流） */
function syncUnits() {
  if (renderMode) {
    if (econPanel) unitRend.setCombatRate(econPanel.mods().combatRate);
    syncUnits();
  } else {
    fallback?.render(units, selectRect);
  }
}

// ---------- HUD ----------
function addLog(text: string) {
  const d = document.createElement('div');
  d.textContent = text;
  hud.log.appendChild(d);
  hud.log.scrollTop = hud.log.scrollHeight;
}

function updateHud() {
  const blueTotal = battle.blue.reduce((a, f) => a + f.count, 0);
  const redTotal = battle.red.reduce((a, f) => a + f.count, 0);
  hud.turn.textContent = `第 ${battle.turn} 回合`;
  hud.troops.textContent = `${Math.round(blueTotal)}（${battle.blue.length} 编制）`;
  hud.enemy.textContent = `${Math.round(redTotal)}（${battle.red.length} 编制）`;
  hud.casA.textContent = String(Math.round(battle.blueCasualties));
  hud.casD.textContent = String(Math.round(battle.redCasualties));
  hud.sel.textContent = String(units.filter((u) => u.selected).length);

  // 实时交战统计（来自 unit 层，与编制层的战斗状态机并行）
  const st = battleStats(units);
  hud.liveA.textContent = String(Math.round(st.blue));
  hud.liveD.textContent = String(Math.round(st.red));
  hud.engaged.textContent = String(st.engaged);
  const rout = st.blueRouting + st.redRouting;
  hud.routed.textContent = String(rout);
  hud.routed.className = rout > 0 ? 'val warn' : 'val';

  const stateMap = {
    ongoing: '交战中',
    blue_win: '我军取胜',
    red_win: '我军战败',
    draw: '双方溃散',
  } as const;
  hud.state.textContent = stateMap[battle.result];
  hud.turn2.textContent = String(battle.turn);
  hud.state.className = battle.result === 'blue_win' ? 'val ok'
    : battle.result === 'red_win' ? 'val warn'
    : 'val';
}

// ---------- 交互 ----------
$('btn-strategic').onclick = () => setView('strategic');
$('btn-tactical').onclick = () => setView('tactical');
$('btn-step').onclick = () => advanceTurn();
$('btn-reset').onclick = () => buildBattle();

function advanceTurn() {
  if (battle.result !== 'ongoing') return;
  const before = battle.log.length;
  battle = stepTurn(battle, bf.terrain);
  // 同步可操控单位的兵力
  syncUnitStrength();
  rebuildArmies();
  updateHud();
  for (let i = before; i < battle.log.length; i++) addLog(battle.log[i]);
}

function syncUnitStrength() {
  const all = [...battle.blue, ...battle.red];
  for (const u of units) {
    const f = all.find((x) => x.cls === u.cls && (x.x > -900 ? 'blue' : 'red') === u.side);
    if (f) u.strength = Math.round(f.count / 3);
  }
}

// 框选层
const selLayer = document.createElement('div');
selLayer.style.cssText =
  'position:absolute;border:1px solid #ffe066;background:rgba(255,224,102,0.12);pointer-events:none;display:none;z-index:10;';
app.appendChild(selLayer);

app.addEventListener('mousedown', (e) => {
  if (e.button === 0) {
    selecting = true;
    selectStart = { x: e.clientX, y: e.clientY };
    selectRect = null;
  } else if (e.button === 2) {
    e.preventDefault();
    issueMoveOrder(e.clientX, e.clientY);
  } else if (e.button === 1) {
    e.preventDefault();
    issueAttackOrder(e.clientX, e.clientY);
  }
});

window.addEventListener('mouseup', (e) => {
  if (e.button === 0 && selecting) {
    selecting = false;
    finishSelection();
  }
});

window.addEventListener('mousemove', (e) => {
  if (selecting && selectStart) {
    selectRect = {
      x0: selectStart.x,
      y0: selectStart.y,
      x1: e.clientX,
      y1: e.clientY,
    };
    updateSelLayer();
  }
});

function updateSelLayer() {
  if (!selectRect) {
    selLayer.style.display = 'none';
    return;
  }
  const x = Math.min(selectRect.x0, selectRect.x1);
  const y = Math.min(selectRect.y0, selectRect.y1);
  const w = Math.abs(selectRect.x1 - selectRect.x0);
  const h = Math.abs(selectRect.y1 - selectRect.y0);
  selLayer.style.display = 'block';
  selLayer.style.left = `${x}px`;
  selLayer.style.top = `${y}px`;
  selLayer.style.width = `${w}px`;
  selLayer.style.height = `${h}px`;
}

/**
 * 软件渲染模式下的框选：把屏幕矩形四角转成世界矩形，
 * 用世界坐标做矩形相交（俯视图是等距投影，屏幕矩形 = 世界矩形）
 */
function unitsInWorldRect(
  list: ControllableUnit[],
  rect: SelectionRect,
  side?: 'blue' | 'red',
): ControllableUnit[] {
  if (!fallback) return [];
  const a = fallback.screenToWorld(rect.x0, rect.y0);
  const b = fallback.screenToWorld(rect.x1, rect.y1);
  const minX = Math.min(a.x, b.x);
  const maxX = Math.max(a.x, b.x);
  const minZ = Math.min(a.z, b.z);
  const maxZ = Math.max(a.z, b.z);
  return list.filter((u) => {
    if (side && u.side !== side) return false;
    return u.x >= minX && u.x <= maxX && u.z >= minZ && u.z <= maxZ;
  });
}

function finishSelection() {
  selLayer.style.display = 'none';
  if (!selectRect) {
    // 单击：清空选择
    units = selectAll(units, false);
    syncUnits();
    return;
  }
  const w = Math.abs(selectRect.x1 - selectRect.x0);
  const h = Math.abs(selectRect.y1 - selectRect.y0);
  if (w < 5 && h < 5) {
    // 视作单击，清空选择
    units = selectAll(units, false);
    syncUnits();
    return;
  }
  // WebGL 用屏幕投影筛；软件渲染直接用世界坐标筛
  const hit = renderMode
    ? unitsInRect(
        units,
        selectRect,
        camera,
        window.innerWidth,
        window.innerHeight,
        bf.terrain,
        'blue',
      )
    : unitsInWorldRect(units, selectRect, 'blue');
  // 框选只影响我方（加选：按住 Shift 才会替换为新选）
  const keep = shiftHeld ? units.filter((u) => u.side === 'blue' && u.selected) : [];
  const hitIds = new Set(hit.map((u) => u.id));
  units = units.map((u) => {
    if (u.side !== 'blue') return u;
    if (hitIds.has(u.id)) return { ...u, selected: true };
    if (keep.some((k) => k.id === u.id)) return u;
    return { ...u, selected: false };
  });
  syncUnits();
  addLog(`选中 ${hit.length} 个单位。`);
}

let shiftHeld = false;
window.addEventListener('keydown', (e) => { if (e.key === 'Shift') shiftHeld = true; });
window.addEventListener('keyup', (e) => { if (e.key === 'Shift') shiftHeld = false; });

/** 把屏幕坐标点转为世界坐标 */
function screenToGround(sx: number, sy: number): { x: number; z: number } | null {
  // 软件渲染模式：直接用 2D 变换
  if (!renderMode) {
    return fallback ? fallback.screenToWorld(sx, sy) : null;
  }
  const ndc = new THREE.Vector2(
    (sx / window.innerWidth) * 2 - 1,
    -(sy / window.innerHeight) * 2 + 1,
  );
  const ray = new THREE.Raycaster();
  ray.setFromCamera(ndc, camera);
  // 地形网格在 bf.ground
  const hits = ray.intersectObject(bf.ground, false);
  if (hits.length > 0) {
    return { x: hits[0].point.x, z: hits[0].point.z };
  }
  // 兜底：与 y=0 平面求交
  const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
  const pt = new THREE.Vector3();
  return ray.ray.intersectPlane(plane, pt) ? { x: pt.x, z: pt.z } : null;
}

function issueMoveOrder(sx: number, sy: number) {
  const hasSel = units.some((u) => u.selected);
  if (!hasSel) {
    addLog('未选中任何单位。先用左键框选我方部队。');
    return;
  }
  const p = screenToGround(sx, sy);
  if (!p) return;
  units = orderGroupMove(units, p.x, p.z);
  syncUnits();
  addLog(`已下令移动至 (${p.x.toFixed(0)}, ${p.z.toFixed(0)})。`);
}

/** 找到离某点最近的敌方单位（用于右键攻击） */
function findEnemyNear(p: { x: number; z: number }, maxDist: number) {
  let best: ControllableUnit | null = null;
  let bestD = maxDist;
  for (const u of units) {
    if (u.side === 'blue') continue;
    if (isRouting(u)) continue;
    const d = Math.hypot(u.x - p.x, u.z - p.z);
    if (d < bestD) {
      best = u;
      bestD = d;
    }
  }
  return best;
}

function issueAttackOrder(sx: number, sy: number) {
  const hasSel = units.some((u) => u.selected);
  if (!hasSel) {
    addLog('未选中任何单位。先用左键框选我方部队。');
    return;
  }
  const p = screenToGround(sx, sy);
  if (!p) return;
  // 该位置附近60 米内的敌军 → 攻击
  const foe = findEnemyNear(p, 60);
  if (foe) {
    units = orderAttack(units, foe.id);
    syncUnits();
    addLog(`已下令攻击敌方单位（${foe.cls === 'infantry' ? '步兵' : foe.cls === 'archer' ? '弓兵' : '骑兵'}）。`);
  } else {
    units = orderGroupMove(units, p.x, p.z);
    syncUnits();
    addLog(`该处无敌军，已下令移动至 (${p.x.toFixed(0)}, ${p.z.toFixed(0)})。`);
  }
}

app.addEventListener('contextmenu', (e) => e.preventDefault());

app.addEventListener('wheel', (e) => {
  e.preventDefault();
  const f = e.deltaY > 0 ? 1.12 : 0.89;
  if (view === 'strategic') {
    camDist = Math.max(200, Math.min(3000, camDist * f));
    camHeight = camDist * 0.64;
  } else {
    camDist = Math.max(12, Math.min(220, camDist * f));
    camHeight = camDist * 0.6;
  }
}, { passive: false });

// 键盘：WASD 平移视角，V 切视角，空格推进回合，R 重置
const keys = new Set<string>();
window.addEventListener('keydown', (e) => {
  if (e.code === 'Space') {
    e.preventDefault();
    advanceTurn();
  } else if (e.code === 'KeyV') {
    setView(view === 'strategic' ? 'tactical' : 'strategic');
  } else if (e.code === 'KeyR') {
    buildBattle();
  } else if (e.code === 'KeyX') {
    // X 停止移动并脱离战斗
    units = orderGroupStop(units);
    units = orderDisengage(units);
    syncUnits();
    addLog('已下令停止并脱离战斗。');
  } else {
    keys.add(e.code);
  }
});
window.addEventListener('keyup', (e) => keys.delete(e.code));

window.addEventListener('resize', () => {
  if (renderMode) {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer!.setSize(window.innerWidth, window.innerHeight);
  } else {
    fallback?.resize();
  }
});

// ---------- 渲染循环 ----------
let lastTime = performance.now();
let fpsAccum = 0;
let fpsFrames = 0;
let engTimeAccum = 0;
let pathTimer = 0;
let pathGrid: ReturnType<typeof makeGrid> | null = null;
let econPanel: EconPanel;

/** 为所有有移动目标的单位重算路径（带每帧预算，避免主循环卡顿） */
function recomputePaths(list: ControllableUnit[]): ControllableUnit[] {
  if (!pathGrid) pathGrid = makeGrid(MAP_SIZE);
  const g = pathGrid;
  const t0 = performance.now();
  const budget = 6; // 每帧最多 6 毫秒，超时则本帧少算几个
  let done = 0;
  return list.map((u) => {
    if (u.targetX === null || u.targetZ === null) return u;
    // 交战中的单位不重算（它们在追人，直线更合适）
    if (u.engageTargetId !== null) return u;
    // 已经算好路径且目标没变，跳过
    if (u.path && u.path.length > 0) return u;
    if (performance.now() - t0 > budget) return u;
    done++;
    const raw = findPath(bf.terrain, g, u.x, u.z, u.targetX, u.targetZ, u.cls);
    if (!raw) return { ...u, path: null };
    return { ...u, path: simplifyPath(raw, u.x, u.z, bf.terrain, g, u.cls) };
  });
}

function loop() {
  const now = performance.now();
  const dt = Math.min(0.1, (now - lastTime) / 1000);
  lastTime = now;

  // WASD 平移视角
  const panSpeed = view === 'strategic' ? 900 : 200;
  let panX = 0;
  let panZ = 0;
  if (keys.has('KeyW')) panZ -= 1;
  if (keys.has('KeyS')) panZ += 1;
  if (keys.has('KeyA')) panX -= 1;
  if (keys.has('KeyD')) panX += 1;
  if (panX !== 0 || panZ !== 0) {
    const len = Math.hypot(panX, panZ);
    const step = (panSpeed * dt) / len;
    camTarget.x = Math.max(-MAP_SIZE / 2, Math.min(MAP_SIZE / 2, camTarget.x + (panX / len) * step));
    camTarget.z = Math.max(-MAP_SIZE / 2, Math.min(MAP_SIZE / 2, camTarget.z + (panZ / len) * step));
  }

  // 推进单位移动与交战（交战会驱动自动索敌、逼近、伤害结算）
  const eng = stepEngagement(units, bf.terrain, dt);
  units = eng.units;
  // 人才带来的战斗加成（都尉/亲卫任职时提升我方单位视觉尺度）
  if (econPanel) unitRend.setCombatRate(econPanel.mods().combatRate);
  // 周期性重算路径（每1.2 秒一次，避免每帧重算的开销）
  pathTimer += dt;
  if (pathTimer > 1.2) {
    pathTimer = 0;
    units = recomputePaths(units);
  }
  syncUnits();
  // 交战事件写日志（限流，避免刷屏）
  if (eng.events.length > 0) {
    engTimeAccum += dt;
    if (engTimeAccum > 1.5) {
      engTimeAccum = 0;
      for (const e of eng.events.slice(0, 2)) addLog(e);
    }
  }

  // 相机跟随（仅 WebGL 模式；软件渲染是固定俯视）
  if (renderMode) {
    const groundY = bf.terrain.heightAt(camTarget.x, camTarget.z);
    const targetY = groundY + (view === 'strategic' ? 40 : 2);
    const k = Math.min(1, dt / 0.12);
    camera.position.x += (camTarget.x - camera.position.x) * k;
    camera.position.z += (camTarget.z - camera.position.z) * k;
    camera.position.y += (targetY + camHeight - camera.position.y) * k;
    camera.lookAt(camTarget.x, targetY, camTarget.z);
    renderer!.render(bf.scene, camera);
  } else {
    fallback?.render(units, selectRect);
  }

  // FPS
  fpsAccum += dt * 1000;
  fpsFrames++;
  if (fpsAccum >= 500) {
    const fps = Math.round((fpsFrames * 1000) / fpsAccum);
    const sel = units.filter((u) => u.selected).length;
    if (renderMode) {
      const st = armies.stats();
      hud.fps.textContent = `${fps} FPS | 单位 ${units.length}（选中 ${sel}）| LOD0 ${st.l0} LOD1 ${st.l1} LOD2 ${st.l2}`;
    } else {
      hud.fps.textContent = `${fps} FPS | 软件渲染 | 单位 ${units.length}（选中 ${sel}）`;
    }
    fpsAccum = 0;
    fpsFrames = 0;
  }

  requestAnimationFrame(loop);
}

// ---------- 启动 ----------
buildBattle();
econPanel = new EconPanel(addLog);
setView('strategic');
loop();

// 暴露给控制台便于调试
(window as unknown as Record<string, unknown>).game = {
  get battle() { return battle; },
  get armies() { return armies; },
  get bf() { return bf; },
  breakdown: () => ({
    blue: formationBreakdown(battle.blue),
    red: formationBreakdown(battle.red),
  }),
  setView,
  buildBattle,
};