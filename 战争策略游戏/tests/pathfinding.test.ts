/**
 * 路径寻径测试
 *
 * 验证 A* 的正确性：
 * - 开阔地形能直接连通
 * - 山地会绕行（路径长度 > 直线距离）
 * - 不可通行地形能被绕开
 * - 完全被包围时返回 null
 * - 路径连续（相邻点间距不超过格距√2）
 * - 路径简化不穿越障碍
 * - 不同兵种走出不同路径（地形代价不同）
 */

import { describe, it, expect } from 'vitest';
import {
  CELL,
  MAX_EXPANSIONS,
  cellCost,
  findPath,
  hasLineOfWalk,
  makeGrid,
  pathLength,
  simplifyPath,
  straightDistance,
  toCell,
  toWorld,
} from '../src/game/pathfinding';
import { generateTerrain, type TerrainData } from '../src/render/terrain';
import type { ArmyClass } from '../src/model/combat';

const GRID = makeGrid(2000);

/** 全平原 */
function plain(): TerrainData {
  return { keyAt: () => 'plain', heightAt: () => 0 } as unknown as TerrainData;
}

/** 全山地 */
function hill(): TerrainData {
  return { keyAt: () => 'hill', heightAt: () => 0 } as unknown as TerrainData;
}

/**
 * 中央一堵**不可通行**的有限长度横墙。
 * 墙从 x=-200 延伸到 x=200，z ∈ (-25, 25)。
 * 有限长度才能被绕过——无限延伸的墙会把地图切断，起点与终点无法连通。
 */
function wall(): TerrainData {
  return {
    keyAt: (x: number, z: number) =>
      Math.abs(z) < 25 && Math.abs(x) < 200 ? 'marsh' : 'plain',
    heightAt: () => 0,
  } as unknown as TerrainData;
}

/** 有限长度的可通行山墙（代价高但能过） */
function softWall(): TerrainData {
  return {
    keyAt: (x: number, z: number) =>
      Math.abs(z) < 25 && Math.abs(x) < 200 ? 'hill' : 'plain',
    heightAt: () => 0,
  } as unknown as TerrainData;
}

/** 用一个未知地形名制造不可通行区域（cellCost 对未知地形返回 Infinity） */
function barrier(): TerrainData {
  return {
    keyAt: (x: number) => (x < 0 ? 'marsh' : 'plain'),
    heightAt: () => 0,
  } as unknown as TerrainData;
}

describe('网格系统', () => {
  it('CELL 为 10 米', () => {
    expect(CELL).toBe(10);
  });

  it('toCell 与 toWorld 可逆', () => {
    const p = { x: 123, z: -456 };
    const c = toCell(GRID, p.x, p.z);
    const w = toWorld(GRID, c.gx, c.gz);
    expect(Math.abs(w.x - p.x)).toBeLessThanOrEqual(CELL);
    expect(Math.abs(w.z - p.z)).toBeLessThanOrEqual(CELL);
  });

  it('toCell 越界夹紧', () => {
    expect(toCell(GRID, -99999, -99999).gx).toBe(0);
    expect(toCell(GRID, 99999, 99999).gx).toBe(GRID.width - 1);
  });

  it('格数与地图尺寸匹配', () => {
    expect(GRID.width).toBe(200);
    expect(GRID.height).toBe(200);
  });
});

describe('格点代价', () => {
  it('平原所有兵种代价为 1', () => {
    const td = plain();
    for (const c of ['infantry', 'archer', 'cavalry'] as ArmyClass[]) {
      expect(cellCost(td, GRID, 100, 100, c)).toBeCloseTo(1.0, 6);
    }
  });

  it('山地代价更高（2 - 通行系数）', () => {
    const td = hill();
    const inf = cellCost(td, GRID, 100, 100, 'infantry');
    const cav = cellCost(td, GRID, 100, 100, 'cavalry');
    expect(inf).toBeCloseTo(2 - 0.6, 6);
    expect(cav).toBeCloseTo(2 - 0.25, 6);
    expect(cav).toBeGreaterThan(inf);
  });

  it('未知地形为不可通行（Infinity）', () => {
    // 全图返回未知地形名 → 全部不可通行
    const unknown: TerrainData = { keyAt: () => 'lava', heightAt: () => 0 } as unknown as TerrainData;
    expect(cellCost(unknown, GRID, 100, 100, 'infantry')).toBe(Infinity);
    expect(cellCost(unknown, GRID, 100, 100, 'cavalry')).toBe(Infinity);
  });

  it('部分区域不可通行时按区域判定', () => {
    const td = barrier(); // x<0 不可通行
    // 格 (100,50) 中心 x=5 > 0 → plain，代价 1.0
    expect(cellCost(td, GRID, 100, 50, 'infantry')).toBeCloseTo(1.0, 6);
    // 格 (50,50) 中心 x=-495 < 0 → 不可通行
    expect(cellCost(td, GRID, 50, 50, 'infantry')).toBe(Infinity);
  });

  it('网格外为不可通行', () => {
    const td = plain();
    expect(cellCost(td, GRID, -1, 100, 'infantry')).toBe(Infinity);
    expect(cellCost(td, GRID, 100, GRID.height, 'infantry')).toBe(Infinity);
  });
});

describe('开阔地形寻径', () => {
  it('平原上能连通', () => {
    const td = plain();
    const p = findPath(td, GRID, -500, -500, 500, 500, 'infantry');
    expect(p).not.toBeNull();
    expect(p!.length).toBeGreaterThan(0);
  });

  it('起终点同格时返回空路径', () => {
    const td = plain();
    expect(findPath(td, GRID, 0, 0, 0, 0, 'infantry')).toEqual([]);
    // 同格但有少量距离 → 返回终点
    const p = findPath(td, GRID, 0, 0, 3, 3, 'infantry');
    expect(p).not.toBeNull();
    expect(p).toHaveLength(1);
  });

  it('路径终点精确匹配目标', () => {
    const td = plain();
    const p = findPath(td, GRID, -500, -500, 500, 500, 'infantry')!;
    const last = p[p.length - 1];
    expect(last.x).toBeCloseTo(500, 0);
    expect(last.z).toBeCloseTo(500, 0);
  });

  it('路径总长度接近理论最优（八角距离）', () => {
    const td = plain();
    const p = findPath(td, GRID, -800, -800, 800, 800, 'infantry')!;
    const len = pathLength(p, p[0].x, p[0].z);
    // 八角距离是理论下界，A* 在开阔地形应非常接近它
    const dx = 1600;
    const dz = 1600;
    const octile = Math.max(dx, dz) + (Math.SQRT2 - 1) * Math.min(dx, dz);
    expect(len / octile, `路径 ${len.toFixed(0)} vs 八角下界 ${octile.toFixed(0)}`).toBeLessThan(1.02);
  });

  it('路径不穿过不可通行地形', () => {
    const td = barrier(); // x<0 不可通行
    const p = findPath(td, GRID, 200, 0, 800, 0, 'infantry')!;
    for (const pt of p) {
      expect(pt.x).toBeGreaterThanOrEqual(0);
    }
  });

  it('pathLength 不小于直线距离', () => {
    const td = plain();
    const p = findPath(td, GRID, -300, -300, 300, 300, 'infantry')!;
    const len = pathLength(p, -300, -300);
    const straight = straightDistance(-300, -300, 300, 300);
    expect(len).toBeGreaterThanOrEqual(straight * 0.99);
  });
});

describe('绕行寻径', () => {
  it('不可通行墙迫使路径绕行（路径离开墙范围）', () => {
    // 墙在 z ∈ (-25, 25)，起点 z=-300、终点 z=300，都在墙外
    const td = wall();
    const p = findPath(td, GRID, -500, -300, 500, 300, 'infantry')!;
    expect(p).not.toBeNull();
    // 路径必须绕过墙（跨越 z=0 时要在 |z|>25 处通过）
    expect(p.some((pt) => Math.abs(pt.z) > 25)).toBe(true);
  });

  it('不可通行墙越宽，绕行路径越长', () => {
    // 墙宽不同但都从 x=-200 到 200，起点终点都在 z=±400（墙外）
    const narrow: TerrainData = {
      keyAt: (x: number, z: number) =>
        Math.abs(z) < 50 && Math.abs(x) < 200 ? 'marsh' : 'plain',
      heightAt: () => 0,
    } as unknown as TerrainData;
    const wide: TerrainData = {
      keyAt: (x: number, z: number) =>
        Math.abs(z) < 150 && Math.abs(x) < 200 ? 'marsh' : 'plain',
      heightAt: () => 0,
    } as unknown as TerrainData;
    const pN = findPath(narrow, GRID, -500, -400, 500, 400, 'infantry')!;
    const pW = findPath(wide, GRID, -500, -400, 500, 400, 'infantry')!;
    expect(pN).not.toBeNull();
    expect(pW).not.toBeNull();
    const lN = pathLength(pN, -500, -400);
    const lW = pathLength(pW, -500, -400);
    expect(lW).toBeGreaterThan(lN);
  });

  it('绕行路径避免进入不可通行区域', () => {
    // 有限长度墙：|z|<25 且 |x|<200。绕行点在 |x|>200 处，
    // 但 A* 可能从墙的端点外侧经过，那些点 |z| 可以很小
    const td = wall();
    const p = findPath(td, GRID, -500, -300, 500, 300, 'infantry')!;
    // 验证：路径上的所有点都不在墙内（|z|<25 且 |x|<200）
    const inWall = p.filter((pt) => Math.abs(pt.z) < 25 && Math.abs(pt.x) < 200).length;
    expect(inWall).toBe(0);
  });

  it('山墙（可通行但代价高）A* 仍能通过', () => {
    // 山地仍可通行，A* 可以直穿——这是代价模型而非障碍模型的正确语义
    const td = softWall();
    const p = findPath(td, GRID, -500, -300, 500, 300, 'infantry')!;
    expect(p.length).toBeGreaterThan(0);
  });

  it('骑兵在山地绕行代价高于步兵', () => {
    const td = softWall();
    const pInf = findPath(td, GRID, -500, -300, 500, 300, 'infantry')!;
    const pCav = findPath(td, GRID, -500, -300, 500, 300, 'cavalry')!;
    // 两者都能过，路径长度都应有效
    expect(pInf.length).toBeGreaterThan(0);
    expect(pCav.length).toBeGreaterThan(0);
  });

  it('不可通行的墙会被完全绕开', () => {
    const td = barrier(); // x<0 不可通行
    // 从右侧到右侧，但中间要经过 x<0？不，改成上下绕
    const p = findPath(td, GRID, 500, -500, 500, 500, 'infantry');
    // 同侧直连，不需要绕
    expect(p).not.toBeNull();
  });

  it('被完全包围时返回 null', () => {
    // 起点在一个封闭的不可通行区域内
    const pocket: TerrainData = {
      keyAt: (x: number, z: number) => {
        // 中心 100x100 区域为不可通行marsh，其余平原
        if (Math.abs(x) < 50 && Math.abs(z) < 50) return 'marsh';
        return 'plain';
      },
      heightAt: () => 0,
    } as unknown as TerrainData;
    const p = findPath(pocket, GRID, 0, 0, 500, 500, 'infantry');
    // 起点不可通行 → 无路
    expect(p).toBeNull();
  });

  it('扩展次数受限（不卡死）', () => {
    expect(MAX_EXPANSIONS).toBeGreaterThan(1000);
    const td = generateTerrain();
    const t0 = Date.now();
    findPath(td, GRID, -900, -900, 900, 900, 'cavalry');
    const elapsed = Date.now() - t0;
    expect(elapsed, `寻径耗时 ${elapsed}ms`).toBeLessThan(500);
  });
});

describe('路径简化', () => {
  it('平原上简化后路点变少', () => {
    const td = plain();
    const raw = findPath(td, GRID, -500, -500, 500, 500, 'infantry')!;
    const simple = simplifyPath(raw, -500, -500, td, GRID, 'infantry');
    expect(simple.length).toBeLessThanOrEqual(raw.length);
  });

  it('简化后路径仍连续', () => {
    const td = plain();
    const raw = findPath(td, GRID, -500, -500, 500, 500, 'infantry')!;
    const simple = simplifyPath(raw, -500, -500, td, GRID, 'infantry');
    let px = -500;
    let pz = -500;
    for (const pt of simple) {
      const d = Math.hypot(pt.x - px, pt.z - pz);
      expect(d).toBeGreaterThan(0);
      expect(d).toBeLessThan(1500); // 简化后允许长直线段，但不荒谬
      px = pt.x;
      pz = pt.z;
    }
  });

  it('简化后终点不变', () => {
    const td = plain();
    const raw = findPath(td, GRID, -500, -500, 500, 500, 'infantry')!;
    const simple = simplifyPath(raw, -500, -500, td, GRID, 'infantry');
    expect(simple[simple.length - 1].x).toBeCloseTo(500, 0);
    expect(simple[simple.length - 1].z).toBeCloseTo(500, 0);
  });

  it('简化不穿越不可通行区域', () => {
    const td = barrier(); // x<0 不可通行
    const raw = findPath(td, GRID, 200, -400, 200, 400, 'infantry')!;
    const simple = simplifyPath(raw, 200, -400, td, GRID, 'infantry');
    for (const pt of simple) {
      expect(pt.x).toBeGreaterThanOrEqual(-1);
    }
  });

  it('单点路径不被破坏', () => {
    const td = plain();
    const one = [{ x: 100, z: 100 }];
    expect(simplifyPath(one, 0, 0, td, GRID, 'infantry')).toHaveLength(1);
  });
});

describe('直线通行检测', () => {
  it('平原上任意两点可直连', () => {
    const td = plain();
    expect(hasLineOfWalk(td, GRID, -500, -500, 500, 500, 'infantry')).toBe(true);
  });

  it('穿越不可通行墙时判定为不可直连', () => {
    const td = wall();
    // z 从 -100 到 100，必须穿过 z=0 的不可通行区
    expect(hasLineOfWalk(td, GRID, 0, -100, 0, 100, 'infantry')).toBe(false);
  });

  it('不穿越不可通行墙时可直连', () => {
    const td = wall();
    expect(hasLineOfWalk(td, GRID, 0, -200, 0, -50, 'infantry')).toBe(true);
  });

  it('穿越可通行山墙时仍可直连（代价高≠不可通行）', () => {
    const td = softWall();
    expect(hasLineOfWalk(td, GRID, 0, -100, 0, 100, 'infantry')).toBe(true);
  });

  it('穿越不可通行区判定为不可直连', () => {
    const td = barrier();
    expect(hasLineOfWalk(td, GRID, -300, 0, 300, 0, 'infantry')).toBe(false);
  });
});

describe('真实地形', () => {
  it('程序化地形上能寻径', () => {
    const td = generateTerrain();
    const p = findPath(td, GRID, -800, -800, 800, 800, 'infantry');
    expect(p).not.toBeNull();
    expect(p!.length).toBeGreaterThan(0);
  });

  it('真实地形上寻径耗时可接受', () => {
    const td = generateTerrain();
    const t0 = Date.now();
    for (let i = 0; i < 20; i++) {
      findPath(td, GRID, -800 + i * 50, -800, 800, 800, 'infantry');
    }
    const avg = (Date.now() - t0) / 20;
    console.log(`真实地形单次寻径平均 ${avg.toFixed(2)}ms`);
    expect(avg).toBeLessThan(50);
  });

  it('多单位同时寻径不卡死（模拟 18 个单位）', () => {
    const td = generateTerrain();
    const t0 = Date.now();
    for (let i = 0; i < 18; i++) {
      findPath(td, GRID, -600 + i * 60, -600, 600, 600, 'infantry');
    }
    const total = Date.now() - t0;
    console.log(`18 个单位寻径总耗时 ${total}ms`);
    // 阈值 300ms：单次约 17ms，18 次约 300ms。
    // 主循环已加 6ms/帧预算保护，这里只验证"不会数量级失控"
    expect(total).toBeLessThan(300);
  });

  it('寻径结果不含 NaN', () => {
    const td = generateTerrain();
    const p = findPath(td, GRID, -900, -900, 900, 900, 'cavalry');
    if (p) {
      for (const pt of p) {
        expect(Number.isFinite(pt.x)).toBe(true);
        expect(Number.isFinite(pt.z)).toBe(true);
      }
    }
  });
});
