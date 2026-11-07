import bpy, mathutils

# ---- 1. 清理 + 导入 ----
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.gltf(filepath="monarch.glb")

mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
# 把网格变换烘焙到顶点，保证后面读到的坐标是世界坐标
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# ---- 2. 建骨架（模型真实竖直方向 = Z）----
arm = bpy.data.armatures.new('Armature')
arm_obj = bpy.data.objects.new('Armature', arm)
bpy.context.scene.collection.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode='EDIT')

def bone(name, head, tail):
    b = arm.edit_bones.new(name)
    b.head = mathutils.Vector(head)
    b.tail = mathutils.Vector(tail)
    b.use_deform = True
    return b

bone('Root',  ( 0.00, 0, 0.00), ( 0.00, 0, 0.10))
bone('Hips',  ( 0.00, 0, 0.45), ( 0.00, 0, 0.60))
bone('Spine', ( 0.00, 0, 0.65), ( 0.00, 0, 0.78))
bone('Chest', ( 0.00, 0, 0.85), ( 0.00, 0, 0.98))
bone('Neck',  ( 0.00, 0, 0.99), ( 0.00, 0, 1.03))
bone('Head',  ( 0.00, 0, 1.08), ( 0.00, 0, 1.22))
bone('Arm.L', (-0.22, 0, 0.82), (-0.22, 0, 0.98))   # 尾巴朝上
bone('Arm.R', ( 0.22, 0, 0.82), ( 0.22, 0, 0.98))
bone('Leg.L', (-0.18, 0, 0.12), (-0.18, 0, 0.00))   # 尾巴朝下
bone('Leg.R', ( 0.18, 0, 0.12), ( 0.18, 0, 0.00))

bpy.ops.object.mode_set(mode='OBJECT')

# ---- 3. 按 区域限定的软权重（关键：袍身中央只跟 Hips/Spine，绝不绑给腿）----
# 骨头的 head 世界坐标（与上面 bone() 一致）
HEADS = {
    'Root':(0,0,0), 'Hips':(0,0,0.45), 'Spine':(0,0,0.65), 'Chest':(0,0,0.85),
    'Neck':(0,0,0.99), 'Head':(0,0,1.08), 'Arm.L':(-0.22,0,0.82), 'Arm.R':(0.22,0,0.82),
    'Leg.L':(-0.18,0,0.12), 'Leg.R':(0.18,0,0.12),
}

def candidates(co):
    x, y, z = co.x, co.y, co.z
    # 胳膊：上半身、向两侧伸出（伸到 x≈±0.27）
    if 0.50 <= z <= 0.96 and abs(x) > 0.15:
        return ('Arm.L' if x < 0 else 'Arm.R', 'Spine')
    # 下半身：只有最底、最窄的靴子才绑腿；整片袍摆（含喇叭外缘）全归盆骨/脊柱
    if z < 0.16 and abs(x) > 0.16:
        return ('Leg.L' if x < 0 else 'Leg.R', 'Hips')
    if z < 0.34:
        return ('Hips', 'Spine')           # 袍身中央 + 喇叭下摆：只跟 Hips/Spine，永不绑腿
    # 躯干竖柱
    if z >= 0.88:
        return (('Head' if (abs(x) < 0.13 and abs(y) < 0.13) else 'Neck'), 'Chest')
    if z >= 0.72: return ('Chest', 'Spine')
    if z >= 0.50: return ('Spine', 'Chest')
    return ('Hips', 'Spine')

import math
groups = {n: mesh.vertex_groups.new(name=n) for n in HEADS}
counts = {n: 0 for n in groups}
for v in mesh.data.vertices:
    c = v.co
    a, b = candidates(c)
    ha, hb = HEADS[a], HEADS[b]
    da = math.hypot(c.x-ha[0], c.y-ha[1], c.z-ha[2]) or 1e-4
    db = math.hypot(c.x-hb[0], c.y-hb[1], c.z-hb[2]) or 1e-4
    wa, wb = 1.0/da, 1.0/db
    s = wa + wb
    groups[a].add([v.index], wa/s, 'REPLACE')
    groups[b].add([v.index], wb/s, 'REPLACE')
    counts[a] += 1; counts[b] += 1

print("weights:", {k: counts[k] for k in counts})

# ---- 4. 挂 Armature 修改器（导出 glb skin 必需）----
mod = mesh.modifiers.new('Armature', 'ARMATURE')
mod.object = arm_obj
mesh.parent = arm_obj

# ---- 5. 保存 + 导出 ----
bpy.ops.wm.save_as_mainfile(filepath="monarch_rigged.blend")
bpy.ops.export_scene.gltf(filepath="monarch_rigged.glb", export_format='GLB')
print("SAVED blend + EXPORTED glb")
print("DONE")
