namespace StrategyGame.Contracts;

// 以下接口是 02 核心算法包必须实现的对外契约。
// 03 表现层只依赖这些接口，不依赖具体实现。

public interface IPathFinder
{
    // 单点终点：路网上最短路径（按 Edge.Cost 加权）。返回节点 Id 序列，含起点和终点。
    // 不可达返回空列表，不抛异常。
    List<string> FindPath(WorldMap map, string fromNodeId, string toNodeId);

    // 多点路径：按 waypoints 顺序逐段求路并拼接，返回完整节点 Id 序列。
    List<string> FindPathVia(WorldMap map, string fromNodeId, IReadOnlyList<string> waypoints);
}

public interface IMarchSimulator
{
    // 推进整局 steps 个时间步，产出新状态 + 轨迹 + 遭遇事件。纯函数：不改入参 state。
    // orders = 本步新下的命令（可为空列表）：
    //   - 非空：先把 Order 转成对应 Army 的 PlannedPath/PathIndex（寻路结果），再推进；
    //   - 为空：让已有 PlannedPath 的部队继续沿路径走，无需重复下令。
    // 遭遇若触发：内部调用 ILossResolver 结算，把 LossResult 填进 EncounterEvent.Loss。
    AdvanceResult Advance(WorldState state, IReadOnlyList<Order> orders, int steps, int seed);

    // 快进：推进到"下一个遭遇事件发生的时刻"或"maxSteps 步"为止（先到者为准）。
    // orders 语义同 Advance。停在遭遇点时，该事件的 Loss 同样必须已填好。
    AdvanceResult FastForward(WorldState state, IReadOnlyList<Order> orders, int maxSteps, int seed);
}

public interface IEncounterDetector
{
    // 在给定状态下检测遭遇。返回本步触发的遭遇事件列表。
    List<EncounterEvent> Detect(WorldState state, int seed);
}

public interface ILossResolver
{
    // 给定一次遭遇，算出各部队损失兵力。纯函数、确定性。
    LossResult Resolve(EncounterEvent encounter, WorldState state, int seed);
}

public interface IVisibilityPolicy
{
    // 敌军是否对观察者可见。第一阶段恒 true；战争迷雾阶段再换实现。
    bool IsEnemyVisible(Army enemyArmy, Army observer, WorldState state);
}
