# 02_内容数据层 · 任务书

## 这是什么

把**所有数值从代码里搬出来**，放进 JSON。策划改数值不需要找程序员，程序员不碰数值。

这是团队分工的关键：**数值调整的迭代速度直接决定这个项目的成败**，而瓶颈几乎总是"改一个数要重新编译一遍"。

## 技术要求

数据用 **JSON**，加载与校验用 **C#**。

## 交付物清单

```
assets/data/
  config.json# 全局平衡参数
  officers.json          # 人才初始表
  cities.json            # 城池初始表
  events.json            # 事件库
  specialties.json       # 特质效果表
src/Config/
  BalanceConfig.cs
  ConfigModels.cs        # OfficerTemplate / CityTemplate / GameEventTemplate
  IConfigLoader.cs
  JsonConfigLoader.cs
  ConfigValidator.cs
tests/
  ConfigValidatorTests.cs
  JsonRoundTripTests.cs
```

## 逐条需求

### R1 数据格式约定

- 编码 **UTF-8**，缩进 2 空格
- ID 一律用整数，**不用字符串做 ID**（字符串 ID 在批量模拟时是性能灾难）
- 所有可调数值必须带 `//` 注释说明用途（JSON 用`_comment` 字段）
- **禁止**在JSON 里写中文键名

### R2 config.json

对应 `BalanceConfig` 的每个字段，全部填具体数值。数值依据来自 `01_设计规格层` 的 `4_数值框架.md`。

**设计已定稿**，`1_设计规格层` 已全部完成，直接取数：

| 键 | 值 | 来源 |
|---|---|---|
| `grainPerPopPerMonth` | 0.08 | 数值框架 |
| `soldierPerPopPerMonth` | 0.5 | 数值框架 |
| `recruitCostSilverPerSoldier` | 3 | 数值框架 |
| `recruitCostGrainPerSoldier` | 2 | 数值框架 |
| `fortifyCostSilverPerLevel` | 200 | 数值框架 |
| `developCostSilverPerLevel` | 150 | 数值框架 |
| `stabilityNaturalRecovery` | 5 | 数值框架 |
| `moraleNaturalRecovery` | 3 | 数值框架 |
| `moraleDecayWhenPeace` | 2 | 数值框架 |
| `loyaltyDecayPerMonth` | 1 | 数值框架 |
| `loyaltyThreshold` | 25 | 数值框架 |
| `rewardCostSilver` | 100 | 数值框架 |
| `rewardLoyaltyGain` | 8 | 数值框架 |
| `maxOrdersPerFactionPerMonth` | 5 | 数值框架 |

**战斗系数（新增，03 包正在等这些值）**：

| 键 | 值 | 来源 |
|---|---|---|
| `baseLoss` | 35 | 战斗设计 |
| `defenderLossFactor` | 60 | 战斗设计 |
| `siegeThresholdLoss` | 70 | 战斗设计 |
| `fieldBattleLossFactor` | 60 | 战斗设计 |
| `stalemateLossRate` | 10 | 战斗设计 |
| `armyMonthlyGrainPerSoldier` | 20 | 战斗设计 |
| `siegeDefeatRoutRate` | 100 | 战斗设计 |

**注意**：战斗系数是百分数（35 表示 35%），命名以百分数语义为准。
若 `BalanceConfig` 中的字段名与此略有不同，以 `Interfaces.cs` 的实际定义为准。

### R2.1 初始状态数据（新增，必做）

数值框架里已定稿初始配置，直接填入 `cities.json` / `officers.json`：

**势力对比**（普通难度）：

| 项 | 玩家 | 中原霸主 | 边疆异族 |
|---|---|---|---|
| 城池数 | 2 | 2 | 2 |
| 每城人口 | 3000 | 4500 | 2500 |
| 人才数量 | 3-4 | 5-6 | 4-5 |
| 平均 Command | 45 | 68 | 60 |
| 平均 Governance | 48 | 65 | 40 |
| 初始粮 | 1200 | 2600 | 1500 |
| 初始银 | 800 | 2000 | 900 |
| 初始兵源 | 300 | 800 | 600 |

**玩家的两座城必须相邻。**

**初始忠诚度**：老部下 65（一起落难的旧人）。

**难度档位**：Easy 时 AI 资源 ×1.3，Hard 时 ×0.85。

### R3 officers.json

原型阶段提供 5-8 名人才，覆盖不同 `Specialty`，让"用人"策略有实际选择空间。

**必须包含**：至少 1 名`Agriculture` 专长、1 名 `Siege`、1 名 `Logistics`、1 名高 `Command` 的猛将。

字段：Id / Name / Command / Governance / Loyalty / Age / Specialty / LoyaltyThreshold

### R4 cities.json

原型阶段 6 座城，3 个势力。必须标明 `IsCapital` 和 `AdjacentCityIds`（相邻关系决定 `Dispatch` 是否合法——**这是最容易配错的地方，必须校验**）。

### R5 events.json

至少 15 条事件，覆盖四类：民生 / 军事 / 人才 / 灾害。

每条含：`Id` / `Category` / `Probability` / `Message` / `Effect`（效果要能被 `Resolver` 消费）。

**每条事件的 `Probability` 在 0-100 之间，且同类别事件概率之和不超过 60**——否则每回合触发过多。

### R6 校验器

`ConfigValidator.Validate` 必须校验：

| 校验项 |失败信息 |
|---|---|
| 数值非负 | 列出所有负数值的路径 |
| 概率在 0-100 | 列出越界项 |
| 同类别事件概率和 ≤ 60 | 给出实际总和 |
| 人才属性在有效范围 | Command/Governance 建议 0-100 |
| 城池引用存在 | `AdjacentCityIds` 指向不存在的城 |
| 首都归属正确 | 每势力恰有一座 `IsCapital` |
| ID 唯一 | 列出重复 ID |

**返回所有问题列表，不抛异常**。由调用方决定是否中断启动。

### R7 名字生成器词库（新增，必做）

按 `01_设计规格层/命名系统/0_命名系统设计.md` 的规格建词库文件。

**交付文件**：

```
assets/data/names/
  faction_words.json      # 势力名用词≥ 60
  city_prefix.json        # 城池名前缀 ≥ 40
  city_suffix.json        # 城池名后缀 ≥ 25
  officer_surname.json    # 姓氏 ≥ 80
  officer_given.json      # 名字 ≥ 60
  specialty_title.json    # 专长称号 ≥ 30
```

**命名风格约束**（违反即退回）：
- **禁止**真实历史地名、真实人物名、有现实指向的国名
- **禁止**现代词汇
- 允许古风地名（山川、关隘、地形命名法）

**实现要求**：
- 注入 `IRandom`，同 Seed 产出完全相同
- 固定 Seed 下 1000 次调用零重复（单元测试验证）
- 禁用 `Guid.NewGuid()` / `DateTime.Now` / `Random.Shared`

**占位名规范**：数据文件里的名字可以先用占位名 `City_01`，但必须带 `TODO(命名)` 标记，便于日后全局替换。

**玩家命名校验**：实现 `NamingRules.ValidatePlayerName`（已在契约里定义）。
玩家可命名自己的势力、君主、招揽到的人才；**城池和 AI 势力不可命名**。

---

## 验收标准（DoD）

### 配置数据

- [ ] 七个 JSON 文件齐全，UTF-8，含 `_comment` 说明
- [ ] `config.json` 包含全部 20 个系数，数值与本文档表格一致
- [ ] 所有 JSON 可被 `JsonConfigLoader` 成功加载
- [ ] 故意写入一个非法数据（如负数资源），校验器能检出并给出可读错误信息
- [ ] 故意破坏城池相邻关系（指向不存在的城），校验器能检出
- [ ] 单元测试：加载→校验→再序列化，往返后数据完全一致
- [ ] 数值全部有`_comment` 说明，无裸数字
- [ ] 6 座城分属 3 势力，相邻关系对称
- [ ] **玩家的两座城相邻**
- [ ] **玩家初始人才 Command/Governance 在 40-55 区间**（平庸是玩法要求，不是随便填）
- [ ] 5 名以上人才，专长覆盖至少 4 种
- [ ] 老部下忠诚度为 65

### 名字生成器

- [ ] 六个词库文件齐全，规模达到 R7 要求的最低值
- [ ] 无真实历史地名、人物名、政治敏感词
- [ ] 固定 Seed 下 1000 次调用零重复（单元测试验证）
- [ ] 同 Seed 两次生成结果完全相同（单元测试验证）
- [ ] 占位名全部带 `TODO(命名)` 标记

## 禁止事项

- 禁止使用字符串 ID
- 禁止使用浮点数存资源数量（用 int，粮草几千石的浮点累加会积累误差）
- 禁止在 JSON 里放逻辑表达式或函数指针（只能放数据，逻辑在 Resolver 里）
- 禁止静默容错——数据非法必须报错，不能用默认值蒙混过关
- 禁止遗漏 `_comment`——半年后没人记得这个数为什么是这个数
- **禁止在词库里使用真实历史地名或真实人物名**
- 禁止使用 `Guid.NewGuid()` / `DateTime.Now` / `Random.Shared` 做名字生成