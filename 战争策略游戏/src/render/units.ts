/**
 * 可操控单位渲染器
 *
 * 把 ControllableUnit（逻辑层）映射到 Three.js 网格（表现层）。
 * 每兵种用一个 InstancedMesh 承载全部该兵种单位，选中单位额外叠加高亮环。
 */

import * as THREE from 'three';
import type { ArmyClass } from '../model/combat';
import { CLASS_COLOR, makeSoldierLOD1, makeSoldierLOD2 } from './models';
import type { ControllableUnit } from '../game/unit';
import type { TerrainData } from './terrain';

export class UnitRenderer {
  readonly group = new THREE.Group();
  private meshes = new Map<ArmyClass, THREE.InstancedMesh>();
  private highlight: THREE.Mesh;
  private td: TerrainData;
  private dummy = new THREE.Object3D();
  /** 人才带来的战斗效率倍率（都尉/亲卫任职时 > 1） */
  private combatRate: number;

  constructor(td: TerrainData, maxPerClass = 200, combatRate = 1) {
    this.td = td;
    this.combatRate = combatRate;

    for (const cls of ['infantry', 'archer', 'cavalry'] as ArmyClass[]) {
      const geo = makeSoldierLOD1(cls);
      const mat = new THREE.MeshLambertMaterial({
        color: CLASS_COLOR[cls],
        flatShading: true,
      });
      const mesh = new THREE.InstancedMesh(geo, mat, maxPerClass);
      mesh.count = 0;
      mesh.frustumCulled = false;
      this.meshes.set(cls, mesh);
      this.group.add(mesh);
    }

    // 选中高亮环
    const ringGeo = new THREE.RingGeometry(1.6, 2.1, 16);
    ringGeo.rotateX(-Math.PI / 2);
    this.highlight = new THREE.Mesh(
      ringGeo,
      new THREE.MeshBasicMaterial({
        color: 0xffe066,
        transparent: true,
        opacity: 0.85,
        side: THREE.DoubleSide,
      }),
    );
    this.highlight.visible = false;
    this.group.add(this.highlight);
  }

  /** 设置战斗效率倍率（人才任命变化时调用） */
  setCombatRate(rate: number) {
    this.combatRate = rate;
  }

  /** 全量刷新所有单位的位置与朝向 */
  update(units: ControllableUnit[]) {
    const counts = new Map<ArmyClass, number>();
    for (const u of units) counts.set(u.cls, (counts.get(u.cls) ?? 0) + 1);

    // 容量不足时扩容
    for (const [cls, mesh] of this.meshes) {
      const need = counts.get(cls) ?? 0;
      if (need > mesh.instanceMatrix.count) {
        const bigger = new THREE.InstancedMesh(
          mesh.geometry,
          mesh.material as THREE.Material,
          Math.ceil(need * 1.4),
        );
        bigger.frustumCulled = false;
        this.group.remove(mesh);
        mesh.dispose();
        this.meshes.set(cls, bigger);
        this.group.add(bigger);
      }
    }

    // 写实例矩阵
    for (const [cls, mesh] of this.meshes) {
      const list = units.filter((u) => u.cls === cls);
      for (let i = 0; i < list.length; i++) {
        const u = list[i];
        this.dummy.position.set(u.x, this.td.heightAt(u.x, u.z), u.z);
        this.dummy.rotation.set(0, u.heading, 0);
        // 战斗效率倍率仅影响我方（敌方不享受玩家的用人加成）
        const scale = u.side === 'blue' ? this.combatRate : 1;
        this.dummy.scale.setScalar(scale);
        this.dummy.updateMatrix();
        mesh.setMatrixAt(i, this.dummy.matrix);
      }
      mesh.count = list.length;
      mesh.instanceMatrix.needsUpdate = true;
    }

    // 高亮环跟随第一个选中单位
    const sel = units.find((u) => u.selected);
    if (sel) {
      this.highlight.visible = true;
      this.highlight.position.set(
        sel.x,
        this.td.heightAt(sel.x, sel.z) + 0.15,
        sel.z,
      );
    } else {
      this.highlight.visible = false;
    }
  }

  dispose() {
    for (const mesh of this.meshes.values()) {
      mesh.geometry.dispose();
      (mesh.material as THREE.Material).dispose();
      mesh.dispose();
    }
    this.highlight.geometry.dispose();
    (this.highlight.material as THREE.Material).dispose();
  }
}

/** 大规模部队的立方体渲染（LOD2），用于远景单位 */
export class MassRenderer {
  readonly group = new THREE.Group();
  private meshes = new Map<string, THREE.InstancedMesh>();
  private dummy = new THREE.Object3D();
  private td: TerrainData;

  constructor(td: TerrainData) {
    this.td = td;
  }

  update(units: ControllableUnit[], maxPerBucket = 4000) {
    // 按 (兵种, 阵营) 分桶
    const buckets = new Map<string, ControllableUnit[]>();
    for (const u of units) {
      const key = `${u.side}_${u.cls}`;
      const arr = buckets.get(key) ?? [];
      arr.push(u);
      buckets.set(key, arr);
    }

    for (const [key, mesh] of this.meshes) {
      if (!buckets.has(key)) {
        this.group.remove(mesh);
        mesh.geometry.dispose();
        (mesh.material as THREE.Material).dispose();
        mesh.dispose();
        this.meshes.delete(key);
      }
    }

    for (const [key, list] of buckets) {
      if (list.length > maxPerBucket) list.length = maxPerBucket;
      const cls = key.split('_')[1] as ArmyClass;
      const side = key.split('_')[0] as 'blue' | 'red';
      const color =
        side === 'blue'
          ? CLASS_COLOR[cls]
          : new THREE.Color(CLASS_COLOR[cls]).multiplyScalar(0.62).getHex();

      let mesh = this.meshes.get(key);
      if (!mesh || mesh.instanceMatrix.count < list.length) {
        if (mesh) {
          this.group.remove(mesh);
          mesh.dispose();
        }
        mesh = new THREE.InstancedMesh(
          makeSoldierLOD2(),
          new THREE.MeshLambertMaterial({ color, flatShading: true }),
          Math.ceil(list.length * 1.3),
        );
        mesh.frustumCulled = false;
        this.meshes.set(key, mesh);
        this.group.add(mesh);
      }

      for (let i = 0; i < list.length; i++) {
        const u = list[i];
        this.dummy.position.set(u.x, this.td.heightAt(u.x, u.z) + 0.85, u.z);
        this.dummy.rotation.set(0, u.heading, 0);
        this.dummy.scale.setScalar(1);
        this.dummy.updateMatrix();
        mesh.setMatrixAt(i, this.dummy.matrix);
      }
      mesh.count = list.length;
      mesh.instanceMatrix.needsUpdate = true;
    }
  }

  dispose() {
    for (const mesh of this.meshes.values()) {
      mesh.geometry.dispose();
      (mesh.material as THREE.Material).dispose();
      mesh.dispose();
    }
    this.meshes.clear();
  }
}
