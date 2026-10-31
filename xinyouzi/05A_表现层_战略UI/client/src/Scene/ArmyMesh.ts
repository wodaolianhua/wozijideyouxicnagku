// ============================================================================
//  Scene/ArmyMesh.ts —— 部队 mesh（资产槽 army_infantry.glb，缺文件 → 程序化占位）。
//  士兵用 THREE.InstancedMesh 批量渲染（性能红线）。位置：驻城 / 行军插值。
// ============================================================================

import * as THREE from 'three';
import type { Army, WorldState } from '../Core/Types.js';
import { factionColor } from '../Ui/Theme.js';
import { cityPosition } from './Adjacency.js';
import type { AssetSlots } from './WorldScene.js';

const SOLDIER_MAX_INSTANCES = 48;

export interface ArmyMeshHandles {
  group: THREE.Group;
  update(army: Readonly<Army>, state: Readonly<WorldState>): void;
  dispose(): void;
}

export async function createArmyMesh(army: Readonly<Army>, slots: AssetSlots): Promise<ArmyMeshHandles> {
  const group = new THREE.Group();
  group.userData.armyId = army.id;

  // ---- 资产槽：army_infantry.glb（缺文件 → 程序化占位体） ----
  const gltf = await slots.get('army_infantry.glb');
  if (gltf) {
    group.add(gltf);
  } else {
    // 旗帜 + 兵圈（InstancedMesh）
    const pole = new THREE.Mesh(
      new THREE.CylinderGeometry(0.1, 0.1, 5.4, 5),
      new THREE.MeshLambertMaterial({ color: '#4a4034' }),
    );
    pole.position.y = 2.7;
    group.add(pole);

    const flag = new THREE.Mesh(
      new THREE.PlaneGeometry(2.4, 1.5),
      new THREE.MeshBasicMaterial({ color: factionColor(army.ownerFactionId), side: THREE.DoubleSide }),
    );
    flag.position.set(1.25, 4.55, 0);
    flag.name = 'flag';
    group.add(flag);

    const ring = new THREE.Mesh(
      new THREE.RingGeometry(2.6, 3.1, 28),
      new THREE.MeshBasicMaterial({ color: factionColor(army.ownerFactionId), transparent: true, opacity: 0.35, side: THREE.DoubleSide }),
    );
    ring.rotation.x = -Math.PI / 2;
    ring.position.y = 0.18;
    ring.name = 'ring';
    group.add(ring);

    const soldierGeo = new THREE.BoxGeometry(0.5, 1.15, 0.4);
    const soldierMat = new THREE.MeshLambertMaterial({ color: '#8f8a7c' });
    const soldiers = new THREE.InstancedMesh(soldierGeo, soldierMat, SOLDIER_MAX_INSTANCES);
    soldiers.name = 'soldiers';
    soldiers.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    group.add(soldiers);
  }

  return {
    group,
    update(armyNow: Readonly<Army>, state: Readonly<WorldState>): void {
      // 位置：驻城；有行军计划时在两城之间插值 55%
      const from = cityPosition(armyNow.positionCityId);
      const moving = armyNow.targetCityId >= 0 && armyNow.targetCityId !== armyNow.positionCityId;
      if (moving) {
        const to = cityPosition(armyNow.targetCityId);
        group.position.copy(from.lerp(to, 0.55));
      } else {
        // 城边驻扎（避免与城体重叠）
        group.position.set(from.x + 13.5, 0, from.z + 7.5);
      }

      // 旗帜/圈着色
      const color = factionColor(armyNow.ownerFactionId);
      group.traverse((o) => {
        const mesh = o as THREE.Mesh;
        if (!mesh.isMesh) {
          return;
        }
        if (mesh.name === 'flag' || mesh.name === 'ring') {
          const mat = mesh.material as THREE.MeshBasicMaterial;
          mat.color.set(color);
        }
      });

      // 士兵实例：数量随兵力（展示级换算，非业务计算：仅映射到显示上限）
      const soldiersMesh = group.getObjectByName('soldiers') as THREE.InstancedMesh | undefined;
      if (soldiersMesh) {
        const count = THREE.MathUtils.clamp(Math.round(armyNow.soldiers / 180), 3, SOLDIER_MAX_INSTANCES);
        const dummy = new THREE.Object3D();
        const rows = Math.ceil(Math.sqrt(count));
        for (let i = 0; i < SOLDIER_MAX_INSTANCES; i++) {
          if (i < count) {
            const r = Math.floor(i / rows);
            const c = i % rows;
            dummy.position.set(
              (c - (rows - 1) / 2) * 1.15,
              0.62 + 0.06 * Math.sin(i * 2.3),
              (r - (rows - 1) / 2) * 1.15,
            );
          } else {
            dummy.position.set(0, -50, 0); // 隐藏多余实例
          }
          dummy.updateMatrix();
          soldiersMesh.setMatrixAt(i, dummy.matrix);
        }
        soldiersMesh.instanceMatrix.needsUpdate = true;
        soldiersMesh.count = SOLDIER_MAX_INSTANCES;
      }

      // 兵力文字标注跟随（交给 WorldScene 的 hover/selection，可扩展）
      void state;
    },
    dispose(): void {
      group.traverse((o) => {
        const m = o as THREE.Mesh;
        if (m.isMesh) {
          m.geometry?.dispose();
          (m.material as THREE.Material)?.dispose();
        }
      });
      group.clear();
    },
  };
}
