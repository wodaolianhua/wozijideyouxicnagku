import bpy

GLB = r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/monarch_rigged.glb"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)

print("==== monarch_rigged.glb re-import ====")
arm = None
for o in bpy.data.objects:
    print("OBJ:", repr(o.name), "TYPE:", o.type)
    if o.type == 'ARMATURE':
        arm = o
        print("  bones:", [b.name for b in o.data.bones])
    if o.type == 'MESH':
        print("  vertex_groups:", [g.name for g in o.vertex_groups])
        print("  modifiers:", [m.type for m in o.modifiers])
        # check skin flag in gltf: armature modifier present?
        has_arm_mod = any(m.type == 'ARMATURE' for m in o.modifiers)
        print("  has ARMATURE modifier:", has_arm_mod)
print("armature found:", arm is not None)
print("==== END ====")
