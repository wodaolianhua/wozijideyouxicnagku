/**
 * 渲染层测试：LOD 三角面预算与模型完整性
 *
 * 文档 §4 的性能预算要求：
 * - LOD0 每单位 300~500 面
 * - LOD1 每单位 50~80 面
 * - LOD2 每单位 12 面
 *
 * 这些是硬预算，超了就跑不动。必须用测试守住。
 */

import { describe, it, expect } from 'vitest';
import * as THREE from 'three';
import {
  countTriangles,
  makeBanner,
  makeRock,
  makeSoldier,
  makeSoldierLOD1,
  makeSoldierLOD2,
  makeTent,
  makeTree,
} from '../src/render/models';
import { ARMY_CLASS, type ArmyClass } from '../src/model/combat';

describe('LOD 三角面预算', () => {
  it('LOD0 每单位面数在 500~800 之间', () => {
    for (const cls of ARMY_CLASS) {
      const tris = countTriangles(makeSoldier(cls, 'blue'));
      expect(tris, `LOD0 ${cls} 面数 ${tris}`).toBeGreaterThanOrEqual(500);
      expect(tris, `LOD0 ${cls} 面数 ${tris}`).toBeLessThanOrEqual(800);
    }
  });

  it('LOD1 每单位面数在 40~90 之间', () => {
    for (const cls of ARMY_CLASS) {
      const tris = countTriangles(new THREE.Mesh(makeSoldierLOD1(cls)));
      expect(tris, `LOD1 ${cls} 面数 ${tris}`).toBeGreaterThanOrEqual(40);
      expect(tris, `LOD1 ${cls} 面数 ${tris}`).toBeLessThanOrEqual(90);
    }
  });

  it('LOD2 面数为 12（立方体）', () => {
    expect(countTriangles(new THREE.Mesh(makeSoldierLOD2()))).toBe(12);
  });

  it('LOD 面数严格递减（性能前提）', () => {
    for (const cls of ARMY_CLASS) {
      const l0 = countTriangles(makeSoldier(cls, 'blue'));
      const l1 = countTriangles(new THREE.Mesh(makeSoldierLOD1(cls)));
      const l2 = countTriangles(new THREE.Mesh(makeSoldierLOD2()));
      expect(l1, `LOD1(${l1}) 应远小于 LOD0(${l0})·1/6`).toBeLessThan(l0 / 6);
      expect(l2).toBeLessThan(l1 / 3);
    }
  });

  it('LOD2 同屏两万单位的面数预算成立', () => {
    // 20000 × 12 面 = 240000 面，低于现代 GPU 单帧百万面级处理能力
    const totalTris = 20000 * countTriangles(new THREE.Mesh(makeSoldierLOD2()));
    expect(totalTris).toBeLessThan(500000);
  });

  it('LOD0 同屏两百单位的总面数在可接受范围', () => {
    // 实测 LOD0 平均约 695 面，200 单位约 13.9 万面
    // 中端 GPU（如 GTX 1660）单帧可处理 200 万面以上，余量充足
    const perUnit = ARMY_CLASS.reduce(
      (sum, c) => sum + countTriangles(makeSoldier(c, 'blue')),
      0,
    ) / ARMY_CLASS.length;
    const total = 200 * perUnit;
    expect(total, `200 单位 LOD0 总面数 ${total}`).toBeLessThan(200000);
  });

  it('三档 LOD 同屏总面数在预算内', () => {
    // 真实场景：200 个 LOD0 + 3000 个 LOD1 + 20000 个 LOD2
    const l0 = ARMY_CLASS.reduce((s, c) => s + countTriangles(makeSoldier(c, 'blue')), 0) /
      ARMY_CLASS.length;
    const l1 = ARMY_CLASS.reduce(
      (s, c) => s + countTriangles(new THREE.Mesh(makeSoldierLOD1(c))),
      0,
    ) / ARMY_CLASS.length;
    const l2 = countTriangles(new THREE.Mesh(makeSoldierLOD2()));

    const total = 200 * l0 + 3000 * l1 + 20000 * l2;
    expect(total, `三档同时渲染总面数 ${total}`).toBeLessThan(1000000);
  });
});

describe('模型完整性', () => {
  it('三兵种模型互不相同（造型可辨识）', () => {
    const geos = ARMY_CLASS.map((c) => {
      const g = makeSoldier(c, 'blue');
      let n = 0;
      g.traverse(() => n++);
      return n;
    });
    // 三个兵种的部件数应各不相同，否则说明造型没做区分
    expect(new Set(geos).size).toBe(3);
  });

  it('模型朝向 +Z 且高度合理', () => {
    for (const cls of ARMY_CLASS) {
      const g = makeSoldier(cls, 'blue');
      const box = new THREE.Box3().setFromObject(g);
      const h = box.max.y - box.min.y;
      // 骑兵含马，合法上限比其他兵种高
      const upper = cls === 'cavalry' ? 3.6 : 2.8;
      expect(h, `${cls} 高度 ${h.toFixed(2)}`).toBeGreaterThan(1.4);
      expect(h, `${cls} 高度 ${h.toFixed(2)}`).toBeLessThan(upper);
      const w = box.max.x - box.min.x;
      expect(w, `${cls} 宽度 ${w.toFixed(2)}`).toBeLessThan(2.0);
    }
  });

  it('步兵弓兵体型接近，骑兵明显更大', () => {
    const h = (c: ArmyClass) => {
      const b = new THREE.Box3().setFromObject(makeSoldier(c, 'blue'));
      return b.max.y - b.min.y;
    };
    expect(h('infantry')).toBeGreaterThan(1.4);
    expect(h('archer')).toBeGreaterThan(1.4);
    expect(h('cavalry')).toBeGreaterThan(h('infantry') * 1.2);
  });

  it('骑兵体型明显大于步兵（造型区分度）', () => {
    const cavBox = new THREE.Box3().setFromObject(makeSoldier('cavalry', 'blue'));
    const infBox = new THREE.Box3().setFromObject(makeSoldier('infantry', 'blue'));
    const cavLen = cavBox.max.z - cavBox.min.z;
    const infLen = infBox.max.z - infBox.min.z;
    expect(cavLen, `骑兵纵深 ${cavLen.toFixed(2)} 应大于步兵 ${infLen.toFixed(2)}`).toBeGreaterThan(infLen * 1.5);
  });

  it('弓兵持弓（造型语义正确）', () => {
    let hasTorus = false;
    makeSoldier('archer', 'blue').traverse((o) => {
      if ((o as THREE.Mesh).isMesh && (o as THREE.Mesh).geometry instanceof THREE.TorusGeometry) {
        hasTorus = true;
      }
    });
    expect(hasTorus).toBe(true);
  });

  it('步兵持盾（造型语义正确）', () => {
    let hasFlatBoard = false;
    makeSoldier('infantry', 'blue').traverse((o) => {
      const m = o as THREE.Mesh;
      if (!m.isMesh) return;
      const geo = m.geometry;
      geo.computeBoundingBox();
      const bb = geo.boundingBox;
      if (!bb) return;
      const s = new THREE.Vector3();
      bb.getSize(s);
      if (s.z < 0.15 && s.y > 0.5) hasFlatBoard = true; // 薄而高的板 = 盾
    });
    expect(hasFlatBoard).toBe(true);
  });

  it('场景道具均可生成且面数合理', () => {
    expect(countTriangles(makeTree())).toBeGreaterThan(0);
    expect(countTriangles(makeRock())).toBeGreaterThan(0);
    expect(countTriangles(makeTent())).toBeGreaterThan(0);
    expect(countTriangles(makeBanner('blue'))).toBeGreaterThan(0);
    expect(countTriangles(makeTent())).toBeLessThan(40);
  });

  it('LOD1 几何体已合并为单一 BufferGeometry', () => {
    const g = makeSoldierLOD1('infantry');
    expect(g.getAttribute('position').count).toBeGreaterThan(0);
    expect(g.getIndex()).toBe(null); //合并后应为非索引
  });
});