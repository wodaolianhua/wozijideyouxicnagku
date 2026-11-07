import json, os, base64, sys

manifest = sys.argv[1]   # json: [{title, file}]
out_html = sys.argv[2]
items = json.load(open(manifest, encoding='utf-8'))

cards = []
for it in items:
    fn = it['file']
    b64 = base64.b64encode(open(fn, 'rb').read()).decode('ascii')
    src = "data:model/gltf-binary;base64," + b64
    cards.append(
        '<div class="card"><div class="title">' + it['title'] + '</div>'
        '<model-viewer src="' + src + '" camera-controls auto-rotate '
        'shadow-intensity="1" exposure="1" environment-image="neutral"></model-viewer></div>'
    )

html = """<!DOCTYPE html><html><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<script type="module" src="model-viewer.min.js"></script>
<style>
body{font-family:system-ui,'Microsoft YaHei',sans-serif;background:#1b1d23;color:#e8e8ea;margin:0}
h1{padding:16px;font-size:18px;margin:0}
.hint{padding:0 16px 8px;color:#9aa;font-size:13px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:16px;padding:16px}
.card{background:#262a33;border-radius:12px;overflow:hidden;box-shadow:0 4px 16px rgba(0,0,0,.4)}
.title{padding:10px 14px;font-weight:600;font-size:14px;border-bottom:1px solid #333}
model-viewer{width:100%;height:340px;background:linear-gradient(#2a2e38,#15171c)}
</style></head><body>
<h1>AI 重做模型预览</h1>
<div class="hint">拖拽旋转 · 滚轮缩放 · 自动旋转。模型已内嵌，离线也能看。</div>
<div class="grid">""" + ''.join(cards) + "</div></body></html>"

open(out_html, 'w', encoding='utf-8').write(html)
print("WROTE", out_html, "cards", len(items))
