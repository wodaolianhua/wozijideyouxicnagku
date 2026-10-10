# 03_规则内核层 · 任务书

## 这是什么

**这是整个项目的脊椎，也是唯一没有第二次机会的包。**

如果这个包的架构错了，后面所有功能都要跟着改。因此这个包的质量标准高于其他所有包——宁可慢，不可错。

它实现那条主干：

```
Order → Resolver → StateDelta → WorldState → Tick → 下一回合
```

## 技术要求

| 项 | 值 |
|---|---|
| 语言 | C# |
| 框架 | .NET 8.0 |
| 工程名 | `StrategyGame.Core` |
| 依赖 | **仅** `StrategyGame.Contracts` + .NET BCL |
| 测试 | xUnit |

**铁律：不得 `using UnityEngine` /任何引擎 SDK。** CI 检查：`grep -r "using UnityEngine\|using Godot" StrategyGame.Core/ && exit 1`

### 必须引用共享契约工程

csproj 里加上：

```xml
<ProjectReference Include="..\..\StrategyGame.Contracts\StrategyGame.Contracts.csproj" />
```

**禁止重新声明 `WorldState` / `Order` / `ResolveResult` / `IResolver` 等类型**——这些都在契约工程里，重���声明会导致类型不匹配，编译失败。

类型定义见 `接口契约_共享类型/`（只读副本）。

## 可以立刻开工的部分

本包**不等01 设计规格定稿也能开工**：

| 现在就能写 | 等设计定稿后再填 |
|---|---|
| 全部类型引用与工程结构 | Resolver 里的数值公式 |
| `IResolver` 六个实现的**骨架**（校验分支 + 返回码） | 战斗系统公式的系数 |
| `StateApplier`（纯字段赋值，逻辑固定） | 数值表的具体取值 |
| `GameClock`（时间换算，逻辑固定） | 事件的概率配置 |
| `TurnPipeline` 的 11 步**调度结构** | |
| 全部单元测试框架 | |

做法：先用占位数值（如 `return 0;`）让流程跑通并通过测试，等设计定稿后只替换数值，不动结构。**结构先定，数值后填。**

## 交付物清单

| 文件/目录 | 内容 |
|---|---|
| `src/WorldState.cs` | 状态类型 + 深拷贝 `Clone()` |
| `src/Types.cs` | 全部枚举（`ActionType` / `ActorType` / `ArmyStance` / `PostType` / `ResolveResultCode`） |
| `src/Order.cs` | 统一指令 + 三个工厂方法 |
| `src/IResolver.cs` | 结算器接口 |
| `src/Resolvers/*.cs` | 六个结算器，每个独立文件 |
| `src/ResolverRegistry.cs` | 注册表 + 分发 |
| `src/CommandProcessor.cs` | 批量处理 |
| `src/StateApplier.cs` | 把 `StateDelta` 写回状态 |
| `src/GameClock.cs` | 时间推进 |
| `src/TurnPipeline.cs` | 回合流水线（11 步固定顺序） |
| `src/Config/*.cs` | 配置加载接口 + 校验器 |
| `tests/*.cs` | 单元测试 |
| `StrategyGame.Core.csproj` | 工程文件 |

## 关键设计决策（必须遵守）

### 决策 1：Resolver 是纯函数

```csharp
//错误写法：
ResolveResult Resolve(WorldState state, Order order) {
    state.Cities[0].Defense -= 10;   // 直接改！无法回放，无法对比
    return result;
}

// 正确写法：
ResolveResult Resolve(WorldState state, Order order) {
    var result = new ResolveResult { Code = ResolveResultCode.Success };
    result.Deltas.Add(new StateDelta {
        TargetType = "City", TargetId = 0, Field = "Defense",
        OldValue = 30, NewValue = 40
    });
    return result;
}
```

**理由**：无头模拟器要做"两次模拟的状态差异对比"，回放系统要能从日志重放，存档要能精确回滚。如果 Resolver 直接改状态，这三件事全部做不到。

所有状态写入统一走 `StateApplier.Apply(state, deltas)`。

### 决策 2：失败必须显式拒绝，不能静默失败

每一次 `Resolve` 必须返回明确的结果码。资源不足、目标非法、执行者不存在——**全部要返回对应的 `Rejected_*`码并附Message**。静默忽略会导致 UI 层面"点了没反应"，玩家无法理解。

### 决策 3：Tick 的 11 步顺序固定

见下方"TurnPipeline 规格"。顺序调整会引发连锁 bug，不要自行改动。

### 决策 4：随机性必须可播种

```csharp
// 错误：
if (Random.Shared.Next(100) < 15) { /* 触发事件 */ }

// 正确：
private readonly Random _rng;
public TurnPipeline(..., int seed) { _rng = new Random(seed); }
if (_rng.Next(100) < 15) { /* ... */ }
```

`WorldState` 里要存`Seed`，保证存档重载后随机序列一致。

## 逐条需求

### R1 状态层

- `WorldState.Clone()` 必须**深拷贝**全部子对象（Faction / City / Army / Officer / PendingOrder）。验收方法：克隆后修改副本，原对象不受影响。
- `GetFaction/GetCity/GetOfficer` 返回 `null` 而非抛异常（找不到时）。
- `TimeUtil.TotalMonths(year, month)` 用于把日期转成单调整数，方便比较。

### R2 指令层

`Order` 的三个工厂方法分别构造 Player / Officer / AiFaction 三种来源的指令，**字段结构完全相同**——这是"AI 与玩家同路"的代码基础。

`ExecuteYear` / `ExecuteMonth` 用于延迟执行。`PendingOrders` 是延迟指令队列。

### R3 结算器层

六个结算器，每个必须：

1. 先做完整的前置校验，返回对应的 `Rejected_*` 码
2. 校验通过后计算所有影响，产出 `StateDelta` 列表
3. 产出至少一条 `EventRecord`
4. **不修改传入的 state**

`ResolverRegistry.Dispatch` 负责查找对应结算器并调用。未注册的 `ActionType` 必须返回 `Rejected_InvalidTarget` 而不是抛异常。

**公式来源**：`02_内容数据层` 的数值表，以及 `01_设计规格层` 的战斗系统设计文档。公式里的系数不要硬编码，从 `BalanceConfig` 读。

### R4 时间层

`GameClock.AdvanceMonth`：月 +1，超过 12 则年 +1、月归1。

`TurnPipeline.RunMonth`严格按 11 步执行：

| # | 步骤 | 说明 |
|---|---|---|
| 1 | 推进时间 | 月/年 |
| 2 | 经济结算 | 粮 = 人口 × 系数 × 人才修正 |
| 3 | 民心/稳定 | 按公式衰减或恢复 |
| 4 | 基建推进 | 城防等级、屯田度累积 |
| 5 | 兵源结算 | 按人口 × Stability 生成 Soldier |
| 6 | 人物事件 | 按事件库概率抽取 |
| 7 | 执行 PendingOrders | 取出本月到期指令交 CommandProcessor |
| 8 | 兵源消耗 | Soldier 归零 → 触发饥荒事件 |
| 9 | AI 决策 | 产出下月 Order（见 04 包，本包只调用接口） |
| 10 | 胜负判定 | 玩家仅剩一城 / 全势力覆灭 → GameOver |
| 11 | 交还决策权 | 返回 TurnResult |

第 9 步通过 `IFactionPlanner` 接口调用。**本包只依赖接口，不依赖 04 包的具体实现**，这样 03 可以独立编译独立测试。

### R5 配置层

- `IConfigLoader` 定义加载接口，具体实现可以先写一个读本地文件的
- `ConfigValidator.Validate` 返回所有问题列表（不抛异常），由调用方决定是否中断
- 校验至少覆盖：数值非负、事件概率在 0-100 之间、人才属性在有效范围内、城市引用存在

## 验收标准（DoD）

### 功能

- [ ] `dotnet build` 零错误零警告
- [ ] 单元测试全部通过，测试覆盖：六个结算器各自的成功路径 + 至少一条拒绝路径
- [ ] 克隆测试：`Clone()` 深拷贝验证通过
- [ ] 确定性测试：同 Seed 同指令序列，两次模拟的最终状态逐字段相等
- [ ] 纯净性测试：`Resolve` 调用前后state 未被修改（快照对比）
- [ ] 未注册的 ActionType 返回拒绝码而非异常
- [ ] 资源不足时正确返回 `Rejected_NoResources`

### 架构

- [ ] 规则层无任何引擎依赖（CI 检查通过）
- [ ] `Tick` 11 步顺序与规格一致
- [ ] 所有数值系数从 `BalanceConfig` 读取，代码中无硬编码魔法数字
- [ ] `TurnPipeline` 对 `IFactionPlanner` 只有接口依赖

### 交付

- [ ] 每个 Resolver 独立文件
- [ ] 所有公开类型有XML 文档注释（说明用途和参数含义）
- [ ] 提供一份最小示例数据（3 势力 / 6 城 / 5 人才）供测试使用

## 禁止事项

- **禁止直接修改传入的 `state`**（必须通过 `StateDelta`）
- 禁止使用未播种的 `Random`
- 禁止硬编码数值（全部走配置）
- 禁止引用 04/05/06 包的任何类型（`IFactionPlanner` 接口除外，且接口定义在本包）
- 禁止把玩法逻辑写进 `TurnPipeline`（流水线只做调度，具体逻辑在 Resolver 里）
- 禁止在没有验收测试通过前提交