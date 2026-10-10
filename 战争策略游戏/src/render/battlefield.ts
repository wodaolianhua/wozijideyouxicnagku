/**
 * 战场场景 —— 地形网格 + 天空 + 灯光 + 装饰物
 */

import * as THREE from 'three';
import {
  GRID,
  MAP_SIZE,
  Rng,
  TERRAIN_COLOR,
  TERRAIN_KEYS,
  generateTerrain,
  type TerrainData,
  type TerrainKey,
} from './terrain';
import { makeRock, makeTent, makeTree } from './models';

export class Battlefield {
  readonly scene = new THREE.Scene();
  readonly terrain: TerrainData;
  readonly ground: THREE.Mesh;
  private decorations: THREE.Group;

  constructor(seed = 20261005) {
    this.terrain = generateTerrain(seed);

    this.scene.background = new THREE.Color(0x8fa8bf);
    this.scene.fog = new THREE.Fog(0x8fa8bf, 900, 2400);

    // 光照
    const sun = new THREE.DirectionalLight(0xfff2d8, 1.5);
    sun.position.set(600, 900, 400);
    this.scene.add(sun);
    this.scene.add(new THREE.HemisphereLight(0xbcd4e8, 0x50503a, 0.85));
    this.scene.add(new THREE.AmbientLight(0xffffff, 0.18));

    // 地形网格：顶点色区分地形类型
    const geo = new THREE.PlaneGeometry(MAP_SIZE, MAP_SIZE, GRID - 1, GRID - 1);
    geo.rotateX(-Math.PI / 2);
    const pos = geo.getAttribute('position');
    const colors = new Float32Array(pos.count * 3);
    const c = new THREE.Color();
    for (let i = 0; i < pos.count; i++) {
      const x = pos.getX(i);
      const z = pos.getZ(i);
      pos.setY(i, this.terrain.heightAt(x, z));
      const key = this.terrain.keyAt(x, z);
      c.setHex(TERRAIN_COLOR[key]);
      // 加一点高度明暗，让地形起伏可读
      const shade = 0.82 + (this.terrain.heightAt(x, z) + 12) / 180 * 0.36;
      colors[i * 3] = c.r * shade;
      colors[i * 3 + 1] = c.g * shade;
      colors[i * 3 + 2] = c.b * shade;
    }
    geo.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    geo.computeVertexNormals();

    const mat = new THREE.MeshLambertMaterial({ vertexColors: true, flatShading: true });
    this.ground = new THREE.Mesh(geo, mat);
    this.ground.receiveShadow = true;
    this.scene.add(this.ground);

    this.decorations = new THREE.Group();
    this.scene.add(this.decorations);
    this.scatterDecorations(seed + 1);

    // 战场边界框（提示地图范围）
    const edges = new THREE.LineSegments(
      new THREE.EdgesGeometry(new THREE.BoxGeometry(MAP_SIZE, 1, MAP_SIZE)),
      new THREE.LineBasicMaterial({ color: 0x5a6472 }),
    );
    edges.position.y = 6;
    this.scene.add(edges);
  }

  private scatterDecorations(seed: number) {
    const rng = new Rng(seed);
    const counts: Record<TerrainKey, number> = {
      plain: 0,
      forest: 0,
      hill: 0,
      river: 0,
      fort: 0,
    };
    // 树：林地多，山地少
    for (let i = 0; i < 420; i++) {
      const x = rng.range(-MAP_SIZE / 2 + 30, MAP_SIZE / 2 - 30);
      const z = rng.range(-MAP_SIZE / 2 + 30, MAP_SIZE / 2 - 30);
      const key = this.terrain.keyAt(x, z);
      if (key === 'forest' || (key === 'hill' && rng.next() < 0.25)) {
        if (counts.forest >= 300) continue;
        const t = makeTree();
        t.position.set(x, this.terrain.heightAt(x, z), z);
        t.rotation.y = rng.range(0, Math.PI * 2);
        t.scale.setScalar(rng.range(0.8, 1.4));
        this.decorations.add(t);
        counts.forest++;
      }
    }
    // 岩石：山地为主
    for (let i = 0; i < 220; i++) {
      const x = rng.range(-MAP_SIZE / 2 + 30, MAP_SIZE / 2 - 30);
      const z = rng.range(-MAP_SIZE / 2 + 30, MAP_SIZE / 2 - 30);
      const key = this.terrain.keyAt(x, z);
      if (key === 'hill' || (key === 'plain' && rng.next() < 0.12)) {
        const r = makeRock();
        r.position.set(x, this.terrain.heightAt(x, z) - 0.4, z);
        r.rotation.set(rng.range(0, 3), rng.range(0, 6), rng.range(0, 3));
        r.scale.setScalar(rng.range(0.6, 2.0));
        this.decorations.add(r);
        counts.hill++;
      }
    }
    // 营帐：平原上散落
    for (let i = 0; i < 14; i++) {
      const x = rng.range(-MAP_SIZE / 2 + 120, MAP_SIZE / 2 - 120);
      const z = rng.range(-MAP_SIZE / 2 + 120, MAP_SIZE / 2 - 120);
      if (this.terrain.keyAt(x, z) !== 'plain') continue;
      const tent = makeTent();
      tent.position.set(x, this.terrain.heightAt(x, z), z);
      tent.rotation.y = rng.range(0, Math.PI * 2);
      this.decorations.add(tent);
      counts.plain++;
    }
  }

  /** 地形类型统计（供 HUD 显示） */
  terrainStats(): Record<TerrainKey, number> {
    const out: Record<TerrainKey, number> = {
      plain: 0, forest: 0, hill: 0, river: 0, fort: 0,
    };
    for (let i = 0; i < this.terrain.type.length; i++) {
      out[TERRAIN_KEYS[this.terrain.type[i]]]++;
    }
    return out;
  }

  dispose() {
    this.ground.geometry.dispose();
    (this.ground.material as THREE.Material).dispose();
    this.decorations.traverse((o) => {
      const m = o as THREE.Mesh;
      if (m.isMesh) {
        m.geometry.dispose();
        (m.material as THREE.Material)?.dispose();
      }
    });
  }
}