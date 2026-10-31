// ============================================================================
//  Scene/Terrain.ts —— 地形（程序化占位；资产槽 terrain.glb 缺文件时兜底）
//  + 装饰性植被 InstancedMesh。地形高度函数导出供行走贴地使用。
// ============================================================================

import * as THREE from 'three';
import { cityPosition } from './Adjacency.js';
import type { AssetSlots } from './WorldScene.js';

/** 地形高度：多频正弦叠加 + 城区压平。振幅小，保证可走性优先。 */
export function terrainHeight(x: number, z: number): number {
  const h =
    2.0 * Math.sin(x * 0.045) * Math.cos(z * 0.05) +
    1.1 * Math.sin(x * 0.11 + 1.7) * Math.sin(z * 0.09 + 0.6) +
    0.45 * Math.sin(x * 0.23 + 4.2) * Math.cos(z * 0.21 + 2.1);
  // 越靠地图边缘略微抬升，形成盆地感
  const edge = Math.max(Math.abs(x), Math.abs(z)) / 115;
  const rim = edge > 0.82 ? (edge - 0.82) * 46 : 0;
  return h + rim;
}

/** 城区平滑压平因子：0 = 完全压平，1 = 保持原高度 */
function flattenFactor(x: number, z: number): number {
  let nearest = Infinity;
  for (const key of Object.keys(CITY_IDS)) {
    const id = Number(key);
    const p = cityPosition(id);
    const d = Math.hypot(p.x - x, p.z - z);
    if (d < nearest) {
      nearest = d;
    }
  }
  if (nearest >= 20) {
    return 1;
  }
  if (nearest <= 9) {
    return 0;
  }
  const t = (nearest - 9) / 11;
  return t * t * (3 - 2 * t); // smoothstep
}

// 布局坐标表的键集合（仅用于压平计算，不涉及业务数据）
const CITY_IDS: Readonly<Record<number, unknown>> = { 0: 0, 1: 0, 2: 0, 3: 0, 4: 0, 5: 0 };

export interface TerrainHandles {
  group: THREE.Group;
  dispose(): void;
}

export async function createTerrain(slots: AssetSlots): Promise<TerrainHandles> {
  const group = new THREE.Group();

  // ---- 资产槽：terrain.glb（缺文件 → 程序化占位地形，绝不崩溃） ----
  const gltf = await slots.get('terrain.glb');
  if (gltf) {
    group.add(gltf);
    return { group, dispose: () => group.clear() };
  }

  // ---- 程序化地形 ----
  const size = 240;
  const seg = 96;
  const geo = new THREE.PlaneGeometry(size, size, seg, seg);
  geo.rotateX(-Math.PI / 2);
  const pos = geo.attributes.position as THREE.BufferAttribute;
  const colors = new Float32Array(pos.count * 3);
  const cLow = new THREE.Color('#3d5940');
  const cMid = new THREE.Color('#57713f');
  const cHigh = new THREE.Color('#8a8a70');
  for (let i = 0; i < pos.count; i++) {
    const x = pos.getX(i);
    const z = pos.getZ(i);
    const h = terrainHeight(x, z) * flattenFactor(x, z);
    pos.setY(i, h);
    const t = THREE.MathUtils.clamp((h + 2.2) / 5.4, 0, 1);
    const c = cLow.clone().lerp(cMid, THREE.MathUtils.clamp(t * 2, 0, 1)).lerp(cHigh, THREE.MathUtils.clamp(t * 2 - 1, 0, 1));
    colors[i * 3] = c.r;
    colors[i * 3 + 1] = c.g;
    colors[i * 3 + 2] = c.b;
  }
  geo.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  geo.computeVertexNormals();
  const mesh = new THREE.Mesh(
    geo,
    new THREE.MeshLambertMaterial({ vertexColors: true }),
  );
  mesh.name = 'terrain';
  group.add(mesh);

  // ---- 装饰性植被：InstancedMesh（性能红线要求的批量渲染示例之一） ----
  const treeCount = 110;
  const trunkGeo = new THREE.CylinderGeometry(0.16, 0.22, 1.1, 5);
  const crownGeo = new THREE.ConeGeometry(0.85, 2.2, 6);
  const trunkMesh = new THREE.InstancedMesh(
    trunkGeo,
    new THREE.MeshLambertMaterial({ color: '#5d4630' }),
    treeCount,
  );
  const crownMesh = new THREE.InstancedMesh(
    crownGeo,
    new THREE.MeshLambertMaterial({ color: '#33502f' }),
    treeCount,
  );
  const dummy = new THREE.Object3D();
  // 简单 LCG：布局稳定，不涉及业务随机
  let seed = 20261031;
  const rand = (): number => {
    seed = (seed * 1103515245 + 12345) & 0x7fffffff;
    return seed / 0x7fffffff;
  };
  let placed = 0;
  while (placed < treeCount) {
    const x = (rand() * 2 - 1) * 108;
    const z = (rand() * 2 - 1) * 108;
    let tooClose = false;
    for (const key of Object.keys(CITY_IDS)) {
      const p = cityPosition(Number(key));
      if (Math.hypot(p.x - x, p.z - z) < 17) {
        tooClose = true;
        break;
      }
    }
    if (tooClose) {
      continue;
    }
    const y = terrainHeight(x, z) * flattenFactor(x, z);
    const s = 0.7 + rand() * 0.8;
    dummy.position.set(x, y + 0.5 * s, z);
    dummy.scale.setScalar(s);
    dummy.rotation.y = rand() * Math.PI;
    dummy.updateMatrix();
    trunkMesh.setMatrixAt(placed, dummy.matrix);
    dummy.position.set(x, y + 2.1 * s, z);
    dummy.updateMatrix();
    crownMesh.setMatrixAt(placed, dummy.matrix);
    placed++;
  }
  trunkMesh.instanceMatrix.needsUpdate = true;
  crownMesh.instanceMatrix.needsUpdate = true;
  group.add(trunkMesh, crownMesh);

  return {
    group,
    dispose: (): void => {
      geo.dispose();
      (mesh.material as THREE.Material).dispose();
      trunkGeo.dispose();
      crownGeo.dispose();
      (trunkMesh.material as THREE.Material).dispose();
      (crownMesh.material as THREE.Material).dispose();
      group.clear();
    },
  };
}
