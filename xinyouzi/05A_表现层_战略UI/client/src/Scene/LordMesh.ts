// ============================================================================
//  Scene/LordMesh.ts —— 玩家君主 mesh（资产槽 lord.glb，缺文件 → 程序化占位体）。
//  附带简易步行动画（表现层演出，不含任何业务逻辑）。
// ============================================================================

import * as THREE from 'three';
import type { AssetSlots } from './WorldScene.js';

export interface LordHandles {
  group: THREE.Group;
  /** speed: 0=静止；t: 累计秒。驱动腿臂摆动与轻微起伏 */
  animate(speed: number, t: number): void;
  setVisible(visible: boolean): void;
  dispose(): void;
}

export async function createLordMesh(factionHex: string, slots: AssetSlots): Promise<LordHandles> {
  const group = new THREE.Group();

  const gltf = await slots.get('lord.glb');
  if (gltf) {
    group.add(gltf);
    return {
      group,
      animate(speed: number, t: number): void {
        // 程序化外观资产不带动画：整体轻微浮动示意行走
        group.position.y = speed > 0.01 ? Math.abs(Math.sin(t * 8)) * 0.12 : 0;
      },
      setVisible(v: boolean): void {
        group.visible = v;
      },
      dispose(): void {
        group.clear();
      },
    };
  }

  // ---- 程序化占位君主 ----
  const cloth = new THREE.MeshLambertMaterial({ color: factionHex });
  const skin = new THREE.MeshLambertMaterial({ color: '#d9b38c' });
  const dark = new THREE.MeshLambertMaterial({ color: '#3a3f4a' });

  // 躯干（披风感）
  const torso = new THREE.Mesh(new THREE.CapsuleGeometry(0.34, 0.72, 4, 10), cloth);
  torso.position.y = 1.28;
  group.add(torso);

  // 腰带
  const belt = new THREE.Mesh(new THREE.CylinderGeometry(0.37, 0.37, 0.12, 10), dark);
  belt.position.y = 1.02;
  group.add(belt);

  // 头
  const head = new THREE.Mesh(new THREE.SphereGeometry(0.24, 12, 10), skin);
  head.position.y = 1.94;
  head.name = 'head';
  group.add(head);

  // 冠
  const crown = new THREE.Mesh(new THREE.CylinderGeometry(0.26, 0.22, 0.14, 8), new THREE.MeshLambertMaterial({ color: '#c9a44c' }));
  crown.position.y = 2.16;
  group.add(crown);

  // 腿 ×2
  const legGeo = new THREE.CapsuleGeometry(0.11, 0.5, 3, 8);
  const legL = new THREE.Mesh(legGeo, dark);
  legL.position.set(0.15, 0.42, 0);
  legL.name = 'legL';
  const legR = new THREE.Mesh(legGeo, dark);
  legR.position.set(-0.15, 0.42, 0);
  legR.name = 'legR';
  group.add(legL, legR);

  // 臂 ×2
  const armGeo = new THREE.CapsuleGeometry(0.09, 0.46, 3, 8);
  const armL = new THREE.Mesh(armGeo, cloth);
  armL.position.set(0.42, 1.3, 0);
  armL.name = 'armL';
  const armR = new THREE.Mesh(armGeo, cloth);
  armR.position.set(-0.42, 1.3, 0);
  armR.name = 'armR';
  group.add(armL, armR);

  // 佩剑
  const sword = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.9, 0.14), new THREE.MeshLambertMaterial({ color: '#9aa7bd' }));
  sword.position.set(-0.5, 0.98, 0.08);
  sword.rotation.z = 0.5;
  group.add(sword);

  return {
    group,
    animate(speed: number, t: number): void {
      const walking = speed > 0.01;
      const swing = walking ? Math.sin(t * 9) * 0.55 : 0;
      const legL = group.getObjectByName('legL');
      const legR = group.getObjectByName('legR');
      const armL = group.getObjectByName('armL');
      const armR = group.getObjectByName('armR');
      if (legL && legR && armL && armR) {
        legL.rotation.x = swing;
        legR.rotation.x = -swing;
        armL.rotation.x = -swing * 0.8;
        armR.rotation.x = swing * 0.8;
      }
      group.position.y = walking ? Math.abs(Math.sin(t * 9)) * 0.08 : 0;
    },
    setVisible(v: boolean): void {
      group.visible = v;
    },
    dispose(): void {
      group.traverse((o) => {
        const m = o as THREE.Mesh;
        if (m.isMesh) {
          m.geometry.dispose();
          (m.material as THREE.Material).dispose();
        }
      });
      group.clear();
    },
  };
}
