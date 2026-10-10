/**
 * 部队渲染 —— LOD 三层实例化
 *
 * 对应文档 §4 的性能方案：
 * - LOD0: 独立网格（近处小队，保留造型细节）
 * - LOD1: InstancedMesh（交战部队）
 * - LOD2: InstancedMesh 立方体（大场面）
 *
 * 单位数据来自 combat.ts 的编制结构，向上聚合兼容裁定#6。
 */

import * as THREE from 'three';
import { type ArmyClass } from '../model/combat';
import { CLASS_COLOR, makeSoldier, makeSoldierLOD1, makeSoldierLOD2 } from './models';
import type { TerrainData } from './terrain';

export interface Formation {
  cls: ArmyClass;
  count: number;
  x: number;
  z: number;
  heading: number; // 弧度
  morale: number;
}

export interface Army {
  side: 'blue' | 'red';
  formations: Formation[];
}

/** LOD 切换距离（米） */
export const LOD0_DIST = 100;
export const LOD1_DIST = 400;

/** LOD1 每编制最大的实例数（超出则降到 LOD2） */
const MAX_INSTANCES_L1 = 900;

const dummy = new THREE.Object3D();

export class ArmyRenderer {
  readonly group = new THREE.Group();
  private lod0: THREE.Group[] = [];
  private lod1: Map<string, THREE.InstancedMesh> = new Map();
  private lod2: Map<string, THREE.InstancedMesh> = new Map();
  private terrain: TerrainData;
  private lastCounts: Map<string, number> = new Map();

  constructor(terrain: TerrainData) {
    this.terrain = terrain;
    this.group.name = 'armies';
  }

  /** 全量重建（原型阶段每回合调用） */
  rebuild(armies: Army[]) {
    for (const g of this.lod0) g.parent?.remove(g);
    this.lod0 = [];
    for (const m of this.lod1.values()) m.parent?.remove(m);
    this.lod1.clear();
    for (const m of this.lod2.values()) m.parent?.remove(m);
    this.lod2.clear();
    this.lastCounts.clear();

    const buildArgs = new Map<string, { f: Formation; color: number; side: 'blue' | 'red' }>();
    for (const army of armies) {
      for (const f of army.formations) {
        const color =
          army.side === 'blue'
            ? CLASS_COLOR[f.cls]
            : new THREE.Color(CLASS_COLOR[f.cls]).multiplyScalar(0.62).getHex();
        buildArgs.set(`${army.side}_${f.cls}_${f.x}`, { f, color, side: army.side });
      }
    }

    for (const { f, color, side } of buildArgs.values()) {
      // 分配到 LOD 层：编制 ≤200 人走LOD0独立网格，否则拆 LOD1/LOD2 实例化
      if (f.count <= 200) {
        this.buildL0(f, side);
      } else {
        const l1Count = Math.min(f.count, MAX_INSTANCES_L1);
        const l2Count = f.count - l1Count;
        if (l1Count > 0) this.buildL1(f, l1Count, color, `l1_${side}_${f.cls}`);
        if (l2Count > 0) this.buildL2(f, l2Count, color, `l2_${side}_${f.cls}`);
      }
    }
  }

  private positions(f: Formation, n: number, seed: number): Float32Array {
    // 编队阵型：按行排列，间距 2.2米（视觉可辨又不重叠）
    const out = new Float32Array(n * 3);
    const cols = Math.ceil(Math.sqrt(n * 1.6));
    let s = seed;
    const rand = () => {
      s = (s * 1664525 + 1013904223) >>> 0;
      return s / 4294967296;
    };
    const cos = Math.cos(f.heading);
    const sin = Math.sin(f.heading);
    for (let i = 0; i < n; i++) {
      const row = Math.floor(i / cols);
      const col = i % cols;
      const lx = (col - cols / 2) * 2.2 + (rand() - 0.5) * 0.8;
      const lz = (row - row * 0.35) * 2.2 + (rand() - 0.5) * 0.8;
      out[i * 3] = f.x + lx * cos - lz * sin;
      out[i * 3 + 1] = 0;
      out[i * 3 + 2] = f.z + lx * sin + lz * cos;
    }
    return out;
  }

  private buildL0(f: Formation, side: 'blue' | 'red') {
    const g = new THREE.Group();
    const n = Math.min(f.count, 200);
    const pos = this.positions(f, n, 1337 + f.x * 31 + f.z * 17);
    for (let i = 0; i < n; i++) {
      const proto = makeSoldier(f.cls, side);
      const m = proto;
      const x = pos[i * 3];
      const z = pos[i * 3 + 2];
      m.position.set(x, this.terrain.heightAt(x, z), z);
      m.rotation.y = f.heading;
      g.add(m);
    }
    this.group.add(g);
    this.lod0.push(g);
  }

  private buildL1(
    f: Formation,
    n: number,
    color: number,
    key: string,
  ) {
    const geo = makeSoldierLOD1(f.cls);
    const mat = new THREE.MeshLambertMaterial({ color, flatShading: true });
    const mesh = new THREE.InstancedMesh(geo, mat, n);
    const pos = this.positions(f, n, 7331 + f.x * 13 + f.z * 7);
    for (let i = 0; i < n; i++) {
      const x = pos[i * 3];
      const z = pos[i * 3 + 2];
      dummy.position.set(x, this.terrain.heightAt(x, z), z);
      dummy.rotation.set(0, f.heading, 0);
      dummy.scale.setScalar(1);
      dummy.updateMatrix();
      mesh.setMatrixAt(i, dummy.matrix);
    }
    mesh.instanceMatrix.needsUpdate = true;
    mesh.frustumCulled = false;
    this.group.add(mesh);
    this.lod1.set(key, mesh);
    this.lastCounts.set(key, n);
  }

  private buildL2(f: Formation, n: number, color: number, key: string) {
    const geo = makeSoldierLOD2();
    const mat = new THREE.MeshLambertMaterial({ color, flatShading: true });
    const mesh = new THREE.InstancedMesh(geo, mat, n);
    const pos = this.positions(f, n, 9931 + f.x * 19 + f.z * 23);
    for (let i = 0; i < n; i++) {
      const x = pos[i * 3];
      const z = pos[i * 3 + 2];
      dummy.position.set(x, this.terrain.heightAt(x, z) + 0.85, z);
      dummy.rotation.set(0, f.heading, 0);
      dummy.scale.setScalar(1);
      dummy.updateMatrix();
      mesh.setMatrixAt(i, dummy.matrix);
    }
    mesh.instanceMatrix.needsUpdate = true;
    mesh.frustumCulled = false;
    this.group.add(mesh);
    this.lod2.set(key, mesh);
    this.lastCounts.set(key, n);
  }

  /** 统计实际渲染的单位数与 draw call 数 */
  stats(): { l0: number; l1: number; l2: number; drawCalls: number } {
    let l0 = 0;
    for (const g of this.lod0) l0 += g.children.length;
    let l1 = 0;
    for (const m of this.lod1.values()) l1 += m.count;
    let l2 = 0;
    for (const m of this.lod2.values()) l2 += m.count;
    return {
      l0,
      l1,
      l2,
      drawCalls: this.lod0.length * 8 + this.lod1.size + this.lod2.size,
    };
  }

  dispose() {
    for (const g of this.lod0) {
      g.traverse((o) => {
        const m = o as THREE.Mesh;
        if (m.isMesh) {
          m.geometry.dispose();
          (m.material as THREE.Material)?.dispose();
        }
      });
    }
    for (const m of this.lod1.values()) {
      m.geometry.dispose();
      (m.material as THREE.Material).dispose();
      m.dispose();
    }
    for (const m of this.lod2.values()) {
      m.geometry.dispose();
      (m.material as THREE.Material).dispose();
      m.dispose();
    }
  }
}