import bpy
import os
from mathutils import Vector

SRC = r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/monarch.glb"
OUT_DIR = r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build"
BLEND = os.path.join(OUT_DIR, "monarch_rigged.blend")
GLB = os.path.join(OUT_DIR, "monarch_rigged.glb")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]

# ---- 建骨架（按实测坐标摆：X=左右, Y=前后, Z=上；腿在 x≈±0.20 分叉）----
arm_data = bpy.data.armatures.new("Armature")
arm_obj = bpy.data.objects.new("Armature", arm_data)
bpy.context.scene.collection.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_obj.data.edit_bones

def bone(name, head, tail, parent=None):
    b = eb.new(name)
    b.head = Vector(head); b.tail = Vector(tail)
    if parent: b.parent = eb[parent]

bone('Root',  (0, 0, 0),       (0, 0, 0.06))
bone('Hips',  (0, 0, 0.50),    (0, 0, 0.58), 'Root')
bone('Spine', (0, 0, 0.58),    (0, 0, 0.72), 'Hips')
bone('Chest', (0, 0, 0.72),    (0, 0, 0.85), 'Spine')
bone('Neck',  (0, 0, 0.85),    (0, 0, 0.92), 'Chest')
bone('Head',  (0, 0, 0.92),    (0, 0, 1.06), 'Neck')
bone('Arm.L', (-0.20, 0, 0.84), (-0.22, 0.06, 0.62), 'Chest')
bone('Arm.R', (0.20, 0, 0.84),  (0.22, 0.06, 0.62), 'Chest')
bone('Leg.L', (-0.20, 0, 0.50), (-0.21, 0, 0.02), 'Hips')
bone('Leg.R', (0.20, 0, 0.50),  (0.21, 0, 0.02), 'Hips')
bpy.ops.object.mode_set(mode='OBJECT')

# ---- 手动按距离刷权重（自动权重对此网格静默失败）----
while mesh.vertex_groups:
    mesh.vertex_groups.remove(mesh.vertex_groups[0])

mw = mesh.matrix_world
aw = arm_obj.matrix_world
bones = []
for b in arm_obj.data.bones:
    bones.append((b.name, aw @ b.head_local, aw @ b.tail_local))

def dist_seg(p, a, b):
    ab = b - a
    denom = ab.dot(ab)
    if denom < 1e-9:
        return (p - a).length
    t = max(0.0, min(1.0, (p - a).dot(ab) / denom))
    return (p - (a + t * ab)).length

vgs = {name: mesh.vertex_groups.new(name=name) for name, _, _ in bones}
TOPN = 3
for v in mesh.data.vertices:
    p = mw @ v.co
    ds = sorted(((n, dist_seg(p, h, t)) for n, h, t in bones), key=lambda x: x[1])[:TOPN]
    raw = [(n, 1.0 / (d + 0.05)) for n, d in ds]
    s = sum(w for _, w in raw)
    for n, w in raw:
        vgs[n].add([v.index], w / s, 'REPLACE')

mesh.parent = arm_obj
if not any(m.type == 'ARMATURE' for m in mesh.modifiers):
    mod = mesh.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm_obj

# 权重校验
grp_sums = {g.index: 0.0 for g in mesh.vertex_groups}
for v in mesh.data.vertices:
    for g in v.groups:
        grp_sums[g.group] += g.weight
print("== 各骨骼权重总和 ==")
for g in mesh.vertex_groups:
    cnt = sum(1 for v in mesh.data.vertices if any(x.group == g.index for x in v.groups))
    print("  %-8s total=%.1f verts=%d" % (g.name, grp_sums[g.index], cnt))

bpy.ops.wm.save_as_mainfile(filepath=BLEND)
bpy.ops.export_scene.gltf(filepath=GLB, export_format='GLB',
                          export_animations=False, export_skins=True)
print("SAVED:", BLEND)
print("EXPORTED:", GLB)
print("DONE")
