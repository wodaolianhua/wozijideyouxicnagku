/* ============================================================================
 * ui.js —— 界面层（原生 DOM，无框架）
 *
 * 设计原则（来自《05》视觉节 +《07/05》三层文本呈现体系）：
 *   层一 世界本身在说话 —— 马赛克地图是主视觉，不是一张配图
 *   层二 微叙事界面     —— 悬停证言、新闻条、顾问便签，3 秒可读
 *   层三 抉择剧场       —— 召见制：先看摘要，想清楚了再点开全文与选项
 *
 * 硬约束：view* 系列必须是"纯返回 HTML 字符串"的函数，绝不可在渲染过程中调 goto()。
 * 原因：goto() 内部会 render()，而 render() 又执行 app.innerHTML = viewPlay()。
 * 一旦 viewPlay 在自己尚未返回时触发 goto，栈上会出现两层 render()，
 * 内层先把 #app 写好，外层随后用（尚未赋值完成的）返回值把 #app 覆盖成字符串
 * "undefined" —— 表现为走完第十时代后结局页白屏。
 * ==========================================================================*/

let S = null;          // 存档
let screen = 'title';  // title | play | result | museum | second
let lastApplied = null;
let secondState = null;

/* 界面局部状态（不入存档） */
let viewMode = 'map';    // map | scene：主舞台显示地图还是抉择插画
let openCards = {};      // 抉择卡展开状态：{eraIdx: {choiceIdx: true}}
let techPick = null;     // 科技树当前点开的节点
let hoverInfo = null;    // 悬停地标 {kind, text, x, y}

/* --- 启动 ----------------------------------------------------------------*/
window.addEventListener('DOMContentLoaded', () => {
  /* 注意：此处不能直接取 #btn-continue —— 它由 viewTitle() 生成的 HTML 才会出现，
     在 render() 之前 DOM 里必然为空，直接访问会抛 TypeError 并中断整个启动流程
     （表现为黑屏：三个脚本都加载成功、#app 却始终是空的）。
     故先 render()，渲染完再按需显示"继续"按钮。 */
  render();
  const cont = document.getElementById('btn-continue');
  if (cont) cont.style.display = hasSave() ? '' : 'none';
});

/* --- 路由 ----------------------------------------------------------------*/
function render() {
  const app = document.getElementById('app');
  if (screen === 'title')  app.innerHTML = viewTitle();
  else if (screen === 'play')   app.innerHTML = viewPlay();
  else if (screen === 'result') app.innerHTML = viewResult();
  else if (screen === 'museum') app.innerHTML = viewMuseum();
  else if (screen === 'second') app.innerHTML = viewSecond();
  afterRender();
}

/* 画布必须在 innerHTML 写完之后挂载——innerHTML 会清空 #app，
   任何提前挂上的 canvas 都会连同 DOM 一起被丢弃。 */
function afterRender() {
  if (!canDraw()) return;
  if (screen === 'play' && S) mountStage();
  if (screen === 'second' && secondState) mountStage2();
  if (screen === 'result' && S) paintEndMap();
}

/* 真实浏览器才有 canvas。uitest.js 的模拟 DOM 没有，必须显式挡掉，
   否则整个测试进程会因 TypeError 中断。 */
function canDraw() {
  try {
    const c = document.createElement('canvas');
    return !!(c && typeof c.getContext === 'function');
  } catch (e) { return false; }
}

/* 回到标题时同步"继续"按钮的可见性——每次重新渲染标题页都会重建该按钮，
   若不在此处统一处理，从对局/结局返回后会残留上一次的状态。 */
function goto(sc) {
  screen = sc;
  if (sc === 'play' || sc === 'second') closeModal();
  render();
  if (sc === 'title') {
    const cont = document.getElementById('btn-continue');
    if (cont) cont.style.display = hasSave() ? '' : 'none';
  }
  window.scrollTo(0, 0);
}

/* ============================ 标题 =======================================*/
function viewTitle() {
  const m = loadMuseum();
  const n = Object.keys(m).length;
  return `
  <div class="title">
    <div class="title-inner">
      <h1>从部落到星辰</h1>
      <p class="sub">十时代 · 三十抉择 · 十二结局</p>
      <p class="desc">
        每一块马赛克都是你走过的路。<br>
        地图上的颜色不是画上去的，是你选出来的。
      </p>
      <div class="btns">
        <button class="btn primary" onclick="startNew()">开始新游戏</button>
        <button class="btn" id="btn-continue" onclick="continueGame()">继续上次进度</button>
        <button class="btn" onclick="goto('museum')">结局博物馆${n ? `（${n}）` : ''}</button>
      </div>
      <p class="foot">可玩垂直切片 · 基于本项目 50 份设计文档 · 单机纯前端</p>
    </div>
  </div>`;
}

function startNew() { S = newGame(); secondState = null; openCards = {}; lastApplied = null; clearSave(); goto('play'); }
function continueGame() { S = loadGame(); if (!S) return startNew(); secondState = S.second || null; goto(S.era > 10 ? 'result' : 'play'); }

/* ============================ 游戏主界面 =================================*/
function viewPlay() {
  const E = ERAS[S.era - 1];
  if (!E) { screen = 'result'; return viewResult(); }   // 不调 goto，避免渲染期副作用
  const C = E.choices[S.idx];
  if (!C) { advanceEra(S); S.idx = 0; return viewPlay(); }  // 纯递归，无副作用

  const pol = MOSAIC ? MOSAIC.playerProfile(S) : null;
  const art = MOSAIC ? MOSAIC.ERA_ART[S.era - 1] : null;

  return `
  <div class="play">
    ${hudBar(E)}

    <div class="stagewrap">
      <!-- ============ 左：马赛克舞台（主视觉） ============ -->
      <div class="stage">
        <div class="stage-tabs">
          <button class="stab ${viewMode === 'map' ? 'on' : ''}" onclick="setViewMode('map')">地图</button>
          <button class="stab ${viewMode === 'scene' ? 'on' : ''}" onclick="setViewMode('scene')">插画</button>
          <span class="stagelab">${art ? art.label : ''} · ${pol ? pol.mark : ''}政体</span>
          <span class="stagelab right">已探索 ${Math.round(STAGE.revealOf(S) * 100)}%</span>
        </div>
        <div class="canvasbox" id="stagebox">
          ${viewMode === 'map'
            ? `<canvas id="cv-map" class="maincv"></canvas>
               <div class="whisper" id="whisper" style="display:none"></div>`
            : `<canvas id="cv-scene" class="maincv"></canvas>
               <div class="scenecap">${C.title}</div>`}
        </div>
        <div class="newsbar" id="newsbar"></div>
      </div>

      <!-- ============ 右：抉择剧场（召见制） ============ -->
      <div class="side">
        <div class="tintbox">
          <div class="polline">${pol ? polTag(pol) : ''}</div>
          <p class="tint">${E.tint}</p>
        </div>
        <div class="cards">
          ${E.choices.map((c, i) => choiceCard(c, i, S.idx, E)).join('')}
        </div>
        ${lastApplied ? consequencePanel(lastApplied) : ''}
      </div>
    </div>
  </div>`;
}

function polTag(pol) {
  if (pol.mark === '铁血') return `<span class="ptag iron">边界锐利 · 铁血</span>`;
  if (pol.mark === '融合') return `<span class="ptag melt">边界弥散 · 融合</span>`;
  return `<span class="ptag mid">边界未定</span>`;
}

function hudBar(E) {
  return `
  <div class="hud">
    <div class="hud-era">
      <span class="en">第 ${cn(S.era)} 时代</span>
      <strong>${E.name}</strong>
    </div>
    <div class="hud-prog">
      ${E.choices.map((_, i) => `<span class="dot ${i < S.idx ? 'done' : i === S.idx ? 'now' : ''}"></span>`).join('')}
    </div>
    <div class="hud-btns">
      <button class="mini" onclick="showStats()">状况</button>
      <button class="mini" onclick="showTech()">科技树</button>
      <button class="mini" onclick="showLog()">回望</button>
      <button class="mini" onclick="showArchive()">档案</button>
      <button class="mini" onclick="confirmQuit()">退出</button>
    </div>
  </div>`;
}

/* 抉择卡：召见制的核心。
   未处理的抉择只给标题与一句话摘要（"先知道有件事要发生"），
   点开才给全文与选项。已处理的显示你选了什么，不可改。 */
function choiceCard(C, i, cur, E) {
  const done = i < cur, isCur = i === cur;
  const log = S.log.filter(l => l.era === S.era && l.title === C.title)[0];
  /* 展开状态的键必须与 toggleCard 构造的完全一致。
     早前这里写 S.era - 1 + ':' + i，而 toggleCard 写 S.era + ':' + i，
     两者永远对不上——点"展开"毫无反应。键统一用 1 起的时代号。 */
  const opened = isCur || (openCards[S.era + ':' + i] === true);
  const sum = oneLine(C);

  if (done) {
    return `
    <div class="card done">
      <div class="cardhead">
        <span class="cn">抉择 ${i + 1}</span>
        <span class="cpick">${log ? log.key + ' · ' + log.label : '已处理'}</span>
      </div>
      <div class="ctitle">${C.title.replace(/^抉择 \d+ · /, '')}</div>
      <div class="csum">${sum}</div>
      <button class="cardmore" onclick="toggleCard(${i})">展开全文 ▾</button>
      ${opened ? cardBody(C, true) : ''}
    </div>`;
  }
  if (isCur) {
    return `
    <div class="card cur">
      <div class="cardhead">
        <span class="cn">抉择 ${i + 1}</span>
        <span class="cnow">待决</span>
      </div>
      <div class="ctitle">${C.title.replace(/^抉择 \d+ · /, '')}</div>
      <div class="csum">${sum}</div>
      ${cardBody(C, false)}
    </div>`;
  }
  /* 未来抉择：只给摘要。可以点开预览，但不能选——按召见制，未来未发生。 */
  return `
  <div class="card future">
    <div class="cardhead"><span class="cn">抉择 ${i + 1}</span><span class="clock">尚未发生</span></div>
    <div class="ctitle">${C.title.replace(/^抉择 \d+ · /, '')}</div>
    <div class="csum">${sum}</div>
    <button class="cardmore" onclick="toggleCard(${i})">先看看会发生什么 ▾</button>
    ${opened ? cardBody(C, true) : ''}
  </div>`;
}

function cardBody(C, readonly) {
  /* 只读卡（已处理 / 未发生）绝不能带 onclick。
     带着 onclick 的 disabled 按钮仍会被自动化点击命中，
     uitest 的"找第一个 pick('X')"就会点到一张不该点的卡上——
     真实用户点不到（disabled），测试能点，这是两个世界。 */
  return `
  <div class="cbody">
    <p class="cbg">${C.bg}</p>
    <div class="opts">
      ${C.options.map(o => `
        <button class="opt" ${readonly ? 'disabled' : `onclick="pick('${o.key}')"`}>
          <span class="k">${o.key}</span>
          <span class="body">
            <span class="label">${o.label}</span>
            <span class="desc">${o.desc}</span>
            ${readonly ? '' : `<span class="imm">${o.imm}</span><span class="lng">${o.long}</span>`}
          </span>
        </button>`).join('')}
    </div>
  </div>`;
}

/* 一句话摘要：从 bg 首句截断，避免玩家必须展开才知道是什么事 */
function oneLine(C) {
  const s = (C.bg || '').split(/[。；\n]/)[0];
  return s.length > 34 ? s.slice(0, 34) + '…' : s;
}

function toggleCard(i) {
  const k = S.era + ':' + i;
  openCards[k] = !openCards[k];
  render();
}

function setViewMode(m) { viewMode = m; render(); }

/* --- 挂载画布 ------------------------------------------------------------*/
function mountStage() {
  const news = document.getElementById('newsbar');
  if (news) {
    news.innerHTML = STAGE.news(S).map(([k, t]) =>
      `<span class="news ${k}">${t}</span>`).join('');
  }
  const box = document.getElementById('stagebox');
  if (viewMode === 'map') {
    const cv = document.getElementById('cv-map');
    if (!cv) return;
    /* 地图尺寸跟着容器走。容器高度由 CSS 固定，避免 0 高度画不出东西。 */
    const w = cv.clientWidth || box.clientWidth || 900;
    const h = cv.clientHeight || box.clientHeight || 460;
    const off = STAGE.paintMap(cv, S.era - 1, S, STAGE.revealOf(S));
    cv._marks = off._marks || [];
    bindHover(cv, box);
  } else {
    const cv = document.getElementById('cv-scene');
    if (cv) STAGE.paintScene(cv, S.era - 1, S, S.idx * 7 + S.era);
  }
}

/* 悬停证言（《07/05》层二：15 字内的人话，不是属性） */
function bindHover(cv, box) {
  const tip = document.getElementById('whisper');
  cv.onmousemove = ev => {
    const r = cv.getBoundingClientRect();
    const x = ev.clientX - r.left, y = ev.clientY - r.top;
    const m = MOSAIC.hoverMark(cv._marks, x, y);
    if (!m) { hideTip(cv, tip); return; }
    const w = STAGE.wordFor(m);
    cv._hovered = m;
    STAGE.paintHover(cv, m);
    if (tip) {
      tip.style.display = '';
      tip.textContent = w.text;
      tip.className = 'whisper on ' + (m.kind === '绞架' || m.kind === '废墟' || m.kind === '发射井' ? 'bad' : '');
      /* 引线终点固定在右上角，证言也放那儿，与 MOSAIC.paintHover 的引线一致 */
      tip.style.left = Math.min(box.clientWidth - 190, m.cx + 50) + 'px';
      tip.style.top = '12px';
    }
  };
  cv.onmouseleave = () => hideTip(cv, tip);
}
function hideTip(cv, tip) {
  if (tip) tip.style.display = 'none';
  STAGE.paintHover(cv, null);
}

/* 后果面板：让因果可感知（《07/05》四法之一） */
function consequencePanel(ap) {
  if (!ap || (!ap.attr.length && !ap.track.length && !ap.flags.length && !ap.notes.length)) return '';
  const attr = ap.attr.map(a => {
    const up = a.d > 0;
    return `<li class="${up ? 'up' : 'down'}">${a.name} <b>${up ? '+' : ''}${a.d}</b></li>`;
  }).join('');
  const trk = ap.track.map(t => `<li class="${t.v > 0 ? 'up' : 'down'}">${t.name} ${t.base ? '基值' : '修正'} <b>${t.v > 0 ? '+' : ''}${t.v}${t.base ? '' : '%'}</b><span class="dim">${t.base ? '' : '（逐代衰减）'}</span></li>`).join('');
  const fl = ap.flags.filter(f => MARKERS[f] && !MARKERS[f].tech)
               .map(f => `<li class="flag">门闩：${MARKERS[f].name}</li>`).join('');
  const nt = ap.notes.map(n => `<li class="note">${n}</li>`).join('');
  return `
  <div class="conseq">
    <h3>这个决定的即时后果</h3>
    <ul>${attr}${trk}${fl}${nt}</ul>
    <p class="hint">这些数字会随时代推移衰减。某些门闩不会（《12》铁律 1、3）。</p>
  </div>`;
}

/* --- 选择 ----------------------------------------------------------------*/
function pick(key) {
  const E = ERAS[S.era - 1], C = E.choices[S.idx];
  if (!C) return;
  const opt = C.options.find(o => o.key === key);
  if (!opt) return;
  lastApplied = applyChoice(S, S.era - 1, C, opt);
  /* 记录属性快照，供趋势图画线。玩家要看的"我现在是什么状况"不只是当前值。 */
  S.hist = S.hist || [];
  S.hist.push({ ...S.attrs });
  S.idx += 1;
  saveGame(S);
  render();
}

function confirmQuit() {
  if (confirm('确定退出？进度会保留在本地存档。')) { saveGame(S); goto('title'); }
}

/* ============================ 状况面板（属性 + 图表）=====================*/
function showStats() {
  const box = document.getElementById('app');
  const attrs = Object.keys(ATTRS).map(k => {
    const A = ATTRS[k], v = S.attrs[k];
    const mid = (A.min + A.max) / 2;
    const pct = ((v - A.min) / (A.max - A.min)) * 100;
    const dev = Math.abs(v - mid) / ((A.max - A.min) / 2) * 50;
    const cls = v > mid + 2 ? 'up' : v < mid - 2 ? 'down' : '';
    return `<div class="arow" onclick="toggleAttr('${k}')" style="cursor:pointer">
      <span class="an">${A.name}${A.decay ? '' : ' <i class="noexp">豁免衰减</i>'}</span>
      <span class="abar"><i style="width:${pct}%" class="${cls}"></i></span>
      <span class="av ${cls}">${v}</span>
    </div>`;
  }).join('');

  const tracks = Object.keys(TRACKS).map(k => {
    const eff = trackEff(S, k), base = S.tracks[k];
    const bonus = eff - base;
    return `<div class="arow">
      <span class="an">${TRACKS[k].name}</span>
      <span class="abar"><i style="width:${eff}%" class="${bonus > 0 ? 'up' : bonus < 0 ? 'down' : ''}"></i></span>
      <span class="av">${eff}${bonus !== 0 ? `<i class="delta">${bonus > 0 ? '+' : ''}${bonus}</i>` : ''}</span>
    </div>`;
  }).join('');

  const der = ['environment','nationalism','diplomacy','welfare','coop','consumerism','dissonance','reformSpace']
    .map(k => `<span class="dchip"><b>${DERIVED[k].name}</b> ${Math.round(derived(S, k))}</span>`).join('');

  const fl = S.flags.filter(f => MARKERS[f] && !MARKERS[f].tech)
    .map(f => `<span class="fchip">${MARKERS[f].name}</span>`).join('') || '<span class="dim">尚无</span>';

  const tech = TECHS.filter(t => S.flags.includes(t))
    .map(t => `<span class="tchip">${MARKERS[t] ? MARKERS[t].name : t}</span>`).join('') || '<span class="dim">尚无</span>';

  const overlay = `
  <div class="modal wide" onclick="closeModal(event)">
    <div class="panel" onclick="event.stopPropagation()">
      <h2>状况</h2>
      <p class="hint">点任意一行可单独追踪它的走势。条形只给方向，形状才给历史。</p>
      <div class="chartgrid">
        <div class="chartbox">
          <div class="cblabel">属性雷达 · 十三项</div>
          <canvas id="cv-radar" class="chartcv"></canvas>
        </div>
        <div class="chartbox">
          <div class="cblabel">变化曲线 · 跨抉择 <span class="dim" id="attrscope">全部</span></div>
          <canvas id="cv-trend" class="chartcv"></canvas>
        </div>
      </div>
      <div class="sec"><h3>隐藏属性（全案脊柱）</h3>${attrs}</div>
      <div class="sec"><h3>科技轨（基值 + 衰减修正）</h3>${tracks}</div>
      <div class="sec"><h3>派生指数</h3><div class="chips">${der}</div></div>
      <div class="sec"><h3>叙事标记 · 门闩（不衰减、不配百分比）</h3><div class="chips">${fl}</div></div>
      <div class="sec"><h3>科技门</h3><div class="chips">${tech}</div></div>
      <button class="btn" onclick="closeModal()">关闭</button>
    </div>
  </div>`;
  box.insertAdjacentHTML('beforeend', overlay);
  if (canDraw()) {
    const r = document.getElementById('cv-radar'), t = document.getElementById('cv-trend');
    if (r) STAGE.paintRadar(r, S, trackedAttrs());
    if (t) STAGE.paintTrend(t, S, trackedAttrs());
  }
}

/* 追踪中的属性：没选就是全部 13 条，选了就只画选中的 */
function trackedAttrs() {
  const n = document.getElementById('attrscope');
  if (n && n._keys) return n._keys;
  return Object.keys(ATTRS);
}
function toggleAttr(k) {
  const n = document.getElementById('attrscope');
  if (!n) return;
  const cur = n._keys || Object.keys(ATTRS);
  let next = cur.slice();
  const i = next.indexOf(k);
  if (i >= 0) { if (next.length > 1) next.splice(i, 1); }
  else next = [k];                       // 点第一下先单看这一条
  n._keys = next;
  n.textContent = next.length === 13 ? '全部' : next.map(x => ATTRS[x].name).join('、');
  const t = document.getElementById('cv-trend');
  if (t && canDraw()) STAGE.paintTrend(t, S, next);
}
function closeModal() { document.querySelectorAll('.modal').forEach(m => m.remove()); }

/* --- 科技树（60 个可点节点）---------------------------------------------*/
function showTech() {
  const box = document.getElementById('app');
  const byTrack = {};
  for (const n of STAGE.TECH_TREE) (byTrack[n.tk] = byTrack[n.tk] || []).push(n);

  const grid = Object.keys(TRACKS).map(tk => {
    const nodes = (byTrack[tk] || []).map(n => {
      const un = STAGE.techUnlocked(S, n);
      return `<button class="tnode ${un ? 'on' : 'off'} ${techPick === tk + n.step ? 'sel' : ''}"
        onclick="pickTech('${tk}',${n.step})">
        <span class="tn">${n.step + 1}</span><span class="tl">${n.name}</span>
      </button>`;
    }).join('');
    const eff = trackEff(S, tk);
    return `<div class="tcol">
      <div class="thead">${TRACKS[tk].name} <b>${eff}</b></div>
      <div class="tnodes">${nodes}</div>
    </div>`;
  }).join('');

  const sel = techPick ? (() => {
    const n = STAGE.TECH_TREE.find(x => x.tk === techPick.tk && x.step === techPick.step);
    if (!n) return '';
    const un = STAGE.techUnlocked(S, n);
    return `<div class="tinfo">
      <b>${TRACKS[n.tk].name} · 第 ${n.step + 1} 阶 —— ${n.name}</b>
      <p>${un ? '已解锁。' : `未解锁：需要${TRACKS[n.tk].name}达到 ${(n.step + 1) * 9}，当前 ${trackEff(S, n.tk)}。`}
      ${n.step >= 9 ? '第十阶是这条轨的终点。' : `再往后是第 ${n.step + 2} 阶。`}</p>
      <p class="dim">节点名由"该轨性质 + 时代推进"生成，是展示用推演，不是《03》《04》原文条目。
      真正参与结算的只有 6 轨数值与 10 个科技门。</p>
    </div>`;
  })() : `<div class="tinfo dim">点任意节点看它在第几阶、还要多少才能解锁。</div>`;

  const gates = TECHS.map(t => {
    const on = S.flags.includes(t);
    return `<span class="tchip ${on ? 'got' : ''}">${on ? '✓ ' : ''}${MARKERS[t] ? MARKERS[t].name : t}</span>`;
  }).join('');

  box.insertAdjacentHTML('beforeend', `
  <div class="modal wide" onclick="closeModal(event)">
    <div class="panel" onclick="event.stopPropagation()">
      <h2>科技树</h2>
      <p class="hint">六轨六十阶，横向是时代推进。灰色是没走到的，亮着的是你换来的。</p>
      <div class="tgrid">${grid}</div>
      ${sel}
      <div class="sec"><h3>科技门（结局判定只看这十个）</h3><div class="chips">${gates}</div></div>
      <button class="btn" onclick="closeModal()">关闭</button>
    </div>
  </div>`);
}
function pickTech(tk, step) {
  techPick = (techPick && techPick.tk === tk && techPick.step === step) ? null : { tk, step };
  closeModal(); showTech();
}

/* --- 回望（抉择日志）----------------------------------------------------*/
function showLog() {
  const box = document.getElementById('app');
  const items = S.log.map(l => `
    <div class="lrow">
      <div class="lhead"><b>第${cn(l.era)}时代</b> · ${l.title}</div>
      <div class="lpick">${l.key}. ${l.label}</div>
      <div class="ldesc">${l.desc}</div>
      ${l.flags.length ? `<div class="lflags">${l.flags.map(f => `<span class="fchip">${MARKERS[f].name}</span>`).join('')}</div>` : ''}
    </div>`).join('') || '<p class="dim">还没有做出任何决定。</p>';
  box.insertAdjacentHTML('beforeend', `
  <div class="modal" onclick="closeModal(event)">
    <div class="panel" onclick="event.stopPropagation()">
      <h2>回望</h2>
      <p class="hint">结局不是奖励，是对这 ${S.log.length} 个选择的审判。</p>
      ${items}
      <button class="btn" onclick="closeModal()">关闭</button>
    </div>
  </div>`);
}

/* --- 档案室：标记日记（《07/05》"我是谁"）------------------------------*/
function showArchive() {
  const box = document.getElementById('app');
  const lines = STAGE.selfPortrait(S);
  const ax = Object.keys(AXES).filter(a => S.flags.includes(a))
    .map(a => `<span class="fchip">${AXES[a].name}</span>`).join('') || '<span class="dim">尚无</span>';
  const axd = Object.keys(AXES).filter(a => S.flags.includes(a))
    .map(a => `<div class="lrow"><div class="lpick">${AXES[a].name}</div><div class="ldesc">${AXES[a].desc}</div></div>`).join('');
  const mk = Object.keys(MARKERS).filter(m => S.flags.includes(m) && !MARKERS[m].tech)
    .map(m => `<div class="lrow"><div class="lpick">${MARKERS[m].name}</div><div class="ldesc">第${cn(MARKERS[m].era)}时代</div></div>`).join('')
    || '<p class="dim">还没有留下门闩。</p>';

  box.insertAdjacentHTML('beforeend', `
  <div class="modal" onclick="closeModal(event)">
    <div class="panel" onclick="event.stopPropagation()">
      <h2>档案室</h2>
      <div class="hidden-line" style="margin:14px 0 26px">
        <div class="hl-label">我是谁</div>
        ${lines.map(l => `<div class="hl-text" style="font-size:15px;margin-bottom:10px">${l}</div>`).join('')}
      </div>
      <div class="sec"><h3>你走过的轴</h3><div class="chips">${ax}</div>${axd}</div>
      <div class="sec"><h3>门闩清单</h3>${mk}</div>
      <button class="btn" onclick="closeModal()">关闭</button>
    </div>
  </div>`);
}

/* ============================ 结局 =======================================*/
function viewResult() {
  const e = S.ending || (S.ending = resolveEnding(S));
  if (!S.museumDone) { recordMuseum(S, e); S.museumDone = true; }

  const d = e.dims;
  /* 四维里的「人道代价」是负向指标：值越高越糟。
     若沿用 TIERS 的正向配色，高代价会显示成蓝色"最高档"色，读起来像好消息。
     故为它单独一套语义色与文案——低代价=安稳，高代价=警告。
     档位数量仍为五档（《12》铁律 5：玩家只见五档，不见数值）。 */
  const COST_TIERS = [
    { max:20, label:'几乎无代价', color:'#2a6a8a', line:'你走通了。这在历史上罕见。' },
    { max:40, label:'代价可控',   color:'#3f7a3a', line:'有人承担了代价，但人数有限。' },
    { max:60, label:'代价沉重',   color:'#8a7a2a', line:'账记在了没有名字的人头上。' },
    { max:80, label:'代价惨重',   color:'#a85a1a', line:'数字已经不重要了，重要的是名字。' },
    { max:101,label:'代价不可逆', color:'#8b1a1a', line:'没有碑铭。只有空地。' },
  ];
  const costTier = v => COST_TIERS.find(t => v < t.max) || COST_TIERS[4];

  const dim = (label, v, isCost) => {
    const t = isCost ? costTier(v) : tier(v);
    const cls = t.label === '最高档' ? 4 : t.label === '高档' || t.label === '代价惨重'
              ? 3 : t.label === '中档' || t.label === '代价沉重' ? 2 : 1;
    return `<div class="dcard">
      <div class="dlabel">${label}</div>
      <div class="dbar"><i style="width:${v}%" class="t${cls}"></i></div>
      <div class="dtier" style="color:${t.color}">${t.label}</div>
      ${t.line ? `<div class="dline">${t.line}</div>` : ''}
    </div>`;
  };

  const syntax = SYNTAX_MAP[e.name] || null;

  return `
  <div class="result">
    <div class="res-head">
      <div class="res-era">第 ${cn(10)} 时代 · 结算</div>
      <h1>${e.name}</h1>
      ${e.isFallback ? '<div class="fb">未命中十二结局中的任何一条——这本身也是一种结局。</div>' : ''}
      ${e.combo ? `<div class="combo">复合结局 · <b>${e.combo.name}</b></div>` : ''}
      <div class="tone">${e.tone}</div>
    </div>

    <div class="res-mapwrap">
      <canvas id="cv-endmap" class="endmap"></canvas>
      <p class="hint">这是你走了三十个抉择之后的世界。红线还是紫带，全在上面。</p>
    </div>

    <div class="res-text">${e.text}</div>

    ${e.combo ? `<div class="res-text combo-text">${e.combo.text}</div>` : ''}

    <div class="hidden-line">
      <div class="hl-label">隐藏句</div>
      <div class="hl-text">${e.hidden}</div>
    </div>
    ${e.combo ? `<div class="hidden-line"><div class="hl-label">复合隐藏句</div><div class="hl-text">${e.combo.hidden}</div></div>` : ''}

    <div class="dims">
      <h2>四维评价</h2>
      <p class="hint">没有结局能在四个维度上都得高分。这就是设计的核心张力：你必须接受不完美。</p>
      ${dim('统一完成度', d.unity)}
      ${dim('人道代价', 100 - d.human, true)}
      ${dim('可持续性', d.sustain)}
      ${dim('文明丰富度', d.rich)}
    </div>

    <div class="res-echo">
      <h2>回望：是什么决定了这一刻</h2>
      ${echoList()}
    </div>

    <div class="res-acts">
      ${e.unlocksSecond || syntax ? `<button class="btn primary" onclick="startSecond()">第二段 · 星际纪元 →</button>` : ''}
      <button class="btn" onclick="goto('museum')">结局博物馆</button>
      <button class="btn" onclick="startNew()">再玩一次</button>
    </div>
    <p class="hint center">接触语法：<b>${syntax ? SYNTAX[syntax].name + ' —— ' + SYNTAX[syntax].desc : '本结局未开放第二段'}</b></p>
  </div>`;
}

/* 结局页的世界地图：这一局走完的地图，和你玩的时候最后看到的是同一张 */
function paintEndMap() {
  if (!canDraw()) return;
  const cv = document.getElementById('cv-endmap');
  if (!cv) return;
  /* 显式给尺寸。afterRender 跑在内联样式完成布局之前，
     此刻 clientWidth 可能是 0，drawMap 会退回默认 900×500，
     画出来的比例和 CSS 里的 aspect-ratio 对不上。 */
  const w = cv.clientWidth || 960;
  const h = Math.round(w / 1.85);
  MOSAIC.drawMap(cv, 9, S, { reveal: 1, w, h });
}

/* 回望：把 30 个选择里真正影响结局的挑出来 */
function echoList() {
  const key = S.log.filter(l => l.applied.flags.length || Math.abs(l.applied.attr.reduce((a, x) => a + Math.abs(x.d), 0)) >= 8);
  const items = key.slice(-8).reverse().map(l => `
    <div class="echo">
      <div class="etop"><b>第${cn(l.era)}时代</b> · ${l.title}</div>
      <div class="epick">${l.key}. ${l.label}</div>
      <div class="ewhy">
        ${l.applied.flags.filter(f => MARKERS[f] && !MARKERS[f].tech).map(f => `<span class="fchip">${MARKERS[f].name}</span>`).join('')}
        ${l.applied.attr.filter(a => Math.abs(a.d) >= 6).map(a => `<span class="achip ${a.d > 0 ? 'up' : 'down'}">${a.name} ${a.d > 0 ? '+' : ''}${a.d}</span>`).join('')}
      </div>
    </div>`).join('');
  return items || '<p class="dim">这一局的选择都很轻。</p>';
}

/* ============================ 结局博物馆 =================================*/
function viewMuseum() {
  const m = loadMuseum();
  const keys = Object.keys(m);
  const cards = keys.map(k => {
    const r = m[k];
    return `<div class="mcard">
      <div class="mname">${r.name}${r.combo ? `<span class="mcombo">${r.combo}</span>` : ''}</div>
      <div class="mdim">
        <span>统一 ${r.unity}</span><span>人性 ${r.human}</span>
        <span>持续 ${r.sustain}</span><span>丰富 ${r.rich}</span>
      </div>
      <div class="mdate">${r.at}</div>
    </div>`;
  }).join('') || '<p class="dim">还没有任何结局被解锁。玩一次就有了。</p>';

  const total = ENDINGS.length + COMBOS.length;
  return `
  <div class="museum">
    <h1>结局博物馆</h1>
    <p class="hint">已解锁 <b>${keys.length}</b> / ${total} · 十二结局 + 四复合结局</p>
    <div class="mgrid">${cards}</div>
    <div class="res-acts">
      <button class="btn" onclick="startNew()">开始新游戏</button>
      <button class="btn" onclick="goto('title')">返回标题</button>
    </div>
  </div>`;
}

/* ============================ 第二段：接触纪元 ===========================*/
function startSecond() {
  const syntax = SYNTAX_MAP[S.ending.name] || '混合型';
  secondState = { syntax, beat: 0, flags: {}, done: false, ending: null };
  S.second = secondState;
  saveGame(S);
  goto('second');
}

function viewSecond() {
  const st = secondState;
  if (!st) { screen = 'result'; return viewResult(); }   // 渲染期不调 goto（见 viewPlay 处说明）
  const C = CONTACT;

  if (st.done && st.ending) return viewSecondEnd();
  if (st.beat === 99) return viewSecondEnd();   // 事件链哨兵：三拍走完
  if (st.beat > CONTACT.beats.length) return viewSecondEnd();  // 防御

  const head = `
  <div class="hud">
    <div class="hud-era">
      <span class="en">${C.num}</span>
      <strong>${C.name}</strong>
    </div>
    <div class="hud-prog">
      ${CONTACT.beats.map((_, i) => `<span class="dot ${i < st.beat ? 'done' : i === st.beat ? 'now' : ''}"></span>`).join('')}
    </div>
    <div class="hud-btns">
      <span class="mini static">接触语法：${SYNTAX[st.syntax].name}</span>
      <button class="mini" onclick="showSyntax()">?</button>
      <button class="mini" onclick="showStats()">状况</button>
      <button class="mini" onclick="confirmQuit2()">退出</button>
    </div>
  </div>`;

  if (st.beat === 0) {
    return `<div class="play">
      ${head}
      <div class="stagewrap">
        <div class="stage">
          <div class="stage-tabs"><span class="stagelab">接触纪元 · 首次接触</span></div>
          <div class="canvasbox"><canvas id="cv-scene2" class="maincv"></canvas>
            <div class="scenecap">${C.open.title}</div></div>
        </div>
        <div class="side">
          <div class="syntaxbar">${SYNTAX[st.syntax].desc}</div>
          <div class="tint">${C.tint}</div>
          <div class="card cur">
            <div class="ctitle">${C.open.title}</div>
            <div class="cbody">
              <div class="prose">${C.open.text.replace(/\n\n/g, '</p><p>')}</div>
              <div class="opts"><button class="opt" onclick="nextBeat()"><span class="k">▸</span><span class="body"><span class="label">继续</span></span></button></div>
            </div>
          </div>
        </div>
      </div>
    </div>`;
  }

  const b = C.beats[st.beat - 1];
  return `<div class="play">
    ${head}
    <div class="stagewrap">
      <div class="stage">
        <div class="stage-tabs"><span class="stagelab">第 ${st.beat} 拍 / 共 ${C.beats.length} 拍</span></div>
        <div class="canvasbox"><canvas id="cv-scene2" class="maincv"></canvas>
          <div class="scenecap">${b.title}</div></div>
      </div>
      <div class="side">
        <div class="syntaxbar">${SYNTAX[st.syntax].desc}</div>
        <div class="card cur">
          <div class="ctitle">${b.title}</div>
          <div class="cbody">
            <div class="prose">${b.text}</div>
            <p class="q">${b.q}</p>
            <div class="opts">
              ${b.opts.map(o => `<button class="opt" onclick="pickBeat('${o.k}')">
                <span class="k">${o.k}</span>
                <span class="body"><span class="label">${o.label}</span><span class="desc">${o.text}</span></span>
              </button>`).join('')}
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>`;
}

function mountStage2() {
  if (!canDraw()) return;
  const cv = document.getElementById('cv-scene2');
  if (cv) STAGE.paintScene(cv, 9, S, 500 + secondState.beat * 13);
}

function nextBeat() { secondState.beat += 1; S.second = secondState; saveGame(S); render(); }

function pickBeat(k) {
  const b = CONTACT.beats[secondState.beat - 1];
  if (!b) return;                                   // 防御：beat 越界直接忽略
  const o = b.opts.find(x => x.k === k);
  if (!o) return;
  secondState.last = { beat: b.title, label: o.label, text: o.text };
  if (o.eff) for (const key in o.eff) secondState.flags[key] = o.eff[key];
  if (o.eff && o.eff['船上政治'] === '返航优先') secondState.flags['返航优先'] = true;

  if (secondState.beat >= CONTACT.beats.length) { secondState.beat = 99; S.second = secondState; saveGame(S); return render(); }
  secondState.beat += 1; S.second = secondState; saveGame(S); render();
}

function confirmQuit2() { if (confirm('退出第二段？进度会保留。')) { saveGame(S); goto('title'); } }

function showSyntax() {
  const box = document.getElementById('app');
  const rows = Object.keys(SYNTAX).map(k => {
    const cur = secondState.syntax === k;
    return `<div class="srow ${cur ? 'cur' : ''}"><b>${SYNTAX[k].name}</b><span>${SYNTAX[k].desc}</span></div>`;
  }).join('');
  box.insertAdjacentHTML('beforeend', `
  <div class="modal" onclick="closeModal(event)">
    <div class="panel" onclick="event.stopPropagation()">
      <h2>接触语法</h2>
      <p class="hint">它不是数值，是选项可用性的门闩。由你的第一段结局唯一决定，不可随机。</p>
      ${rows}
      <button class="btn" onclick="closeModal()">关闭</button>
    </div>
  </div>`);
}

/* 事件链 + 结算 + 第二段结局 */
function viewSecondEnd() {
  const C = CONTACT;
  const st = secondState;
  if (!st) { screen = 'result'; return viewResult(); }   // 渲染期不调 goto

  if (st.beat === 99 && !st.ending) {
    return `<div class="play">
      <div class="hud"><div class="hud-era"><span class="en">${C.num}</span><strong>${C.name}</strong></div>
      <div class="hud-btns"><span class="mini static">专属事件链</span></div></div>
      <div class="stagewrap">
        <div class="stage">
          <div class="stage-tabs"><span class="stagelab">不可跳过 · 不可加速</span></div>
          <div class="canvasbox"><canvas id="cv-scene2" class="maincv"></canvas>
            <div class="scenecap">${C.chain.title}</div></div>
        </div>
        <div class="side">
          <div class="card cur">
            <div class="ctitle">${C.chain.title}</div>
            <div class="cbody">
              <div class="prose">${C.chain.text.replace(/\n\n/g, '</p><p>')}</div>
              <div class="opts"><button class="opt" onclick="finishSecond()"><span class="k">▸</span><span class="body"><span class="label">继续</span></span></button></div>
            </div>
          </div>
        </div>
      </div>
    </div>`;
  }

  if (!st.ending) return `<div class="play"><div class="choice">
    <h2>${C.name}</h2>
    <div class="opts"><button class="opt" onclick="nextBeat()"><span class="k">▸</span><span class="body"><span class="label">继续</span></span></button></div>
  </div></div>`;

  const e = st.ending;
  const syntax = SYNTAX[st.syntax];
  return `
  <div class="result">
    <div class="res-head">
      <div class="res-era">${C.num} · 结算</div>
      <h1>${e.name}</h1>
      <div class="combo">强度：${e.strength} · 接触语法：${syntax.name}</div>
      <div class="tone">你带着${cn2(st)}出发，最后变成了这个。</div>
    </div>
    <div class="res-mapwrap">
      <canvas id="cv-endmap" class="endmap"></canvas>
      <p class="hint">船离开之后，地球还剩你走出来的那个样子。</p>
    </div>
    <div class="res-text">${e.text}</div>
    <div class="hidden-line"><div class="hl-label">隐藏句</div><div class="hl-text">${e.hidden}</div></div>

    <div class="res-echo">
      <h2>你带去的是</h2>
      ${Object.keys(st.flags).map(k => `<div class="echo"><div class="epick">${k}：${fmt(st.flags[k])}</div></div>`).join('')}
    </div>

    <div class="res-acts">
      <button class="btn primary" onclick="startNew()">从部落重新开始</button>
      <button class="btn" onclick="goto('museum')">结局博物馆</button>
      <button class="btn" onclick="goto('title')">返回标题</button>
    </div>
    <p class="hint center">第二段尚未实现门槛／相遇／边界三个纪元——本切片只做到接触纪元。</p>
  </div>`;
}

function finishSecond() {
  const st = secondState;
  const ctx = { get: k => st.flags[k], has: k => !!st.flags[k] };
  const e = CONTACT.ends.find(x => { try { return x.when(ctx); } catch (err) { return false; } }) || CONTACT.ends[4];
  st.ending = e; st.done = true; st.beat = 100;
  S.second = st; saveGame(S);
  render();
}

function fmt(v) { return typeof v === 'boolean' ? (v ? '是' : '否') : v; }

/* 中文数字（《06》全篇用"第一时代…第十时代"的写法） */
const CN_NUM = ['零','一','二','三','四','五','六','七','八','九','十',
                 '十一','十二','十三','十四','十五','十六','十七','十八','十九','二十'];
function cn(n) { return CN_NUM[n] != null ? CN_NUM[n] : String(n); }

function cn2(st) {
  const p = [];
  if (st.flags['带入的人']) p.push(st.flags['带入的人']);
  if (st.flags['介绍词']) p.push(st.flags['介绍词']);
  if (st.flags.船上政治) p.push(st.flags.船上政治 + '的船上');
  return p.length ? p.join('、') + '的地球' : '一个说不清的地球';
}

/* --- 测试钩子：仅供 uitest.js 驱动内部状态，游戏中不调用 ----------------*/
if (typeof module !== 'undefined' && module.exports) {
  globalThis.__setSecond = st => { secondState = st; if (S) S.second = st; };
  globalThis.__getState  = () => ({ S, secondState, screen });
  globalThis.__setEnding = e => { S.ending = e; };
  globalThis.__setMode   = m => { viewMode = m; };
}
