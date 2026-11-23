// ============================================================================
//  Battle/Cinema.ts —— 战斗演出（2D 疆域图上的「小人打架」）
// ============================================================================
//  【定位】纯演出。一切数字来自 /api/tick 的结算结果，本文件不做任何战斗计算，
//  也不显示任何百分比——玩家要看的是「打得惨不惨」，不是看公式。
//  战损只用「极低 / 低 / 中 / 高」四档描述，档位是从真实伤亡比例映射出来的标签。
//
//  v1.9：从 three.js 场景里的 3D 小人改成全屏 Canvas 2D 覆盖层。
//  流程：列阵 → 冲锋 → 交锋（按真实伤亡逐批倒地）→ 收场。
//  任何时刻按 Esc 或点一下画面立即收场，绝不阻塞结算面板。
// ============================================================================

import type { GameStateStore } from '../Core/GameStateStore.js';
import type { SettledOrder, TickResponse } from '../Core/Types.js';
import type { WorldScene } from '../Scene/WorldScene.js';
import type { Hud } from '../Ui/Hud.js';
import { factionColor } from '../Ui/Theme.js';

/** 一方的战况摘要（全部由 Delta 还原，不含任何业务判断） */
interface SideSummary {
  factionId: number;
  before: number;
  loss: number;
}

/** 一场战斗的演出剧本 */
interface BattleScript {
  cityId: number;
  outcome: 'capture' | 'repelled' | 'stalemate';
  attacker: SideSummary;
  defender: SideSummary;
}

/** 战损档位文案 */
function lossTier(loss: number, before: number): string {
  if (before <= 0) {
    return '—';
  }
  const r = loss / before;
  if (r < 0.05) return '极低';
  if (r < 0.15) return '低';
  if (r < 0.3) return '中';
  return '高';
}

/** 每方摆多少人：兵力越多列越宽，但封顶，避免糊成一团 */
function menFor(soldiers: number): number {
  if (soldiers <= 0) return 6;
  return Math.max(6, Math.min(28, Math.round(6 + Math.log10(soldiers + 1) * 6)));
}

/** 极简确定性伪随机（xorshift32）：同一场战斗每次站位一致，录屏可复现 */
function makeRng(seed: number): () => number {
  let s = seed | 0 || 0x9e3779b9;
  return () => {
    s ^= s << 13;
    s ^= s >>> 17;
    s ^= s << 5;
    return ((s >>> 0) % 10000) / 10000;
  };
}

function clamp01(v: number): number {
  return v < 0 ? 0 : v > 1 ? 1 : v;
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

interface Slot {
  col: number;
  row: number;
  cols: number;
  rows: number;
  jx: number;
  jy: number;
}

function formation(n: number, cols: number, rng: () => number): Slot[] {
  const rows = Math.ceil(n / cols);
  const out: Slot[] = [];
  for (let i = 0; i < n; i++) {
    out.push({
      col: i % cols,
      row: Math.floor(i / cols),
      cols,
      rows,
      jx: (rng() - 0.5) * 7,
      jy: (rng() - 0.5) * 5,
    });
  }
  return out;
}

/** 倒地顺序：col 越大越靠中线，先倒；同列用随机数打散 */
function fallOrder(form: Slot[], rng: () => number): number[] {
  return form
    .map((s, i) => ({ i, col: s.col, r: rng() }))
    .sort((a, b) => b.col - a.col || a.r - b.r)
    .map((o) => o.i);
}

/** 时间轴（秒） */
const T_TITLE = 0.9;
const T_FORM = 2.0;
const T_CHARGE = 2.8;
const T_CLASH_END = 7.0;
const T_END = 9.2;

export class BattleCinema {
  private playing = false;
  private skipRequested = false;
  private skipHandler: ((ev?: Event) => void) | null = null;
  private raf = 0;
  private canvas: HTMLCanvasElement | null = null;

  constructor(
    private readonly scene: WorldScene,
    private readonly store: GameStateStore,
    private readonly hud: Hud,
  ) {}

  /** 本月若有攻城结算 → 演出；否则直接返回（不阻塞结算面板） */
  async maybePlay(tick: TickResponse): Promise<void> {
    if (this.playing || !this.store.ready) {
      return;
    }

    const scripts = this.collectScripts(tick);
    if (scripts.length === 0) {
      return;
    }

    this.playing = true;
    this.hud.showCinema('战报');

    try {
      // 一月最多演两场：再多就变成看动画片，玩家会想跳过
      for (const script of scripts.slice(0, 2)) {
        await this.playOne(script);
      }
    } finally {
      this.endSkip();
      this.hud.hideCinema();
      this.playing = false;
    }
  }

  /**
   * 本月会产生战斗演出的剧本。
   * 【第 8 轮】不再自动播放——玩家原话「正玩着呢突然冒出来一堆小人打架」。
   * 改成战报列表，玩家想看哪场点哪场。
   */
  scriptsFor(tick: TickResponse): BattleScript[] {
    return this.collectScripts(tick);
  }

  /** 玩家在战报里点「观看回放」时，播这一场 */
  async playScript(script: BattleScript): Promise<void> {
    if (this.playing || !this.store.ready) {
      return;
    }
    this.playing = true;
    this.hud.showCinema('战报回放');
    try {
      await this.playOne(script);
    } finally {
      this.endSkip();
      this.hud.hideCinema();
      this.playing = false;
    }
  }

  /**
   * 演示入口：不依赖真实战报，摆一场固定规模的战斗。
   * 用途：?cinema-demo=1 时无头截图 / 联调演出时间轴。
   */
  async playDemo(cityId: number): Promise<void> {
    if (this.playing) {
      return;
    }
    this.playing = true;
    try {
      await this.playOne({
        cityId,
        outcome: 'capture',
        attacker: { factionId: 0, before: 3200, loss: 620 },
        defender: { factionId: 1, before: 1800, loss: 1500 },
      });
    } finally {
      this.playing = false;
    }
  }

  // ==========================================================================
  //  从结算结果还原剧本
  // ==========================================================================

  private collectScripts(tick: TickResponse): BattleScript[] {
    const playerFactionId = this.store.playerFaction?.id ?? 0;
    const out: BattleScript[] = [];

    for (const r of tick.results) {
      if (r.action !== 'Dispatch' || r.code !== 'Success') {
        continue;
      }
      const script = this.buildScript(r, playerFactionId);
      if (script) {
        out.push(script);
      }
    }

    return out;
  }

  private buildScript(r: SettledOrder, playerFactionId: number): BattleScript | null {
    const actorFactionId = r.actorType === 'Player' ? playerFactionId : r.actorId;

    // 本月这条指令涉及的所有「兵力减少」Delta
    const losses = r.deltas.filter(
      (d) => d.targetType === 'Army' && d.field === 'Soldiers' && d.newValue < d.oldValue,
    );
    if (losses.length === 0) {
      return null; // 调兵、空城接管这类没有交战的指令，不演
    }

    const attacker: SideSummary = { factionId: actorFactionId, before: 0, loss: 0 };
    const defender: SideSummary = { factionId: -1, before: 0, loss: 0 };

    for (const d of losses) {
      const army = this.store.army(d.targetId);
      const ownerId = army?.ownerFactionId ?? -1;
      const target = ownerId === actorFactionId ? attacker : defender;

      if (target.factionId === -1) {
        target.factionId = ownerId;
      }
      target.before += d.oldValue;
      target.loss += d.oldValue - d.newValue;
    }

    if (attacker.before === 0) {
      attacker.before = this.store.state.armies
        .filter((a) => a.ownerFactionId === actorFactionId && a.positionCityId === r.targetId)
        .reduce((s, a) => s + a.soldiers, 0);
    }
    if (defender.factionId === -1) {
      defender.factionId = this.store.city(r.targetId)?.ownerFactionId ?? -1;
    }

    const keys = r.events.map((e) => e.eventKey);
    const outcome: BattleScript['outcome'] = keys.includes('CityCaptured')
      ? 'capture'
      : keys.includes('SiegeRepelled')
        ? 'repelled'
        : 'stalemate';

    return {
      cityId: r.targetId,
      outcome,
      attacker: { ...attacker, loss: Math.max(0, attacker.loss) },
      defender: { ...defender, loss: Math.max(0, defender.loss) },
    };
  }

  // ==========================================================================
  //  单场演出（全屏 Canvas 2D）
  // ==========================================================================

  private playOne(script: BattleScript): Promise<void> {
    return new Promise<void>((resolve) => {
      const city = this.store.city(script.cityId);
      const cityName = city?.name ?? `城 #${script.cityId}`;

      const attColor = factionColor(script.attacker.factionId);
      const defColor = factionColor(script.defender.factionId);

      const rng = makeRng(script.cityId * 7919 + script.attacker.before * 31 + script.defender.before);
      const nA = menFor(script.attacker.before);
      const nD = menFor(script.defender.before);

      const ratioA = script.attacker.before > 0 ? clamp01(script.attacker.loss / script.attacker.before) : 0;
      const ratioD = script.defender.before > 0 ? clamp01(script.defender.loss / script.defender.before) : 0;
      const deadA = Math.round(nA * ratioA);
      const deadD = Math.round(nD * ratioD);

      const colsA = Math.max(3, Math.ceil(Math.sqrt(nA * 1.4)));
      const colsD = Math.max(3, Math.ceil(Math.sqrt(nD * 1.4)));
      const formA = formation(nA, colsA, rng);
      const formD = formation(nD, colsD, rng);

      // 倒地顺序：靠中线的那一列先倒（同列随机），交锋才有「前排先被打穿」的层次感。
      // 原来是纯随机，看着像全军莫名同时倒下。
      const orderA = fallOrder(formA, rng);
      const orderD = fallOrder(formD, rng);
      const rankA = new Map<number, number>();
      const rankD = new Map<number, number>();
      orderA.slice(0, deadA).forEach((idx, rank) => rankA.set(idx, rank));
      orderD.slice(0, deadD).forEach((idx, rank) => rankD.set(idx, rank));

      const canvas = document.createElement('canvas');
      canvas.style.cssText = 'position:fixed;inset:0;width:100%;height:100%;z-index:40;pointer-events:none;';
      document.body.appendChild(canvas);
      this.canvas = canvas;

      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const W = window.innerWidth;
      const H = window.innerHeight;
      canvas.width = Math.round(W * dpr);
      canvas.height = Math.round(H * dpr);

      const ctx = canvas.getContext('2d');
      if (!ctx) {
        this.teardown();
        resolve();
        return;
      }
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

      this.skipRequested = false;
      this.bindSkip();

      const start = performance.now();

      // 【注意】这里刻意不用 rAF 回调的 timestamp：
      // 无头虚拟时间模式下它和 performance.now() 不同源，会算出负的 t。
      const loop = (): void => {
        const t = (performance.now() - start) / 1000;
        this.drawFrame(ctx, W, H, t, {
          cityName,
          script,
          attColor,
          defColor,
          nA,
          nD,
          deadA,
          deadD,
          formA,
          formD,
          rankA,
          rankD,
        });

        if (t >= T_END || this.skipRequested) {
          this.teardown();
          resolve();
          return;
        }
        this.raf = requestAnimationFrame(loop);
      };

      this.raf = requestAnimationFrame(loop);
    });
  }

  private drawFrame(
    ctx: CanvasRenderingContext2D,
    W: number,
    H: number,
    t: number,
    d: {
      cityName: string;
      script: BattleScript;
      attColor: string;
      defColor: string;
      nA: number;
      nD: number;
      deadA: number;
      deadD: number;
      formA: Slot[];
      formD: Slot[];
      rankA: Map<number, number>;
      rankD: Map<number, number>;
    },
  ): void {
    // ---- 背景 ----
    const bg = ctx.createRadialGradient(W / 2, H * 0.52, 60, W / 2, H * 0.52, Math.max(W, H) * 0.75);
    bg.addColorStop(0, 'rgba(26,30,26,0.92)');
    bg.addColorStop(1, 'rgba(6,8,10,0.97)');
    ctx.fillStyle = bg;
    ctx.fillRect(0, 0, W, H);

    // ---- 战场地面 ----
    const groundW = Math.min(W * 0.78, 1100);
    const groundH = Math.min(H * 0.42, 420);
    const gx = (W - groundW) / 2;
    const gy = H * 0.56 - groundH / 2;

    ctx.save();
    ctx.beginPath();
    ctx.ellipse(W / 2, H * 0.56, groundW / 2, groundH / 2, 0, 0, Math.PI * 2);
    const gd = ctx.createLinearGradient(0, gy, 0, gy + groundH);
    gd.addColorStop(0, '#4a4a34');
    gd.addColorStop(0.5, '#3b3d2a');
    gd.addColorStop(1, '#2b2d20');
    ctx.fillStyle = gd;
    ctx.fill();
    ctx.strokeStyle = 'rgba(180,160,110,0.22)';
    ctx.lineWidth = 2;
    ctx.stroke();
    ctx.restore();

    // 地面细纹
    ctx.save();
    ctx.globalAlpha = 0.12;
    ctx.strokeStyle = '#c9b884';
    ctx.lineWidth = 1;
    for (let i = 0; i < 26; i++) {
      const y = gy + ((i * 37) % groundH);
      ctx.beginPath();
      ctx.moveTo(gx + 40, y);
      ctx.lineTo(gx + groundW - 40, y + ((i % 3) - 1) * 6);
      ctx.stroke();
    }
    ctx.restore();

    // ---- 阵型进度 ----
    const formP = clamp01((t - T_TITLE) / (T_FORM - T_TITLE));
    const chargeP = clamp01((t - T_FORM) / (T_CHARGE - T_FORM));
    const easing = (v: number): number => v * v * (3 - 2 * v);

    const spacingX = 15;
    const spacingY = 19;

    const drawSide = (
      form: Slot[],
      color: string,
      side: 1 | -1,
      dead: number,
      ranks: Map<number, number>,
    ): void => {
      const anchorX = side === 1 ? W * 0.31 : W * 0.69;

      // 冲锋距离按真实阵型算：让最靠中线的一列刚好冲到中线，两军矛尖相接。
      // 【修复】原实现固定冲 92px，两军间距却有 0.38W（约 450px），永远碰不到面；
      // 且方向写反（x -= side），实际是各自向后撤。这里改为整阵平移、朝中线对冲。
      const cols = form.length > 0 ? form[0].cols : 3;
      const halfWidth = ((cols - 1) / 2) * spacingX;
      const innerEdge = anchorX + side * halfWidth;
      const meetX = W * 0.5 - side * 12;
      const chargeDist = Math.max(0, (meetX - innerEdge) * side);

      for (let i = 0; i < form.length; i++) {
        const s = form[i];

        // 初始从场外走进来
        const enter = easing(clamp01(formP * 1.25 - (i % 6) * 0.04));
        const homeX = anchorX + side * (s.col - (s.cols - 1) / 2) * spacingX + s.jx;
        const homeY = H * 0.56 + (s.row - (s.rows - 1) / 2) * spacingY + s.jy;

        const startX = anchorX + side * (groundW / 2 + 90);
        let x = lerp(startX, homeX, enter);
        const y = homeY;

        // 冲锋：整阵朝中线推进，直到两军接触
        x += side * easing(chargeP) * chargeDist;

        // 交锋：只有最前排有节奏地「刺击」一下，不是整片乱抖。
        // 【第 8 轮】原来是 sin(t*26) 整片高频抖动，玩家原话「在中间来回移动跟傻子一样」。
        let lunge = 0;
        if (t > T_CHARGE) {
          const front = s.col === s.cols - 1;
          if (front) {
            const beat = Math.sin((t - T_CHARGE) * 9.5 + (i % 3) * 0.9);
            lunge = Math.max(0, beat) * 3.6 * (1 - clamp01((t - T_CLASH_END) / 0.5));
          }
        }

        // 倒地进度
        let fall = 0;
        const rank = ranks.get(i);
        if (rank !== undefined) {
          const at = T_CHARGE + (rank / Math.max(1, dead)) * (T_CLASH_END - T_CHARGE) * 0.92;
          fall = clamp01((t - at) / 0.32);
        }

        this.drawMan(ctx, x + side * lunge, y, color, fall, side, i);
      }
    };

    drawSide(d.formA, d.attColor, 1, d.deadA, d.rankA);
    drawSide(d.formD, d.defColor, -1, d.deadD, d.rankD);

    // ---- 接触瞬间的尘土冲击波 ----
    if (t > T_CHARGE && t < T_CHARGE + 1.2) {
      const p = clamp01((t - T_CHARGE) / 1.2);
      const cx = W * 0.5;
      const cy = H * 0.56;
      ctx.save();
      ctx.globalAlpha = (1 - p) * 0.6;
      ctx.strokeStyle = 'rgba(214,196,158,0.95)';
      ctx.lineWidth = 3.2 * (1 - p) + 1;
      ctx.beginPath();
      ctx.ellipse(cx, cy + 10, 36 + p * 190, 9 + p * 26, 0, 0, Math.PI * 2);
      ctx.stroke();
      for (let k = 0; k < 12; k++) {
        const ang = (k / 12) * Math.PI * 2;
        const rr = 26 + p * 140;
        ctx.fillStyle = `rgba(196,176,136,${((1 - p) * 0.55).toFixed(3)})`;
        ctx.beginPath();
        ctx.arc(cx + Math.cos(ang) * rr, cy + 10 + Math.sin(ang) * rr * 0.3, 3 * (1 - p) + 1, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();
    }

    // ---- 锋线交击火花：两军接触的中间地带随机闪白点 ----
    if (t > T_CHARGE && t < T_CLASH_END) {
      ctx.save();
      for (let k = 0; k < 7; k++) {
        const seed = Math.floor(t * 9 + k * 3.7);
        const fx = W * 0.5 + Math.sin(seed * 12.9898) * 30;
        const fy = H * 0.56 + Math.sin(seed * 78.233) * 30;
        const a = 0.3 + 0.4 * Math.abs(Math.sin(t * 22 + k * 1.3));
        ctx.fillStyle = `rgba(255,246,214,${a.toFixed(3)})`;
        ctx.beginPath();
        ctx.arc(fx, fy, 1.7, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();
    }

    // ---- 顶部字幕 ----
    let caption: string;
    if (t < T_FORM) {
      caption = `${d.cityName} · 两军列阵`;
    } else if (t < T_CHARGE) {
      caption = `${d.cityName} · 冲锋`;
    } else if (t < T_CLASH_END) {
      caption = `${d.cityName} · 交锋`;
    } else {
      caption =
        d.script.outcome === 'capture'
          ? `${d.cityName} · 城破`
          : d.script.outcome === 'repelled'
            ? `${d.cityName} · 击退`
            : `${d.cityName} · 相持`;
    }

    ctx.save();
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.font = `700 ${Math.round(Math.min(34, W / 28))}px "Microsoft YaHei","PingFang SC",system-ui,sans-serif`;
    ctx.fillStyle = 'rgba(240,228,200,0.96)';
    ctx.shadowColor = 'rgba(0,0,0,0.8)';
    ctx.shadowBlur = 12;
    ctx.fillText(caption, W / 2, H * 0.17);


    // ---- 战损（只给档位，不给百分比） ----
    if (t > T_CHARGE) {
      const alpha = clamp01((t - T_CHARGE) / 0.5);
      const attLabel = `攻方战损【${lossTier(d.script.attacker.loss, d.script.attacker.before)}】`;
      const defLabel = `守方战损【${lossTier(d.script.defender.loss, d.script.defender.before)}】`;

      ctx.save();
      ctx.globalAlpha = alpha;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.font = `600 ${Math.round(Math.min(20, W / 46))}px "Microsoft YaHei","PingFang SC",system-ui,sans-serif`;
      ctx.fillStyle = 'rgba(226,214,186,0.9)';
      ctx.fillText(`${attLabel}　　${defLabel}`, W / 2, H * 0.85);

      ctx.font = `400 ${Math.round(Math.min(13, W / 72))}px "Microsoft YaHei",system-ui,sans-serif`;
      ctx.fillStyle = 'rgba(190,180,158,0.55)';
      ctx.fillText('Esc / 点击 跳过', W / 2, H * 0.93);
      ctx.restore();
    }
  }

  /** 一个小人：头 + 身体 + 一杆矛 */
  private drawMan(
    ctx: CanvasRenderingContext2D,
    x: number,
    y: number,
    color: string,
    fall: number,
    side: 1 | -1,
    seed: number,
  ): void {
    // 【修复】原来 s=1，在 1080p 上每个小人只有十几像素，整片阵看成两三个点。
    const s = 1.55;
    ctx.save();
    ctx.translate(x, y);

    if (fall > 0) {
      // 倒地：向冲锋方向侧翻，变暗
      ctx.rotate(side * fall * (Math.PI / 2));
      ctx.globalAlpha = 1 - fall * 0.45;
    }

    const sway = fall > 0 ? 0 : Math.sin(seed + x * 0.02) * 0.6;
    ctx.rotate(sway * 0.02);

    // 影子
    ctx.beginPath();
    ctx.ellipse(0, 9 * s, 5.2 * s, 2 * s, 0, 0, Math.PI * 2);
    ctx.fillStyle = 'rgba(0,0,0,0.28)';
    ctx.fill();

    // 矛
    ctx.strokeStyle = 'rgba(52,42,26,0.95)';
    ctx.lineWidth = 1.4;
    ctx.beginPath();
    ctx.moveTo(side * 5 * s, 7 * s);
    ctx.lineTo(side * 9 * s, -14 * s);
    ctx.stroke();

    // 身体
    ctx.beginPath();
    ctx.moveTo(-4 * s, 8 * s);
    ctx.lineTo(0, -5 * s);
    ctx.lineTo(4 * s, 8 * s);
    ctx.closePath();
    ctx.fillStyle = color;
    ctx.fill();
    ctx.strokeStyle = 'rgba(16,14,10,0.55)';
    ctx.lineWidth = 0.8;
    ctx.stroke();

    // 双腿：冲锋姿态一前一后，让轮廓像人而不是一个三角
    ctx.strokeStyle = 'rgba(16,14,10,0.75)';
    ctx.lineWidth = 1.3 * s;
    ctx.beginPath();
    ctx.moveTo(-1.6 * s, 6 * s);
    ctx.lineTo(-3.1 * s, 11.5 * s);
    ctx.moveTo(1.6 * s, 6 * s);
    ctx.lineTo(3.1 * s, 11.5 * s);
    ctx.stroke();

    // 头
    ctx.beginPath();
    ctx.arc(0, -8.5 * s, 3.4 * s, 0, Math.PI * 2);
    ctx.fillStyle = color;
    ctx.fill();
    ctx.strokeStyle = 'rgba(16,14,10,0.55)';
    ctx.stroke();

    ctx.restore();
  }

  // ==========================================================================
  //  跳过与清理
  // ==========================================================================

  private bindSkip(): void {
    this.endSkip();
    const handler = (): void => {
      this.skipRequested = true;
    };
    this.skipHandler = handler;
    window.addEventListener('keydown', handler);
    window.addEventListener('pointerdown', handler);
  }

  private endSkip(): void {
    if (this.skipHandler) {
      window.removeEventListener('keydown', this.skipHandler);
      window.removeEventListener('pointerdown', this.skipHandler);
      this.skipHandler = null;
    }
  }

  private teardown(): void {
    cancelAnimationFrame(this.raf);
    this.endSkip();
    if (this.canvas) {
      this.canvas.remove();
      this.canvas = null;
    }
  }
}
