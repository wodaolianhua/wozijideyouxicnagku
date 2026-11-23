import bpy

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=r"E:/xinyouzi/05A_表现层_战略UI/city_castle_build/monarch.glb")
mesh = [o for o in bpy.data.objects if o.type=='MESH'][0]
verts = mesh.data.vertices

bands = [(0.0,0.15),(0.15,0.35),(0.35,0.5),(0.5,0.65),(0.65,0.82),(0.82,0.95),(0.95,1.06)]
print("z-band        X-range            Y-range            nVerts")
for lo,hi in bands:
    xs=[]; ys=[]; n=0
    for v in verts:
        z=v.co.z
        if lo<=z<hi:
            xs.append(v.co.x); ys.append(v.co.y); n+=1
    if n==0:
        print(f"[{lo:.2f},{hi:.2f}]  (empty)"); continue
    print(f"[{lo:.2f},{hi:.2f}]  X[{min(xs):.3f},{max(xs):.3f}]  Y[{min(ys):.3f},{max(ys):.3f}]  n={n}")

# 腿部分裂检测：z<0.35 时按 X 聚类
print("\n腿部分裂 (z<0.35) X 直方图:")
buckets={}
for v in verts:
    if v.co.z<0.35:
        b=round(v.co.x*4)/4  # 0.25 桶
        buckets[b]=buckets.get(b,0)+1
for k in sorted(buckets): print(f"  x≈{k:+.2f}: {buckets[k]}")
print("\n手臂检测 (z 0.5-0.85) X 极值附近采样:")
arm=[v.co for v in verts if 0.5<=v.co.z<0.85]
if arm:
    print("  X max:",round(max(v.x for v in arm),3),"X min:",round(min(v.x for v in arm),3))
    print("  Y max:",round(max(v.y for v in arm),3),"Y min:",round(min(v.y for v in arm),3))
print("DONE")
