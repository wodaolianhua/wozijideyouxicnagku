#!/usr/bin/env bash
# 契约分发脚本
#
# 作用：把 StrategyGame.Contracts/ 的真实 .cs 契约文件同步到各包目录。
# 为什么需要：每个执行者只读自己那一个文件夹，看不到契约工程。
#
# 使用：契约变更后运行 bash 分发契约.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/StrategyGame.Contracts"

if [ ! -d "$SRC" ]; then
  echo "错误：找不到契约工程 $SRC" >&2
  exit 1
fi

emit() {
  local dir="$1" name="$2"
  local dst="$ROOT/$dir/接口契约_共享类型"
  mkdir -p "$dst"
  # 只拷源码文件，排除 obj/bin 下的构建产物
  cp "$SRC"/Enums.cs "$SRC"/Models.cs "$SRC"/Interfaces.cs "$dst/"
  cat > "$dst/README_契约使用说明.md" <<EOF
# 契约类型（只读，禁止修改）

这是 \`StrategyGame.Contracts/\` 的**真实 .cs 源码副本**，由 \`bash E:/xinyouzi/分发契约.sh\` 自动同步。

## 最重要的一条

**不要手抄这些类型。** 你的项目通过 ProjectReference 直接引用它们：

\`\`\`xml
<ProjectReference Include="..\\..\\StrategyGame.Contracts\\StrategyGame.Contracts.csproj" />
\`\`\`

如果你手抄了，本项目会变成两套类型，编译必然失败。

## 命名空间

所有类型都在 \`StrategyGame.Contracts\` 命名空间下。文件里没写 using 是正常的。

## 包含内容

| 文件 | 内容 |
|---|---|
| \`Enums.cs\` | ActionType / ActorType / ResolveResultCode / PostType 等枚举 |
| \`Models.cs\` | WorldState / Order / City / Army / Officer / ResolveResult 等数据结构 |
| \`Interfaces.cs\` | 所有接口、API 契约、JSON 线格式、文件归属表 |

## 修改规则

- **禁止**修改本目录任何文件
- **禁止**在自己的包里重新声明这里的类型
- 觉得契约设计有问题 → 提变更申请到 \`00_项目管理/变更申请/\`，不要自己改
EOF
  echo "  ✓ $dir/接口契约_共享类型/  ($name)"
}

echo "分发契约类型到各包..."
emit "01_设计规格层" "设计规格"
emit "02_内容数据层" "内容数据"
emit "03_规则内核层" "规则内核"
emit "04_智能体层"   "智能体"
emit "05A_表现层_战略UI" "战略 UI"
emit "05B_表现层_实时战斗" "实时战斗"
emit "06_工程与验证层" "工程验证"

# 清理旧的 Markdown 副本（已被真实 .cs 取代）
find "$ROOT" -name "接口契约_核心契约副本.md" -delete 2>/dev/null || true
echo "  ✓ 清理旧的 Markdown 契约副本"

# 契约自检
echo ""
echo "契约自检..."
if command -v dotnet >/dev/null 2>&1; then
  if (cd "$SRC" && dotnet build -v quiet --nologo >/dev/null 2>&1); then
    echo "  ✓ 契约工程编译通过"
  else
    echo "  ✗ 契约工程编译失败！请检查后重新分发" >&2
    exit 1
  fi
else
  echo "  ! 未找到 dotnet，跳过编译检查"
fi

echo ""
echo "完成。契约权威来源：StrategyGame.Contracts/"
echo "组合前请执行全量构建验证：dotnet build"