// ============================================================================
//  Scene/CityInterior.ts —— 城内视图（Canvas 2D）
//
//  目标：点开自己的城 → 一眼看见「我这城建到什么程度了」。
//  四类建筑按当前等级画规模：
//    · 农田 → 开垦的地块（等级越高，地块越多）
//    · 兵营 → 营房 + 校场上的士卒（等级越高，营房与兵越多）
//    · 城墙 → 环城夯土/砖石墙（等级越高，墙越厚、垛口与角楼越多）
//    · 市集 → 摊肆（等级越高，摊子越多）
//  正在升级的那一类：金色工地标记 + 「升 X 级 · 剩 N 月」+ 工期进度格。
//
//  铁律：
//  · 只读 store，不写状态、不发指令（下令仍走城池面板）。
//  · Canvas 2D，不引入 3D / three.js / WebGL。
//  · 不引入「关卡 / 局」概念，不做亲征 / 第一人称。
//  · 不破坏疆域图与城池面板的既有交互：进城只是盖一层画布，关掉即恢复。
// ============================================================================

import { BUILDING_EFFECTS, BUILDING_NAMES } from '../Core/GameClient.js';
import type { GameStateStore } from '../Core/GameStateStore.js';
import type { City } from '../Core/Types.js';
import { factionColor } from '../Ui/Theme.js';

/* ------------------------------------------------------------------ 建筑编码 */
// 与 BuildResolver.SecondaryTargetId 一致：0 农田 1 兵营 2 城墙 3 市集
const FARM = 0;
const BARRACKS = 1;
const WALL = 2;
const MARKET = 3;

/** 建筑工期（月）：口径与后端 PlaceholderNumbers / BuildResolver.GetMonths 一致 */
const BUILD_MONTHS: readonly number[] = [1, 1, 2, 1];

/** 建筑等级上限（与 BuildingSystem.MaxLevel 一致） */
const MAX_LEVEL = 5;

/* ----------------------------------------------------- 下令后的乐观动画标记 */
// 指令要等下月结算才写进 state，但玩家点「下令」的瞬间就该看到工地动起来。
// 纯前端临时标记，TTL 后自动消失；结算后由真实的 upgradingBuilding 接管画面。
const pendingBuilds = new Map<string, number>();
const PENDING_TTL_MS = 15000;

export function markBuildPending(cityId: number, buildingIdx: number): void {
  pendingBuilds.set(`${cityId}:${buildingIdx}`, Date.now());
}

export function isBuildPending(cityId: number, buildingIdx: number): boolean {
  const key = `${cityId}:${buildingIdx}`;
  const t = pendingBuilds.get(key);
  if (t === undefined) {
    return false;
  }
  if (Date.now() - t > PENDING_TTL_MS) {
    pendingBuilds.delete(key);
    return false;
  }
  return true;
}

/* ---------------------------------------------------------------------- 配色 */
const INK = '#3a2d1a';
const INK_SOFT = '#75603d';
const PAPER = '#e9dcc0';
const PAPER_HI = '#f5ecd6';
const GOLD = '#b8892f';
const GOLD_HI = '#e0b457';
const STONE = '#8d867a';
const EARTH = '#a8895b';

/** 可命中的区域：四类建筑 */
type ZoneKind = 'farm' | 'barracks' | 'wall' | 'market';

interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
}

interface Zone {
  kind: ZoneKind;
  /** 建筑编码（0-3），与 upgradingBuilding 同口径 */
  index: number;
  rect: Rect;
}

export interface CityInteriorCallbacks {
  /** 用户点了「返回疆域图」 */
  onExit(): void;
}

export class CityInterior {
  private container!: HTMLElement;
  private canvas!: HTMLCanvasElement;
  private ctx!: CanvasRenderingContext2D;
  private store!: GameStateStore;
  private cb!: CityInteriorCallbacks;

  private cityId: number | null = null;
  private raf = 0;
  private dpr = 1;
  private vw = 0;
  private vh = 0;
  private disposed = false;

  /** 每帧重建的命中区（供鼠标悬停判定） */
  private zones: Zone[] = [];
  /** 「返回疆域图」按钮位置（每帧重建） */
  private backBtn: Rect | null = null;
  private hoverZone: Zone | null = null;
  private pointer = { x: 0, y: 0 };
  private pointerInside = false;

  private readonly onResize = (): void => {
    this.resize();
  };
  private readonly onPointerMove = (ev: PointerEvent): void => {
    const rect = this.canvas.getBoundingClientRect();
    this.pointer = { x: ev.clientX - rect.left, y: ev.clientY - rect.top };
    this.pointerInside = true;
  };
  private readonly onPointerLeave = (): void => {
    this.pointerInside = false;
    this.hoverZone = null;
  };
  private readonly onPointerDown = (ev: PointerEvent): void => {
    const rect = this.canvas.getBoundingClientRect();
    const x = ev.clientX - rect.left;
    const y = ev.clientY - rect.top;
    const btn = this.backBtn;
    if (btn && hitRect(btn, x, y)) {
      this.cb.onExit();
      return;
    }
    // 点空白处等价于取消悬停
    this.hoverZone = null;
  };

  // -------------------------------------------------------------------------
  //  初始化
  // -------------------------------------------------------------------------
  async init(
    container: HTMLElement,
    store: GameStateStore,
    callbacks: CityInteriorCallbacks,
  ): Promise<void> {
    this.container = container;
    this.store = store;
    this.cb = callbacks;

    this.canvas = document.createElement('canvas');
    // z-index 2：盖在疆域图画布（z-index auto）之上；display:none 时完全不占事件。
    this.canvas.style.cssText =
      'position:absolute;inset:0;width:100%;height:100%;display:none;z-index:2;touch-action:none;';
    container.appendChild(this.canvas);

    const ctx = this.canvas.getContext('2d');
    if (!ctx) {
      throw new Error('当前浏览器不支持 Canvas 2D');
    }
    this.ctx = ctx;

    this.resize();
    window.addEventListener('resize', this.onResize);
    this.canvas.addEventListener('pointermove', this.onPointerMove);
    this.canvas.addEventListener('pointerleave', this.onPointerLeave);
    this.canvas.addEventListener('pointerdown', this.onPointerDown);
  }

  private resize(): void {
    const rect = this.container.getBoundingClientRect();
    this.vw = Math.max(320, rect.width);
    this.vh = Math.max(240, rect.height);
    this.dpr = Math.min(window.devicePixelRatio || 1, 2);
    this.canvas.width = Math.round(this.vw * this.dpr);
    this.canvas.height = Math.round(this.vh * this.dpr);
  }

  // -------------------------------------------------------------------------
  //  公开方法
  // -------------------------------------------------------------------------
  /** 进入城内视图。返回是否成功（未开局 / 城池不存在则 false）。 */
  open(cityId: number): boolean {
    if (!this.store.ready) {
      return false;
    }
    if (!this.store.city(cityId)) {
      return false;
    }
    this.cityId = cityId;
    this.canvas.style.display = 'block';
    this.resize();
    if (!this.raf) {
      this.startLoop();
    }
    return true;
  }

  /** 退出城内视图，回到疆域图。 */
  close(): void {
    if (this.cityId === null) {
      return;
    }
    this.cityId = null;
    this.hoverZone = null;
    this.canvas.style.display = 'none';
    if (this.raf) {
      cancelAnimationFrame(this.raf);
      this.raf = 0;
    }
  }

  get isOpen(): boolean {
    return this.cityId !== null;
  }

  dispose(): void {
    this.disposed = true;
    if (this.raf) {
      cancelAnimationFrame(this.raf);
      this.raf = 0;
    }
    window.removeEventListener('resize', this.onResize);
    this.canvas.removeEventListener('pointermove', this.onPointerMove);
    this.canvas.removeEventListener('pointerleave', this.onPointerLeave);
    this.canvas.removeEventListener('pointerdown', this.onPointerDown);
    this.canvas.remove();
  }

  // -------------------------------------------------------------------------
  //  每帧绘制
  // -------------------------------------------------------------------------
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

  private frame(now: number): void {
    this.draw(now);
    if (this.pointerInside) {
      this.hoverZone = this.pickZone(this.pointer.x, this.pointer.y);
    }
  }

  private currentCity(): Readonly<City> | undefined {
    return this.cityId === null ? undefined : this.store.city(this.cityId);
  }

  private draw(now: number): void {
    const ctx = this.ctx;
    ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    ctx.clearRect(0, 0, this.vw, this.vh);
    ctx.fillStyle = '#161a20';
    ctx.fillRect(0, 0, this.vw, this.vh);

    const sheet = this.sheetRect();
    this.drawSheet(ctx, sheet);

    const city = this.currentCity();
    if (!city) {
      this.text(ctx, '此城已不存在', sheet.x + sheet.w / 2, sheet.y + sheet.h / 2, 16, INK);
      return;
    }

    this.drawTitle(ctx, sheet, city);
    this.drawChips(ctx, sheet, city);
    this.drawCity(ctx, sheet, city, now);
    this.drawBackButton(ctx, sheet, now);
    this.drawFooter(ctx, sheet);
    this.drawTooltip(ctx, city);
  }

  /** 图页范围（顶栏与底栏让位） */
  private sheetRect(): Rect {
    const top = 68;
    const bottom = 58;
    // 左侧城池面板开着时，图页整体右移，别让面板把农田那一片压住
    const inset = this.sideInset();
    return {
      x: 14 + inset,
      y: top,
      w: Math.max(280, this.vw - 28 - inset),
      h: Math.max(200, this.vh - top - bottom),
    };
  }

  /** 左侧城池面板的占位宽度（面板是 HUD 层的 DOM，按实际宽度让位最稳） */
  private sideInset(): number {
    const side = document.querySelector('.sg-side');
    if (!side || side.classList.contains('sg-hidden')) {
      return 0;
    }
    const r = side.getBoundingClientRect();
    return r.width > 0 ? r.right + 14 : 0;
  }

  // -------------------------------------------------------------------------
  //  纸面
  // -------------------------------------------------------------------------
  private drawSheet(ctx: CanvasRenderingContext2D, sheet: Rect): void {
    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,0.55)';
    ctx.shadowBlur = 26;
    ctx.fillStyle = PAPER;
    ctx.fillRect(sheet.x, sheet.y, sheet.w, sheet.h);
    ctx.restore();

    // 纸纹：极淡的横向纤维
    ctx.save();
    ctx.beginPath();
    ctx.rect(sheet.x, sheet.y, sheet.w, sheet.h);
    ctx.clip();
    ctx.globalAlpha = 0.05;
    ctx.strokeStyle = '#6b5636';
    ctx.lineWidth = 1;
    for (let i = 0; i < 90; i++) {
      const y = sheet.y + (i * 37) % sheet.h;
      const x = sheet.x + ((i * 113) % Math.max(1, sheet.w - 60));
      ctx.beginPath();
      ctx.moveTo(x, y);
      ctx.lineTo(x + 30 + ((i * 17) % 40), y);
      ctx.stroke();
    }
    ctx.restore();

    // 城郭平面网格
    ctx.save();
    ctx.strokeStyle = 'rgba(90,72,44,0.08)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let x = sheet.x; x <= sheet.x + sheet.w; x += 36) {
      ctx.moveTo(x, sheet.y);
      ctx.lineTo(x, sheet.y + sheet.h);
    }
    for (let y = sheet.y; y <= sheet.y + sheet.h; y += 36) {
      ctx.moveTo(sheet.x, y);
      ctx.lineTo(sheet.x + sheet.w, y);
    }
    ctx.stroke();
    ctx.restore();

    ctx.strokeStyle = 'rgba(78,60,34,0.6)';
    ctx.lineWidth = 2;
    ctx.strokeRect(sheet.x, sheet.y, sheet.w, sheet.h);
    ctx.strokeStyle = 'rgba(78,60,34,0.22)';
    ctx.lineWidth = 1;
    ctx.strokeRect(sheet.x + 6, sheet.y + 6, sheet.w - 12, sheet.h - 12);
  }

  // -------------------------------------------------------------------------
  //  标题
  // -------------------------------------------------------------------------
  private drawTitle(ctx: CanvasRenderingContext2D, sheet: Rect, city: Readonly<City>): void {
    const state = this.store.state;
    const owner = state.factions.find((f) => f.id === city.ownerFactionId);
    const y = sheet.y + 22;

    const name = `${city.isCapital ? '★ ' : ''}${city.name}`;
    this.text(ctx, name, sheet.x + 22, y, 19, INK, 'left', 700);
    const nameW = this.measure(ctx, name, 19, 700);

    this.text(
      ctx,
      owner?.name ?? '无主',
      sheet.x + 32 + nameW,
      y + 1,
      13,
      factionColor(city.ownerFactionId),
      'left',
    );
    const ownerW = this.measure(ctx, owner?.name ?? '无主', 13);

    const stat = `人口 ${city.population}/${city.maxPopulation}　城防 ${city.defense}/${city.maxDefense}　第 ${state.year} 年 ${state.month} 月`;
    this.text(ctx, stat, sheet.x + 46 + nameW + ownerW, y + 1, 13, INK_SOFT, 'left');
  }

  // -------------------------------------------------------------------------
  //  四类建筑状态条（精确等级 + 等级格 + 在建进度）
  // -------------------------------------------------------------------------
  private drawChips(ctx: CanvasRenderingContext2D, sheet: Rect, city: Readonly<City>): void {
    const y = sheet.y + 38;
    const h = 54;
    const gap = 10;
    const w = Math.min(210, (sheet.w - 44 - gap * 3) / 4);

    for (let i = 0; i < BUILDING_NAMES.length; i++) {
      const x = sheet.x + 22 + i * (w + gap);
      const level = this.levelOf(city, i);
      const upgrading = city.upgradingBuilding === i && city.upgradeMonthsRemaining > 0;

      roundRect(ctx, x, y, w, h, 8);
      ctx.fillStyle = upgrading ? 'rgba(224,180,87,0.30)' : 'rgba(255,252,240,0.62)';
      ctx.fill();
      ctx.strokeStyle = upgrading ? GOLD : 'rgba(90,72,44,0.45)';
      ctx.lineWidth = upgrading ? 2 : 1;
      ctx.stroke();

      this.text(ctx, `${BUILDING_NAMES[i]} Lv${level}`, x + 10, y + 17, 14, INK, 'left', 700);
      const labelW = this.measure(ctx, `${BUILDING_NAMES[i]} Lv${level}`, 14, 700);
      this.pips(ctx, x + 16 + labelW, y + 17, level, MAX_LEVEL, INK);

      if (upgrading) {
        const total = BUILD_MONTHS[i] ?? 1;
        const done = Math.max(0, Math.min(total, total - city.upgradeMonthsRemaining));
        this.text(
          ctx,
          `升 ${level + 1} 级 · 剩 ${city.upgradeMonthsRemaining} 月`,
          x + 10,
          y + 38,
          12,
          '#8a6320',
          'left',
        );
        const segW = Math.max(8, (w - 20) / total - 4);
        for (let s = 0; s < total; s++) {
          const sx = x + 10 + s * (segW + 4);
          roundRect(ctx, sx, y + 47, segW, 4, 2);
          ctx.fillStyle = s < done ? GOLD_HI : 'rgba(90,72,44,0.2)';
          ctx.fill();
        }
      } else {
        this.text(ctx, BUILDING_EFFECTS[i] ?? '', x + 10, y + 38, 12, INK_SOFT, 'left');
      }
    }
  }

  // -------------------------------------------------------------------------
  //  城内平面
  // -------------------------------------------------------------------------
  private drawCity(
    ctx: CanvasRenderingContext2D,
    sheet: Rect,
    city: Readonly<City>,
    now: number,
  ): void {
    const areaTop = sheet.y + 102;
    const areaH = Math.max(180, sheet.h - 102 - 26);
    const compW = Math.min(sheet.w - 44, 1160);
    const compH = Math.min(areaH, 620);
    const compX = sheet.x + (sheet.w - compW) / 2;
    const compY = areaTop + (areaH - compH) / 2;
    const comp: Rect = { x: compX, y: compY, w: compW, h: compH };

    const wallLevel = city.fortifyLevel;
    const wt = wallLevel === 0 ? 5 : 10 + wallLevel * 4;
    const court: Rect = {
      x: compX + wt,
      y: compY + wt,
      w: compW - wt * 2,
      h: compH - wt * 2,
    };

    this.zones = [];

    // ---- 城墙（外圈）----
    this.drawWall(ctx, comp, court, wt, wallLevel, city, now);

    // ---- 城内分区 ----
    const pad = 12;
    const inner: Rect = {
      x: court.x + pad,
      y: court.y + pad,
      w: court.w - pad * 2,
      h: court.h - pad * 2,
    };
    const gap = 12;
    const rowH = (inner.h - gap) / 2;
    const colW = (inner.w - gap * 2) / 3;

    const farm: Rect = { x: inner.x, y: inner.y, w: colW, h: rowH };
    const keep: Rect = { x: inner.x + colW + gap, y: inner.y, w: colW, h: rowH };
    const market: Rect = { x: inner.x + (colW + gap) * 2, y: inner.y, w: colW, h: rowH };
    const barracks: Rect = { x: inner.x, y: inner.y + rowH + gap, w: inner.w, h: rowH };

    // 农田
    this.drawFarm(ctx, farm, city.farmLevel, city.upgradingBuilding === FARM, city, now);
    this.zones.push({ kind: 'farm', index: FARM, rect: farm });
    // 城楼（装饰：随城池总等级变大）
    this.drawKeep(ctx, keep, city, now);
    // 市集
    this.drawMarket(ctx, market, city.marketLevel, city.upgradingBuilding === MARKET, city, now);
    this.zones.push({ kind: 'market', index: MARKET, rect: market });
    // 兵营
    this.drawBarracks(
      ctx,
      barracks,
      city.barracksLevel,
      city.upgradingBuilding === BARRACKS,
      city,
      now,
    );
    this.zones.push({ kind: 'barracks', index: BARRACKS, rect: barracks });
    // 城墙（环带）
    this.zones.push({ kind: 'wall', index: WALL, rect: comp });
  }

  // -------------------------------------------------------------------------
  //  城墙
  // -------------------------------------------------------------------------
  private drawWall(
    ctx: CanvasRenderingContext2D,
    comp: Rect,
    court: Rect,
    wt: number,
    level: number,
    city: Readonly<City>,
    now: number,
  ): void {
    const upgrading = city.upgradingBuilding === WALL && city.upgradeMonthsRemaining > 0;

    if (level === 0) {
      // 还没有城墙：木栅一圈
      ctx.save();
      ctx.setLineDash([8, 6]);
      ctx.strokeStyle = 'rgba(90,66,38,0.75)';
      ctx.lineWidth = 2;
      ctx.strokeRect(comp.x, comp.y, comp.w, comp.h);
      ctx.setLineDash([]);
      ctx.strokeStyle = '#6b4a2c';
      ctx.lineWidth = 2;
      for (let x = comp.x + 14; x < comp.x + comp.w; x += 26) {
        this.stake(ctx, x, comp.y, 1);
        this.stake(ctx, x, comp.y + comp.h, -1);
      }
      for (let y = comp.y + 14; y < comp.y + comp.h; y += 26) {
        this.stake(ctx, comp.x, y, 1);
        this.stake(ctx, comp.x + comp.w, y, -1);
      }
      ctx.restore();
    } else {
      const stone = level >= 3 ? STONE : EARTH;
      // 墙体：外圈 - 内圈
      ctx.save();
      ctx.beginPath();
      ctx.rect(comp.x, comp.y, comp.w, comp.h);
      ctx.rect(court.x, court.y, court.w, court.h);
      ctx.fillStyle = stone;
      ctx.fill('evenodd');
      ctx.restore();

      // 墙顶走道：沿墙体中线压一道亮色，墙才看得出「有厚度」
      ctx.save();
      ctx.beginPath();
      ctx.rect(comp.x, comp.y, comp.w, comp.h);
      ctx.rect(court.x, court.y, court.w, court.h);
      ctx.clip('evenodd');
      ctx.fillStyle = 'rgba(255,250,235,0.35)';
      ctx.fillRect(comp.x, comp.y + wt * 0.32, comp.w, wt * 0.36);
      ctx.fillRect(comp.x + wt * 0.32, comp.y, wt * 0.36, comp.h);
      ctx.restore();

      // 垛口：沿外缘向外突出的方齿
      ctx.fillStyle = stone;
      const merlon = 7;
      const step = 16;
      for (let x = comp.x + 6; x < comp.x + comp.w - 6; x += step) {
        ctx.fillRect(x, comp.y - merlon, 9, merlon);
        ctx.fillRect(x, comp.y + comp.h, 9, merlon);
      }
      for (let y = comp.y + 6; y < comp.y + comp.h - 6; y += step) {
        ctx.fillRect(comp.x - merlon, y, merlon, 9);
        ctx.fillRect(comp.x + comp.w, y, merlon, 9);
      }

      // 内外沿描边
      ctx.strokeStyle = 'rgba(48,37,20,0.95)';
      ctx.lineWidth = 2.6;
      ctx.strokeRect(comp.x, comp.y, comp.w, comp.h);
      ctx.strokeRect(court.x, court.y, court.w, court.h);

      // 角楼（2 级起）与沿边敌楼（4 级起）
      if (level >= 2) {
        const s = wt + 10;
        for (const [cx, cy] of [
          [comp.x, comp.y],
          [comp.x + comp.w, comp.y],
          [comp.x, comp.y + comp.h],
          [comp.x + comp.w, comp.y + comp.h],
        ] as const) {
          this.tower(ctx, cx, cy, s);
        }
      }
      if (level >= 4) {
        const s = wt + 4;
        this.tower(ctx, comp.x + comp.w / 2, comp.y, s);
        this.tower(ctx, comp.x + comp.w / 2, comp.y + comp.h, s);
        this.tower(ctx, comp.x, comp.y + comp.h / 2, s);
        this.tower(ctx, comp.x + comp.w, comp.y + comp.h / 2, s);
      }

      // 城门（1 级起）：底部中间开口 + 门楼
      if (level >= 1) {
        const gw = Math.min(78, comp.w * 0.16);
        const gh = wt + 8;
        const gx = comp.x + comp.w / 2 - gw / 2;
        const gy = comp.y + comp.h - wt;
        ctx.fillStyle = PAPER;
        ctx.fillRect(gx, gy, gw, wt);
        ctx.strokeStyle = 'rgba(58,45,26,0.85)';
        ctx.lineWidth = 2;
        ctx.strokeRect(gx, gy, gw, wt);
        // 门楼
        ctx.fillStyle = '#7d5a38';
        ctx.fillRect(gx - 6, gy - 6, gw + 12, 10);
        ctx.strokeStyle = 'rgba(58,45,26,0.85)';
        ctx.strokeRect(gx - 6, gy - 6, gw + 12, 10);
      }
    }

    // 城墙铭牌
    const label = upgrading ? `城墙 Lv${level} · 在建` : `城墙 Lv${level}`;
    const w = this.measure(ctx, label, 13, 700) + 22;
    const h = 20;
    const bx = comp.x + comp.w / 2 - w / 2;
    const by = comp.y + wt / 2 - h / 2;
    roundRect(ctx, bx, by, w, h, 10);
    ctx.fillStyle = upgrading ? 'rgba(232,190,92,0.95)' : 'rgba(247,240,220,0.94)';
    ctx.fill();
    ctx.strokeStyle = upgrading ? '#6b4a10' : 'rgba(58,45,26,0.7)';
    ctx.lineWidth = upgrading ? 2 : 1;
    ctx.stroke();
    this.text(ctx, label, comp.x + comp.w / 2, by + h / 2, 13, upgrading ? '#4a3208' : INK, 'center', 700);

    // 在建：整圈金光 + 吊车
    if (upgrading) {
      const pulse = 0.55 + 0.35 * Math.sin(now / 260);
      ctx.save();
      ctx.fillStyle = `rgba(224,180,87,${(0.1 * pulse).toFixed(3)})`;
      ctx.fillRect(comp.x, comp.y, comp.w, comp.h);
      ctx.strokeStyle = `rgba(196,140,40,${pulse.toFixed(3)})`;
      ctx.lineWidth = 4;
      ctx.setLineDash([12, 8]);
      ctx.lineDashOffset = -(now / 40) % 20;
      ctx.strokeRect(comp.x - 6, comp.y - 6, comp.w + 12, comp.h + 12);
      ctx.restore();
      this.crane(ctx, comp.x + comp.w / 2 + w / 2 + 26, comp.y + wt / 2, 30, '#7a5510');
      this.text(
        ctx,
        `升 ${level + 1} 级 · 剩 ${city.upgradeMonthsRemaining} 月`,
        comp.x + comp.w / 2 + w / 2 + 46,
        comp.y + wt / 2,
        12,
        '#7a5510',
        'left',
        700,
        'rgba(247,240,220,0.95)',
      );
    }
  }

  /** 木栅尖桩 */
  private stake(ctx: CanvasRenderingContext2D, x: number, y: number, dir: number): void {
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineTo(x, y + dir * 9);
    ctx.stroke();
  }

  /** 角楼 / 敌楼 */
  private tower(ctx: CanvasRenderingContext2D, x: number, y: number, s: number): void {
    const w = s;
    const h = s * 0.8;
    const rx = x - w / 2;
    const ry = y - h / 2;
    roundRect(ctx, rx, ry, w, h, 3);
    ctx.fillStyle = '#8a8278';
    ctx.fill();
    ctx.strokeStyle = 'rgba(58,45,26,0.9)';
    ctx.lineWidth = 1.6;
    ctx.stroke();
    // 顶
    ctx.fillStyle = '#7d4a30';
    ctx.fillRect(rx - 3, ry - 5, w + 6, 6);
    ctx.strokeRect(rx - 3, ry - 5, w + 6, 6);
  }

  // -------------------------------------------------------------------------
  //  农田
  // -------------------------------------------------------------------------
  private drawFarm(
    ctx: CanvasRenderingContext2D,
    zone: Rect,
    level: number,
    upgrading: boolean,
    city: Readonly<City>,
    now: number,
  ): void {
    this.zoneFrame(ctx, zone, `农田 Lv${level}`, upgrading, city, now);

    const body: Rect = {
      x: zone.x + 6,
      y: zone.y + 26,
      w: zone.w - 12,
      h: zone.h - 32,
    };
    const gap = 6;
    const cellW = (body.w - gap * 2) / 3;
    const cellH = (body.h - gap) / 2;

    for (let i = 0; i < 6; i++) {
      const col = i % 3;
      const row = (i / 3) | 0;
      const px = body.x + col * (cellW + gap);
      const py = body.y + row * (cellH + gap);
      const filled = i < level;

      if (filled) {
        ctx.fillStyle = '#9cae5f';
        ctx.fillRect(px, py, cellW, cellH);
        // 垄沟
        ctx.strokeStyle = 'rgba(78,96,40,0.75)';
        ctx.lineWidth = 1.4;
        ctx.beginPath();
        for (let k = 1; k < 4; k++) {
          const ly = py + (cellH * k) / 4;
          ctx.moveTo(px + 3, ly);
          ctx.lineTo(px + cellW - 3, ly);
        }
        ctx.stroke();
        // 作物
        ctx.strokeStyle = '#3f5220';
        ctx.lineWidth = 1.6;
        ctx.beginPath();
        const rows = 3;
        for (let r = 0; r < rows; r++) {
          const ly = py + (cellH * (r + 0.5)) / rows;
          for (let c = 0; c < 4; c++) {
            const cx = px + ((cellW / 5) * (c + 1)) / 1.1;
            ctx.moveTo(cx, ly + 5);
            ctx.lineTo(cx, ly - 5);
          }
        }
        ctx.stroke();
        ctx.strokeStyle = 'rgba(58,45,26,0.8)';
        ctx.lineWidth = 1.4;
        ctx.strokeRect(px, py, cellW, cellH);
      } else {
        ctx.fillStyle = 'rgba(120,100,70,0.07)';
        ctx.fillRect(px, py, cellW, cellH);
        ctx.save();
        ctx.setLineDash([5, 4]);
        ctx.strokeStyle = 'rgba(90,72,44,0.4)';
        ctx.lineWidth = 1.2;
        ctx.strokeRect(px, py, cellW, cellH);
        ctx.restore();
      }
    }
  }

  // -------------------------------------------------------------------------
  //  兵营
  // -------------------------------------------------------------------------
  private drawBarracks(
    ctx: CanvasRenderingContext2D,
    zone: Rect,
    level: number,
    upgrading: boolean,
    city: Readonly<City>,
    now: number,
  ): void {
    this.zoneFrame(ctx, zone, `兵营 Lv${level}`, upgrading, city, now);

    const body: Rect = {
      x: zone.x + 6,
      y: zone.y + 26,
      w: zone.w - 12,
      h: zone.h - 32,
    };

    // 左：营房（等级越高，营房越多越大）
    const hutW = Math.min(58, body.w * 0.115);
    const hutH = Math.min(42, body.h * 0.3);
    for (let i = 0; i < level; i++) {
      const perRow = body.w > 620 ? 5 : 3;
      const col = i % perRow;
      const row = (i / perRow) | 0;
      const hx = body.x + 8 + col * (hutW + 14);
      const hy = body.y + 10 + row * (hutH + 14);
      this.hut(ctx, hx, hy, hutW, hutH);
    }

    // 右：校场
    const yardW = body.w * 0.42;
    const yardH = Math.min(body.h - 16, 120);
    const yardX = body.x + body.w - yardW - 6;
    const yardY = body.y + (body.h - yardH) / 2;
    ctx.fillStyle = 'rgba(178,148,104,0.5)';
    roundRect(ctx, yardX, yardY, yardW, yardH, 6);
    ctx.fill();
    ctx.save();
    ctx.setLineDash([6, 5]);
    ctx.strokeStyle = 'rgba(90,72,44,0.5)';
    ctx.lineWidth = 1.3;
    ctx.stroke();
    ctx.restore();

    // 校场上的士卒：等级越高越多
    const soldiers = level * 2;
    const perRow = 6;
    for (let i = 0; i < soldiers; i++) {
      const col = i % perRow;
      const row = (i / perRow) | 0;
      const sx = yardX + yardW * ((col + 0.5) / perRow);
      const sy = yardY + 26 + row * 26;
      if (sy > yardY + yardH - 12) {
        break;
      }
      this.soldier(ctx, sx, sy);
    }
    this.text(ctx, '校场', yardX + yardW / 2, yardY + yardH - 10, 11, INK_SOFT, 'center');

    if (level === 0) {
      this.text(ctx, '尚无营房', body.x + body.w * 0.25, body.y + body.h / 2, 13, INK_SOFT, 'center');
    }
  }

  /** 营房：土墙 + 茅顶 + 门 */
  private hut(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number): void {
    ctx.fillStyle = '#a67c4e';
    ctx.fillRect(x, y + h * 0.35, w, h * 0.65);
    ctx.strokeStyle = 'rgba(58,45,26,0.85)';
    ctx.lineWidth = 1.5;
    ctx.strokeRect(x, y + h * 0.35, w, h * 0.65);
    // 屋顶
    ctx.beginPath();
    ctx.moveTo(x - 4, y + h * 0.38);
    ctx.lineTo(x + w / 2, y);
    ctx.lineTo(x + w + 4, y + h * 0.38);
    ctx.closePath();
    ctx.fillStyle = '#6f4c2e';
    ctx.fill();
    ctx.stroke();
    // 门
    ctx.fillStyle = 'rgba(50,36,20,0.8)';
    ctx.fillRect(x + w * 0.38, y + h * 0.68, w * 0.24, h * 0.32);
  }

  /** 士卒：头 + 身 + 枪 */
  private soldier(ctx: CanvasRenderingContext2D, x: number, y: number): void {
    ctx.strokeStyle = 'rgba(58,45,26,0.9)';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.arc(x, y - 9, 3.2, 0, Math.PI * 2);
    ctx.stroke();
    ctx.fillStyle = '#7d3a2c';
    ctx.beginPath();
    ctx.moveTo(x - 3.6, y + 5);
    ctx.lineTo(x - 2.4, y - 5);
    ctx.lineTo(x + 2.4, y - 5);
    ctx.lineTo(x + 3.6, y + 5);
    ctx.closePath();
    ctx.fill();
    ctx.stroke();
    // 枪
    ctx.beginPath();
    ctx.moveTo(x + 5, y + 5);
    ctx.lineTo(x + 5, y - 14);
    ctx.stroke();
  }

  // -------------------------------------------------------------------------
  //  市集
  // -------------------------------------------------------------------------
  private drawMarket(
    ctx: CanvasRenderingContext2D,
    zone: Rect,
    level: number,
    upgrading: boolean,
    city: Readonly<City>,
    now: number,
  ): void {
    this.zoneFrame(ctx, zone, `市集 Lv${level}`, upgrading, city, now);

    const body: Rect = {
      x: zone.x + 6,
      y: zone.y + 26,
      w: zone.w - 12,
      h: zone.h - 32,
    };

    const awnings = ['#b8563f', '#c08a3f', '#7c8a4a', '#8a6aa8', '#3f7a8a'];
    const stallH = Math.min(46, body.h / 5.4);
    for (let i = 0; i < level; i++) {
      const sx = body.x + 10;
      const sy = body.y + 6 + i * (stallH + 6);
      this.stall(ctx, sx, sy, body.w - 24, stallH, awnings[i % awnings.length]);
    }

    if (level === 0) {
      // 空市：几块石头
      ctx.fillStyle = 'rgba(120,100,70,0.35)';
      for (let i = 0; i < 5; i++) {
        ctx.beginPath();
        ctx.ellipse(
          body.x + 20 + ((i * 37) % Math.max(1, body.w - 40)),
          body.y + 18 + ((i * 23) % Math.max(1, body.h - 36)),
          4,
          3,
          0,
          0,
          Math.PI * 2,
        );
        ctx.fill();
      }
      this.text(ctx, '空市', body.x + body.w / 2, body.y + body.h / 2, 13, INK_SOFT, 'center');
    }
  }

  /** 摊子：柜台 + 篷顶 + 货 */
  private stall(
    ctx: CanvasRenderingContext2D,
    x: number,
    y: number,
    w: number,
    h: number,
    color: string,
  ): void {
    const counterY = y + h * 0.58;
    // 柜台
    ctx.fillStyle = '#8a6440';
    ctx.fillRect(x, counterY, w, h * 0.3);
    ctx.strokeStyle = 'rgba(58,45,26,0.8)';
    ctx.lineWidth = 1.4;
    ctx.strokeRect(x, counterY, w, h * 0.3);
    // 支杆
    ctx.beginPath();
    ctx.moveTo(x + 3, counterY);
    ctx.lineTo(x + 3, y + 4);
    ctx.moveTo(x + w - 3, counterY);
    ctx.lineTo(x + w - 3, y + 4);
    ctx.stroke();
    // 篷顶
    ctx.beginPath();
    ctx.moveTo(x - 4, y + 6);
    ctx.quadraticCurveTo(x + w / 2, y - 8, x + w + 4, y + 6);
    ctx.closePath();
    ctx.fillStyle = color;
    ctx.fill();
    ctx.stroke();
    // 货
    const goods = ['#c8a24a', '#8fae5a', '#b8563f', '#d8c07a'];
    for (let i = 0; i < 3; i++) {
      ctx.fillStyle = goods[(i + color.length) % goods.length];
      ctx.beginPath();
      ctx.arc(x + w * (0.28 + i * 0.22), counterY - 3, 3.4, 0, Math.PI * 2);
      ctx.fill();
      ctx.stroke();
    }
  }

  // -------------------------------------------------------------------------
  //  城楼（装饰：随城池总等级变大，四种建筑越高，城楼越气派）
  // -------------------------------------------------------------------------
  private drawKeep(
    ctx: CanvasRenderingContext2D,
    zone: Rect,
    city: Readonly<City>,
    now: number,
  ): void {
    const total =
      city.farmLevel + city.barracksLevel + city.fortifyLevel + city.marketLevel;
    const cx = zone.x + zone.w / 2;
    const cy = zone.y + 34 + (zone.h - 44) / 2;

    if (total === 0) {
      ctx.save();
      ctx.setLineDash([7, 6]);
      ctx.strokeStyle = 'rgba(90,72,44,0.45)';
      ctx.lineWidth = 1.5;
      ctx.strokeRect(cx - 60, cy - 26, 120, 52);
      ctx.restore();
      this.text(ctx, '尚无城郭', cx, cy, 13, INK_SOFT, 'center');
      return;
    }

    const w = 54 + total * 4.2;
    const h = 38 + total * 1.9;

    // 台基
    ctx.fillStyle = '#b3a996';
    roundRect(ctx, cx - w * 0.62, cy + h * 0.24, w * 1.24, h * 0.24, 3);
    ctx.fill();
    ctx.strokeStyle = 'rgba(58,45,26,0.85)';
    ctx.lineWidth = 1.6;
    ctx.stroke();

    // 殿身
    ctx.fillStyle = '#a86f47';
    ctx.fillRect(cx - w / 2, cy - h * 0.05, w, h * 0.32);
    ctx.strokeRect(cx - w / 2, cy - h * 0.05, w, h * 0.32);

    // 屋顶
    this.roof(ctx, cx, cy - h * 0.05, w, h * 0.45, '#8c3a2c');
    if (total >= 12) {
      // 重檐
      this.roof(ctx, cx, cy - h * 0.34, w * 0.6, h * 0.3, '#7d3327');
    }

    // 旗
    ctx.strokeStyle = 'rgba(58,45,26,0.9)';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(cx + w * 0.34, cy - h * 0.4);
    ctx.lineTo(cx + w * 0.34, cy - h * 0.95);
    ctx.stroke();
    ctx.fillStyle = '#d8b04a';
    ctx.beginPath();
    ctx.moveTo(cx + w * 0.34, cy - h * 0.95);
    ctx.lineTo(cx + w * 0.34 + 20, cy - h * 0.95 + 6);
    ctx.lineTo(cx + w * 0.34, cy - h * 0.95 + 13);
    ctx.closePath();
    ctx.fill();
    ctx.stroke();
  }

  /** 中式屋顶（简笔） */
  private roof(
    ctx: CanvasRenderingContext2D,
    cx: number,
    baseY: number,
    w: number,
    h: number,
    color: string,
  ): void {
    const e = w * 0.12;
    ctx.beginPath();
    ctx.moveTo(cx - w / 2 - e, baseY);
    ctx.quadraticCurveTo(cx - w * 0.18, baseY - h * 0.95, cx, baseY - h);
    ctx.quadraticCurveTo(cx + w * 0.18, baseY - h * 0.95, cx + w / 2 + e, baseY);
    ctx.lineTo(cx + w / 2, baseY);
    ctx.lineTo(cx - w / 2, baseY);
    ctx.closePath();
    ctx.fillStyle = color;
    ctx.fill();
    ctx.strokeStyle = 'rgba(58,45,26,0.9)';
    ctx.lineWidth = 1.6;
    ctx.stroke();
  }

  // -------------------------------------------------------------------------
  //  分区外框 / 在建标记
  // -------------------------------------------------------------------------
  private zoneFrame(
    ctx: CanvasRenderingContext2D,
    zone: Rect,
    label: string,
    upgrading: boolean,
    city: Readonly<City>,
    now: number,
  ): void {
    const hovered = this.hoverZone?.rect === zone;

    ctx.save();
    roundRect(ctx, zone.x, zone.y, zone.w, zone.h, 8);
    ctx.fillStyle = hovered ? 'rgba(255,250,235,0.55)' : 'rgba(214,197,158,0.4)';
    ctx.fill();
    ctx.strokeStyle = 'rgba(90,72,44,0.45)';
    ctx.lineWidth = 1.2;
    ctx.stroke();
    ctx.restore();

    this.text(ctx, label, zone.x + 8, zone.y + 13, 13, INK, 'left', 700);

    const idx = this.indexOfZone(zone);
    const pending = isBuildPending(city.id, idx);
    const active = upgrading || pending;
    if (!active) {
      return;
    }

    // 在建 / 已下令：金色虚线框 + 提示
    const pulse = 0.5 + 0.4 * Math.sin(now / 240);
    ctx.save();
    ctx.strokeStyle = `rgba(184,137,47,${pulse.toFixed(3)})`;
    ctx.lineWidth = 2.4;
    ctx.setLineDash([9, 6]);
    ctx.lineDashOffset = -(now / 36) % 15;
    roundRect(ctx, zone.x + 1.5, zone.y + 1.5, zone.w - 3, zone.h - 3, 8);
    ctx.stroke();
    ctx.restore();

    // 农田派农夫，其余工地派建筑工；吊车只出现在真正的工地上
    const kind = this.zones.find((z) => z.index === idx)?.kind;
    if (kind === 'farm') {
      this.farmers(ctx, zone, now, idx);
    } else {
      this.crane(ctx, zone.x + zone.w - 24, zone.y + 30, 24, '#8a6320');
      this.builders(ctx, zone, now, idx);
    }
    const tip = upgrading
      ? `升 ${this.levelOf(city, idx) + 1} 级 · 剩 ${city.upgradeMonthsRemaining} 月`
      : '已下令 · 下月开工';
    this.text(ctx, tip, zone.x + 8, zone.y + zone.h - 12, 12, '#8a6320', 'left', 700);
  }

  /**
   * 农田里的农夫：固定在自己的地块上干活（不再来回乱逛），
   * 戴斗笠、穿短褐、举锄翻土，落锄时溅起泥土。
   */
  private farmers(ctx: CanvasRenderingContext2D, zone: Rect, now: number, seed: number): void {
    const slots = this.workSlots(zone, 3);
    ctx.save();
    slots.forEach((x, i) => {
      const phase = (Math.sin(now / 260 + i * 1.9 + seed) + 1) / 2; // 0~1 挥锄
      const strike = phase > 0.92;
      this.drawPerson(ctx, {
        x,
        baseY: zone.y + zone.h - 20,
        phase,
        hat: 'straw',
        tunic: '#6b7a52',
        tool: 'hoe',
        toolPhase: phase,
      });
      if (strike) {
        ctx.fillStyle = 'rgba(122,96,58,0.5)';
        ctx.beginPath();
        ctx.arc(x + 9, zone.y + zone.h - 21, 3.2, 0, Math.PI * 2);
        ctx.fill();
      }
    });
    ctx.restore();
  }

  /**
   * 建筑工地上的工匠：固定工位挥锤砸木料，落点蹦木屑。
   * 裹幞头、穿褐衣、腰系布带，和农夫造型区分开。
   */
  private builders(ctx: CanvasRenderingContext2D, zone: Rect, now: number, seed: number): void {
    const slots = this.workSlots(zone, 3);
    ctx.save();
    slots.forEach((x, i) => {
      const phase = (Math.sin(now / 220 + i * 2.3 + seed) + 1) / 2; // 0~1 挥锤
      const strike = phase > 0.9;
      this.drawPerson(ctx, {
        x: x - 6,
        baseY: zone.y + zone.h - 20,
        phase,
        hat: 'headband',
        tunic: '#8a6a44',
        tool: 'hammer',
        toolPhase: phase,
      });
      if (strike) {
        for (let k = 0; k < 3; k++) {
          ctx.fillStyle = `rgba(168,137,91,${0.6 - k * 0.15})`;
          ctx.beginPath();
          ctx.arc(x + 4 + k * 3, zone.y + zone.h - 24 - k * 2, 1.6, 0, Math.PI * 2);
          ctx.fill();
        }
      }
    });
    ctx.restore();
  }

  /** 三个固定工位：人在原地干活，不闲逛 */
  private workSlots(zone: Rect, count: number): number[] {
    const left = zone.x + 30;
    const right = zone.x + zone.w - 56;
    const out: number[] = [];
    for (let i = 0; i < count; i++) {
      out.push(left + ((right - left) * i) / Math.max(1, count - 1));
    }
    return out;
  }

  /**
   * 画一个能看出人形的干活小人（约 30px 高）：
   * 头 + 帽、躯干短褐 + 腰带、双腿、持工具的手臂，上半身随挥具前倾。
   */
  private drawPerson(
    ctx: CanvasRenderingContext2D,
    o: {
      x: number;
      baseY: number;
      phase: number;
      hat: 'straw' | 'headband';
      tunic: string;
      tool: 'hoe' | 'hammer';
      toolPhase: number;
    },
  ): void {
    const { x, baseY } = o;
    ctx.save();
    ctx.translate(x, baseY);

    // 影子
    ctx.fillStyle = 'rgba(60,48,30,0.18)';
    ctx.beginPath();
    ctx.ellipse(0, 1.5, 8, 2.8, 0, 0, Math.PI * 2);
    ctx.fill();

    // 双腿（微微前后岔开，像扎着马步干活）
    ctx.strokeStyle = '#4a3a24';
    ctx.lineWidth = 2.2;
    ctx.lineCap = 'round';
    ctx.beginPath();
    ctx.moveTo(-2, -9);
    ctx.lineTo(-4.5, 0);
    ctx.moveTo(2, -9);
    ctx.lineTo(4.5, 0);
    ctx.stroke();
    // 鞋
    ctx.fillStyle = '#2e2315';
    ctx.fillRect(-6.5, -1.5, 4.5, 2.2);
    ctx.fillRect(2, -1.5, 4.5, 2.2);

    // 上半身随挥具前倾
    const lean = 0.12 + o.phase * 0.18;
    ctx.translate(0, -9);
    ctx.rotate(lean);

    // 躯干：短褐（下摆略宽）+ 腰带
    ctx.fillStyle = o.tunic;
    ctx.beginPath();
    ctx.moveTo(-5, 0);
    ctx.lineTo(5, 0);
    ctx.lineTo(6.5, 11);
    ctx.lineTo(-6.5, 11);
    ctx.closePath();
    ctx.fill();
    ctx.strokeStyle = 'rgba(40,30,16,0.55)';
    ctx.lineWidth = 1;
    ctx.stroke();
    ctx.fillStyle = '#3d2f1c';
    ctx.fillRect(-6.5, 7.5, 13, 2.2);

    // 脖子 + 头
    ctx.fillStyle = '#c8a273';
    ctx.fillRect(-1.4, -3, 2.8, 3.4);
    ctx.beginPath();
    ctx.arc(0, -7.5, 4.2, 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = 'rgba(90,64,38,0.6)';
    ctx.lineWidth = 0.8;
    ctx.stroke();

    // 头饰
    if (o.hat === 'straw') {
      // 斗笠：宽檐圆锥
      ctx.fillStyle = '#c8b078';
      ctx.beginPath();
      ctx.moveTo(-9, -8.5);
      ctx.quadraticCurveTo(0, -14.5, 9, -8.5);
      ctx.quadraticCurveTo(0, -6.8, -9, -8.5);
      ctx.fill();
      ctx.strokeStyle = 'rgba(110,86,44,0.7)';
      ctx.lineWidth = 0.9;
      ctx.stroke();
    } else {
      // 幞头/布包头
      ctx.fillStyle = '#5a4630';
      ctx.beginPath();
      ctx.arc(0, -8.4, 4.4, Math.PI, 0);
      ctx.fill();
      ctx.fillRect(-4.4, -8.8, 8.8, 2);
    }

    // 持工具的手臂 + 工具，绕肩摆动
    const swing = -1.05 + o.toolPhase * 2.1;
    ctx.save();
    ctx.translate(3, 2);
    ctx.rotate(swing);
    // 手臂
    ctx.strokeStyle = o.tunic;
    ctx.lineWidth = 2.4;
    ctx.beginPath();
    ctx.moveTo(0, 0);
    ctx.lineTo(0, 9);
    ctx.stroke();
    // 手柄
    ctx.strokeStyle = '#5a3f19';
    ctx.lineWidth = 1.7;
    ctx.beginPath();
    ctx.moveTo(0, 8);
    ctx.lineTo(0, -13);
    ctx.stroke();
    if (o.tool === 'hoe') {
      // 锄头：横刃
      ctx.fillStyle = '#7d8288';
      ctx.fillRect(-4.5, -15.5, 10, 3.4);
      ctx.strokeStyle = 'rgba(40,42,46,0.8)';
      ctx.lineWidth = 0.7;
      ctx.strokeRect(-4.5, -15.5, 10, 3.4);
    } else {
      // 锤子：木柄铁头
      ctx.fillStyle = '#6b6f75';
      ctx.fillRect(-3.5, -17, 9, 5);
      ctx.strokeStyle = 'rgba(40,42,46,0.8)';
      ctx.lineWidth = 0.7;
      ctx.strokeRect(-3.5, -17, 9, 5);
    }
    ctx.restore();

    // 另一只手扶住柄中段
    ctx.strokeStyle = o.tunic;
    ctx.lineWidth = 2.2;
    ctx.beginPath();
    ctx.moveTo(-3, 2);
    ctx.lineTo(2, 7);
    ctx.stroke();

    ctx.restore();
  }

  /** 反查分区对应的建筑编码（用于「升 X 级」文案） */
  private indexOfZone(zone: Rect): number {
    for (const z of this.zones) {
      if (z.rect === zone) {
        return z.index;
      }
    }
    return 0;
  }

  /** 工地的吊车 */
  private crane(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, color: string): void {
    ctx.save();
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(x, y + s * 0.5);
    ctx.lineTo(x, y - s * 0.5);
    ctx.moveTo(x - s * 0.45, y - s * 0.42);
    ctx.lineTo(x + s * 0.55, y - s * 0.28);
    ctx.moveTo(x + s * 0.34, y - s * 0.33);
    ctx.lineTo(x + s * 0.34, y - s * 0.02);
    ctx.stroke();
    ctx.restore();
  }

  // -------------------------------------------------------------------------
  //  返回按钮 / 页脚 / 提示
  // -------------------------------------------------------------------------
  private drawBackButton(
    ctx: CanvasRenderingContext2D,
    sheet: Rect,
    now: number,
  ): void {
    const label = '← 返回疆域图（Esc）';
    const w = this.measure(ctx, label, 15, 700) + 34;
    const h = 38;
    // 【第 8 轮】原来贴在右上角、30px 高、淡色描边，玩家根本找不到出口。
    // 改成左上角、实底金框、加大字号——这是城内唯一出口，必须一眼看见。
    const x = sheet.x + 18;
    const y = sheet.y + 12;
    this.backBtn = { x, y, w, h };

    const hovered =
      this.pointerInside && hitRect(this.backBtn, this.pointer.x, this.pointer.y);
    const pulse = 0.5 + 0.4 * Math.sin(now / 220);

    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,0.28)';
    ctx.shadowBlur = hovered ? 12 : 6;
    ctx.shadowOffsetY = 2;
    roundRect(ctx, x, y, w, h, 10);
    ctx.fillStyle = hovered
      ? `rgba(224,180,87,${0.9 * pulse + 0.05})`
      : 'rgba(224,180,87,0.88)';
    ctx.fill();
    ctx.restore();

    roundRect(ctx, x, y, w, h, 10);
    ctx.strokeStyle = hovered ? '#fff6dd' : 'rgba(120,92,40,0.85)';
    ctx.lineWidth = hovered ? 2.2 : 1.4;
    ctx.stroke();
    this.text(ctx, label, x + w / 2, y + h / 2, 15, '#2a2114', 'center', 700);
  }

  private drawFooter(ctx: CanvasRenderingContext2D, sheet: Rect): void {
    const y = sheet.y + sheet.h - 12;
    this.text(
      ctx,
      '鼠标悬停看建筑详情 · 正在兴建的那一处会闪金框 · 左上角按钮或 Esc 返回疆域图',
      sheet.x + 22,
      y,
      12,
      INK_SOFT,
      'left',
    );
  }

  private drawTooltip(ctx: CanvasRenderingContext2D, city: Readonly<City>): void {
    const zone = this.hoverZone;
    if (!zone || !this.pointerInside) {
      return;
    }
    const level = this.levelOf(city, zone.index);
    const lines = [
      `${BUILDING_NAMES[zone.index]} · Lv${level}`,
      BUILDING_EFFECTS[zone.index] ?? '',
    ];
    if (city.upgradingBuilding === zone.index && city.upgradeMonthsRemaining > 0) {
      lines.push(`在建：升 ${level + 1} 级 · 剩 ${city.upgradeMonthsRemaining} 月`);
    }

    let w = 0;
    for (const line of lines) {
      w = Math.max(w, this.measure(ctx, line, 12));
    }
    w += 20;
    const h = lines.length * 18 + 12;
    let x = this.pointer.x + 16;
    let y = this.pointer.y + 14;
    if (x + w > this.vw - 8) {
      x = this.pointer.x - w - 12;
    }
    if (y + h > this.vh - 8) {
      y = this.pointer.y - h - 10;
    }
    x = Math.max(8, x);
    y = Math.max(8, y);

    roundRect(ctx, x, y, w, h, 8);
    ctx.fillStyle = 'rgba(20,26,38,0.95)';
    ctx.fill();
    ctx.strokeStyle = GOLD;
    ctx.lineWidth = 1.2;
    ctx.stroke();
    for (let i = 0; i < lines.length; i++) {
      this.text(
        ctx,
        lines[i],
        x + 10,
        y + 15 + i * 18,
        12,
        i === 0 ? '#e8e4d8' : '#9aa7bd',
        'left',
        i === 0 ? 700 : 400,
        '#e8e4d8',
      );
    }
  }

  // -------------------------------------------------------------------------
  //  命中判定 / 绘制小工具
  // -------------------------------------------------------------------------
  private pickZone(x: number, y: number): Zone | null {
    for (const zone of this.zones) {
      if (hitRect(zone.rect, x, y)) {
        return zone;
      }
    }
    return null;
  }

  private levelOf(city: Readonly<City>, index: number): number {
    switch (index) {
      case FARM:
        return city.farmLevel;
      case BARRACKS:
        return city.barracksLevel;
      case WALL:
        return city.fortifyLevel;
      case MARKET:
        return city.marketLevel;
      default:
        return 0;
    }
  }

  /** 等级格：实心=已达等级，空心=待建 */
  private pips(
    ctx: CanvasRenderingContext2D,
    x: number,
    y: number,
    filled: number,
    total: number,
    color: string,
    size = 9,
    gap = 3,
  ): void {
    for (let i = 0; i < total; i++) {
      const px = x + i * (size + gap);
      const py = y - size / 2;
      ctx.fillStyle = i < filled ? color : 'rgba(90,72,44,0.18)';
      ctx.fillRect(px, py, size, size);
      ctx.strokeStyle = 'rgba(58,45,26,0.55)';
      ctx.lineWidth = 1;
      ctx.strokeRect(px, py, size, size);
    }
  }

  private text(
    ctx: CanvasRenderingContext2D,
    content: string,
    x: number,
    y: number,
    size: number,
    color: string,
    align: CanvasTextAlign = 'center',
    weight: number = 400,
    halo: string = 'rgba(247,240,220,0.9)',
  ): void {
    ctx.save();
    ctx.font = `${weight} ${size}px "Microsoft YaHei","PingFang SC",system-ui,sans-serif`;
    ctx.textAlign = align;
    ctx.textBaseline = 'middle';
    ctx.lineJoin = 'round';
    ctx.lineWidth = Math.max(2.5, size * 0.3);
    ctx.strokeStyle = halo;
    ctx.strokeText(content, x, y);
    ctx.fillStyle = color;
    ctx.fillText(content, x, y);
    ctx.restore();
  }

  private measure(
    ctx: CanvasRenderingContext2D,
    content: string,
    size: number,
    weight: number = 400,
  ): number {
    ctx.save();
    ctx.font = `${weight} ${size}px "Microsoft YaHei","PingFang SC",system-ui,sans-serif`;
    const w = ctx.measureText(content).width;
    ctx.restore();
    return w;
  }
}

// ---------------------------------------------------------------------------
//  小工具
// ---------------------------------------------------------------------------

function hitRect(r: Rect, x: number, y: number): boolean {
  return x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h;
}

function roundRect(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  w: number,
  h: number,
  r: number,
): void {
  const rr = Math.min(r, w / 2, h / 2);
  ctx.beginPath();
  ctx.moveTo(x + rr, y);
  ctx.lineTo(x + w - rr, y);
  ctx.arcTo(x + w, y, x + w, y + rr, rr);
  ctx.lineTo(x + w, y + h - rr);
  ctx.arcTo(x + w, y + h, x + w - rr, y + h, rr);
  ctx.lineTo(x + rr, y + h);
  ctx.arcTo(x, y + h, x, y + h - rr, rr);
  ctx.lineTo(x, y + rr);
  ctx.arcTo(x, y, x + rr, y, rr);
  ctx.closePath();
}
