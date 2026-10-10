/**
 * 路径寻径 —— A* 算法
 *
 * 解决「直线移动遇山不绕行」的缺口。
 * 代价函数 = 距离 × (2 - 通行系数)，即难走的地形代价更高但仍可通行。
 *
 * 设计取舍：
 * - 网格寻径（不是连续空间），格距 10 米，足够表达战场尺度
 * - 通行系数为 0 的地形视为障碍（当前五种地形的系数都 > 0，
 *   但保留障碍判定以便后续加入河流、沼泽等不可通行地形）
 * - 限制展开节点数，防止极端情况下卡死主循环
 */

import type { ArmyClass } from '../model/combat';
import { passability, TERRAIN_PASS } from './unit';
import type { TerrainData } from '../render/terrain';

/** 寻径格网边长（米） */
export const CELL = 10;

/** 最大展开节点数（超出则返回近似路径而非精确路径） */
export const MAX_EXPANSIONS = 20000;

export interface PathNode {
  gx: number;
  gz: number;
}

export interface GridSpec {
  minX: number;
  minZ: number;
  width: number;  // 格数
  height: number; // 格数
}

export function makeGrid(mapSize: number): GridSpec {
  const n = Math.floor(mapSize / CELL);
  return {
    minX: -mapSize / 2,
    minZ: -mapSize / 2,
    width: n,
    height: n,
  };
}

export function toCell(grid: GridSpec, x: number, z: number): PathNode {
  return {
    gx: Math.max(0, Math.min(grid.width - 1, Math.floor((x - grid.minX) / CELL))),
    gz: Math.max(0, Math.min(grid.height - 1, Math.floor((z - grid.minZ) / CELL))),
  };
}

export function toWorld(grid: GridSpec, gx: number, gz: number): { x: number; z: number } {
  return {
    x: grid.minX + (gx + 0.5) * CELL,
    z: grid.minZ + (gz + 0.5) * CELL,
  };
}

/** 该格对某兵种的通行代价（1 = 最好，Infinity = 不可通行） */
export function cellCost(
  td: TerrainData,
  grid: GridSpec,
  gx: number,
  gz: number,
  cls: ArmyClass,
): number {
  if (gx < 0 || gz < 0 || gx >= grid.width || gz >= grid.height) return Infinity;
  const w = toWorld(grid, gx, gz);
  const p = passability(td, w.x, w.z, cls);
  if (p <= 0) return Infinity;
  // 通行系数 1.0 → 代价 1.0；通行系数 0.25 → 代价 1.75
  return 2 - p;
}

/** 八方向邻居（含对角） */
const NEIGHBORS: ReadonlyArray<readonly [number, number, number]> = [
  [1, 0, 1], [-1, 0, 1], [0, 1, 1], [0, -1, 1],
  [1, 1, Math.SQRT2], [1, -1, Math.SQRT2], [-1, 1, Math.SQRT2], [-1, -1, Math.SQRT2],
];

/** 二叉堆优先队列（A* 需要按 f值取出最小） */
class MinHeap {
  private items: Array<{ key: number; gx: number; gz: number }> = [];
  get size(): number { return this.items.length; }
  push(key: number, gx: number, gz: number) {
    this.items.push({ key, gx, gz });
    let i = this.items.length - 1;
    while (i > 0) {
      const p = (i - 1) >> 1;
      if (this.items[p].key <= this.items[i].key) break;
      [this.items[p], this.items[i]] = [this.items[i], this.items[p]];
      i = p;
    }
  }
  pop(): { key: number; gx: number; gz: number } | undefined {
    if (this.items.length === 0) return undefined;
    const top = this.items[0];
    const last = this.items.pop()!;
    if (this.items.length > 0) {
      this.items[0] = last;
      let i = 0;
      for (;;) {
        const l = i * 2 + 1;
        const r = l + 1;
        let m = i;
        if (l < this.items.length && this.items[l].key < this.items[m].key) m = l;
        if (r < this.items.length && this.items[r].key < this.items[m].key) m = r;
        if (m === i) break;
        [this.items[m], this.items[i]] = [this.items[i], this.items[m]];
        i = m;
      }
    }
    return top;
  }
}

/**
 * A* 寻径。
 *
 * @returns 世界坐标路径点列表（含终点，不含起点）；无路可走时返回 null
 */
export function findPath(
  td: TerrainData,
  grid: GridSpec,
  fromX: number,
  fromZ: number,
  toX: number,
  toZ: number,
  cls: ArmyClass,
): Array<{ x: number; z: number }> | null {
  const start = toCell(grid, fromX, fromZ);
  const goal = toCell(grid, toX, toZ);
  if (start.gx === goal.gx && start.gz === goal.gz) {
    // 同一格内无需寻径；若终点与起点仍有距离，仍返回终点
    return Math.hypot(toX - fromX, toZ - fromZ) < 0.5 ? [] : [{ x: toX, z: toZ }];
  }

  const w = grid.width;
  const cameFrom = new Int32Array(w * grid.height).fill(-1);
  const gScore = new Float64Array(w * grid.height).fill(Infinity);
  const closed = new Uint8Array(w * grid.height);

  const h = (gx: number, gz: number) => {
    const dx = Math.abs(gx - goal.gx);
    const dz = Math.abs(gz - goal.gz);
    // 八角距离
    return (Math.max(dx, dz) + (Math.SQRT2 - 1) * Math.min(dx, dz)) * CELL;
  };

  const open = new MinHeap();
  const startIdx = start.gz * w + start.gx;
  gScore[startIdx] = 0;
  open.push(h(start.gx, start.gz), start.gx, start.gz);

  let expansions = 0;
  let found = false;
  let hitLimit = false;

  while (open.size > 0) {
    const cur = open.pop()!;
    const ci = cur.gz * w + cur.gx;
    if (closed[ci]) continue;
    closed[ci] = 1;
    if (cur.gx === goal.gx && cur.gz === goal.gz) {
      found = true;
      break;
    }
    if (++expansions > MAX_EXPANSIONS) {
      hitLimit = true;
      break;
    }

    for (const [dx, dz, base] of NEIGHBORS) {
      const nx = cur.gx + dx;
      const nz = cur.gz + dz;
      if (nx < 0 || nz < 0 || nx >= w || nz >= grid.height) continue;
      const ni = nz * w + nx;
      if (closed[ni]) continue;
      // 对角线不允许穿角（防止斜穿障碍）
      if (dx !== 0 && dz !== 0) {
        if (cellCost(td, grid, cur.gx + dx, cur.gz, cls) === Infinity) continue;
        if (cellCost(td, grid, cur.gx, cur.gz + dz, cls) === Infinity) continue;
      }
      const step = cellCost(td, grid, nx, nz, cls);
      if (step === Infinity) continue;
      const tentative = gScore[ci] + step * base * CELL;
      if (tentative < gScore[ni]) {
        gScore[ni] = tentative;
        cameFrom[ni] = ci;
        open.push(tentative + h(nx, nz), nx, nz);
      }
    }
  }

  if (!found) {
    // 超出扩展上限时返回「近似路径」——从已探索区域里取离目标最近的点。
    // 这比直接返回 null 好：玩家看到部队朝目标方向移动，而不是原地不动。
    if (hitLimit) {
      let bestIdx = -1;
      let bestH = Infinity;
      for (let i = 0; i < closed.length; i++) {
        if (!closed[i]) continue;
        const gx = i % w;
        const gz = Math.floor(i / w);
        const hh = h(gx, gz);
        if (hh < bestH) {
          bestH = hh;
          bestIdx = i;
        }
      }
      if (bestIdx >= 0) {
        const partial: Array<{ x: number; z: number }> = [];
        let i = bestIdx;
        while (i !== -1 && i !== startIdx) {
          const gx = i % w;
          const gz = Math.floor(i / w);
          partial.push(toWorld(grid, gx, gz));
          i = cameFrom[i];
        }
        partial.reverse();
        if (partial.length > 0) {
          partial[0] = { x: fromX, z: fromZ };
          return partial;
        }
      }
    }
    return null;
  }

  // 回溯：终点 → 起点
  const path: Array<{ x: number; z: number }> = [];
  let idx = goal.gz * w + goal.gx;
  while (idx !== -1 && idx !== startIdx) {
    const gx = idx % w;
    const gz = Math.floor(idx / w);
    path.push(toWorld(grid, gx, gz));
    idx = cameFrom[idx];
  }
  path.reverse();
  // 起点格中心可能与真实起点差半格；替换为真实起点，保证首段不跳跃。
  // 不能用 unshift —— 那会多出一个与第一格中心同格的点，间距变成 2 格。
  if (path.length > 0) {
    path[0] = { x: fromX, z: fromZ };
    path[path.length - 1] = { x: toX, z: toZ };
  } else {
    path.push({ x: toX, z: toZ });
  }
  return path;
}

/** 路径总长度（米） */
export function pathLength(path: Array<{ x: number; z: number }>, fromX: number, fromZ: number): number {
  let len = 0;
  let px = fromX;
  let pz = fromZ;
  for (const p of path) {
    len += Math.hypot(p.x - px, p.z - pz);
    px = p.x;
    pz = p.z;
  }
  return len;
}

/** 直线距离（作为寻径结果的对照） */
export function straightDistance(fromX: number, fromZ: number, toX: number, toZ: number): number {
  return Math.hypot(toX - fromX, toZ - fromZ);
}

/**
 * 路径简化：去掉可以直连的中间点。
 *
 * A* 在开阔地形会产生锯齿状路径。若a→c 之间的所有格都可通行，
 * 则中间的 b 是多余的，可以让单位从 a 直接走到 c，移动更平滑。
 */
export function simplifyPath(
  path: Array<{ x: number; z: number }>,
  fromX: number,
  fromZ: number,
  td: TerrainData,
  grid: GridSpec,
  cls: ArmyClass,
): Array<{ x: number; z: number }> {
  if (path.length <= 1) return path;
  const out: Array<{ x: number; z: number }> = [];
  let ax = fromX;
  let az = fromZ;
  let i = 0;

  while (i < path.length) {
    let best = i;
    // 从最远点往回找第一个可直连的
    for (let j = path.length - 1; j >= i; j--) {
      if (hasLineOfWalk(td, grid, ax, az, path[j].x, path[j].z, cls)) {
        best = j;
        break;
      }
    }
    out.push(path[best]);
    ax = path[best].x;
    az = path[best].z;
    i = best + 1;
  }
  return out;
}

/** 两点之间是否可直线通行（采样检查） */
export function hasLineOfWalk(
  td: TerrainData,
  grid: GridSpec,
  fromX: number,
  fromZ: number,
  toX: number,
  toZ: number,
  cls: ArmyClass,
): boolean {
  const dist = Math.hypot(toX - fromX, toZ - fromZ);
  const steps = Math.max(1, Math.ceil(dist / (CELL * 0.5)));
  for (let s = 1; s < steps; s++) {
    const t = s / steps;
    const px = fromX + (toX - fromX) * t;
    const pz = fromZ + (toZ - fromZ) * t;
    const c = toCell(grid, px, pz);
    if (cellCost(td, grid, c.gx, c.gz, cls) === Infinity) return false;
  }
  return true;
}

export { TERRAIN_PASS };
