# 契约类型（只读，禁止修改）

这是 `StrategyGame.Contracts/` 的**真实 .cs 源码副本**，由 `bash E:/军事指挥游戏/分发契约.sh` 自动同步。

## 最重要的一条

**不要手抄这些类型。** 你的项目通过 ProjectReference 直接引用契约工程：

```xml
<ProjectReference Include="..\..\StrategyGame.Contracts\StrategyGame.Contracts.csproj" />
```

路径按你 csproj 实际位置调整。若手抄，本项目会变成两套类型，编译必然失败。

## 命名空间

所有类型都在 `StrategyGame.Contracts` 命名空间下。

## 包含内容

| 文件 | 内容 |
|---|---|
| `Enums.cs` | TerrainType / EdgeType / Faction / UnitType / OrderKind / EventKind 等枚举 |
| `Models.cs` | WorldMap / MapNode / MapEdge / Army / Order / WorldState / MarchTrail / EncounterEvent / LossResult 等数据结构 |
| `Interfaces.cs` | IPathFinder / IMarchSimulator / IEncounterDetector / ILossResolver / IVisibilityPolicy 接口 |
| `Contracts.ts` | 上述类型的 TypeScript 版（前端用，字段/枚举数值与 .cs 一一对应） |

## 前端特别注意（03 包）

前端是 TypeScript，直接用这份 `Contracts.ts`（拷进你的 src 或配置路径引用），字段和枚举数值与 .cs 严格对应。
**禁止自己另写一套前端类型**——那就是手抄，字段对不上会静默出错。

## JSON 大小写（全链统一 PascalCase，零配置）

数据 JSON、后端 API、前端解析，字段名一律 **PascalCase**（大写开头，与 .cs 属性名一模一样，例如 NodeId / PlannedPath / UnitType）。
原因：.NET System.Text.Json 默认就是 PascalCase，全链统一 = 不用任何配置、不会静默丢数据。**禁止改成 camelCase。** 枚举传数值。

## 修改规则

- **禁止**修改本目录任何文件
- **禁止**在自己的包里重新声明这里的类型
- 觉得契约设计有问题 → 提变更申请到 `00_项目管理/变更申请/`，不要自己改
