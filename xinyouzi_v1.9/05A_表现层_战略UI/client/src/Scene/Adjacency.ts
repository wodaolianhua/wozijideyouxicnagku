// ============================================================================
//  Scene/Adjacency.ts —— 城池布局坐标表（唯一豁免的静态数据）
//
//  【00 裁决豁免】契约 City 无坐标字段，允许 UI 内置一张「仅布局用」静态坐标表。
//  城名 / 归属 / 人口 / 城防 / 首都等业务数据一律从 state 读，
//  禁止写进本文件。邻接关系也来自 state.adjacentCityIds，坐标表只管画在哪。
//
//  v1.9：从 3D 场景坐标（x/z + 高度）改成 2D 平面画布坐标（x 向东 / y 向南）。
//  七座城按「西北玩家 · 中东赤壤 · 南面苍岭」摆开，与邻接表对得上。
// ============================================================================

import type { WorldState } from '../Core/Types.js';

export interface CityLayoutPoint {
  x: number;
  y: number;
}

/** 画布逻辑尺寸（世界单位 = 像素 @ 1 倍缩放） */
export const MAP_W = 1000;
export const MAP_H = 760;

/**
 * 仅布局用：城 Id → 地图平面坐标。
 *
 * 关系校验（与 cities.json 的邻接表一致）：
 *   1↔2 · 2↔3 2↔4 · 3↔4 3↔5 · 4↔6 · 5↔7 · 6↔7
 */
export const CITY_LAYOUT: Readonly<Record<number, CityLayoutPoint>> = {
  1: { x: 190, y: 150 },
  2: { x: 380, y: 215 },
  3: { x: 600, y: 285 },
  4: { x: 390, y: 400 },
  5: { x: 790, y: 390 },
  6: { x: 300, y: 580 },
  7: { x: 610, y: 615 },
};

export function cityXY(cityId: number): CityLayoutPoint {
  return CITY_LAYOUT[cityId] ?? { x: MAP_W / 2, y: MAP_H / 2 };
}

/** 去重后的相邻城对（a < b），按 Id 排序，保证每次绘制顺序一致。 */
export function adjacencyPairs(state: Readonly<WorldState>): Array<[number, number]> {
  const seen = new Set<string>();
  const pairs: Array<[number, number]> = [];

  for (const city of state.cities) {
    for (const adjId of city.adjacentCityIds) {
      if (adjId === city.id) {
        continue;
      }
      const a = Math.min(city.id, adjId);
      const b = Math.max(city.id, adjId);
      const key = `${a}-${b}`;
      if (!seen.has(key)) {
        seen.add(key);
        pairs.push([a, b]);
      }
    }
  }

  pairs.sort((x, y) => (x[0] - y[0]) || (x[1] - y[1]));
  return pairs;
}
