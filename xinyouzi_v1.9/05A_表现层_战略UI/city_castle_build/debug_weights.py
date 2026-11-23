import bpy

BLEND = r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/monarch_rigged.blend"
bpy.ops.wm.open_mainfile(filepath=BLEND)

mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
print("mesh:", mesh.name, "armature:", arm.name)

# 统计每个顶点组的总权重
grp_sums = {g.index: 0.0 for g in mesh.vertex_groups}
for v in mesh.data.vertices:
    for g in v.groups:
        grp_sums[g.group] += g.weight

print("== 各骨骼顶点组权重总和 ==")
for g in mesh.vertex_groups:
    print("  %-8s total_w=%.3f  verts_in_group=%d" % (
        g.name, grp_sums[g.index],
        sum(1 for v in mesh.data.vertices if any(x.group == g.index for x in v.groups))))

print("mesh has ARMATURE modifier:", any(m.type=='ARMATURE' for m in mesh.modifiers))
print("DONE")
