// ============================================================================
//  Scene/Adjacency.ts —— 城池布局坐标表（唯一豁免的静态数据）+ 邻接连线。
//
//  【00 裁决豁免】契约 City 无坐标字段，允许 UI 内置一张「仅布局用」静态坐标表
//  （按城 Id）。城名 / 归属 / 人口 / 城防 / 首都等业务数据一律从 state 读，
//  禁止写进本文件。邻接连线按 state 的 AdjacentCityIds 动态绘制，与坐标表无关。
// ============================================================================

import * as THREE from 'three';
import type { WorldState } from '../Core/Types.js';

export interface CityLayoutPoint {
  x: number;
  z: number;
}

/** 仅布局用：城 Id → 地图平面坐标（x 向东，z 向南）。与任何业务数据无关。 */
export const CITY_LAYOUT: Readonly<Record<number, CityLayoutPoint>> = {
  0: { x: -62, z: -30 },
  1: { x: 2, z: -48 },
  2: { x: 66, z: -26 },
  3: { x: -44, z: 34 },
  4: { x: 12, z: 44 },
  5: { x: 74, z: 34 },
};

export const MAP_BOUND = 115; // 君主可行走范围半径

export function cityPosition(cityId: number): THREE.Vector3 {
  const p = CITY_LAYOUT[cityId];
  return new THREE.Vector3(p?.x ?? 0, 0, p?.z ?? 0);
}

/** 邻接连线组：底图全部连线（暗）+ 选中城的连线（亮） */
export class AdjacencyLines {
  readonly group = new THREE.Group();

  private baseLines: THREE.LineSegments | null = null;
  private highlightLines: THREE.LineSegments | null = null;

  rebuild(state: Readonly<WorldState>, selectedCityId: number | null): void {
    this.disposeBase();

    // 去重后的城对（a < b）
    const pairs: Array<[number, number]> = [];
    const seen = new Set<string>();
    for (const city of state.cities) {
      for (const adjId of city.adjacentCityIds) {
        const key = city.id < adjId ? `${city.id}-${adjId}` : `${adjId}-${city.id}`;
        if (city.id !== adjId && !seen.has(key)) {
          seen.add(key);
          pairs.push([Math.min(city.id, adjId), Math.max(city.id, adjId)]);
        }
      }
    }

    const toPoints = (list: Array<[number, number]>): number[] => {
      const arr: number[] = [];
      for (const [a, b] of list) {
        const pa = cityPosition(a);
        const pb = cityPosition(b);
        arr.push(pa.x, 0.4, pa.z, pb.x, 0.4, pb.z);
      }
      return arr;
    };

    const baseGeom = new THREE.BufferGeometry();
    baseGeom.setAttribute('position', new THREE.Float32BufferAttribute(toPoints(pairs), 3));
    this.baseLines = new THREE.LineSegments(
      baseGeom,
      new THREE.LineBasicMaterial({ color: 0x5f7392, transparent: true, opacity: 0.45 }),
    );
    this.group.add(this.baseLines);

    if (selectedCityId !== null) {
      const selected = state.cities.find((c) => c.id === selectedCityId);
      if (selected) {
        const linked = pairs.filter(([a, b]) => a === selectedCityId || b === selectedCityId);
        if (linked.length > 0) {
          const geom = new THREE.BufferGeometry();
          geom.setAttribute('position', new THREE.Float32BufferAttribute(toPoints(linked), 3));
          this.highlightLines = new THREE.LineSegments(
            geom,
            new THREE.LineBasicMaterial({ color: 0xc9a44c, transparent: true, opacity: 0.95 }),
          );
          this.group.add(this.highlightLines);
        }
      }
    }
  }

  setSelected(state: Readonly<WorldState>, selectedCityId: number | null): void {
    this.rebuild(state, selectedCityId);
  }

  private disposeBase(): void {
    if (this.baseLines) {
      this.group.remove(this.baseLines);
      this.baseLines.geometry.dispose();
      (this.baseLines.material as THREE.Material).dispose();
      this.baseLines = null;
    }
    if (this.highlightLines) {
      this.group.remove(this.highlightLines);
      this.highlightLines.geometry.dispose();
      (this.highlightLines.material as THREE.Material).dispose();
      this.highlightLines = null;
    }
  }

  dispose(): void {
    this.disposeBase();
  }
}
