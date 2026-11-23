#!/usr/bin/env python3
"""
JSON 语法预检工具。

【为什么需要它】
数据文件由策划手工编辑，最常见的三类低级错误是：
  1. 注释里写了未转义的英文双引号  → 文件直接变成非法 JSON
  2. 数值写成了小数或带引号        → 违反"禁止浮点数存资源"
  3. 末尾多了逗号                  → 反序列化直接失败

.NET 的校验器要等 dotnet build 之后才能跑，这个脚本可以秒级反馈。
注意：它只做语法与格式预检，业务校验（相邻对称、唯一首都等）由 ConfigValidator 负责。
"""
import io
import json
import re
import sys
import os

DATA_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'assets', 'data')

# 以 _ 开头的键是元信息（_comment / _todos / _c_xxx），其中的字符串不参与数值检查。
META_PREFIX = '_'


def strip_strings_and_meta(text):
    """返回去掉字符串字面量与元信息后的结构文本，用于扫描裸数值。"""
    # 先移除所有字符串字面量
    text = re.sub(r'"(?:\\.|[^"\\])*"', '""', text)
    return text


def check_file(path):
    name = os.path.basename(path)
    problems = []

    try:
        raw = io.open(path, encoding='utf-8').read()
    except Exception as e:
        return [f'{name}: 无法读取（{e}）']

    # --- 1. 语法合法性 ---
    try:
        data = json.loads(raw)
    except Exception as e:
        return [f'{name}: JSON 语法错误 —— {e}']

    # --- 2. 必须有 _comment ---
    if not isinstance(data, dict):
        return [f'{name}: 根节点必须是对象，实际为 {type(data).__name__}']
    if '_comment' not in data:
        problems.append(f'{name}: 缺少 _comment 字段（任务书 R1 要求所有数值都要有说明）')

    # --- 3. 浮点数扫描（结构层，排除字符串内的文本） ---
    skeleton = strip_strings_and_meta(raw)
    for m in re.finditer(r'(?<![\w"])-?\d+\.\d+', skeleton):
        problems.append(f'{name}: 出现浮点数字面量 "{m.group(0)}"（契约要求资源一律用 int）')

    # --- 4. 末尾逗号 ---
    if re.search(r',(\s*[}\]])', skeleton):
        problems.append(f'{name}: 存在多余的末尾逗号，JSON 解析器会报错')

    # --- 5. 缩进必须是 2 空格 ---
    for i, line in enumerate(raw.split('\n'), 1):
        stripped = line.lstrip(' ')
        indent = len(line) - len(stripped)
        if stripped and indent and indent % 2 != 0:
            problems.append(f'{name}:{i}: 缩进不是 2 的倍数（{indent} 空格）')
            break

    # --- 6. 中文键名禁止 ---
    def walk_keys(obj, path):
        if isinstance(obj, dict):
            for k, v in obj.items():
                if re.search(r'[一-鿿]', k):
                    problems.append(f'{name}{path}: 键 "{k}" 含中文（任务书 R1 禁止）')
                walk_keys(v, f'{path}.{k}')
        elif isinstance(obj, list):
            for idx, v in enumerate(obj):
                walk_keys(v, f'{path}[{idx}]')
    walk_keys(data, '')

    return problems


def main():
    files = sorted(f for f in os.listdir(DATA_DIR) if f.endswith('.json'))
    if not files:
        print('assets/data 下没有找到任何 .json 文件')
        return 1

    all_problems = []
    for f in files:
        all_problems.extend(check_file(os.path.join(DATA_DIR, f)))

    print(f'预检 {len(files)} 个文件：')
    for f in files:
        print(f'  - {f}')

    if not all_problems:
        print('\n全部通过：语法合法、含 _comment、无浮点、无多余逗号、缩进 2 空格、无中文键名。')
        return 0

    print(f'\n发现 {len(all_problems)} 个问题：')
    for p in all_problems:
        print(f'  [!] {p}')
    return 1


if __name__ == '__main__':
    sys.exit(main())
