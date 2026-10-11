#!/usr/bin/env python3
# ============================================================================
#  tools/make_fixtures.py —— 重建坏数据样本（fixtures）
# ============================================================================
#  【为什么需要这个脚本】
#  fixtures/ 下每个目录都是「一份完整的合法数据 + 注入一处错误」。
#  数据文件一改（比如契约升到v1.2，全量改成万分比），所有 fixture 就全部过期，
#  而手工同步 10 个目录 × 7 个文件是不现实的手工活，也极易漏改。
#
#  【铁律】
#  坏数据样本必须走真实链路：真实文件 → 加载 → 校验。
#  靠代码 new 一个坏对象测不到「JSON 里写错了」这种真实情况。
#
#  用法：python tools/make_fixtures.py
# ============================================================================

import io
import json
import os
import shutil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "assets", "data")
FIX = os.path.join(ROOT, "tests", "StrategyGame.Data.Tests", "fixtures")

DATA_FILES = [
    "config.json", "officers.json", "cities.json", "events.json",
    "specialties.json", "factions.json", "difficulty.json",
]


def read(name):
    with io.open(os.path.join(DATA, name), encoding="utf-8") as f:
        return json.load(f)


def write(path, obj):
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
        f.write("\n")


def make(name, mutate):
    """复制一份完整数据，然后注入一处错误。"""
    dst = os.path.join(FIX, name)
    if os.path.isdir(dst):
        shutil.rmtree(dst)
    os.makedirs(dst)

    for f in DATA_FILES:
        shutil.copy2(os.path.join(DATA, f), os.path.join(dst, f))

    # 词库也要跟着复制：LoadAll 会加载 names/，缺了就加载失败，
    # 那样测的就不是「注入的那处错误」而是「文件缺失」。
    shutil.copytree(os.path.join(DATA, "names"), os.path.join(dst, "names"))

    mutate(dst)


def edit(path, fn):
    obj = read(os.path.basename(path))
    fn(obj)
    write(path, obj)


def city_by_id(obj, cid):
    for c in obj["cities"]:
        if c["id"] == cid:
            return c
    raise KeyError(cid)


def event_by_id(obj, eid):
    for e in obj["events"]:
        if e["id"] == eid:
            return e
    raise KeyError(eid)


def officer_by_id(obj, oid):
    for o in obj["officers"]:
        if o["id"] == oid:
            return o
    raise KeyError(oid)


# ============================================================================
#  各样本的注入逻辑
# ============================================================================

def bad_negative(dst):
    """势力 0 的粮草改成负数。"""
    def m(obj):
        obj["factions"][0]["resources"]["grain"] = -500
    edit(os.path.join(dst, "factions.json"), m)


def bad_prob_over(dst):
    """事件概率超出万分比上界（原为百分数时代的 101，现在应是 10100）。"""
    def m(obj):
        event_by_id(obj, 1001)["probability"] = 10100
    edit(os.path.join(dst, "events.json"), m)


def bad_adj_symmetric(dst):
    """城池 5 单方面去掉对城池 4 的相邻声明。"""
    def m(obj):
        c = city_by_id(obj, 5)
        c["adjacentCityIds"] = [x for x in c["adjacentCityIds"] if x != 4]
    edit(os.path.join(dst, "cities.json"), m)


def bad_adj_missing(dst):
    """城池 1 的相邻列表指向不存在的城 999。"""
    def m(obj):
        city_by_id(obj, 1)["adjacentCityIds"] = [2, 999]
    edit(os.path.join(dst, "cities.json"), m)


def bad_no_capital(dst):
    """势力 1 名下没有首都。"""
    def m(obj):
        city_by_id(obj, 3)["isCapital"] = False
    edit(os.path.join(dst, "cities.json"), m)


def bad_two_capital(dst):
    """势力 1 名下有两座首都。"""
    def m(obj):
        city_by_id(obj, 4)["isCapital"] = True
    edit(os.path.join(dst, "cities.json"), m)


def bad_strong_player(dst):
    """玩家人才 Command 拉到 88，超出没落贵族上限。"""
    def m(obj):
        officer_by_id(obj, 1)["command"] = 88
    edit(os.path.join(dst, "officers.json"), m)


def bad_prob_sum(dst):
    """Economy 类别概率之和超过 6000。"""
    def m(obj):
        event_by_id(obj, 1001)["probability"] = 5500
    edit(os.path.join(dst, "events.json"), m)


def bad_player_one_city(dst):
    """玩家势力只剩1 座城（把城池 2 划给中原霸主）。

    这一条是本轮返工新增的样本：上一轮正是这个错误溜过了校验，
    所以它必须成为一条常驻的回归样本，而不是只写在注释里。
    """
    def m(obj):
        city_by_id(obj, 2)["factionId"] = 1
    edit(os.path.join(dst, "cities.json"), m)


def bad_player_not_adjacent(dst):
    """玩家两座城不相邻（移除 1↔2 的双向声明）。"""
    def m(obj):
        city_by_id(obj, 1)["adjacentCityIds"] = []
        city_by_id(obj, 2)["adjacentCityIds"] = [3, 4]
    edit(os.path.join(dst, "cities.json"), m)


def bad_base_kill(dst):
    """baseKill 填 0：攻城分支永远不可达。"""
    def m(obj):
        obj["baseKill"] = 0
    edit(os.path.join(dst, "config.json"), m)


def bad_ratio_scale(dst):
    """把万分比当成百分数填：baseLoss 填 35 而不是 3500。

    这是契约 v1.2 之后最容易犯的错——从旧配置沿用数字时不会报错，
    但会让所有战损缩小 100 倍，且从数据表面完全看不出问题。
    """
    def m(obj):
        obj["baseLoss"] = 35
        obj["siegeThresholdLoss"] = 70
    edit(os.path.join(dst, "config.json"), m)


def bad_deprecated_field(dst):
    """已废弃字段没置 0。"""
    def m(obj):
        obj["defenderLossFactor"] = 60
        obj["stalemateLossRate"] = 10
    edit(os.path.join(dst, "config.json"), m)


def bad_event_id_zero(dst):
    """事件 Id 为 0，违反「全项目 Id 必须是正整数」。"""
    def m(obj):
        event_by_id(obj, 1002)["id"] = 0
    edit(os.path.join(dst, "events.json"), m)


def bad_name_bank_small(dst):
    """词库被砍到规模下限以下。"""
    path = os.path.join(dst, "names", "officer_surname.json")
    with io.open(path, encoding="utf-8") as f:
        obj = json.load(f)
    obj["surnames"] = obj["surnames"][:10]
    write(path, obj)


def bad_name_bank_dup(dst):
    """词库里有重复词条，组合数被凭空缩小。"""
    path = os.path.join(dst, "names", "city_prefix.json")
    with io.open(path, encoding="utf-8") as f:
        obj = json.load(f)
    obj["prefixes"][5] = obj["prefixes"][0]
    write(path, obj)


FIXTURES = [
    ("bad_negative", bad_negative),
    ("bad_prob_over", bad_prob_over),
    ("bad_adj_symmetric", bad_adj_symmetric),
    ("bad_adj_missing", bad_adj_missing),
    ("bad_no_capital", bad_no_capital),
    ("bad_two_capital", bad_two_capital),
    ("bad_strong_player", bad_strong_player),
    ("bad_prob_sum", bad_prob_sum),
    ("bad_player_one_city", bad_player_one_city),
    ("bad_player_not_adjacent", bad_player_not_adjacent),
    ("bad_base_kill", bad_base_kill),
    ("bad_ratio_scale", bad_ratio_scale),
    ("bad_deprecated_field", bad_deprecated_field),
    ("bad_event_id_zero", bad_event_id_zero),
    ("bad_name_bank_small", bad_name_bank_small),
    ("bad_name_bank_dup", bad_name_bank_dup),
]


def main():
    for name, fn in FIXTURES:
        make(name, fn)
        print("  ✓ %s" % name)
    print("已重建 %d 个坏数据样本。" % len(FIXTURES))


if __name__ == "__main__":
    main()