# Blender headless build script: low-poly fantasy castle (Plan A silhouette)
# Output: city_castle.glb  (single mesh, single material, Y-up, base centered on ground)
import bpy, math, os

OUT = r"E:\xinyouzi\05A_表现层_战略UI\city_castle_build\city_castle.glb"

# ---- clean scene ----
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

# ---- one neutral material (plan A: faction color applied at runtime) ----
mat = bpy.data.materials.new("CastleMat")
mat.use_nodes = True
if mat.node_tree:
    for n in mat.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            n.inputs['Base Color'].default_value = (0.80, 0.80, 0.82, 1.0)
            n.inputs['Roughness'].default_value = 0.9
            n.inputs['Metallic'].default_value = 0.0
mat.diffuse_color = (0.80, 0.80, 0.82, 1.0)

def add_box(w, h, d, loc, rotz=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.active_object
    o.scale = (w, h, d)
    o.rotation_euler = (0, 0, rotz)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.append(mat)
    return o

def add_cyl(r, h, seg, loc):
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=h, vertices=seg, location=loc)
    o = bpy.context.active_object
    o.data.materials.append(mat)
    return o

def add_cone(r, h, seg, loc, rotz=0.0):
    bpy.ops.mesh.primitive_cone_add(radius1=r, radius2=0.0, depth=h, vertices=seg, location=loc)
    o = bpy.context.active_object
    o.rotation_euler = (0, 0, rotz)
    bpy.ops.object.transform_apply(rotation=True)
    o.data.materials.append(mat)
    return o

def add_plane(w, h, loc, rotz=0.0):
    bpy.ops.mesh.primitive_plane_add(size=1, location=loc)
    o = bpy.context.active_object
    o.scale = (w, h, 1)
    o.rotation_euler = (0, 0, rotz)
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    o.data.materials.append(mat)
    return o

parts = []

# base platform (footprint 15x15 -> corner radius ~10.6)
parts.append(add_box(15, 15, 1.2, (0, 0, 0.6)))

# 4 corner towers
for sx in (-1, 1):
    for sy in (-1, 1):
        parts.append(add_cyl(1.5, 6.0, 10, (sx * 6.3, sy * 6.3, 3.0)))

# curtain walls (height 4)
parts.append(add_box(15, 1.2, 4, (0, 6.9, 3.2)))
parts.append(add_box(15, 1.2, 4, (0, -6.9, 3.2)))
parts.append(add_box(1.2, 12.6, 4, (6.9, 0, 3.2)))
parts.append(add_box(1.2, 12.6, 4, (-6.9, 0, 3.2)))

# crenellations (merlons) on wall tops
for x in (-6.0, -3.6, -1.2, 1.2, 3.6, 6.0):
    parts.append(add_box(1.0, 0.6, 1.2, (x, 6.9, 5.8)))
    parts.append(add_box(1.0, 0.6, 1.2, (x, -6.9, 5.8)))
for y in (-4.8, -2.4, 0, 2.4, 4.8):
    parts.append(add_box(0.6, 1.0, 1.2, (6.9, y, 5.8)))
    parts.append(add_box(0.6, 1.0, 1.2, (-6.9, y, 5.8)))

# central keep
parts.append(add_box(5, 5, 7, (0, 0, 4.7)))

# keep roof (4-sided pyramid)
parts.append(add_cone(4, 3, 4, (0, 0, 9.7), rotz=math.pi / 4))

# flagpole + plain flag (oriented toward +Z front)
parts.append(add_cyl(0.12, 3.4, 6, (0, 0, 9.9)))
parts.append(add_plane(2.0, 1.2, (1.0, 0, 11.0)))

# ---- consolidate into ONE mesh ----
for o in parts:
    o.data.materials.clear()
    o.data.materials.append(mat)
bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()

mesh_obj = bpy.context.active_object
bpy.ops.object.shade_flat()  # faceted low-poly look

# move origin to base center (0,0,0) without moving geometry
bpy.context.scene.cursor.location = (0, 0, 0)
bpy.ops.object.origin_set(type='ORIGIN_CURSOR')

# ---- stats ----
me = mesh_obj.data
tris = sum(len(p.vertices) - 2 for p in me.polygons)
xs = [v.co.x for v in me.vertices]
ys = [v.co.y for v in me.vertices]
zs = [v.co.z for v in me.vertices]
minz, maxz = min(zs), max(zs)
radius = max(math.sqrt(x * x + y * y) for x, y in zip(xs, ys))
print("STATS faces=%d tris~=%d minz=%.3f maxz=%.3f height=%.3f maxRadius=%.3f" %
      (len(me.polygons), tris, minz, maxz, maxz - minz, radius))

# ---- export GLB (Y-up, transforms baked, self-contained) ----
bpy.ops.export_scene.gltf(
    filepath=OUT,
    export_format='GLB',
    export_yup=True,
    export_apply=True,
    export_cameras=False,
    export_lights=False,
    export_materials='EXPORT',
    export_texcoords=True,
    export_normals=True,
    use_selection=False,
)
print("EXPORTED", OUT, os.path.getsize(OUT), "bytes")
