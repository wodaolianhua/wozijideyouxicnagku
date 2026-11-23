// 契约的 TypeScript 版本 —— 与 Enums.cs / Models.cs 一一对应。
// 权威来源：MiningGame.Contracts/（本文件与 .cs 同源，改一处必须同步改另一处）。
// 前端（03 包）通过分发脚本拿到只读副本，禁止手抄、禁止自行声明这些类型。

// ===== 枚举（数值与 .cs 严格一致，从 0 开始）=====
export enum MineralType { Soil, Coal, Iron, Copper, Silver, Gold, RareCrystal }
export enum TileType { Air, Dirt, Stone, Bedrock, Ore }
export enum ToolKind { BareHands, Pickaxe, Drill, Excavator }
export enum WorkerKind { Robot, HiredMiner }
export enum ActionKind { Move, Dig, Sell, BuyTool, HireWorker, GoSurface }
export enum Direction { Up, Down, Left, Right }
export enum DigResultCode { Ok, Empty, NotDiggable, ToolTooWeak, BackpackFull, OutOfBounds }
export enum MoveResultCode { Ok, Blocked, OutOfBounds }
export enum AdvanceResultCode { Ok, NotEnoughMoney, InvalidTarget, BackpackFull }

// ===== 数据结构（字段名与 .cs 属性名严格一致：PascalCase）=====
// ⚠️ 字段名必须保持 PascalCase（大写开头），与 C# 属性名一模一样。
// 原因：.NET System.Text.Json 默认 PascalCase，全链统一 = 零配置、零歧义。禁止改成 camelCase。
export interface Cell {
  X: number; Y: number; Tile: TileType; Mineral: MineralType;
  Hardness: number; OreAmount: number;
}
export interface MineGrid {
  Width: number; Height: number; SurfaceY: number; ShaftX: number;
  Cells: Cell[];
}
export interface BackpackItem { Mineral: MineralType; Amount: number; }
export interface Backpack { Capacity: number; Used: number; Items: BackpackItem[]; }
export interface Worker {
  Id: string; Kind: WorkerKind; X: number; Y: number;
  Power: number; Interval: number; Wage: number;
}
export interface ToolSpec { Kind: ToolKind; Name: string; Power: number; Price: number; }
export interface WorkerSpec {
  Kind: WorkerKind; Name: string; Power: number;
  Interval: number; Wage: number; Price: number;
}
export interface GameAction {
  Kind: ActionKind; Direction: Direction; Tool: ToolKind; Worker: WorkerKind;
}
export interface DigResult {
  Code: DigResultCode; Mineral: MineralType; Amount: number;
  X: number; Y: number; StaminaCost: number;
}
export interface SellResult { Income: number; Sold: BackpackItem[]; }
export interface GameState {
  Time: number; Grid: MineGrid; MinerX: number; MinerY: number;
  Stamina: number; MaxStamina: number; Money: number;
  CurrentTool: ToolKind; OwnedTools: ToolKind[];
  Backpack: Backpack; Workers: Worker[];
  ToolShop: ToolSpec[]; WorkerShop: WorkerSpec[];
  Prices: { [mineralType: number]: number };
}
export interface DigOutcome { Digs: DigResult[]; Sale: SellResult; Message: string; }
export interface AdvanceResult {
  State: GameState; Outcomes: DigOutcome[]; Code: AdvanceResultCode;
}

// ===== JSON 线格式（全链统一 PascalCase，零配置）=====
// 01 造的数据 JSON、后端 API 收发、前端解析，字段名一律 PascalCase（与 .cs 属性名一致）。
// 枚举以数值传输（MineralType.Gold = 5），前端按上面枚举数值解析，不要当字符串。
// 后端不要额外配置 camelCase 序列化策略——保持 .NET 默认即可。
