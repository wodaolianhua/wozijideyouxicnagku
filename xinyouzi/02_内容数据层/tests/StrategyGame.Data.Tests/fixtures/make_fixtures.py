#!/usr/bin/env python3
"""
生成坏数据样本（fixture）。

【为什么用脚本生成而不是手写】
坏数据样本必须是"只坏一处、其余全对"，否则测试无法确定校验器抓到的
到底是不是目标错误。手写整份 JSON 极易连带写坏别的字段，导致测试变成
"抓到了某个错误，但不知道是不是我要测的那个"。

做法：以真实数据为基底，用精确的字符串替换只改动目标字段。
"""
import io
import json
import os
import shutil

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'assets', 'data')
OUT = os.path.dirname(os.path.abspath(__file__))

BASE_FILES = ['config.json', 'officers.json', 'cities.json', 'events.json',
              'specialties.json', 'factions.json', 'difficulty.json']


def fresh_copy(name):
    d = os.path.join(OUT, name)
    if os.path.isdir(d):
        shutil.rmtree(d)
    os.makedirs(d)
    for f in BASE_FILES:
        shutil.copy(os.path.join(BASE, f), os.path.join(d, f))
    return d


def patch(path, old, new, count=1):
    s = io.open(path, encoding='utf-8').read()
    assert s.count(old) >= count, f'替换目标未找到或数量不足: {old!r} (found {s.count(old)})'
    s = s.replace(old, new, count)
    io.open(path, 'w', encoding='utf-8', newline='\n').write(s)


def verify(d):
    """确认生成的坏数据仍然是合法 JSON——否则测的是解析器而不是校验器。"""
    for f in os.listdir(d):
        json.load(io.open(os.path.join(d, f), encoding='utf-8'))


# ---------------------------------------------------------------- 1. 负数资源
d = fresh_copy('bad_negative')
patch(os.path.join(d, 'factions.json'), '"grain": 1200,', '"grain": -500,')
verify(d)
print('bad_negative        : 玩家势力初始粮草 1200 → -500（负数资源）')

# ---------------------------------------------------------------- 2. 概率 101
d = fresh_copy('bad_prob101')
patch(os.path.join(d, 'events.json'), '"probability": 12,', '"probability": 101,')
verify(d)
print('bad_prob101         : EVT_ECO_001 概率 12 → 101（概率越界）')

# ------------------------------------------------- 3. 相邻关系不对称（A→B 但 B↛A）
d = fresh_copy('bad_adj_symmetric')
# 城池 5 的相邻列表删掉 4，使 4→5 存在但 5→4 不存在
patch(os.path.join(d, 'cities.json'), '"adjacentCityIds": [4, 6],', '"adjacentCityIds": [6],')
verify(d)
print('bad_adj_symmetric   : 城池5 移除相邻城池4（4→5 单向，A含B但B不含A）')

# --------------------------------------------- 4. 指向不存在的城池（悬空引用）
d = fresh_copy('bad_adj_missing')
patch(os.path.join(d, 'cities.json'), '"adjacentCityIds": [2],', '"adjacentCityIds": [2, 999],')
# 让 999 那一侧对称性检查不误报：由于 999 不存在，存在性检查会先命中。
verify(d)
print('bad_adj_missing     : 城池1 的相邻列表加入不存在的城池 999（悬空引用）')

def patch_city_is_capital(path, city_id, new_value):
    """按城池 id 定位 cities[] 里的那一项，只改它的 isCapital 字段。

    不用多行字符串匹配——那些注释文本一改就会失配，脆弱得没法维护。
    这里用缩进层级定位到 'cities' 数组内 '"id": <city_id>' 所在的整块对象。
    """
    lines = io.open(path, encoding='utf-8').read().split('\n')

    # 找到 cities 数组起点
    start = next(i for i, l in enumerate(lines) if l.strip() == '"cities": [')
    # 找到 cityStates 数组起点（cities 数组在此结束）
    end = next(i for i, l in enumerate(lines) if l.strip() == '"cityStates": [')

    # 在 [start, end) 内找 id 为 city_id 的那一项
    target = None
    for i in range(start, end):
        if lines[i].strip() == f'"id": {city_id},':
            target = i
            break
    assert target is not None, f'未找到 id={city_id} 的城池'

    # 从 id 行往后找到同层的 isCapital
    for j in range(target, end):
        if lines[j].strip().startswith('"isCapital"'):
            indent = lines[j][:len(lines[j]) - len(lines[j].lstrip())]
            lines[j] = f'{indent}"isCapital": {str(new_value).lower()},'
            io.open(path, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))
            return
    raise AssertionError(f'id={city_id} 的城池没有 isCapital 字段')


# ---------------------------------------------------------------- 5. 势力没有首都
d = fresh_copy('bad_no_capital')
patch_city_is_capital(os.path.join(d, 'cities.json'), 3, False)
verify(d)
print('bad_no_capital      : 中原霸主(id=1)的首都不再标记为首都（势力无首都）')

# ---------------------------------------------------------------- 6. 两个首都
d = fresh_copy('bad_two_capital')
patch_city_is_capital(os.path.join(d, 'cities.json'), 4, True)
verify(d)
print('bad_two_capital     : 中原霸主的城池 4 也标记为首都（首都不唯一）')

# ---------------------------------------------------------------- 7. 玩家人才过强
d = fresh_copy('bad_strong_player')
patch(os.path.join(d, 'officers.json'),
      '"command": 48,\n      "governance": 52,',
      '"command": 88,\n      "governance": 52,')
verify(d)
print('bad_strong_player   : 玩家人才 Command 48 → 88（超过玩家上限 55）')

# ---------------------------------------------------------------- 8. 概率和超过 60
d = fresh_copy('bad_prob_sum')
# Economy 类别原本 12+8+7+7 = 34。把 EVT_ECO_002 从 8 提到 40，和变成 66 > 60
patch(os.path.join(d, 'events.json'), '"probability": 8,\n      "message": "市集熙攘', '"probability": 40,\n      "message": "市集熙攘')
verify(d)
print('bad_prob_sum        : Economy 类别概率和 34 → 66（超过同类别上限 60）')

print('\n全部 fixture 生成完毕，且均通过 JSON 语法校验。')
