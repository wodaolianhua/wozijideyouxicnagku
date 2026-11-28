'use strict';

// 枚举数值与《03_表现层_2D挖矿/接口契约_共享类型/Contracts.ts》严格一致（契约唯一权威）。
const TileType = { Air: 0, Dirt: 1, Stone: 2, Bedrock: 3, Ore: 4 };
const Direction = { Up: 0, Down: 1, Left: 2, Right: 3 };
const ActionKind = { Move: 0, Dig: 1, Sell: 2, BuyTool: 3, HireWorker: 4, GoSurface: 5, Polish: 6 };
const ToolKind = { BareHands: 0, Pickaxe: 1, Drill: 2, Excavator: 3 };
const WorkerKind = { Robot: 0, HiredMiner: 1 };

const MineralName = ['泥土', '煤', '铁', '铜', '银', '金', '稀有晶体'];
const MineralColor = ['#8a6a4a', '#4a4a4a', '#a9b2bc', '#d07a3a', '#dfe7ef', '#f0c94b', '#b77cff'];
const GradeName = ['普通', '良好', '精良', '卓越', '完美'];
const GradeClass = ['g0', 'g1', 'g2', 'g3', 'g4'];

const TILE = 22;

const canvas = document.getElementById('mine');
const ctx = canvas.getContext('2d');

let state = null;
let facing = Direction.Down;
let busy = false;

// ===== 启动 =====
init();

async function init() {
  bindEvents();
  try {
    state = await api('/api/state');
    sizeCanvas();
    render();
    log('欢迎来到矿山。方向键 / WASD 移动，撞到实心格会自动挖。', 'good');
    if (!state.OwnedTools.includes(ToolKind.Pickaxe)) {
      log(`开局资金 ¥${state.Money}，先去右下商店买把镐（¥30）才能挖动石头和煤。`, 'good');
    }
  } catch (err) {
    log('连接后端失败：' + err.message, 'bad');
  }
}

function bindEvents() {
  window.addEventListener('keydown', (e) => {
    if (e.target && /input|textarea|select/i.test(e.target.tagName)) return;

    const moveKeys = {
      ArrowUp: Direction.Up, w: Direction.Up, W: Direction.Up,
      ArrowDown: Direction.Down, s: Direction.Down, S: Direction.Down,
      ArrowLeft: Direction.Left, a: Direction.Left, A: Direction.Left,
      ArrowRight: Direction.Right, d: Direction.Right, D: Direction.Right,
    };

    if (Object.prototype.hasOwnProperty.call(moveKeys, e.key)) {
      e.preventDefault();
      tryMove(moveKeys[e.key]);
      return;
    }

    if (e.key === ' ') {
      e.preventDefault();
      digFacing();
    } else if (e.key === 'g' || e.key === 'G') {
      goSurface();
    } else if (e.key === 'p' || e.key === 'P') {
      polishBatch();
    } else if (e.key === 'f' || e.key === 'F') {
      sellAll();
    }
  });

  document.getElementById('btn-surface').addEventListener('click', goSurface);
  document.getElementById('btn-polish').addEventListener('click', polishBatch);
  document.getElementById('btn-polish-all').addEventListener('click', polishAll);
  document.getElementById('btn-sell').addEventListener('click', sellAll);
  document.getElementById('btn-ff').addEventListener('click', fastForward);

  canvas.addEventListener('click', (e) => {
    if (!state) return;
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / rect.width;
    const scaleY = canvas.height / rect.height;
    const cx = Math.floor((e.clientX - rect.left) * scaleX / TILE);
    const cy = Math.floor((e.clientY - rect.top) * scaleY / TILE);

    const dx = cx - state.MinerX;
    const dy = cy - state.MinerY;
    if (Math.abs(dx) + Math.abs(dy) !== 1) {
      log('只能操作角色相邻的一格。');
      return;
    }

    const dir = dx === 1 ? Direction.Right : dx === -1 ? Direction.Left : dy === 1 ? Direction.Down : Direction.Up;
    tryMove(dir);
  });
}

// ===== 请求 =====
async function api(path, body) {
  const options = body
    ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
    : {};
  const res = await fetch(path, options);
  if (!res.ok) {
    let detail = '';
    try { detail = JSON.stringify(await res.json()); } catch (_) { detail = await res.text(); }
    throw new Error(`${res.status} ${detail}`);
  }
  return res.json();
}

function nextSeed() {
  return Math.floor(Math.random() * 1000000000);
}

async function runAction(action, times = 1, options = {}) {
  if (!state || busy) return;
  busy = true;
  try {
    const actions = Array.isArray(action)
      ? action
      : Array.from({ length: times }, () => ({ ...action }));
    const res = await api('/api/advance', {
      State: state,
      Actions: actions,
      Steps: actions.length,
      Seed: nextSeed(),
    });
    state = res.State;
    handleOutcomes(res.Outcomes, options);
    render();
  } catch (err) {
    log('操作失败：' + err.message, 'bad');
  } finally {
    busy = false;
  }
}

async function fastForward() {
  if (!state || busy) return;
  busy = true;
  try {
    const dig = { Kind: ActionKind.Dig, Direction: facing, Tool: 0, Worker: 0 };
    const move = { Kind: ActionKind.Move, Direction: facing, Tool: 0, Worker: 0 };
    const res = await api('/api/fast-forward', {
      State: state,
      Actions: [dig, move],
      MaxSteps: 300,
      Seed: nextSeed(),
    });
    state = res.State;
    handleOutcomes(res.Outcomes, { maxLog: 3 });
    log(`快进结束：背包 ${state.Backpack.Used}/${state.Backpack.Capacity}，体力 ${state.Stamina}/${state.MaxStamina}。`, 'good');
    render();
  } catch (err) {
    log('快进失败：' + err.message, 'bad');
  } finally {
    busy = false;
  }
}

// ===== 动作 =====
function tryMove(dir) {
  if (!state) return;
  facing = dir;
  const d = delta(dir);
  const target = cellAt(state.MinerX + d.x, state.MinerY + d.y);
  if (!target) return;

  if (target.Tile === TileType.Air) {
    runAction({ Kind: ActionKind.Move, Direction: dir, Tool: 0, Worker: 0 });
  } else {
    // 撞到实心格：先挖，再顺势迈进去（矿脉剩多单位时下一按继续挖）
    runAction([
      { Kind: ActionKind.Dig, Direction: dir, Tool: 0, Worker: 0 },
      { Kind: ActionKind.Move, Direction: dir, Tool: 0, Worker: 0 },
    ]);
  }
}

function digFacing() {
  runAction({ Kind: ActionKind.Dig, Direction: facing, Tool: 0, Worker: 0 });
}

function goSurface() {
  runAction({ Kind: ActionKind.GoSurface, Direction: 0, Tool: 0, Worker: 0 });
}

function polishBatch() {
  if (!state) return;
  if (!atSurface()) return log('要回到地面（按 G）才能打磨。', 'bad');
  if (state.Backpack.Used <= 0) return log('背包里没有原矿可打磨。', 'bad');
  runAction({ Kind: ActionKind.Polish, Direction: 0, Tool: 0, Worker: 0 });
}

function polishAll() {
  if (!state) return;
  if (!atSurface()) return log('要回到地面（按 G）才能打磨。', 'bad');
  const units = state.Backpack.Items.reduce((sum, i) => sum + i.Amount, 0);
  if (units <= 0) return log('背包里没有原矿可打磨。', 'bad');

  const per = state.PolishSettings.UnitsPerAction || 5;
  const times = Math.ceil(units / per);
  runAction({ Kind: ActionKind.Polish, Direction: 0, Tool: 0, Worker: 0 }, times, { maxLog: 10 });
}

function sellAll() {
  if (!state) return;
  if (!atSurface()) return log('要回到地面（按 G）才能卖矿。', 'bad');
  runAction({ Kind: ActionKind.Sell, Direction: 0, Tool: 0, Worker: 0 });
}

function buyTool(kind) {
  runAction({ Kind: ActionKind.BuyTool, Tool: kind, Direction: 0, Worker: 0 });
}

function hireWorker(kind) {
  runAction({ Kind: ActionKind.HireWorker, Worker: kind, Direction: 0, Tool: 0 });
}

// ===== 结果处理 =====
function handleOutcomes(outcomes, options) {
  const maxLog = options.maxLog != null ? options.maxLog : 6;
  let logged = 0;
  const sales = [];
  const polishes = [];

  for (const o of outcomes) {
    if (o.Message && logged < maxLog) {
      const bad = /钱不够|体力不足|挖不动|满了|不能|没有|越界|挡住|基岩/.test(o.Message);
      log(o.Message, bad ? 'bad' : '');
      logged++;
    }
    if (o.Sale && (o.Sale.Income > 0 || (o.Sale.SoldProducts && o.Sale.SoldProducts.length))) sales.push(o.Sale);
    if (o.Polish && o.Polish.ConsumedUnits > 0) polishes.push(o.Polish);
  }

  if (outcomes.length > logged && outcomes.length > 0) {
    log(`…（另有 ${outcomes.length - logged} 步没有新事件）`);
  }
  if (sales.length) toastSale(sales);
  if (polishes.length) toastPolish(polishes);
}

function toastSale(sales) {
  const total = sales.reduce((sum, s) => sum + s.Income, 0);
  const lines = [];
  const raws = new Map();
  const products = new Map();

  for (const sale of sales) {
    for (const item of sale.Sold || []) {
      raws.set(item.Mineral, (raws.get(item.Mineral) || 0) + item.Amount);
    }
    for (const line of sale.SoldProducts || []) {
      const key = `${line.Mineral}-${line.Grade}`;
      const cur = products.get(key) || { ...line, Amount: 0, Subtotal: 0 };
      cur.Amount += line.Amount;
      cur.Subtotal += line.Subtotal;
      products.set(key, cur);
    }
  }

  for (const [mineral, amount] of raws) {
    lines.push(`原矿 ${MineralName[mineral]} ×${amount}`);
  }
  for (const p of products.values()) {
    lines.push(`<span class="badge ${GradeClass[p.Grade]}">${GradeName[p.Grade]}</span> ${MineralName[p.Mineral]} ×${p.Amount} @¥${p.UnitPrice}`);
  }

  toast(`卖出 ¥${total}`, lines);
}

function toastPolish(polishes) {
  const merged = new Map();
  let units = 0;
  for (const p of polishes) {
    units += p.ConsumedUnits;
    for (const item of p.Produced || []) {
      const key = `${item.Mineral}-${item.Grade}`;
      merged.set(key, (merged.get(key) || 0) + item.Amount);
    }
  }

  const lines = [`消耗原矿 ${units} 单位`];
  for (const [key, amount] of merged) {
    const [mineral, grade] = key.split('-').map(Number);
    const price = unitPrice(mineral, grade);
    lines.push(`<span class="badge ${GradeClass[grade]}">${GradeName[grade]}</span> ${MineralName[mineral]} ×${amount}（¥${price}/件）`);
  }
  toast('打磨完成', lines);
}

// ===== 渲染 =====
function render() {
  if (!state) return;
  renderHud();
  renderBag();
  renderGoods();
  renderQualityTable();
  renderShop();
  updateButtons();
  draw();
}

function renderHud() {
  document.getElementById('hud-money').textContent = '¥' + state.Money;
  document.getElementById('hud-bag').textContent = `${state.Backpack.Used}/${state.Backpack.Capacity}`;

  const staminaPct = state.MaxStamina > 0 ? (state.Stamina / state.MaxStamina) * 100 : 0;
  document.getElementById('stamina-fill').style.width = Math.max(0, Math.min(100, staminaPct)) + '%';
  document.getElementById('hud-stamina').textContent = `${state.Stamina}/${state.MaxStamina}`;

  const tool = state.ToolShop.find(t => t.Kind === state.CurrentTool);
  document.getElementById('hud-tool').textContent = tool ? tool.Name : '徒手';
  document.getElementById('hud-depth').textContent = Math.max(0, state.MinerY - state.Grid.SurfaceY) + 'm';
}

function renderBag() {
  const el = document.getElementById('bag-list');
  const items = (state.Backpack.Items || []).filter(i => i.Amount > 0);
  const value = items.reduce((sum, i) => sum + basePrice(i.Mineral) * i.Amount, 0);
  document.getElementById('bag-value').textContent = items.length ? `原矿估值 ¥${value}` : '';

  if (!items.length) {
    el.innerHTML = '<div class="empty">背包是空的</div>';
    return;
  }

  el.innerHTML = items.map(i => `
    <div class="row">
      <div class="left">
        <span class="dot" style="background:${MineralColor[i.Mineral]}"></span>
        <span class="name">${MineralName[i.Mineral]}</span>
        <span class="muted">×${i.Amount}</span>
      </div>
      <span class="price">¥${basePrice(i.Mineral) * i.Amount}</span>
    </div>`).join('');
}

function renderGoods() {
  const el = document.getElementById('goods-list');
  const goods = (state.PolishedGoods || []).filter(g => g.Amount > 0);
  let total = 0;
  for (const g of goods) total += unitPrice(g.Mineral, g.Grade) * g.Amount;
  document.getElementById('goods-value').textContent = goods.length ? `成品估值 ¥${total}` : '';

  if (!goods.length) {
    el.innerHTML = '<div class="empty">还没打磨过。回地面按“打磨一批”。</div>';
    return;
  }

  const sorted = [...goods].sort((a, b) => (b.Grade - a.Grade) || (b.Mineral - a.Mineral));
  el.innerHTML = sorted.map(g => `
    <div class="row">
      <div class="left">
        <span class="badge ${GradeClass[g.Grade]}">${GradeName[g.Grade]}</span>
        <span class="name">${MineralName[g.Mineral]}</span>
        <span class="muted">×${g.Amount}</span>
      </div>
      <span class="price">¥${unitPrice(g.Mineral, g.Grade) * g.Amount}</span>
    </div>`).join('');
}

function renderQualityTable() {
  const el = document.getElementById('quality-table');
  const percents = state.PolishSettings.GradePricePercent || [];
  const gold = basePrice(5);
  const rare = basePrice(6);

  const head = `<div class="q-row head"><span>成色</span><span class="num">加成</span><span class="num">金单价</span><span class="num">稀有晶体</span></div>`;
  const rows = GradeName.map((name, grade) => {
    const percent = percents[grade] || 100;
    return `<div class="q-row">
      <span><span class="badge ${GradeClass[grade]}">${name}</span></span>
      <span class="num">${percent}%</span>
      <span class="num">¥${Math.floor(gold * percent / 100)}</span>
      <span class="num">¥${Math.floor(rare * percent / 100)}</span>
    </div>`;
  }).join('');

  el.innerHTML = head + rows;
}

function renderShop() {
  const toolEl = document.getElementById('tool-shop');
  const tools = (state.ToolShop || []).filter(t => t.Kind !== ToolKind.BareHands);
  toolEl.innerHTML = tools.map(t => {
    const owned = state.OwnedTools.includes(t.Kind);
    return `<div class="shop-item">
      <div class="info">
        <div class="n">${t.Name}</div>
        <div class="d">挖掘力 ${t.Power} · ¥${t.Price}</div>
      </div>
      ${owned
        ? '<span class="buy owned">已拥有</span>'
        : `<button class="buy" onclick="buyTool(${t.Kind})">购买</button>`}
    </div>`;
  }).join('');

  const workerEl = document.getElementById('worker-shop');
  const workers = state.WorkerShop || [];
  workerEl.innerHTML = workers.map(w => `
    <div class="shop-item">
      <div class="info">
        <div class="n">${w.Name}</div>
        <div class="d">挖掘力 ${w.Power} · 每 ${w.Interval} 步一次 · ${w.Wage > 0 ? '工资 ¥' + w.Wage + '/步' : '免工资'} · ¥${w.Price}</div>
      </div>
      <button class="buy" onclick="hireWorker(${w.Kind})">雇佣</button>
    </div>`).join('');
}

function updateButtons() {
  const surface = atSurface();
  const hasOre = state.Backpack.Used > 0;
  const hasGoods = (state.PolishedGoods || []).some(g => g.Amount > 0);

  document.getElementById('btn-polish').disabled = !surface || !hasOre;
  document.getElementById('btn-polish-all').disabled = !surface || !hasOre;
  document.getElementById('btn-sell').disabled = !surface || (!hasOre && !hasGoods);
}

function draw() {
  const grid = state.Grid;
  const width = grid.Width;
  const height = grid.Height;

  ctx.clearRect(0, 0, canvas.width, canvas.height);

  // 地表以上的天空
  const sky = ctx.createLinearGradient(0, 0, 0, (grid.SurfaceY + 1) * TILE);
  sky.addColorStop(0, '#0e1622');
  sky.addColorStop(1, '#1b2a3d');
  ctx.fillStyle = sky;
  ctx.fillRect(0, 0, canvas.width, (grid.SurfaceY + 1) * TILE);

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const cell = grid.Cells[y * width + x];
      const px = x * TILE;
      const py = y * TILE;

      if (cell.Tile === TileType.Air) {
        if (y > grid.SurfaceY) {
          ctx.fillStyle = '#12161d';
          ctx.fillRect(px, py, TILE, TILE);
        }
        continue;
      }

      if (cell.Tile === TileType.Dirt) {
        ctx.fillStyle = '#6a4a2f';
        ctx.fillRect(px, py, TILE, TILE);
        ctx.fillStyle = 'rgba(0,0,0,0.16)';
        for (let i = 0; i < 4; i++) {
          const n = hash(x * 13 + i, y * 7 - i);
          ctx.fillRect(px + 2 + Math.floor(n * (TILE - 5)), py + 2 + Math.floor(hash(y, x + i) * (TILE - 5)), 2, 2);
        }
      } else if (cell.Tile === TileType.Stone) {
        ctx.fillStyle = '#50555d';
        ctx.fillRect(px, py, TILE, TILE);
        ctx.fillStyle = 'rgba(255,255,255,0.07)';
        for (let i = 0; i < 3; i++) {
          const n = hash(x * 5 + i, y * 11 + i);
          ctx.fillRect(px + 3 + Math.floor(n * (TILE - 7)), py + 3 + Math.floor(hash(y + i, x) * (TILE - 7)), 3, 2);
        }
      } else if (cell.Tile === TileType.Bedrock) {
        ctx.fillStyle = '#23262c';
        ctx.fillRect(px, py, TILE, TILE);
        ctx.strokeStyle = 'rgba(255,255,255,0.05)';
        ctx.beginPath();
        ctx.moveTo(px, py + TILE);
        ctx.lineTo(px + TILE, py);
        ctx.stroke();
      } else if (cell.Tile === TileType.Ore) {
        ctx.fillStyle = '#4c4238';
        ctx.fillRect(px, py, TILE, TILE);
        drawOre(px, py, cell.Mineral, x, y);
      }

      // 格子细线
      ctx.strokeStyle = 'rgba(0,0,0,0.18)';
      ctx.strokeRect(px + 0.5, py + 0.5, TILE - 1, TILE - 1);
    }
  }

  // 地表线
  ctx.strokeStyle = 'rgba(230, 200, 110, 0.35)';
  ctx.beginPath();
  ctx.moveTo(0, (grid.SurfaceY + 1) * TILE + 0.5);
  ctx.lineTo(canvas.width, (grid.SurfaceY + 1) * TILE + 0.5);
  ctx.stroke();

  // 机器 / 雇工
  for (const w of state.Workers || []) {
    ctx.fillStyle = '#2f8f5b';
    ctx.fillRect(w.X * TILE + 6, w.Y * TILE + 6, TILE - 12, TILE - 12);
    ctx.strokeStyle = '#65e39a';
    ctx.strokeRect(w.X * TILE + 6.5, w.Y * TILE + 6.5, TILE - 13, TILE - 13);
  }

  // 角色
  const cx = state.MinerX * TILE + TILE / 2;
  const cy = state.MinerY * TILE + TILE / 2;
  ctx.beginPath();
  ctx.arc(cx, cy, TILE * 0.32, 0, Math.PI * 2);
  ctx.fillStyle = '#eaf4ff';
  ctx.fill();
  ctx.lineWidth = 2;
  ctx.strokeStyle = '#4da3ff';
  ctx.stroke();

  // 朝向
  const d = delta(facing);
  ctx.beginPath();
  ctx.moveTo(cx, cy);
  ctx.lineTo(cx + d.x * TILE * 0.42, cy + d.y * TILE * 0.42);
  ctx.strokeStyle = '#4da3ff';
  ctx.lineWidth = 2;
  ctx.stroke();
}

function drawOre(px, py, mineral, gx, gy) {
  const color = MineralColor[mineral] || '#ffffff';
  const positions = [
    [0.3, 0.35, 3.2],
    [0.62, 0.55, 2.6],
    [0.45, 0.72, 2.2],
  ];

  for (let i = 0; i < positions.length; i++) {
    const [ox, oy, r] = positions[i];
    const jitter = hash(gx * 3 + i, gy * 5 + i) * 3;
    ctx.beginPath();
    ctx.arc(px + ox * TILE + jitter, py + oy * TILE, r, 0, Math.PI * 2);
    ctx.fillStyle = color;
    ctx.fill();
  }

  ctx.beginPath();
  ctx.arc(px + 0.6 * TILE, py + 0.45 * TILE, 1.2, 0, Math.PI * 2);
  ctx.fillStyle = 'rgba(255,255,255,0.75)';
  ctx.fill();
}

// ===== 工具函数 =====
function cellAt(x, y) {
  const grid = state.Grid;
  if (x < 0 || y < 0 || x >= grid.Width || y >= grid.Height) return null;
  return grid.Cells[y * grid.Width + x];
}

function atSurface() {
  return state && state.MinerY <= state.Grid.SurfaceY;
}

function delta(dir) {
  if (dir === Direction.Up) return { x: 0, y: -1 };
  if (dir === Direction.Down) return { x: 0, y: 1 };
  if (dir === Direction.Left) return { x: -1, y: 0 };
  return { x: 1, y: 0 };
}

function basePrice(mineral) {
  const value = state.Prices[mineral];
  return value != null ? value : 0;
}

function unitPrice(mineral, grade) {
  const percents = state.PolishSettings.GradePricePercent || [];
  const percent = percents[grade] || 100;
  return Math.floor(basePrice(mineral) * percent / 100);
}

function sizeCanvas() {
  canvas.width = state.Grid.Width * TILE;
  canvas.height = state.Grid.Height * TILE;
}

function hash(x, y) {
  let h = x * 374761393 + y * 668265263;
  h = (h ^ (h >> 13)) * 1274126177;
  return ((h ^ (h >> 16)) >>> 0) / 4294967295;
}

function log(text, kind) {
  if (!text) return;
  const box = document.getElementById('log');
  const line = document.createElement('div');
  line.className = 'line ' + (kind || '');
  line.textContent = text;
  box.appendChild(line);
  while (box.childElementCount > 40) box.removeChild(box.firstChild);
  box.scrollTop = box.scrollHeight;
}

function toast(title, lines) {
  const root = document.getElementById('toast-root');
  const el = document.createElement('div');
  el.className = 'toast';
  el.innerHTML = `<div class="t-title">${title}</div>` +
    (lines || []).map(l => `<div class="t-line">${l}</div>`).join('');
  root.appendChild(el);
  setTimeout(() => {
    el.classList.add('out');
    setTimeout(() => el.remove(), 300);
  }, 4200);
}

// 供 HTML 内联 onclick 调用
window.buyTool = buyTool;
window.hireWorker = hireWorker;
