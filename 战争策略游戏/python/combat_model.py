"""战斗数值模型 —— 纯函数实现，无副作用、无随机性（随机性由调用方注入的 rng 负责）。

本模块是 docs/02-战斗系统数值框架.md 的可执行版本。
所有公式与文档一一对应，改动必须同步。

单位约定：
- 兵力 STR：单个士兵的基础战力点（非人数），人数由 engaged_count 单独传入
- 士气 MOR：0~100
- 体能 STA：0~100
- 防御 DEF：0~100
- 伤害单位：HP 点，1 个士兵 = SOLDIER_HP
- 一次交战 = 一个回合（turn），不做帧级模拟
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, Tuple

# ---------------------------------------------------------------- 常量

SOLDIER_HP = 10.0
DAMAGE_SCALE = 0.115
ROUT_MORALE = 20.0# 士气低于此值即溃退
ROUT_STRENGTH = 0.25    # 战力剩余比例低于此值即溃散（与士气溃退并列的第二条溃退条件）
TURN_SECONDS = 10.0     # 一个回合代表 10 秒实时战斗

ARMY_CLASS = ("infantry", "archer", "cavalry")


# ---------------------------------------------------------------- 数据结构


@dataclass(frozen=True)
class Soldier:
    """单个士兵的静态属性。数值区间见文档 §3。"""

    cls: str
    name: str
    str_: float   # 兵力（战力点）
    mor: float    # 士气
    sta: float    # 体能
    base_hit: float  # 基础命中率（0~1）
    deff: float   # 防御（0~100）
    cost: int     # 征兵成本（人口）


@dataclass(frozen=True)
class Terrain:
    name: str
    move_cost: float  # 行军消耗倍率
    hit_mod: float    # 命中率修正（乘算）
    per_class: Dict[str, float]  # 各兵种在该地形上的效率系数


# ---------------------------------------------------------------- 兵种基础值

SOLDIERS: Dict[str, Soldier] = {
    "infantry": Soldier(
        cls="infantry",
        name="步兵",
        str_=10.0,
        mor=60.0,
        sta=50.0,
        base_hit=0.45,
        deff=8.0,
        cost=1,
    ),
    "archer": Soldier(
        cls="archer",
        name="弓兵",
        str_=7.0,
        mor=52.0,
        sta=40.0,
        base_hit=0.55,
        deff=5.0,
        cost=1,
    ),
    "cavalry": Soldier(
        cls="cavalry",
        name="骑兵",
        str_=12.0,
        mor=72.0,
        sta=80.0,
        base_hit=0.40,
        deff=7.0,
        cost=2,
    ),
}


# ---------------------------------------------------------------- 地形

TERRAINS: Dict[str, Terrain] = {
    "plain": Terrain(
        name="平原",
        move_cost=1.0,
        hit_mod=1.00,
        per_class={"infantry": 1.00, "archer": 1.00, "cavalry": 1.00},
    ),
    "forest": Terrain(
        name="林地",
        move_cost=1.4,
        hit_mod=0.95,
        per_class={"infantry": 0.95, "archer": 1.25, "cavalry": 0.80},
    ),
    "hill": Terrain(
        name="山地",
        move_cost=1.8,
        hit_mod=0.90,
        per_class={"infantry": 1.10, "archer": 0.90, "cavalry": 0.60},
    ),
    "river": Terrain(
        name="河谷",
        move_cost=1.5,
        hit_mod=0.95,
        per_class={"infantry": 1.00, "archer": 1.00, "cavalry": 0.90},
    ),
    "fort": Terrain(
        name="城池",
        move_cost=2.5,
        hit_mod=0.85,
        per_class={"infantry": 1.00, "archer": 1.00, "cavalry": 0.70},
    ),
}


# ---------------------------------------------------------------- 兵种克制

#: 三角克制：COUNTER[攻方][守方] = 该组合下攻方获得的效率系数（1.00 或 1.30）
COUNTER: Dict[str, Dict[str, float]] = {
    "infantry": {"infantry": 1.00, "archer": 1.00, "cavalry": 1.30},
    "archer":   {"infantry": 1.30, "archer": 1.00, "cavalry": 1.00},
    "cavalry":  {"infantry": 1.00, "archer": 1.30, "cavalry": 1.00},
}


# ---------------------------------------------------------------- 核心公式


def effective_power(
    soldier: Soldier,
    terrain: Terrain,
    general_bonus: float = 1.0,
) -> float:
    """单兵有效战斗力（输出侧）。

        有效战斗力 = 兵力 × (1 + 士气/100) × (1 + 体能/200) × 地形系数 × 将领加成

    兵种系数不在此函数内，因为需要知道对手是谁。
    """
    mor_factor = 1.0 + soldier.mor / 100.0
    sta_factor = 1.0 + soldier.sta / 200.0
    terrain_factor = terrain.per_class[soldier.cls]
    return soldier.str_ * mor_factor * sta_factor * terrain_factor * general_bonus


def hit_rate(attacker: Soldier, terrain: Terrain) -> float:
    """命中率（只取决于攻方能力与地形，不含守方状态）。

        命中率 = 基础命中率 × 地形修正

    士气不进入命中率——守方士气高应该是「受到的伤害低」（见 damage_mitigation），
    而不是「打不中」。若士气同时压低命中率，会与减伤重复计算，导致高士气兵种
    抵消掉全部克制收益（v0.1 首版测试实测缺陷）。

    结果夹在 [0.05, 0.95] 防止出现 0 命中或必中。
    """
    raw = attacker.base_hit * terrain.hit_mod
    return max(0.05, min(0.95, raw))


def damage_mitigation(defender: Soldier) -> float:
    """守方减伤系数（守方状态只在这里生效）。

        减伤 = (1 - 防御/200) × (1 - 士气/200)
    """
    return (1.0 - defender.deff / 200.0) * (1.0 - defender.mor / 200.0)


def counter_factor(attacker_cls: str, defender_cls: str) -> float:
    """兵种系数。

    单一来源派生，不独立维护第二张表（v0.1 首版同时维护 COUNTER 与 COUNTERED_BY
    两张表，导致同一组合被重复计算，实测使克制净收益只剩 4%）：
    - 攻方克守方：取 COUNTER[攻方][守方]（1.30）
    - 攻方被守方克：取其倒数（1 / 1.30 ≈ 0.769）
    - 同类或无关系：1.00
    """
    gain = COUNTER[attacker_cls][defender_cls]
    if gain > 1.0:
        return gain
    reverse = COUNTER[defender_cls][attacker_cls]
    if reverse > 1.0:
        return 1.0 / reverse
    return 1.0


def damage_per_turn(
    attacker: Soldier,
    defender: Soldier,
    terrain: Terrain,
    general_bonus: float = 1.0,
) -> float:
    """单个攻方士兵对单个守方士兵一回合造成的 HP 伤害。

        每回合伤害 = 有效战斗力 × 兵种系数 × 命中率 × 守方减伤 × DAMAGE_SCALE

    注意：此返回值是「单兵口径」，与参战人数无关。批量结算由 casualty_rate()
    × 人数 完成——把人数混进单兵公式会导致线性量纲混乱。
    """
    eff = effective_power(attacker, terrain, general_bonus)
    eff *= counter_factor(attacker.cls, defender.cls)
    hit = hit_rate(attacker, terrain)
    mitig = damage_mitigation(defender)
    return eff * hit * mitig * DAMAGE_SCALE


def casualty_rate(
    attacker: Soldier,
    defender: Soldier,
    terrain: Terrain,
    general_bonus: float = 1.0,
) -> float:
    """一回合内单个守方士兵的伤亡比例（0~1）。批量结算用这个。"""
    return damage_per_turn(attacker, defender, terrain, general_bonus) / SOLDIER_HP


def turns_to_kill(
    attacker: Soldier,
    defender: Soldier,
    terrain: Terrain,
    general_bonus: float = 1.0,
) -> float:
    """单兵对单兵打掉对方需要几个回合。"""
    per_turn = damage_per_turn(attacker, defender, terrain, general_bonus)
    if per_turn <= 0:
        return float("inf")
    return SOLDIER_HP / per_turn


def is_routed(morale: float) -> bool:
    """士气是否已跌破溃退阈值。"""
    return morale < ROUT_MORALE


def morale_after_combat(
    morale: float,
    casualty: float,
    won: bool,
    general_morale_bonus: float = 0.0,
) -> float:
    """一轮交战后的士气变化。

    规则（文档 §4.5）：
    - 每损失 10% 兵力，士气 -3
    - 获胜 +5，失败 -12（败绩对士气的打击大于胜利的激励）
    - 结果夹在 [0, 100]
    """
    delta = -casualty * 30.0
    delta += 5.0 if won else -12.0
    delta += general_morale_bonus
    return max(0.0, min(100.0, morale + delta))


def march_cost(distance: float, terrain: Terrain, troops: float) -> float:
    """行军消耗的粮。distance 单位为公里，troops 为人数。

        粮耗 = 距离 × 地形行军倍率 × 人数 / 1000
    """
    if troops <= 0:
        return 0.0
    return distance * terrain.move_cost * troops / 1000.0


def siege_modifier(besieger_cls: str, fort_level: int) -> float:
    """攻城时攻方的效率折扣。fort_level 1~5。"""
    level = max(1, min(5, fort_level))
    base = 1.0 - 0.12 * (level - 1)
    if besieger_cls == "cavalry":
        base *= 0.85
    return max(0.20, base)


def supply_capacity(surplus_food: float, troops: float) -> bool:
    """判断一支部队能否就地取给（不依赖行军粮耗）。

        每 1000 人需要 10 单位粮的日耗。
    """
    return surplus_food >= troops / 1000.0 * 10.0


def resolve_duel(
    attacker: Soldier,
    defender: Soldier,
    terrain: Terrain,
    attacker_count: int,
    defender_count: int,
    max_turns: int = 300,
    general_bonus: float = 1.0,
) -> Dict[str, object]:
    """确定性逐回合结算一场部队对轰（不引入随机数，同输入必同输出）。

    结算规则：
    - 每回合双方同时造成伤害，伤害量 = 单兵伤害 × 自身存活人数
    - 兵力比例低于 ROUT_STRENGTH 即溃散，战斗结束
    - 双方同时溃散则判为惨败（attacker_wiped=True 且 defender_wiped=True）

    返回：{winner, turns, attacker_left, defender_left, seconds}
    """
    a_rate = casualty_rate(attacker, defender, terrain, general_bonus)
    d_rate = casualty_rate(defender, attacker, terrain, general_bonus)

    a_left = float(attacker_count)
    d_left = float(defender_count)
    a_start = float(attacker_count) or 1.0
    d_start = float(defender_count) or 1.0
    turns = 0

    while turns < max_turns:
        if a_left / a_start < ROUT_STRENGTH or d_left / d_start < ROUT_STRENGTH:
            break
        # 同时结算：双方伤亡都按本回合开始时的存活人数计算
        a_casualties = d_rate * d_left
        d_casualties = a_rate * a_left
        a_left = max(0.0, a_left - a_casualties)
        d_left = max(0.0, d_left - d_casualties)
        turns += 1

    ratio_a = a_left / a_start
    ratio_d = d_left / d_start
    a_out = ratio_a < ROUT_STRENGTH
    d_out = ratio_d < ROUT_STRENGTH

    if a_out and d_out:
        winner = "draw"
    elif d_out:
        winner = "attacker"
    elif a_out:
        winner = "defender"
    else:
        winner = "timeout"

    return {
        "winner": winner,
        "turns": turns,
        "attacker_left": round(a_left, 2),
        "defender_left": round(d_left, 2),
        "seconds": turns * TURN_SECONDS,
        "attacker_routed": a_out,
        "defender_routed": d_out,
    }


# ---------------------------------------------------------------- 便捷查询


def soldier(cls: str) -> Soldier:
    if cls not in SOLDIERS:
        raise KeyError(f"未知兵种: {cls}")
    return SOLDIERS[cls]


def terrain(name: str) -> Terrain:
    if name not in TERRAINS:
        raise KeyError(f"未知地形: {name}")
    return TERRAINS[name]


def counter_matrix() -> Tuple[Tuple[str, ...], Tuple[Tuple[float, ...], ...]]:
    """返回 3x3 兵种系数矩阵（行=攻方，列=守方）。"""
    rows = ARMY_CLASS
    vals = tuple(
        tuple(counter_factor(a, d) for d in ARMY_CLASS) for a in ARMY_CLASS
    )
    return rows, vals


def casualty_matrix(tname: str) -> Tuple[Tuple[str, ...], Tuple[Tuple[float, ...], ...]]:
    """给定地形，返回 3x3 单回合伤亡比例矩阵（行=攻方，列=守方）。"""
    terr = terrain(tname)
    rows = ARMY_CLASS
    vals = tuple(
        tuple(casualty_rate(soldier(a), soldier(d), terr) for d in ARMY_CLASS)
        for a in ARMY_CLASS
    )
    return rows, vals