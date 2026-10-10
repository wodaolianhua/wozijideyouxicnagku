/* ============================================================================
 * uitest.js —— UI 层渲染自检（无浏览器，模拟最小 DOM）
 * 目的：Node 引擎测试通过 ≠ 页面能渲染。本测试跑通 ui.js 的每个渲染分支，
 *       确保任何路径下不抛异常、且产出合法 HTML。
 * ==========================================================================*/
const fs = require('fs'), path = require('path'), vm = require('vm');

/* --- 极简 DOM 替身 ------------------------------------------------------*/
function makeEl() {
  const el = {
    innerHTML: '', style: {}, textContent: '',
    classList: { add(){}, remove(){}, contains(){ return false; } },
    insertAdjacentHTML(_, html) { this._app = (this._app || '') + html; },
    querySelectorAll() { return []; },
    remove() {},
  };
  return el;
}
const appEl = makeEl();

const store = {};
/* 关键：getElementById 必须只对真实存在的 id 返回元素。
 * 早前它对任意 id 都返回假元素，掩盖了一个真实的启动崩溃——ui.js 在
 * DOMContentLoaded 里访问 #btn-continue（在 render() 之前根本不存在），
 * 真实浏览器中直接抛 TypeError，页面全黑。此处必须保持严格。 */
const sandbox = {
  console,
  document: {
    getElementById(id) { return id === 'app' ? appEl : null; },
    querySelectorAll() { return []; },
    createElement: makeEl,
    addEventListener(ev, fn) { if (ev === 'DOMContentLoaded') setTimeout(fn, 0); },
  },
  localStorage: {
    getItem: k => (k in store ? store[k] : null),
    setItem: (k, v) => { store[k] = String(v); },
    removeItem: k => { delete store[k]; },
  },
  setTimeout, clearTimeout, alert: () => {},
  confirm: () => true, prompt: () => '',
};
sandbox.window = sandbox;
sandbox.window.addEventListener = (ev, fn) => { if (ev === 'DOMContentLoaded') setTimeout(fn, 0); };
sandbox.window.scrollTo = () => {};
sandbox.globalThis = sandbox;
sandbox.module = { exports: {} };
vm.createContext(sandbox);

let src = '';
/* 注意：必须包含 mosaic.js 与 stage.js。ui.js 在渲染后会挂载画布，
   若这两个文件缺席，ui.js 顶层的 MOSAIC.playerProfile(S) 会抛 ReferenceError。
   模拟 DOM 的 createElement 返回的元素没有 getContext，ui.js 的 canDraw()
   会因此返回 false，跳过所有画布绘制——这正是本测试要验证的降级路径。 */
for (const f of ['data.js', 'engine.js', 'mosaic.js', 'stage.js', 'ui.js']) {
  src += fs.readFileSync(path.join(__dirname, f), 'utf8') + '\n';
}
src += `
globalThis.__U = { startNew, continueGame, startSecond, nextBeat, pickBeat, finishSecond,
  showStats, showTech, showLog, showArchive, showSyntax, pick, goto, render, viewTitle, viewPlay, viewResult, viewMuseum, viewSecond, viewSecondEnd,
  toggleCard, setViewMode, pickTech, toggleAttr, canDraw, afterRender,
  newGame, applyChoice, advanceEra, resolveEnding, ERAS, ENDINGS, SYNTAX_MAP, CONTACT, AXES, MARKERS, ATTRS, TRACKS, DERIVED, derived, trackEff,
  MOSAIC, STAGE, getS: () => S };
`;
vm.runInContext(src, sandbox, { filename: 'bundle-ui.js' });
const U = sandbox.__U;

let pass = 0, fail = 0;
const ok = (n, c, extra) => {
  if (c) { pass++; console.log('  ✓ ' + n); }
  else { fail++; console.log('  ✗ ' + n + (extra ? '  → ' + extra : '')); }
};
/* render 助手：模拟浏览器的赋值语义。
   坑：view* 系列是"纯返回 HTML 字符串"，不写 innerHTML；而 goto()/render()
   内部会自己写。若助手只读 innerHTML，则所有 viewPlay() 断言都在检查空串——
   早前 [UI-2] 的"渲染结果无 undefined"就是这样恒真的，它其实什么都没验。
   故：先看 innerHTML（goto 路径），为空则回退到返回值（view* 路径）。 */
const render = fn => {
  appEl.innerHTML = ''; appEl._app = '';
  const ret = fn();
  if (appEl.innerHTML) return appEl.innerHTML + (appEl._app || '');
  return (typeof ret === 'string' ? ret : '') + (appEl._app || '');
};

console.log('\n[UI-0] 启动路径（回归防护）');
{
  /* 模拟真实浏览器的启动顺序：DOMContentLoaded 回调必须把 #app 填上内容。
   * 曾经的 bug：回调里先访问 #btn-continue（render() 之前根本不存在）→ TypeError
   * 中断启动 → 真实浏览器里整页全黑，而模拟 DOM 因 getElementById 永不返回 null
   * 恰好掩盖了它。此处 render() 必须不抛异常、且执行后 #app 非空。 */
  let bootErr = null;
  appEl.innerHTML = ''; appEl._app = '';
  try { U.render(); } catch (e) { bootErr = e; }
  ok('render() 不抛异常', !bootErr, bootErr && bootErr.message);
  ok('render() 后 #app 非空（启动即有画面）', appEl.innerHTML.length > 200,
     'len=' + appEl.innerHTML.length);
  ok('无存档时 goto(title) 不因缺按钮崩溃', (() => {
    try { U.goto('title'); return true; } catch (e) { return false; }
  })());
}

console.log('\n[UI-0b] 走完十时代必须落在结局页（回归防护）');
{
  /* 曾经的真实 bug：viewPlay 在渲染期调用 goto('result')，而 goto 内部又会 render()。
   * 于是栈上出现两层 render()——内层把结局页写进 #app，外层随后用尚未赋值完成的
   * 返回值把 #app 覆盖成字符串 "undefined"，表现为走完第十时代后白屏。
   * 这个 bug 只在真实浏览器出现（模拟 DOM 的 render 替身不写 innerHTML，掩盖了它），
   * 故此处显式模拟浏览器的赋值语义：render() 必须把返回值写进 appEl.innerHTML。 */
  U.startNew();
  let steps = 0, ended = false;
  while (steps++ < 40) {
    const html = appEl.innerHTML;
    if (/<h1[^>]*>/.test(html) && /四维评价|结局/.test(html)) { ended = true; break; }
    const m = appEl._app || '';
    // 模拟点击第一个选项
    const optMatch = (html + m).match(/pick\('([ABC])'\)/);
    if (!optMatch) break;
    try { U.pick(optMatch[1]); } catch (e) { break; }
  }
  const out = appEl.innerHTML + (appEl._app || '');
  ok('三十次抉择后进入结局页（非空）', ended && out.length > 500,
     'ended=' + ended + ' len=' + out.length);
  ok('结局页不是字符串 undefined', out.trim() !== 'undefined' && !/^undefined$/.test(out.trim()));
  ok('结局页含四维评价', out.includes('四维评价'));
  ok('结局页含回望', out.includes('回望'));
}

console.log('\n[UI-1] 标题页');
{
  const h = render(() => U.goto('title'));
  ok('渲染非空', h.length > 200);
  ok('含游戏名', h.includes('从部落到星辰'));
  ok('无 undefined 泄漏', !h.includes('undefined'));
  ok('无模板残留 ${', !h.includes('${'));
  ok('无 [object Object]', !h.includes('[object Object]'));
}

console.log('\n[UI-2] 三十个抉择逐个渲染 + 三个选项逐个点击');
{
  U.startNew();
  let threw = null, undefHits = 0, totalOpts = 0;
  for (let e = 0; e < 10; e++) {
    for (let c = 0; c < 3; c++) {
      let h;
      try { h = render(() => U.viewPlay()); } catch (err) { threw = `era${e+1}/choice${c+1}: ${err.message}`; break; }
      if (h.includes('undefined')) undefHits++;
      // 逐个选项点击（每次重开）
      for (const o of U.ERAS[e].choices[c].options) {
        totalOpts++;
        try {
          U.startNew();
          // 快进到目标位置
          for (let ee = 0; ee < e; ee++) { for (let cc = 0; cc < 3; cc++) {
            U.pick(U.ERAS[ee].choices[cc].options[0].key);
          } }
          for (let cc = 0; cc < c; cc++) U.pick(U.ERAS[e].choices[cc].options[0].key);
          U.pick(o.key);
          const hh = render(() => U.viewPlay());
          if (hh.includes('undefined')) undefHits++;
        } catch (err) { threw = `click era${e+1}/choice${c+1}/${o.key}: ${err.message}`; break; }
      }
      if (threw) break;
    }
    if (threw) break;
  }
  ok('30 抉择 × 3 选项全部可渲染可点击', !threw, threw);
  ok(`共 ${totalOpts} 个选项路径`, totalOpts === 90);
  ok('渲染结果无 undefined', undefHits === 0, `${undefHits} 处`);
}

console.log('\n[UI-3] 结局页（12 结局 + 兜底）');
{
  let bad = [];
  const names = new Set();
  for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) for (let k = 0; k < 3; k++) {
    U.startNew();
    for (let e = 0; e < 10; e++) for (let c = 0; c < 3; c++) {
      const key = ['A','B','C'][(i + j + k + c) % 3];
      U.pick(U.ERAS[e].choices[c].options.find(o => o.key === key) ? key : 'A');
    }
    let h;
    try { h = render(() => U.goto('result')); } catch (err) { bad.push('render:' + err.message); continue; }
    if (h.includes('undefined')) bad.push('undefined in ' + (h.match(/<h1>([^<]+)/) || [])[1]);
    const m = h.match(/<h1>([^<]+)<\/h1>/);
    if (m) names.add(m[1]);
    if (!h.includes('隐藏句')) bad.push('缺隐藏句: ' + (m && m[1]));
    if (!h.includes('四维评价')) bad.push('缺四维评价: ' + (m && m[1]));
  }
  ok('结局页全部可渲染', bad.length === 0, bad.slice(0, 3).join(' | '));
  ok(`覆盖结局：${[...names].join('、')}`, names.size >= 2);
}

console.log('\n[UI-4] 属性面板 / 回望 / 语法面板');
{
  U.startNew();
  for (let e = 0; e < 4; e++) for (let c = 0; c < 3; c++) U.pick(U.ERAS[e].choices[c].options[0].key);
  const threw = [];
  try { render(() => U.showStats()); } catch (err) { threw.push('showStats:' + err.message); }
  try { render(() => U.showLog()); } catch (err) { threw.push('showLog:' + err.message); }
  ok('属性面板可渲染', !threw.some(t => t.startsWith('showStats')), threw.join('|'));
  ok('回望面板可渲染', !threw.some(t => t.startsWith('showLog')), threw.join('|'));
}

console.log('\n[UI-8] 马赛克舞台（本次新增）');
{
  U.startNew();
  for (let c = 0; c < 3; c++) U.pick(U.ERAS[0].choices[c].options[0].key);
  render(() => U.goto('play'));
  const S = U.getS();
  let h = appEl.innerHTML;
  ok('对局页含地图画布', h.includes('cv-map'));
  ok('对局页含地图/插画切换', h.includes("setViewMode('map')") && h.includes("setViewMode('scene')"));
  ok('对局页含新闻条容器', h.includes('id="newsbar"'));
  ok('对局页含三条抉择卡', (h.match(/class="card /g) || []).length === 3,
     'count=' + (h.match(/class="card /g) || []).length);
  ok('未发生的抉择标为"尚未发生"', h.includes('尚未发生'));
  ok('只读卡不带 onclick（不可被误点）',
     !/class="opt" disabled" onclick/.test(h) && !/disabled" onclick="pick/.test(h));

  /* 切到插画模式 */
  U.setViewMode('scene');
  h = render(() => U.viewPlay());
  ok('插画模式渲染出场景画布', h.includes('cv-scene'));
  U.setViewMode('map');

  /* 属性快照序列：趋势图的数据源 */
  ok('每次抉择都记了属性快照', Array.isArray(S.hist) && S.hist.length >= 3,
     'hist=' + (S.hist ? S.hist.length : 'none'));
  ok('快照含全部 13 条属性', S.hist && S.hist[0] && Object.keys(S.hist[0]).length === 13,
     S.hist && S.hist[0] ? Object.keys(S.hist[0]).length : 'none');

  /* 召见制：未来抉择可展开预览，但不可选。
     注意不能靠 split('card future')[1] 取卡——同一时代有两张 future 卡，
     split 取到的是第一张（抉择 2），而 toggleCard(2) 展开的是第二张（抉择 3），
     断言会检查一张根本没展开的卡。改为数 cbg 的数量。 */
  ok('未来抉择卡存在', (h.match(/class="card future"/g) || []).length === 2,
     'future=' + (h.match(/class="card future"/g) || []).length);
  const cbgBefore = (h.match(/class="cbg"/g) || []).length;
  render(() => U.toggleCard(2));
  h = appEl.innerHTML || '';
  ok('展开未来抉择后多出完整背景（可预览全文）',
     (h.match(/class="cbg"/g) || []).length === cbgBefore + 1,
     `${cbgBefore} → ${(h.match(/class="cbg"/g) || []).length}`);
  /* 整个对局页有可点选项的按钮数必须恒为 3——只有"待决"那一张卡。
     展开任何 future 卡都不该增加这个数字。
     早前用 split 切字符串来判断，但切点落在"待决卡"之后，
     把待决卡自己的 3 个按钮也算进了 future 区，断言必然失败。 */
  const pickCount = s => (s.match(/onclick="pick\('[^']+'\)"/g) || []).length;
  ok('展开后整页可点选项仍为 3 个（只有待决卡）', pickCount(h) === 3,
     'n=' + pickCount(h) + ' ' + (h.match(/onclick="pick\('[^']+'\)"/g) || []).join(','));
  /* 收起 */
  render(() => U.toggleCard(2));
  h = appEl.innerHTML || '';
  ok('再点一次可收起', (h.match(/class="cbg"/g) || []).length === cbgBefore,
     'n=' + (h.match(/class="cbg"/g) || []).length);
}

console.log('\n[UI-9] 科技树 60 节点');
{
  U.startNew();
  for (let e = 0; e < 6; e++) for (let c = 0; c < 3; c++) U.pick(U.ERAS[e].choices[c].options[0].key);
  ok('科技树节点总数为 60', U.STAGE.TECH_TREE.length === 60, 'n=' + U.STAGE.TECH_TREE.length);
  const h = render(() => U.showTech());
  const nodes = (h.match(/class="tnode /g) || []).length;
  ok('科技树渲染出 60 个可点节点', nodes === 60, 'n=' + nodes);
  ok('节点按 6 轨分列', (h.match(/class="tcol"/g) || []).length === 6);
  ok('科技门单独列出', h.includes('结局判定只看这十个'));
  const h2 = render(() => { U.pickTech('sci', 9); });
  ok('点节点后出现该阶说明', /科学方法 · 第 10 阶/.test(h2), (h2.match(/class="tinfo"[\s\S]{0,140}/) || [])[0]);
  /* 门槛提示只应在"未解锁"时出现。走完六个时代后 sci≈55，
     第 1 阶（需 9）早已解锁，若在此断言门槛文案就等于断言一个假事实。
     故改点第 10 阶（需 90）——它必定未解锁。 */
  ok('未解锁节点给出还差多少的门槛提示', /需要科学方法达到 90，当前 \d+/.test(h2),
     (h2.match(/class="tinfo"[\s\S]{0,200}/) || [])[0]);
  const h3 = render(() => { U.pickTech('mil', 0); });
  ok('已解锁节点显示"已解锁"', /已解锁。/.test(h3));
}

console.log('\n[UI-10] 档案室 · 标记日记');
{
  U.startNew();
  for (let c = 0; c < 3; c++) U.pick(U.ERAS[0].choices[c].options[1].key);
  const h = render(() => U.showArchive());
  ok('档案室可渲染', h.length > 200);
  ok('含"我是谁"三句自述', h.includes('我是谁'));
  ok('自述是三段', (h.match(/class="hl-text"/g) || []).length === 3,
     'n=' + (h.match(/class="hl-text"/g) || []).length);
  ok('含走过的叙事轴', h.includes('你走过的轴'));
  ok('含门闩清单', h.includes('门闩清单'));
}

console.log('\n[UI-11] 结局页的世界地图');
{
  U.startNew();
  for (let e = 0; e < 10; e++) for (let c = 0; c < 3; c++) U.pick(U.ERAS[e].choices[c].options[0].key);
  const h = render(() => U.goto('result'));
  ok('结局页含终局地图', h.includes('cv-endmap'));
  ok('地图旁有说明文字', h.includes('红线还是紫带'));
  ok('政务图仍可渲染（无 undefined）', !h.includes('undefined'));
}

console.log('\n[UI-12] 无画布环境的降级（回归防护）');
{
  /* 模拟 DOM 的 createElement 返回的元素没有 getContext。
     若 ui.js 未做 canDraw() 判定，afterRender 会直接 TypeError，
     表现为"测试环境一进对局页就崩"——真实浏览器有 canvas，
     但任何降级路径（如隐私模式禁 canvas）都会踩到同一条路。 */
  ok('canDraw() 正确识别无画布环境', U.canDraw() === false);
  let threw = null;
  try { U.startNew(); U.afterRender(); } catch (e) { threw = e.message; }
  ok('无画布时 afterRender 不抛异常', !threw, threw);
  const h = render(() => U.viewPlay());
  ok('无画布时对局页仍出内容（文字流降级）', h.length > 500 && h.includes('抉择'));
}

console.log('\n[UI-5] 第二段 · 接触纪元（六个结局全跑）');
{
  const seen = new Set();
  let threw = null;
  // 遍历三拍 × 每拍三选项 = 27 条路径（第一拍决定最多）
  for (let a = 0; a < 3 && !threw; a++)
  for (let b = 0; b < 3 && !threw; b++)
  for (let c = 0; c < 3 && !threw; c++) {
    try {
      U.startNew();
      // 直接构造一个能进第二段的第二段状态（跳过第一段结局门槛）
      const st = { syntax: ['繁复型','混合型','最小型','技术性','生活性'][(a + b + c) % 5],
                   beat: 0, flags: {}, done: false, ending: null };
      sandbox.__setSecond(st);
      render(() => U.goto('second'));
      U.nextBeat();                      // 开场 → 第一拍
      render(() => U.goto('second'));
      // pickBeat 内部已自动推进 beat，不需要再调 nextBeat
      U.pickBeat(['A','B','C'][a]);
      render(() => U.goto('second'));
      U.pickBeat(['A','B','C'][b]);
      render(() => U.goto('second'));
      U.pickBeat(['A','B','C'][c]);
      render(() => U.goto('second'));     // → beat=99 事件链页
      U.finishSecond();
      const h = render(() => U.goto('second'));
      const m = h.match(/<h1>([^<]+)<\/h1>/);
      if (m) seen.add(m[1]);
      if (h.includes('undefined')) threw = 'undefined in second ending';
    } catch (err) { threw = `a${a}b${b}c${c}: ${err.message}\n${(err.stack||'').split('\n').slice(1,4).join('\n')}`; }
  }
  ok('27 条路径全部可完成', !threw, threw);
  ok(`接触纪元结局覆盖：${[...seen].join('、')}`, seen.size >= 2);
}

console.log('\n[UI-6] 结局博物馆');
{
  const h = render(() => U.goto('museum'));
  ok('可渲染', h.length > 100);
  ok('无 undefined', !h.includes('undefined'));
}

console.log('\n[UI-7] 存档 / 读档 / 继续');
{
  U.startNew();
  for (let c = 0; c < 3; c++) U.pick(U.ERAS[0].choices[c].options[0].key);
  render(() => U.viewPlay());
  const has = !!sandbox.localStorage.getItem('from_tribes_to_stars_v1');
  ok('进度已写入 localStorage', has);
  const h = render(() => U.continueGame());
  ok('读档后可继续渲染', h.length > 200);
  ok('读档后无 undefined', !h.includes('undefined'));
}

console.log('\n' + '─'.repeat(52));
console.log(`  通过 ${pass}　失败 ${fail}`);
console.log('─'.repeat(52) + '\n');
process.exit(fail ? 1 : 0);
