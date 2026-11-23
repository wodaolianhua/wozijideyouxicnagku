// ============================================================================
//  Scene/WorldScene.ts —— 2D 疆域地图（Canvas 2D）
//
//  v1.9 起彻底替换原来的 three.js 3D 场景：
//    · 一张平面地图：纸面底 + 势力疆域色块 + 边界 + 道路 + 城池
//    · 疆域用「最近城池」划格生成，占领后整片变色
//    · 迷雾：未探索的城盖灰雾，首次可见时 1.5 秒淡出
//    · 三级缩放：近 1.8 / 中 1.0 / 远 0.55，0.3 秒缓动
//    · 没有透视、没有光照、没有 3D 网格
//
//  对外接口刻意保持与旧版同名同签名（init / refreshFromStore / setMode /
//  focusCity / showMarchArrow / screenPos …），Hud 与 main 不必改接线。
// ============================================================================

import type { City } from '../Core/Types.js';
import type { GameStateStore } from '../Core/GameStateStore.js';
import type { Controls } from '../Player/Controls.js';
import { factionColor } from '../Ui/Theme.js';
import { adjacencyPairs, cityXY, MAP_H, MAP_W } from './Adjacency.js';

export type ViewMode = 'god' | 'field';

export type HoverTarget = { kind: 'city'; id: number } | { kind: 'army'; id: number };

export interface WorldSceneCallbacks {
  onHover(target: HoverTarget | null): void;
  onSelectCity(cityId: number): void;
  onSelectArmy(armyId: number): void;
  onClickEmpty(): void;
}

/** 三档缩放倍率（相对 baseScale） */
const ZOOM_TIERS = [1.8, 1.0, 0.55];
const TIER_NEAR = 0;
const TIER_MID = 1;
const TIER_FAR = 2;

/** 疆域半径（世界单位）：超过这个距离不算任何势力的地 */
const TERRITORY_RADIUS = 215;

/** 迷雾不透明度与淡出时长 */
const FOG_ALPHA = 0.55;
const FOG_FADE_MS = 1500;

/** 格点噪声：让疆域边界不是完美圆弧 */
function noise2(x: number, y: number, seed: number): number {
  let n = Math.imul(x, 374761393) ^ Math.imul(y, 668265263) ^ Math.imul(seed, 1442695041);
  n = Math.imul(n ^ (n >>> 13), 1274126177);
  return ((n ^ (n >>> 16)) >>> 0) / 4294967295;
}

function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const h = hex.replace('#', '');
  const v = parseInt(h.length === 3 ? h.split('').map((c) => c + c).join('') : h, 16);
  return { r: (v >> 16) & 255, g: (v >> 8) & 255, b: v & 255 };
}

export class WorldScene {
  private container!: HTMLElement;
  private canvas!: HTMLCanvasElement;
  private ctx!: CanvasRenderingContext2D;
  private store!: GameStateStore;
  private cb!: WorldSceneCallbacks;
  private controls: Controls | null = null;

  private viewMode: ViewMode = 'god';
  private raf = 0;
  private visible = true;
  private dpr = 1;
  private vw = 0;
  private vh = 0;
  private lastT = 0;
  private disposed = false;
  private resizers: Array<() => void> = [];

  private baseScale = 1;
  private scale = 1;
  private targetScale = 1;
  private tier = TIER_MID;
  private offsetX = 0;
  private offsetY = 0;

  private selectedCityId: number | null = null;
  private hover: HoverTarget | null = null;
  private march: { from: number; to: number } | null = null;

  /** 曾经可见过的城（永久记住，不随视野缩小而消失） */
  private explored = new Set<number>();
  private revealAt = new Map<number, number>();

  private territory: HTMLCanvasElement | null = null;
  private territoryKey = '';
  private paper: HTMLCanvasElement | null = null;

  private dragging = false;
  private downX = 0;
  private downY = 0;
  private moved = 0;
  private pinchStart = 0;

  // -------------------------------------------------------------------------
  //  初始化
  // -------------------------------------------------------------------------
  async init(
    container: HTMLElement,
    store: GameStateStore,
    callbacks: WorldSceneCallbacks,
    controls: Controls,
  ): Promise<void> {
    this.container = container;
    this.store = store;
    this.cb = callbacks;
    this.controls = controls;

    this.canvas = document.createElement('canvas');
    this.canvas.style.cssText =
      'position:absolute;inset:0;width:100%;height:100%;display:block;touch-action:none;cursor:grab;';
    container.appendChild(this.canvas);

    const ctx = this.canvas.getContext('2d');
    if (!ctx) {
      throw new Error('当前浏览器不支持 Canvas 2D');
    }
    this.ctx = ctx;

    this.resize();
    this.buildPaper();

    // 键盘快捷键仍由 Controls 统一管（Hud 通过 onShortcut 消费）
    controls.attach(this.canvas);

    this.bindPointer();

    const onResize = (): void => this.resize();
    window.addEventListener('resize', onResize);
    this.resizers.push(() => window.removeEventListener('resize', onResize));

    const tierParam = new URLSearchParams(window.location.search).get('tier');
    if (tierParam === 'near') {
      this.tier = TIER_NEAR;
    } else if (tierParam === 'far') {
      this.tier = TIER_FAR;
    }
    this.scale = this.baseScale * ZOOM_TIERS[this.tier];
    this.targetScale = this.scale;
    this.offsetX = (this.vw - MAP_W * this.scale) / 2;
    this.offsetY = (this.vh - MAP_H * this.scale) / 2;

    this.refreshFromStore();

    this.startLoop();
  }

  /** 渲染循环（显隐切换时会停 / 启） */
  private startLoop(): void {
    const loop = (t: number): void => {
      if (this.disposed) {
        return;
      }
      this.raf = requestAnimationFrame(loop);
      this.frame(t);
    };
    this.raf = requestAnimationFrame(loop);
  }

  /**
   * 显隐切换。
   *
   * 城内视图盖上来时：疆域图停止渲染并让出事件，回来时接着跑。
   * 不用「盖一层不透明画布」实现，是为了不白白烧 CPU，也免得底下那张图还在动。
   */
  setVisible(visible: boolean): void {
    if (this.visible === visible) {
      return;
    }
    this.visible = visible;
    if (this.canvas) {
      this.canvas.style.display = visible ? 'block' : 'none';
    }
    if (!visible) {
      if (this.raf) {
        cancelAnimationFrame(this.raf);
        this.raf = 0;
      }
      return;
    }
    if (!this.raf && !this.disposed) {
      this.lastT = 0;
      this.startLoop();
    }
  }

  private resize(): void {
    const rect = this.container.getBoundingClientRect();
    this.vw = Math.max(320, rect.width);
    this.vh = Math.max(240, rect.height);
    this.dpr = Math.min(window.devicePixelRatio || 1, 2);

    this.canvas.width = Math.round(this.vw * this.dpr);
    this.canvas.height = Math.round(this.vh * this.dpr);

    // baseScale：整张地图刚好塞进视口（留边）
    this.baseScale = Math.min(this.vw / (MAP_W + 90), this.vh / (MAP_H + 90));
    this.scale = this.baseScale * ZOOM_TIERS[this.tier];
    this.targetScale = this.scale;

    // 居中
    this.offsetX = (this.vw - MAP_W * this.scale) / 2;
    this.offsetY = (this.vh - MAP_H * this.scale) / 2;
  }

  // -------------------------------------------------------------------------
  //  纸面底图（预渲染一次）
  // -------------------------------------------------------------------------
  private buildPaper(): void {
    const cv = document.createElement('canvas');
    cv.width = MAP_W;
    cv.height = MAP_H;
    const c = cv.getContext('2d');
    if (!c) {
      return;
    }

    const g = c.createLinearGradient(0, 0, MAP_W, MAP_H);
    g.addColorStop(0, '#efe4ca');
    g.addColorStop(0.5, '#e7d9ba');
    g.addColorStop(1, '#dfd0ae');
    c.fillStyle = g;
    c.fillRect(0, 0, MAP_W, MAP_H);

    // 纤维纹理：稀疏短线 + 细点，模拟旧纸
    c.globalAlpha = 0.06;
    c.strokeStyle = '#6b5636';
    c.lineWidth = 1;
    for (let i = 0; i < 900; i++) {
      const x = (i * 731) % MAP_W;
      const y = (i * 397) % MAP_H;
      const len = 6 + ((i * 13) % 18);
      const ang = ((i * 29) % 360) * (Math.PI / 180);
      c.beginPath();
      c.moveTo(x, y);
      c.lineTo(x + Math.cos(ang) * len, y + Math.sin(ang) * len);
      c.stroke();
    }

    c.globalAlpha = 0.05;
    c.fillStyle = '#4a3a22';
    for (let i = 0; i < 2600; i++) {
      c.fillRect((i * 977) % MAP_W, (i * 613) % MAP_H, 1, 1);
    }
    c.globalAlpha = 1;

    // 四周压暗，像摊开的图页
    const vg = c.createRadialGradient(
      MAP_W / 2, MAP_H / 2, Math.min(MAP_W, MAP_H) * 0.35,
      MAP_W / 2, MAP_H / 2, Math.max(MAP_W, MAP_H) * 0.72,
    );
    vg.addColorStop(0, 'rgba(0,0,0,0)');
    vg.addColorStop(1, 'rgba(70,52,26,0.22)');
    c.fillStyle = vg;
    c.fillRect(0, 0, MAP_W, MAP_H);

    this.paper = cv;
  }

  // -------------------------------------------------------------------------
  //  疆域层（只在城池归属变化时重算）
  // -------------------------------------------------------------------------
  private rebuildTerritory(): void {
    const W = 400;
    const H = 304;
    const cv = this.territory ?? document.createElement('canvas');
    cv.width = W;
    cv.height = H;
    const c = cv.getContext('2d');
    if (!c) {
      return;
    }

    const cities = this.store.state.cities;
    const owner = new Int16Array(W * H).fill(-1);
    const img = c.createImageData(W, H);

    for (let gy = 0; gy < H; gy++) {
      for (let gx = 0; gx < W; gx++) {
        const wx = ((gx + 0.5) * MAP_W) / W;
        const wy = ((gy + 0.5) * MAP_H) / H;

        let bestIdx = -1;
        let bestD = Infinity;
        for (let k = 0; k < cities.length; k++) {
          const p = cityXY(cities[k].id);
          let d = Math.hypot(wx - p.x, wy - p.y);
          // 边界抖动用低频噪声，避免一圈完美的同心圆
          d *= 0.86 + 0.3 * noise2(gx >> 2, gy >> 2, cities[k].id * 17 + 5);
          if (d < bestD) {
            bestD = d;
            bestIdx = k;
          }
        }

        if (bestIdx < 0 || bestD > TERRITORY_RADIUS) {
          continue;
        }
        owner[gy * W + gx] = cities[bestIdx].ownerFactionId;
      }
    }

    // 上色：势力色 + 边界格加深
    for (let gy = 0; gy < H; gy++) {
      for (let gx = 0; gx < W; gx++) {
        const f = owner[gy * W + gx];
        if (f < 0) {
          continue;
        }

        const right = gx + 1 < W ? owner[gy * W + gx + 1] : f;
        const down = gy + 1 < H ? owner[(gy + 1) * W + gx] : f;
        const isEdge = right !== f || down !== f;

        const rgb = hexToRgb(factionColor(f));
        const i = (gy * W + gx) * 4;
        const k = isEdge ? 0.55 : 1;

        img.data[i] = Math.round(rgb.r * k);
        img.data[i + 1] = Math.round(rgb.g * k);
        img.data[i + 2] = Math.round(rgb.b * k);
        img.data[i + 3] = isEdge ? 165 : 108;
      }
    }

    c.putImageData(img, 0, 0);
    this.territory = cv;
  }

  // -------------------------------------------------------------------------
  //  状态刷新
  // -------------------------------------------------------------------------
  refreshFromStore(): void {
    if (!this.ctx) {
      return;
    }

    const state = this.store.state;
    const pf = this.store.playerFaction;

    // 可见 = 自己的城 + 与自己城相邻的城；一旦可见就永久记住
    if (pf) {
      const now = performance.now();
      for (const city of state.cities) {
        if (city.ownerFactionId !== pf.id) {
          continue;
        }
        if (!this.explored.has(city.id)) {
          this.explored.add(city.id);
          this.revealAt.set(city.id, now);
        }
        for (const adjId of city.adjacentCityIds) {
          if (!this.explored.has(adjId)) {
            this.explored.add(adjId);
            this.revealAt.set(adjId, now);
          }
        }
      }
    }

    const key = state.cities.map((c) => `${c.id}:${c.ownerFactionId}`).join('|');
    if (key !== this.territoryKey) {
      this.territoryKey = key;
      this.rebuildTerritory();
    }
  }

  // -------------------------------------------------------------------------
  //  每帧绘制
  // -------------------------------------------------------------------------
  private frame(t: number): void {
    const dt = Math.min(0.05, (t - (this.lastT || t)) / 1000);
    this.lastT = t;

    // 缩放缓动（约 0.3 秒收敛）
    const k = 1 - Math.pow(0.001, dt / 0.3);
    this.scale += (this.targetScale - this.scale) * k;
    if (Math.abs(this.targetScale - this.scale) < 0.0005) {
      this.scale = this.targetScale;
    }

    this.clampOffset();
    this.draw(t);
  }

  private clampOffset(): void {
    const w = MAP_W * this.scale;
    const h = MAP_H * this.scale;
    const margin = 120;

    if (w <= this.vw) {
      this.offsetX = (this.vw - w) / 2;
    } else {
      this.offsetX = Math.min(margin, Math.max(this.vw - w - margin, this.offsetX));
    }

    if (h <= this.vh) {
      this.offsetY = (this.vh - h) / 2;
    } else {
      this.offsetY = Math.min(margin, Math.max(this.vh - h - margin, this.offsetY));
    }
  }

  private draw(now: number): void {
    const ctx = this.ctx;

    ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    ctx.clearRect(0, 0, this.vw, this.vh);

    // 画布外的桌面底色
    ctx.fillStyle = '#161a20';
    ctx.fillRect(0, 0, this.vw, this.vh);

    ctx.save();
    ctx.translate(this.offsetX, this.offsetY);
    ctx.scale(this.scale, this.scale);

    // 地图投影
    ctx.shadowColor = 'rgba(0,0,0,0.55)';
    ctx.shadowBlur = 30 / this.scale;
    ctx.fillStyle = '#e9dcc0';
    ctx.fillRect(0, 0, MAP_W, MAP_H);
    ctx.shadowBlur = 0;

    if (this.paper) {
      ctx.drawImage(this.paper, 0, 0);
    }

    if (this.territory) {
      ctx.globalAlpha = 0.92;
      ctx.imageSmoothingEnabled = true;
      ctx.imageSmoothingQuality = 'high';
      ctx.drawImage(this.territory, 0, 0, MAP_W, MAP_H);
      ctx.globalAlpha = 1;
    }

    this.drawRoads(ctx);
    this.drawCities(ctx, now);
    this.drawArmies(ctx);
    this.drawFactionNames(ctx);
    this.drawMarch(ctx, now);
    this.drawBorder(ctx);

    ctx.restore();
  }

  private drawBorder(ctx: CanvasRenderingContext2D): void {
    ctx.strokeStyle = 'rgba(78,60,34,0.75)';
    ctx.lineWidth = 3 / this.scale;
    ctx.strokeRect(0, 0, MAP_W, MAP_H);
    ctx.strokeStyle = 'rgba(78,60,34,0.3)';
    ctx.lineWidth = 1 / this.scale;
    const inset = 9 / this.scale;
    ctx.strokeRect(inset, inset, MAP_W - inset * 2, MAP_H - inset * 2);
  }

  private drawRoads(ctx: CanvasRenderingContext2D): void {
    const pairs = adjacencyPairs(this.store.state);

    ctx.save();
    ctx.setLineDash([9, 7]);
    ctx.lineCap = 'round';

    for (const [a, b] of pairs) {
      const known = this.explored.has(a) && this.explored.has(b);
      const pa = cityXY(a);
      const pb = cityXY(b);

      ctx.strokeStyle = known ? 'rgba(120,96,58,0.75)' : 'rgba(120,96,58,0.28)';
      ctx.lineWidth = known ? 3 : 2;
      ctx.beginPath();
      ctx.moveTo(pa.x, pa.y);
      ctx.lineTo(pb.x, pb.y);
      ctx.stroke();
    }

    ctx.restore();
  }

  private drawCities(ctx: CanvasRenderingContext2D, now: number): void {
    // 玩家势力：地图上自己的城要一眼认出来
    const playerId = this.store.state.factions.find((f) => f.isPlayer)?.id ?? -1;
    for (const city of this.store.state.cities) {
      const p = cityXY(city.id);
      const known = this.explored.has(city.id);
      const fog = this.fogAlpha(city.id, now);
      const color = known ? factionColor(city.ownerFactionId) : '#7b8494';
      const mine = known && city.ownerFactionId === playerId;

      // 我方城池：外圈呼吸光晕 + 「你」标签，解决「不知道自己是哪一方」
      if (mine) {
        const glow = 0.5 + 0.5 * Math.sin(now / 620);
        const g = ctx.createRadialGradient(p.x, p.y, 18, p.x, p.y, 46);
        g.addColorStop(0, `rgba(255,236,168,${(0.34 * glow).toFixed(3)})`);
        g.addColorStop(1, 'rgba(255,236,168,0)');
        ctx.fillStyle = g;
        ctx.beginPath();
        ctx.arc(p.x, p.y, 46, 0, Math.PI * 2);
        ctx.fill();
      }

      if (this.selectedCityId === city.id) {
        const pulse = 1 + 0.12 * Math.sin(now / 320);
        ctx.beginPath();
        ctx.arc(p.x, p.y, 34 * pulse, 0, Math.PI * 2);
        ctx.strokeStyle = 'rgba(255,214,120,0.95)';
        ctx.lineWidth = 3;
        ctx.setLineDash([6, 6]);
        ctx.stroke();
        ctx.setLineDash([]);
      }

      // 城郭：外环 + 深色底 + 垛口 + 中心块
      ctx.beginPath();
      ctx.arc(p.x, p.y, 21, 0, Math.PI * 2);
      ctx.fillStyle = known ? 'rgba(24,29,38,0.92)' : 'rgba(46,52,62,0.9)';
      ctx.fill();
      ctx.lineWidth = 4;
      ctx.strokeStyle = color;
      ctx.stroke();

      ctx.fillStyle = color;
      for (let i = 0; i < 6; i++) {
        const ang = (Math.PI * 2 * i) / 6 - Math.PI / 2;
        const bx = p.x + Math.cos(ang) * 21;
        const by = p.y + Math.sin(ang) * 21;
        ctx.save();
        ctx.translate(bx, by);
        ctx.rotate(ang + Math.PI / 2);
        ctx.fillRect(-2.6, -3.4, 5.2, 6.8);
        ctx.restore();
      }

      ctx.beginPath();
      ctx.arc(p.x, p.y, 7, 0, Math.PI * 2);
      ctx.fillStyle = color;
      ctx.fill();

      if (known && city.isCapital) {
        // 星星 = 都城。每个势力都有一座，所以地图上会有多个星；
        // 玩家自己的都城用金色大星并标注，别家都城只用暗铜小星，避免混淆。
        if (mine) {
          const sp = 0.5 + 0.5 * Math.sin(now / 500);
          this.star(ctx, p.x, p.y - 34, 8.5, `rgba(255,214,90,${(0.75 + 0.25 * sp).toFixed(3)})`);
          this.label(ctx, '都城', p.x, p.y - 48, 12, '#8a6320');
        } else {
          this.star(ctx, p.x, p.y - 32, 5.5, 'rgba(120,104,78,0.85)');
        }
      }

      // 远景只留色块与势力名，不显示城名
      if (known && this.tier !== TIER_FAR) {
        this.label(ctx, mine ? `${city.name}〔你〕` : city.name, p.x, p.y + 43, mine ? 16 : 15, mine ? '#7a4a00' : '#2f2517');
      }

      if (known && this.tier === TIER_NEAR) {
        const garrison = this.store.state.armies
          .filter((a) => a.positionCityId === city.id)
          .reduce((s, a) => s + a.soldiers, 0);
        if (garrison > 0) {
          this.label(ctx, `驻 ${this.fmt(garrison)}`, p.x, p.y + 63, 12, '#5a4525');
        }
      }

      // 迷雾：多层错位云团 + 缓慢漂移，边缘柔和不规则。
      // 【第 8 轮】原来是单层径向渐变，半径 205 满透明度，看着就是一颗颗实心「蛋」。
      if (fog > 0.001) {
        const base = FOG_ALPHA * fog;
        const blobs: Array<[number, number, number]> = [
          [0, 0, 148],
          [-56, -32, 92],
          [52, 28, 100],
          [14, -54, 82],
          [-36, 48, 88],
        ];
        for (let bi = 0; bi < blobs.length; bi++) {
          const [dx, dy, r] = blobs[bi];
          const wob = 7 * Math.sin(now / 1100 + bi * 1.7 + city.id);
          const cx = p.x + dx + wob;
          const cy = p.y + dy + wob * 0.5;
          const g = ctx.createRadialGradient(cx, cy, r * 0.12, cx, cy, r);
          g.addColorStop(0, `rgba(58,64,76,${(base * 0.3).toFixed(3)})`);
          g.addColorStop(0.58, `rgba(58,64,76,${(base * 0.2).toFixed(3)})`);
          g.addColorStop(1, 'rgba(58,64,76,0)');
          ctx.fillStyle = g;
          ctx.beginPath();
          ctx.arc(cx, cy, r, 0, Math.PI * 2);
          ctx.fill();
        }
      }
    }
  }

  private drawArmies(ctx: CanvasRenderingContext2D): void {
    const byCity = new Map<number, number>();

    for (const army of this.store.state.armies) {
      if (army.soldiers <= 0) {
        continue;
      }
      const cityId = army.positionCityId;
      if (!this.explored.has(cityId)) {
        continue;
      }

      const idx = byCity.get(cityId) ?? 0;
      byCity.set(cityId, idx + 1);

      const p = cityXY(cityId);
      const x = p.x + 30 + idx * 13;
      const y = p.y - 26;
      const color = factionColor(army.ownerFactionId);

      // 旗杆
      ctx.strokeStyle = 'rgba(40,32,18,0.9)';
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(x, y);
      ctx.lineTo(x, y + 26);
      ctx.stroke();

      // 旗面
      ctx.beginPath();
      ctx.moveTo(x, y);
      ctx.lineTo(x + 15, y + 4);
      ctx.lineTo(x, y + 11);
      ctx.closePath();
      ctx.fillStyle = color;
      ctx.fill();
      ctx.strokeStyle = 'rgba(30,25,15,0.7)';
      ctx.lineWidth = 1;
      ctx.stroke();

      if (this.tier === TIER_NEAR) {
        this.label(ctx, this.fmt(army.soldiers), x + 8, y + 24, 11, '#3a2d18');
      }
    }
  }

  private drawFactionNames(ctx: CanvasRenderingContext2D): void {
    if (this.tier !== TIER_FAR) {
      return;
    }

    const byFaction = new Map<number, { x: number; y: number; n: number }>();
    for (const city of this.store.state.cities) {
      if (!this.explored.has(city.id)) {
        continue;
      }
      const p = cityXY(city.id);
      const cur = byFaction.get(city.ownerFactionId) ?? { x: 0, y: 0, n: 0 };
      cur.x += p.x;
      cur.y += p.y;
      cur.n += 1;
      byFaction.set(city.ownerFactionId, cur);
    }

    for (const [factionId, agg] of byFaction) {
      const faction = this.store.faction(factionId);
      if (!faction) {
        continue;
      }
      this.label(ctx, faction.name, agg.x / agg.n, agg.y / agg.n, 30, factionColor(factionId), 0.92);
    }
  }

  private drawMarch(ctx: CanvasRenderingContext2D, now: number): void {
    if (!this.march) {
      return;
    }
    const a = cityXY(this.march.from);
    const b = cityXY(this.march.to);
    const ang = Math.atan2(b.y - a.y, b.x - a.x);

    ctx.save();
    ctx.strokeStyle = 'rgba(214,74,52,0.95)';
    ctx.lineWidth = 4;
    ctx.setLineDash([14, 10]);
    ctx.lineDashOffset = -(now / 45) % 24;
    ctx.beginPath();
    ctx.moveTo(a.x, a.y);
    ctx.lineTo(b.x, b.y);
    ctx.stroke();
    ctx.setLineDash([]);

    ctx.fillStyle = 'rgba(214,74,52,0.95)';
    ctx.beginPath();
    ctx.moveTo(b.x, b.y);
    ctx.lineTo(b.x - Math.cos(ang - 0.4) * 26, b.y - Math.sin(ang - 0.4) * 26);
    ctx.lineTo(b.x - Math.cos(ang + 0.4) * 26, b.y - Math.sin(ang + 0.4) * 26);
    ctx.closePath();
    ctx.fill();
    ctx.restore();
  }

  // -------------------------------------------------------------------------
  //  绘制小工具
  // -------------------------------------------------------------------------
  private label(
    ctx: CanvasRenderingContext2D,
    text: string,
    x: number,
    y: number,
    size: number,
    color: string,
    alpha = 1,
  ): void {
    ctx.save();
    ctx.globalAlpha = alpha;
    ctx.font = `600 ${size}px "Microsoft YaHei","PingFang SC",system-ui,sans-serif`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.lineJoin = 'round';
    ctx.lineWidth = Math.max(2.5, size * 0.32);
    ctx.strokeStyle = 'rgba(245,236,214,0.92)';
    ctx.strokeText(text, x, y);
    ctx.fillStyle = color;
    ctx.fillText(text, x, y);
    ctx.restore();
  }

  private star(
    ctx: CanvasRenderingContext2D,
    x: number,
    y: number,
    r: number,
    color: string,
  ): void {
    ctx.save();
    ctx.beginPath();
    for (let i = 0; i < 10; i++) {
      const rad = i % 2 === 0 ? r : r * 0.45;
      const ang = (Math.PI / 5) * i - Math.PI / 2;
      const px = x + Math.cos(ang) * rad;
      const py = y + Math.sin(ang) * rad;
      if (i === 0) {
        ctx.moveTo(px, py);
      } else {
        ctx.lineTo(px, py);
      }
    }
    ctx.closePath();
    ctx.fillStyle = color;
    ctx.fill();
    ctx.strokeStyle = 'rgba(60,44,20,0.6)';
    ctx.lineWidth = 1;
    ctx.stroke();
    ctx.restore();
  }

  private fmt(n: number): string {
    return n >= 10000 ? `${(n / 10000).toFixed(1)}万` : String(n);
  }

  private fogAlpha(cityId: number, now: number): number {
    if (!this.explored.has(cityId)) {
      return 1;
    }
    const t = this.revealAt.get(cityId);
    if (t === undefined) {
      return 0;
    }
    const p = (now - t) / FOG_FADE_MS;
    return p >= 1 ? 0 : 1 - p;
  }

  // -------------------------------------------------------------------------
  //  交互
  // -------------------------------------------------------------------------
  private bindPointer(): void {
    const canvas = this.canvas;

    const onDown = (ev: PointerEvent): void => {
      if (ev.button !== 0) {
        return;
      }
      this.dragging = true;
      this.moved = 0;
      this.downX = ev.clientX;
      this.downY = ev.clientY;
      canvas.style.cursor = 'grabbing';
      canvas.setPointerCapture?.(ev.pointerId);
    };

    const onMove = (ev: PointerEvent): void => {
      if (this.dragging) {
        const dx = ev.clientX - this.downX;
        const dy = ev.clientY - this.downY;
        this.moved += Math.abs(dx) + Math.abs(dy);
        this.offsetX += dx;
        this.offsetY += dy;
        this.downX = ev.clientX;
        this.downY = ev.clientY;
        return;
      }

      const hit = this.pick(ev.clientX, ev.clientY);
      if ((hit?.id ?? -1) !== (this.hover?.id ?? -1) || hit?.kind !== this.hover?.kind) {
        this.hover = hit;
        this.cb.onHover(hit);
      }
    };

    const onUp = (ev: PointerEvent): void => {
      if (!this.dragging) {
        return;
      }
      this.dragging = false;
      canvas.style.cursor = 'grab';
      canvas.releasePointerCapture?.(ev.pointerId);

      // 位移小于阈值才算点击，避免拖地图时误开面板
      if (this.moved < 7) {
        const hit = this.pick(ev.clientX, ev.clientY);
        if (hit?.kind === 'city') {
          this.cb.onSelectCity(hit.id);
        } else if (hit?.kind === 'army') {
          this.cb.onSelectArmy(hit.id);
        } else {
          this.cb.onClickEmpty();
        }
      }
    };

    const onWheel = (ev: WheelEvent): void => {
      ev.preventDefault();
      this.zoomBy(ev.deltaY > 0 ? 1 : -1);
    };

    const onTouchStart = (ev: TouchEvent): void => {
      if (ev.touches.length === 2) {
        const a = ev.touches[0];
        const b = ev.touches[1];
        this.pinchStart = Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY);
      }
    };

    const onTouchMove = (ev: TouchEvent): void => {
      if (ev.touches.length !== 2 || this.pinchStart <= 0) {
        return;
      }
      const a = ev.touches[0];
      const b = ev.touches[1];
      const d = Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY);
      if (Math.abs(d - this.pinchStart) > 45) {
        this.zoomBy(d < this.pinchStart ? 1 : -1);
        this.pinchStart = d;
      }
    };

    canvas.addEventListener('pointerdown', onDown);
    canvas.addEventListener('pointermove', onMove);
    canvas.addEventListener('pointerup', onUp);
    canvas.addEventListener('pointercancel', onUp);
    canvas.addEventListener('wheel', onWheel, { passive: false });
    canvas.addEventListener('touchstart', onTouchStart, { passive: true });
    canvas.addEventListener('touchmove', onTouchMove, { passive: true });

    this.resizers.push(
      () => canvas.removeEventListener('pointerdown', onDown),
      () => canvas.removeEventListener('pointermove', onMove),
      () => canvas.removeEventListener('pointerup', onUp),
      () => canvas.removeEventListener('pointercancel', onUp),
      () => canvas.removeEventListener('wheel', onWheel),
      () => canvas.removeEventListener('touchstart', onTouchStart),
      () => canvas.removeEventListener('touchmove', onTouchMove),
    );
  }

  /** 屏幕坐标 → 命中城池（2D 版只认城，部队挂在城面板里看） */
  private pick(clientX: number, clientY: number): HoverTarget | null {
    const rect = this.canvas.getBoundingClientRect();
    const wx = (clientX - rect.left - this.offsetX) / this.scale;
    const wy = (clientY - rect.top - this.offsetY) / this.scale;

    let best: City | null = null;
    let bestD = Infinity;
    for (const city of this.store.state.cities) {
      const p = cityXY(city.id);
      const d = Math.hypot(wx - p.x, wy - p.y);
      if (d < 34 && d < bestD) {
        bestD = d;
        best = city;
      }
    }

    return best ? { kind: 'city', id: best.id } : null;
  }

  private zoomBy(dir: number): void {
    const next = Math.max(TIER_NEAR, Math.min(TIER_FAR, this.tier + dir));
    if (next === this.tier) {
      return;
    }
    this.tier = next;
    this.targetScale = this.baseScale * ZOOM_TIERS[this.tier];
  }

  /** 当前档位：近景 / 中景 / 全图 */
  get zoomTierName(): string {
    return this.tier === TIER_NEAR ? '近景' : this.tier === TIER_FAR ? '全图' : '中景';
  }

  // -------------------------------------------------------------------------
  //  对外方法（保持旧签名）
  // -------------------------------------------------------------------------
  get mode(): ViewMode {
    return this.viewMode;
  }

  setMode(mode: ViewMode): void {
    // 2D 疆域图没有亲征视角：两种入口都落在同一张平面图上
    this.viewMode = mode === 'field' ? 'god' : mode;
  }

  setSelectedCity(cityId: number | null): void {
    this.selectedCityId = cityId;
  }

  focusCity(cityId: number): void {
    const p = cityXY(cityId);
    this.tier = TIER_NEAR;
    this.targetScale = this.baseScale * ZOOM_TIERS[this.tier];
    this.scale = this.targetScale;

    this.offsetX = this.vw / 2 - p.x * this.scale;
    this.offsetY = this.vh / 2 - p.y * this.scale;
    this.clampOffset();
  }

  showMarchArrow(fromCityId: number, toCityId: number): void {
    this.march = { from: fromCityId, to: toCityId };
  }

  removeMarchArrow(): void {
    this.march = null;
  }

  /** 城池在屏幕上的位置（战斗演出定位用） */
  cityScreenPos(cityId: number): { x: number; y: number } | null {
    const p = cityXY(cityId);
    return { x: this.offsetX + p.x * this.scale, y: this.offsetY + p.y * this.scale };
  }

  screenPos(v: { x: number; y: number; z?: number }): { x: number; y: number } | null {
    return { x: this.offsetX + v.x * this.scale, y: this.offsetY + (v.z ?? v.y) * this.scale };
  }

  get lordWorldPos(): { x: number; y: number; z: number } {
    const pf = this.store.playerFaction;
    const capital = pf?.capitalCityId ?? this.store.state.cities[0]?.id ?? 1;
    const p = cityXY(capital);
    return { x: p.x, y: 0, z: p.y };
  }

  armyWorldPos(armyId: number): { x: number; y: number; z: number } | null {
    const army = this.store.army(armyId);
    if (!army) {
      return null;
    }
    const p = cityXY(army.positionCityId);
    return { x: p.x, y: 0, z: p.y };
  }

  consumeInteract(): boolean {
    return false;
  }

  dispose(): void {
    this.disposed = true;
    cancelAnimationFrame(this.raf);
    for (const fn of this.resizers) {
      fn();
    }
    this.resizers = [];
    this.controls?.detach();
    this.canvas?.remove();
  }
}
