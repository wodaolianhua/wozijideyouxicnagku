import bpy, os

raw = r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/castle_raw.glb"
out = r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/castle_ai.glb"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=raw)

meshes = [o for o in bpy.data.objects if o.type == 'MESH']
print("RAW_MESH_COUNT", len(meshes))
raw_faces = sum(len(o.data.polygons) for o in meshes)
print("RAW_FACES", raw_faces)

for o in meshes:
    o.select_set(True)
if meshes:
    bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def bbox():
    minx=miny=minz=1e9; maxx=maxy=maxz=-1e9
    for o in meshes:
        for v in o.data.vertices:
            x,y,z=v.co
            minx=min(minx,x); miny=min(miny,y); minz=min(minz,z)
            maxx=max(maxx,x); maxy=max(maxy,y); maxz=max(maxz,z)
    return (minx,miny,minz,maxx,maxy,maxz)

minx,miny,minz,maxx,maxy,maxz = bbox()
height = maxz - minz
radius = max(abs(minx), abs(maxx), abs(minz), abs(maxz))
print("RAW_HEIGHT", round(height,3), "RAW_RADIUS", round(radius,3))

# scale so it fits the game footprint (R<=11.5, H<=12.5)
target_r = 11.0
target_h = 11.5
s = min(target_r/radius, target_h/height)
print("SCALE", round(s,4))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.transform.resize(value=(s,s,s))
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# re-ground on z=0 and center X/Y
minx,miny,minz,maxx,maxy,maxz = bbox()
cx=(minx+maxx)/2.0; cy=(miny+maxy)/2.0
bpy.ops.object.select_all(action='SELECT')
bpy.ops.transform.translate(value=(-cx,-cy,-minz))
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

TARGET = 30000
ratio = max(0.01, min(1.0, TARGET/raw_faces)) if raw_faces > 0 else 1.0
print("DECIMATE_RATIO", round(ratio,4))
for o in meshes:
    md = o.modifiers.new("dec", 'DECIMATE')
    md.ratio = ratio
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier="dec")
print("NEW_FACES", sum(len(o.data.polygons) for o in meshes))

try:
    bpy.ops.export_scene.gltf(
        filepath=out, export_format='GLB',
        export_draco_mesh_compression_enable=True,
        export_image_format='WEBP',
    )
except Exception as e:
    print("WEBP_OR_DRACO_FAILED", repr(e))
    bpy.ops.export_scene.gltf(filepath=out, export_format='GLB')
print("EXPORTED", out, "SIZE_MB", round(os.path.getsize(out)/1024/1024,2))
