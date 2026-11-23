import bpy, mathutils, math

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.wm.open_mainfile(filepath="monarch_rigged.blend")
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
scn = bpy.context.scene
bpy.context.view_layer.objects.active = arm; arm.select_set(True)
bpy.ops.object.mode_set(mode='POSE')
pb = arm.pose.bones
ph = 0.0   # t=0 相位：腿一前一后、盆骨侧倾
step = math.sin(ph) * 0.8
pb['Leg.L'].rotation_euler = (step, 0, 0)
pb['Leg.R'].rotation_euler = (-step, 0, 0)
pb['Arm.L'].rotation_euler = (-step*0.8, 0, 0)
pb['Arm.R'].rotation_euler = ( step*0.8, 0, 0)
pb['Hips'].rotation_euler  = (0, 0, math.sin(ph)*0.06)
pb['Spine'].rotation_euler = (0.04, math.sin(ph)*0.05, 0)
pb['Chest'].rotation_euler = (0, -math.sin(ph)*0.04, 0)
pb['Head'].rotation_euler  = (0, math.sin(ph*0.5)*0.06, 0)
bpy.ops.object.mode_set(mode='OBJECT')

scn.render.engine = 'BLENDER_EEVEE'
scn.render.resolution_x, scn.render.resolution_y = 480, 640
scn.render.image_settings.file_format = 'PNG'
scn.world = bpy.data.worlds.new('W'); scn.world.use_nodes = False; scn.world.color = (0.2,0.22,0.28)
light = bpy.data.lights.new('L','SUN'); lobj=bpy.data.objects.new('L',light); scn.collection.objects.link(lobj)
lobj.location=(2,-2,4); lobj.rotation_euler=(0.6,0,0.4)
cam=bpy.data.cameras.new('C'); cam_obj=bpy.data.objects.new('C',cam); scn.collection.objects.link(cam_obj)
scn.camera=cam_obj; cam_obj.rotation_mode='QUATERNION'
outdir="E:/xinyouzi/05A_表现层_战略UI/city_castle_build/"
target=mathutils.Vector((0,0,0.6))
for name,loc in [('pose_front',mathutils.Vector((0,-3.2,0.6))),('pose_side',mathutils.Vector((3.2,0,0.6)))]:
    cam_obj.location=loc
    cam_obj.rotation_quaternion=(target-loc).to_track_quat('-Z','Z')
    bpy.context.view_layer.update()
    scn.render.filepath=outdir+name+'.png'
    bpy.ops.render.render(write_still=True)
    print("RENDERED",name)
print("DONE")
