# 04_智能体层 · 任务书

## 这是什么

两件事：**AI 势力怎么决策**，以及**人才系统怎么运转**。

人才系统是本项目区别于普通 SLG 的核心特色——"像皇帝一样用人"。这个包做好了，项目立得住；做不好，就退化成一个普通的数值经营游戏。

## 铁律（最重要）

**AI 势力只能通过产出 `Order` 影响世界，绝不允许直接修改 `WorldState`。**

这不是风格偏好，是硬性要求。如果 AI 直接改状态，会出现三类无法修复的问题：

1. AI 走了一条玩家走不通的路径（因为它绕过了校验）
2. 无头模拟器无法复现AI 的行为，平衡验证失效
3. 后期出现"AI 因为某个 bug 凭空获得资源"时，无法定位

代码审查时看到 AI 层直接调用 `StateApplier` 或修改 `state`，一律打回。

## 技术要求

| 项 | 值 |
|---|---|
| 语言 | C# |
| 框架 | .NET 8.0 |
| 依赖 | **`StrategyGame.Contracts`** + `StrategyGame.Core` |

### 必须引用共享契约工程

```xml
<ProjectReference Include="..\..\StrategyGame.Contracts\StrategyGame.Contracts.csproj" />
<ProjectReference Include="..\..\StrategyGame.Core\StrategyGame.Core.csproj" />
```

**你实现的是 `IFactionPlanner` 和 `IPersonnelPolicy` 两个接口。** 接口签名在 `接口契约_共享类型/Interfaces.cs` 里，**不要自己重新声明这两个接口**——IDE 会自动列出可实现的方法让你选。

禁止重新声明 `Order` / `Officer` / `WorldState` 等类型。

## 交付物清单

```
src/
  IFactionPlanner.cs           # 接口（在 03 包定义，本包实现）
  AiFactionPlanner.cs          # 主决策器
  AiStrategies/
    EconStrategy.cs            # 优先发展经济
    MilitaryStrategy.cs        # 优先军事扩张
    DefenseStrategy.cs         # 优先固守
    StrategySelector.cs        # 策略选择
  Personnel/
    IPersonnelPolicy.cs        # 接口
    BestFitPolicy.cs           # 按岗位匹配最佳人才
    LoyaltySystem.cs           # 忠诚度与叛逃
src/Personnel/
  PersonEffects.cs             # 人才特质如何影响结算
tests/
  AiPlannerTests.cs
  PersonnelPolicyTests.cs
  LoyaltyTests.cs
```

## 逐条需求

### R1 人才效果必须真实影响结算

这是"用人"能不能成为乐趣的关键。测试标准：

- 把 `Agriculture` 专长的人才放到 `CityGovernor` 岗，该城粮产**明显提升**
- 换成一个`None` 特质的人，粮产回落
- 高 `Command` 的人当`General`，攻城战损率下降
- `Logistics` 专长的人在任上时，部队 `Morale` 衰减更慢

**如果换人之后数值没变化，"用人"就只是个装饰。** 这是本包最重要的验收项。

### R2 AI 决策器

`AiFactionPlanner.Plan(state, factionId)` 返回下个月要执行的 `Order` 列表。

三种基础策略（原型阶段够用）：

| 策略 | 行为 |
|---|---|
| `EconStrategy` | 粮银不足时优先屯田，其次征兵 |
| `MilitaryStrategy` | 兵力充足时向相邻城出兵 |
| `DefenseStrategy` | 敌兵逼近时优先征兵 + 修城 |

`StrategySelector` 根据势力状态选择策略（缺粮→Econ，兵多且有威胁→Military，受威胁→Defense）。

**AI 每回合产出的 Order 数量必须设上限**（建议 ≤5），否则会淹没 UI。

### R3 忠诚度系统

- 忠诚度每月衰减（幅度来自 `BalanceConfig.LoyaltyDecayPerMonth`）
- 低于 `LoyaltyThreshold` 时，可能触发叛逃事件
- 叛逃：人才 `OwnerFactionId` 变为 -1，可能加入敌方势力
- **必须做反制手段**：玩家应能通过赏赐、加官进爵提升忠诚。如果忠诚只降不升，玩家最终会失去所有人才，这个机制就是纯负面体验

### R4 人才任命

- `AssignPost` 指令把人才放到 `PostType` 岗位
- 一个岗位一个人，一个同时只能担任一个岗位
- 换任需要时间（建议 1 个月），不能瞬间切换
- 关键岗位必须有对应专长才能发挥加成，无专长则加成为基础值

### R5 智能体性能

AI 决策在 `Tick` 第 9 步执行，每回合每个势力都要跑一次。必须保证单次决策耗时 < 5ms，否则 1000 次批量模拟会跑很久。

## 验收标准（DoD）

### 硬性（不满足则包退回）

- [ ] `AiFactionPlanner` 中无任何直接修改 `WorldState` 的代码
- [ ] 换人测试通过：调换同岗位的人才，结算结果**可观测地变化**
- [ ] 忠诚度有升有降，不存在只降不升的单向路径
- [ ] 单次 `Plan` 耗时 < 5ms

### 功能

- [ ] 三种 AI 策略都能跑通且行为明显不同
- [ ] `PlanAll` 能为全部 AI 势力产出指令，每势力不超过 5 条
- [ ] 人才有专长且专长效果在结算中生效
- [ ] 叛逃事件能被触发，且能被玩家赏赐阻止
- [ ] 岗位占用互斥正确（一个人不能同时管两座城）
- [ ] AI 单位测试：连续模拟 100 回合不崩溃、不产生非法 Order

### 边界

- [ ] AI 不会产出核心契约 `ActionType` 之外的指令
- [ ] AI 不会产出必然失败的 Order（资源不足却持续征兵）

## 禁止事项

- **禁止直接修改 `WorldState`**（只能产出 `Order`）
- 禁止为AI 写特殊通道（"AI 专用加资源"这类后门一律禁止）
- 禁止硬编码 AI 势力数量
- 禁止在 `Plan` 里调用 `StateApplier`
- 禁止把忠诚度做成只降不升的单向系统
- 禁止 AI 决策产生超过上限的 Order 洪水