# 02 内容数据层 · 交付说明

数值数据与校验系统已建成。本文说明交付了什么、怎么用、有哪些待办。

---

## 一、怎么用

### 策划改数值

直接改 `assets/data/*.json`，**不需要找程序员、不需要重新编译**。

改完跑一条命令确认没写错：

```bash
dotnet run --project tools/ConfigCheck
```

输出会告诉你哪个文件、哪个字段、什么问题。退出码 1 表示数据非法，禁止启动。

### 程序员接入

```csharp
var loader = new JsonConfigLoader("assets/data");
var data = loader.LoadAll();

var result = ConfigValidator.Validate(data);
if (result.HasErrors)
{
    Console.Error.WriteLine(result.ToReport());
    return;   // 由调用方决定是否中断启动
}
```

`ConfigValidator.Validate` **不抛异常**，一次返回全部问题。策划改错三处，你看到三条错误，而不是改一处跑一次。

### 数据文件格式

除 `config.json` 外一律是信封结构：

```json
{
  "_comment": "这个文件是干什么的",
  "_todos": ["TODO(数值): 待定稿的部分"],
  "cities": [ ... ],
  "cityStates": [ ... ]
}
```

- `_comment` / `_todos` / `_c_xxx` 是元信息，反序列化时自动跳过，但**校验器会强制检查它们在不在**。
- 缺少 `_comment` 会被判为错误——半年后没人记得这个数为什么是这个数。

---

## 二、交付清单

### 数据文件（`assets/data/`）

| 文件 | 内容 |
|---|---|
| `config.json` | 全局平衡参数（万分比整数刻度）+ 专长加成表 + `extendedBalance` |
| `officers.json` | 人才初始表 6 名 + `officerStates`（归属势力与任职） |
| `cities.json` | 城池 6 座 + `cityStates`（防御、人口上限、等级） |
| `events.json` | 事件库 18 条（整数 Id），覆盖 5 个类别 |
| `specialties.json` | 特质效果表 6 条，覆盖全部专长枚举 |
| `factions.json` | 势力 3 家 + 初始资源 |
| `difficulty.json` | 难度三档系数 |

### 名字词库（`assets/data/names/`）

R7 要求。词库是**数据不是代码**，策划自行加词不需要重编译。

| 文件 | 内容 | 规模 |
|---|---|---|
| `faction_words.json` | 势力名用形容词 / 名词 | 66 / 69 |
| `city_prefix.json` | 城池名前缀 / 后缀 | 51 / 34 |
| `officer_surname.json` | 人才姓 | 222 |
| `officer_given.json` | 人才名（名） | 121 |
| `ruler_given.json` | 君主名（名） | 91 |
| `specialty_title.json` | 按专长分组的称号 + 方言 | 5×8 + None×3，方言 38 |

全部为自造架空词（世界观架空，见设计决策 D13）：不含真实历史地名与真实历史人物名。

### 代码

```
src/StrategyGame.Contracts/     契约编译工程（链接引用只读契约，未写入契约目录）
src/StrategyGame.Data/Config/
  ConfigModels.cs本包补充的类型（SpecialtyTemplate / CityStateData / ExtendedBalance / NameWordBank 等）
  ValidationResult.cs           校验问题与结果模型
  JsonConfigLoader.cs           IConfigLoader 的 JSON 实现
  ConfigValidator.cs            校验器本体
  NameGenerator.cs              INameGenerator 实现 + DeterministicRandom（xorshift32）
tests/StrategyGame.Data.Tests/  97 个单元测试
tools/ConfigCheck/              命令行校验工具
tools/make_fixtures.py          坏数据样本生成脚本（16 个样本）
tools/json_lint.py              JSON 语法预检（秒级反馈，不需编译）
```

---

## 三、刻度约定（契约 v1.2）

**所有比率、系数、概率一律为万分比整数：10000 = 100%。禁小数、禁百分数。**

例：`grainPerPopPerMonth = 800` 表示每人每月消耗 8% 口粮。

---

## 四、校验规则

校验器覆盖 30+ 类问题，全部返回中文可读错误，定位到文件+字段+实际值。

### 结构性硬规则

| 规则 | 行为 |
|---|---|
| 相邻关系对称 | A 含 B 则 B 必须含 A。不对称时报错并**同时指出两端**，因为单向通道会让「能打别人、别人打不进来」 |
| 每势力恰好一座首都 | 缺首都报错、多首都报错，并与 `factions.json` 的 `capitalCityId` 双向交叉校验 |
| **玩家势力城池数 == 配置期望值** | 期望值从 `config.json` 的 `extendedBalance.expectedPlayerCityCount` 读，**不写死**。当前为 2 |
| **玩家两城必须相邻** | 玩家两座城若互不相邻，等于两块各自独立的飞地，报错 |
| 所有实体 Id 为正整数 | 城池 / 人才 / 势力 / 特质 / 事件 一律 `id > 0` |

### 数值规则

- 数值非负
- **万分比字段落在 0–10000**；`0 < value < 100` 时额外告警「看起来像是把百分数当成了万分比」
- **同类别事件概率和 ≤ 6000**（万分比），超限报错并列出明细
- `baseKill` 必须存在且 > 0（契约 v1.2 新增的守方被歼公式）
- 事件概率落在 0–10000
- 人才属性 0–100、年龄 16–80
- **玩家人才 Command/Governance 必须在 40–55**（玩法设计要求 D14，不是随手填）

### 词库规则

- 6 个词库文件必须存在
- 各词库规模不低于 `NameWordBank.Minimums` 的下限
- 词条无空白项、无重复
- 称号必须覆盖全部 `OfficerSpecialty` 枚举值，键名合法

### 兼容与告警

- **废弃字段**（`defenderLossFactor` / `stalemateLossRate`）> 0 时报 **Warning**（不是 Error），提示应迁移到 v1.2 的 `baseLoss` / `baseKill` 双公式
- **浮点数文本层拦截**：反序列化前扫描原始 JSON，命中即报错
- 每个文件必须有 `_comment`；`_todos` 里的占位待办以 Info 级上报

---

## 五、当前数据

### 势力分布

| 势力 | 城池 | 人口合计 | 粮 / 银 / 兵源 |
|---|---|---|---|
| 0 玩家（没落贵族） | 1、2 | 5800 | 1200 / 800 / 300 |
| 1 中原霸主 | 3、4 | 8700 | 2600 / 2000 / 800 |
| 2 边疆异族 | 5、6 | 4700 | 1500 / 900 / 600 |

玩家的弱势体现在**资源与人才**上，不体现在人口总和上。

### 城池布局（6 城 / 3 势力）

```
        [1] 玩家都城 3000
         │
        [2] 玩家 2800
         │
    ┌────┴────┐
 [3] 中原霸主都城 4500   [4] 中原 4200
    │                        │
    └──────────┬─────────────┘
               │
        [5] 边疆都城 2500
               │
        [6] 边疆 2200
```

- 玩家起始 **2 座相邻小城（城池 1、2）**，见设计决策 D14
- 城池 1 只连城池 2；**城池 2 是玩家势力唯一通往外部的门户**，同时连通 3、4，卡在中原霸主腹地
- 相邻关系严格对称，由测试逐对验证

### 玩家初始人才（全部落在 40-55）

| Id | Command | Governance | Loyalty | 专长 | 定位 |
|---|---|---|---|---|---|
| 1 | 48 | 52 | 65 | Agriculture | 种田 |
| 2 | 55 | 44 | 65 | Siege | 攻城 |
| 3 | 43 | 50 | 65 | Logistics | 后勤（暂未任职） |

对比 AI 势力人才 Command 最高 76、政务最高 71——弱势感明确。

### 战斗系数（万分比）

| 字段 | 值 | 含义 |
|---|---|---|
| `baseLoss` | 3500 | 攻方自损 35% |
| `baseKill` | 2700 | 守方被歼 27% |
| `siegeThresholdLoss` | 7000 | 攻城越过阈值后追加损耗 |
| `fieldBattleLossFactor` | 6000 | 野战损耗系数 |
| `armyMonthlyGrainPerSoldier` | 200 | 每兵每月耗粮 2% |
| `siegeDefeatRoutRate` | 10000 | 攻城失败溃散率 |
| `defenderLossFactor` | 0 | **已废弃**，v1.2 走独立公式 |
| `stalemateLossRate` | 0 | **已废弃** |

### 事件概率分布（万分比）

| 类别 | 条数 | 概率和 |
|---|---|---|
| Economy | 4 | 3400 |
| Military | 4 | 3600 |
| Personnel | 4 | 3000 |
| Disaster | 4 | 3400 |
| Diplomacy | 2 | 2000 |

全部 ≤ 6000 上限。

事件 Id 为整数，分段约定：`1000+` 民生 / `1100+` 军事 / `1200+` 人才 / `1300+` 灾害 / `1400+` 外交。

---

## 六、契约适配说明（v1.2）

### 已解决

| 原问题 | 处理 |
|---|---|
| `BalanceConfig` 两个比率字段是 `int`，装不下 0.08 / 0.5 | **契约 v1.2 已改为万分比整数**。`grainPerPopPerMonth = 800`、`soldierPerPopPerMonth = 5000`，系数已生效 |
| `GameEventTemplate.Id` 是 `string`，与「禁止字符串 ID」冲突 | **契约 v1.2 已改为 `int`**。`events.json` 全部改为分段整数 |
| `stabilityNaturalRecovery` / `moraleNaturalRecovery` / `moraleDecayWhenPeace` / `rewardCostSilver` / `rewardLoyaltyGain` 契约无对应项 | **裁决：不扩契约**。判断标准是「03 包需要读它吗？不需要 → 不进契约」。这 5 个字段放在 02 自己的 `ExtendedBalance` 模型里，读写由本包负责 |

### 契约内字段缺失（仍未解决，绕过而非改契约）

契约 `CityTemplate` 没有 `MaxPopulation`、`OfficerTemplate` 没有 `OwnerFactionId`。这在编译期直接暴露（不是我猜的，是编译器告诉我的）。

处理：不动契约，用同文件内的旁挂数组 `cityStates` / `officerStates` 表达。运行时由 03 包组装成契约的 `City` / `Officer` 对象。

---

## 七、验证结果

```
dotnet build   →  0 个警告，0 个错误
dotnet test    →  失败: 0，通过: 97，已跳过: 0，总计: 97
```

`dotnet run --project tools/ConfigCheck` → 错误 0，警告 0，结论「数据可用」。

### 验收：故意写坏数据，校验器必须检出

16 个坏数据样本全部被检出，且错误信息可读。样本由 `tools/make_fixtures.py` 从当前 `assets/data` 全量重建（完整合法数据 + 注入一处错误），走真实加载链路：

| 样本 | 注入的错误 | 结果 |
|---|---|---|
| `bad_negative` | 粮草 1200 → **-500** | 检出：数值为 -500，不允许为负数 |
| `bad_prob_over` | 概率 1200 → **10100** | 检出：触发概率为 10100，合法区间是 0-10000 |
| `bad_prob_sum` | Economy 一条概率 → **5500** | 检出：类别 Economy 概率之和为 7700，超过上限 6000 |
| `bad_adj_symmetric` | 城池 5 移除相邻城池 4 | 检出：相邻关系不对称，同时指出两端 |
| `bad_adj_missing` | 加入不存在的城 **999** | 检出：相邻列表指向不存在的城池 Id 999 |
| `bad_no_capital` | 势力没有首都 | 检出：势力 1 名下没有首都 |
| `bad_two_capital` | 一个势力两座首都 | 检出：势力 1 名下有 2 座首都 |
| `bad_strong_player` | 玩家人才 Command 48 → **88** | 检出：超出玩家人才上限 55 |
| **`bad_player_one_city`** | 玩家只剩城池 1 | 检出：玩家势力城池数为 1，期望 2 |
| `bad_player_not_adjacent` | 玩家两城拆开 | 检出：玩家两座城池不相邻 |
| `bad_base_kill` | `baseKill` → **0** | 检出：守方被歼基准系数必须大于 0 |
| `bad_ratio_scale` | `baseLoss` 3500 → **35** | 告警：看起来像是把百分数当成了万分比（Warning，非 Error） |
| `bad_deprecated_field` | `defenderLossFactor` → **5000** | 告警：废弃字段应置 0（Warning，非 Error） |
| `bad_event_id_zero` | 事件 Id → **0** | 检出：事件 Id 必须是正整数 |
| `bad_name_bank_small` | 删掉大部分姓氏 | 检出：姓氏词库规模不足 |
| `bad_name_bank_dup` | 塞入重复词条 | 检出：词库存在重复项 |

### 名字生成器确定性

同 Seed 两次生成逐字一致（4 种生成方法各 21 个 id）；不同 Seed 产出不同；`GetState` / `SetState` 可存档恢复；1000 城池名 / 1000 人才名 / 1000 势力名**零重复**。

### 过程记录：校验器抓到了真实 bug

把城池 2 划归中原霸主后，我漏改了城池 3 的相邻列表。是校验器报出来的：

```
[错误] 相邻关系不对称：城池 3（City_03）把 1（City_01）列为相邻，
       但 1（City_01）的 adjacentCityIds 里没有 3。
```

靠肉眼绝不会发现这类错误——这正是相邻对称性校验存在的意义。

此外单元测试还抓出我自己写的 `ValidationResult.Error()` 参数顺序写反（location 与 message 传反），已修复。

---

## 八、未完成项

### 数值全部为占位

01_设计规格层的数值框架尚未定稿，所有数值带 `TODO(数值)` 标记。定稿后策划直接改 JSON 即可，无需改代码。

城池名 / 人才名 / 势力名仍是 `City_01` 形式的占位名，词库已就绪，接入 `INameGenerator` 后全局替换即可。

### 未纳入的设计

- 事件 `Effects` 的键名约定（`grain`/`silver`/`morale`/`stability`/`loyalty`）需与 03 包 Resolver 对齐
- `specialties.json` 的 `bonusPercent` 与 `config.json` 的 `specialtyBonus` 描述同一套加成，目前以 `config.json` 为运行时真值，两处需同步改