/* ============================================================================
 * mosaic.js —— 马赛克视觉引擎（纯 Canvas 程序化生成，零外部图片依赖）
 *
 * 设计依据：
 *   《05》四节视觉："UI 应随时代演进，但保留视觉连续性。从石刻铭文到羊皮纸
 *                    到印刷体到屏幕像素，形式在变，但核心视觉语言保持一致。"
 *   《05》四节地图："边界线的粗细、颜色的饱和度、地名的字体都应随时代与玩家
 *                    选择而变化。"
 *   《07/05》层一："地图即史书——玩家的领土颜色、地标密度、道路纹理全部由
 *                    历史标记驱动。玩家不用读任何文本，他的手知道五千年做了什么。"
 *
 * 核心手法：
 *   一切图像都由"马赛克小块"拼成——每块是一个纯色矩形，可带轻微色差与
 *   边缘抖动，模拟手镶嵌的陶瓷/玻璃马赛克与早期像素画。
 *   每个时代有自己的色板、颗粒大小、抖动幅度与元素类型。
 * ==========================================================================*/

/* --- 确定性随机：同一颗种子必须画出同一张图 ---------------------------------
 * 为什么不用 Math.random：地图会随存档反复重绘，若每次都不同，
 * 玩家会以为世界在自己没做选择时自己变了。用种子保证稳定。 */
function rng(seed) {
  let s = seed >>> 0 || 1;
  return () => {
    s ^= s << 13; s >>>= 0;
    s ^= s >> 17;
    s ^= s << 5;  s >>>= 0;
    return s / 4294967296;
  };
}
const hashStr = str => {
  let h = 2166136261 >>> 0;
  for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); }
  return h >>> 0;
};

/* --- 颜色工具 ---------------------------------------------------------------*/
function hex2rgb(h) {
  h = h.replace('#', '');
  if (h.length === 3) h = h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
  return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)];
}
const rgb2hex = (r, g, b) =>
  '#' + [r, g, b].map(v => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')).join('');

function shade(hex, amt) {
  const [r, g, b] = hex2rgb(hex);
  return amt >= 0
    ? rgb2hex(r + (255 - r) * amt, g + (255 - g) * amt, b + (255 - b) * amt)
    : rgb2hex(r * (1 + amt), g * (1 + amt), b * (1 + amt));
}
function mix(h1, h2, t) {
  const a = hex2rgb(h1), b = hex2rgb(h2);
  return rgb2hex(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t);
}
/* 相对亮度（sRGB 亮度加权近似）。视觉调参的通用尺子：
   "这个色比那个色亮多少"必须能算，不能靠眼睛。 */
function L(hex) {
  const p = hex2rgb(hex);
  return 0.2126 * p[0] + 0.7152 * p[1] + 0.0722 * p[2];
}

/* ============================================================================
 * 十个时代的视觉规格
 * 每一项都是一种"文明自我表达"的形式——从石头到像素，形式在变，
 * 但都是同一种东西：这块地是我的。
 * ==========================================================================*/
/* --- 时代视觉规格 ------------------------------------------------------------
 * 海陆必须一眼分得开。判据（实测得出，不是目测）：
 *   相对亮度差 = |L(land) - L(water)| / max(L) ，需 > 22%
 * 早前十个时代里有五个达不到（羊皮纸 9.5%、混凝土 14.1%、煤烟 18.2%…），
 * 画出来是一整片糊掉的棕灰，看不出哪里是海、哪里是陆——
 * 而"看得见海岸线"是《05》视觉节的第一条要求。
 * 修法：水相压暗压冷、陆相提亮提暖，两头拉开。
 * --------------------------------------------------------------------------*/
const ERA_ART = [
  /* 1 部落崛起 —— 石刻与火 */
  { era:1, name:'部落崛起', key:'stone',
    cell:13, jitter:2.2,                 // 块大、抖得多 → 粗粝的石头感
    ground:['#2e2620','#1f1a16','#3a3028'], land:'#7d6a4f',
    accent:['#c2622a','#e0913c','#f2c14e'],   // 火光
    sky:['#2a1f18','#1a1310'], fog:0.18,
    style:'stone', label:'石刻', marks:['篝火','帐篷','图腾柱','石碑'] },

  /* 2 古典王国 —— 泥板与楔形文字 */
  { era:2, name:'古典王国', key:'clay',
    cell:11, jitter:1.4,
    ground:['#3a4a52','#2a3840','#4a5c64'], land:'#9a7850',
    accent:['#c9a227','#e0b93f','#8a5a2b'],
    sky:['#3d2e20','#2a1f16'], fog:0.12,
    style:'clay', label:'泥板', marks:['神庙','城墙','王座柱','粮仓'] },

  /* 3 铁血与信仰 —— 血色与铁 */
  { era:3, name:'铁血与信仰', key:'iron',
    cell:10, jitter:1.1,
    ground:['#232330','#191922','#2e2e3c'], land:'#5e5e6a',
    accent:['#8b1a1a','#c0392b','#d4af37'],
    sky:['#1c1c22','#121216'], fog:0.16,
    style:'iron', label:'铁与血', marks:['城堡','铁砧','绞架','教堂'] },

  /* 4 古典理性 —— 羊皮纸与柱廊 */
  { era:4, name:'古典理性', key:'parchment',
    cell:10, jitter:0.8,
    /* 羊皮纸本身是浅色，若水也用浅色就完全没有海岸线。
       故水相压成墨蓝（老羊皮上的墨），陆相保持羊皮浅色——"墨与纸"恰是时代特征。 */
    ground:['#2c3a4a','#1e2a38','#3c4c5e'], land:'#d8c89a',
    accent:['#4a6b8a','#7a9bb5','#8b6914'],
    sky:['#ded0a8','#c9b88f'], fog:0.06,
    style:'parchment', label:'羊皮纸', marks:['柱廊','竞技场','神庙','浴场'] },

  /* 5 地理大发现 —— 海图与船帆 */
  { era:5, name:'地理大发现', key:'ocean',
    cell:9, jitter:0.7,
    ground:['#123642','#0a2632','#1a4a58'], land:'#9a8a5e',
    accent:['#c94f2a','#e8a33d','#f0e0c0'],
    sky:['#12303c','#0a1e28'], fog:0.2,
    style:'ocean', label:'海图', marks:['帆船','港口','灯塔','货舱'] },

  /* 6 工业与机器 —— 煤烟与铁轨 */
  { era:6, name:'工业与机器', key:'industrial',
    /* 块最大：煤烟时代的东西最粗糙，机器是整块的。 */
    cell:9, jitter:0.6,
    ground:['#232628','#171819','#303335'], land:'#6b6252',
    accent:['#c46a1a','#e0913c','#6a8a5a'],
    sky:['#2a2a2a','#181818'], fog:0.30,   // 煤烟最重
    style:'industrial', label:'煤烟', marks:['烟囱','水塔','铁轨','厂房'] },

  /* 7 战争与革命 —— 废墟与硝烟 */
  { era:7, name:'战争与革命', key:'war',
    cell:8, jitter:1.5,                  // 抖动最大：碎块 = 废墟
    /* 偏暖的焦土色，与时代6的中性灰拉开。 */
    ground:['#332b24','#221c17','#443a30'], land:'#7a6450',
    accent:['#8b1a1a','#c0392b','#6a6a5a'],
    sky:['#2a2420','#1a1614'], fog:0.22,
    style:'war', label:'硝烟', marks:['废墟','战壕','探照灯','断桥'] },

  /* 8 冷战与核 —— 混凝土与霓虹 */
  { era:8, name:'冷战与核', key:'concrete',
    cell:6, jitter:0.15,                 // 抖动趋近 0：预制板，精确对齐
    /* 冷灰蓝，与时代7的暖焦土正相反。 */
    ground:['#232a33','#171d24','#313a45'], land:'#7c8490',
    accent:['#4aa3c4','#c44a7a','#e0c040'],
    sky:['#20242a','#12151a'], fog:0.10,
    style:'concrete', label:'混凝土', marks:['发射井','天线阵','方楼','铁幕'] },

  /* 9 全球化与网络 —— 光纤与屏幕 */
  { era:9, name:'全球化与网络', key:'fiber',
    cell:6, jitter:0.25,                // 块最小 → 最接近像素
    ground:['#0c1220','#070b14','#141c2e'], land:'#2c4058',
    accent:['#3ddad7','#7a5cff','#ff6a9a'],
    sky:['#0a0e18','#060810'], fog:0.08,
    style:'fiber', label:'光纤', marks:['数据中心','光缆','基站','服务器'] },

  /* 10 星辰大海 —— 屏幕像素 */
  { era:10, name:'星辰大海', key:'pixel',
    cell:5, jitter:0.0,                 // 无抖动 → 最纯粹的像素画
    /* 星辰大海仍是暗色底（太空就是黑的），但两侧都得让开：
       - 陆相太暗 → 画不出海岸线（早前 land:#242440 实测绝对差只有 21）
       - 水相太黑 → 陆块像粉紫岛屿浮在黑布上（实测最亮水格 L 仅 17）
       配色的坑：水色是 mix(ground[0], ground[1], t*0.8) 现算的，
       所以真正出现在屏幕上的最亮水格 ≈ mix(ground0, ground1, 0.8)，
       不是 ground 数组里最亮的那一档。按实际值配：
       深水 0d1424(L20) → 浅水 243858(L54)，陆 3a3a68(L61)，实际差 24~41。 */
    ground:['#0d1424','#16233c','#111a2e'], land:'#3a3a68',
    accent:['#5ce1e6','#a06cff','#ff5c8a','#ffe66d'],
    sky:['#0a0d1c','#05070f'], fog:0.02,
    style:'pixel', label:'屏幕像素', marks:['星舰','轨道站','殖民地','跃迁门'] },
];

/* --- 地形高度场：让地图不是随机噪点，而是有大陆、有海岸、有山脊 --------------*/
function terrain(seed, cols, rows) {
  const r = rng(seed);
  const h = [];
  /* 值噪声：多倍频叠加，天然形成"大形 + 细节"的层次。
     注意：每个倍频的网格必须一次性生成、只 push (f+1)² 个值，
     再用双线性插值取样。早前在每个格子里重新 push 整个网格，
     既让噪声随位置抖动（画质问题），又因行跨度算错而全部产出 NaN
     —— 地形整体失效，政体染色全部变 #NaNNaN，地图画出来是一片黑。 */
  const oct = [[3, 1.0], [7, 0.5], [15, 0.25], [31, 0.12]];
  /* 预先为每个倍频生成网格（只生成一次） */
  const grids = oct.map(([f]) => {
    const g = new Array((f + 1) * (f + 1));
    for (let i = 0; i < g.length; i++) g[i] = r();
    return g;
  });
  for (let y = 0; y < rows; y++) {
    const row = [];
    for (let x = 0; x < cols; x++) {
      let v = 0, tot = 0;
      for (let oi = 0; oi < oct.length; oi++) {
        const f = oct[oi][0], amp = oct[oi][1], g = grids[oi], W1 = f + 1;
        /* 取样坐标：用 %f 让噪声在边界环绕，避免边缘出现硬缝 */
        const fx = (x / cols) * f, fy = (y / rows) * f;
        const ix = Math.floor(fx) % f, iy = Math.floor(fy) % f;
        const tx = fx - Math.floor(fx), ty = fy - Math.floor(fy);
        /* smoothstep：让插值平滑，消除网格状硬边 */
        const sx = tx * tx * (3 - 2 * tx), sy = ty * ty * (3 - 2 * ty);
        const a = g[iy * W1 + ix], b = g[iy * W1 + ((ix + 1) % f)];
        const c = g[((iy + 1) % f) * W1 + ix], d = g[((iy + 1) % f) * W1 + ((ix + 1) % f)];
        /* 双线性插值 */
        v += (a + (b - a) * sx + (c - a) * sy + (a - b - c + d) * sx * sy) * amp;
        tot += amp;
      }
      row.push(v / tot);
    }
    h.push(row);
  }
  /* 岛缘衰减：让四边沉入海/雾，地图有明确边界而非铺满。
     早前是方形衰减、衰减带只有 20% 宽——大陆一直连到画布边，
     整幅看着像"一块大陆"而不是"世界"。
     改成径向衰减：中心是大陆腹地，向四周渐次沉入大洋。
     调参经过两次：底值 0.30 + 0.62 宽的衰减带太强了，大陆被压成居中的方块，
     像沙盘而不是地图。现在底值 0.46、带宽 0.46——边缘确实沉下去了，
     但陆块保持不规则、一直延伸到画框，海陆关系更像真实世界地图。 */
  for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) {
    const nx = (x / (cols - 1)) * 2 - 1, ny = (y / (rows - 1)) * 2 - 1;
    const d = Math.max(Math.abs(nx), Math.abs(ny));
    const k = Math.max(0, Math.min(1, (1 - d) / 0.46));
    h[y][x] *= 0.46 + 0.54 * (k * k * (3 - 2 * k));
  }
  return h;
}

/* ============================================================================
 * 画一块马赛克地图
 * @param cv      canvas
 * @param eraIdx  0..9
 * @param state   玩家状态（决定地形染色与地标）
 * @param opt     { reveal: 0..1 已探索比例, hover: {x,y}|null, reduced:bool }
 * ==========================================================================*/
function drawMap(cv, eraIdx, state, opt) {
  const A = ERA_ART[eraIdx] || ERA_ART[0];
  const o = opt || {};
  /* opt.w / opt.h 允许显式指定尺寸。离屏缓存画布没有 clientWidth，
     若只依赖 clientWidth 会退回默认 900×500，缓存与可见画布就对不上。 */
  const W = o.w || cv.clientWidth || 900, H = o.h || cv.clientHeight || 500;
  const dpr = o.dpr || Math.min(2, window.devicePixelRatio || 1);
  cv.width = W * dpr; cv.height = H * dpr;
  const g = cv.getContext('2d');
  g.scale(dpr, dpr);

  const seed = hashStr('era' + eraIdx + '|' + (state ? state.era : 1));
  const cols = Math.max(24, Math.round(W / A.cell));
  const rows = Math.max(14, Math.round(H / A.cell));
  const cw = W / cols, ch = H / rows;
  const H0 = terrain(seed, cols, rows);
  const r = rng(seed ^ 0x9e37);

  /* --- 玩家状态 → 政权特征量。地图的每一处颜色都由此而来 ----------------- */
  const pol = playerProfile(state);

  /* --- 天空渐层（时代色） -------------------------------------------------*/
  const sky = g.createLinearGradient(0, 0, 0, H);
  sky.addColorStop(0, A.sky[0]); sky.addColorStop(1, A.sky[1]);
  g.fillStyle = sky; g.fillRect(0, 0, W, H);

  /* --- 远景剪影：每个时代有自己的天际线（《05》"UI 随时代演进"） --------*/
  drawSkyline(g, A, W, H, r, pol);

  /* --- 地形马赛克 ---------------------------------------------------------*/
  /* 海平面取 0.30：值噪声的实际分布偏中低，取 0.46 会让陆地只剩约 11%，
     地图几乎全是水，看着像一张空图。 */
  const seaLevel = 0.30;
  const cells = [];   // 供悬停命中检测
  /* 政体对整片陆域的染色。
     早前只染海岸线（edge > 0 的格子），但那种格子在整个地图里占比极小，
     两种政体画出来几乎一样——玩家看不出自己选了什么。
     教训：染色权重不能只看"设计意图"，必须实测最终像素。
     这条注释原先写"融合 0.30 就够"，是错的：紫混进偏暖的底色后，
     蓝分量根本压不过绿分量（实测紫向底砖 33 格，铁血红向 2715 格）。 */
  const polity = pol.border;
  const strong = pol.borderHard;               // 铁血 0.95 / 融合 0.15
  /* 政体色的"存在感"与锐利度是两件事，不能共用一个系数。
     早前 polityMix = 0.22 + strong*0.55，于是铁血 0.74（陆面通红）、
     融合 0.30（被底色吃掉，实测紫向底砖只有 33 格，玩家看不出政体变了）。
     现在：底砖染色对两种政体都保持可感知的强度，锐利度交给海岸线
     （drawCoast 用 hard 控线宽与虚实）。 */
  const polityMix = strong > 0.6 ? 0.62 : 0.42;
  for (let y = 0; y < rows; y++) {
    for (let x = 0; x < cols; x++) {
      const hgt = H0[y][x];
      const px = x * cw, py = y * ch;
      let col, kind;
      if (hgt < seaLevel) {
        /* 水/低地：深浅随深度 */
        const t = hgt / seaLevel;
        col = mix(A.ground[0], A.ground[1], t * 0.8);
        kind = 'water';
        /* 铁血政体的近岸水带一点红，但必须很轻。
           早前这里是 polityMix * 0.35，整片海都泛红——海陆对比被染色吃掉，
           地图又退回"一整片糊色"。《05》原文是"**边界**是锐利的红线"，
           红在边界上，不在水里。真正的红线交给 drawCoast 去画。 */
        if (pol.borderHard > 0.6) col = mix(col, polity, polityMix * 0.10);
      } else {
        const t = (hgt - seaLevel) / (1 - seaLevel);
        col = mix(A.land, t > 0.62 ? A.accent[1] : A.ground[2], t * 0.75);
        kind = 'land';
        /* 陆地染政体色：越靠近核心（高地）越浓，形成"中心—边缘"的统治感。
           系数不能再低了——低到 0.30 时融合政体的紫在底色里完全读不出来，
           玩家看不出自己选了什么。0.55 起跳，高地再加满。 */
        col = mix(col, polity, polityMix * (0.55 + t * 0.45));
      }
      /* 近岸水带一点政体色，但只给很窄一圈、很淡。
         早前这里是 `mix(col, polity, edge * pol.borderHard)`，权重满值 0.95，
         结果整条近岸水带被染成亮红（实测 #b13a2f），水陆对比被彻底吃掉——
         这正是「一整片糊色」的第二个来源。
         真正的红线由 drawCoast 描出来，那才是"边界"。
         这里只保留一点点渗色，让红色像墨一样洇进海里，而不是铺满整片。 */
      const edge = edgeFactor(H0, x, y, seaLevel);
      if (kind === 'water' && edge > 0) {
        col = mix(col, polity, edge * edge * 0.22 * pol.borderHard);
      }
      /* 随机色差：让每块砖不完全一样，这是马赛克质感的关键。
         幅度必须小（±4%）：调大会把政体染色和时代色一起吃掉，
         两种政体画出来又变成同一张图。 */
      col = shade(col, (r() - 0.5) * 0.08);
      cells.push({ x: px, y: py, w: cw, h: ch, col, kind, hgt, gx: x, gy: y });
    }
  }

  /* 逐块绘制。抖动用四边各自偏移，形成不规则手拼感 */
  for (const c of cells) {
    const j = A.jitter;
    /* 抖动偏移必须存回格子对象：海岸描边要画在和底砖完全相同的位置上，
       早前描边另算一遍随机偏移，结果线全都错开半格，像给地图描了个外框。 */
    c.ox = (r() - 0.5) * j; c.oy = (r() - 0.5) * j;
    g.fillStyle = c.col;
    g.fillRect(c.x + c.ox, c.y + c.oy, c.w + 0.6, c.h + 0.6);
  }

  /* --- 海岸线：一条看得见的线，而不是一片糊在一起的渐变 -------------------
   * 只靠底色差不够。低对比的时代（混凝土、硝烟、屏幕像素）里海陆仍然会糊成
   * 一整片——而"看得见海岸线"是《05》视觉节的第一条要求。
   * 所以给"临水的陆地格子"沿其水侧描一道亮边，线色随政体走：
   *   铁血 → 锐利红线（边界是切出来的）
   *   融合 → 柔紫渐变带（边界是化开的）
   * 两遍描边：先宽而淡当辉光，再细而亮当线，接近手绘的岸线。 */
  drawCoast(g, cells, H0, seaLevel, A, pol, cw, ch);

  /* --- 地貌符号：山、高地、树丛、河流 ------------------------------------*/
  drawRelief(g, A, cols, rows, cw, ch, H0, seaLevel, pol, r, cells);

  /* --- 地标：密度与种类由玩家历史决定（《07/05》"地标密度由标记驱动"）----*/
  const marks = drawLandmarks(g, A, cols, rows, cw, ch, H0, seaLevel, pol, r, state);

  /* --- 道路纹理：连出去的每一条都是你当年选的方向 -----------------------*/
  drawRoads(g, A, cols, rows, cw, ch, H0, seaLevel, pol, r);

  /* --- 雾/硝烟/煤烟：时代的呼吸 ----------------------------------------*/
  if (A.fog > 0) drawFog(g, A, W, H, r, pol);

  /* --- 未探索区域的迷雾（《07/05》"地图即史书"，看不清也是一种信息）------
     走过两轮弯路，这里记清楚：
     ① 早前是铺一整层均匀黑（alpha = (1-reveal)*0.72），观感是画框四周被压成
        一圈死黑，大陆像浮在黑纸上——那不是"未探索"，那是"坏了"。改径向渐变。
     ② 但只改形状还不够。实测发现岸线"明明画了却看不见"：
        对照实验（同种子同状态，reveal=1 与 reveal=0 各画一张）证明
        关雾时大陆外缘那一圈亮白线完整连贯，开雾时几乎被吞平。
        根因是雾的**外圈 alpha 到了 0.90**——而海岸线恰恰就画在大陆外缘，
        正好是雾最厚的地方。雾把"这里是海"这条信息整个抹掉了。
        迷雾的功能是"看不清远方"，不是"看不清自己脚下有海"。
     ③ 所以改成：外圈封顶 0.62（留得住岸线），并让中心更透（0.18），
        真正表达"视野在打开"的是时代推进（reveal 0.78 → 1.0），
        而不是让边缘糊成一团。 */
  const reveal = o.reveal == null ? 1 : o.reveal;
  if (reveal < 1) {
    const k = 1 - reveal;
    const mist = mix(A.sky[1], '#000000', 0.35);
    /* 必须把 hex 拆成 r,g,b 三个数再拼 rgba。
       早前写成 'rgba(' + mist.slice(1) + ',0.3)' —— slice(1) 去掉的是 '#'，
       拼出来是 "rgba(110c0a,0.3)"，浏览器直接抛 SyntaxError，
       整幅地图一笔都画不出来（黑屏）。 */
    const m = hex2rgb(mist);
    const rgba = a => 'rgba(' + m[0] + ',' + m[1] + ',' + m[2] + ',' + a.toFixed(3) + ')';
    const rg = g.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.18,
                                      W / 2, H / 2, Math.max(W, H) * 0.62);
    rg.addColorStop(0, rgba(k * 0.18));
    rg.addColorStop(0.55, rgba(k * 0.46));
    /* 外圈封顶 0.62：再厚就吃掉海岸线了（见上方②） */
    rg.addColorStop(1, rgba(Math.min(0.62, k * 0.80)));
    g.fillStyle = rg;
    g.fillRect(0, 0, W, H);
  }

  /* --- 挂回内部数据，供 hoverMark 命中检测与证言显示 --------------------*/
  cv._cells = cells; cv._cw = cw; cv._ch = ch; cv._cols = cols; cv._rows = rows;
  cv._marks = marks;
  cv._hovered = o.hover || null;
  cv._hoverMark = o.hover ? hoverMark(marks, o.hover.x, o.hover.y) : null;
}

/* 命中某个地标：按到中心的距离（地标比格子大，按格子查会不准） */
function hoverMark(marks, px, py) {
  if (!marks) return null;
  for (let i = marks.length - 1; i >= 0; i--) {
    const m = marks[i];
    const dx = px - m.cx, dy = py - m.cy;
    if (dx * dx + dy * dy <= m.r * m.r) return m;
  }
  return null;
}

/* 单元类型：0 深水 1 浅水 2 低地 3 高地 4 山 */
function unitOf(h, sea) {
  if (h < sea * 0.55) return 0;
  if (h < sea) return 1;
  if (h < sea + (1 - sea) * 0.35) return 2;
  if (h < sea + (1 - sea) * 0.7) return 3;
  return 4;
}
const edgeFactor = (Hm, x, y, sea) => {
  const h = Hm[y][x];
  let d = 9;
  for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
    const nx = x + dx, ny = y + dy;
    if (nx < 0 || ny < 0 || nx >= Hm[0].length || ny >= Hm.length) continue;
    if (Hm[ny][nx] < sea) d = Math.min(d, Math.abs(h - sea));
  }
  return Math.max(0, 1 - d * 1.6);
};

function hitCell(cells, px, py) {
  for (const c of cells) if (px >= c.x && px < c.x + c.w && py >= c.y && py < c.y + c.h) return c;
  return null;
}

/* ============================================================================
 * 海岸线描边
 * 把"临水的陆地格子"的水侧描出来。三遍：辉光 → 主线 → 浪花。
 * 视口外当作水，于是整幅图自带一圈岸线，等于免费的画框。
 * ==========================================================================*/
function drawCoast(g, cells, H0, sea, A, pol, cw, ch) {
  const cols = H0[0].length, rows = H0.length;
  const land = (x, y) => x >= 0 && y >= 0 && x < cols && y < rows && H0[y][x] >= sea;

  /* 收集所有"水侧边"线段：[x0,y0,x1,y1, 法线x, 法线y] */
  const segs = [];
  for (const c of cells) {
    if (c.kind !== 'land') continue;
    const x0 = c.x + (c.ox || 0), y0 = c.y + (c.oy || 0);
    const x1 = x0 + c.w + 0.6, y1 = y0 + c.h + 0.6;
    if (!land(c.gx, c.gy - 1)) segs.push([x0, y0, x1, y0, 0, -1]);
    if (!land(c.gx, c.gy + 1)) segs.push([x0, y1, x1, y1, 0, 1]);
    if (!land(c.gx - 1, c.gy)) segs.push([x0, y0, x0, y1, -1, 0]);
    if (!land(c.gx + 1, c.gy)) segs.push([x1, y0, x1, y1, 1, 0]);
  }
  if (!segs.length) return;

  /* 线色：立场明确时用政体色（铁血红线 / 融合紫带），
     中立时用该时代自己的岸色。
     早前一律用 pol.border，而中立政体的 border 是 '#c9b88f'（浅羊皮），
     混白后成 '#d9ceb1'，画在深色陆块上就是一圈刺眼白边——
     石刻这种本该粗朴的地图被描成了现代描边图。
     海岸线属于"地图的物理"，该跟时代；政体色属于"政治"，只管立场。 */
  const hard = pol.borderHard;
  const stance = hard > 0.5;                    // 有明确立场才让政体色接管
  const line = stance
    ? mix(pol.border, '#ffffff', 0.22)
    : mix(A.ground[0], '#ffffff', 0.52);        // 岸色：比水亮、比陆暗

  const stroke = (width, alpha, style, off) => {
    g.lineWidth = width; g.globalAlpha = alpha; g.strokeStyle = style;
    g.beginPath();
    for (const s of segs) {
      const dx = off ? s[4] * off[0] : 0, dy = off ? s[5] * off[1] : 0;
      g.moveTo(s[0] + dx, s[1] + dy);
      g.lineTo(s[2] + dx, s[3] + dy);
    }
    g.stroke();
  };

  g.lineCap = 'butt';
  /* hard 在这里才真正兑现"锐利 vs 渐变"：
     铁血（0.95）→ 主线细而实、几乎不透明，像用刀切出来的；
     融合（0.15）→ 主线宽而淡、叠三层，像洇开的一条色带。
     中立（无立场）时整体压暗一档：岸线只是"这里是海"的提示，
     不该是画面上最亮的东西。 */
  const k = stance ? 1 : 0.62;
  /* 1) 辉光：宽而淡，把线从底色里托起来。融合政体的辉光要更宽，那是"化开"。 */
  stroke(cw * (0.34 + (1 - hard) * 0.40), (0.18 + hard * 0.16) * k, line, null);
  /* 2) 主线：细而实，这才是"岸线"本身 */
  stroke(cw * (0.20 - hard * 0.07), (0.42 + hard * 0.46) * k, mix(line, '#ffffff', 0.10), null);
  /* 3) 浪花：往水里挪半格的一道浅色。
        混白系数必须压住：早前 0.7 混出 '#e1e0df'（近乎纯白），
        在深色陆块旁画一圈，是刺眼白边而不是浪花。0.34 刚好是"泛白的泡沫"。 */
  const foam = (1 - hard * 0.6) * k;
  if (foam > 0.2) {
    stroke(Math.max(1, cw * 0.12), 0.22 * foam,
      mix(line, '#ffffff', 0.34), [cw * 0.45, ch * 0.45]);
  }
  g.globalAlpha = 1;
}

/* ============================================================================
 * 玩家状态 → 政权画像：地图的一切变化都从这里来
 * ==========================================================================*/
function playerProfile(s) {
  if (!s) return { border: '#888', borderHard: 0.4, ruin: 0, density: 0.5, warm: 0.4, sea: '#2a4a5a', mark: '中立' };
  const a = s.attrs || {};
  const v = n => (a[n] == null ? 0 : a[n]);
  const fl = new Set(s.flags || []);
  /* 统一方式决定边界：红线的锐利 vs 色带的渐变（《05》视觉条原文） */
  const iron = v('contra') * 0.5 + v('warEthics') * -0.3 + (fl.has('排外传统') ? 30 : 0);
  const melt = v('poly') + v('postcol') * 0.6;
  let border = '#c9b88f', hard = 0.35;
  if (iron > 55)      { border = '#c0392b'; hard = 0.95; }   // 锐利红线
  else if (iron > 30) { border = '#c46a1a'; hard = 0.6; }
  else if (melt > 80) { border = '#7a5cff'; hard = 0.15; }  // 渐变色带
  else if (melt > 55) { border = '#4aa3c4'; hard = 0.25; }
  return {
    border, borderHard: hard,
    ruin: Math.max(0, (v('contra') - 55) / 45),
    density: Math.max(0.15, Math.min(1, 0.25 + v('poly') / 160 + v('trust') / 320)),
    warm: Math.max(0, Math.min(1, 0.35 + v('core') / 200)),
    /* 地标密度：科技越发达，人造物越多 */
    built: (s.tracks ? (s.tracks.sci || 10) / 100 : 0.2) * 0.7 + (fl.has('has_techcrisis') ? -0.3 : 0),
    mark: iron > 55 ? '铁血' : melt > 80 ? '融合' : '未定',
    /* 地标风格：铁血政体建军营与绞架，融合政体建市集与浴场。
       这是《07/05》层一"地标密度由历史标记驱动"的直接落地——
       玩家不读文字，看地标就知道自己变成了什么。 */
    flavor: iron > 55 ? 'iron' : melt > 80 ? 'melt' : 'mid',
  };
}

/* 每个时代有三种地标风格，按政体切换 */
const MARK_FLAVOR = {
  iron: {
    stone:   ['篝火','图腾柱','绞架','军帐'],
    clay:    ['王座柱','城墙','绞架','刑场'],
    iron:    ['城堡','铁砧','绞架','军营'],
    parchment:['竞技场','角斗场','绞架','柱廊'],
    ocean:   ['帆船','炮台','要塞','绞架'],
    industrial:['烟囱','铁轨','工厂','烟囱'],
    war:     ['废墟','战壕','断桥','探照灯'],
    concrete:['发射井','方楼','天线阵','铁幕'],
    fiber:   ['数据中心','基站','服务器','天线阵'],
    pixel:   ['轨道站','发射井','天线阵','基地站'],
  },
  melt: {
    stone:   ['篝火','帐篷','市集','图腾柱'],
    clay:    ['神庙','粮仓','市集','柱廊'],
    iron:    ['教堂','浴场','市集','钟楼'],
    parchment:['柱廊','浴场','竞技场','市集'],
    ocean:   ['帆船','港口','货舱','灯塔'],
    industrial:['厂房','水塔','铁轨','市集'],
    war:     ['废墟','战壕','市集','断桥'],
    concrete:['天线阵','基站','市集','方楼'],
    fiber:   ['数据中心','基站','光缆','服务器'],
    pixel:   ['轨道站','殖民地','跃迁门','基地站'],
  },
};
MARK_FLAVOR.mid = null;   // 中间态用时代默认（ERA_ART.marks）

/* --- 天际线剪影：每时代不同 ----------------------------------------------*/
function drawSkyline(g, A, W, H, r, pol) {
  const baseY = H * 0.30;
  g.fillStyle = shade(A.sky[1], -0.15);
  g.fillRect(0, baseY, W, H - baseY);
  const cols = A.style;
  let x = 0;
  while (x < W) {
    let w, hgt;
    switch (cols) {
      case 'stone':     w = 22 + r() * 40; hgt = 12 + r() * 34; break;
      case 'clay':      w = 30 + r() * 46; hgt = 22 + r() * 52; break;
      case 'iron':      w = 20 + r() * 30; hgt = 26 + r() * 58; break;
      case 'parchment': w = 18 + r() * 24; hgt = 30 + r() * 60; break;   // 柱廊
      case 'ocean':     w = 26 + r() * 50; hgt = 14 + r() * 30; break;
      case 'industrial':w = 16 + r() * 26; hgt = 34 + r() * 78; break;   // 烟囱最高
      case 'war':       w = 22 + r() * 34; hgt = 10 + r() * 40; break;   // 废墟矮且碎
      case 'concrete':  w = 24 + r() * 30; hgt = 28 + r() * 66; break;
      case 'fiber':     w = 14 + r() * 22; hgt = 30 + r() * 72; break;
      default:          w = 16 + r() * 20; hgt = 26 + r() * 64; break;   // pixel
    }
    g.fillStyle = shade(A.sky[1], -0.28 - r() * 0.12);
    g.fillRect(x, baseY - hgt, w, hgt);
    /* 工业时代加烟囱冒烟，战争时代加断口 */
    if (cols === 'industrial' && w < 24) {
      g.fillStyle = shade(A.sky[1], -0.34);
      g.fillRect(x + w * 0.4, baseY - hgt - 8, 5, 9);
    }
    if (cols === 'war' && hgt > 20) {
      g.fillStyle = shade(A.sky[1], -0.34);
      g.fillRect(x + w * 0.5, baseY - hgt, w * 0.3, 5);   // 缺口
    }
    x += w + 2 + r() * 6;
  }
}

/* --- 地标：马赛克拼出的小型建筑群 ---------------------------------------*/
function drawLandmarks(g, A, cols, rows, cw, ch, Hm, sea, pol, r, state) {
  const fl = new Set((state && state.flags) || []);
  /* 地标风格随政体切换：铁血建军营，融合建市集 */
  const F = MARK_FLAVOR[pol.flavor] && MARK_FLAVOR[pol.flavor][A.key];
  const types = F || A.marks;
  /* 数量 6 + density*16 → 4 + density*11。
     18 个 / 513 陆格 = 3.5%，数字上不算密，但视觉上"满地橙点"。
     原因不是数量而是视觉重量：地标是纯 accent 色（时代 1 是橙 #c2622a/#e0913c），
     在土黄陆面上对比强烈，每个都是一个小视觉钉子。
     地标承担的是"这里有人类活动"的背景信息，不该和海岸线抢注意力
     —— 海岸线才是"这里是什么"的主线。降到 4+11 让它退回配角。 */
  const target = Math.round(4 + pol.density * 11);
  const marks = [];        // 每次重绘必须重置，否则地标会跨时代累积
  let placed = 0, guard = 0;
  while (placed < target && guard++ < 400) {
    const gx = 2 + Math.floor(r() * (cols - 4));
    const gy = 2 + Math.floor(r() * (rows - 4));
    if (Hm[gy][gx] < sea + 0.04) continue;
    /* 避开已有地标 */
    let clash = false;
    for (const p of marks) {
      if (Math.abs(p.x - gx) < 2 && Math.abs(p.y - gy) < 2) { clash = true; break; }
    }
    if (clash) continue;
    const px = gx * cw, py = gy * ch;
    const kind = types[Math.floor(r() * types.length)];
    /* 已解锁的科技地标才可能出现（has_space → 星舰，has_ai → 数据中心…）*/
    if (kind === '星舰' && !fl.has('has_space')) continue;
    if (kind === '殖民地' && !fl.has('has_space')) continue;
    if (kind === '跃迁门' && !fl.has('has_space')) continue;
    if (kind === '数据中心' && !fl.has('has_techcrisis') && !(state && state.tracks && state.tracks.sci > 50)) continue;
    drawMark(g, A, px, py, cw, ch, kind, pol, r);
    marks.push({ x: gx, y: gy, kind, cx: px + cw / 2, cy: py + ch / 2, r: cw * 1.6 });
    placed++;
  }
  return marks;
}

function drawMark(g, A, x, y, cw, ch, kind, pol, r) {
  const u = Math.max(2, Math.round(cw / 3));   // 地标也用马赛克小块拼
  const bx = x + cw / 2, by = y + ch / 2;
  /* 地标是纯 accent 色的小方块，散在土黄陆地上时读起来像一把彩色纸屑——
     亮是对的（人造物该显眼），但没有"落地感"，所以不像建筑像贴纸。
     修法：先铺一层暗基座（阴影/地基），符号坐在上面。
     这一步不改变符号本身的语义，只给它一个"站在地上"的依据。 */
  const col = A.accent[Math.floor(r() * A.accent.length)];
  const dim = shade(col, -0.35);
  const put = (px, py, c) => { g.fillStyle = c; g.fillRect(px, py, u + 0.5, u + 0.5); };
  /* 暗基座：取陆相本色压暗，与地貌符号同一套派生逻辑。
     压暗量 -0.42 → -0.28：基座的作用是"接住符号"，不是"画一个黑框"。
     -0.42 在时代 1 出来是一圈明显更黑的方块，反而把橙点衬得更跳。 */
  const plinth = shade(mix(A.land, A.ground[0], 0.25), -0.28);
  const baseW = u * 2.6;
  put(bx - baseW / 2, by + u * 0.4, plinth);
  put(bx + baseW / 2 - u, by + u * 0.4, plinth);

  switch (kind) {
    case '篝火': case '断桥': case '铁轨': case '市集': case '光缆':
      put(bx - u, by, col); put(bx, by - u, col); put(bx + u, by, col); put(bx, by, col);
      if (kind === '市集') { put(bx, by + u, dim); put(bx - u, by + u, dim); }
      break;
    case '帐篷': case '厂房': case '货舱': case '军帐': case '工厂': case '钟楼':
      for (let i = -1; i <= 1; i++) put(bx + i * u, by - u * 0.5, i === 0 ? col : dim);
      put(bx, by + u * 0.5, col);
      if (kind === '钟楼') put(bx, by - u * 1.8, col);
      break;
    case '图腾柱': case '王座柱': case '水塔': case '探照灯': case '发射井': case '天线阵':
    case '军营': case '角斗场':
      put(bx, by - u, col); put(bx, by + u, dim); put(bx - u, by + u * 0.5, dim); put(bx + u, by + u * 0.5, dim);
      break;
    case '石碑': case '粮仓': case '要塞': case '铁幕':
      put(bx - u, by - u, col); put(bx, by - u, col); put(bx + u, by - u, col);
      put(bx, by, col); put(bx - u, by + u, dim); put(bx + u, by + u, dim);
      break;
    case '神庙': case '城堡': case '教堂': case '方楼': case '港口': case '灯塔':
    case '竞技场': case '浴场': case '数据中心': case '服务器': case '基地站': case '基站':
    case '轨道站': case '殖民地': case '跃迁门': case '刑场': case '柱廊':
      for (let i = -1; i <= 1; i++) for (let j = -1; j <= 0; j++)
        put(bx + i * u, by + j * u, (i + j) % 2 ? col : dim);
      break;
    case '帆船': case '星舰': case '炮台':
      put(bx, by, col); put(bx + u, by, col); put(bx - u, by + u, col);
      put(bx, by + u, dim); put(bx, by - u, shade(col, 0.3));
      break;
    case '铁砧': case '废墟': case '战壕': case '钢架': case '绞架':
      put(bx - u, by, dim); put(bx, by - u, col); put(bx, by, col); put(bx + u, by + u, dim);
      break;
    default:
      put(bx, by, col); put(bx - u, by, dim); put(bx + u, by, dim);
  }
  /* 战争/崩坏后的废墟：随机给地标加断口 */
  if (pol.ruin > 0.35 && r() < pol.ruin * 0.6) {
    g.clearRect(bx - u * 2, by - u, u, u);
  }
}

/* --- 道路：你走过的方向，会在地图上留下线（《07/05》层一"道路纹理"）-----*/
function drawRoads(g, A, cols, rows, cw, ch, Hm, sea, pol, r) {
  const n = Math.round(1 + pol.density * 4);
  g.lineWidth = Math.max(1, A.cell * 0.14);
  for (let i = 0; i < n; i++) {
    let gx = 2 + Math.floor(r() * (cols - 4));
    let gy = 2 + Math.floor(r() * (rows - 4));
    g.strokeStyle = shade(A.accent[1], -0.2);
    g.globalAlpha = 0.5;
    g.beginPath(); g.moveTo(gx * cw, gy * ch);
    for (let s = 0; s < 14; s++) {
      /* 优先往高地方向走，道路才有"通向核心"的意味 */
      let best = null, bestH = Hm[gy][gx];
      for (const [dx, dy] of [[1,0],[-1,0],[0,1],[0,-1],[1,1],[-1,-1],[1,-1],[-1,1]]) {
        const nx = gx + dx, ny = gy + dy;
        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
        if (Hm[ny][nx] > bestH) { bestH = Hm[ny][nx]; best = [nx, ny]; }
      }
      if (!best) break;
      gx = best[0]; gy = best[1];
      g.lineTo(gx * cw, gy * ch);
    }
    g.stroke(); g.globalAlpha = 1;
  }
}

/* ============================================================================
 * 地貌符号：让地图从"一整片色块"变成"一个世界"
 * 早前地图只有底色块 + 十几个地标点，看起来就是一张噪声图——
 * 地形本身是随机值噪声，没有 mountain / river / forest 这些可读的语义。
 * 现在按高度与坡度分层画符号，这也是传统马赛克像素地图的做法。
 * 全部用小方块/短线，保持马赛克语言，不引入圆弧等外来笔法。
 * ==========================================================================*/
function drawRelief(g, A, cols, rows, cw, ch, Hm, sea, pol, r, cells) {
  const s = A.cell;                       // 符号随时代变小：古图粗、现代图精细
  /* 符号色必须从"这一格实际画出来的颜色"派生，而不是从 A.land 固定值派生。
     早前 ink() 直接基于陆相本色，但陆相早就被政体色染过了（polityMix 0.42~0.62），
     于是铁血政体下符号画在偏红底上却用偏黄的墨、融合政体下反之，
     两种政体的地貌符号对比度完全不一样，其中一种必然糊掉。
     传入 cells 数组逐格派生：k 为正=提亮（凸起），负=压暗（凹陷）。 */
  const inkOf = (cell, k) => shade(cell ? cell.col : A.land, k);
  /* 提亮量必须随底色亮度自适应。
     羊皮纸时代陆相 L=200，shade(+0.32) 只能提到 218（+8.9%）——
     八年实测里最亮的陆面把受光面的提亮空间压没了，
     于是浅色地图上的山"只有暗面没有亮面"，读起来是一块脏斑。
     判据：受光面必须比本地陆面亮 ≥ 12%，达不到就自动加码。
     提亮是往白走，浅底已经接近白，所以只能"不够就多加"，
     上限 0.62 是为了不把山画成纯白块（那反而丢形状）。 */
  const baseL = L(A.land);
  const litK = (() => {
    for (const k of [0.32, 0.42, 0.52, 0.62]) {
      if (L(shade(A.land, k)) >= baseL * 1.12) return k;
    }
    return 0.62;
  })();
  /* 坡度：与四邻高差。陡坡画山、平缓画平原，靠这个把两种地表分开。 */
  const slopeAt = (x, y) => {
    let d = 0;
    for (const [dx, dy] of [[1,0],[-1,0],[0,1],[0,-1]]) {
      const nx = x + dx, ny = y + dy;
      if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
      d += Math.abs(Hm[ny][nx] - Hm[y][x]);
    }
    return d;
  };
  /* --- 关键：先分类，再按类别抽样，而不是逐格决定 --------------------------
   * 早前是"每个格子各自判断要不要画符号"，但地形是双线性插值出来的平滑曲面，
   * 高地必然连成一大片。于是相邻格子几乎同时判定为山，符号又都排在各自格子
   * 中心——结果就是一片整齐的竖条纹，像栅栏，像条形码，完全不像山。
   * 正确做法（也是传统像素地图的做法）：先给每格算一个"该处应该是什么"的类别，
   * 再在每一类里随机抽若干个点来画。这样山就散落在山地里，密度对但排列乱。 */
  const bucket = { peak: [], high: [], plain: [] };
  for (let y = 1; y < rows - 1; y++) {
    for (let x = 1; x < cols - 1; x++) {
      const h = Hm[y][x];
      if (h < sea) continue;
      const t = (h - sea) / (1 - sea);
      const sl = slopeAt(x, y);
      const k = (t > 0.70 || (t > 0.54 && sl > 0.05)) ? 'peak'
              : (t > 0.40 ? 'high' : 'plain');
      /* 带上格子对象，符号色才能从实际底色派生 */
      const cell = cells ? cells[y * cols + x] : null;
      bucket[k].push([x, y, t, cell]);
    }
  }
  const pick = (arr, n, minDist) => {
    /* 随机抽 n 项，且彼此间隔至少 minDist 格。
       只做"随机抽不重复"是不够的：山格在数组里是按扫描序排列的，
       抽到 [x=30,y=12] 时 [x=31,y=12] 大概率也在附近，纯随机照样会挑出
       紧挨着的两三个——屏幕上就是又一片竖条纹。必须显式排斥邻近样本。
       试够多次仍找不到位置就退出，让"稀疏区保持稀疏"。 */
    const a = arr.slice();
    const out = [];
    let guard = n * 24;
    while (out.length < n && a.length && guard-- > 0) {
      const idx = Math.floor(r() * a.length);
      const cand = a[idx];
      let tooClose = false;
      for (const [ox, oy] of out) {
        if (Math.abs(ox - cand[0]) < minDist && Math.abs(oy - cand[1]) < minDist) { tooClose = true; break; }
      }
      if (tooClose) continue;
      out.push(cand);
      a.splice(idx, 1);
    }
    return out;
  };
  /* 抽样密度按"这类地形的总面积"走，而不是固定个数——
     山地占三成时山就该密，山地只占一成时就不该满山都是山。
     minDist 随时代变小：古图块大，山也画得大而稀；现代图块小，可以密而小。
     基数必须用「该类地形的格数」，不是整幅地图的格数。
     早前用 area = cols*rows，于是格小的时代（cell 从 13 降到 5，
     同一张画布格数翻倍多）分到同样多的山，但每座山占的像素更少——
     羊皮纸时代实测：陆格 1217（十个时代最多），受光面亮像素只剩 3 个，
     山稀到几乎看不见。密度该跟"这类地形有多少"走，不该跟画布多大走。 */
  const far = Math.max(2, Math.round(s / 1.6));
  /* 系数 0.055 是从"像素够不够"倒推的，实测太保守：
     时代 1 的 peak 桶只有几十格，0.055 算下来是 2~3 座山，
     放大截图里整片平原上一座山都没有——不是稀疏，是消失。
     提到 0.16 并加绝对下限 6：稀疏时代至少有几座山可看，
     密集时代（时代 4~6 有 1200~1600 陆格）自然成脉。
     pick() 的 minDist 仍然排斥邻近样本，所以加密不会变回栅栏。 */
  const nPeak = c => Math.max(6, Math.round(c * 0.16));
  for (const [x, y, t, cell] of pick(bucket.peak, nPeak(bucket.peak.length), far)) {
    const px = x * cw + cw / 2, py = y * ch + ch / 2;
    /* 画法：三层阶梯山（暗体 + 背光面 + 受光面），最高处加雪顶。
       这一版改掉的是"读不出是山"的问题，不是"看不见"的问题——
       早前是两块等宽暗矩形加几根亮竖条，屏幕上出来是一根焦黑的 T 形，
       像烧焦的树枝。教训：形状要能自解释，"暗一档+ 亮一档"只是让它不消失，
       不等于让它可读。可读的形状必须有轮廓：逐层收窄 = 山。 */
    const w = cw * 1.05;                    // 比一格略宽，山才连得成脉
    const hgt = ch * (t > 0.85 ? 1.40 : 1.10);
    const base = py + hgt * 0.5;            // 山脚落在格子下半
    const layers = 3;
    for (let i = 0; i < layers; i++) {
      const f = i / layers;
      const lw = w * (1 - f * 0.72);        // 顶层只剩底层三成宽
      const ly = base - hgt * ((i + 1) / layers);
      const lh = hgt / layers + 1;
      /* 受光面的宽度有绝对下限 2px，不跟着层宽等比收窄。
         顶层 lw 只有底层的 28%，而时代 4~5 的格宽才 9~10px，
         于是顶层受光面算下来是 1.2px —— 亚像素，屏幕上等于没有，
         统计里也几乎不计（羊皮纸时代实测受光面亮像素只剩 3 个）。
         马赛克的最小可辨单元是 1px，但 1px 亮块在纹理里等于杂点，
         所以下限取 2px：顶层受光面不再缩到"看不见"，山才有立体感。 */
      const litW = Math.min(lw, Math.max(2, lw * 0.42));
      g.fillStyle = inkOf(cell, -0.22);     // 山体
      g.fillRect(px - lw / 2, ly, lw, lh);
      g.fillStyle = inkOf(cell, -0.40);     // 背光面：右半压得更暗
      g.fillRect(px + lw * 0.06, ly, lw * 0.44, lh);
      g.fillStyle = inkOf(cell, litK);       // 受光面：左偏上
      g.fillRect(px - lw / 2, ly, litW, lh);
    }
    /* 雪顶：只给最高的那批山。矮山戴雪顶会让整片山地看着像插了一排旗。 */
    if (t > 0.85) {
      g.fillStyle = inkOf(cell, Math.min(0.72, litK + 0.24));
      g.fillRect(px - w * 0.13, base - hgt, w * 0.26, hgt * 0.20);
    }
  }
  for (const [x, y, , cell] of pick(bucket.high, nPeak(bucket.high.length * 0.6), Math.max(2, Math.round(s / 2)))) {
    const px = x * cw + cw / 2, py = y * ch + ch / 2;
    /* 高地：斜排短线，等高线的感觉 */
    g.fillStyle = inkOf(cell, 0.26);
    for (let i = 0; i < 2; i++) {
      g.fillRect(px - s * 0.7 + i * s * 0.7, py - s * 0.3 + (i % 2) * s * 0.35,
        s * 0.62, Math.max(1, s * 0.16));
    }
  }
  for (const [x, y, , cell] of pick(bucket.plain, nPeak(bucket.plain.length * 0.75), Math.max(2, Math.round(s / 1.4)))) {
    const px = x * cw + cw / 2, py = y * ch + ch / 2;
    /* 平原上的树丛：三小点，疏密随机。
       同样用"暗底 + 亮顶"两笔：单笔纯色点在马赛克底上会消失。 */
    for (let i = 0; i < 3; i++) {
      const tx = px + (r() - 0.5) * s * 1.5, ty = py + (r() - 0.5) * s * 1.2;
      const d = Math.max(1, s * 0.28);
      g.fillStyle = inkOf(cell, -0.30);
      g.fillRect(tx, ty, d, d);
      g.fillStyle = inkOf(cell, 0.30);
      g.fillRect(tx, ty, d, Math.max(1, d * 0.4));
    }
  }

  /* 河流：从最高处沿最陡下降方向走出，陆相上一道略亮的线。
     河流让地图有"流向"，是"世界"与"噪声"最直接的区别之一。
     颜色取自该时代陆相的提亮版，而不是"水色混白"：
       早前用 mix(A.ground[0], 纯白, 0.34) —— 水色是冷调，混白后成 '#75706c'，
       画在暖褐的土黄陆块上被眼睛读成一条冷蓝色带，比海岸线还抢眼。
     河属于"陆面上的东西"，该跟着陆相走；海才是水色的。
     提亮系数 0.26 → 0.16：放大截图里 0.26 出来是一条近白的宽带，
     亮度盖过海岸线（"这里是海"）——河是次要信息，不该抢主线的位置。
     河的任务是"让地图有流向"，不是"让地图有一道亮痕"。 */
  const rivers = 2 + Math.round(pol.density * 2);
  g.strokeStyle = mix(A.land, '#ffffff', 0.16);
  g.lineWidth = Math.max(1, s * 0.28);
  g.lineCap = 'round';
  for (let i = 0; i < rivers; i++) {
    /* 起点必须是陆地高地，且离岸不太近，否则河只画两格就没了 */
    let gx = -1, gy = -1;
    for (let tries = 0; tries < 60; tries++) {
      const tx = 2 + Math.floor(r() * (cols - 4)), ty = 2 + Math.floor(r() * (rows - 4));
      if (Hm[ty][tx] > sea + (1 - sea) * 0.5) { gx = tx; gy = ty; break; }
    }
    if (gx < 0) continue;
    g.beginPath(); g.moveTo(gx * cw + cw / 2, gy * ch + ch / 2);
    for (let s2 = 0; s2 < 60; s2++) {
      let best = null, bestH = Hm[gy][gx];
      for (const [dx, dy] of [[1,0],[-1,0],[0,1],[0,-1],[1,1],[-1,-1],[1,-1],[-1,1]]) {
        const nx = gx + dx, ny = gy + dy;
        if (nx < 1 || ny < 1 || nx >= cols - 1 || ny >= rows - 1) continue;
        if (Hm[ny][nx] < bestH) { bestH = Hm[ny][nx]; best = [nx, ny]; }
      }
      if (!best) break;
      gx = best[0]; gy = best[1];
      /* 带点横向摆动，直线往山下冲看着像用尺子画的 */
      g.lineTo(gx * cw + cw / 2 + (r() - 0.5) * cw * 0.5,
               gy * ch + ch / 2 + (r() - 0.5) * ch * 0.5);
      if (Hm[gy][gx] < sea) break;           // 入海
    }
    g.stroke();
  }
  g.lineCap = 'butt';
}

/* --- 雾/硝烟/煤烟：时代不同的呼吸 ---------------------------------------*/
function drawFog(g, A, W, H, r, pol) {
  const bands = A.style === 'industrial' || A.style === 'war' ? 5 : 3;
  for (let i = 0; i < bands; i++) {
    const y = H * (0.55 + r() * 0.45);
    const h = H * (0.04 + r() * 0.10);
    g.fillStyle = A.style === 'industrial' ? 'rgba(60,55,50,0.16)'
              : A.style === 'war' ? 'rgba(70,60,55,0.15)'
              : 'rgba(200,200,210,0.07)';
    /* 用小方块堆出雾带，保持马赛克语言一致 */
    for (let px = 0; px < W; px += A.cell * 3) {
      const off = (r() - 0.5) * h;
      g.fillRect(px, y + off, A.cell * 3, h * 0.6);
    }
  }
}

/* ============================================================================
 * 马赛克场景插画（抉择剧场配图）
 * 与地图不同：这是"看"，不是"看全局"。
 * ==========================================================================*/
function drawScene(cv, eraIdx, state, opt) {
  const A = ERA_ART[eraIdx] || ERA_ART[0];
  const o = opt || {};
  const W = o.w || cv.clientWidth || 800, H = o.h || cv.clientHeight || 420;
  const dpr = o.dpr || Math.min(2, window.devicePixelRatio || 1);
  cv.width = W * dpr; cv.height = H * dpr;
  const g = cv.getContext('2d'); g.scale(dpr, dpr);
  const seed = hashStr('scene' + eraIdx + '|' + (o.seed || 0) + '|' + (state ? state.era : 1));
  const r = rng(seed);
  const pol = playerProfile(state);

  /* 天空 */
  const sky = g.createLinearGradient(0, 0, 0, H);
  sky.addColorStop(0, A.sky[0]); sky.addColorStop(0.62, A.sky[1]); sky.addColorStop(1, A.ground[0]);
  g.fillStyle = sky; g.fillRect(0, 0, W, H);

  /* 远景天际线 */
  drawSkyline(g, A, W, H * 0.8, r, pol);

  /* 中景地面：马赛克小块铺就，带起伏 */
  const c = A.cell * 1.5;
  const gy0 = H * 0.42;
  for (let y = gy0; y < H; y += c) {
    /* 越近块越大，制造景深——这本身就是马赛克画的透视办法 */
    const t = (y - gy0) / (H - gy0);
    const cs = c * (0.6 + t * 1.4);
    for (let x = 0; x < W; x += cs) {
      const n = r();
      const base = mix(A.ground[1], A.ground[2], n);
      g.fillStyle = shade(base, (r() - 0.5) * 0.12);
      g.fillRect(x + (r() - 0.5) * A.jitter, y + (r() - 0.5) * A.jitter, cs + 0.6, cs + 0.6);
    }
  }

  /* 主体：地标剪影，比地图更大更醒目 */
  const hero = A.marks[Math.floor(r() * A.marks.length)];
  const scale = Math.min(W, H) / 9;
  const col = A.accent[0], dim = shade(A.accent[0], -0.35);
  g.save();
  g.translate(W / 2, H * 0.62);
  for (let i = -3; i <= 3; i++)
    for (let j = -4; j <= 2; j++) {
      const on = Math.abs(i) + Math.abs(j) < 5;
      if (!on && r() < 0.55) continue;
      g.fillStyle = (i + j) % 2 ? col : dim;
      g.globalAlpha = j < -1 ? 0.55 : 1;
      g.fillRect(i * scale, j * scale, scale + 0.5, scale + 0.5);
    }
  g.restore(); g.globalAlpha = 1;

  /* 前景人物剪影：一个时代总得有人 */
  const px = W * (0.2 + r() * 0.6), py = H * 0.88;
  const ps = Math.min(W, H) / 14;
  g.fillStyle = 'rgba(10,8,6,0.82)';
  for (let i = -1; i <= 1; i++) for (let j = -3; j <= 0; j++)
    g.fillRect(px + i * ps + ps, py + j * ps, ps + 0.5, ps + 0.5);

  /* 时代雾气统一 */
  if (A.fog > 0) {
    g.fillStyle = 'rgba(0,0,0,' + (A.fog * 0.3) + ')';
    for (let y = H * 0.5; y < H; y += A.cell * 3)
      for (let x = 0; x < W; x += A.cell * 3)
        if (r() < 0.5) g.fillRect(x, y, A.cell * 3, A.cell * 3 * (0.4 + r()));
  }
  /* 四角压暗，把注意力收进画面中心 */
  const vg = g.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.28, W / 2, H / 2, Math.max(W, H) * 0.72);
  vg.addColorStop(0, 'rgba(0,0,0,0)'); vg.addColorStop(1, 'rgba(0,0,0,0.55)');
  g.fillStyle = vg; g.fillRect(0, 0, W, H);
}

/* --- 导出 -----------------------------------------------------------------*/
/* ============================================================================
 * 悬停证言（《07/05》层二：15 字内的人话，不是属性）
 * 原则：骑兵说"他父亲也是马背上的"，而不说"士气+12"。
 * 这些句子是这个世界的居民在说话，不是系统在播报。
 * ==========================================================================*/
const MARK_WORDS = {
  篝火:   ['围火的人比昨天少了一个', '有人开始数柴'],
  帐篷:   ['帐子外面还是孩子的声音', '新帐比旧帐小'],
  图腾柱: ['刻痕又深了一道', '没有人记得刻的是谁'],
  石碑:   ['字迹磨平了，但还在', '当年的名字比现在多'],
  神庙:   ['香火比人多', '祭司说这十年只多了一个神'],
  王座柱: ['新王换了柱子没换', '觐见要等三天'],
  城墙:   ['墙比城先修好', '修补处的颜色不一样'],
  粮仓:   ['开门的时候队伍排到河对岸', '今年是第三次称重'],
  城堡:   ['吊桥是放下来收的', '夜里能听见铁链响'],
  铁砧:   ['锤子声从早到晚没停过', '铁匠的儿子也是铁匠'],
  绞架:   ['那里站过三个人', '现在是鸟停的地方'],
  军营:   ['空的营帐比满的多', '有个兵一直没回来'],
  教堂:   ['钟比人响得早', '告解的队伍排到了街口'],
  竞技场: ['观众席比看台新', '上个月没有举行'],
  角斗场: ['门槛上的血洗不掉了', '名字刻在柱子背面'],
  柱廊:   ['柱子比人影多', '有人在柱子上刻了名字'],
  浴场:   ['水池换过三次水', '更衣的凳子不够坐'],
  帆船:   ['三年前出发没回来', '船帆上补了十七块布'],
  港口:   ['卸货的号子变了调', '有三条船一直空着'],
  灯塔:   ['灯还亮着，没人看', '守塔的老人换了三个'],
  货舱:   ['压舱石比货重', '货单比货多'],
  炮台:   ['炮口朝着自己的城', '再也没开过'],
  要塞:   ['驻军比居民多', '粮吃到第三年才断'],
  烟囱:   ['天亮前不能看这边', '烟囱比烟先被拆'],
  水塔:   ['水要排队领', '塔下有人住'],
  铁轨:   ['轨到头了，车还在开', '枕木是本地人铺的'],
  厂房:   ['窗户全被钉上了', '机器停在那年就没再转'],
  工厂:   ['烟囱的烟记号还在', '招工的牌子没摘'],
  废墟:   ['原来的门牌还能认出来', '现在住着三家'],
  战壕:   ['土里翻出过钢盔', '当年的战报在档案里'],
  断桥:   ['断口边长了一棵树', '桥那头没有路了'],
  探照灯: ['灯照向自己这边', '值夜的人换了班'],
  发射井: ['井盖是焊死的', '倒计时牌停在一个数上'],
  天线阵: ['一圈铁架对着天空', '雨天不能靠近'],
  方楼:   ['厚墙里住过人', '窗户只开了一条缝'],
  铁幕:   ['过不去的那道线', '线上的东西都算数'],
  市集:   ['三种语言在吆喝', '秤是官府的，布是民间的'],
  数据中心:['机器昼夜不停', '电是从很远的地方送来的'],
  服务器: ['机房里没有人', '风扇声比人声大'],
  基站:   ['信号在这里最好', '塔下有人住'],
  基站_alias:['塔下有人住'],
  光缆:   ['埋得比路还深', '断了要绕很远'],
  轨道站: ['进出都要等窗口', '窗外什么都看不见'],
  星舰:   ['燃料只够去一趟', '上一次回来少了三个人'],
  殖民地: ['户籍上还是原籍', '第二代已经不会说旧话'],
  跃迁门: ['通过的不是距离', '回来的不一定是原来那个'],
};
/* 取证言：同一种地标每次说法不同，玩家多悬停几次不会腻 */
function markWord(kind, seedN) {
  const arr = MARK_WORDS[kind];
  if (!arr || !arr.length) return kind;
  return arr[Math.abs(seedN) % arr.length];
}

if (typeof window !== 'undefined') {
  window.MOSAIC = { ERA_ART, drawMap, drawScene, playerProfile, rng, hashStr, shade, mix, terrain, markWord, MARK_WORDS, hoverMark };
}
if (typeof module !== 'undefined' && module.exports) {
  module.exports = { ERA_ART, drawMap, drawScene, playerProfile, rng, hashStr, shade, mix, terrain, markWord, MARK_WORDS, hoverMark };
}
