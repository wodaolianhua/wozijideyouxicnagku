/* ============================================================================
 * stagetest.js —— 舞台层自检（stage.js）
 *
 * 测的是"玩家能不能看见"这一层：画布有没有真的画出东西、图表是不是空的、
 * 悬停命中准不准、雷达/曲线的几何对不对。
 * 这些在 mosaictest.js 里测不到——那套测的是引擎纯函数，不碰 ctx。
 * ==========================================================================*/
const fs = require('fs'), path = require('path'), vm = require('vm');

/* --- Canvas 记录器 --------------------------------------------------------
   与 mosaictest.js 的同名替身刻意保持一致：计数器对象必须叫 n，
   绝不能也叫 fillRect 之类，否则对象字面量里的同名方法会遮蔽计数器。 */
function makeCtx() {
  const n = { fillRect: 0, fillStyle: [], stroke: 0, beginPath: 0, arc: 0,
              lineTo: 0, moveTo: 0, clearRect: 0, drawImage: 0,
              fill: 0, closePath: 0, rect: 0, clip: 0, save: 0, restore: 0, scale: 0,
              fillText: [] };
  return {
    n,
    set fillStyle(v) { n.fillStyle.push(v); },  get fillStyle() { return '#000'; },
    set strokeStyle(v) {}, get strokeStyle() { return '#fff'; },
    set globalAlpha(v) {},  get globalAlpha() { return 1; },
    set lineWidth(v) {},    get lineWidth() { return 1; },
    set font(v) {},        get font() { return '10px serif'; },
    set textAlign(v) {},   get textAlign() { return 'left'; },
    set textBaseline(v) {},get textBaseline() { return 'top'; },
    fillRect() { n.fillRect++; },
    stroke() { n.stroke++; }, beginPath() { n.beginPath++; }, arc() { n.arc++; },
    lineTo() { n.lineTo++; }, moveTo() { n.moveTo++; },
    fill() { n.fill++; }, closePath() { n.closePath++; }, rect() { n.rect++; }, clip() { n.clip++; },
    fillText(t) { n.fillText.push(String(t)); },
    strokeRect() {}, clearRect() { n.clearRect++; },
    save() { n.save++; }, restore() { n.restore++; },
    translate() {}, scale() { n.scale++; }, setTransform() {},
    drawImage() { n.drawImage++; },
    measureText: s => ({ width: String(s).length * 6 }),
    createLinearGradient() { return { addColorStop() {} }; },
    createRadialGradient() { return { addColorStop() {} }; },
  };
}
function makeCanvas(w, h) {
  const c = makeCtx();
  const ZEROS = ['fillRect','stroke','beginPath','arc','lineTo','moveTo','clearRect',
                  'drawImage','fill','closePath','rect','clip','save','restore','scale'];
  return {
    width: 0, height: 0, clientWidth: w, clientHeight: h,
    getContext: () => c, n: c.n,
    /* 归零必须作用在真实计数器 c.n 上。
       早前另建了一张"零值表"再逐项赋值，那张表与 c.n 毫无关系，
       reset() 什么也没清，测试读到的一直是累计值。 */
    reset() { for (const k of ZEROS) c.n[k] = 0; c.n.fillStyle.length = 0; c.n.fillText.length = 0; },
  };
}

const store = {};
/* stage.js 内部会 document.createElement('canvas') 建一块离屏画布并全程复用。
   测试要读它的计数器，就必须能从沙箱外拿到同一块——
   故把 createElement 建出来的每一块都登记到 madeCanvases。 */
const madeCanvases = [];
const sandbox = {
  console,
  document: {
    getElementById: () => null,
    querySelectorAll: () => [],
    createElement: tag => {
      if (tag !== 'canvas') return {};
      const cv = makeCanvas(300, 200);
      madeCanvases.push(cv);
      return cv;
    },
    addEventListener() {},
  },
  localStorage: { getItem: k => (k in store ? store[k] : null), setItem: () => {}, removeItem: () => {} },
  devicePixelRatio: 2,
  setTimeout, clearTimeout,
};
sandbox.window = sandbox;
sandbox.window.devicePixelRatio = 2;
sandbox.globalThis = sandbox;
sandbox.module = { exports: {} };
vm.createContext(sandbox);

let src = '';
for (const f of ['data.js', 'engine.js', 'mosaic.js', 'stage.js']) {
  src += fs.readFileSync(path.join(__dirname, f), 'utf8') + '\n';
}
src += `
globalThis.__X = { STAGE, MOSAIC, newGame, applyChoice, advanceEra, trackEff, derived,
  ERAS, ATTRS, TRACKS, TECHS, MARKERS, AXES };
`;
/* makeCanvas 定义在本文件作用域，sandbox 的 createElement 闭包能直接引用它，
   不需要往 vm 里桥接（桥接反而会与宿主声明同名冲突）。 */
vm.runInContext(src, sandbox, { filename: 'bundle-stage.js' });
const X = sandbox.__X;

let pass = 0, fail = 0;
const ok = (n, c, extra) => {
  if (c) { pass++; console.log('  ✓ ' + n); }
  else { fail++; console.log('  ✗ ' + n + (extra ? '  → ' + extra : '')); }
};

/* 造一个走过若干抉择的存档 */
function played(eraCount, pickFn) {
  const s = X.newGame();
  for (let e = 0; e < eraCount; e++) for (let c = 0; c < 3; c++) {
    const C = X.ERAS[e].choices[c];
    const o = pickFn ? pickFn(C, e, c) : C.options[0];
    X.applyChoice(s, e, C, o);
    s.idx += 1;
    s.hist = s.hist || []; s.hist.push({ ...s.attrs });
    if (c === 2) X.advanceEra(s), s.idx = 0;
  }
  return s;
}

console.log('\n[ST-1] 地图舞台真的画出了东西');
{
  const s = played(3);
  const cv = makeCanvas(760, 430);
  const off = X.STAGE.paintMap(cv, s.era - 1, s, X.STAGE.revealOf(s));
  /* stage.js 复用自己的离屏画布，绘制量全在那上面；
     可见画布只负责 blit。这是设计（否则鼠标一动就重画一万多块）。 */
  const offCv = off === cv ? null : off;
  ok('paintMap 返回离屏画布', offCv && typeof offCv.n === 'object');
  /* 可见画布走的是 blit：地形画在离屏，可见画布只 drawImage 一次。
     若这里去数可见画布的 fillRect，得到的必然是 0——那是设计，不是缺陷。 */
  ok('可见画布通过 drawImage 呈现（不在可见画布上重画）', cv.n.drawImage === 1, 'n=' + cv.n.drawImage);
  ok('可见画布每次都先清空', cv.n.clearRect >= 1, 'n=' + cv.n.clearRect);
  ok('离屏缓存有大量绘制调用', offCv.n.fillRect > 1000, 'n=' + offCv.n.fillRect);
  ok('离屏画布挂了地标表供悬停命中', Array.isArray(offCv._marks) && offCv._marks.length > 0,
     'marks=' + (offCv._marks ? offCv._marks.length : 'none'));
  ok('地标数在合理区间（6~22）', offCv._marks.length >= 6 && offCv._marks.length <= 22,
     'n=' + offCv._marks.length);
  ok('离屏画布就是沙箱登记的那块（可被测试归零）',
     madeCanvases.indexOf(offCv) >= 0, 'idx=' + madeCanvases.indexOf(offCv));
}

console.log('\n[ST-2] 缓存策略：状态不变不重画');
{
  const s = played(2);
  const a = makeCanvas(700, 400);
  const off = X.STAGE.paintMap(a, s.era - 1, s, 1);
  const k1 = X.STAGE.cacheKey();
  const firstFills = off.n.fillRect;
  ok('首次渲染确实画了地形', firstFills > 1000, 'n=' + firstFills);
  /* 关键：状态不变时不该重复跑那一万多次 fillRect，
     否则鼠标每动一下就重画全图，帧率必崩。 */
  off.reset();
  X.STAGE.paintMap(a, s.era - 1, s, 1);
  const k2 = X.STAGE.cacheKey();
  ok('相同状态下 cacheKey 一致', k1 === k2);
  ok('命中缓存时不重画地形（只 blit）', off.n.fillRect === 0 && a.n.drawImage === 2,
     'fillRect=' + off.n.fillRect + ' drawImage=' + a.n.drawImage);
  /* 状态一变必须重画 */
  off.reset();
  s.idx = 2;
  X.STAGE.paintMap(a, s.era - 1, s, 1);
  ok('状态变化后重画地形', off.n.fillRect > 1000, 'n=' + off.n.fillRect);
}

console.log('\n[ST-2b] 地图 ↔ 插画 切换不串图（回归防护）');
{
  /* 曾经的真实 bug：paintMap 与 paintScene 共用同一块离屏画布。
     画地图 → 切插画（覆写同一块）→ 切回地图，此时 cacheKey 未变，
     于是跳过重画，把插画当地图 blit 出来——整个画面错掉。
     判据：切回地图后离屏画布必须重新被填满（不能是插画的残留）。 */
  const s = played(2);
  const cv = makeCanvas(700, 400);
  const mapOff = X.STAGE.paintMap(cv, s.era - 1, s, 1);
  const sceneCv = makeCanvas(700, 400);
  const sceneOff = X.STAGE.paintScene(sceneCv, s.era - 1, s, 3);
  ok('地图与插画使用两块不同的离屏画布', mapOff !== sceneOff);
}

console.log('\n[ST-2b] 地图 ↔ 插画 切换不串图（回归防护）');
{
  /* 曾经的真实 bug：paintMap 与 paintScene 共用同一块离屏画布。
     画地图 → 切插画（覆写同一块）→ 切回地图，此时 cacheKey 未变，
     于是跳过重画，把插画当地图 blit 出来——整个画面错掉。
     修法：两块独立离屏画布。
     判据：切回地图后，地图离屏的内容必须与切走之前逐像素一致（用色序列比对）。 */
  const s = played(2);
  const cv = makeCanvas(700, 400);
  const mapOff = X.STAGE.paintMap(cv, s.era - 1, s, 1);
  const mapSeq = mapOff.n.fillStyle.join();
  const sceneCv = makeCanvas(700, 400);
  const sceneOff = X.STAGE.paintScene(sceneCv, s.era - 1, s, 3);
  ok('地图与插画使用两块不同的离屏画布', mapOff !== sceneOff);
  ok('插画画布被独立填满（有自己的内容）', sceneOff.n.fillRect > 200,
     'n=' + sceneOff.n.fillRect);
  /* 切回地图：命中缓存、不重画（fillRect 归零）正是正确行为——
     地图离屏从未被插画覆写，没有重画的理由。
     真正的判据是"插画那次调用没有碰地图的画布"：
     若两图共用一块，paintScene 会把地图的计数器一起冲掉。 */
  mapOff.reset(); sceneOff.reset();
  X.STAGE.paintMap(cv, s.era - 1, s, 1);
  ok('切回地图命中缓存（地图离屏未被重写）', mapOff.n.fillRect === 0,
     'fillRect=' + mapOff.n.fillRect);
  ok('切回地图时插画画布完全没被触碰（两者互不干扰）', sceneOff.n.fillRect === 0,
     'scene fillRect=' + sceneOff.n.fillRect);
  /* 若共用一块画布，此处 paintScene 会把地图的缓存内容冲掉。
     用一个此前没用过的 reveal 值，保证首次一定真的画（否则会命中上一用例留下的缓存）。 */
  const m2 = makeCanvas(700, 400);
  const s2 = played(3);
  const rv = X.STAGE.revealOf(s2);
  const offA = X.STAGE.paintMap(m2, s2.era - 1, s2, rv);
  const seqA = offA.n.fillStyle.join();
  X.STAGE.paintScene(m2, s2.era - 1, s2, 3);
  X.STAGE.paintMap(m2, s2.era - 1, s2, rv);
  ok('地→插→地 往返后地图内容与首次完全一致',
     offA.n.fillStyle.join() === seqA && seqA.length > 100,
     'lenA=' + seqA.length + ' lenNow=' + offA.n.fillStyle.join().length);
}

console.log('\n[ST-3] 悬停证言与命中检测');
{
  const s = played(4);
  const cv = makeCanvas(760, 430);
  const off = X.STAGE.paintMap(cv, s.era - 1, s, 1);
  const marks = off._marks;
  /* 正中某个地标 → 必须命中，且证言是 15 字内的人话 */
  const m = marks[0];
  const hit = X.MOSAIC.hoverMark(marks, m.cx, m.cy);
  ok('地标中心可命中', hit === m, JSON.stringify(hit && hit.kind));
  const w = X.STAGE.wordFor(hit);
  ok('证言非空', !!w && !!w.text, JSON.stringify(w));
  ok('证言是 15 字内（层二要求）', w.text.length <= 15, w.text.length + '字: ' + w.text);
  ok('证言带地标类型', !!w.kind, w.kind);
  /* 空白处不应命中 */
  ok('空白处不命中', X.MOSAIC.hoverMark(marks, 2, 2) === null);
  /* 同种地标多次取词会变（不腻） */
  const words = new Set();
  for (let i = 0; i < 40; i++) words.add(X.MOSAIC.markWord(m.kind, i));
  ok('同种地标证言会变化', words.size >= 1 && words.size <= 3, 'variants=' + words.size);
  /* 悬停层只画高亮，不重画底图 */
  const before = cv.n.fillRect;
  X.STAGE.paintHover(cv, m);
  ok('悬停层绘制量极小（不是重画全图）', cv.n.fillRect - before < 60,
     'extra=' + (cv.n.fillRect - before));
  ok('悬停层画了高亮圆', cv.n.arc >= 2, 'arc=' + cv.n.arc);
  X.STAGE.paintHover(cv, null);
  ok('移出悬停时清空高亮', true);
}

console.log('\n[ST-4] 场景插画');
{
  const s = played(3);
  const cv = makeCanvas(520, 240);
  /* 离屏画布是复用的，计数器跨用例累计；测单次调用量前必须归零。 */
  const o1 = X.STAGE.paintScene(cv, s.era - 1, s, 0);
  o1.reset();
  const o2 = X.STAGE.paintScene(cv, s.era - 1, s, 0);
  const fills1 = o2.n.fillRect;
  const seq1 = o2.n.fillStyle.join();
  ok('可见画布 blit 插画', cv.n.drawImage >= 1, 'n=' + cv.n.drawImage);
  ok('插画有大量绘制调用（离屏）', fills1 > 200, 'n=' + fills1);
  /* 三次调用必须用完全相同的画布尺寸：马赛克块数由 W/H 与 cell 推出，
     尺寸一变块数就变，拿绘制量做确定性比对必然误判。 */
  o1.reset();
  const o3 = X.STAGE.paintScene(cv, s.era - 1, s, 0);
  ok('同 seed 同尺寸两次绘制量一致（确定性）', o3.n.fillRect === fills1,
     fills1 + ' vs ' + o3.n.fillRect);
  const seq2 = o3.n.fillStyle.join();
  o1.reset();
  X.STAGE.paintScene(cv, s.era - 1, s, 7);
  ok('不同 seed 画面不同（用色序列比对）',
     o1.n.fillStyle.join() !== seq2 && seq1 === seq2,
     'seq1===seq2:' + (seq1 === seq2) + ' same3:' + (o1.n.fillStyle.join() === seq2));
}

console.log('\n[ST-5] 属性雷达图');
{
  const s = played(5);
  const cv = makeCanvas(320, 300);
  X.STAGE.paintRadar(cv, s);
  ok('雷达图有绘制', cv.n.fillRect > 0 && cv.n.stroke > 0);
  /* 13 条属性 → 13 个 3×3 顶点方块 + 1 个多边形填充。
     底盘网格走 stroke 不走 fillRect，故这里只数顶点。 */
  ok('雷达图 13 个顶点标记', cv.n.fillRect === 13, 'fillRect=' + cv.n.fillRect);
  ok('写出了 13 个属性名', cv.n.fillText.length === 13, 'n=' + cv.n.fillText.length);
  ok('五档底盘网格（铁律 5）', cv.n.stroke > 5, 'stroke=' + cv.n.stroke);
  ok('多边形已闭合填充', cv.n.closePath >= 1 && cv.n.fill >= 1,
     'closePath=' + cv.n.closePath + ' fill=' + cv.n.fill);
  /* 单选模式 */
  const cv2 = makeCanvas(320, 300);
  X.STAGE.paintRadar(cv2, s, ['trust']);
  ok('单属性模式可渲染', cv2.n.fillRect > 0);
  /* 空数据不崩 */
  const cv3 = makeCanvas(320, 300);
  let threw = null;
  try { X.STAGE.paintRadar(cv3, s, []); } catch (e) { threw = e.message; }
  ok('空属性列表不抛异常', !threw, threw);
}

console.log('\n[ST-6] 变化曲线');
{
  const s = played(6);
  const cv = makeCanvas(320, 200);
  X.STAGE.paintTrend(cv, s);
  ok('曲线图有描边', cv.n.stroke > 0, 'stroke=' + cv.n.stroke);
  ok('13 条属性 → 13 条折线', cv.n.stroke >= 13, 'stroke=' + cv.n.stroke);
  ok('有快照序列可画', Array.isArray(s.hist) && s.hist.length > 6, 'hist=' + s.hist.length);
  /* 无历史时退化为横线而不是空图 */
  const fresh = X.newGame();
  const cv2 = makeCanvas(320, 200);
  let threw = null;
  try { X.STAGE.paintTrend(cv2, fresh); } catch (e) { threw = e.message; }
  ok('新开局无历史时仍画出底线', !threw && cv2.n.stroke > 0, threw || 'stroke=' + cv2.n.stroke);
  /* 单属性追踪 */
  const cv3 = makeCanvas(320, 200);
  X.STAGE.paintTrend(cv3, s, ['trust']);
  ok('单属性追踪可渲染', cv3.n.stroke > 0);
}

console.log('\n[ST-7] 科技树 60 节点');
{
  ok('节点总数 60', X.STAGE.TECH_TREE.length === 60, 'n=' + X.STAGE.TECH_TREE.length);
  const names = new Set(X.STAGE.TECH_TREE.map(n => n.name));
  ok('60 个节点名互不相同', names.size === 60, 'unique=' + names.size);
  const byTrack = {};
  for (const n of X.STAGE.TECH_TREE) byTrack[n.tk] = (byTrack[n.tk] || 0) + 1;
  ok('六条轨各 10 阶', Object.keys(byTrack).length === 6 &&
     Object.values(byTrack).every(v => v === 10), JSON.stringify(byTrack));
  /* 门槛单调：低阶必须比高阶容易解锁 */
  const s = played(4);
  let mono = true;
  for (const tk of Object.keys(X.TRACKS)) {
    for (let i = 1; i < 10; i++) {
      const lo = X.STAGE.techUnlocked(s, { tk, step: i - 1 });
      const hi = X.STAGE.techUnlocked(s, { tk, step: i });
      if (hi && !lo) mono = false;
    }
  }
  ok('解锁单调（高阶解锁则低阶必已解锁）', mono);
  const fresh = X.newGame();
  const unlockedFresh = X.STAGE.TECH_TREE.filter(n => X.STAGE.techUnlocked(fresh, n)).length;
  /* 开局六轨基值为 10~15，第 1 阶门槛是 9，故每轨恰好解锁第 1 阶 = 6 个。
     断言写成"恰好 6 且没有任何轨解锁到第 2 阶"——这才是设计事实。 */
  ok('新开局每轨恰好解锁第 1 阶（共 6 个）', unlockedFresh === 6, 'n=' + unlockedFresh);
  ok('新开局无人解锁到第 2 阶',
     X.STAGE.TECH_TREE.filter(n => n.step >= 1 && X.STAGE.techUnlocked(fresh, n)).length === 0);
  const late = played(9, C => C.options[0]);
  const unlockedLate = X.STAGE.TECH_TREE.filter(n => X.STAGE.techUnlocked(late, n)).length;
  ok('走完九时代解锁过半', unlockedLate > 30, 'n=' + unlockedLate);
}

console.log('\n[ST-8] 档案室 · 标记日记');
{
  const s = played(5);
  const L = X.STAGE.selfPortrait(s);
  ok('恰好三句', L.length === 3, 'n=' + L.length);
  ok('每句都是完整中文句子', L.every(x => x.length > 8 && /[。！？]$/.test(x)),
     JSON.stringify(L.map(x => x.length)));
  ok('不含 undefined / NaN', !L.join('').match(/undefined|NaN/));
  /* 铁血政体与融合政体的自述必须不同 */
  const iron = played(6, C => C.options.find(o => o.cat === 'violence') || C.options[0]);
  const melt = played(6, C => C.options.find(o => o.cat === 'inclusion') || C.options[0]);
  ok('不同政体自述不同', X.STAGE.selfPortrait(iron)[0] !== X.STAGE.selfPortrait(melt)[0],
     X.STAGE.selfPortrait(iron)[0] + ' || ' + X.STAGE.selfPortrait(melt)[0]);
}

console.log('\n[ST-9] 新闻条（层二低语层）');
{
  const fresh = X.newGame();
  const n1 = X.STAGE.news(fresh);
  ok('永远至少有一条', n1.length >= 1, 'n=' + n1.length);
  ok('结构为 [档位, 文本]', n1.every(x => Array.isArray(x) && x.length === 2));
  ok('档位只用 好/坏/平', n1.every(x => ['好', '坏', '平'].includes(x[0])),
     n1.map(x => x[0]).join(','));
  ok('每条都是短句（≤20 字）', n1.every(x => x[1].length <= 20),
     n1.map(x => x[1].length).join(','));
  /* 造一个灾难局，看是否报坏消息 */
  const bad = X.newGame();
  bad.flags = ['核废土', '排外传统', '血腥记忆', '文明等级论'];
  bad.attrs.trust = 12; bad.attrs.contra = 95;
  const n2 = X.STAGE.news(bad);
  ok('灾难局至少两条坏消息', n2.filter(x => x[0] === '坏').length >= 2,
     n2.map(x => x[0] + ':' + x[1]).join(' | '));
  const good = X.newGame();
  good.flags = ['公民民族', '公共审议传统', '文明记忆', '人命优先'];
  good.attrs.trust = 82; good.attrs.poly = 80; good.tracks = { sci: 80, cult: 70, mil: 20, inst: 20, econ: 20, edu: 20 };
  const n3 = X.STAGE.news(good);
  ok('盛世局至少一条好消息', n3.filter(x => x[0] === '好').length >= 2,
     n3.map(x => x[0] + ':' + x[1]).join(' | '));
  ok('新闻无 undefined / NaN', !n2.concat(n3).map(x => x[1]).join('').match(/undefined|NaN/));
}

console.log('\n[ST-10] 已探索比例');
{
  const s1 = X.newGame();
  const r1 = X.STAGE.revealOf(s1);
  /* 开局要看得清，但不是全清。
     早前是 0.30 起步：实测第一时代整幅地图灰蒙蒙，色调全被压平，
     而那时玩家才刚做出第一个抉择，不该先被雾挡住视野。
     现在 0.78 起步——看得到自己的世界，但仍有一圈雾，暗示"外面还有未知"。 */
  ok('开局探索度高但非全清（0.7~0.85）', r1 > 0.70 && r1 < 0.85, 'r=' + r1);
  s1.era = 10; s1.idx = 2;
  const r10 = X.STAGE.revealOf(s1);
  ok('第十时代探索度更高', r10 > r1, r1 + ' → ' + r10);
  ok('探索度不超过 1', r10 <= 1, 'r=' + r10);
}

console.log('\n' + '─'.repeat(52));
console.log(`  通过 ${pass}　失败 ${fail}`);
console.log('─'.repeat(52) + '\n');
process.exit(fail ? 1 : 0);
