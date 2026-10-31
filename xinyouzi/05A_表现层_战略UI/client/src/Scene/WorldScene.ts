// ============================================================================
//  Scene/WorldScene.ts —— three.js 场景编排。
//  R3 上帝视角（轨道相机 + 6 城 + 邻接连线 + 部队）· R4 亲征视角（跟随相机 + 君主）。
//  资产槽机制在本文件定义：GLB 缺失/加载失败 → 程序化占位体，整局不崩溃。
// ============================================================================

import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import type { GameStateStore } from '../Core/GameStateStore.js';
import { factionColor } from '../Ui/Theme.js';
import { AdjacencyLines, MAP_BOUND, cityPosition } from './Adjacency.js';
import type { ArmyMeshHandles } from './ArmyMesh.js';
import { createArmyMesh } from './ArmyMesh.js';
import type { CityMeshHandles } from './CityMesh.js';
import { createCityMesh } from './CityMesh.js';
import type { LordHandles } from './LordMesh.js';
import { createLordMesh } from './LordMesh.js';
import type { TerrainHandles } from './Terrain.js';
import { createTerrain, terrainHeight } from './Terrain.js';
import type { Controls } from '../Player/Controls.js';

export type ViewMode = 'god' | 'field';

// ============================================================================
//  资产槽（asset slot）：assets/ 下按固定文件名加载；失败/缺失 → null → 占位体
// ============================================================================
export interface AssetSlots {
  get(file: string): Promise<THREE.Object3D | null>;
}

export function createAssetSlots(): AssetSlots {
  const cache = new Map<string, Promise<THREE.Object3D | null>>();
  const loader = new GLTFLoader();
  const get = (file: string): Promise<THREE.Object3D | null> => {
    let p = cache.get(file);
    if (!p) {
      p = new Promise((resolve) => {
        try {
          loader.load(
            `assets/${file}`,
            (gltf) => resolve(gltf.scene),
            undefined,
            () => resolve(null), // 404 / 解析失败 → 占位体
          );
        } catch {
          resolve(null);
        }
      });
      cache.set(file, p);
    }
    return p;
  };
  return { get };
}

// ============================================================================
export type HoverTarget = { kind: 'city'; id: number } | { kind: 'army'; id: number };

export interface WorldSceneCallbacks {
  onHover(target: HoverTarget | null): void;
  onSelectCity(cityId: number): void;
  onSelectArmy(armyId: number): void;
  onClickEmpty(): void;
}

interface CameraTween {
  fromPos: THREE.Vector3;
  toPos: THREE.Vector3;
  fromLook: THREE.Vector3;
  toLook: THREE.Vector3;
  elapsed: number;
  duration: number;
  resolve: () => void;
}

const GOD_CAMERA_POS = new THREE.Vector3(0, 128, 158);

export class WorldScene {
  private renderer!: THREE.WebGLRenderer;
  private scene!: THREE.Scene;
  private camera!: THREE.PerspectiveCamera;
  private orbit!: OrbitControls;
  private clock = new THREE.Clock();
  private container!: HTMLElement;
  private store!: GameStateStore;
  private callbacks!: WorldSceneCallbacks;
  private slots: AssetSlots = createAssetSlots();
  private resizeObserver: ResizeObserver | null = null;

  private terrain!: TerrainHandles;
  private adjacency = new AdjacencyLines();
  private cityGroup = new THREE.Group();
  private armyGroup = new THREE.Group();
  private fxGroup = new THREE.Group();

  private cityMeshes = new Map<number, CityMeshHandles>();
  private armyMeshes = new Map<number, ArmyMeshHandles>();
  private lord: LordHandles | null = null;

  mode: ViewMode = 'god';
  selectedCityId: number | null = null;
  selectedArmyId: number | null = null;
  hover: HoverTarget | null = null;

  // 亲征相机状态
  private camYaw = 0.6;
  private camPitch = 0.42;
  private camDist = 7.5;
  private lordPos = new THREE.Vector3();
  private lordYaw = 0;

  // 军议行军箭头
  private marchArrow: THREE.Group | null = null;

  // 相机飞行（战斗演出用）
  private tweens: CameraTween[] = [];

  private elapsed = 0;
  private running = false;
  private controls: Controls | null = null;

  async init(container: HTMLElement, store: GameStateStore, callbacks: WorldSceneCallbacks, controls: Controls): Promise<void> {
    this.container = container;
    this.store = store;
    this.callbacks = callbacks;

    this.renderer = new THREE.WebGLRenderer({ antialias: false, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.5)); // 核显性能红线
    this.renderer.setSize(container.clientWidth, container.clientHeight);
    this.renderer.shadowMap.enabled = false; // 不开阴影，保帧率
    container.appendChild(this.renderer.domElement);

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color('#0d1420');
    this.scene.fog = new THREE.Fog('#0d1420', 220, 420);

    this.camera = new THREE.PerspectiveCamera(55, container.clientWidth / container.clientHeight, 0.5, 900);
    this.camera.position.copy(GOD_CAMERA_POS);

    const hemi = new THREE.HemisphereLight(0xbfd4e8, 0x3a4636, 0.9);
    const sun = new THREE.DirectionalLight(0xfff2d8, 1.15);
    sun.position.set(80, 140, 60);
    this.scene.add(hemi, sun);

    this.scene.add(this.cityGroup, this.armyGroup, this.fxGroup);

    // 地形
    this.terrain = await createTerrain(this.slots);
    this.scene.add(this.terrain.group);
    this.scene.add(this.adjacency.group);

    // 城池
    const state = store.state;
    for (const city of state.cities) {
      const handles = await createCityMesh(city, this.slots);
      const pos = cityPosition(city.id);
      handles.group.position.copy(pos);
      this.cityMeshes.set(city.id, handles);
      this.cityGroup.add(handles.group);
    }

    // 部队
    for (const army of state.armies) {
      const handles = await createArmyMesh(army, this.slots);
      this.armyMeshes.set(army.id, handles);
      this.armyGroup.add(handles.group);
      handles.update(army, state);
    }

    // 君主（初始在玩家都城旁）
    const pf = store.playerFaction;
    const capital = pf ? cityPosition(pf.capitalCityId) : new THREE.Vector3();
    this.lordPos.set(capital.x + 6, 0, capital.z + 10);
    this.lord = await createLordMesh(factionColor(pf?.id ?? 0), this.slots);
    this.scene.add(this.lord.group);
    this.lord.group.position.copy(this.lordPos);
    this.lord.setVisible(false); // 上帝视角默认隐藏君主

    // 轨道相机（上帝视角）
    this.orbit = new OrbitControls(this.camera, this.renderer.domElement);
    this.orbit.target.set(0, 0, 0);
    this.orbit.enableDamping = true;
    this.orbit.dampingFactor = 0.08;
    this.orbit.minDistance = 34;
    this.orbit.maxDistance = 340;
    this.orbit.maxPolarAngle = 1.32;
    this.orbit.mouseButtons = {
      LEFT: THREE.MOUSE.ROTATE,
      MIDDLE: THREE.MOUSE.DOLLY,
      RIGHT: THREE.MOUSE.PAN,
    };

    // 拾取（悬停 / 点击）
    const dom = this.renderer.domElement;
    const raycaster = new THREE.Raycaster();
    const pointer = new THREE.Vector2();
    let downX = 0;
    let downY = 0;

    const pickAt = (clientX: number, clientY: number): HoverTarget | null => {
      const rect = dom.getBoundingClientRect();
      pointer.x = ((clientX - rect.left) / rect.width) * 2 - 1;
      pointer.y = -((clientY - rect.top) / rect.height) * 2 + 1;
      raycaster.setFromCamera(pointer, this.camera);
      const hits = raycaster.intersectObjects([this.cityGroup, this.armyGroup], true);
      for (const hit of hits) {
        let obj: THREE.Object3D | null = hit.object;
        while (obj) {
          if (obj.userData?.cityId !== undefined) {
            return { kind: 'city', id: obj.userData.cityId as number };
          }
          if (obj.userData?.armyId !== undefined) {
            return { kind: 'army', id: obj.userData.armyId as number };
          }
          obj = obj.parent;
        }
      }
      return null;
    };

    dom.addEventListener('pointermove', (ev) => {
      const t = pickAt(ev.clientX, ev.clientY);
      if ((t?.id ?? null) !== (this.hover?.id ?? null) || t?.kind !== this.hover?.kind) {
        this.hover = t;
        this.callbacks.onHover(t);
        dom.style.cursor = t ? 'pointer' : 'default';
      }
    });
    dom.addEventListener('pointerdown', (ev) => {
      downX = ev.clientX;
      downY = ev.clientY;
    });
    dom.addEventListener('pointerup', (ev) => {
      // 拖拽（轨道旋转）不算点击
      if (Math.hypot(ev.clientX - downX, ev.clientY - downY) > 6) {
        return;
      }
      const t = pickAt(ev.clientX, ev.clientY);
      if (t?.kind === 'city') {
        this.selectedCityId = t.id;
        this.selectedArmyId = null;
        this.callbacks.onSelectCity(t.id);
      } else if (t?.kind === 'army') {
        this.selectedArmyId = t.id;
        this.callbacks.onSelectArmy(t.id);
      } else {
        this.callbacks.onClickEmpty();
      }
      this.syncSelectionVisuals();
    });

    // 操控器（亲征输入）
    controls.attach(dom);
    this.controls = controls;

    // 自适应
    this.resizeObserver = new ResizeObserver(() => {
      if (!this.container) {
        return;
      }
      const w = this.container.clientWidth;
      const h = this.container.clientHeight;
      this.renderer.setSize(w, h);
      this.camera.aspect = w / h;
      this.camera.updateProjectionMatrix();
    });
    this.resizeObserver.observe(container);

    this.adjacency.rebuild(state, null);
    this.running = true;
    this.clock.start();
    const loop = (): void => {
      if (!this.running) {
        return;
      }
      requestAnimationFrame(loop);
      const dt = Math.min(this.clock.getDelta(), 0.1);
      this.update(dt, controls);
      this.renderer.render(this.scene, this.camera);
    };
    loop();
  }

  /** 每帧更新：上帝=轨道阻尼；亲征=君主行走 + 相机跟随 */
  private update(dt: number, controls: Controls): void {
    this.elapsed += dt;

    // 相机飞行（战斗演出）
    if (this.tweens.length > 0) {
      const tw = this.tweens[0]!;
      tw.elapsed += dt;
      const k = Math.min(tw.elapsed / tw.duration, 1);
      const e = k * k * (3 - 2 * k);
      this.camera.position.lerpVectors(tw.fromPos, tw.toPos, e);
      const look = new THREE.Vector3().lerpVectors(tw.fromLook, tw.toLook, e);
      this.camera.lookAt(look);
      if (k >= 1) {
        this.tweens.shift();
        tw.resolve();
      }
    } else if (this.mode === 'god') {
      this.orbit.update();
    } else {
      // 亲征：应用输入
      controls.consumeLook(this.camYawHandle, this.camPitchHandle);
      this.camDist = THREE.MathUtils.clamp(this.camDist + controls.consumeZoom() * 0.01, 2.2, 15);

      const mv = controls.moveVector;
      const moving = mv.x * mv.x + mv.y * mv.y > 0.001;
      if (moving) {
        // 相机朝向的水平前向
        const fwd = new THREE.Vector3(-Math.sin(this.camYaw), 0, -Math.cos(this.camYaw));
        const right = new THREE.Vector3(-fwd.z, 0, fwd.x);
        const dir = new THREE.Vector3()
          .addScaledVector(fwd, mv.y)
          .addScaledVector(right, mv.x);
        if (dir.lengthSq() > 0.001) {
          dir.normalize();
          const speed = 14;
          this.lordPos.addScaledVector(dir, speed * dt);
          this.lordPos.x = THREE.MathUtils.clamp(this.lordPos.x, -MAP_BOUND, MAP_BOUND);
          this.lordPos.z = THREE.MathUtils.clamp(this.lordPos.z, -MAP_BOUND, MAP_BOUND);
          this.lordYaw = Math.atan2(dir.x, dir.z);
        }
      }
      const groundY = terrainHeight(this.lordPos.x, this.lordPos.z) * this.flattenUnderLord();
      this.lordPos.y = groundY;
      if (this.lord) {
        this.lord.group.position.copy(this.lordPos);
        this.lord.group.rotation.y = this.lordYaw;
        this.lord.animate(moving ? 1 : 0, this.elapsed);
        this.lord.setVisible(this.camDist > 2.5); // 贴脸切第一人称
      }

      // 相机跟随（球坐标）
      const horiz = Math.cos(this.camPitch) * this.camDist;
      const camPos = new THREE.Vector3(
        this.lordPos.x + Math.sin(this.camYaw) * horiz,
        this.lordPos.y + Math.sin(this.camPitch) * this.camDist + 1.6,
        this.lordPos.z + Math.cos(this.camYaw) * horiz,
      );
      if (this.camDist <= 2.5) {
        // 第一人称：相机贴头部
        const head = new THREE.Vector3(this.lordPos.x, this.lordPos.y + 1.9, this.lordPos.z).addScaledVector(
          new THREE.Vector3(-Math.sin(this.camYaw), 0, -Math.cos(this.camYaw)),
          0.3,
        );
        this.camera.position.copy(head);
        this.camera.lookAt(head.x - Math.sin(this.camYaw) * 4, head.y - 0.2, head.z - Math.cos(this.camYaw) * 4);
      } else {
        this.camera.position.copy(camPos);
        this.camera.lookAt(this.lordPos.x, this.lordPos.y + 1.6, this.lordPos.z);
      }
    }

    // 部队旗面轻微飘动（演出）
    for (const handles of this.armyMeshes.values()) {
      const flag = handles.group.getObjectByName('flag');
      if (flag) {
        flag.rotation.y = Math.sin(this.elapsed * 2.2 + handles.group.id) * 0.18;
      }
    }

    // 行军箭头脉冲
    if (this.marchArrow) {
      const mat = (this.marchArrow.children[0] as THREE.Mesh)?.material as THREE.MeshBasicMaterial | undefined;
      if (mat) {
        mat.opacity = 0.55 + 0.4 * Math.sin(this.elapsed * 5);
      }
    }

    // 城名标签 LOD：亲征时仅近距离显示
    for (const [id, handles] of this.cityMeshes) {
      if (this.mode === 'god') {
        handles.setLabelVisible(true);
      } else {
        const p = cityPosition(id);
        handles.setLabelVisible(p.distanceTo(this.lordPos) < 90);
      }
    }
  }

  private flattenUnderLord(): number {
    // 君主脚下与地形城区压平逻辑一致（城区附近为 0）
    for (const [id] of this.cityMeshes) {
      const p = cityPosition(id);
      if (Math.hypot(p.x - this.lordPos.x, p.z - this.lordPos.z) < 12) {
        return 0;
      }
    }
    return 1;
  }

  private camYawHandle = (delta: number): void => {
    this.camYaw += delta;
  };

  private camPitchHandle = (delta: number): void => {
    this.camPitch = THREE.MathUtils.clamp(this.camPitch + delta, -0.05, 1.15);
  };

  // ---------------------------------------------------------------------------
  //  模式切换
  // ---------------------------------------------------------------------------
  setMode(mode: ViewMode): void {
    this.mode = mode;
    if (mode === 'god') {
      this.orbit.enabled = true;
      this.camera.position.copy(GOD_CAMERA_POS);
      this.orbit.target.set(0, 0, 0);
      this.orbit.update();
      this.lord?.setVisible(false);
    } else {
      this.orbit.enabled = false;
      this.lord?.setVisible(true);
      this.camYaw = 0.6;
      this.camPitch = 0.42;
      this.camDist = 7.5;
    }
    this.adjacency.rebuild(this.store.state, this.selectedCityId);
  }

  // ---------------------------------------------------------------------------
  //  选中 / 悬停可视化
  // ---------------------------------------------------------------------------
  setSelectedCity(cityId: number | null): void {
    this.selectedCityId = cityId;
    this.syncSelectionVisuals();
  }

  focusCity(cityId: number): void {
    const p = cityPosition(cityId);
    this.tweens.push({
      fromPos: this.camera.position.clone(),
      toPos: new THREE.Vector3(p.x + 46, 52, p.z + 58),
      fromLook: this.orbit.target.clone(),
      toLook: p.clone(),
      elapsed: 0,
      duration: 0.7,
      resolve: () => {
        this.orbit.target.copy(p);
        this.orbit.update();
      },
    });
  }

  private syncSelectionVisuals(): void {
    for (const [id, handles] of this.cityMeshes) {
      handles.setSelected(id === this.selectedCityId);
    }
    this.adjacency.rebuild(this.store.state, this.selectedCityId);
  }

  // ---------------------------------------------------------------------------
  //  军议：行军箭头（地面升起）
  // ---------------------------------------------------------------------------
  showMarchArrow(fromCityId: number, toCityId: number): void {
    this.removeMarchArrow();
    const from = cityPosition(fromCityId);
    const to = cityPosition(toCityId);
    const dir = new THREE.Vector3().subVectors(to, from);
    const len = dir.length();
    if (len < 0.001) {
      return;
    }
    const g = new THREE.Group();

    const shaft = new THREE.Mesh(
      new THREE.CylinderGeometry(0.9, 0.9, len - 3, 10),
      new THREE.MeshBasicMaterial({ color: 0xc9a44c, transparent: true, opacity: 0.8 }),
    );
    shaft.position.copy(from).addScaledVector(dir, 0.5);
    shaft.position.y = 1.6;
    shaft.rotation.z = Math.PI / 2;
    shaft.rotation.y = -Math.atan2(dir.z, dir.x);
    // 让圆柱轴线指向目标
    shaft.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir.clone().normalize());
    g.add(shaft);

    const head = new THREE.Mesh(
      new THREE.ConeGeometry(2.2, 3.4, 10),
      new THREE.MeshBasicMaterial({ color: 0xc9a44c, transparent: true, opacity: 0.9 }),
    );
    head.position.copy(to).addScaledVector(dir, -1.7 / Math.max(len, 0.001));
    head.position.y = 1.6;
    head.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir.clone().normalize());
    g.add(head);

    this.marchArrow = g;
    this.fxGroup.add(g);
  }

  removeMarchArrow(): void {
    if (this.marchArrow) {
      this.fxGroup.remove(this.marchArrow);
      this.marchArrow.traverse((o) => {
        const m = o as THREE.Mesh;
        if (m.isMesh) {
          m.geometry.dispose();
          (m.material as THREE.Material).dispose();
        }
      });
      this.marchArrow = null;
    }
  }

  // ---------------------------------------------------------------------------
  //  战斗演出支持
  // ---------------------------------------------------------------------------
  flySequence(points: Array<{ pos: THREE.Vector3; look: THREE.Vector3; duration: number }>): Promise<void> {
    return new Promise((resolve) => {
      const step = (): void => {
        const next = points.shift();
        if (!next) {
          resolve();
          return;
        }
        this.tweens.push({
          fromPos: this.camera.position.clone(),
          toPos: next.pos.clone(),
          fromLook: this.currentLook(),
          toLook: next.look.clone(),
          elapsed: 0,
          duration: next.duration,
          resolve: step,
        });
      };
      step();
    });
  }

  private currentLook(): THREE.Vector3 {
    if (this.mode === 'god') {
      return this.orbit.target.clone();
    }
    return this.lordPos.clone();
  }

  /** 战斗位置特效（InstancedMesh 火花，纯演出） */
  sparkAt(cityId: number): void {
    const p = cityPosition(cityId);
    const count = 36;
    const mesh = new THREE.InstancedMesh(
      new THREE.BoxGeometry(0.5, 0.5, 0.5),
      new THREE.MeshBasicMaterial({ color: 0xffb84d, transparent: true, opacity: 0.95 }),
      count,
    );
    mesh.position.copy(p);
    mesh.position.y = 6;
    const dirs: THREE.Vector3[] = [];
    const dummy = new THREE.Object3D();
    for (let i = 0; i < count; i++) {
      dirs.push(new THREE.Vector3(Math.random() - 0.5, Math.random() * 0.9 + 0.2, Math.random() - 0.5).normalize());
    }
    this.fxGroup.add(mesh);
    let t = 0;
    const anim = (): void => {
      t += 0.035;
      for (let i = 0; i < count; i++) {
        dummy.position.copy(dirs[i]!).multiplyScalar(t * 16);
        dummy.position.y = Math.max(0.2, dummy.position.y - t * t * 6);
        dummy.updateMatrix();
        mesh.setMatrixAt(i, dummy.matrix);
      }
      mesh.instanceMatrix.needsUpdate = true;
      const mat = mesh.material as THREE.MeshBasicMaterial;
      mat.opacity = Math.max(0, 0.95 - t * 0.85);
      if (t < 1.2) {
        requestAnimationFrame(anim);
      } else {
        this.fxGroup.remove(mesh);
        mesh.geometry.dispose();
        mat.dispose();
      }
    };
    anim();
  }

  /** 世界坐标 → 屏幕像素（交互气泡定位） */
  screenPos(v: THREE.Vector3): { x: number; y: number } | null {
    const ndc = v.clone().project(this.camera);
    if (ndc.z > 1) {
      return null;
    }
    return {
      x: ((ndc.x + 1) / 2) * this.container.clientWidth,
      y: ((-ndc.y + 1) / 2) * this.container.clientHeight,
    };
  }

  get lordWorldPos(): THREE.Vector3 {
    return this.lordPos.clone();
  }

  /** 部队组当前世界坐标（供交互检测） */
  armyWorldPos(armyId: number): THREE.Vector3 | null {
    return this.armyMeshes.get(armyId)?.group.position.clone() ?? null;
  }

  /** 交互键边沿（E / Enter / 右下按钮） */
  consumeInteract(): boolean {
    return this.controls?.consumeInteract() ?? false;
  }

  // ---------------------------------------------------------------------------
  //  状态刷新（store.onChange）
  // ---------------------------------------------------------------------------
  refreshFromStore(): void {
    const state = this.store.state;

    for (const city of state.cities) {
      const handles = this.cityMeshes.get(city.id);
      if (handles) {
        handles.update(city);
      }
    }

    // 部队增删 + 更新
    const armyIds = new Set<number>();
    for (const army of state.armies) {
      armyIds.add(army.id);
      let handles = this.armyMeshes.get(army.id);
      if (!handles) {
        // 新部队：同步占位（不留空窗）——createArmyMesh 的 GLTF 探测已有缓存，通常直接占位体
        void createArmyMesh(army, this.slots).then((h) => {
          this.armyMeshes.set(army.id, h);
          this.armyGroup.add(h.group);
          const a = this.store.army(army.id);
          if (a) {
            h.update(a, this.store.state);
          }
        });
      } else {
        handles.update(army, state);
      }
    }
    for (const [id, handles] of this.armyMeshes) {
      if (!armyIds.has(id)) {
        this.armyGroup.remove(handles.group);
        handles.dispose();
        this.armyMeshes.delete(id);
        if (this.selectedArmyId === id) {
          this.selectedArmyId = null;
        }
      }
    }

    this.syncSelectionVisuals();
  }

  dispose(): void {
    this.running = false;
    this.resizeObserver?.disconnect();
    this.adjacency.dispose();
    this.terrain.dispose();
    for (const c of this.cityMeshes.values()) {
      c.dispose();
    }
    for (const a of this.armyMeshes.values()) {
      a.dispose();
    }
    this.lord?.dispose();
    this.renderer.dispose();
    this.renderer.domElement.remove();
  }
}
