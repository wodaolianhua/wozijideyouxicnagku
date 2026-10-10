"""生成 Python 参考实现的数值快照，供跨语言一致性检查使用。

用法： python cross_lang_fixtures.py
输出： fixtures.json

TS 侧对应脚本： cross_lang_check.mjs
两边必须逐位一致（浮点 6 位小数）。
"""

import json
import sys

from combat_model import (
    ARMY_CLASS,
    SOLDIERS,
    TERRAINS,
    casualty_matrix,
    counter_matrix,
    damage_mitigation,
    effective_power,
    hit_rate,
    morale_after_combat,
    march_cost,
    resolve_duel,
    siege_modifier,
    turns_to_kill,
)


def r6(v: float) -> float:
    return round(v, 6)


def build() -> dict:
    out: dict = {
        "panels": {},
        "counterMatrix": {},
        "effectivePower": {},
        "hitRate": {},
        "mitigation": {},
        "casualtyRate": {},
        "turnsToKill": {},
        "duels": {},
        "misc": {},
    }

    for cls in ARMY_CLASS:
        s = SOLDIERS[cls]
        out["panels"][cls] = [s.str_, s.mor, s.sta, s.base_hit, s.deff, s.cost]

    _, cm = counter_matrix()
    for i, a in enumerate(ARMY_CLASS):
        for j, d in enumerate(ARMY_CLASS):
            out["counterMatrix"][f"{a}|{d}"] = r6(cm[i][j])

    for cls in ARMY_CLASS:
        for tname in TERRAINS:
            t = TERRAINS[tname]
            s = SOLDIERS[cls]
            k = f"{cls}|{tname}"
            out["effectivePower"][k] = r6(effective_power(s, t))
            out["hitRate"][k] = r6(hit_rate(s, t))
            out["mitigation"][k] = r6(damage_mitigation(s))

    for tname in TERRAINS:
        _, mat = casualty_matrix(tname)
        for i, a in enumerate(ARMY_CLASS):
            for j, d in enumerate(ARMY_CLASS):
                out["casualtyRate"][f"{tname}|{a}|{d}"] = r6(mat[i][j])

    for cls in ARMY_CLASS:
        out["turnsToKill"][cls] = r6(turns_to_kill(SOLDIERS[cls], SOLDIERS[cls], TERRAINS["plain"]))

    duel_cases = [
        ("plain", 1000),
        ("hill", 1000),
        ("forest", 1000),
        ("plain", 100),
        ("hill", 4000),
    ]
    for a in ARMY_CLASS:
        for d in ARMY_CLASS:
            for tname, count in duel_cases:
                r = resolve_duel(SOLDIERS[a], SOLDIERS[d], TERRAINS[tname], count, count)
                out["duels"][f"{a}|{d}|{tname}|{count}"] = [
                    r["winner"],
                    r["turns"],
                    r["attacker_left"],
                    r["defender_left"],
                    r["attacker_routed"],
                    r["defender_routed"],
                ]

    for tname in TERRAINS:
        for dist in (5, 10, 20):
            for troops in (500, 1000, 5000):
                out["misc"][f"march|{tname}|{dist}|{troops}"] = r6(
                    march_cost(dist, TERRAINS[tname], troops)
                )
    for lvl in (1, 2, 3, 4, 5):
        for cls in ARMY_CLASS:
            out["misc"][f"siege|{cls}|{lvl}"] = r6(siege_modifier(cls, lvl))
    for c in (0.0, 0.1, 0.3, 0.5):
        for won in (True, False):
            key = f"morale|{c:.1f}|{'True' if won else 'False'}"
            out["misc"][key] = r6(morale_after_combat(60.0, c, won))

    return out


if __name__ == "__main__":
    data = build()
    with open("fixtures.json", "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    total = sum(len(v) for v in data.values())
    print(f"已写出 fixtures.json，共 {total} 项数值")