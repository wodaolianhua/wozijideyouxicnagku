/**
 * 士兵级操控 —— 框选、指令、移动
 *
 * 这是核心玩法的入口：玩家能选中部队并下令。
 * 对应文档 01 §3.2 循环 A（战术层，秒级）。
 *
 * 设计要点：
 * - 框选用屏幕空间矩形 vs 单位的投影位置
 * - 指令是「朝某点移动」，单位自动避开不可通行地形
 * - 所有逻辑为纯函数或可控状态，便于测试
 */

import * as THREE from 'three';
import type { ArmyClass } from '../model/combat';
import type { TerrainData } from '../render/terrain';

/** 单位行为状态 */
export type UnitState = 'idle' | 'moving' | 'engaging' | 'routing';

export interface ControllableUnit {
  id: number;
  cls: ArmyClass;
  /** 当前世界坐标 */
  x: number;
  z: number;
  /** 当前朝向（弧度） */
  heading: number;
  /** 编制人数（随战斗递减） */
  strength: number;
  /** 所属方 */
  side: 'blue' | 'red';
  /** 当前移动目标点，null = 待命 */
  targetX: number | null;
  targetZ: number | null;
  /** 是否已选中 */
  selected: boolean;
  /** 移动速度（米/秒） */
  speed: number;
  /** 行为状态 */
  state: UnitState;
  /** 正在交战的敌方单位 id，null = 未交战 */
  engageTargetId: number | null;
  /** 士气0~100，跌破 ROUT_MORALE 即溃退 */
  morale: number;
  /** 待走路径（世界坐标点列），null 或空 = 无路径 */
  path: Vec2[] | null;
  /** 路径已重算的冷却（秒），避免每帧重算 */
  pathCooldown: number;
}

/** 各兵种移动速度（米/秒） */
export const CLASS_SPEED: Record<ArmyClass, number> = {
  infantry: 5.0,
  archer: 5.6,
  cavalry: 11.0,
};

/** 各兵种攻击距离（米）—— 进入此距离即自动交战 */
export const CLASS_ATTACK_RANGE: Record<ArmyClass, number> = {
  infantry: 12,
  archer: 140,
  cavalry: 16,
};

/** 交战时的转向速度（弧度/秒），比移动时慢，体现"打起来了顾不上调整朝向" */
export const ENGAGE_TURN_RATE = 1.2;

export function createUnit(
  cls: ArmyClass,
  x: number,
  z: number,
  heading: number,
  strength: number,
  side: 'blue' | 'red',
): ControllableUnit {
  return {
    id: nextId++,
    cls,
    x,
    z,
    heading,
    strength,
    side,
    targetX: null,
    targetZ: null,
    selected: false,
    speed: CLASS_SPEED[cls],
    state: 'idle',
    engageTargetId: null,
    morale: 70,
    path: null,
    pathCooldown: 0,
  };
}

/** 各兵种在各地形的通行系数（1=正常，0.3=极难，0=不可通行） */
export const TERRAIN_PASS: Record<string, Record<ArmyClass, number>> = {
  plain: { infantry: 1.0, archer: 1.0, cavalry: 1.0 },
  forest: { infantry: 0.75, archer: 0.8, cavalry: 0.5 },
  hill: { infantry: 0.6, archer: 0.65, cavalry: 0.25 },
  river: { infantry: 0.7, archer: 0.7, cavalry: 0.45 },
  fort: { infantry: 0.9, archer: 0.9, cavalry: 0.6 },
};

/** 框选矩形（屏幕空间，像素） */
export interface SelectionRect {
  x0: number;
  y0: number;
  x1: number;
  y1: number;
}

export interface Vec2 {
  x: number;
  z: number;
}

/** 屏幕坐标（像素）：x向右，y 向下（与 DOM 一致） */
export interface ScreenPoint {
  x: number;
  y: number;
}

let nextId = 1;

/**
 * 地形通行系数。
 * 未知地形返回 0（不可通行）——让 A* 把它当障碍，而不是猜一个值。
 */
export function passability(td: TerrainData, x: number, z: number, cls: ArmyClass): number {
  const key = td.keyAt(x, z);
  const table = TERRAIN_PASS[key];
  return table ? table[cls] : 0;
}

/** 某点是否可通行（任意兵种至少能过） */
export function isWalkable(td: TerrainData, x: number, z: number): boolean {
  const key = td.keyAt(x, z);
  return key in TERRAIN_PASS;
}

/**
 * 单位向目标推进一帧。
 *
 * 规则：
 * - 目标为空则原地待命
 * - 每步按 speed × dt × 地形通行系数推进
 * - 到达阈值内则停下并清空目标
 * - 朝向始终对准移动方向（平滑插值）
 *
 * 返回新单位对象（不修改传入对象）。
 */
export function stepUnit(u: ControllableUnit, td: TerrainData, dt: number): ControllableUnit {
  if (u.targetX === null || u.targetZ === null) return { ...u, path: null };

  let goalX = u.targetX;
  let goalZ = u.targetZ;
  let path = u.path;

  // 无路径或路径已走完：直接朝最终目标走（直线）
  if (!path || path.length === 0) {
    // 直线可达则直接走；不可达但也没路径时仍尝试直线（可能因地形系数低而很慢）
  } else {
    // 沿路径走：取路径上最近的点作为当前路点
    while (path.length > 0) {
      const wp = path[0];
      if (Math.hypot(wp.x - u.x, wp.z - u.z) < 4.0) {
        path = path.slice(1); // 到达路点，弹出
      } else break;
    }
    if (path.length === 0) {
      path = null; // 路径走完，剩下直接走最后一段
    } else {
      goalX = path[0].x;
      goalZ = path[0].z;
    }
  }

  const dx = goalX - u.x;
  const dz = goalZ - u.z;
  const dist = Math.hypot(dx, dz);

  // 到达判定必须在计算位移之前：单步位移可能大于剩余距离，
  // 若先位移再判断，会越过目标点且目标永远不清空（实测缺陷）。
  if (dist < 1.0) {
    const done = !path && isNearFinal(u, goalX, goalZ);
    return done
      ? { ...u, targetX: null, targetZ: null, path: null }
      : { ...u, path };
  }

  const pass = passability(td, u.x, u.z, u.cls);
  const stepDist = u.speed * pass * dt;
  const move = Math.min(dist, stepDist);

  const nx = u.x + (dx / dist) * move;
  const nz = u.z + (dz / dist) * move;

  // 目标点不可通行时，先朝目标方向找一个最近的可通行点
  const walkable = isWalkable(td, nx, nz);
  const finalX = walkable ? nx : u.x;
  const finalZ = walkable ? nz : u.z;

  // 本步之后是否已到位（仅当路径走完且到达最终目标）
  const arrived = !path && isNearFinal(u, finalX, finalZ);

  // 朝向：平滑转向目标方向
  const wantHeading = Math.atan2(finalX - u.x, finalZ - u.z);
  let diff = wantHeading - u.heading;
  while (diff > Math.PI) diff -= Math.PI * 2;
  while (diff < -Math.PI) diff += Math.PI * 2;
  const turnRate = 4.0 * dt; // 弧度/秒
  const heading = u.heading + Math.max(-turnRate, Math.min(turnRate, diff));

  return {
    ...u,
    x: finalX,
    z: finalZ,
    heading,
    path,
    targetX: arrived ? null : u.targetX,
    targetZ: arrived ? null : u.targetZ,
  };
}

/** 判断是否已到达最终目标 */
function isNearFinal(u: ControllableUnit, x: number, z: number): boolean {
  if (u.targetX === null || u.targetZ === null) return true;
  return Math.hypot(u.targetX - x, u.targetZ - z) < 1.0;
}

/** 批量推进（一个时间步内所有单位） */
export function stepAll(units: ControllableUnit[], td: TerrainData, dt: number): ControllableUnit[] {
  return units.map((u) => stepUnit(u, td, dt));
}

/**
 * 下令移动到某点。传入 terrain 与 grid 时会计算绕行路径（避开难走地形）。
 *
 * 目标不可通行时单位会原地不动（stepUnit 内做判定），但仍接受命令，
 * 这样玩家下令后能看到单位停住，理解是地形挡住了。
 */
export function orderMove(
  unit: ControllableUnit,
  x: number,
  z: number,
  path?: Vec2[] | null,
): ControllableUnit {
  return { ...unit, targetX: x, targetZ: z, path: path ?? null };
}

/** 下令停止并清除路径 */
export function orderStop(unit: ControllableUnit): ControllableUnit {
  return { ...unit, targetX: null, targetZ: null, path: null };
}

/** 全选/取消全选 */
export function selectAll(units: ControllableUnit[], selected: boolean): ControllableUnit[] {
  return units.map((u) => ({ ...u, selected }));
}

/** 选中/取消单个单位 */
export function toggleSelect(units: ControllableUnit[], id: number, selected: boolean): ControllableUnit[] {
  return units.map((u) => (u.id === id ? { ...u, selected } : u));
}

// ---------------------------------------------------------------- 框选

/**
 * 框选判定：单位的世界坐标投影到屏幕后，是否落在矩形内。
 * camera 必须是已 updateMatrixWorld 的 PerspectiveCamera。
 */
export function projectToScreen(
  worldX: number,
  worldY: number,
  worldZ: number,
  camera: THREE.PerspectiveCamera,
  width: number,
  height: number,
): ScreenPoint | null {
  const v = new THREE.Vector3(worldX, worldY, worldZ).project(camera);
  if (v.z > 1) return null; // 在相机后方
  return {
    x: ((v.x + 1) / 2) * width,
    y: ((1 - v.y) / 2) * height,
  };
}

/** 从框选矩形中筛出单位 */
export function unitsInRect(
  units: ControllableUnit[],
  rect: SelectionRect,
  camera: THREE.PerspectiveCamera,
  width: number,
  height: number,
  terrain: TerrainData,
  side?: 'blue' | 'red',
): ControllableUnit[] {
  const minX = Math.min(rect.x0, rect.x1);
  const maxX = Math.max(rect.x0, rect.x1);
  const minY = Math.min(rect.y0, rect.y1);
  const maxY = Math.max(rect.y0, rect.y1);
  const out: ControllableUnit[] = [];

  for (const u of units) {
    if (side && u.side !== side) continue;
    const p = projectToScreen(u.x, terrain.heightAt(u.x, u.z) + 1, u.z, camera, width, height);
    if (!p) continue;
    if (p.x >= minX && p.x <= maxX && p.y >= minY && p.y <= maxY) out.push(u);
  }
  return out;
}

/** 已选单位 */
export function selectedUnits(units: ControllableUnit[]): ControllableUnit[] {
  return units.filter((u) => u.selected);
}

/** 已选单位的质心（用于整体移动） */
export function selectionCentroid(units: ControllableUnit[]): Vec2 | null {
  const sel = selectedUnits(units);
  if (sel.length === 0) return null;
  let sx = 0;
  let sz = 0;
  for (const u of sel) {
    sx += u.x;
    sz += u.z;
  }
  return { x: sx / sel.length, z: sz / sel.length };
}

/**
 * 下令移动到某点：已选单位平移到相对位置，保持编队形状。
 * 这是「框选一片 → 点目标 → 整队平移」的标准操作。
 */
export function orderGroupMove(
  units: ControllableUnit[],
  destX: number,
  destZ: number,
  pathFor?: (u: ControllableUnit, targetX: number, targetZ: number) => Vec2[] | null,
): ControllableUnit[] {
  const centroid = selectionCentroid(units);
  if (!centroid) return units;
  const dx = destX - centroid.x;
  const dz = destZ - centroid.z;
  return units.map((u) => {
    if (!u.selected) return { ...u };
    const tx = u.x + dx;
    const tz = u.z + dz;
    const path = pathFor ? pathFor(u, tx, tz) : null;
    return { ...u, targetX: tx, targetZ: tz, path };
  });
}

/** 下令已选单位原地停止（清除路径） */
export function orderGroupStop(units: ControllableUnit[]): ControllableUnit[] {
  return units.map((u) =>
    u.selected ? { ...u, targetX: null, targetZ: null, path: null } : { ...u },
  );
}

/** 单位是否全部到位（无移动目标） */
export function allArrived(units: ControllableUnit[]): boolean {
  return units.every((u) => u.targetX === null || u.targetZ === null);
}

/** 单位距目标的剩余距离（用于排序与AI） */
export function distanceToTarget(u: ControllableUnit): number {
  if (u.targetX === null || u.targetZ === null) return 0;
  return Math.hypot(u.targetX - u.x, u.targetZ - u.z);
}
