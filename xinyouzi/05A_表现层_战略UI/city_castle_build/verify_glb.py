import bpy
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.gltf(filepath="monarch_rigged.glb")

arm_count = 0
skinned = 0
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        arm_count += 1
        print("ARMATURE:", o.name, "bones:", len(o.data.bones))
    if o.type == 'MESH':
        sm = [m for m in o.modifiers if m.type == 'ARMATURE']
        print("MESH:", o.name, "vgroups:", len(o.vertex_groups),
              "armature_mod:", len(sm) > 0)
        if sm: skinned += 1

# 统计每个顶点组权重总和（确认非空）
for o in bpy.data.objects:
    if o.type == 'MESH' and o.vertex_groups:
        tot = {g.name: 0.0 for g in o.vertex_groups}
        for v in o.data.vertices:
            for w in v.groups:
                tot[o.vertex_groups[w.group].name] += w.weight
        print("weight sum:", {k: round(val,1) for k,val in tot.items()})
print("arm_count", arm_count, "skinned", skinned)
print("DONE")
