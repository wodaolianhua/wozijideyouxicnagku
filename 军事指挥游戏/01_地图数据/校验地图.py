#!/usr/bin/env python3
"""校验初始地图的契约字段、引用、路网、坐标和初始部队状态。"""

import json
import math
import re
import sys
from collections import Counter, deque
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DATA_FILE = ROOT / "初始地图.json"
CONTRACT_DIR = ROOT / "接口契约_共享类型"
MAX_NEIGHBOUR_DISTANCE = 260.0
errors = []


def fail(message):
    errors.append(message)


def contract_properties(models_source, class_name):
    match = re.search(
        rf"public\s+sealed\s+class\s+{re.escape(class_name)}\s*\{{(.*?)^\}}",
        models_source,
        re.MULTILINE | re.DOTALL,
    )
    if not match:
        raise ValueError(f"契约中未找到 {class_name}")
    properties = []
    for line in match.group(1).splitlines():
        prop = re.search(
            r"public\s+\w+(?:<[\w, ]+>)?\s+(\w+)\s*\{\s*get;\s*set;\s*\}",
            line,
        )
        if prop:
            properties.append(prop.group(1))
    return properties


def contract_enum_values(enums_source, enum_name):
    match = re.search(
        rf"public\s+enum\s+{re.escape(enum_name)}\s*\{{(.*?)\}}",
        enums_source,
        re.DOTALL,
    )
    if not match:
        raise ValueError(f"契约中未找到 {enum_name}")
    values = set()
    next_value = 0
    for raw_line in match.group(1).splitlines():
        line = raw_line.split("//", 1)[0].strip()
        item = re.fullmatch(r"(\w+)(?:\s*=\s*(-?\d+))?\s*,?", line)
        if not item:
            continue
        value = int(item.group(2)) if item.group(2) is not None else next_value
        values.add(value)
        next_value = value + 1
    return values


def check_fields(value, properties, path):
    if not isinstance(value, dict):
        fail(f"{path} 必须是对象")
        return False
    actual = set(value)
    expected = set(properties)
    missing = sorted(expected - actual)
    extra = sorted(actual - expected)
    if missing:
        fail(f"{path} 缺少字段：{', '.join(missing)}")
    if extra:
        fail(f"{path} 含契约外字段：{', '.join(extra)}")
    return not missing and not extra


def is_integer(value):
    return isinstance(value, int) and not isinstance(value, bool)


def is_number(value):
    return (
        isinstance(value, (int, float))
        and not isinstance(value, bool)
        and math.isfinite(value)
    )


def check_nonempty_string(value, path):
    if not isinstance(value, str) or not value.strip():
        fail(f"{path} 必须是非空字符串")
        return False
    return True


def check_enum(value, valid_values, path):
    if not is_integer(value) or value not in valid_values:
        fail(f"{path} 不是合法枚举数值：{value!r}")
        return False
    return True


def main():
    models_source = (CONTRACT_DIR / "Models.cs").read_text(encoding="utf-8-sig")
    enums_source = (CONTRACT_DIR / "Enums.cs").read_text(encoding="utf-8-sig")
    schemas = {
        name: contract_properties(models_source, name)
        for name in ("WorldState", "WorldMap", "MapNode", "MapEdge", "Army")
    }
    enums = {
        name: contract_enum_values(enums_source, name)
        for name in ("TerrainType", "EdgeType", "Faction", "UnitType")
    }

    try:
        data = json.loads(DATA_FILE.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"无法读取地图数据：{exc}", file=sys.stderr)
        return 2

    if not isinstance(data, dict):
        print("地图数据根节点必须是 WorldState 对象", file=sys.stderr)
        return 1

    check_fields(data, schemas["WorldState"], "WorldState")
    if not is_integer(data.get("Time")) or data.get("Time", -1) < 0:
        fail("Time 必须是非负整数")

    world_map = data.get("Map")
    if not isinstance(world_map, dict):
        fail("Map 必须是 WorldMap 对象")
        world_map = {}
    else:
        check_fields(world_map, schemas["WorldMap"], "WorldState.Map")

    nodes = world_map.get("Nodes", [])
    edges = world_map.get("Edges", [])
    armies = data.get("Armies", [])
    if not isinstance(nodes, list):
        fail("Map.Nodes 必须是数组")
        nodes = []
    if not isinstance(edges, list):
        fail("Map.Edges 必须是数组")
        edges = []
    if not isinstance(armies, list):
        fail("Armies 必须是数组")
        armies = []
    if not 12 <= len(nodes) <= 20:
        fail(f"节点数量应为 12–20，实际为 {len(nodes)}")

    node_ids = set()
    coordinates = {}
    occupied_coordinates = {}
    for index, node in enumerate(nodes):
        path = f"Map.Nodes[{index}]"
        if not isinstance(node, dict):
            fail(f"{path} 必须是 MapNode 对象")
            continue
        check_fields(node, schemas["MapNode"], path)
        node_id = node.get("Id")
        if check_nonempty_string(node_id, f"{path}.Id"):
            if node_id in node_ids:
                fail(f"节点 Id 重复：{node_id}")
            node_ids.add(node_id)
        check_nonempty_string(node.get("Name"), f"{path}.Name")
        check_enum(node.get("Terrain"), enums["TerrainType"], f"{path}.Terrain")
        x, y = node.get("X"), node.get("Y")
        if not is_number(x) or not is_number(y):
            fail(f"{path} 的 X/Y 必须是有限数值")
        elif isinstance(node_id, str):
            if (x, y) in occupied_coordinates:
                fail(f"{path} 与 {occupied_coordinates[(x, y)]} 的坐标重复")
            else:
                occupied_coordinates[(x, y)] = path
            coordinates.setdefault(node_id, (x, y))

    adjacency = {node_id: set() for node_id in node_ids}
    edge_pairs = set()
    road_costs = []
    mountain_costs = []
    neighbour_distances = []
    for index, edge in enumerate(edges):
        path = f"Map.Edges[{index}]"
        if not isinstance(edge, dict):
            fail(f"{path} 必须是 MapEdge 对象")
            continue
        check_fields(edge, schemas["MapEdge"], path)
        from_id, to_id = edge.get("From"), edge.get("To")
        endpoints_valid = True
        for endpoint_name, endpoint in (("From", from_id), ("To", to_id)):
            if check_nonempty_string(endpoint, f"{path}.{endpoint_name}"):
                if endpoint not in node_ids:
                    fail(f"{path}.{endpoint_name} 引用了不存在的节点：{endpoint}")
                    endpoints_valid = False
            else:
                endpoints_valid = False
        if endpoints_valid:
            if from_id == to_id:
                fail(f"{path} 不允许自环：{from_id}")
            pair = tuple(sorted((from_id, to_id)))
            if pair in edge_pairs:
                fail(f"无向边重复：{from_id} <-> {to_id}")
            edge_pairs.add(pair)
            adjacency[from_id].add(to_id)
            adjacency[to_id].add(from_id)
            if from_id in coordinates and to_id in coordinates:
                x1, y1 = coordinates[from_id]
                x2, y2 = coordinates[to_id]
                distance = math.hypot(x2 - x1, y2 - y1)
                neighbour_distances.append(distance)
                if distance > MAX_NEIGHBOUR_DISTANCE:
                    fail(
                        f"{path} 相连节点坐标距离 {distance:.2f} 超过 "
                        f"{MAX_NEIGHBOUR_DISTANCE:.0f}"
                    )
        edge_type = edge.get("Type")
        type_valid = check_enum(edge_type, enums["EdgeType"], f"{path}.Type")
        cost = edge.get("Cost")
        if not is_number(cost) or cost <= 0:
            fail(f"{path}.Cost 必须是正的有限数值")
        elif type_valid:
            if edge_type == 0:
                road_costs.append(cost)
            else:
                mountain_costs.append(cost)

    if not road_costs or not mountain_costs:
        fail("路网必须同时包含道路和山路")
    elif min(mountain_costs) <= max(road_costs):
        fail("所有山路的行军代价都必须高于道路")

    if node_ids:
        reached = set()
        queue = deque([next(iter(node_ids))])
        while queue:
            current = queue.popleft()
            if current in reached:
                continue
            reached.add(current)
            queue.extend(adjacency[current] - reached)
        unreachable = sorted(node_ids - reached)
        if unreachable:
            fail(f"路网不连通，不可达节点：{', '.join(unreachable)}")

    army_ids = set()
    faction_counts = Counter()
    faction_strength = Counter()
    for index, army in enumerate(armies):
        path = f"Armies[{index}]"
        if not isinstance(army, dict):
            fail(f"{path} 必须是 Army 对象")
            continue
        check_fields(army, schemas["Army"], path)
        army_id = army.get("Id")
        if check_nonempty_string(army_id, f"{path}.Id"):
            if army_id in army_ids:
                fail(f"部队 Id 重复：{army_id}")
            army_ids.add(army_id)
        check_nonempty_string(army.get("Name"), f"{path}.Name")
        faction = army.get("Faction")
        faction_valid = check_enum(faction, enums["Faction"], f"{path}.Faction")
        check_enum(army.get("UnitType"), enums["UnitType"], f"{path}.UnitType")
        strength = army.get("Strength")
        if not is_integer(strength) or strength <= 0:
            fail(f"{path}.Strength 必须是正整数")
        node_id = army.get("NodeId")
        node_valid = check_nonempty_string(node_id, f"{path}.NodeId")
        if node_valid and node_id not in node_ids:
            fail(f"{path}.NodeId 引用了不存在的节点：{node_id}")
            node_valid = False
        progress = army.get("EdgeProgress")
        if not is_number(progress) or not 0.0 <= progress <= 1.0:
            fail(f"{path}.EdgeProgress 必须在 0..1 之间")
        elif progress != 0.0:
            fail(f"{path} 是初始部队，EdgeProgress 必须为 0")
        planned_path = army.get("PlannedPath")
        path_index = army.get("PathIndex")
        if not isinstance(planned_path, list) or not planned_path:
            fail(f"{path}.PlannedPath 必须至少包含当前节点")
        else:
            for path_node in planned_path:
                if path_node not in node_ids:
                    fail(f"{path}.PlannedPath 引用了不存在的节点：{path_node}")
            if not is_integer(path_index) or not 0 <= path_index < len(planned_path):
                fail(f"{path}.PathIndex 超出 PlannedPath 范围")
            elif node_valid and planned_path[path_index] != node_id:
                fail(f"{path}.NodeId 必须等于 PlannedPath[PathIndex]")
        if path_index != 0:
            fail(f"{path} 是初始部队，PathIndex 必须为 0")
        if node_valid and planned_path != [node_id]:
            fail(f"{path} 是初始部队，PlannedPath 必须仅为 [NodeId]")
        if faction_valid:
            faction_counts[faction] += 1
            if is_integer(strength) and strength > 0:
                faction_strength[faction] += strength

    if faction_counts[0] < 2:
        fail("玩家方至少需要 2 支部队")
    if faction_counts[1] < 2:
        fail("敌方至少需要 2 支部队")
    if faction_strength[0] >= faction_strength[1]:
        fail("玩家方初始总兵力必须低于敌方")

    if errors:
        print(f"校验失败，共 {len(errors)} 项：", file=sys.stderr)
        for item in errors:
            print(f"- {item}", file=sys.stderr)
        return 1

    print("地图数据校验通过")
    print(f"节点：{len(nodes)}；无向边：{len(edges)}；部队：{len(armies)}")
    print(
        f"玩家：{faction_counts[0]} 支 / {faction_strength[0]} 兵；"
        f"敌方：{faction_counts[1]} 支 / {faction_strength[1]} 兵"
    )
    print(
        f"道路代价：{min(road_costs):.1f}–{max(road_costs):.1f}；"
        f"山路代价：{min(mountain_costs):.1f}–{max(mountain_costs):.1f}"
    )
    print(
        "路网连通；相连节点最大坐标距离："
        f"{max(neighbour_distances):.2f}（上限 {MAX_NEIGHBOUR_DISTANCE:.0f}）"
    )
    print("契约字段与数值枚举检查通过；节点、路径及部队引用均有效")
    return 0


if __name__ == "__main__":
    sys.exit(main())
