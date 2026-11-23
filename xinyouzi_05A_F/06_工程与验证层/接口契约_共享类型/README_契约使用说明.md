# 契约类型（只读，禁止修改）

这是 `StrategyGame.Contracts/` 的**真实 .cs 源码副本**，由 `bash E:/xinyouzi/分发契约.sh` 自动同步。

## 最重要的一条

**不要手抄这些类型。** 你的项目通过 ProjectReference 直接引用它们：

```xml
<ProjectReference Include="..\..\StrategyGame.Contracts\StrategyGame.Contracts.csproj" />
```

如果你手抄了，本项目会变成两套类型，编译必然失败。

## 命名空间

所有类型都在 `StrategyGame.Contracts` 命名空间下。文件里没写 using 是正常的。

## 包含内容

| 文件 | 内容 |
|---|---|
| `Enums.cs` | ActionType / ActorType / ResolveResultCode / PostType 等枚举 |
| `Models.cs` | WorldState / Order / City / Army / Officer / ResolveResult 等数据结构 |
| `Interfaces.cs` | 所有接口、API 契约、JSON 线格式、文件归属表 |

## 修改规则

- **禁止**修改本目录任何文件
- **禁止**在自己的包里重新声明这里的类型
- 觉得契约设计有问题 → 提变更申请到 `00_项目管理/变更申请/`，不要自己改
