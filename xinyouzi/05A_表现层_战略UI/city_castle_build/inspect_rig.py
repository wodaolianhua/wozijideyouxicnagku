import bpy

glb = r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/monarch.glb"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)

print("==== OBJECTS IN monarch.glb ====")
for obj in bpy.data.objects:
    print("OBJ:", repr(obj.name), "TYPE:", obj.type)
    if obj.type == 'MESH':
        d = obj.data
        print("  verts:", len(d.vertices), "edges:", len(d.edges), "faces:", len(d.polygons))
        bb = obj.bound_box
        xs = [v[0] for v in bb]; ys = [v[1] for v in bb]; zs = [v[2] for v in bb]
        print("  bbox  x:[%.3f, %.3f] y:[%.3f, %.3f] z:[%.3f, %.3f]" % (
            min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
        print("  materials:", [m.name for m in obj.data.materials])
    if obj.type == 'ARMATURE':
        print("  bones:", [b.name for b in obj.data.bones])
print("==== END ====")
