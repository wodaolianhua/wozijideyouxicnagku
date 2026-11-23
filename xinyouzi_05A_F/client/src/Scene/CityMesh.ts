// ============================================================================
//  Scene/CityMesh.ts —— 城池 mesh（资产槽 city_castle.glb，缺文件 → 程序化占位）。
//  城名 / 归属色 / 人口 / 城防 / 首都标识一律来自 state（业务数据禁止硬编码）。
// ============================================================================

import * as THREE from 'three';
import type { City, WorldState } from '../Core/Types.js';
import { factionColor, factionColorDim } from '../Ui/Theme.js';
import type { AssetSlots } from './WorldScene.js';

export interface CityMeshHandles {
  group: THREE.Group;
  /** 归属色部件（每帧状态刷新时重着色） */
  update(city: Readonly<City>): void;
  setSelected(selected: boolean): void;
  setLabelVisible(visible: boolean): void;
  dispose(): void;
}

function makeLabelTexture(city: Readonly<City>): THREE.CanvasTexture {
  const canvas = document.createElement('canvas');
  canvas.width = 256;
  canvas.height = 80;
  const ctx = canvas.getContext('2d');
  if (ctx) {
    ctx.clearRect(0, 0, 256, 80);
    ctx.fillStyle = 'rgba(11, 14, 20, 0.78)';
    ctx.roundRect?.(6, 8, 244, 62, 12);
    ctx.fill();
    ctx.fillStyle = factionColor(city.ownerFactionId);
    ctx.fillRect(6, 8, 8, 62);
    ctx.fillStyle = '#e8e4d8';
    ctx.font = '700 30px "Microsoft YaHei", sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText(`${city.isCapital ? '★' : ''}${city.name}`, 134, 48);
  }
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

export async function createCityMesh(city: Readonly<City>, slots: AssetSlots): Promise<CityMeshHandles> {
  const group = new THREE.Group();
  group.userData.cityId = city.id;

  // ---- 资产槽：city_castle.glb（缺文件 → 程序化占位体） ----
  const gltf = await slots.get('city_castle.glb');
  let bodyRoot: THREE.Object3D;
  let flagParts: THREE.Mesh[] = [];

  if (gltf) {
    bodyRoot = gltf;
    bodyRoot.traverse((o) => {
      if ((o as THREE.Mesh).isMesh) {
        flagParts.push(o as THREE.Mesh);
      }
    });
  } else {
    bodyRoot = new THREE.Group();

    // 台基
    const base = new THREE.Mesh(
      new THREE.CylinderGeometry(11, 12.4, 1.4, 8),
      new THREE.MeshLambertMaterial({ color: '#7a7f72' }),
    );
    base.position.y = 0.7;
    bodyRoot.add(base);

    // 城墙
    const wallMat = new THREE.MeshLambertMaterial({ color: '#9a958a' });
    const wall = new THREE.Mesh(new THREE.CylinderGeometry(8.6, 8.9, 3.2, 8, 1, true), wallMat);
    wall.position.y = 3.0;
    bodyRoot.add(wall);

    // 主楼
    const keep = new THREE.Mesh(new THREE.BoxGeometry(5.4, 6.4, 5.4), wallMat);
    keep.position.y = 6.4;
    bodyRoot.add(keep);

    // 主楼屋顶
    const roof = new THREE.Mesh(
      new THREE.ConeGeometry(4.6, 3.0, 4),
      new THREE.MeshLambertMaterial({ color: '#5a4a3a' }),
    );
    roof.position.y = 11.1;
    roof.rotation.y = Math.PI / 4;
    bodyRoot.add(roof);

    // 归属旗（旗面颜色随势力刷新）
    const pole = new THREE.Mesh(
      new THREE.CylinderGeometry(0.12, 0.12, 7, 5),
      new THREE.MeshLambertMaterial({ color: '#4a4034' }),
    );
    pole.position.set(9.4, 6.5, 0);
    bodyRoot.add(pole);
    const flag = new THREE.Mesh(
      new THREE.PlaneGeometry(2.8, 1.8),
      new THREE.MeshBasicMaterial({ color: factionColor(city.ownerFactionId), side: THREE.DoubleSide }),
    );
    flag.position.set(10.9, 9.0, 0);
    bodyRoot.add(flag);
    flagParts = [flag, base, roof];
  }

  group.add(bodyRoot);

  // 选中高亮环
  const ring = new THREE.Mesh(
    new THREE.RingGeometry(12.6, 13.8, 40),
    new THREE.MeshBasicMaterial({ color: 0xc9a44c, transparent: true, opacity: 0.9, side: THREE.DoubleSide }),
  );
  ring.rotation.x = -Math.PI / 2;
  ring.position.y = 0.25;
  ring.visible = false;
  group.add(ring);

  // 城名标签（Sprite）
  let labelTex = makeLabelTexture(city);
  const label = new THREE.Sprite(new THREE.SpriteMaterial({ map: labelTex, depthTest: false }));
  label.scale.set(14, 4.4, 1);
  label.position.y = 16.5;
  label.renderOrder = 10;
  group.add(label);

  const scaleForPopulation = (pop: number, maxPop: number): number => 0.92 + 0.36 * (maxPop > 0 ? pop / maxPop : 0);

  const applyScale = (cityNow: Readonly<City>): void => {
    const s = scaleForPopulation(cityNow.population, cityNow.maxPopulation);
    bodyRoot.scale.setScalar(s);
  };

  applyScale(city);

  return {
    group,
    update(cityNow: Readonly<City>): void {
      // 归属色刷新
      const color = factionColor(cityNow.ownerFactionId);
      for (const part of flagParts) {
        const mat = part.material as THREE.MeshBasicMaterial | THREE.MeshLambertMaterial;
        if ('color' in mat) {
          if ((mat as THREE.MeshBasicMaterial).isMeshBasicMaterial) {
            mat.color.set(color);
          } else {
            mat.color.set(color).lerp(new THREE.Color('#ffffff'), 0.0);
          }
        }
      }
      // 城防 → 城墙视觉（略）
      if (cityNow.name !== city.name || cityNow.isCapital !== city.isCapital || cityNow.ownerFactionId !== city.ownerFactionId) {
        // 城名 / 首都 / 归属变化 → 重绘标签
        labelTex.dispose();
        labelTex = makeLabelTexture(cityNow);
        (label.material as THREE.SpriteMaterial).map = labelTex;
        (label.material as THREE.SpriteMaterial).needsUpdate = true;
        city = cityNow;
      }
      applyScale(cityNow);
    },
    setSelected(selected: boolean): void {
      ring.visible = selected;
    },
    setLabelVisible(visible: boolean): void {
      label.visible = visible;
    },
    dispose(): void {
      labelTex.dispose();
      (label.material as THREE.SpriteMaterial).dispose();
      ring.geometry.dispose();
      (ring.material as THREE.Material).dispose();
      group.traverse((o) => {
        const m = o as THREE.Mesh;
        if (m.isMesh && m.geometry) {
          m.geometry.dispose();
        }
      });
      group.clear();
    },
  };
}

/** 悬停信息卡文本（全部来自 state） */
export function cityHoverText(city: Readonly<City>, state: Readonly<WorldState>): string {
  const owner = state.factions.find((f) => f.id === city.ownerFactionId);
  const armies = state.armies.filter((a) => a.positionCityId === city.id);
  const lines = [
    `<b>${city.isCapital ? '★' : ''}${city.name}</b>（${owner ? owner.name : '无主'}）`,
    `人口 ${city.population}/${city.maxPopulation} · 城防 ${city.defense}/${city.maxDefense}（等级 ${city.fortifyLevel}）`,
    `屯田等级 ${city.farmLevel} · 相邻 ${city.adjacentCityIds.map((id) => state.cities.find((c) => c.id === id)?.name ?? id).join('、')}`,
  ];
  for (const a of armies) {
    lines.push(`驻军 #${a.id} ${a.soldiers} 人 · 士气 ${a.morale}`);
  }
  return lines.join('<br/>');
}

export { factionColorDim };
