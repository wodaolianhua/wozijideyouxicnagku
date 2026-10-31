import bpy, mathutils, os

BLEND = r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/monarch_rigged.blend"
OUT = r"E:/xinyouzi/05A_表现层_战略UI/monarch_test"
bpy.ops.wm.open_mainfile(filepath=BLEND)

arm = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x = 640; scene.render.resolution_y = 880
scene.render.image_settings.file_format = 'PNG'

world = bpy.data.worlds.new('W'); world.use_nodes=False; scene.world=world
world.color=(0.18,0.2,0.25)
light = bpy.data.lights.new('Sun','SUN'); light.energy=2.0
lobj=bpy.data.objects.new('Sun',light); scene.collection.objects.link(lobj); lobj.location=(2,-2,4)

cam = bpy.data.cameras.new('Cam'); cam_obj=bpy.data.objects.new('Cam',cam)
scene.collection.objects.link(cam_obj); scene.camera=cam_obj; cam_obj.rotation_mode='QUATERNION'
def look_from(loc,tgt):
    cam_obj.location=mathutils.Vector(loc)
    cam_obj.rotation_quaternion=(mathutils.Vector(tgt)-mathutils.Vector(loc)).to_track_quat('-Z','Y')

# 进入姿态模式摆走路 pose：左腿前、右腿后，手臂反向
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')
pb = arm.pose.bones
for b in pb: b.rotation_mode='XYZ'; b.rotation_euler=(0,0,0)
pb['Leg.L'].rotation_euler.x =  0.5
pb['Leg.R'].rotation_euler.x = -0.5
pb['Arm.L'].rotation_euler.x = -0.25
pb['Arm.R'].rotation_euler.x =  0.25
pb['Chest'].rotation_euler.y =  0.06
bpy.ops.object.mode_set(mode='OBJECT')

look_from((0,-2.6,0.55),(0,0,0.55))
scene.render.filepath=os.path.join(OUT,"pose_walk_front.png")
bpy.ops.render.render(write_still=True)
look_from((-2.6,0,0.55),(0,0,0.55))
scene.render.filepath=os.path.join(OUT,"pose_walk_side.png")
bpy.ops.render.render(write_still=True)
print("POSED+RENDERED")
