/**
 * 程序化低多边形模型生成
 *
 * 不依赖任何外部美术资源。全部用 Three.js 基础几何体拼装，
 * 三角形面数控制在 LOD0 每单位 300~500 面以内。
 *
 * 造型要点（对应文档 §3 的兵种定位）：
 * - 步兵：持盾，方肩，最矮最宽 —— 一眼看出是防线
 * - 弓兵：持弓，瘦高，无肩甲 —— 一眼看出是远程
 * - 骑兵：高头大马，披风 —— 体型明显大于步弓
 */

import * as THREE from 'three';
import type { ArmyClass } from '../model/combat';

export const CLASS_COLOR: Record<ArmyClass, number> = {
  infantry: 0x8b5a3c,
  archer: 0x4a7c59,
  cavalry: 0x6b4a8a,
};

/** 统计三角形面数，用于验证 LOD 预算 */
export function countTriangles(mesh: THREE.Object3D): number {
  let tris = 0;
  mesh.traverse((obj) => {
    const m = obj as THREE.Mesh;
    if (!m.isMesh || !m.geometry) return;
    const g = m.geometry as THREE.BufferGeometry;
    const idx = g.getIndex();
    if (idx) tris += idx.count / 3;
    else if (g.getAttribute('position')) tris += g.getAttribute('position').count / 3;
  });
  return Math.trunc(tris);
}

function flatMat(color: number): THREE.MeshLambertMaterial {
  return new THREE.MeshLambertMaterial({ color, flatShading: true });
}

/** 士兵模型（朝向 +Z），高约 1.8 单位（米） */
export function makeSoldier(cls: ArmyClass, side: 'blue' | 'red'): THREE.Group {
  const g = new THREE.Group();
  const body = CLASS_COLOR[cls];
  const skin = 0xc9a882;
  const metal = 0x9aa0a6;
  const leather = 0x4a3428;
  const cloth = side === 'blue' ? 0x2a4d8f : 0x8f2a2a;

  const matBody = flatMat(body);
  const matSkin = flatMat(skin);
  const matMetal = flatMat(metal);
  const matLeather = flatMat(leather);
  const matCloth = flatMat(cloth);
  const matDark = flatMat(0x2e2a26);

  // ---- 躯干：甲片躯干 + 胸甲 + 腰带 ----
  const torso = new THREE.Mesh(new THREE.CylinderGeometry(0.22, 0.26, 0.62, 8), matBody);
  torso.position.y = 1.15;
  g.add(torso);

  const chest = new THREE.Mesh(new THREE.SphereGeometry(0.24, 8, 5, 0, Math.PI * 2, 0, Math.PI / 2), matMetal);
  chest.position.y = 1.32;
  chest.scale.set(1, 0.7, 0.85);
  g.add(chest);

  const belt = new THREE.Mesh(new THREE.CylinderGeometry(0.27, 0.27, 0.1, 8), matLeather);
  belt.position.y = 0.88;
  g.add(belt);

  const buckle = new THREE.Mesh(new THREE.BoxGeometry(0.1, 0.08, 0.04), matMetal);
  buckle.position.set(0, 0.88, 0.26);
  g.add(buckle);

  // 背甲横带
  for (const y of [1.2, 1.05]) {
    const strap = new THREE.Mesh(new THREE.BoxGeometry(0.46, 0.06, 0.04), matDark);
    strap.position.set(0, y, -0.2);
    g.add(strap);
  }

  // ---- 头颈 ----
  const neck = new THREE.Mesh(new THREE.CylinderGeometry(0.08, 0.1, 0.1, 6), matSkin);
  neck.position.y = 1.46;
  g.add(neck);

  const head = new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.24, 0.22), matSkin);
  head.position.y = 1.58;
  g.add(head);

  const helm = new THREE.Mesh(new THREE.ConeGeometry(0.17, 0.22, 8), matMetal);
  helm.position.y = 1.78;
  g.add(helm);

  const helmRim = new THREE.Mesh(new THREE.CylinderGeometry(0.19, 0.19, 0.05, 8), matMetal);
  helmRim.position.y = 1.69;
  g.add(helmRim);

  const noseGuard = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.16, 0.03), matMetal);
  noseGuard.position.set(0, 1.62, 0.12);
  g.add(noseGuard);

  // ---- 手臂（两段） ----
  for (const side2 of [-1, 1]) {
    const upper = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.06, 0.3, 6), matBody);
    upper.position.set(side2 * 0.28, 1.22, 0);
    upper.rotation.z = side2 * 0.12;
    g.add(upper);

    const fore = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.05, 0.28, 6), matSkin);
    fore.position.set(side2 * 0.34, 0.96, 0.02);
    fore.rotation.z = side2 * 0.12;
    g.add(fore);
  }

  // ---- 腿（大腿 + 小腿 + 靴） ----
  for (const side2 of [-1, 1]) {
    const thigh = new THREE.Mesh(new THREE.CylinderGeometry(0.09, 0.08, 0.4, 6), matBody);
    thigh.position.set(side2 * 0.11, 0.6, 0);
    g.add(thigh);

    const shin = new THREE.Mesh(new THREE.CylinderGeometry(0.08, 0.07, 0.34, 6), matLeather);
    shin.position.set(side2 * 0.11, 0.24, 0);
    g.add(shin);

    const boot = new THREE.Mesh(new THREE.BoxGeometry(0.14, 0.1, 0.22), matDark);
    boot.position.set(side2 * 0.11, 0.05, 0.04);
    g.add(boot);
  }

  if (cls === 'infantry') {
    // 方肩甲（左右各一）+ 胸甲 + 盾 + 长枪
    for (const side2 of [-1, 1]) {
      const pauldron = new THREE.Mesh(new THREE.BoxGeometry(0.26, 0.16, 0.3), matMetal);
      pauldron.position.set(side2 * 0.31, 1.42, 0);
      pauldron.rotation.z = side2 * 0.2;
      g.add(pauldron);

      const stud = new THREE.Mesh(new THREE.SphereGeometry(0.05, 6, 4), matMetal);
      stud.position.set(side2 * 0.4, 1.44, 0);
      g.add(stud);
    }

    const shield = new THREE.Mesh(new THREE.BoxGeometry(0.52, 0.74, 0.08), matBody);
    shield.position.set(0.34, 1.1, 0.16);
    shield.rotation.y = -0.3;
    g.add(shield);

    const shieldBoss = new THREE.Mesh(new THREE.SphereGeometry(0.09, 6, 4), matMetal);
    shieldBoss.position.set(0.35, 1.1, 0.22);
    g.add(shieldBoss);

    const shieldRimTop = new THREE.Mesh(new THREE.BoxGeometry(0.56, 0.06, 0.1), matMetal);
    shieldRimTop.position.set(0.34, 1.46, 0.16);
    shieldRimTop.rotation.y = -0.3;
    g.add(shieldRimTop);

    const spear = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 2.4, 5), matLeather);
    spear.position.set(-0.3, 1.2, 0.1);
    spear.rotation.x = 0.15;
    g.add(spear);

    const spearTip = new THREE.Mesh(new THREE.ConeGeometry(0.06, 0.24, 5), matMetal);
    spearTip.position.set(-0.36, 2.42, 0.27);
    g.add(spearTip);
  } else if (cls === 'archer') {
    // 瘦肩 + 弓（含弓臂与弓弦）+ 箭袋 + 箭
    for (const side2 of [-1, 1]) {
      const cap = new THREE.Mesh(new THREE.SphereGeometry(0.11, 6, 4), matCloth);
      cap.position.set(side2 * 0.28, 1.4, 0);
      cap.scale.set(1, 0.6, 1);
      g.add(cap);
    }

    const bow = new THREE.Mesh(
      new THREE.TorusGeometry(0.44, 0.035, 4, 10, Math.PI),
      matLeather,
    );
    bow.position.set(0.34, 1.15, 0);
    bow.rotation.set(0, Math.PI / 2, Math.PI / 2);
    g.add(bow);

    const bowString = new THREE.Mesh(new THREE.CylinderGeometry(0.008, 0.008, 0.86, 3), matDark);
    bowString.position.set(0.34, 1.15, 0);
    g.add(bowString);

    const quiver = new THREE.Mesh(new THREE.CylinderGeometry(0.09, 0.09, 0.5, 6), matLeather);
    quiver.position.set(-0.22, 1.2, -0.16);
    quiver.rotation.x = 0.5;
    g.add(quiver);

    for (let i = 0; i < 3; i++) {
      const arrow = new THREE.Mesh(new THREE.CylinderGeometry(0.012, 0.012, 0.5, 3), matDark);
      arrow.position.set(-0.22 + (i - 1) * 0.03, 1.44, -0.28);
      arrow.rotation.x = 0.5;
      g.add(arrow);
    }
  } else {
    // 披风 + 长矛 + 高马
    const cape = new THREE.Mesh(new THREE.ConeGeometry(0.36, 0.9, 6), matCloth);
    cape.position.set(0, 1.05, -0.22);
    cape.rotation.x = -0.18;
    g.add(cape);

    const lance = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 2.6, 5), matLeather);
    lance.position.set(0.36, 1.24, 0.1);
    lance.rotation.x = 0.2;
    g.add(lance);

    const lanceTip = new THREE.Mesh(new THREE.ConeGeometry(0.055, 0.22, 5), matMetal);
    lanceTip.position.set(0.47, 2.54, 0.36);
    g.add(lanceTip);

    // 马
    const horseMat = flatMat(0x6b4a35);
    const horseBody = new THREE.Mesh(new THREE.BoxGeometry(0.62, 0.52, 1.5), horseMat);
    horseBody.position.set(0, 0.66, -0.1);
    g.add(horseBody);

    const horseChest = new THREE.Mesh(new THREE.BoxGeometry(0.56, 0.46, 0.3), horseMat);
    horseChest.position.set(0, 0.72, 0.55);
    g.add(horseChest);

    const neck = new THREE.Mesh(new THREE.BoxGeometry(0.32, 0.62, 0.32), horseMat);
    neck.position.set(0, 1.05, 0.86);
    neck.rotation.x = -0.35;
    g.add(neck);

    const head = new THREE.Mesh(new THREE.BoxGeometry(0.24, 0.3, 0.5), horseMat);
    head.position.set(0, 1.38, 1.12);
    head.rotation.x = 0.25;
    g.add(head);

    for (const sx of [-1, 1]) {
      const ear = new THREE.Mesh(new THREE.ConeGeometry(0.05, 0.14, 4), horseMat);
      ear.position.set(sx * 0.09, 1.55, 1.06);
      g.add(ear);
    }

    // 四条腿
    for (const [hx, hz] of [
      [-0.22, -0.6],
      [0.22, -0.6],
      [-0.22, 0.5],
      [0.22, 0.5],
    ] as [number, number][]) {
      const leg = new THREE.Mesh(new THREE.BoxGeometry(0.13, 0.62, 0.13), horseMat);
      leg.position.set(hx, 0.35, hz);
      g.add(leg);

      const hoof = new THREE.Mesh(new THREE.BoxGeometry(0.14, 0.1, 0.14), matDark);
      hoof.position.set(hx, 0.06, hz);
      g.add(hoof);
    }

    const tail = new THREE.Mesh(new THREE.ConeGeometry(0.08, 0.5, 4), matLeather);
    tail.position.set(0, 0.72, -0.88);
    tail.rotation.x = -0.9;
    g.add(tail);

    const saddle = new THREE.Mesh(new THREE.BoxGeometry(0.56, 0.12, 0.44), matLeather);
    saddle.position.set(0, 0.96, -0.05);
    g.add(saddle);

    g.scale.setScalar(1.15);
  }

  return g;
}

/**
 * LOD1 模型：简化人形（约 50~80 面）
 * 去掉细节，保留轮廓与兵种色
 */
export function makeSoldierLOD1(cls: ArmyClass): THREE.BufferGeometry {
  const parts: THREE.BufferGeometry[] = [];
  const push = (geo: THREE.BufferGeometry, y: number, s = 1) => {
    geo.scale(s, s, s);
    geo.translate(0, y, 0);
    parts.push(geo);
  };
  // 合并为两段：躯干（含头）+ 腿块，追求面数最小
  push(new THREE.CylinderGeometry(0.24, 0.3, 1.5, 5), 1.05); // 躯干
  push(new THREE.BoxGeometry(0.24, 0.24, 0.24), 1.72); // 头
  push(new THREE.BoxGeometry(0.32, 0.72, 0.38), 0.36); // 腿块
  if (cls === 'archer') {
    push(new THREE.TorusGeometry(0.4, 0.05, 3, 5, Math.PI), 1.1);
  } else if (cls === 'infantry') {
    push(new THREE.BoxGeometry(0.5, 0.7, 0.1), 1.1);
  } else {
    push(new THREE.BoxGeometry(0.62, 0.5, 1.5), 0.6);
  }
  return mergeGeometries(parts);
}

/** LOD2 模型：立方体（12 面） */
export function makeSoldierLOD2(): THREE.BufferGeometry {
  return new THREE.BoxGeometry(0.7, 1.7, 0.7);
}

/** 合并多个几何体（同属性布局） */
export function mergeGeometries(list: THREE.BufferGeometry[]): THREE.BufferGeometry {
  const positions: number[] = [];
  const normals: number[] = [];
  for (const g of list) {
    const gg = g.index ? g.toNonIndexed() : g;
    const pos = gg.getAttribute('position');
    const nor = gg.getAttribute('normal');
    for (let i = 0; i < pos.count; i++) {
      positions.push(pos.getX(i), pos.getY(i), pos.getZ(i));
    }
    if (nor) {
      for (let i = 0; i < nor.count; i++) {
        normals.push(nor.getX(i), nor.getY(i), nor.getZ(i));
      }
    }
    if (gg !== g) gg.dispose();
  }
  const out = new THREE.BufferGeometry();
  out.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  if (normals.length === positions.length) {
    out.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3));
  } else {
    out.computeVertexNormals();
  }
  return out;
}

/** 树木（低多边形） */
export function makeTree(): THREE.Group {
  const g = new THREE.Group();
  const trunk = new THREE.Mesh(
    new THREE.CylinderGeometry(0.22, 0.3, 2.4, 5),
    flatMat(0x5a4030),
  );
  trunk.position.y = 1.2;
  g.add(trunk);
  const foliage = new THREE.Mesh(new THREE.ConeGeometry(1.5, 3.4, 6), flatMat(0x2f5a2a));
  foliage.position.y = 3.8;
  g.add(foliage);
  return g;
}

/** 岩石（低多边形） */
export function makeRock(): THREE.Mesh {
  const geo = new THREE.DodecahedronGeometry(1.2, 0);
  const pos = geo.getAttribute('position');
  for (let i = 0; i < pos.count; i++) {
    pos.setXYZ(
      i,
      pos.getX(i) * (0.75 + Math.abs(Math.sin(i * 12.9898)) * 0.5),
      pos.getY(i) * (0.6 + Math.abs(Math.cos(i * 78.233)) * 0.5),
      pos.getZ(i) * (0.75 + Math.abs(Math.sin(i * 39.425)) * 0.5),
    );
  }
  geo.computeVertexNormals();
  return new THREE.Mesh(geo, flatMat(0x7a7268));
}

/** 营帐（经营层据点） */
export function makeTent(): THREE.Group {
  const g = new THREE.Group();
  const tent = new THREE.Mesh(new THREE.ConeGeometry(1.8, 2.6, 4), flatMat(0xa89a7c));
  tent.position.y = 1.3;
  tent.rotation.y = Math.PI / 4;
  g.add(tent);
  return g;
}

/** 军旗 */
export function makeBanner(side: 'blue' | 'red'): THREE.Group {
  const g = new THREE.Group();
  const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 3.2, 5), flatMat(0x6b5a3c));
  pole.position.y = 1.6;
  g.add(pole);
  const flag = new THREE.Mesh(
    new THREE.PlaneGeometry(1.1, 0.7),
    new THREE.MeshLambertMaterial({
      color: side === 'blue' ? 0x2a4d8f : 0x8f2a2a,
      side: THREE.DoubleSide,
      flatShading: true,
    }),
  );
  flag.position.set(0.58, 2.7, 0);
  g.add(flag);
  return g;
}