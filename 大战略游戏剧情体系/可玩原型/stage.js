/* ============================================================================
 * stage.js —— 对局视觉舞台层
 *
 * 存在的理由：纯文字原型"没什么意思"。玩家需要看见、点开、比较。
 * 本文件把 mosaic.js（画什么）接到 ui.js（怎么玩）中间，负责：
 *   1. 地图舞台：马赛克地图上屏、地图/插画切换、悬停证言
 *   2. 属性可视化：13 条属性雷达图 + 跨抉择变化曲线
 *   3. 科技树：6 轨 × 10 阶 = 60 个可点节点
 *   4. 档案室：标记日记（"我是谁"）
 *
 * 设计依据（全部来自项目文档，非凭空发挥）：
 *   《05_叙事基调建议》第四节  —— 地图即史书，边界随政体锐利/渐变；UI 随时代演进
 *   《07_扩展设计/05_叙事呈现与反馈系统》—— 三层文本呈现体系
 *       层一 世界本身在说话（地图，无文字）
 *       层二 微叙事界面（3 秒可读：悬停证言、新闻条、顾问便签）
 *       层三 抉择剧场（召见制，玩家主动进入）
 * ==========================================================================*/

const STAGE = (() => {
  /* --- 离屏缓存：地图很贵（一次 ~16000 个 fillRect），
         悬停时不能重画全图，否则鼠标一动就掉帧。
         策略：底图只在"状态变了"时重画一次进离屏 canvas，
               悬停只把离屏图 blit 上来 + 额外画高亮和证言。

         地图与插画必须用两块独立画布。早前共用一块，导致：
         画地图 → 切插画（覆写同一块）→ 切回地图，此时 cacheKey 未变，
         于是跳过重画并把插画当地图 blit 出来——画面整个错掉。 ---------*/
  const cacheCv = { map: null, scene: null };
  let cacheKey = '';

  function offscreen(slot, w, h) {
    if (!cacheCv[slot]) cacheCv[slot] = document.createElement('canvas');
    const c = cacheCv[slot];
    /* 离屏画布只用来当位图源，style 无意义。
       但仍要判空再写：测试替身与某些精简 DOM 环境没有 style，
       直接赋值会 TypeError 并连带整局渲染失败。 */
    if (c.style) { c.style.width = w + 'px'; c.style.height = h + 'px'; }
    return c;
  }

  /* --- 1. 地图舞台 --------------------------------------------------------*/

  /* 画底图到离屏。key 变了才重画。 */
  function paintMap(cv, eraIdx, state, reveal) {
    const w = cv.clientWidth || 900, h = cv.clientHeight || 460;
    const key = [eraIdx, w, h, Math.round(reveal * 20),
                 state && state.era, state && state.idx,
                 JSON.stringify((state && state.flags || []).length),
                 state && JSON.stringify(state.attrs)].join('|');
    const off = offscreen('map', w, h);
    if (key !== cacheKey) {
      MOSAIC.drawMap(off, eraIdx, state, { w, h, dpr: 1, reveal });
      cacheKey = key;
    }
    blit(cv, off, w, h);
    return off;
  }

  function blit(cv, off, w, h) {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    if (cv.width !== Math.round(w * dpr) || cv.height !== Math.round(h * dpr)) {
      cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr);
    }
    const g = cv.getContext('2d');
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.clearRect(0, 0, cv.width, cv.height);
    g.drawImage(off, 0, 0, w, h);
  }

  /* 悬停层：只画高亮圈与连线，不重画底图 */
  function paintHover(cv, mark) {
    const w = cv.clientWidth || 900, h = cv.clientHeight || 460;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const g = cv.getContext('2d');
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, w, h);
    if (!mark) return;
    g.save();
    g.strokeStyle = 'rgba(255,236,180,0.9)';
    g.lineWidth = 1.4;
    g.beginPath(); g.arc(mark.cx, mark.cy, mark.r, 0, Math.PI * 2); g.stroke();
    g.strokeStyle = 'rgba(255,236,180,0.35)';
    g.beginPath(); g.arc(mark.cx, mark.cy, mark.r + 5, 0, Math.PI * 2); g.stroke();
    /* 从地标拉一条引线到右上角，那里写证言 */
    g.beginPath();
    g.moveTo(mark.cx + mark.r * 0.7, mark.cy - mark.r * 0.7);
    g.lineTo(Math.min(w - 8, mark.cx + 46), 22);
    g.stroke();
    g.restore();
  }

  /* 已探索比例：随时代推进（《07/05》"看不清也是一种信息"）
     方向是"越推进看得越清"——大战略游戏的核心感受之一就是视野打开：
     从部落的一小块地盘，到全球，到星海。文档只说了"看不清也是一种信息"，
     没规定方向；反过来的"越到后期越被雾笼罩"是在惩罚玩家，不该选。
     但起点不能是 0.30：实测第一时代就有 70% 是雾，整幅地图灰蒙蒙一片，
     色调全被压平，那时玩家才刚做出第一个抉择。
     所以 0.78 起步，第十时代接近全清。 */
  const revealOf = s => {
    const t = Math.min(1, ((s.era - 1) * 3 + (s.idx || 0)) / 30);
    return Math.min(1, 0.78 + t * 0.21);
  };

  /* --- 2. 场景插画（抉择剧场配图）---------------------------------------*/
  /* 返回离屏画布：调用方（与测试）需要知道底图阶段的实际绘制量，
     可见画布上的调用数只有 blit 一次，量不出画面复杂度。 */
  function paintScene(cv, eraIdx, state, seed) {
    const w = cv.clientWidth || 520, h = cv.clientHeight || 240;
    const off = offscreen('scene', w + 7, h + 9);
    MOSAIC.drawScene(off, eraIdx, state, { w, h, dpr: 1, seed });
    blit(cv, off, w, h);
    return off;
  }

  /* --- 3. 属性雷达图 ------------------------------------------------------*/
  /* 13 条属性，0~100 归一。画成雷达是因为属性之间此消彼长，
     13 个数字排成条形图看不出"你选了军功还是选了民心"。 */
  function paintRadar(cv, state, keys) {
    const w = cv.clientWidth || 320, h = cv.clientHeight || 300;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    cv.width = w * dpr; cv.height = h * dpr;
    const g = cv.getContext('2d');
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, w, h);

    const list = (keys || Object.keys(ATTRS)).map(k => {
      const A = ATTRS[k], v = state.attrs[k] || 0;
      return { k, name: A.name, n: Math.max(0, Math.min(1, (v - A.min) / (A.max - A.min))) };
    });
    const N = list.length;
    if (!N) return;
    const cx = w / 2, cy = h / 2 + 4, R = Math.min(w, h) / 2 - 34;

    /* 底盘网格：五档（呼应《12》铁律 5——玩家只见五档） */
    g.strokeStyle = 'rgba(212,160,74,0.20)'; g.lineWidth = 1;
    for (let ring = 1; ring <= 5; ring++) {
      g.beginPath();
      for (let i = 0; i <= N; i++) {
        const a = (i % N) / N * Math.PI * 2 - Math.PI / 2, rr = R * ring / 5;
        const x = cx + Math.cos(a) * rr, y = cy + Math.sin(a) * rr;
        i ? g.lineTo(x, y) : g.moveTo(x, y);
      }
      g.stroke();
    }
    g.strokeStyle = 'rgba(212,160,74,0.14)';
    for (let i = 0; i < N; i++) {
      const a = i / N * Math.PI * 2 - Math.PI / 2;
      g.beginPath(); g.moveTo(cx, cy);
      g.lineTo(cx + Math.cos(a) * R, cy + Math.sin(a) * R); g.stroke();
    }

    /* 数据面：政体画像决定填色，与地图同一套语言 */
    const pol = MOSAIC.playerProfile(state);
    const grd = g.createRadialGradient(cx, cy, 0, cx, cy, R);
    grd.addColorStop(0, hexA(pol.border, 0.55));
    grd.addColorStop(1, hexA(pol.border, 0.14));
    g.fillStyle = grd;
    g.beginPath();
    list.forEach((it, i) => {
      const a = i / N * Math.PI * 2 - Math.PI / 2, rr = Math.max(2, R * it.n);
      const x = cx + Math.cos(a) * rr, y = cy + Math.sin(a) * rr;
      i ? g.lineTo(x, y) : g.moveTo(x, y);
    });
    g.closePath(); g.fill();
    g.strokeStyle = pol.border; g.lineWidth = 1.6; g.stroke();

    /* 顶点 + 名字 */
    g.font = '10px "Songti SC",serif';
    g.textAlign = 'center'; g.textBaseline = 'middle';
    list.forEach((it, i) => {
      const a = i / N * Math.PI * 2 - Math.PI / 2;
      const rr = Math.max(2, R * it.n);
      const px = cx + Math.cos(a) * rr, py = cy + Math.sin(a) * rr;
      g.fillStyle = '#f0e6d4';
      g.fillRect(px - 1.5, py - 1.5, 3, 3);
      const lx = cx + Math.cos(a) * (R + 16), ly = cy + Math.sin(a) * (R + 16);
      g.fillStyle = it.n > 0.62 ? '#e8c88a' : it.n < 0.3 ? '#b3705c' : '#9b9080';
      g.fillText(it.name, lx, ly);
    });
  }

  /* --- 4. 变化曲线（跨抉择）----------------------------------------------*/
  /* 玩家要的"我现在是什么状况"不只是当前值，还有走势。
   * 没有历史快照就退化为"当前值横线"，不空图。 */
  function paintTrend(cv, state, keys) {
    const w = cv.clientWidth || 320, h = cv.clientHeight || 200;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    cv.width = w * dpr; cv.height = h * dpr;
    const g = cv.getContext('2d');
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, w, h);

    const list = (keys || Object.keys(ATTRS)).map(k => {
      const A = ATTRS[k];
      return { k, name: A.name, min: A.min, max: A.max,
               v: state.attrs[k] || 0, hist: (state.hist || []).map(s => s[k] == null ? null : s[k]) };
    });
    const padL = 6, padR = 6, padT = 16, padB = 26;
    const W = w - padL - padR, H = h - padT - padB;
    const steps = Math.max(1, (state.hist || []).length);

    /* 网格 */
    g.strokeStyle = 'rgba(212,160,74,0.13)'; g.lineWidth = 1;
    for (let i = 0; i <= 4; i++) {
      const y = padT + H * i / 4;
      g.beginPath(); g.moveTo(padL, y); g.lineTo(padL + W, y); g.stroke();
    }
    g.font = '9px "Songti SC",serif'; g.textBaseline = 'middle';
    g.fillStyle = '#6d6355'; g.textAlign = 'left';
    g.fillText('强', padL, padT - 8);
    g.fillText('弱', padL, padT + H + 8);

    const colors = ['#d4a04a', '#6a9ab8', '#7fb069', '#c9705a', '#a98ac0', '#c9b88f'];
    list.forEach((it, i) => {
      const col = colors[i % colors.length];
      const yOf = v => padT + H - ((v - it.min) / (it.max - it.min)) * H;
      g.strokeStyle = col; g.lineWidth = 1.5;
      g.beginPath();
      const pts = it.hist.length ? it.hist : [it.v];
      pts.forEach((v, j) => {
        const x = padL + (steps === 1 ? W : W * j / (steps - 1));
        j ? g.lineTo(x, yOf(v)) : g.moveTo(x, yOf(v));
      });
      /* 折线末端连到当前值，历史里没有的补一段虚线 */
      if (pts.length && pts[pts.length - 1] !== it.v) {
        const x0 = padL + (steps === 1 ? W : W), x1 = padL + W + 2;
        g.setLineDash([2, 3]);
        g.lineTo(Math.max(x0, x1), yOf(it.v));
        g.setLineDash([]);
      }
      g.stroke();
      g.fillStyle = col;
      g.fillRect(padL + W, yOf(it.v) - 1.5, 4, 3);
    });

    /* 图例（名字 + 当前值 + 走向箭头） */
    g.font = '9px "Songti SC",serif'; g.textAlign = 'left';
    let lx = padL, ly = h - 8;
    list.slice(0, 6).forEach((it, i) => {
      const col = colors[i % colors.length];
      const first = it.hist.length ? it.hist[0] : it.v;
      const dv = it.v - first;
      const arrow = dv > 1 ? '▲' : dv < -1 ? '▼' : '—';
      const label = it.name + ' ' + it.v + arrow;
      g.fillStyle = col;
      g.fillText(label, lx, ly);
      lx += g.measureText(label).width + 10;
    });
  }

  /* --- 5. 科技树：6 轨 × 10 阶 = 60 节点 ---------------------------------*/
  /* 名字按"该轨的性质 + 时代推进"排布，每阶对应一个时代。
     这是实现层的合理推演（文档只给了 6 轨 10 门），故在 README 标注为
     "由轨与时代生成的展示用节点"，不是《03》《04》原文条目。 */
  const TREE = {
    mil:  ['石斧与盾', '青铜长矛', '铁甲骑兵', '弩与围城', '火枪与棱堡', '火炮与线列', '装甲与铁甲舰', '空军与雷达', '导弹与核武', '轨道打击'],
    cult: ['口传与篝火', '泥板与碑铭', '圣书与抄本', '印刷与纸', '报刊与连载', '广播与电影', '电视与录像', '数字与网络', '虚拟与沉浸', '集体记忆工程'],
    inst: ['部落议事', '城邦议会', '官僚与法典', '贵族与等级', '君主与常备军', '共和与宪政', '代议与普选', '国际组织', '全球治理架构', '后国家共治'],
    sci:  ['结绳计数', '历法与几何', '公理与演绎', '实验与实证', '统计与地质', '相对论与量子', '基因与信息论', '复杂系统与网络', '人工意识', '统一场与意识科学'],
    econ: ['采集与狩猎', '农牧与剩余', '冶铁与度量', '货币与市场', '复式记账', '蒸汽与工厂', '铁路与电气', '石油与流水线', '全球供应链', '自动化与后稀缺'],
    edu:  ['口耳相传', '私塾与师徒', '官学与科举', '普及识字', '公共义务教育', '大众出版', '高等教育普及', '终身教育网络', '在线学习', '人人可及的通识'],
  };
  const TECH_TREE = (() => {
    const out = [];
    for (const tk in TREE) for (let i = 0; i < 10; i++) out.push({ tk, step: i, name: TREE[tk][i] });
    return out;
  })();

  /* 某节点是否已解锁：轨有效值跨过该阶门槛（第 N 阶需 ≥ N×9） */
  function techUnlocked(state, node) {
    return trackEff(state, node.tk) >= (node.step + 1) * 9;
  }

  /* --- 6. 档案室：标记日记（"我是谁"）----------------------------------*/
  /*《07/05》："系统根据玩家全部标记，用 3 句话生成当下的文明自我描述。" */
  function selfPortrait(state) {
    const a = state.attrs, f = x => state.flags.includes(x);
    const pol = MOSAIC.playerProfile(state);
    const L = [];
    L.push(pol.mark === '铁血'
      ? `我们把边界画得很清楚。清楚到没有人需要解释为什么那里不能过。`
      : pol.mark === '融合'
      ? `我们的边界在慢慢变淡。有人说不清自己从哪来，但这不再是罪名。`
      : `我们还在决定自己是什么。地图上的颜色每年都在改。`);
    L.push(f('核废土') ? `我们曾经让一座城的灯灭了。现在那片地上什么也不长。`
      : f('AI共生') ? `我们把一部分自己交给了算法。它比我们更了解我们的偏好。`
      : f('公共审议传统') ? `凡是要紧的事，都得坐下来吵一架才能定。`
      : f('铁屋') ? `我们很安全。安全的意思是，没有人问为什么。`
      : `我们还在为一些说不清对错的事情争论不休。`);
    const t = Math.round(a.trust);
    L.push(t >= 62 ? `街上有人朝我们扔东西，但多半是冲别人。`
      : t >= 42 ? `抱怨是有的，但没人真的不信。`
      : `没人说什么。但每个人都知道该自己解决。`);
    return L;
  }

  /* --- 7. 新闻条（层二：低语层事件）-------------------------------------*/
  /* 全部是被动阅读的一行字，不打断操作。 */
  function news(state) {
    const a = state.attrs, f = x => state.flags.includes(x);
    const out = [];
    const env = derived(state, 'environment');
    const nat = derived(state, 'nationalism');
    const dis = derived(state, 'dissonance');
    if (env < 35) out.push(['坏', `气候记事：今年的雨季短了${Math.max(1, Math.round((45 - env) / 3))}天`]);
    if (nat > 65) out.push(['坏', `边境线上又多了两个哨所`]);
    if (dis >= 6) out.push(['坏', `有${dis}件事大家各有各的说法`]);
    if (a.trust < 38) out.push(['坏', `粮价的告示换了三版，最后一版没人看`]);
    if (f('核废土')) out.push(['坏', `东边那片地已经十年没长出东西`]);
    if (f('核克制者') && !f('核使用者')) out.push(['好', `核武库仍封着，钥匙在两把锁里`]);
    if (f('开放网络') || trackEff(state, 'cult') > 55) out.push(['好', `远处的消息比邻居来得还快`]);
    if (a.trust > 66) out.push(['好', `有老兵把勋章挂出来了，没人笑`]);
    if (a.poly > 68) out.push(['好', `三个族群开始共用一个市场`]);
    if (trackEff(state, 'sci') > 62) out.push(['好', `医院把那个病治好了`]);
    if (state.flags.includes('文明记忆')) out.push(['好', `档案馆开门了，门口排着队`]);
    if (!out.length) out.push(['平', `今天没有值得记的事。这在历史上很少见。`]);
    return out;
  }

  /* --- 小工具 ------------------------------------------------------------*/
  function hexA(hex, a) {
    if (!hex || hex[0] !== '#') return `rgba(212,160,74,${a})`;
    const r = parseInt(hex.slice(1, 3), 16), g = parseInt(hex.slice(3, 5), 16), b = parseInt(hex.slice(5, 7), 16);
    return `rgba(${r},${g},${b},${a})`;
  }

  /* 悬停证言：同一种地标每次说法不同 */
  function wordFor(mark) {
    if (!mark) return null;
    return { kind: mark.kind, text: MOSAIC.markWord(mark.kind, Math.round(mark.cx + mark.cy)) };
  }

  return { paintMap, paintHover, paintScene, paintRadar, paintTrend,
           revealOf, techUnlocked, TECH_TREE, selfPortrait, news, wordFor, blit, hexA, cacheKey: () => cacheKey };
})();

if (typeof window !== 'undefined') window.STAGE = STAGE;
if (typeof module !== 'undefined' && module.exports) module.exports = STAGE;
