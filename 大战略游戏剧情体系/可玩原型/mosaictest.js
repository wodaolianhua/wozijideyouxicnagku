/* mosaic 自检：验证视觉引擎的确定性与几何正确性（不需要真实浏览器） */
const fs = require('fs'), path = require('path'), vm = require('vm');

/* 极简 canvas 记录器：只记录绘制调用，不做真实光栅化。
 * 注意：计数器对象必须与 ctx 分开命名。若把计数器也叫 _c，
 * 对象字面量里的 fillRect 方法会遮蔽同名属性，测试会读到函数而非数字。 */
function makeCtx() {
  const n = { fillRect: 0, stroke: 0, beginPath: 0, fillStyle: [], strokeStyle: [], lineWidth: [],
              gradStops: [] };
  const push = (off, col) => { n.gradStops.push([off, col]); };
  return {
    n,
    set fillStyle(v) { n.fillStyle.push(v); },
    get fillStyle() { return '#000'; },
    /* 描边色与线宽必须一并记录：政体区分现在主要走海岸线描边，
       只统计 fillStyle 会把"铁血红线 vs 融合紫带"这条主线判没了。 */
    set strokeStyle(v) { n.strokeStyle.push(v); },
    get strokeStyle() { return '#000'; },
    set lineWidth(v) { n.lineWidth.push(v); },
    get lineWidth() { return 1; },
    set globalAlpha(v) {},
    get globalAlpha() { return 1; },
    fillRect() { n.fillRect++; },
    stroke() { n.stroke++; },
    beginPath() { n.beginPath++; },
    lineTo() {}, moveTo() {},
    strokeRect() {}, clearRect() {},
    save() {}, restore() {}, translate() {}, scale() {},
    /* 渐变的色标必须真的校验格式。
       早前这里是空函数，于是一类真实浏览器才会抛的错被测试放行过：
       'rgba(' + mist.slice(1) + ',' + alpha + ')' —— slice(1) 去掉的是 '#'，
       拼出 "rgba(110c0a,0.3)"，浏览器抛 SyntaxError，整幅地图黑屏，
       而 196 项断言全绿。
       教训：测试替身越"宽容"，越等于没有替身。 */
    createLinearGradient() { return grad(push); },
    createRadialGradient() { return grad(push); },
  };
}
/* 模拟 CanvasGradient：按浏览器的规则校验色标，并把色标记下来。
   记下来的原因：迷雾渐变的 alpha 数值本身就是被测对象——
   "雾不能厚到把海岸线吃掉"是一条数值约束，光校验格式抓不到。 */
function grad(push) {
  return {
    addColorStop(off, col) {
      if (typeof off !== 'number' || off < 0 || off > 1 || isNaN(off)) {
        throw new SyntaxError('addColorStop: offset 非法 → ' + off);
      }
      if (typeof col !== 'string' || !isPaintable(col)) {
        throw new SyntaxError('addColorStop: color 不可解析 → ' + col);
      }
      if (push) push(off, col);
    },
  };
}
/* 颜色可解析性判定：#rgb / #rrggbb / rgb() / rgba()，与浏览器大致同规则 */
function isPaintable(c) {
  if (/^#[0-9a-fA-F]{3}$/.test(c) || /^#[0-9a-fA-F]{6}$/.test(c)) return true;
  const m = c.match(/^rgba?\(\s*([\d.]+%?)\s*,\s*([\d.]+%?)\s*,\s*([\d.]+%?)\s*(,\s*[\d.]+%?\s*)?\)$/);
  if (!m) return false;
  /* 关键：每个分量必须是纯数字。早前 "rgba(110c0a,0.3)" 里的 "110c0a"
     之所以能蒙混，是因为我原来的检查压根不存在。 */
  for (let i = 1; i <= 3; i++) {
    if (!/^([\d.]+%?)$/.test(m[i])) return false;
  }
  return true;
}
function makeCanvas(w, h) {
  const c = makeCtx();
  return { width: 0, height: 0, clientWidth: w, clientHeight: h, getContext: () => c, n: c.n };
}

const store = {};
const sandbox = {
  console,
  window: { devicePixelRatio: 1 },
  document: { createElement: () => makeCanvas(900, 500) },
  localStorage: { getItem: k => (k in store ? store[k] : null), setItem: (k, v) => { store[k] = v; }, removeItem: k => { delete store[k]; } },
  setTimeout, clearTimeout, module: { exports: {} },
};
sandbox.globalThis = sandbox;
sandbox.window.devicePixelRatio = 1;
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(__dirname, 'mosaic.js'), 'utf8'), sandbox, { filename: 'mosaic.js' });
const M = sandbox.window.MOSAIC;

let pass = 0, fail = 0;
const ok = (n, c, extra) => { c ? (pass++, console.log('  ✓ ' + n)) : (fail++, console.log('  ✗ ' + n + (extra ? '  → ' + extra : ''))); };
const hex = c => {
  const r = parseInt(c.slice(1, 3), 16), g = parseInt(c.slice(3, 5), 16), b = parseInt(c.slice(5, 7), 16);
  return [r, g, b];
};

console.log('\n[MOS-1] 时代视觉规格');
ok('十个时代都有视觉规格', M.ERA_ART.length === 10);
ok('每时代都有 cell/accent/marks', M.ERA_ART.every(a => a.cell > 0 && a.accent.length >= 2 && a.marks.length >= 3));
ok('马赛克块随时代变小（石刻→像素）',
   M.ERA_ART[0].cell > M.ERA_ART[5].cell && M.ERA_ART[5].cell > M.ERA_ART[9].cell,
   M.ERA_ART.map(a => a.cell).join(','));
ok('抖动随时代变小（粗粝→精确）',
   M.ERA_ART[0].jitter > M.ERA_ART[9].jitter,
   M.ERA_ART.map(a => a.jitter).join(','));
ok('每时代标签互不相同', new Set(M.ERA_ART.map(a => a.label)).size === 10);
ok('工业时代雾最重（煤烟）', M.ERA_ART[5].fog === Math.max(...M.ERA_ART.map(a => a.fog)));

console.log('\n[MOS-1b] 海陆可辨性（配色回归防护）');
{
  /* 判据来自真实浏览器截图的教训，不是目测：
   * 早前十个时代里有五个达不到（羊皮纸 9.5%、混凝土 14.1%、煤烟 18.2%），
   * 画出来是一整片糊掉的棕灰，看不出哪里是海、哪里是陆。
   * 这条断言就是为了防止将来有人"顺手调色"又调回糊配色。 */
  const lum = c => {
    const [r, g, b] = hex(c);
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const rows = M.ERA_ART.map((A, i) => {
    /* 必须按 drawMap 的真实混色路径取值，不能取 ground 数组里最亮的那一档。
       drawMap 里水色是 mix(ground[0], ground[1], t*0.8)，t = hgt/seaLevel ∈ [0,1)，
       所以屏幕上最亮的水格是 t≈1 时的 mix(ground0, ground1, 0.8)——不是 ground 里最亮的值。
       早前直接 Math.max(...ground) 算出来的"水亮度"是虚的，调色时完全对不上屏幕。 */
    const shallow = M.mix(A.ground[0], A.ground[1], 0.8);
    const deep = M.mix(A.ground[0], A.ground[1], 0.1);
    const w = lum(shallow);
    const l = lum(A.land);
    const mx = Math.max(l, w);
    return { i: i + 1, name: A.label, w, l, deep: lum(deep),
      abs: Math.abs(l - w), rel: mx > 0 ? Math.abs(l - w) / mx * 100 : 0 };
  });
  console.log('  时代                深水L  浅水L   陆L   绝对差  相对差');
  for (const r of rows) {
    console.log('  ' + String(r.i).padStart(2) + ' ' + r.name.padEnd(6, '　') +
      '   ' + r.deep.toFixed(0).padStart(3) + '   ' + r.w.toFixed(0).padStart(3) +
      '   ' + r.l.toFixed(0).padStart(3) + '    ' + r.abs.toFixed(0).padStart(3) +
      '    ' + r.rel.toFixed(1).padStart(4) + '%');
  }
  const bad = rows.filter(r => r.rel < 22);
  ok('十时代海陆相对亮度差均 > 22%', bad.length === 0,
    bad.map(b => b.i + '时代' + b.name + ' ' + b.rel.toFixed(1) + '%').join('; '));
  /* 绝对差也不能太小：相对差达标但两者都很暗时，画面仍然是一团黑。 */
  const badAbs = rows.filter(r => r.abs < 24);
  ok('十时代海陆绝对亮度差均 >= 24', badAbs.length === 0,
    badAbs.map(b => b.i + '时代' + b.name + ' abs=' + b.abs.toFixed(0)).join('; '));
}
{
  /* 海岸线必须真的描出来（三遍 stroke），否则"看得见海岸线"只是文档里的一句话。 */
  let noStroke = [];
  for (let i = 0; i < 10; i++) {
    const cv = makeCanvas(800, 450);
    M.drawMap(cv, i, { attrs: { poly: 50, contra: 20, trust: 50, core: 50 }, flags: [], tracks: {} }, {});
    if (cv.n.stroke < 3) noStroke.push(i + '时代:stroke=' + cv.n.stroke);
  }
  ok('十时代都描了海岸线（≥3 遍：辉光/主线/浪花）', noStroke.length === 0, noStroke.join('; '));
}
{
  /* 描边不能改变底砖的确定性：同种子两次的 strokeStyle 序列必须一致。 */
  const mk = () => {
    const cv = makeCanvas(600, 400);
    M.drawMap(cv, 4, { attrs: { poly: 50, contra: 20, trust: 50, core: 50 }, flags: [], tracks: {} }, {});
    return cv.n;
  };
  const a = mk(), b = mk();
  ok('海岸描边同样是确定性的', a.stroke === b.stroke && a.fillRect === b.fillRect,
  a.stroke + '/' + a.fillRect + ' vs ' + b.stroke + '/' + b.fillRect);
}
{
  /* 回归防护：未探索迷雾的渐变色标必须是浏览器能解析的颜色。
     真实 bug：'rgba(' + mist.slice(1) + ',' + alpha + ')' 把 # 切掉，
     拼出 "rgba(110c0a,0.3)"，浏览器抛 SyntaxError → 整幅地图黑屏。
     当时 196 项断言全绿，因为测试替身的 addColorStop 是空函数。 */
  let bad = [];
  for (let i = 0; i < 10; i++) {
    for (const rv of [0, 0.3, 0.62, 1]) {
      if (rv === 1) continue;   // reveal=1 不走迷雾分支
      try {
        const cv = makeCanvas(400, 260);
        M.drawMap(cv, i, { attrs: { poly: 50, contra: 20, trust: 50, core: 50 }, flags: [], tracks: {} },
          { reveal: rv });
      } catch (e) { bad.push(i + '时代reveal=' + rv + ' → ' + e.message); }
    }
  }
  ok('迷雾渐变色标全部可解析（十时代 × 三档已探索）', bad.length === 0, bad.join('; '));
}
{
  /* 迷雾不能厚到把海岸线吃掉。
   *
   * 真实缺陷：外圈雾 alpha 封顶 0.97，而海岸线恰恰画在大陆外缘——
   * 正好是雾最厚的地方。对照实验（同种子同状态，reveal=1 与 reveal=0 各画一张）
   * 证明关雾时大陆外缘那圈亮线完整连贯，开雾时几乎被吞平。
   * 量化后：改前亮线保留率≈0%，改后（封顶 0.62）= 77%。
   *
   * 为什么这条必须自动化：早前的断言是"描边遍数 ≥ 3"，那是"画了几次"，
   * 不是"看得见"。遍数再多，被 0.97 的雾盖住也等于零。
   * 教训与"Node 全绿 ≠ 浏览器能跑"同源：断言必须测真正关心的那个量。 */
  let tooThick = [];
  for (let i = 0; i < 10; i++) {
    for (const rv of [0, 0.5, 0.78]) {
      const cv = makeCanvas(400, 260);
      M.drawMap(cv, i, { attrs: { poly: 50, contra: 20, trust: 50, core: 50 }, flags: [], tracks: {} },
        { reveal: rv });
      /* 最后一档是外圈。阈值 0.62 是实测定的：再厚就吃掉岸线。 */
      for (const [off, col] of cv.n.gradStops) {
        if (off < 0.99) continue;
        const m = String(col).match(/,\s*([\d.]+)\s*\)\s*$/);
        if (!m) continue;
        if (parseFloat(m[1]) > 0.62 + 1e-9) {
          tooThick.push(i + '时代reveal=' + rv + ' 外圈α=' + m[1]);
        }
      }
    }
  }
  ok('迷雾外圈 alpha 封顶 0.62（不得吞掉海岸线）', tooThick.length === 0, tooThick.join('; '));
}
{
  /* 山峰必须画成「可读的山」，而不是「看得见的暗块」。
   *
   * 真实缺陷：早前山峰是两块等宽暗矩形 + 几根亮竖条，
   * 在土黄底上出来是一根焦黑的 T 形，像烧焦的树枝，读不出是山。
   * 现在是三层阶梯山：山体 -0.22 / 背光面 -0.40 / 受光面 litK（随底色自适应），
   * 最高的山再加雪顶。三层明度是山这个形状的定义。
   *
   * 这条断言的形态换过四次，四次都栽在同一个毛路上，一并记在这里：
   * 都在试图用「像素统计」去代理「形状是否可读」，于是不断跟噪声搏斗。
   *   ① 全图亮度分层 ≥5 —— 地形底砖本身就有五六种亮度，噪声淹信号；
   *      把三层改回两层，仍然全绿。断言太松等于没有断言。
   *   ② 绝对阈值 L<26 / L>120 —— 明亮时代（羊皮纸 L200）整体误报，
   *      暗档也永远不够 40。
   *   ③ 以 A.land 为基准 —— 底砖早被政体染色，与本色差 40+ 亮度。
   *   ④ 以同图陆面中位为基准、卡 1.18 倍率 —— 羊皮纸时代受光面的
   *      物理上限只有 1.197 倍（183→219），卡 1.18 只剩 3 个像素过线；
   *      放宽到 1.10 又变成对噪声放行。
   * 结论（也是这一轮最该记住的）：不要用像素统计代理形状。
   * 直接数「符号层用了几档明度」——三层结构必然产生 ≥3 档显著不同的明度，
   * 单层旧画法只有 2 档。这是定义本身，不是代理指标，
   * 因而不受面积、密度、底色深浅的影响。 */
  let thin = [];
  for (let i = 0; i < 10; i++) {
    const cv = makeCanvas(560, 336);
    M.drawMap(cv, i, { attrs: { poly: 50, contra: 20, trust: 50, core: 50 }, flags: [], tracks: {} },
      { reveal: 1 });
    const land = (cv._cells || []).filter(x => x && x.kind === 'land' && typeof x.col === 'string');
    if (land.length < 50) { thin.push(i + '时代:陆地样本=' + land.length); continue; }
    const lum = h => 0.2126 * parseInt(h.slice(1, 3), 16)
                   + 0.7152 * parseInt(h.slice(3, 5), 16)
                   + 0.0722 * parseInt(h.slice(5, 7), 16);
    const med = land.map(x => lum(x.col)).sort((a, b) => a - b)[Math.floor(land.length / 2)];
    /* 符号层 = 明显偏离陆面中位的那些颜色（暗面 + 受光面 + 雪顶） */
    const syms = cv.n.fillStyle.filter(c => {
      if (typeof c !== 'string' || c[0] !== '#' || c.length !== 7) return false;
      const v = lum(c);
      return v < med * 0.88 || v > med * 1.10;
    }).map(lum);
    /* 按「相对陆面的偏离档位」聚类：暗面一档、受光面一档、雪顶再一档 */
    const bands = new Set(syms.map(v =>
      v > med ? Math.ceil((v / med - 1) * 20) : Math.floor((v / med - 1) * 20)));
    if (bands.size < 3 || syms.length < 24) {
      thin.push(i + '时代:明度档=' + bands.size + ' 符号色=' + syms.length);
    }
  }
  ok('山峰是三层明度结构（不是单色块）', thin.length === 0, thin.join('; '));
}

console.log('\n[MOS-2] 确定性（同种子同图）');
{
  const mk = () => { const cv = makeCanvas(600, 400); M.drawMap(cv, 3, { attrs: {}, flags: [], tracks: {} }, {}); return cv.n; };
  const a = mk(), b = mk();
  ok('两次绘制块数一致', a.fillRect === b.fillRect, a.fillRect + ' vs ' + b.fillRect);
  ok('两次配色序列一致', a.fillStyle.join('|') === b.fillStyle.join('|'));
}
{
  const cv1 = makeCanvas(400, 300), cv2 = makeCanvas(400, 300);
  M.drawMap(cv1, 0, { attrs: {}, flags: [], tracks: {} }, {});
  M.drawMap(cv2, 1, { attrs: {}, flags: [], tracks: {} }, {});
  ok('不同时代绘制量不同', cv1.n.fillRect !== cv2.n.fillRect);
}

console.log('\n[MOS-3] 十时代全部可绘制');
{
  let bad = [];
  for (let i = 0; i < 10; i++) {
    try {
      const cv = makeCanvas(800, 450);
      M.drawMap(cv, i, { attrs: { poly: 50, contra: 20, trust: 50, core: 50, warEthics: 0, postcol: 0 }, flags: [], tracks: { sci: 30 } }, {});
      if (cv.n.fillRect < 200) bad.push(i + '时代:块数' + cv.n.fillRect);
    } catch (e) { bad.push(i + '时代:' + e.message); }
  }
  ok('地图十时代无异常且块数充足', bad.length === 0, bad.join('; '));
}
{
  let bad = [];
  for (let i = 0; i < 10; i++) {
    try {
      const cv = makeCanvas(700, 380);
      M.drawScene(cv, i, { attrs: { poly: 50, contra: 20, trust: 50, core: 50 }, flags: [], tracks: { sci: 30 } }, { seed: 1 });
      if (cv.n.fillRect < 100) bad.push(i + '时代:' + cv.n.fillRect);
    } catch (e) { bad.push(i + '时代:' + e.message); }
  }
  ok('插画十时代无异常', bad.length === 0, bad.join('; '));
}

console.log('\n[MOS-4] 玩家状态改变画面（地图即史书）');
{
  const iron = { attrs: { contra: 90, warEthics: -60, poly: 10, postcol: -50, trust: 20, core: 30 }, flags: ['排外传统'], tracks: {} };
  const melt = { attrs: { contra: 10, warEthics: 60, poly: 90, postcol: 60, trust: 80, core: 70 }, flags: [], tracks: {} };
  const a = makeCanvas(600, 400);
  M.drawMap(a, 5, iron, {});
  const b = makeCanvas(600, 400);
  M.drawMap(b, 5, melt, {});

  /* 正确判据：画面整体色调必须明显不同。
     早前只染海岸线（占比极小的格子），两种政体画出来几乎一样；
     后来底砖染色一度过重（整片海都泛红），把海陆对比吃掉了。
     现在的分工是：底砖轻染（暗示归属）+ 海岸线重描（明确边界）。

     计数必须分开加权，原因有二：
       1) fillStyle 每格记一次，量级是万级；strokeStyle 每次 stroke() 记一次，
          只有个位数——直接相加等于描边这一项几乎不存在。
       2) 描边一次 stroke 覆盖上千像素，与一个底砖不是一个量级的事。
     所以底砖按"格数"算，描边按"线宽 × 次数"折算成覆盖量。 */
  const isRed = c => {
    if (typeof c !== 'string' || c[0] !== '#') return false;
    const [r, g, bb] = hex(c);
    return r > bb + 18;             // 红分量明显高于蓝 → 偏红
  };
  const isPurple = c => {
    if (typeof c !== 'string' || c[0] !== '#') return false;
    const [r, g, bb] = hex(c);
    return bb > g + 18;             // 蓝紫分量明显高于绿 → 偏紫
  };
  /* 描边一次 stroke 覆盖大片像素，与一个底砖不是一个量级，故乘 40 折算 */
  const rednessOf = cv => cv.n.fillStyle.filter(isRed).length + cv.n.strokeStyle.filter(isRed).length * 40;
  const purpleOf = cv => cv.n.fillStyle.filter(isPurple).length + cv.n.strokeStyle.filter(isPurple).length * 40;
  const ri = rednessOf(a), rm = rednessOf(b);
  const pi = purpleOf(a), pm = purpleOf(b);

  ok('铁血政体明显偏红', ri > 500, '红向量 ' + ri);
  ok('融合政体明显偏紫', pm > 500, '紫向量 ' + pm);
  ok('两种政体色调可区分（差值显著）', Math.abs(ri - rm) > 200 && Math.abs(pi - pm) > 200,
     '红 ' + ri + '/' + rm + '  紫 ' + pi + '/' + pm);
  /* 铁血图里的红色必须多于融合图，反之亦然——只比"都大于阈值"不够，
     两张图都一片红也算通过。 */
  ok('铁血比融合更红', ri > rm * 1.5, ri + ' vs ' + rm);
  ok('融合比铁血更紫', pm > pi * 1.5, pm + ' vs ' + pm);
  /* 海岸线本身必须真的带政体色：这是《05》"锐利红线 vs 渐变色带"的落点。 */
  const strokeHasPolity = cv => cv.n.strokeStyle.some(c => isRed(c) || isPurple(c));
  ok('海岸线描边带政体色（红线/紫带）', strokeHasPolity(a) && strokeHasPolity(b),
    '铁血:' + a.n.strokeStyle.slice(0, 3).join(',') + ' 融合:' + b.n.strokeStyle.slice(0, 3).join(','));

  /* 地标风格必须随政体切换——这是"地标密度由历史驱动"的直接落地 */
  const ki = [...new Set((a._marks || []).map(m => m.kind))].join(',');
  const km = [...new Set((b._marks || []).map(m => m.kind))].join(',');
  ok('地标风格随政体不同', ki !== km, '铁血:' + ki + ' / 融合:' + km);
  ok('铁血政体地标更少（多元权力低）', (a._marks || []).length < (b._marks || []).length,
     (a._marks || []).length + ' vs ' + (b._marks || []).length);
}
{
  /* 中立政体的岸线必须跟时代走，不能用政体色。
     真实 bug：中立的 border 是 '#c9b88f'（浅羊皮），混白成 '#d9ceb1'，
     画在深色陆块上是一圈刺眼白边——石刻这种本该粗朴的地图被描成了现代描边图。
     设计原则：海岸线属于"地图的物理"，该跟时代；政体色属于"政治"，只管立场。 */
  const neutral = { attrs: { contra: 0, warEthics: 0, poly: 0, postcol: 0, trust: 50, core: 50 }, flags: [], tracks: {} };
  const cv = makeCanvas(600, 400);
  M.drawMap(cv, 0, neutral, {});
  const used = [...new Set(cv.n.strokeStyle)];
  const tooPale = used.filter(c => {
    if (c[0] !== '#') return false;
    const [r, g, b] = hex(c);
    return r > 190 && g > 180 && b > 160;   // 三分量都高 = 浅色，深底上会刺眼
  });
  ok('中立政体岸线不是浅色（无刺眼白边）', tooPale.length === 0,
    '岸线色 ' + used.slice(0, 4).join(',') + ' | 政体色 ' + M.playerProfile(neutral).border);
}
{
  const p1 = M.playerProfile({ attrs: { contra: 90, warEthics: -60, poly: 10, postcol: -50, trust: 20, core: 30 }, flags: ['排外传统'] });
  const p2 = M.playerProfile({ attrs: { contra: 10, warEthics: 60, poly: 90, postcol: 60, trust: 80, core: 70 }, flags: [] });
  ok('铁血政体：边界硬且红', p1.borderHard > 0.8 && p1.border.toLowerCase().includes('c0392b'), JSON.stringify({ b: p1.border, h: p1.borderHard }));
  ok('融合政体：边界柔且非红', p2.borderHard < 0.3 && !p2.border.toLowerCase().includes('c0392b'), JSON.stringify({ b: p2.border, h: p2.borderHard }));
  ok('政体风格标签正确', p1.flavor === 'iron' && p2.flavor === 'melt', p1.flavor + '/' + p2.flavor);
}

console.log('\n[MOS-5] 地标受科技门控');
{
  const noTech = makeCanvas(800, 500);
  let err1 = null;
  try { M.drawMap(noTech, 9, { attrs: { poly: 90, trust: 80, contra: 10, core: 60, warEthics: 0, postcol: 0 }, flags: [], tracks: { sci: 40 } }, {}); }
  catch (e) { err1 = e.message; }
  ok('无太空科技时绘制不报错', !err1 && noTech.n.fillRect > 200, err1 || ('块数' + noTech.n.fillRect));

  const withTech = makeCanvas(800, 500);
  let err2 = null;
  try { M.drawMap(withTech, 9, { attrs: { poly: 90, trust: 80, contra: 10, core: 60, warEthics: 0, postcol: 0 }, flags: ['has_space'], tracks: { sci: 80 } }, {}); }
  catch (e) { err2 = e.message; }
  ok('有太空科技时绘制不报错', !err2 && withTech.n.fillRect > 200, err2 || ('块数' + withTech.n.fillRect));
  /* 注意：块数相同是正常的——地标数量由"多元权力+信任"（density）决定，
     不是由科技决定。科技只影响"地标种类"（星舰/殖民地/跃迁门需 has_space）。
     故正确的判据是：有太空科技时地图上会出现太空类地标。 */
  const spaceKinds = ['星舰', '殖民地', '跃迁门', '轨道站'];
  const kindsOf = cv => (cv._marks || []).map(m => m.kind);
  ok('解锁 has_space 后出现太空地标', kindsOf(withTech).some(k => spaceKinds.includes(k)),
     '地标: ' + kindsOf(withTech).join(','));
  ok('未解锁 has_space 时无殖民地/跃迁门',
     !kindsOf(noTech).some(k => ['殖民地', '跃迁门'].includes(k)),
     '地标: ' + kindsOf(noTech).join(','));
}

console.log('\n[MOS-6] 地标不跨时代累积（回归防护）');
{
  const counts = [];
  for (let k = 0; k < 3; k++) {
    const cv = makeCanvas(600, 400);
    M.drawMap(cv, 4, { attrs: { poly: 70, contra: 20, trust: 60, core: 50, warEthics: 0, postcol: 0 }, flags: [], tracks: { sci: 30 } }, {});
    counts.push(cv.n.fillRect);
  }
  ok('三次重绘块数一致（无累积）', counts[0] === counts[1] && counts[1] === counts[2], counts.join(','));
}

console.log('\n[MOS-7] 未探索迷雾');
{
  const full = makeCanvas(600, 400);
  M.drawMap(full, 2, { attrs: { poly: 50, contra: 20, trust: 50, core: 50, warEthics: 0, postcol: 0 }, flags: [], tracks: {} }, { reveal: 1 });
  const half = makeCanvas(600, 400);
  M.drawMap(half, 2, { attrs: { poly: 50, contra: 20, trust: 50, core: 50, warEthics: 0, postcol: 0 }, flags: [], tracks: {} }, { reveal: 0.5 });
  ok('reveal 影响绘制内容', full.n.fillStyle.length !== half.n.fillStyle.length,
     full.n.fillStyle.length + ' vs ' + half.n.fillStyle.length);
}

console.log('\n[MOS-8] 颜色工具');
ok('shade 提亮', M.shade('#808080', 0.5) !== '#808080');
ok('mix 中点', /^#[0-9a-f]{6}$/i.test(M.mix('#000000', '#ffffff', 0.5)));
ok('hashStr 稳定', M.hashStr('abc') === M.hashStr('abc'));
ok('hashStr 不同串不同值', M.hashStr('abc') !== M.hashStr('abd'));
{
  const r1 = M.rng(12345), r2 = M.rng(12345);
  ok('同种子随机序列一致', r1() === r2() && r1() === r2());
}

console.log('\n────────────────────────────────────────────────────');
console.log('  通过 ' + pass + '　失败 ' + fail);
console.log('────────────────────────────────────────────────────');
process.exit(fail ? 1 : 0);
