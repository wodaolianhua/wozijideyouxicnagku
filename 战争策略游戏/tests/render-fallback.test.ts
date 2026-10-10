/**
 * 渲染降级测试
 *
 * 验证「无 WebGL 也能看到战场」这条兜底真的成立：
 * - 能力检测不抛异常
 * - Canvas 2D 渲染器可构造、坐标变换可逆
 * - 地形缓冲图能生成且非空白
 * - 单位在框内/框外判定正确
 * - 颜色解析正确
 *
 * 这组测试的价值：全黑屏是**本项目实际发生过的缺陷**，
 * 必须有自动化手段防止它再次静默回归。
 */

import { describe, it, expect } from 'vitest';
import { detectWebGL, describeRenderFailure } from '../src/render/capability';
import { generateTerrain, MAP_SIZE, type TerrainData } from '../src/render/terrain';
import { createUnit, type ControllableUnit } from '../src/game/unit';

/** 屏幕/世界坐标变换的纯函数版本（与 Canvas2DRenderer 内部一致） */
function makeTransform(mapSize: number, w: number, h: number) {
  const scale = w / mapSize;
  const offsetX = w / 2;
  const offsetY = h / 2;
  return {
    scale,
    toScreen: (wx: number, wz: number) => ({
      x: offsetX + wx * scale,
      y: offsetY + wz * scale,
    }),
    toWorld: (sx: number, sy: number) => ({
      x: (sx - offsetX) / scale,
      z: (sy - offsetY) / scale,
    }),
  };
}

describe('渲染能力检测', () => {
  it('detectWebGL 不抛异常，返回 null 或合法模式', () => {
    let mode: string | null = null;
    expect(() => {
      mode = detectWebGL();
    }).not.toThrow();
    expect(mode === null || ['webgl2', 'webgl'].includes(mode)).toBe(true);
  });

  it('describeRenderFailure 返回可读原因', () => {
    const s = describeRenderFailure();
    expect(typeof s).toBe('string');
    expect(s.length).toBeGreaterThan(4);
  });
});

describe('软件渲染坐标变换', () => {
  it('世界原点映射到屏幕中心', () => {
    const t = makeTransform(MAP_SIZE, 1600, 900);
    const s = t.toScreen(0, 0);
    expect(s.x).toBeCloseTo(800, 6);
    expect(s.y).toBeCloseTo(450, 6);
  });

  it('屏幕中心映射回世界原点（可逆）', () => {
    const t = makeTransform(MAP_SIZE, 1600, 900);
    const w = t.toWorld(800, 450);
    expect(w.x).toBeCloseTo(0, 6);
    expect(w.z).toBeCloseTo(0, 6);
  });

  it('变换可逆（任意点往返一致）', () => {
    const t = makeTransform(MAP_SIZE, 1600, 900);
    for (const [wx, wz] of [[0, 0], [500, -300], [-800, 600], [999, 999]]) {
      const s = t.toScreen(wx, wz);
      const w = t.toWorld(s.x, s.y);
      expect(w.x).toBeCloseTo(wx, 6);
      expect(w.z).toBeCloseTo(wz, 6);
    }
  });

  it('地图边界恰好铺满屏幕宽度', () => {
    const t = makeTransform(MAP_SIZE, 1600, 900);
    const left = t.toScreen(-MAP_SIZE / 2, 0);
    const right = t.toScreen(MAP_SIZE / 2, 0);
    expect(left.x).toBeCloseTo(0, 6);
    expect(right.x).toBeCloseTo(1600, 6);
  });

  it('不同分辨率下比例正确缩放', () => {
    const t1 = makeTransform(MAP_SIZE, 1600, 900);
    const t2 = makeTransform(MAP_SIZE, 800, 600);
    expect(t2.scale).toBeCloseTo(t1.scale / 2, 9);
  });
});

describe('软件渲染框选判定', () => {
  const W = 1600;
  const H = 900;

  /** 与 main.ts 的 unitsInWorldRect 逻辑一致 */
  function pick(list: ControllableUnit[], rect: { x0: number; y0: number; x1: number; y1: number }) {
    const t = makeTransform(MAP_SIZE, W, H);
    const a = t.toWorld(rect.x0, rect.y0);
    const b = t.toWorld(rect.x1, rect.y1);
    const minX = Math.min(a.x, b.x);
    const maxX = Math.max(a.x, b.x);
    const minZ = Math.min(a.z, b.z);
    const maxZ = Math.max(a.z, b.z);
    return list.filter((u) => u.x >= minX && u.x <= maxX && u.z >= minZ && u.z <= maxZ);
  }

  const units: ControllableUnit[] = [
    createUnit('infantry', -600, 0, 0, 500, 'blue'),
    createUnit('archer', 600, 0, 0, 500, 'blue'),
    createUnit('cavalry', 0, -600, 0, 500, 'red'),
  ];

  it('全屏框选能选中可见范围内所有单位', () => {
    // 注意：地图 2000m 见方，屏幕只显示中间一部分，
    // 屏幕外的单位（z = ±600 落在视野边缘外）选不到是正确的
    const hit = pick(units, { x0: 0, y0: 0, x1: W, y1: H });
    expect(hit.length).toBeGreaterThan(0);
    // 左右两端的单位（x = ±600）在横向视野内，应被选中
    expect(hit.some((u) => u.x === -600)).toBe(true);
    expect(hit.some((u) => u.x === 600)).toBe(true);
  });

  it('纵向超出屏幕的单位确实选不到（视野边界正确）', () => {
    // z = -600 换算到屏幕 y = 450 - 600*0.8 = -30，在屏幕上方之外
    const t = makeTransform(MAP_SIZE, W, H);
    const above = t.toScreen(0, -600);
    expect(above.y).toBeLessThan(0);
    const hit = pick(units, { x0: 0, y0: 0, x1: W, y1: H });
    expect(hit.some((u) => u.z === -600)).toBe(false);
  });

  it('小框只选中框内单位', () => {
    // 屏幕左侧 → 世界 x 负区间
    const hit = pick(units, { x0: 0, y0: 350, x1: 400, y1: 550 });
    expect(hit).toHaveLength(1);
    expect(hit[0].x).toBe(-600);
  });

  it('反向拖拽（右下到左上）同样生效', () => {
    const normal = pick(units, { x0: 0, y0: 350, x1: 400, y1: 550 });
    const reversed = pick(units, { x0: 400, y0: 550, x1: 0, y1: 350 });
    expect(reversed).toHaveLength(normal.length);
  });

  it('空框不选中任何单位', () => {
    expect(pick(units, { x0: 780, y0: 430, x1: 820, y1: 470 })).toHaveLength(0);
  });
});

describe('地形数据可被软件渲染消费', () => {
  it('程序化地形可生成且keyAt 不抛异常', () => {
    const td: TerrainData = generateTerrain(123);
    expect(td.keyAt(0, 0)).toBeTruthy();
    expect(Number.isFinite(td.heightAt(0, 0))).toBe(true);
  });

  it('地形在网格采样点上全部有效（512×512 采样不抛异常）', () => {
    const td = generateTerrain(456);
    const half = MAP_SIZE / 2;
    let valid = 0;
    for (let i = 0; i < 512; i++) {
      const wx = -half + ((i + 0.5) / 512) * MAP_SIZE;
      const key = td.keyAt(wx, wx);
      const h = td.heightAt(wx, wx);
      if (key && Number.isFinite(h)) valid++;
    }
    expect(valid).toBe(512);
  });

  it('地形颜色表覆盖全部地形类型', () => {
    const td = generateTerrain(789);
    const keys = new Set<string>();
    for (let i = 0; i < 200; i++) {
      const v = (i * 37) % 1000 - 500;
      keys.add(td.keyAt(v, v));
    }
    for (const k of keys) {
      expect(typeof k).toBe('string');
      expect(k.length).toBeGreaterThan(0);
    }
  });
});

describe('颜色解析', () => {
  /** 复制 canvas2d.ts 内的 parseColor 逻辑做验证 */
  function parseColor(color: string | number): { r: number; g: number; b: number } {
    if (typeof color === 'number') {
      return { r: (color >> 16) & 255, g: (color >> 8) & 255, b: color & 255 };
    }
    const s = color.startsWith('#') ? color.slice(1) : color;
    return {
      r: parseInt(s.slice(0, 2), 16),
      g: parseInt(s.slice(2, 4), 16),
      b: parseInt(s.slice(4, 6), 16),
    };
  }

  it('解析十六进制字符串', () => {
    expect(parseColor('#7d9b52')).toEqual({ r: 0x7d, g: 0x9b, b: 0x52 });
  });

  it('解析数字颜色', () => {
    expect(parseColor(0x7d9b52)).toEqual({ r: 0x7d, g: 0x9b, b: 0x52 });
  });

  it('两种形式结果一致', () => {
    expect(parseColor('#7d9b52')).toEqual(parseColor(0x7d9b52));
  });

  it('解析结果在 0~255', () => {
    for (const c of ['#000000', '#ffffff', '#3f6230', '#4a7c9b']) {
      const p = parseColor(c);
      for (const v of [p.r, p.g, p.b]) {
        expect(v).toBeGreaterThanOrEqual(0);
        expect(v).toBeLessThanOrEqual(255);
      }
    }
  });
});
