# 00 号裁决：士气三分支的「参战信号」用 HasEngagedThisMonth（契约 v1.4）

日期：2026-10-23
签发：00 号（大脑）
接收：03 规则内核层执行 AI
状态：**已生效**。契约已由 00 改完、编译通过（0 错误 0 警告）、已分发到全部 7 个包。本单是《提示词_第2轮补丁.md》的**权威补充与勘误**，与补丁提示词冲突处，以本单为准。

---

## 一、你卡住的那个问题，裁决如下

你发现的时序歧义是真实存在的，发现得对，但正确动作是**停下上报**，不是无限读文件。歧义内容：

- 流水线第 3 步结士气，第 7 步才打仗；战斗时把 `MonthsWithoutBattle` 清 0。
- 第 3 步看到 0，既可能是「上月刚打仗」，也可能是「新部队开局默认 0」——无法区分。

**裁决：不靠计数器推断参战，新增独立月度标记 `Army.HasEngagedThisMonth`（bool）。** 与现有 `HasRestedThisMonth` / `HasMutiniedThisMonth` 是同一套模式。契约改动由 00 完成，你不许碰契约。

### 标记生命周期（11 步对齐，照此实现）

| 时机 | 动作 |
|---|---|
| 新设部队 | `HasEngagedThisMonth = false`（默认值，开局第一次结算=未参战，走恢复分支）|
| DispatchResolver 第 7 步战斗发生时 | 与「MonthsWithoutBattle 清零」同一个位置，给**攻方部队**补发 delta 置 `true` |
| MoraleSystem 次月第 3 步结算时 | 读它：true = 参战周期；**读完发 delta 置回 false**（消费即清）|
| 连续作战 | 第 7 步又置 true，每月照常被识别为参战周期 |

**范围明确**：只给攻方部队置位，与当前 MonthsWithoutBattle 清零范围完全一致（现实现也只清攻方）。守方不扩大——守方参战信号是未来设计议题，本轮不做，不许自行发挥。

## 二、你要改的文件（03 包内，共 5 处 + 测试）

1. **src/Types.cs**：在 DeltaField 字符串常量区（`ArmyMonthsWithoutBattle` 旁）加
   `public const string ArmyHasEngagedThisMonth = "HasEngagedThisMonth";`
2. **StateApplier**：照 `ArmyHasMutiniedThisMonth`（bool）的现有写法，加新字段的 bool 写回分支。
3. **src/Resolvers/DispatchResolver.cs**（约 L228-232 清零处）：紧挨清零 delta，补发
   `result.WithDelta(DeltaTarget.Army, army.Id, DeltaField.ArmyHasEngagedThisMonth, army.HasEngagedThisMonth ? 1 : 0, 1);`
   （按本文件现有 bool delta 的 0/1 惯例写，先看同文件 HasRestedThisMonth 怎么表达。）
4. **src/Systems/MoraleSystem.cs**：`ResolveMonthlyMorale` 改为三分支，顺序如下：

```
对每个 inBattle 部队：
  if (army.HasEngagedThisMonth)            // 参战周期
      士气：不恢复、不腐化（本月净结算变化 = 0，战斗士气增减已在战斗时由 Resolver 结过）
      MonthsWithoutBattle：保持 0，不 +1
      发 delta：HasEngagedThisMonth 1 → 0（消费清除）
  else:
      m = army.MonthsWithoutBattle + 1
      if (m < 4)   morale += 3             // 恢复：第1/2/3个无战月
      else         morale -= 6             // 腐化：第4个无战月起，只扣6，不叠加+3（互斥！）
      发 MonthsWithoutBattle → m 的 delta
  低士气哗变判定：两个分支都照跑（参战月也可能因战损低士气哗变），逻辑不变
```

要点：**先读 flag 再决定要不要 +1 计数器**；腐化月绝不能再带 +3（这是补丁核心）；文件头注释 L54-58 的旧三步描述同步改成三分支。

5. **src/Resolvers/DispatchResolver.cs 围困返回码**：按补丁提示词第三节，旧的被 v1.3 契约替换的返回码改为 `RejectedTargetSieged`（=10），含注释。
6. **TODO(05B) 注释**：按补丁提示词补上。

## 三、4 个新测试（勘误补丁提示词的算术错误）

补丁提示词测试2写「60+3+3+3−6=57」——**算术错误，正确结果是 63**（60+9−6=63）。按 63 断言，并在汇报里注明本勘误。

- **T1 参战月不恢复**：直接调 MoraleSystem，构造部队连续 3 次结算前 `HasEngagedThisMonth=true`、士气 50。断言：每次结算士气不变（50）、flag 被清回 false、计数器保持 0。战斗本身的 −3 是 DispatchResolver 的产出，不属本测试；如要覆盖 50→41 的端到端，另写一个经 DispatchResolver 攻城未决（僵持）+ 月度结算的流水线测试，僵持战损士气 −3 共 3 次 = 41。
- **T2 连续未参战 4 月开始腐化**：flag 全程 false，计数器初始 0，士气 60。逐月：+3、+3、+3（计数器 1/2/3），第 4 月计数器 4 → **只 −6，不 +3**，末值 **63**。
- **T3 战/休交替（原「12 月末<57」作废）**：经实算，交替场景下休整月既有休整 +3 又有月度恢复 +3（计数器战后为 0，休整月 m=1<4），士气是**上升**的，原提示词的「<57」在正确规则下数学上不可能成立，不许为凑数篡改规则。改为断言逐月事实序列：参战后结算月净变化 0、休整月结算 +3、计数器永不达到 4（永不腐化），末值按公式实算后写死。**此差异必须在汇报中写明。**
- **T4 恢复与腐化不叠加**：对 T1/T2/T3 的全部结算输出检查断言——同一次结算中，同一部队不存在「既 +3 又 −6」的 delta 组合。

既有测试里凡依赖旧「无条件 +3、腐化月再叠加 −6」行为的断言，按新规则更新期望值（这是规则变更的正常跟随，不是放宽标准；改完在汇报中列出改了哪几条、为什么）。

## 四、环境与纪律

- dotnet build / dotnet test **一律 PowerShell**，Git Bash 会崩（MSBuild 子节点问题，02 已踩过）。工程根无 .sln，直接针对 tests 工程跑。
- 不许再读 01/02 文档做「语义对齐」——本单已经把语义钉死，继续读即视为空转。
- 不许改契约目录、不改设计文档；发现新歧义，写入汇报等 00 裁决。
- 完成后：build 0 错误 0 警告，测试总数应为 **189（185 旧 + 4 新）全过**；把完整汇报写入 `03_规则内核层/补丁汇报.md`，内容含：测试末行输出、改动文件清单、被更新的旧断言清单、T3 差异说明、任何遗留疑问。

收到本单后直接开工，不再讨论。
