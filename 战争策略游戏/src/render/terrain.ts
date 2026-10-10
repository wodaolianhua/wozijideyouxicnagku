/**
 * 程序化战场地形
 *
 * 用确定性噪声生成地形高度场，保证每次重置地形一致（可复现的战争推演）。
 * 地形类型从高度与坡度推导，对应 combat.ts 的五种地形系数。
 */

import { TERRAINS, type ArmyClass } from '../model/combat';

export const TERRAIN_KEYS = ['plain', 'forest', 'hill', 'river', 'fort'] as const;
export type TerrainKey = (typeof TERRAIN_KEYS)[number];

export const TERRAIN_COLOR: Record<TerrainKey, number> = {
  plain: 0x7d9b52,
  forest: 0x3f6230,
  hill: 0x8a7f63,
  river: 0x4a7c9b,
  fort: 0x9b8a7d,
};

export const MAP_SIZE = 2000; // 米
export const GRID = 128; // 地形网格分辨率

/** 确定性伪随机（xorshift），避免 Math.random 导致地形不可复现 */
export class Rng {
  private state: number;
  constructor(seed = 12345) {
    this.state = seed >>> 0 || 1;
  }
  next(): number {
    let x = this.state;
    x ^= x << 13;
    x >>>= 0;
    x ^= x >> 17;
    x ^= x << 5;
    x >>>= 0;
    this.state = x;
    return x / 4294967296;
  }
  range(min: number, max: number): number {
    return min + this.next() * (max - min);
  }
  int(min: number, max: number): number {
    return Math.floor(this.range(min, max + 1));
  }
}

/** 值噪声（双线性插值），用于生成连续的高度场 */
function valueNoise(rng: Rng, size: number): number[] {
  const grid = new Array(size * size);
  for (let i = 0; i < grid.length; i++) grid[i] = rng.next();
  return grid;
}

function sampleNoise(grid: number[], size: number, x: number, y: number): number {
  const xi = Math.floor(x);
  const yi = Math.floor(y);
  const fx = x - xi;
  const fy = y - yi;
  const idx = (a: number, b: number) => ((b % size) + size) % size * size + (((a % size) + size) % size);
  const v00 = grid[idx(xi, yi)];
  const v10 = grid[idx(xi + 1, yi)];
  const v01 = grid[idx(xi, yi + 1)];
  const v11 = grid[idx(xi + 1, yi + 1)];
  const sx = fx * fx * (3 - 2 * fx);
  const sy = fy * fy * (3 - 2 * fy);
  const top = v00 + (v10 - v00) * sx;
  const bot = v01 + (v11 - v01) * sx;
  return top + (bot - top) * sy;
}

export interface TerrainData {
  height: Float32Array;
  type: Uint8Array; // 索引到 TERRAIN_KEYS
  keyAt(x: number, z: number): TerrainKey;
  heightAt(x: number, z: number): number;
  /** 坡度（0~1），用于判断是否骑兵可通行 */
  slopeAt(x: number, z: number): number;
}

export function generateTerrain(seed = 20261005): TerrainData {
  const rng = new Rng(seed);
  const octaves = [
    { grid: valueNoise(rng, 8), freq: 8, amp: 1.0 },
    { grid: valueNoise(rng, 16), freq: 16, amp: 0.5 },
    { grid: valueNoise(rng, 32), freq: 32, amp: 0.25 },
  ];
  const height = new Float32Array(GRID * GRID);
  const type = new Uint8Array(GRID * GRID);

  for (let z = 0; z < GRID; z++) {
    for (let x = 0; x < GRID; x++) {
      let h = 0;
      let ampSum = 0;
      for (const o of octaves) {
        const g = o.grid.length ** 0.5;
        h += sampleNoise(o.grid, g, (x / GRID) * o.freq, (z / GRID) * o.freq) * o.amp;
        ampSum += o.amp;
      }
      h /= ampSum;
      // 河谷：沿 x 轴中央的带状洼地
      const riverDist = Math.abs(z / GRID - 0.5);
      if (riverDist < 0.045) {
        const t = 1 - riverDist / 0.045;
        h -= t * 0.45;
      }
      height[z * GRID + x] = h;
    }
  }

  // 由高度与坡度判定地形类型
  const sampleH = (gx: number, gz: number) => height[Math.min(GRID - 1, Math.max(0, gz)) * GRID + Math.min(GRID - 1, Math.max(0, gx))];
  for (let z = 0; z < GRID; z++) {
    for (let x = 0; x < GRID; x++) {
      const h = sampleH(x, z);
      const slope =
        Math.abs(sampleH(x + 1, z) - sampleH(x - 1, z)) +
        Math.abs(sampleH(x, z + 1) - sampleH(x, z - 1));
      const riverDist = Math.abs(z / GRID - 0.5);
      let key: TerrainKey;
      if (riverDist < 0.045) key = 'river';
      else if (slope > 0.09) key = 'hill';
      else if (h > 0.62) key = 'forest';
      else key = 'plain';
      type[z * GRID + x] = TERRAIN_KEYS.indexOf(key);
    }
  }

  // 预计算高度归一化范围
  let minH = Infinity;
  let maxH = -Infinity;
  for (let i = 0; i < height.length; i++) {
    if (height[i] < minH) minH = height[i];
    if (height[i] > maxH) maxH = height[i];
  }
  const range = maxH - minH || 1;
  for (let i = 0; i < height.length; i++) {
    height[i] = ((height[i] - minH) / range) * 180 - 12;
  }

  const gx = (worldX: number) =>
    Math.min(GRID - 1, Math.max(0, Math.round(((worldX + MAP_SIZE / 2) / MAP_SIZE) * (GRID - 1))));
  const gz = (worldZ: number) =>
    Math.min(GRID - 1, Math.max(0, Math.round(((worldZ + MAP_SIZE / 2) / MAP_SIZE) * (GRID - 1))));

  return {
    height,
    type,
    keyAt(x, z) {
      return TERRAIN_KEYS[this.type[gz(z) * GRID + gx(x)]];
    },
    heightAt(x, z) {
      return this.height[gz(z) * GRID + gx(x)];
    },
    slopeAt(x, z) {
      const h = this.heightAt(x, z);
      const dx = this.heightAt(x + MAP_SIZE / GRID, z) - h;
      const dz = this.heightAt(x, z + MAP_SIZE / GRID) - h;
      return Math.min(1, Math.hypot(dx, dz) / 40);
    },
  };
}

/** 地形系数：把 combat.ts 的地形系数应用到具体地块 */
export function terrainFactor(td: TerrainData, x: number, z: number, cls: ArmyClass): number {
  return TERRAINS[td.keyAt(x, z)].perClass[cls];
}