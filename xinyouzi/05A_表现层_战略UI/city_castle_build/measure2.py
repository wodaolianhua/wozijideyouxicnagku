import bpy, bmesh

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.gltf(filepath="monarch.glb")

mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
bm = bmesh.new(); bm.from_mesh(mesh.data)
coords = [v.co.copy() for v in bm.verts]
ys = [c.y for c in coords]; xs = [c.x for c in coords]; zs = [c.z for c in coords]
ymin, ymax = min(ys), max(ys); xmin, xmax = min(xs), max(xs); zmin, zmax = min(zs), max(zs)
print("YMIN", round(ymin,3), "YMAX", round(ymax,3))
print("XMIN", round(xmin,3), "XMAX", round(xmax,3))
print("ZMIN", round(zmin,3), "ZMAX", round(zmax,3))

H = ymax - ymin
for i in range(10):
    lo = ymin + H*i/10; hi = ymin + H*(i+1)/10
    seg = [c for c in coords if lo <= c.y < hi]
    if not seg: continue
    sx = [c.x for c in seg]; sz = [c.z for c in seg]
    print(f"band{i} y[{lo:.2f},{hi:.2f}] X[{min(sx):.2f},{max(sx):.2f}] Z[{min(sz):.2f},{max(sz):.2f}] n={len(seg)}")

bot = [c for c in coords if c.y < ymin + H*0.25]
if bot:
    bx = [c.x for c in bot]
    print("BOTTOM n=", len(bot), "x[", round(min(bx),3), round(max(bx),3), "]")
    left = [c.x for c in bot if c.x < 0]; right = [c.x for c in bot if c.x > 0]
    if left: print("  left x mean", round(sum(left)/len(left),3))
    if right: print("  right x mean", round(sum(right)/len(right),3))

arm = [c for c in coords if (ymin+H*0.45) < c.y < (ymin+H*0.90) and abs(c.x) > 0.14]
if arm:
    ax = [c.x for c in arm]; az = [c.z for c in arm]; ay = [c.y for c in arm]
    print("ARM n=", len(arm), "x[", round(min(ax),3), round(max(ax),3), "] z[", round(min(az),3), round(max(az),3), "] y[", round(min(ay),3), round(max(ay),3), "]")
print("DONE")
