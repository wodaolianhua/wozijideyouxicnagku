#!/usr/bin/env bash
# 契约分发脚本（挖矿游戏）
#
# 作用：把 MiningGame.Contracts/ 的真实契约文件同步到各执行包目录。
# 为什么需要：每个执行者只读自己那一个文件夹，看不到契约工程。
#
# 使用：契约变更后运行 bash 分发契约.sh
# 编译自检：见脚本末尾提示，务必用 PowerShell 跑 dotnet（Git Bash 跑 dotnet 必崩）。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/MiningGame.Contracts"

if [ ! -d "$SRC" ]; then
  echo "错误：找不到契约工程 $SRC" >&2
  exit 1
fi

emit() {
  local dir="$1" name="$2"
  local dst="$ROOT/$dir/接口契约_共享类型"
  mkdir -p "$dst"
  cp "$SRC"/Enums.cs "$SRC"/Models.cs "$SRC"/Interfaces.cs "$SRC"/Contracts.ts "$dst/"
  cat > "$dst/README_契约使用说明.md" <<EOF
# 契约类型（只读，禁止修改）

这是 \`MiningGame.Contracts/\` 的**真实源码副本**，由 \`bash E:/挖矿游戏/分发契约.sh\` 自动同步。

## 最重要的一条

**不要手抄这些类型。** 你的项目通过 ProjectReference 直接引用契约工程：

\`\`\`xml
<ProjectReference Include="..\\..\\MiningGame.Contracts\\MiningGame.Contracts.csproj" />
\`\`\`

路径按你 csproj 实际位置调整。若手抄，本项目会变成两套类型，编译必然失败。

## 命名空间

所有类型都在 \`MiningGame.Contracts\` 命名空间下。

## 包含内容

| 文件 | 内容 |
|---|---|
| \`Enums.cs\` | MineralType / TileType / ToolKind / WorkerKind / ActionKind / Direction 及各结果码枚举 |
| \`Models.cs\` | Cell / MineGrid / Backpack / BackpackItem / GameState / DigResult / SellResult / DigOutcome / AdvanceResult 等数据结构 |
| \`Interfaces.cs\` | IMiningSimulator / IDigResolver / IMovementResolver / IBackpackPolicy / ISellResolver / IAutomationResolver 接口 + 动作语义说明 |
| \`Contracts.ts\` | 上述类型的 TypeScript 版（前端用，字段/枚举数值与 .cs 一一对应） |

## 前端特别注意（03 包）

前端是 TypeScript，直接用这份 \`Contracts.ts\`（拷进你的 src 或配置路径引用），字段和枚举数值与 .cs 严格对应。
**禁止自己另写一套前端类型**——那就是手抄，字段对不上会静默出错。

## JSON 大小写（全链统一 PascalCase，零配置）

数据 JSON、后端 API、前端解析，字段名一律 **PascalCase**（大写开头，与 .cs 属性名一模一样，例如 MinerX / CurrentTool / Backpack）。
原因：.NET System.Text.Json 默认就是 PascalCase，全链统一 = 不用任何配置、不会静默丢数据。**禁止改成 camelCase。** 枚举传数值。

## 修改规则

- **禁止**修改本目录任何文件
- **禁止**在自己的包里重新声明这里的类型
- 觉得契约设计有问题 → 提变更申请到 \`00_项目管理/变更申请/\`，不要自己改
EOF
  echo "  ✓ $dir/接口契约_共享类型/  ($name)"
}

echo "分发契约类型到各包..."
emit "01_矿物与地图数据" "矿物与地图数据"
emit "02_核心算法"      "核心算法"
emit "03_表现层_2D挖矿" "表现层"

echo ""
echo "完成。契约权威来源：MiningGame.Contracts/"
echo ""
echo "⚠️ 编译自检请用 PowerShell（不要用 Git Bash）："
echo "   cd 'E:\挖矿游戏\MiningGame.Contracts'"
echo "   & 'C:\Program Files\dotnet\dotnet.exe' build"
