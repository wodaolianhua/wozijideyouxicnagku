// 契约的 TypeScript 版本 —— 与 Enums.cs / Models.cs 一一对应。
// 权威来源：StrategyGame.Contracts/（本文件与 .cs 同源，改一处必须同步改另一处）。
// 前端（03 包）通过分发脚本拿到只读副本，禁止手抄、禁止自行声明这些类型。

// ===== 枚举（数值与 .cs 严格一致，从 0 开始）=====
export enum TerrainType { Plain, Hill, Mountain, Forest, River }
export enum EdgeType { Road, MountainPath }
export enum Faction { Player, Enemy, Neutral }
export enum UnitType { Infantry, Cavalry, Archer, Siege }
export enum OrderKind { MoveTo, MoveVia, Hold }
export enum EventKind { Arrival, Encounter }
export enum ResolveResultCode { Ok, NoPath, InvalidTarget, AlreadyArrived }

// ===== 数据结构（字段名与 .cs 属性名严格一致：PascalCase）=====
// ⚠️ 字段名必须保持 PascalCase（大写开头），与 C# 属性名一模一样。
// 原因：.NET System.Text.Json 默认序列化和反序列化都用 PascalCase，
// 全链统一 PascalCase = 零配置、零歧义。禁止改成 camelCase，否则数据静默丢失。
export interface MapNode {
  Id: string; Name: string; Terrain: TerrainType; X: number; Y: number;
}
export interface MapEdge {
  From: string; To: string; Type: EdgeType; Cost: number;
}
export interface WorldMap {
  Nodes: MapNode[]; Edges: MapEdge[];
}
export interface Army {
  Id: string; Name: string; Faction: Faction; UnitType: UnitType;
  Strength: number; NodeId: string; EdgeProgress: number;
  PlannedPath: string[]; PathIndex: number;
}
export interface Order {
  ArmyId: string; Kind: OrderKind; Waypoints: string[];
}
export interface TrailPoint {
  X: number; Y: number; Time: number;
}
export interface MarchTrail {
  ArmyId: string; Points: TrailPoint[];
}
export interface EncounterEvent {
  Time: number; NodeId: string; ArmyIds: string[];
  IsAmbush: boolean; AmbusherId: string;
  Loss: LossResult;   // 本次遭遇损失，随事件传给前端展示
}
export interface LossResult {
  LossByArmy: { [armyId: string]: number };
}
export interface WorldState {
  Time: number; Map: WorldMap; Armies: Army[];
}
export interface AdvanceResult {
  State: WorldState; Trails: MarchTrail[];
  Encounters: EncounterEvent[]; Code: ResolveResultCode;
}

// ===== JSON 线格式（全链统一 PascalCase，零配置）=====
// 01 造的数据 JSON、后端 API 收发、前端解析，字段名一律 PascalCase（与 .cs 属性名一致）。
// 枚举以数值传输（TerrainType.Mountain = 2），前端按上面枚举数值解析，不要当字符串。
// 后端不要额外配置 camelCase 序列化策略——保持 .NET 默认即可。
