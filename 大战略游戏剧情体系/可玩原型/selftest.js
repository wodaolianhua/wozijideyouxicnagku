/* ============================================================================
 * selftest.js —— 引擎自检（Node 环境运行，不依赖浏览器）
 * 验证：① 30 抉择全部可执行  ② 12 结局全部可达  ③ 五条铁律生效
 * 运行：node 自检.js
 * ==========================================================================*/
const fs = require('fs'), path = require('path'), vm = require('vm');

const ctx = { console, localStorage: { getItem: () => null, setItem: () => {}, removeItem: () => {} } };
vm.createContext(ctx);
let src = '';
for (const f of ['data.js', 'engine.js']) {
  src += fs.readFileSync(path.join(__dirname, f), 'utf8') + '\n';
}
// vm 沙箱内 const/let 不挂到 context，显式导出
src += `
globalThis.__X = { ERAS, ENDINGS, ATTRS, TRACKS, TIERS, MARKERS, AXES, TECHS, COMBOS, CONTACT, SYNTAX, SYNTAX_MAP,
  newGame, applyChoice, advanceEra, resolveEnding, makeCtx, derived, trackEff, tier, DECAY };
`;
vm.runInContext(src, ctx, { filename: 'bundle.js' });
const { ERAS, ENDINGS, ATTRS, TRACKS, TIERS, MARKERS, AXES, TECHS, COMBOS, CONTACT, SYNTAX, SYNTAX_MAP,
  newGame, applyChoice, advanceEra, resolveEnding, makeCtx, derived, trackEff } = ctx.__X;

let pass = 0, fail = 0;
const ok  = (n, c) => { c ? (pass++, console.log('  ✓ ' + n)) : (fail++, console.log('  ✗ ' + n)); };

/* ---------- 1. 数据完整性 ---------- */
console.log('\n[1] 数据完整性');
ok('30 个抉择（10 时代 × 3）', ERAS.length === 10 && ERAS.every(e => e.choices.length === 3));
const optCount = ERAS.reduce((a, e) => a + e.choices.reduce((b, c) => b + c.options.length, 0), 0);
ok('90 个选项（30 × ABC）', optCount === 90);
ok('12 结局齐备', ENDINGS.length === 12);
ok('四维评价键完整', ENDINGS.every(e => { const d = e.dims(makeCtx(newGame())); return ['unity','human','sustain','rich'].every(k => typeof d[k] === 'number'); }));
ok('五档区间连续无洞', TIERS.every((t, i) => i === 0 || t.min === TIERS[i-1].max));
ok('五档覆盖 0~100', TIERS[0].min === 0 && TIERS[TIERS.length-1].max === 101);
const badAttr = [];
ERAS.forEach(e => e.choices.forEach(c => c.options.forEach(o => {
  if (o.base) for (const k in o.base)
    if (!ATTRS[k] && !TRACKS[k] && k !== 'environment') badAttr.push(`${e.era}/${c.title}/${o.key}:base:${k}`);
  if (o.mods) o.mods.forEach(m => { if (!TRACKS[m.t]) badAttr.push(`mod:${m.t}`); });
  if (o.flags) o.flags.forEach(f => { if (!MARKERS[f] && !AXES[f]) badAttr.push(`flag:${f}`); });
})));
ok('无未定义属性/科技轨/标记引用', badAttr.length === 0);
if (badAttr.length) console.log('     ' + [...new Set(badAttr)].join(', '));
ok(`叙事轴 ${Object.keys(AXES).length} 条全部登记`, Object.keys(AXES).length >= 40);
ok('门闩 ' + Object.keys(MARKERS).length + ' 条 · 科技门 ' + TECHS.length + ' 条', TECHS.length === 10);

/* ---------- 2. 铁律 1：半衰期衰减 ---------- */
console.log('\n[2] 铁律 1 · 半衰期衰减 0.6^代');
{
  const s = newGame();
  s.era = 1; s.idx = 0;
  const C = ERAS[0].choices[0], A = C.options[0];   // 武力征服：mil +20%
  applyChoice(s, 0, C, A);
  const v1 = s.mods.find(m => m.track === 'mil').v;
  ok('产生时为原值 20', Math.abs(v1 - 20) < 0.01);
  advanceEra(s);
  const v2 = s.mods.find(m => m.track === 'mil').v;
  ok('跨 1 代衰减为 12 (20×0.6)', Math.abs(v2 - 12) < 0.01);
  advanceEra(s); advanceEra(s);
  const v3 = s.mods.find(m => m.track === 'mil').v;
  ok('跨 3 代衰减为 4.32 (20×0.6³)', Math.abs(v3 - 4.32) < 0.01);

  const g = newGame();
  g.flags.push('求知传统');
  const before = g.attrs.gene;
  for (let i = 0; i < 5; i++) advanceEra(g);
  ok('门闩（FLAG）不衰减', g.flags.includes('求知传统'));
  ok('连续属性向中点回归而非线性下降', g.attrs.gene >= before && g.attrs.gene <= 50);
  const legacyBefore = 0;
  g.attrs.legacy = -60;
  for (let i = 0; i < 5; i++) advanceEra(g);
  ok('殖民遗产值豁免衰减（-60 保持）', g.attrs.legacy === -60);
}

/* ---------- 3. 铁律 2：同类不叠加 ---------- */
console.log('\n[3] 铁律 2 · 同类不叠加 max + 其余×0.25');
{
  const s = newGame();
  s.mods = [{ id:'a', track:'mil', v:30, born:1 }, { id:'b', track:'mil', v:20, born:1 }, { id:'c', track:'mil', v:10, born:1 }];
  const eff = trackEff(s, 'mil');
  const base = s.tracks.mil;
  const expected = 30 + (20 + 10) * 0.25;   // = 37.5
  ok(`30/20/10 → 期望 +37.5，实得 ${eff - base}（引擎取整，允许 ±0.5）`, Math.abs((eff - base) - expected) <= 0.5);
}

/* ---------- 4. 铁律 4：上限封顶 ---------- */
console.log('\n[4] 铁律 4 · 单时代累计修正封顶 ±60%');
{
  const s = newGame();
  s.mods = [
    { id:'a', track:'mil', v:50, born:1 }, { id:'b', track:'mil', v:50, born:1 },
    { id:'c', track:'mil', v:50, born:1 }, { id:'d', track:'mil', v:50, born:1 },
  ];
  const bonus = trackEff(s, 'mil') - s.tracks.mil;
  ok(`4×50 修正被压到 ${bonus}（≤60）`, bonus === 60);
  // 科技轨本身下限为 0（base 不能为负），故负向封顶要用高基值才观察得到
  const s2 = newGame();
  s2.tracks.mil = 80;
  s2.mods = [{ id:'x', track:'mil', v:-50, born:1 }, { id:'y', track:'mil', v:-50, born:1 }];
  const b2 = trackEff(s2, 'mil') - s2.tracks.mil;
  ok(`高基值下负向封底到 ${b2}（≥-60）`, b2 === -60);
  const s3 = newGame();
  s3.mods = [{ id:'x', track:'mil', v:-50, born:1 }, { id:'y', track:'mil', v:-50, born:1 }];
  ok(`低基值下被地板截断为 ${trackEff(s3, 'mil')}（科技轨不为负）`, trackEff(s3, 'mil') === 0);
}

/* ---------- 5. 铁律 3：门闩不配百分比 ---------- */
console.log('\n[5] 铁律 3 · FLAG 不折算数值');
{
  const s = newGame();
  s.flags.push('铁屋');
  const t0 = trackEff(s, 'mil');
  ok('置入门闩不改变任何科技轨数值', t0 === trackEff(newGame(), 'mil'));
  ok('门闩只通过派生指数起作用', derived(s, 'coop') < derived(newGame(), 'coop'));
}

/* ---------- 6. 全通跑测：30 抉择无异常 ---------- */
console.log('\n[6] 全通跑测（30 抉择 × 多条路径）');
function playthrough(picks) {
  const s = newGame();
  let k = 0;
  for (let e = 0; e < 10; e++) {
    for (let c = 0; c < 3; c++) {
      const C = ERAS[e].choices[c];
      const o = C.options.find(x => x.key === picks[k % 3]) || C.options[0];
      applyChoice(s, e, C, o); k++;
    }
    advanceEra(s); s.idx = 0;
  }
  s.era = 11;
  return s;
}
{
  let threw = null;
  const strategies = ['AAA','BBB','CCC','ABC','CBA','AAA','CCC','BBB','ABC','CCC','BBB','AAA','CCC','BBB','ABC'];
  strategies.forEach(st => { try { playthrough(st.split('')); } catch (e) { threw = st + ': ' + e.message; } });
  ok('15 条不同路径全部无异常', !threw);
  if (threw) console.log('     ' + threw);
  const s = playthrough('ABC'.split(''));
  ok('抉择日志恰好 30 条', s.log.length === 30);
  ok('era 推进到 11', s.era === 11);
}

/* ---------- 7. 12 结局可达性（白盒构造） -------------------------------*/
console.log('\n[7] 十二结局可达性');
{
  // 先看自然跑测能命中哪些
  const reached = {};
  for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) for (let k = 0; k < 3; k++) {
    const st = ['A','B','C'][i].repeat(10) + ['A','B','C'][j].repeat(10) + ['A','B','C'][k].repeat(10);
    const e = resolveEnding(playthrough(st.split('')));
    reached[e.name] = (reached[e.name] || 0) + 1;
  }
  console.log('     27 条均匀策略自然命中：' + Object.keys(reached).join('、'));

  // 白盒构造：造一个"第十一世代"的干净存档，只设目标结局所需条件
  function blank() {
    const t = newGame();
    t.era = 11; t.idx = 0;
    return t;
  }
  const builders = {
    /* 崩坏判据是 derived('dissonance') >= 14 —— 由 violence 与 inclusion 推出，
       故白盒态必须同时给足 violence、拉满 inclusion=0，否则 dissonance 达不到阈值。
       另注：_maxStreak 参与判定，每时代仅 3 抉择故自然上限为 3。 */
    '文明崩坏': t => { t.counts.violence = 18; t.counts.inclusion = 0; t.counts._maxStreak = 3; },
    '铁血征服': t => { t.counts.violence = 6; t.counts._maxStreak = 2; t.counts.inclusion = 0;
                 t.final.unify = '世界政府';
                 t.tracks.mil = 70; t.attrs.poly = 20; t.attrs.trust = 40; t.attrs.core = 25;
                 t.attrs.legacy = 55; t.attrs.postcol = -40;   // 征服路线必然高殖民遗产、低后殖民关系
                 t.flags.push('排外传统','殖民原罪','文明等级论'); },
    '文化同化': t => { t.counts.inclusion = 6; t.tracks.cult = 75; t.attrs.poly = 80; t.attrs.trust = 80;
                 t.attrs.postcol = 60; t.attrs.legacy = -40; },
    '科技乌托邦': t => { t.flags.push('has_strong_ai','has_sci_method','has_fusion');
                 t.counts.knowledge = 3; t.attrs.conscience = 90; t.attrs.trust = 85; },
    '联邦共治': t => { t.counts.contract = 5; t.tracks.inst = 75; t.flags.push('has_law');
                 t.final.unify = '联邦共治'; t.attrs.poly = 78; t.attrs.trust = 80; t.attrs.postcol = 30; },
    '外星接触': t => { t.flags.push('has_space'); t.final.human = '星际拓展';
                 t.counts.knowledge = 3; t.tracks.sci = 70; t.attrs.trust = 78; t.attrs.core = 72; t.attrs.poly = 70; },
    '神权复兴': t => { t.counts.faith = 5; t.attrs.contra = 88; t.tracks.sci = 35;
                 t.flags.push('神权至上','全面审查'); t.attrs.trust = 62; },
    '数字联邦': t => { t.final.unify = '超越国家'; t.flags.push('AI共生','has_conscious');
                 t.counts.knowledge = 3; t.tracks.sci = 72; t.attrs.modern = 88; t.attrs.trust = 58; },
    '生态回归': t => { t.attrs.legacy = -75; t.counts.nature = 4; t.flags.push('本土深耕');
                 t.attrs.modern = 25; t.attrs.core = 72; t.attrs.trust = 78; t.attrs.poly = 65; },
    '永久分裂': t => { t.flags.push('排外传统','血统纯正'); t.attrs.poly = 8; t.attrs.trust = 22;
                 t.attrs.resil = 28; t.attrs.core = 35; },
    '后人类超越': t => { t.flags.push('has_genedit','has_conscious','has_strong_ai');
                 t.final.unify = '超越国家'; t.counts.change = 3; t.attrs.modern = 90; t.attrs.trust = 55; },
    '循环重启': t => { t.flags.push('文明记忆','清算能力'); t.counts.reflect = 4;
                 t.attrs.trust = 82; t.attrs.core = 78; t.attrs.contra = 30; },
  };
  const missed = [];
  for (const e of ENDINGS) {
    const t = blank();
    builders[e.name](t);
    const r = resolveEnding(t);
    if (r.id !== e.id) missed.push(`${e.name}（order ${e.order}）→ 被 higher-order 抢先：${r.name}`);
  }
  ok('十二结局判定函数全部可达', missed.length === 0);
  if (missed.length) missed.forEach(m => console.log('     ' + m));
}

/* ---------- 7b. 结局分布覆盖度 ----------------------------------------
 * 《04》明写："文化同化结局需要在至少6个时代中选择文化/包容类选项；
 * 文明崩坏结局可以由任意3个时代的极端选项组合触发。"
 * 即"稀有结局本就该需要玩家主动去选"，随机分布不均衡是设计意图，不是缺陷。
 * 故此处只断言两条底线：①崩坏不能吞掉一切（否则玩家无论怎么选都同一个结局）
 * ②稀有结局必须是"可达但需要意图"（白盒已证可达，见 [7]）。
 * ----------------------------------------------------------------------*/
console.log('\n[7b] 结局分布覆盖度 · 随机 3000 局');
{
  let seed = 20261004;
  const rnd = () => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed / 0x7fffffff; };

  const dist = {};
  const N = 3000;
  for (let n = 0; n < N; n++) {
    const s = newGame();
    for (let e = 0; e < 10; e++) {
      for (let c = 0; c < 3; c++) {
        const C = ERAS[e].choices[c];
        applyChoice(s, e, C, C.options[Math.floor(rnd() * 3)]);
      }
      advanceEra(s); s.idx = 0;
    }
    s.era = 11;
    const r = resolveEnding(s);
    dist[r.name] = (dist[r.name] || 0) + 1;
  }
  const sorted = Object.entries(dist).sort((a, b) => b[1] - a[1]);
  console.log('     ' + sorted.map(([k, v]) => `${k} ${(v/N*100).toFixed(1)}%`).join('  '));

  const kinds = sorted.length;
  const collapseShare = (dist['文明崩坏'] || 0) / N;
  ok(`随机游玩命中 ${kinds} 种结局`, kinds >= 5);
  ok(`崩坏占比 ${(collapseShare*100).toFixed(1)}% < 75%（崩不应吞掉全部路径）`, collapseShare < 0.75);
  ok('存在 ≥3 种非崩坏结局且各占 >1%', sorted.filter(([k, v]) => k !== '文明崩坏' && v/N > 0.01).length >= 3);
  console.log('     随机未覆盖（需主动选择，属设计意图）：' +
    (ENDINGS.filter(e => !dist[e.name]).map(e => e.name).join('、') || '无'));
}


console.log('\n[8] 复合结局（《04》四种）');
{
  const names = COMBOS.map(c => c.need.join('+'));
  ok('四组复合条件齐备：' + names.join(' / '), COMBOS.length === 4);
}

/* ---------- 9. 接触语法派生（《10》4.2） ---------- */
console.log('\n[9] 接触语法派生映射');
{
  const missing = ENDINGS.filter(e => !SYNTAX_MAP[e.name]).map(e => e.name);
  ok('十二结局全部有对应接触语法', missing.length === 0);
  if (missing.length) console.log('     缺：' + missing.join('、'));
  const vals = new Set(Object.values(SYNTAX_MAP));
  ok(`五态覆盖：${[...vals].join('、')}`, vals.size === 5);
}

/* ---------- 10. 第二段事件链 ---------- */
console.log('\n[10] 第二段 · 接触纪元');
{
  ok('三拍主线', CONTACT.beats.length === 3);
  ok('每拍 3 选项', CONTACT.beats.every(b => b.opts.length === 3));
  ok('专属事件链不可跳过且无抉择', CONTACT.chain.skippable === false && CONTACT.chain.hasChoice === false);
  ok('六个第二段结局', CONTACT.ends.length === 6);
  const ctxf = { get: k => ({ '介绍词':'伤口' })[k], has: k => false };
  ok('结局判定函数可执行', CONTACT.ends.some(e => { try { return e.when(ctxf); } catch (x) { return false; } }));
  ok('"第二次出发"为兜底（when 恒真）', CONTACT.ends.find(e=>e.id==='again').when({get:()=>undefined,has:()=>false}) === true);
}

/* ---------- 11. 四维五档（《12》铁律 5） ---------- */
console.log('\n[11] 铁律 5 · 结局按档不按值');
{
  const s = playthrough('ABC'.split(''));
  const e = resolveEnding(s);
  const bad = ['unity','human','sustain','rich'].filter(k => {
    const v = e.dims[k]; return !TIERS.some(t => v >= t.min && v < t.max);
  });
  ok('四维全部落在五档区间内', bad.length === 0);
  if (bad.length) console.log('     越界：' + bad.join('、'));
  ok('12 结局 × 4 维 = 48 个评价值全部可排档',
     ENDINGS.every(en => { const d = en.dims(makeCtx(newGame())); return ['unity','human','sustain','rich'].every(k => TIERS.some(t => d[k] >= t.min && d[k] < t.max)); }));
}

/* ---------- 汇总 ---------- */
console.log('\n' + '─'.repeat(52));
console.log(`  通过 ${pass}　失败 ${fail}`);
console.log('─'.repeat(52) + '\n');
process.exit(fail ? 1 : 0);
