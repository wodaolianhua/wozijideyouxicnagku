import bpy

print("==== 在 .blend 里查 ====")
bpy.ops.wm.open_mainfile(filepath=r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/monarch_rigged.blend")
for o in bpy.data.objects:
    par = o.parent.name if o.parent else None
    vcount = len(o.data.vertices) if o.type == 'MESH' else '-'
    print("  %-12s type=%-8s parent=%-10s verts=%s" % (o.name, o.type, par, vcount))

print("==== 在 原始 glb 里查 ====")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/monarch.glb")
for o in bpy.data.objects:
    par = o.parent.name if o.parent else None
    vcount = len(o.data.vertices) if o.type == 'MESH' else '-'
    print("  %-12s type=%-8s parent=%-10s verts=%s" % (o.name, o.type, par, vcount))
print("DONE")
