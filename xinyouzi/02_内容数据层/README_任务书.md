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

关键字段：`GrainPerPopPerMonth`、`SoldierPerPopPerMonth`、`LoyaltyThreshold`、`SpecialtyBonus`。

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

## 验收标准（DoD）

- [ ] 七个 JSON 文件齐全，UTF-8，含 `_comment` 说明
- [ ] 所有 JSON 可被 `JsonConfigLoader` 成功加载
- [ ] 故意写入一个非法数据（如负数资源），校验器能检出并给出可读错误信息
- [ ] 故意破坏城池相邻关系（指向不存在的城），校验器能检出
- [ ] 单元测试：加载→校验→再序列化，往返后数据完全一致
- [ ] 数值全部有`_comment` 说明，无裸数字
- [ ] 5 名以上人才，特质覆盖至少 4 种
- [ ] 6 座城，相邻关系对称（A 邻 B 则 B 也邻 A）

## 禁止事项

- 禁止使用字符串 ID
- 禁止使用浮点数存资源数量（用 int，粮草几千石的浮点累加会积累误差）
- 禁止在 JSON 里放逻辑表达式或函数指针（只能放数据，逻辑在 Resolver 里）
- 禁止静默容错——数据非法必须报错，不能用默认值蒙混过关
- 禁止遗漏 `_comment`——半年后没人记得这个数为什么是这个数