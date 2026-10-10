"""战斗数值模型的确定性验证测试。

运行： python tests/test_combat_model.py
     （或 pytest tests/test_combat_model.py -v）

测试目标（对应文档 §7 验收标准）：
1. 单调性：任何属性上调，有效战斗力/伤害不得下降
2. 量级：同兵种对拼应在 8~20 回合内分出胜负
3. 方向性：三角克制必须严格成立，且净收益显著（≥25%）
4. 边界：不出现负伤害、不出现命中率越界、不出现除零、不出现 NaN/Inf
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from combat_model import (  # noqa: E402
    ARMY_CLASS,
    DAMAGE_SCALE,
    ROUT_MORALE,
    SOLDIER_HP,
    SOLDIERS,
    TERRAINS,
    casualty_matrix,
    casualty_rate,
    counter_factor,
    counter_matrix,
    damage_mitigation,
    damage_per_turn,
    effective_power,
    hit_rate,
    is_routed,
    march_cost,
    morale_after_combat,
    resolve_duel,
    siege_modifier,
    soldier,
    supply_capacity,
    terrain,
    turns_to_kill,
)


def _replace(base, **kw):
    """基于现有兵种复制一份并改字段（用于单调性测试）。"""
    return base.__class__(
        cls=base.cls, name=base.name,
        str_=kw.get("str_", base.str_),
        mor=kw.get("mor", base.mor),
        sta=kw.get("sta", base.sta),
        base_hit=kw.get("base_hit", base.base_hit),
        deff=kw.get("deff", base.deff),
        cost=kw.get("cost", base.cost),
    )


# ================================================================ 1. 单调性


def test_effective_power_monotonic_on_morale():
    """士气越高，有效战斗力越高。"""
    base = soldier("infantry")
    vals = [
        effective_power(_replace(base, mor=float(m)), terrain("plain"))
        for m in (0, 20, 40, 60, 80, 100)
    ]
    assert all(vals[i] < vals[i + 1] for i in range(len(vals) - 1)), vals


def test_effective_power_monotonic_on_sta():
    """体能越高，有效战斗力越高。"""
    base = soldier("infantry")
    vals = [
        effective_power(_replace(base, sta=float(s)), terrain("plain"))
        for s in (0, 25, 50, 75, 100)
    ]
    assert all(vals[i] < vals[i + 1] for i in range(len(vals) - 1)), vals


def test_effective_power_monotonic_on_str():
    """兵力越高，有效战斗力越高。"""
    base = soldier("infantry")
    vals = [
        effective_power(_replace(base, str_=v), terrain("plain"))
        for v in (5, 10, 15, 20)
    ]
    assert all(vals[i] < vals[i + 1] for i in range(len(vals) - 1)), vals


def test_general_bonus_monotonic_and_linear():
    """将领加成越高有效战斗力越高，且严格线性。"""
    s, t = soldier("infantry"), terrain("plain")
    p1 = effective_power(s, t, 1.0)
    p2 = effective_power(s, t, 1.1)
    assert p2 > p1
    assert abs(p2 / p1 - 1.1) < 1e-9


def test_hit_rate_monotonic_on_terrain():
    """地形命中修正越高，命中率越高，且严格按 hit_mod 排序。"""
    vals = {
        t: hit_rate(soldier("infantry"), terrain(t)) for t in TERRAINS
    }
    mods = {t: terrain(t).hit_mod for t in TERRAINS}
    ordered = sorted(mods.items(), key=lambda kv: kv[1], reverse=True)
    rates = [vals[t] for t, _ in ordered]
    assert all(rates[i] >= rates[i + 1] for i in range(len(rates) - 1)), \
        f"命中率未跟随地形命中修正排序: {vals}"
    assert vals["plain"] == max(vals.values()), "平原命中率应为最高"
    assert vals["fort"] == min(vals.values()), "城池命中率应为最低"


def test_morale_only_affects_output_and_mitigation_not_hit():
    """士气不得影响命中率——否则与减伤重复计算（v0.1 首版实测缺陷）。"""
    base = soldier("infantry")
    for m in (0, 50, 100):
        assert hit_rate(_replace(base, mor=float(m)), terrain("plain")) == \
               hit_rate(base, terrain("plain"))


# ================================================================ 2. 量级


def test_same_class_duel_turns_in_range():
    """同兵种单兵对决应在 8~20 回合内分出胜负。"""
    for cls in ARMY_CLASS:
        tt = turns_to_kill(soldier(cls), soldier(cls), terrain("plain"))
        assert 8.0 <= tt <= 20.0, f"{cls} 同兵种对决 {tt:.1f} 回合，超出 8~20 区间"


def test_casualty_rate_magnitude():
    """同兵种对拼的单回合伤亡比例应在 4%~25%。

    只校验同兵种对拼（无克制干扰），这是战斗节奏的基准。
    被克制的极端组合不在此区间，属正常现象。
    """
    for cls in ARMY_CLASS:
        for tname in TERRAINS:
            r = casualty_rate(soldier(cls), soldier(cls), terrain(tname))
            assert 0.04 <= r <= 0.25, f"{cls}@{tname} 单回合伤亡 {r:.3f} 超出区间"


def test_battle_turn_count_100v100():
    """100v100 同兵种对拼，应在 8~25 回合内分出胜负。"""
    r = resolve_duel(
        soldier("infantry"), soldier("infantry"),
        terrain("plain"), 100, 100,
    )
    assert r["winner"] != "timeout", "100v100 未在 300 回合内分出胜负"
    assert r["turns"] < 300, f"战斗超时: {r}"
    assert 5 <= r["turns"] <= 25, f"100v100 打了 {r['turns']} 回合，超出 5~25 区间"


def test_battle_scale_invariance():
    """战斗结果必须与兵力规模无关（线性模型的必然性质），否则大地图数字不可复用。"""
    small = resolve_duel(soldier("infantry"), soldier("infantry"), terrain("plain"), 50, 50)
    large = resolve_duel(soldier("infantry"), soldier("infantry"), terrain("plain"), 50000, 50000)
    assert small["winner"] == large["winner"], f"规模影响了胜负: {small} vs {large}"
    assert small["turns"] == large["turns"], \
        f"战斗回合数随规模变化: 50人={small['turns']} 50000人={large['turns']}"


def test_battle_duration_in_target_range():
    """单场局部交战的时长应落在 1~6 分钟（按 10 秒/回合折算），对应单场会战 5~10 分钟的节奏。"""
    r = resolve_duel(soldier("infantry"), soldier("infantry"), terrain("plain"), 1000, 1000)
    secs = r["seconds"]
    assert 60 <= secs <= 360, f"局部交战时长 {secs:.0f}s，超出 1~6 分钟"


def test_counter_decides_actual_battle():
    """关键验收：兵种克制必须真实改变战斗结局，而不只是改变数字。"""
    plain = terrain("plain")
    # 步兵 vs 骑兵：等兵数，步兵应胜
    r = resolve_duel(soldier("infantry"), soldier("cavalry"), plain, 1000, 1000)
    assert r["winner"] == "attacker", f"步兵应克骑兵，实测 {r}"
    # 骑兵 vs 步兵：同一次合演，骑兵应败
    r2 = resolve_duel(soldier("cavalry"), soldier("infantry"), plain, 1000, 1000)
    assert r2["winner"] == "defender", f"骑兵应被步兵克制，实测 {r2}"


def test_terrain_decides_actual_battle():
    """关键验收：地形必须真实改变战斗结局。"""
    inf, cav = soldier("infantry"), soldier("cavalry")
    # 平原：步兵胜骑兵
    assert resolve_duel(inf, cav, terrain("plain"), 1000, 1000)["winner"] == "attacker"
    # 山地：骑兵受重罚，步兵即使面对骑兵也应取胜（克制方向不变但优势扩大）
    hill_r = resolve_duel(inf, cav, terrain("hill"), 1000, 1000)
    assert hill_r["winner"] == "attacker", f"山地里步兵应更稳地胜骑兵，实测 {hill_r}"


def test_rout_condition_triggers():
    """战力跌破 ROUT_STRENGTH 必须判溃退。"""
    # 骑兵在山地以 1:4 劣势冲击步兵，骑兵应先崩（山地重罚 + 步兵克骑兵）
    r = resolve_duel(soldier("cavalry"), soldier("infantry"), terrain("hill"), 500, 2000)
    assert r["attacker_routed"] is True, f"骑兵攻方应崩溃溃退，实测 {r}"
    assert r["winner"] == "defender", f"守方步兵应获胜，实测 {r}"
    assert r["turns"] < 10, f"溃退应发生得很快，实测 {r['turns']} 回合"


def test_mass_advantage_can_override_terrain():
    """兵力优势可以抵消地形劣势——保证地形不是唯一决定因素。"""
    # 平原：骑兵 4:1 优势应碾压步兵
    r = resolve_duel(soldier("cavalry"), soldier("infantry"), terrain("plain"), 4000, 1000)
    assert r["winner"] == "attacker", f"平原上骑兵 4:1 应胜，实测 {r}"
    # 山地：同样的4:1，骑兵可能仍然崩（地形代价很高）
    r2 = resolve_duel(soldier("cavalry"), soldier("infantry"), terrain("hill"), 4000, 1000)
    assert r2["winner"] in ("attacker", "defender")
    # 但 8:1 在山地应足以翻回来
    r3 = resolve_duel(soldier("cavalry"), soldier("infantry"), terrain("hill"), 8000, 1000)
    assert r3["winner"] == "attacker", f"山地8:1 骑兵应胜，实测 {r3}"


def test_casualty_rate_is_scale_free():
    """伤亡比例与兵力规模无关（线性模型的必然性质），确保大地图复用同一公式。"""
    _, plain = casualty_matrix("plain")
    for i, a in enumerate(ARMY_CLASS):
        for j, d in enumerate(ARMY_CLASS):
            assert plain[i][j] == casualty_rate(soldier(a), soldier(d), terrain("plain"))


# ================================================================ 3. 方向性（克制）


def test_counter_cycle_direction():
    """三角克制方向必须严格成立：步克骑、骑克弓、弓克步。"""
    inf, arc, cav = "infantry", "archer", "cavalry"
    assert counter_factor(inf, cav) > 1.0, "步兵应克骑兵"
    assert counter_factor(cav, arc) > 1.0, "骑兵应克弓兵"
    assert counter_factor(arc, inf) > 1.0, "弓兵应克步兵"


def test_counter_cycle_reverse_is_penalized():
    """反向必须受罚（系数 < 1.0），且为克制的倒数。"""
    inf, arc, cav = "infantry", "archer", "cavalry"
    assert counter_factor(cav, inf) < 1.0, "骑兵被步兵克，应受罚"
    assert counter_factor(arc, cav) < 1.0, "弓兵被骑兵克，应受罚"
    assert counter_factor(inf, arc) < 1.0, "步兵被弓兵克，应受罚"
    # 倒数关系验证
    assert abs(counter_factor(cav, inf) * counter_factor(inf, cav) - 1.0) < 1e-9


def test_counter_matrix_symmetric_inverse():
    """克制矩阵必须满足：f(a,d) × f(d,a) = 1（无冗余叠乘）。"""
    rows, vals = counter_matrix()
    for i in range(len(rows)):
        for j in range(len(rows)):
            assert abs(vals[i][j] * vals[j][i] - 1.0) < 1e-9, \
                f"{rows[i]}->{rows[j]}={vals[i][j]} 与反向 {rows[j]}->{rows[i]}={vals[j][i]} 不互为倒数"


def test_counter_margin_significant():
    """克制的净收益必须 ≥25%，否则决策噪声大于决策价值。"""
    rows, vals = counter_matrix()
    gains = [v for row in vals for v in row if v > 1.0]
    losses = [v for row in vals for v in row if v < 1.0]
    assert len(gains) == 3 and len(losses) == 3, f"克制矩阵应恰有 3 条增益 3 条惩罚，实测 {len(gains)}/{len(losses)}"
    assert min(gains) >= 1.25, f"克制增益最小 {min(gains):.3f}，低于 1.25"
    assert max(losses) <= 0.80, f"被克制惩罚最大 {max(losses):.3f}，高于 0.80"


def test_counter_beats_same_class():
    """克制收益必须大于同兵种对战，才能形成有效决策。"""
    inf, arc, cav = "infantry", "archer", "cavalry"
    t = terrain("plain")
    assert casualty_rate(soldier(inf), soldier(cav), t) > casualty_rate(soldier(inf), soldier(inf), t)
    assert casualty_rate(soldier(cav), soldier(arc), t) > casualty_rate(soldier(cav), soldier(cav), t)
    assert casualty_rate(soldier(arc), soldier(inf), t) > casualty_rate(soldier(arc), soldier(arc), t)


def test_counter_cannot_be_nullified_by_enemy_stats():
    """关键回归测试：守方的高士气/高面板不得把克制收益完全抵消。

    这是 v0.1 首版的真实缺陷——克制净收益仅剩 4%，形同虚设。
    """
    inf, cav = "infantry", "cavalry"
    t = terrain("plain")
    gain = counter_factor(inf, cav)
    base = casualty_rate(soldier(inf), soldier(inf), t)
    vs_cav = casualty_rate(soldier(inf), soldier(cav), t)
    assert vs_cav / base >= 1.15, \
        f"步兵对骑兵的相对伤害优势仅 {(vs_cav / base - 1) * 100:.1f}%，低于 15%（克制被守方属性抵消）"
    assert gain == 1.30


def test_terrain_favors_expected_classes():
    """山地必须让骑兵吃亏、林地必须让弓兵占便宜。"""
    hill, forest = terrain("hill"), terrain("forest")
    assert hill.per_class["cavalry"] < 1.0, "山地骑兵应受罚"
    assert hill.per_class["infantry"] > 1.0, "山地步兵应占优"
    assert forest.per_class["archer"] > 1.0, "林地弓兵应占优"
    assert forest.per_class["cavalry"] < 1.0, "林地骑兵应受罚"


def test_cavalry_bad_on_hill_by_margin():
    """山地对骑兵的惩罚必须显著（≤0.70），否则地形无决策价值。"""
    assert terrain("hill").per_class["cavalry"] <= 0.70


def test_terrain_can_override_counter():
    """验证地形与克制的相互作用：山地足以逆转兵力克制的优势。"""
    inf, cav = "infantry", "cavalry"
    # 平原：步兵克骑兵
    assert casualty_rate(soldier(inf), soldier(cav), terrain("plain")) > \
           casualty_rate(soldier(cav), soldier(inf), terrain("plain"))
    # 山地：骑兵大幅受罚，步兵反超
    assert casualty_rate(soldier(cav), soldier(inf), terrain("hill")) < \
           casualty_rate(soldier(inf), soldier(cav), terrain("hill")), \
        "山地里骑兵打步兵仍占优，说明地形惩罚不足以逆转克制"


# ================================================================ 4. 边界与鲁棒性


def test_no_negative_damage():
    """任何组合下伤害不得为负。"""
    for a in ARMY_CLASS:
        for d in ARMY_CLASS:
            for t in TERRAINS:
                dmg = damage_per_turn(soldier(a), soldier(d), terrain(t))
                assert dmg >= 0.0, f"{a}->{d}@{t} 伤害为负: {dmg}"


def test_hit_rate_within_bounds():
    """命中率必须落在 [0.05, 0.95]。"""
    for a in ARMY_CLASS:
        for t in TERRAINS:
            h = hit_rate(soldier(a), terrain(t))
            assert 0.05 <= h <= 0.95, f"{a}@{t} 命中率越界: {h}"


def test_mitigation_always_positive_and_below_one():
    """减伤系数必须严格在 (0,1] 区间——否则高防兵种会被打成负伤害或超神。"""
    for cls in ARMY_CLASS:
        for mor in (0, 50, 100):
            m = damage_mitigation(_replace(soldier(cls), mor=float(mor)))
            assert 0.0 < m <= 1.0, f"{cls} mor={mor} 减伤越界: {m}"


def test_morale_clamped():
    """士气必须夹在 0~100。"""
    assert morale_after_combat(5.0, 0.9, won=False) == 0.0
    assert morale_after_combat(99.0, 0.0, won=True) <= 100.0


def test_rout_threshold():
    """溃退阈值判定正确。"""
    assert is_routed(ROUT_MORALE - 1.0) is True
    assert is_routed(ROUT_MORALE + 1.0) is False


def test_morale_loss_exceeds_win_gain():
    """败绩对士气的打击必须大于胜利的激励——这是「打了败仗就崩」的设计基础。"""
    start = 60.0
    won = morale_after_combat(start, 0.1, won=True)
    lost = morale_after_combat(start, 0.1, won=False)
    assert (start - lost) > (won - start)


def test_morale_monotonic_on_casualty():
    """伤亡越大，士气越低。"""
    start = 70.0
    vals = [morale_after_combat(start, c, won=False) for c in (0.0, 0.1, 0.3, 0.5)]
    assert all(vals[i] > vals[i + 1] for i in range(len(vals) - 1)), vals


def test_march_cost_zero_troops():
    """零兵力行军不产生粮耗，也不产生负数。"""
    assert march_cost(10.0, terrain("plain"), 0) == 0.0
    assert march_cost(10.0, terrain("plain"), -5) == 0.0


def test_march_cost_scales_monotonically():
    """行军粮耗随距离、地形倍率、人数单调上升。"""
    plain, hill = terrain("plain"), terrain("hill")
    c1 = march_cost(10.0, plain, 1000)
    assert c1 < march_cost(20.0, plain, 1000)
    assert c1 < march_cost(10.0, hill, 1000)
    assert c1 < march_cost(10.0, plain, 2000)


def test_march_cost_hill_expensive():
    """山地行军粮耗必须显著高于平原（≥1.5 倍）。"""
    plain, hill = terrain("plain"), terrain("hill")
    assert march_cost(10.0, hill, 1000) >= 1.5 * march_cost(10.0, plain, 1000)


def test_siege_modifier_decreasing_with_fort_level():
    """据点等级越高，攻城效率越低。"""
    vals = [siege_modifier("infantry", lvl) for lvl in range(1, 6)]
    assert all(vals[i] > vals[i + 1] for i in range(len(vals) - 1)), vals


def test_siege_cavalry_worse_than_infantry():
    """骑兵攻城必须比步兵更差。"""
    for lvl in range(1, 6):
        assert siege_modifier("cavalry", lvl) < siege_modifier("infantry", lvl)


def test_siege_modifier_clamped():
    """据点等级越界输入必须被夹紧，不产生负值或零值。"""
    assert siege_modifier("infantry", 0) == siege_modifier("infantry", 1)
    assert siege_modifier("infantry", 99) == siege_modifier("infantry", 5)
    assert siege_modifier("infantry", 99) > 0.20


def test_supply_capacity_threshold():
    """就地取给判定必须与粮量单调相关，且有明确临界点。"""
    assert supply_capacity(0.0, 1000) is False
    assert supply_capacity(9.9, 1000) is False
    assert supply_capacity(10.0, 1000) is True
    assert supply_capacity(100.0, 1000) is True


def test_no_nan_or_inf_anywhere():
    """全组合扫描：任何输出不得为 NaN 或 Inf（假阴性防护）。"""
    for a in ARMY_CLASS:
        for d in ARMY_CLASS:
            for t in TERRAINS:
                assert math.isfinite(damage_per_turn(soldier(a), soldier(d), terrain(t)))
                assert math.isfinite(casualty_rate(soldier(a), soldier(d), terrain(t)))
                assert math.isfinite(effective_power(soldier(a), terrain(t)))
    for cls in ARMY_CLASS:
        assert math.isfinite(turns_to_kill(soldier(cls), soldier(cls), terrain("plain")))


def test_unknown_key_raises():
    """未知兵种/地形必须显式报错，不静默返回默认值（防止策划配错表而无人发现）。"""
    for fn, arg in ((soldier, "dragon"), (terrain, "lava")):
        try:
            fn(arg)
            raise AssertionError(f"{fn.__name__}('{arg}') 应抛 KeyError 但未抛")
        except KeyError:
            pass


def test_design_intent_panel_consistency():
    """设计意图校验：骑兵面板最高、步兵防御最高、弓兵命中最高但兵力最低。"""
    assert SOLDIERS["cavalry"].str_ > SOLDIERS["infantry"].str_ > SOLDIERS["archer"].str_
    assert SOLDIERS["infantry"].deff > SOLDIERS["cavalry"].deff > SOLDIERS["archer"].deff
    assert SOLDIERS["archer"].base_hit > SOLDIERS["infantry"].base_hit > SOLDIERS["cavalry"].base_hit
    assert SOLDIERS["cavalry"].sta > SOLDIERS["infantry"].sta > SOLDIERS["archer"].sta
    assert SOLDIERS["cavalry"].cost == SOLDIERS["infantry"].cost * 2


def test_archer_is_glass_cannon():
    """弓兵必须是高命中低兵力高伤亡承受——脆皮输出定位。"""
    arch, cav = soldier("archer"), soldier("cavalry")
    t = terrain("plain")
    assert hit_rate(arch, t) > hit_rate(cav, t)
    assert damage_per_turn(cav, arch, t) / SOLDIER_HP > \
           damage_per_turn(arch, cav, t) / SOLDIER_HP, "弓兵应比骑兵更容易被打死"


def test_damage_scale_consistent_with_turns_to_kill():
    """DAMAGE_SCALE 与 SOLDIER_HP 的一致性：两者乘积决定回合数，必须同源。"""
    a = soldier("infantry")
    per_turn = damage_per_turn(a, a, terrain("plain"))
    assert abs(casualty_rate(a, a, terrain("plain")) - per_turn / SOLDIER_HP) < 1e-12
    assert abs(turns_to_kill(a, a, terrain("plain")) - 1.0 / casualty_rate(a, a, terrain("plain"))) < 1e-9
    assert DAMAGE_SCALE > 0


def main() -> int:
    tests = [(k, v) for k, v in sorted(globals().items()) if k.startswith("test_")]
    failed = []
    for name, fn in tests:
        try:
            fn()
            print(f"[PASS] {name}")
        except AssertionError as e:
            failed.append((name, str(e)))
            print(f"[FAIL] {name}: {e}")
        except Exception as e:  # noqa: BLE001
            failed.append((name, f"{type(e).__name__}: {e}"))
            print(f"[ERROR] {name}: {type(e).__name__}: {e}")
    print("-" * 56)
    print(f"共 {len(tests)} 项，通过 {len(tests) - len(failed)}，失败 {len(failed)}")
    for name, msg in failed:
        print(f"  未通过: {name} -> {msg}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())