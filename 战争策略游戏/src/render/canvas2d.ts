/**
 * Canvas 2D 软件渲染兜底
 *
 * 当WebGL 不可用时，用 Canvas 2D 画出战场俯视图，保证玩法可验证：
 * - 地形按高度与类型着色
 * - 单位按兵种与阵营画成可辨识符号
 * - 框选、移动、攻击、交战全部照常工作
 *
 * 这不是"降级到阉割版"，而是换一种渲染后端：
 * 俯视 2D 战棋视图本身就是一种合法表现（很多策略游戏的老式俯视视角）。
 * 玩家看不到 3D 山体起伏，但能看到全部战术信息与交互。
 */

import { TERRAIN_COLOR, type TerrainData } from './terrain';
import type { ControllableUnit } from '../game/unit';

export interface Canvas2DOptions {
  terrain: TerrainData;
  mapSize: number;
}

const TERRAIN_FILL: Record<string, string> = {
  plain: '#7d9b52',
  forest: '#3f6230',
  hill: '#8a7f63',
  river: '#4a7c9b',
  fort: '#9b8a7d',
};

/** 各兵种的符号与颜色（俯视下靠形状+颜色双重区分） */
const CLASS_STYLE: Record<string, { fill: string; stroke: string; shape: 'circle' | 'square' | 'triangle' }> = {
  infantry: { fill: '#8b5a3c', stroke: '#f0d0b0', shape: 'square' },
  archer: { fill: '#4a7c59', stroke: '#c0e8d0', shape: 'circle' },
  cavalry: { fill: '#6b4a8a', stroke: '#d8b0f0', shape: 'triangle' },
};

export class Canvas2DRenderer {
  readonly canvas: HTMLCanvasElement;
  private ctx: CanvasRenderingContext2D;
  private opts: Canvas2DOptions;
  private offTerrain: HTMLCanvasElement | null = null;
  /** 世界坐标 → 屏幕坐标 */
  private scale = 1;
  private offsetX = 0;
  private offsetY = 0;

  constructor(opts: Canvas2DOptions) {
    this.opts = opts;
    this.canvas = document.createElement('canvas');
    const ctx = this.canvas.getContext('2d');
    if (!ctx) throw new Error('Canvas 2D 也不可用，无法渲染');
    this.ctx = ctx;
    this.resize();
    this.renderTerrainToBuffer();
  }

  resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    this.canvas.width = Math.floor(window.innerWidth * dpr);
    this.canvas.height = Math.floor(window.innerHeight * dpr);
    this.canvas.style.width = `${window.innerWidth}px`;
    this.canvas.style.height = `${window.innerHeight}px`;
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.updateTransform();
    this.renderTerrainToBuffer();
  }

  private updateTransform() {
    const w = window.innerWidth;
    const h = window.innerHeight;
    this.scale = w / this.opts.mapSize;
    this.offsetX = w / 2;
    this.offsetY = h / 2;
  }

  /** 屏幕坐标 → 世界坐标 */
  screenToWorld(sx: number, sy: number): { x: number; z: number } {
    return {
      x: (sx - this.offsetX) / this.scale,
      z: (sy - this.offsetY) / this.scale,
    };
  }

  /** 世界坐标 → 屏幕坐标 */
  worldToScreen(wx: number, wz: number): { x: number; y: number } {
    return {
      x: this.offsetX + wx * this.scale,
      y: this.offsetY + wz * this.scale,
    };
  }

  /** 预渲染地形到离屏画布（地形是静态的，只需画一次） */
  private renderTerrainToBuffer() {
    const size = 512;
    const off = document.createElement('canvas');
    off.width = size;
    off.height = size;
    const ctx = off.getContext('2d');
    if (!ctx) return;
    const img = ctx.createImageData(size, size);
    const half = this.opts.mapSize / 2;
    for (let py = 0; py < size; py++) {
      const wz = -half + ((py + 0.5) / size) * this.opts.mapSize;
      for (let px = 0; px < size; px++) {
        const wx = -half + ((px + 0.5) / size) * this.opts.mapSize;
        const key = this.opts.terrain.keyAt(wx, wz);
        const h = this.opts.terrain.heightAt(wx, wz);
        // 用基础色 + 高度明暗
        const c = parseColor(TERRAIN_FILL[key]);
        const shade = 0.78 + ((h + 12) / 180) * 0.44;
        const i = (py * size + px) * 4;
        img.data[i] = clamp255(c.r * shade);
        img.data[i + 1] = clamp255(c.g * shade);
        img.data[i + 2] = clamp255(c.b * shade);
        img.data[i + 3] = 255;
      }
    }
    ctx.putImageData(img, 0, 0);
    this.offTerrain = off;
  }

  /** 渲染一帧 */
  render(units: ControllableUnit[], selRect: { x0: number; y0: number; x1: number; y1: number } | null) {
    const ctx = this.ctx;
    const w = window.innerWidth;
    const h = window.innerHeight;

    ctx.clearRect(0, 0, w, h);
    ctx.fillStyle = '#0d1117';
    ctx.fillRect(0, 0, w, h);

    // 地形
    const half = this.opts.mapSize / 2;
    const tl = this.worldToScreen(-half, -half);
    const mapPx = this.opts.mapSize * this.scale;
    if (this.offTerrain) {
      ctx.imageSmoothingEnabled = true;
      ctx.drawImage(this.offTerrain, tl.x, tl.y, mapPx, mapPx);
    }

    // 地图边界
    ctx.strokeStyle = '#5a6472';
    ctx.lineWidth = 2;
    ctx.strokeRect(tl.x, tl.y, mapPx, mapPx);

    // 框选矩形
    if (selRect) {
      ctx.fillStyle = 'rgba(255,224,102,0.12)';
      ctx.strokeStyle = '#ffe066';
      ctx.lineWidth = 1;
      const x = Math.min(selRect.x0, selRect.x1);
      const y = Math.min(selRect.y0, selRect.y1);
      ctx.fillRect(x, y, Math.abs(selRect.x1 - selRect.x0), Math.abs(selRect.y1 - selRect.y0));
      ctx.strokeRect(x, y, Math.abs(selRect.x1 - selRect.x0), Math.abs(selRect.y1 - selRect.y0));
    }

    // 单位（先画敌方再画我方，避免我方被压在下层）
    const enemy = units.filter((u) => u.side === 'red');
    const friendly = units.filter((u) => u.side === 'blue');
    this.drawUnits(ctx, enemy);
    this.drawUnits(ctx, friendly);

    // 图例
    this.drawLegend(ctx);

    // 模式提示
    ctx.fillStyle = 'rgba(210,153,34,0.9)';
    ctx.fillRect(0, h - 26, w, 26);
    ctx.fillStyle = '#0d1117';
    ctx.font = '12px "Microsoft YaHei", sans-serif';
    ctx.fillText(
      '软件渲染模式（本机无 WebGL）· 俯视 2D 战棋视图 · 玩法与3D 版完全一致',
      8,
      h - 8,
    );
  }

  private drawUnits(ctx: CanvasRenderingContext2D, units: ControllableUnit[]) {
    for (const u of units) {
      const s = this.worldToScreen(u.x, u.z);
      if (s.x < -20 || s.y < -20 || s.x > window.innerWidth + 20 || s.y > window.innerHeight + 20) {
        continue;
      }
      const style = CLASS_STYLE[u.cls] ?? CLASS_STYLE.infantry;
      // 溃退单位半透明
      const routing = u.strength <= 0 || u.morale < 20;
      const r = Math.max(4, Math.min(11, 3 + Math.sqrt(Math.max(0, u.strength)) / 6));

      ctx.globalAlpha = routing ? 0.35 : u.selected ? 1 : 0.9;
      ctx.fillStyle = style.fill;
      ctx.strokeStyle = u.selected ? '#ffe066' : style.stroke;
      ctx.lineWidth = u.selected ? 2.5 : 1.2;

      ctx.beginPath();
      if (style.shape === 'square') {
        ctx.rect(s.x - r, s.y - r, r * 2, r * 2);
      } else if (style.shape === 'circle') {
        ctx.arc(s.x, s.y, r, 0, Math.PI * 2);
      } else {
        // 三角（骑兵）
        ctx.moveTo(s.x, s.y - r * 1.2);
        ctx.lineTo(s.x + r, s.y + r);
        ctx.lineTo(s.x - r, s.y + r);
        ctx.closePath();
      }
      ctx.fill();
      ctx.stroke();

      // 朝向线
      if (u.targetX !== null || u.engageTargetId !== null) {
        ctx.strokeStyle = routing ? '#6e7681' : 'rgba(255,255,255,0.55)';
        ctx.lineWidth = 1;
        const hx = u.heading;
        ctx.beginPath();
        ctx.moveTo(s.x, s.y);
        ctx.lineTo(s.x + Math.sin(hx) * r * 2, s.y + Math.cos(hx) * r * 2);
        ctx.stroke();
      }

      // 兵力条
      if (u.strength > 0 && r > 5) {
        const bw = r * 2;
        const ratio = Math.min(1, u.strength / 1000);
        ctx.fillStyle = 'rgba(0,0,0,0.55)';
        ctx.fillRect(s.x - bw / 2, s.y + r + 2, bw, 2.5);
        ctx.fillStyle = u.side === 'blue' ? '#3fb950' : '#f85149';
        ctx.fillRect(s.x - bw / 2, s.y + r + 2, bw * ratio, 2.5);
      }

      ctx.globalAlpha = 1;
    }
  }

  private drawLegend(ctx: CanvasRenderingContext2D) {
    const items: Array<[string, string]> = [
      ['#8b5a3c', '步兵'],
      ['#4a7c59', '弓兵'],
      ['#6b4a8a', '骑兵'],
    ];
    let x = 12;
    const y = window.innerHeight - 60;
    ctx.font = '12px "Microsoft YaHei", sans-serif';
    ctx.fillStyle = 'rgba(13,17,23,0.85)';
    ctx.fillRect(x - 8, y - 14, 220, 24);
    for (const [color, label] of items) {
      ctx.fillStyle = color;
      ctx.fillRect(x, y - 8, 10, 10);
      ctx.strokeStyle = '#c9d1d9';
      ctx.lineWidth = 1;
      ctx.strokeRect(x, y - 8, 10, 10);
      ctx.fillStyle = '#c9d1d9';
      ctx.fillText(label, x + 15, y);
      x += 70;
    }
  }
}

/** 解析 '#rrggbb' 为 rgb 分量（也兼容 0x 前缀的数字） */
function parseColor(color: string | number): { r: number; g: number; b: number } {
  if (typeof color === 'number') {
    return {
      r: (color >> 16) & 255,
      g: (color >> 8) & 255,
      b: color & 255,
    };
  }
  const s = color.startsWith('#') ? color.slice(1) : color;
  return {
    r: parseInt(s.slice(0, 2), 16),
    g: parseInt(s.slice(2, 4), 16),
    b: parseInt(s.slice(4, 6), 16),
  };
}

function clamp255(v: number): number {
  return Math.max(0, Math.min(255, Math.round(v)));
}

export { TERRAIN_COLOR };
