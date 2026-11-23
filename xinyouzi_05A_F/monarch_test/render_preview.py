import bpy
import mathutils, os

SRC = r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/monarch.glb"
OUT = r"E:/xinyouzi/05A_表现层_战略UI/monarch_test"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x = 640
scene.render.resolution_y = 880
scene.render.image_settings.file_format = 'PNG'

# 灯光 & 世界
world = bpy.data.worlds.new('W'); world.use_nodes = True
scene.world = world
world.node_tree.nodes['Background'].inputs[0].default_value = (0.18,0.2,0.25,1)
light = bpy.data.lights.new('Sun','SUN'); light.energy = 2.0
lobj = bpy.data.objects.new('Sun', light); scene.collection.objects.link(lobj)
lobj.location = (2, -2, 4)

# 相机
cam = bpy.data.cameras.new('Cam')
cam_obj = bpy.data.objects.new('Cam', cam)
scene.collection.objects.link(cam_obj)
scene.camera = cam_obj
cam_obj.rotation_mode = 'QUATERNION'

def look_from(loc, target):
    cam_obj.location = mathutils.Vector(loc)
    d = mathutils.Vector(target) - mathutils.Vector(loc)
    cam_obj.rotation_quaternion = d.to_track_quat('-Z','Y')

# 正面（朝 +Z 看，相机在 -Y）
look_from((0, -2.6, 0.55), (0, 0, 0.55))
scene.render.filepath = os.path.join(OUT, "preview_front.png")
bpy.ops.render.render(write_still=True)

# 侧面（相机在 -X）
look_from((-2.6, 0, 0.55), (0, 0, 0.55))
scene.render.filepath = os.path.join(OUT, "preview_side.png")
bpy.ops.render.render(write_still=True)

print("RENDERED front+side")
