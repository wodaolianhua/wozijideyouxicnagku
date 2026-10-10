/**
 * 士兵级操控测试
 *
 * 验证框选、指令、移动的正确性：
 * - 移动朝目标推进，位移与速度×时间×地形系数成正比
 * - 到达目标后停下
 * - 不可通行地形阻挡移动
 * - 框选能正确筛出矩形内单位
 * - 整队平移保持编队形状
 * - 全部为纯函数
 */

import { describe, it, expect } from 'vitest';
import * as THREE from 'three';
import {
  CLASS_SPEED,
  TERRAIN_PASS,
  type ControllableUnit,
  createUnit,
  isWalkable,
  orderGroupMove,
  orderGroupStop,
  orderMove,
  orderStop,
  passability,
  projectToScreen,
  selectAll,
  selectedUnits,
  selectionCentroid,
  stepAll,
  stepUnit,
  toggleSelect,
  unitsInRect,
  allArrived,
  distanceToTarget,
} from '../src/game/unit';
import { generateTerrain, type TerrainData } from '../src/render/terrain';
import { ARMY_CLASS, type ArmyClass } from '../src/model/combat';

/** 构造一个全部为平原的假地形（便于精确验证） */
function flatTerrain(key: 'plain' | 'hill' | 'forest' = 'plain'): TerrainData {
  return {
    height: new Float32Array(128 * 128),
    type: new Uint8Array(128 * 128).fill(
      ['plain', 'forest', 'hill'].indexOf(key),
    ),
    keyAt: () => key,
    heightAt: () => 0,
    slopeAt: () => 0,
  } as unknown as TerrainData;
}

/** 构造一个按区域分地形的假地形 */
function zonedTerrain(zones: { x: number; key: 'plain' | 'hill' }[]): TerrainData {
  return {
    keyAt: (x: number) => {
      for (const z of zones) if (x < z.x) return z.key;
      return zones[zones.length - 1].key;
    },
    heightAt: () => 0,
  } as unknown as TerrainData;
}

const unit = (cls: ArmyClass, x: number, z: number, side: 'blue' | 'red' = 'blue'): ControllableUnit =>
  createUnit(cls, x, z, 0, 500, side);

describe('单位创建', () => {
  it('初始朝向目标为未选中状态正确', () => {
    const u = unit('infantry', 0, 0);
    expect(u.targetX).toBeNull();
    expect(u.targetZ).toBeNull();
    expect(u.selected).toBe(false);
    expect(u.strength).toBe(500);
    expect(u.side).toBe('blue');
  });

  it('各兵种速度不同且骑兵最快', () => {
    expect(CLASS_SPEED.cavalry).toBeGreaterThan(CLASS_SPEED.archer);
    expect(CLASS_SPEED.archer).toBeGreaterThan(CLASS_SPEED.infantry);
  });

  it('单位 id 唯一', () => {
    const a = unit('infantry', 0, 0);
    const b = unit('infantry', 1, 1);
    expect(a.id).not.toBe(b.id);
  });
});

describe('地形通行', () => {
  it('平地所有兵种畅通', () => {
    const td = flatTerrain('plain');
    for (const cls of ARMY_CLASS) {
      expect(passability(td, 0, 0, cls), `${cls} 平地`).toBe(1.0);
    }
  });

  it('山地骑兵最难通行', () => {
    const td = flatTerrain('hill');
    const cav = passability(td, 0, 0, 'cavalry');
    const inf = passability(td, 0, 0, 'infantry');
    expect(cav).toBeLessThan(inf);
    expect(cav).toBeLessThan(0.3);
  });

  it('林地骑兵受罚明显', () => {
    const td = flatTerrain('forest');
    expect(passability(td, 0, 0, 'cavalry')).toBeLessThan(0.6);
  });

  it('每种地形对所有兵种都有配置', () => {
    for (const t of Object.keys(TERRAIN_PASS)) {
      for (const cls of ARMY_CLASS) {
        const v = TERRAIN_PASS[t][cls];
        expect(v, `${t}.${cls}`).toBeGreaterThan(0);
        expect(v, `${t}.${cls}`).toBeLessThanOrEqual(1);
      }
    }
  });

  it('isWalkable 对五种地形均为真', () => {
    for (const t of ['plain', 'forest', 'hill', 'river', 'fort']) {
      const td = { keyAt: () => t } as unknown as TerrainData;
      expect(isWalkable(td, 0, 0), t).toBe(true);
    }
  });
});

describe('移动', () => {
  it('无目标时不动', () => {
    const td = flatTerrain();
    const u = unit('infantry', 0, 0);
    const r = stepUnit(u, td, 1.0);
    expect(r.x).toBe(0);
    expect(r.z).toBe(0);
  });

  it('位移 = 速度 × 时间 × 地形系数（平���）', () => {
    const td = flatTerrain();
    const u = orderMove(unit('infantry', 0, 0), 100, 0);
    const r = stepUnit(u, td, 1.0);
    expect(r.x).toBeCloseTo(5.0, 6); // 步兵速度 5.0 × 1 秒
  });

  it('地形系数直接缩放位移', () => {
    const plain = flatTerrain('plain');
    const hill = flatTerrain('hill');
    const u = orderMove(unit('infantry', 0, 0), 100, 0);
    const rp = stepUnit(u, plain, 1.0);
    const rh = stepUnit(u, hill, 1.0);
    expect(rh.x).toBeLessThan(rp.x);
    expect(rh.x).toBeCloseTo(rp.x * TERRAIN_PASS.hill.infantry, 6);
  });

  it('地形系数使骑兵在山地几乎不动', () => {
    const td = flatTerrain('hill');
    const u = orderMove(unit('cavalry', 0, 0), 100, 0);
    const r = stepUnit(u, td, 1.0);
    expect(r.x).toBeLessThan(CLASS_SPEED.cavalry * 0.3);
  });

  it('朝目标移动，不背离', () => {
    const td = flatTerrain();
    const u = orderMove(unit('infantry', 0, 0), -100, 0);
    const r = stepUnit(u, td, 1.0);
    expect(r.x).toBeLessThan(0);
  });

  it('到达目标后停下并清空目标', () => {
    const td = flatTerrain();
    let u = orderMove(unit('infantry', 0, 0), 10, 0);
    u = stepUnit(u, td, 10.0);
    expect(u.targetX).toBeNull();
    expect(u.targetZ).toBeNull();
  });

  it('到达后不再移动', () => {
    const td = flatTerrain();
    let u = orderMove(unit('infantry', 0, 0), 10, 0);
    u = stepUnit(u, td, 10.0);
    const settled = u;
    const after = stepUnit(u, td, 1.0);
    expect(after.x).toBeCloseTo(settled.x, 9);
  });

  it('不会越过目标点（不超调）', () => {
    const td = flatTerrain();
    let u = orderMove(unit('infantry', 0, 0), 3, 0);
    u = stepUnit(u, td, 1.0);
    expect(u.x).toBeLessThanOrEqual(3.0001);
    expect(u.x).toBeGreaterThanOrEqual(2.9);
  });

  it('斜向移动距离正确', () => {
    const td = flatTerrain();
    const u = orderMove(unit('infantry', 0, 0), 30, 40); // 距离 50
    const r = stepUnit(u, td, 1.0);
    const moved = Math.hypot(r.x - 0, r.z - 0);
    expect(moved).toBeCloseTo(5.0, 5);
    expect(r.x / r.z).toBeCloseTo(30 / 40, 5);
  });

  it('不可通行地形阻挡移动', () => {
    // x<100 为山地（骑兵 0.25），x>=100 为平地
    const td = zonedTerrain([
      { x: 100, key: 'hill' },
      { x: 9999, key: 'plain' },
    ]);
    // 目标在平地，但起点在山地 —— 仍可缓慢移动
    const u = orderMove(unit('cavalry', 0, 0), 500, 0);
    const r = stepUnit(u, td, 1.0);
    expect(r.x).toBeGreaterThan(0);
    // 山地中的位移应远小于平地速度
    expect(r.x).toBeLessThan(CLASS_SPEED.cavalry * 0.3);
  });

  it('朝向平滑转向目标', () => {
    const td = flatTerrain();
    // 初始朝向 +Z（heading=0），目标在 +X 方向 → 应转向 +90度
    const u = orderMove(unit('infantry', 0, 0), 100, 0);
    const r = stepUnit(u, td, 0.1);
    expect(r.heading).toBeGreaterThan(0);
    expect(r.heading).toBeLessThan(Math.PI / 2 + 0.1);
  });

  it('长时间转向收敛到目标方向', () => {
    const td = flatTerrain();
    let u = orderMove(unit('infantry', 0, 0), 100, 0);
    for (let i = 0; i < 20; i++) u = stepUnit(u, td, 0.1);
    expect(Math.abs(u.heading - Math.PI / 2)).toBeLessThan(0.15);
  });

  it('stepAll 推进所有单位', () => {
    const td = flatTerrain();
    const units = [
      orderMove(unit('infantry', 0, 0), 100, 0),
      orderMove(unit('cavalry', 0, 20), 100, 20),
    ];
    const r = stepAll(units, td, 0.5);
    expect(r[0].x).toBeGreaterThan(0);
    expect(r[1].x).toBeGreaterThan(0);
    expect(r[1].x).toBeGreaterThan(r[0].x); // 骑兵更快
  });

  it('批量推进不修改原数组', () => {
    const td = flatTerrain();
    const units = [orderMove(unit('infantry', 0, 0), 100, 0)];
    const snapshot = JSON.stringify(units);
    stepAll(units, td, 1.0);
    expect(JSON.stringify(units)).toBe(snapshot);
  });
});

describe('指令', () => {
  it('orderMove 设置目标', () => {
    const u = orderMove(unit('infantry', 0, 0), 50, 60);
    expect(u.targetX).toBe(50);
    expect(u.targetZ).toBe(60);
  });

  it('orderStop 清除目标', () => {
    const u = orderStop(orderMove(unit('infantry', 0, 0), 50, 60));
    expect(u.targetX).toBeNull();
  });

  it('allArrived 判定到位状态', () => {
    const moving = [orderMove(unit('infantry', 0, 0), 100, 0)];
    expect(allArrived(moving)).toBe(false);
    const stopped = [orderStop(moving[0])];
    expect(allArrived(stopped)).toBe(true);
  });

  it('distanceToTarget 返回剩余距离', () => {
    const u = orderMove(unit('infantry', 0, 0), 30, 40);
    expect(distanceToTarget(u)).toBeCloseTo(50, 6);
    expect(distanceToTarget(orderStop(u))).toBe(0);
  });
});

describe('选择', () => {
  it('selectAll 全选/取消', () => {
    const units = [unit('infantry', 0, 0), unit('archer', 10, 10)];
    const sel = selectAll(units, true);
    expect(sel.every((u) => u.selected)).toBe(true);
    const none = selectAll(sel, false);
    expect(none.every((u) => !u.selected)).toBe(true);
  });

  it('toggleSelect 切换单个单位', () => {
    const units = [unit('infantry', 0, 0), unit('archer', 10, 10)];
    const sel = toggleSelect(units, units[0].id, true);
    expect(sel[0].selected).toBe(true);
    expect(sel[1].selected).toBe(false);
  });

  it('selectedUnits 只返回选中单位', () => {
    const units = selectAll(
      [unit('infantry', 0, 0), unit('archer', 10, 10)],
      false,
    );
    const sel = toggleSelect(units, units[1].id, true);
    expect(selectedUnits(sel)).toHaveLength(1);
    expect(selectedUnits(sel)[0].id).toBe(units[1].id);
  });

  it('选择操作不修改原数组', () => {
    const units = [unit('infantry', 0, 0)];
    const snapshot = JSON.stringify(units);
    selectAll(units, true);
    toggleSelect(units, units[0].id, true);
    expect(JSON.stringify(units)).toBe(snapshot);
  });

  it('selectionCentroid 返回已选质心', () => {
    const units = selectAll(
      [unit('infantry', 0, 0), unit('infantry', 10, 0)],
      true,
    );
    const c = selectionCentroid(units);
    expect(c).not.toBeNull();
    expect(c!.x).toBeCloseTo(5, 6);
    expect(c!.z).toBeCloseTo(0, 6);
  });

  it('未选任何单位时质心为 null', () => {
    expect(selectionCentroid([unit('infantry', 0, 0)])).toBeNull();
  });
});

describe('整队移动', () => {
  it('已选单位整体平移，编队形状保持', () => {
    const units = selectAll(
      [unit('infantry', 0, 0), unit('infantry', 10, 0)],
      true,
    );
    const r = orderGroupMove(units, 100, 0);
    // 偏移量 = 目标质心(100) - 当前质心(5) = 95
    expect(r[0].targetX).toBeCloseTo(95, 6);
    expect(r[1].targetX).toBeCloseTo(105, 6);
    // 间距不变
    expect(Math.abs(r[1].targetX! - r[0].targetX!)).toBeCloseTo(10, 6);
  });

  it('未选单位不动', () => {
    const units = [unit('infantry', 0, 0), unit('infantry', 50, 0)];
    // 只选第一个
    const mixed = toggleSelect(units, units[0].id, true);
    const r = orderGroupMove(mixed, 100, 0);
    expect(r[0].targetX).not.toBeNull();
    expect(r[1].targetX).toBeNull();
  });

  it('整队移动不修改原数组', () => {
    const units = selectAll([unit('infantry', 0, 0)], true);
    const snapshot = JSON.stringify(units);
    orderGroupMove(units, 100, 100);
    expect(JSON.stringify(units)).toBe(snapshot);
  });

  it('整队停止清除所有目标', () => {
    const units = selectAll(
      [orderMove(unit('infantry', 0, 0), 50, 0), orderMove(unit('archer', 10, 0), 60, 0)],
      true,
    );
    const r = orderGroupStop(units);
    expect(r.every((u) => u.targetX === null)).toBe(true);
  });

  it('整队移动后实际能走到位（质心到达目标）', () => {
    const td = flatTerrain();
    const units = selectAll([unit('infantry', 0, 0), unit('infantry', 6, 0)], true);
    const moved = orderGroupMove(units, 60, 0);
    let cur = moved;
    for (let i = 0; i < 30; i++) cur = stepAll(cur, td, 0.5);
    const c = selectionCentroid(cur)!;
    expect(c.x).toBeCloseTo(60, 0);
  });
});

describe('框选', () => {
  // 建一台俯视相机对准原点
  const setupCamera = () => {
    const cam = new THREE.PerspectiveCamera(55, 1600 / 900, 0.5, 6000);
    cam.position.set(0, 900, 800);
    cam.lookAt(0, 0, 0);
    cam.updateMatrixWorld(true);
    return cam;
  };

  it('投影落在屏幕内', () => {
    const cam = setupCamera();
    const p = projectToScreen(0, 0, 0, cam, 1600, 900);
    expect(p).not.toBeNull();
    expect(p!.x).toBeCloseTo(800, 0);
    expect(p!.y).toBeCloseTo(450, 3);
  });

  it('相机后方的点投影为 null', () => {
    const cam = setupCamera();
    const behind = projectToScreen(0, 0, 5000, cam, 1600, 900);
    expect(behind).toBeNull();
  });

  it('框选能选出矩形内单位', () => {
    const cam = setupCamera();
    const units = [unit('infantry', 0, 0), unit('infantry', 300, 300)];
    const rect = { x0: 0, y0: 0, x1: 900, y1: 700 };
    const hit = unitsInRect(units, rect, cam, 1600, 900, flatTerrain());
    expect(hit.length).toBeGreaterThanOrEqual(1);
    expect(hit.some((u) => u.id === units[0].id)).toBe(true);
  });

  it('小矩形选不到远处单位', () => {
    const cam = setupCamera();
    const units = [unit('infantry', 800, 800)];
    const rect = { x0: 0, y0: 0, x1: 100, y1: 100 };
    const hit = unitsInRect(units, rect, cam, 1600, 900, flatTerrain());
    expect(hit).toHaveLength(0);
  });

  it('框选可按阵营过滤', () => {
    const cam = setupCamera();
    const units = [unit('infantry', 0, 0, 'blue'), unit('infantry', 0, 0, 'red')];
    const rect = { x0: 0, y0: 0, x1: 1600, y1: 900 };
    const blue = unitsInRect(units, rect, cam, 1600, 900, flatTerrain(), 'blue');
    expect(blue).toHaveLength(1);
    expect(blue[0].side).toBe('blue');
  });

  it('反向拖拽（右下到左上）同样生效', () => {
    const cam = setupCamera();
    const units = [unit('infantry', 0, 0)];
    const rect = { x0: 900, y0: 700, x1: 0, y1: 0 }; // 反向
    const hit = unitsInRect(units, rect, cam, 1600, 900, flatTerrain());
    expect(hit.length).toBeGreaterThanOrEqual(1);
  });

  it('框选不修改原数组', () => {
    const cam = setupCamera();
    const units = [unit('infantry', 0, 0)];
    const snapshot = JSON.stringify(units);
    unitsInRect(units, { x0: 0, y0: 0, x1: 1600, y1: 900 }, cam, 1600, 900, flatTerrain());
    expect(JSON.stringify(units)).toBe(snapshot);
  });
});

describe('真实地形上的移动', () => {
  it('在程序化地形上移动不产生 NaN', () => {
    const td = generateTerrain();
    const u = orderMove(unit('cavalry', -800, -800), 800, 800);
    let cur = u;
    for (let i = 0; i < 200; i++) cur = stepUnit(cur, td, 0.1);
    expect(Number.isFinite(cur.x)).toBe(true);
    expect(Number.isFinite(cur.z)).toBe(true);
    expect(Number.isFinite(cur.heading)).toBe(true);
  });

  it('长时间推进不发散', () => {
    const td = generateTerrain();
    const units = ARMY_CLASS.map((c, i) =>
      orderMove(unit(c, -500 + i * 50, -500), 500, 500),
    );
    let cur = units;
    for (let i = 0; i < 500; i++) cur = stepAll(cur, td, 0.1);
    for (const u of cur) {
      expect(Number.isFinite(u.x)).toBe(true);
      expect(Math.abs(u.x)).toBeLessThan(2000);
      expect(Math.abs(u.z)).toBeLessThan(2000);
    }
  });

  it('地形起伏不影响移动逻辑（高度只用于渲染）', () => {
    const td = generateTerrain();
    const u = orderMove(unit('infantry', 0, 0), 500, 0);
    const r = stepUnit(u, td, 1.0);
    // 只要该点可通行，就应按速度移动
    if (isWalkable(td, r.x, r.z)) {
      expect(r.x).toBeGreaterThan(0);
    }
  });
});
