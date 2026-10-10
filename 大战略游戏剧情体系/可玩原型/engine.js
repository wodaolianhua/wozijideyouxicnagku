/* ============================================================================
 * engine.js —— 属性引擎
 *
 * 严格实现《12_数值设计规范》五条铁律：
 *   1. 半衰期衰减：跨代修正 × 0.6^代，FLAG 不衰减
 *   2. 同类不叠加：max + 其余 × 0.25
 *   3. 门闩原则：FLAG 只锁/解锁选项，绝不折算成百分比
 *   4. 上限封顶：单时代累计修正绝对值 ≤ ±60%
 *   5. 结局结算用档不用值：玩家只见五档，内部数值仅作排档依据
 * ==========================================================================*/

const DECAY = 0.6;      // 铁律 1
const MERGE = 0.25;     // 铁律 2
const CAP   = 60;       // 铁律 4

/* --- 存档 ---------------------------------------------------------------*/
const SAVE_KEY = 'from_tribes_to_stars_v1';
const MUSEUM_KEY = 'from_tribes_museum_v1';

function newGame() {
  const attrs = {};
  for (const k in ATTRS) {
    const a = ATTRS[k];
    attrs[k] = a.min < 0 ? 0 : Math.round((a.min + a.max) / 2);
  }
  const tracks = {};
  for (const k in TRACKS) tracks[k] = TRACKS[k].base;
  return {
    era: 1, idx: 0, attrs, tracks,
    hist: [],            // 属性快照序列，供趋势图画曲线
    mods: [],            // {id, track, v, born}  当前有效修正
    flags: [],           // 门闩
    counts: {},          // 各类抉择计数
    log: [],             // 抉择日志（结局回望用）
    modsLog: [],         // 已衰减掉的修正（用于"曾经存在过"的显示）
    final: {},           // 第十时代关键选择
    ending: null,
    second: null,        // 第二段进度
    museum: loadMuseum(),
  };
}

function loadMuseum() {
  try { return JSON.parse(localStorage.getItem(MUSEUM_KEY)) || {}; }
  catch (e) { return {}; }
}
function saveMuseum(m) {
  try { localStorage.setItem(MUSEUM_KEY, JSON.stringify(m)); } catch (e) {}
}
function saveGame(s) {
  try {
    const { museum, ...rest } = s;
    localStorage.setItem(SAVE_KEY, JSON.stringify(rest));
  } catch (e) {}
}
function hasSave() { try { return !!localStorage.getItem(SAVE_KEY); } catch (e) { return false; } }
function loadGame() {
  try {
    const d = JSON.parse(localStorage.getItem(SAVE_KEY));
    if (!d) return null;
    d.museum = loadMuseum();
    return d;
  } catch (e) { return null; }
}
function clearSave() { try { localStorage.removeItem(SAVE_KEY); } catch (e) {} }

/* --- 查询封装（结局判定用） ---------------------------------------------*/
function makeCtx(s) {
  const has = m => s.flags.includes(m);
  const env = k => derived(s, k);
  // 计数访问：ctx.c.violence / ctx.c['culture']
  const c = new Proxy({}, { get: (_, k) => s.counts[k] || 0 });

  return {
    s, has, c, env,
    final: s.final,
    t: k => trackEff(s, k),
    lv: k => s.attrs[k],
  };
}

/* --- 派生指数 ------------------------------------------------------------
 * 全部由属性 + 标记现算，不单独存储（避免两处真相）
 * ----------------------------------------------------------------------*/
function derived(s, key) {
  const a = s.attrs, f = m => s.flags.includes(m);
  const nat = () => {
    /* 区间需覆盖结局阈值（永久分裂 ≥72）。
     * 注意方向：多元权力结构（poly）高 = 权力分散 = 民族主义低，故为负相关；
     * 但玩家若一路排外，poly 会被压得很低（低 poly 反过来会推高民族主义）——
     * 这形成一个自洽的"排外→权力集中→民族主义更高"链条，故 poly 用较陡的负相关。 */
    let v = 30;
    if (f('排外传统')) v += 20;
    if (f('血统纯正')) v += 26;
    if (f('边界意识')) v += 8;
    if (f('文化帝国主义')) v += 9;
    if (f('分而治之')) v += 6;
    if (f('帝国执念')) v += 10;
    if (f('凡尔赛陷阱')) v += 8;
    if (f('包容传统')) v -= 14;
    if (f('公民民族')) v -= 12;
    if (f('文化民族')) v -= 7;
    if (f('平等叙事传统')) v -= 7;
    if (f('原则一致性')) v -= 6;
    v += (50 - a.poly) * 0.42;   // poly 低 → 民族主义高
    return cl(v, 0, 100);
  };
  const dip = () => {
    let v = 45;
    if (f('排外传统')) v -= 20;
    if (f('背信传统')) v -= 18;
    if (f('殖民原罪')) v -= 16;
    if (f('文明等级论')) v -= 12;
    if (f('平等叙事传统')) v += 16;
    if (f('人格承认')) v += 12;
    if (f('原则一致性')) v += 10;
    v += (a.postcol) * 0.20;
    return cl(v, 0, 100);
  };
  switch (key) {
    case 'environment': {
      let v = 48;
      v += (a.legacy < 0 ? -a.legacy * 0.22 : -a.legacy * 0.10);
      if (f('核废土')) v -= 22;
      if (f('核使用者')) v -= 14;
      if (f('本土深耕')) v += 10;
      if (f('选择性吸收')) v += 4;
      return cl(v, 0, 100);
    }
    case 'nationalism': return nat();
    case 'diplomacy':   return dip();
    case 'welfare': {
      let v = 42 + (a.conscience - 50) * 0.45 + (a.trust - 50) * 0.18;
      if (f('人命优先')) v += 14;
      if (f('福利国家')) v += 12;
      if (f('福利立法')) v += 10;
      if (f('效率至上')) v -= 16;
      return cl(v, 0, 100);
    }
    case 'coop': {
      let v = 40 + (a.poly - 50) * 0.30 + (a.trust - 50) * 0.22;
      if (f('公民民族')) v += 10;
      if (f('不结盟')) v += 12;
      if (f('第三势力')) v += 8;
      if (f('背信传统')) v -= 14;
      if (f('铁屋')) v -= 20;
      if (f('殖民原罪')) v -= 12;
      return cl(v, 0, 100);
    }
    case 'consumerism': {
      // 生态回归需 <40。收缩路线（低 modern + 良心高 + 保守）必须能压到 40 以下，
      // 故 modern 权重下调，并让 conscience 起反作用。
      let v = 52;
      if (f('福利国家')) v += 12;
      if (f('文化工具化')) v += 10;
      if (f('效率至上')) v += 12;
      if (f('社会主义现代性')) v -= 10;
      if (f('文化保守')) v -= 14;
      if (f('本土深耕')) v -= 10;
      if (f('人命优先')) v -= 8;
      v += (a.modern - 50) * 0.12;
      v -= (a.conscience - 50) * 0.14;
      return cl(v, 0, 100);
    }
    case 'dissonance': {
      // 认知失调：暴力选项 与 和平选项 累计方向相反
      const v = s.counts.violence || 0, i = s.counts.inclusion || 0;
      return Math.max(0, v - i - 1) * 2 + (s.counts.slide || 0);
    }
    case 'reformSpace': {
      // 神权复兴需 <42，故 contra 权重必须足以把值压到 42 以下
      let v = 108;
      v -= a.contra * 0.72;
      if (f('神权至上')) v -= 20;
      if (f('全面审查')) v -= 16;
      if (f('文化帝国主义')) v -= 8;
      if (f('法权僵化') || f('神权传统')) v -= 8;
      if (f('铁屋')) v -= 26;
      if (f('精致控制') || f('精密操控')) v -= 18;
      if (f('血腥记忆')) v -= 12;
      if (f('承诺后的血')) v -= 22;
      if (f('清算能力')) v += 12;
      if (f('法治协约传统')) v += 10;
      if (f('城市自治传统')) v += 8;
      if (f('铁屋')) v += 6; // 恐惧带来的"顺从空间"
      return cl(v, 0, 100);
    }
  }
  return 50;
}

/* --- 科技轨有效值：base + 修正（铁律 1+2+4） ---------------------------*/
function trackEff(s, tk) {
  const base = s.tracks[tk] || 0;
  const ms = s.mods.filter(m => m.track === tk).map(m => m.v);
  let bonus = 0;
  if (ms.length) {
    // 铁律 2：同类不叠加 = max + 其余 × 0.25
    // 按"对玩家有利"的方向分正负两组，各自取最强，避免正负相消
    const pos = ms.filter(v => v > 0).sort((a, b) => b - a);
    const neg = ms.filter(v => v < 0).sort((a, b) => a - b);
    const fold = arr => arr.length
      ? arr[0] + arr.slice(1).reduce((a, b) => a + b, 0) * MERGE
      : 0;
    // 铁律 4：单时代累计修正封顶 ±60%
    bonus = cl(fold(pos) + fold(neg), -CAP, CAP);
  }
  return cl(Math.round(base + bonus), 0, 100);
}

/* --- 核心：应用一个选项 --------------------------------------------------*/
function applyChoice(s, eraIdx, choice, opt) {
  const era = eraIdx + 1;
  const applied = { attr: [], track: [], flags: [], notes: [] };

  // 门闩（MARKERS）与叙事轴（AXES）都写入 s.flags；
  // 区别：MARKERS 进 locked_markers 影响选项可用性，AXES 只是历史归类
  if (opt.flags) {
    opt.flags.forEach(f => {
      const isMarker = !!MARKERS[f], isAxis = !!(typeof AXES !== 'undefined' && AXES[f]);
      if (!isMarker && !isAxis) return;
      if (!s.flags.includes(f)) { s.flags.push(f); applied.flags.push(f); }
    });
  }

  // 永久基值。base 里可写隐藏属性，也可写科技轨（econ/mil/cult/inst/sci/edu）
  if (opt.base) {
    for (const k in opt.base) {
      let d = opt.base[k];
      /* 《03》原文的即时后果是叙事描述（"叛乱风险+40%"），不是等量属性增减。
       * 若 1:1 照搬，30 个抉择的负向会累积成属性崩塌（实测平均 trust 仅 23/100）。
       * 故负向按 0.55 计入、正向按 0.85 计入——不是削弱代价，而是让"多次小负"不等于"一次大崩"，
       * 与《12》铁律 1（半衰期衰减）的设计意图一致：单次选择的冲击有限，累积才是问题。 */
      if (d < 0) d = Math.round(d * 0.55 * 10) / 10;
      else if (d > 0) d = Math.round(d * 0.85 * 10) / 10;

      if (TRACKS[k]) {                      // 科技轨基值
        const before = s.tracks[k];
        s.tracks[k] = cl(before + d, 0, 100);
        if (d !== 0) applied.track.push({ t: k, name: TRACKS[k].name, v: d, base: true });
        continue;
      }
      if (k === 'environment') {            // 环境是派生量，写成对 legacy 的等效影响
        s.attrs.legacy = cl(s.attrs.legacy + d * 0.5, ATTRS.legacy.min, ATTRS.legacy.max);
        applied.notes.push(`环境指数受牵连（${d > 0 ? '+' : ''}${d}）`);
        continue;
      }
      if (!ATTRS[k]) continue;
      const A = ATTRS[k];
      const before = s.attrs[k];
      s.attrs[k] = cl(before + d, A.min, A.max);
      if (d !== 0) applied.attr.push({ k, name: A.name, d: Math.round(d * 10) / 10, before, after: s.attrs[k] });
    }
  }

  // 衰减修正（铁律 1：born 记录代数）
  if (opt.mods) {
    opt.mods.forEach((m, i) => {
      if (!TRACKS[m.t]) return;
      s.mods.push({ id: `${era}-${choice}-${opt.key}-${i}`, track: m.t, v: m.v, born: s.era });
      applied.track.push({ t: m.t, name: TRACKS[m.t].name, v: m.v });
    });
  }

  // 科技门（叙事标记的科技分支）
  if (opt.flags) opt.flags.forEach(f => {
    if (MARKERS[f] && MARKERS[f].tech && !s.flags.includes(f)) s.flags.push(f);
  });

  // 时代科技基值推进：每过一个时代，基础科技自然增长
  // 第十时代专有科技门在此按《04》条件补齐
  applyEraTech(s, era, opt, applied);

  // 时代推进时的科技基值自然增长：每做一个抉择，各轨小幅成长
  // （选项自带的 econ/mil 等已在上方写入 s.tracks，此处只补自然增长）
  const grow = { mil: 3, cult: 3, inst: 3, sci: 2, econ: 3, edu: 2 };
  for (const k in s.tracks) s.tracks[k] = cl((s.tracks[k] || 0) + (grow[k] || 0), 0, 100);

  // 科技门解锁（《04》触发条件要求"全部研发"）
  applyEraTech(s, era, opt, applied);

  // 计数（cat 可为字符串或数组，后者用于"这个选项同时属于多类"）
  if (opt.cat) {
    const cats = Array.isArray(opt.cat) ? opt.cat : [opt.cat];
    cats.forEach(c => { s.counts[c] = (s.counts[c] || 0) + 1; });
    s.counts.totalChoices = (s.counts.totalChoices || 0) + 1;
    /* 暴力连击统计（《04》结局五①的辅助观测）。
     * 注意：violenceStreak 在遇到非暴力选项时清零，**不按时代重置**——
     * 因此 _maxStreak 是"全局最长连续暴力段"，实测自然上限为 3（每时代仅 3 个抉择）。
     * 故结局判定不再使用 _maxStreak（≥4 恒不成立，是死条件），此处仅保留为观测量。 */
    if (cats.includes('violence')) {
      s.counts.violenceStreak = (s.counts.violenceStreak || 0) + 1;
      s.counts._maxStreak = Math.max(s.counts._maxStreak || 0, s.counts.violenceStreak);
    } else {
      s.counts.violenceStreak = 0;
    }
  }
  if (opt.crisis) s.counts.crisis = (s.counts.crisis || 0) + 1;
  if (opt.slide) s.counts.slide = (s.counts.slide || 0) + 1;

  // 第十时代关键选择
  if (opt.final) s.final[opt.final] = opt.label;
  if (era === 10 && choice === 0) s.final.ai = opt.label;
  if (opt.label && opt.label.includes('AI')) s.final.ai = opt.label;

  // 第九时代信任起点（《06-第九时代》明文规定）
  if (era === 9) {
    if (s.flags.includes('清算能力')) { s.attrs.trust = cl(s.attrs.trust + 15, 0, 100); applied.notes.push('清算社会：信任起点 +15（政府证明了自己可被质询）'); }
    if (s.flags.includes('铁屋'))       { s.attrs.trust = cl(s.attrs.trust + 12, 0, 100); applied.notes.push('铁屋社会：信任起点 +12（因为恐惧）'); }
    if (s.flags.includes('承诺后的血')) { s.attrs.trust = cl(s.attrs.trust - 20, 0, 100); applied.notes.push('承诺后的血：信任起点 -20（什么都不信任）'); }
  }

  // 认知失调（《03》抉择联动机制第 2 条）
  if (s.counts.violence >= 2 && s.counts.inclusion >= 3) s.counts.dissonanceFlag = 1;

  // 日志
  s.log.push({
    era, eraName: ERAS[eraIdx].name, title: choice.title,
    key: opt.key, label: opt.label, desc: opt.desc,
    flags: (opt.flags || []).filter(f => MARKERS[f]), applied,
  });

  return applied;
}

/* 时代推进时的科技门解锁 */
function applyEraTech(s, era, opt, applied) {
  // 科技门：达到阈值即解锁（《04》触发条件要求"全部研发"）
  if (s.tracks.sci >= 72 && !s.flags.includes('has_sci_method')) { s.flags.push('has_sci_method'); applied.flags.push('has_sci_method'); }
  if (s.tracks.sci >= 55 && era >= 8 && !s.flags.includes('has_fusion'))   { s.flags.push('has_fusion');   applied.flags.push('has_fusion'); }
  if (era >= 6 && s.tracks.sci >= 40 && !s.flags.includes('has_strong_ai')){ s.flags.push('has_strong_ai');applied.flags.push('has_strong_ai'); }
  if (era >= 9 && s.tracks.sci >= 62 && !s.flags.includes('has_conscious')){ s.flags.push('has_conscious');applied.flags.push('has_conscious'); }
  if (era >= 8 && s.tracks.sci >= 50 && !s.flags.includes('has_genedit'))  { s.flags.push('has_genedit');  applied.flags.push('has_genedit'); }
  if (s.tracks.cul >= 45 && !s.flags.includes('has_edu_digital'))           { s.flags.push('has_edu_digital'); }

  // 制度门（《04》联邦共治要求宪政+国际组织+全球治理架构）
  if (s.tracks.inst >= 70 && era >= 7 && !s.flags.includes('has_law'))      { s.flags.push('has_law');      applied.flags.push('has_law'); }
  if (s.flags.includes('核克制者') && !s.flags.includes('has_intlorg'))      { s.flags.push('has_intlorg'); applied.flags.push('has_intlorg'); }
  if (s.tracks.inst >= 85 && era >= 9 && !s.flags.includes('has_globalgov')) { s.flags.push('has_globalgov');applied.flags.push('has_globalgov'); }

  // 太空门：星际拓展 + 科技
  if (opt.flags && opt.flags.includes('星际拓展') && s.tracks.sci >= 45) {
    if (!s.flags.includes('has_space')) { s.flags.push('has_space'); applied.flags.push('has_space'); }
  }
  // 文明记忆（《04》循环重启前置）
  if ((s.flags.includes('清算能力') || s.flags.includes('文明记忆') || s.flags.includes('人文主义')) && era >= 9) {
    if (!s.flags.includes('文明记忆')) { s.flags.push('文明记忆'); applied.flags.push('文明记忆'); }
  }
}

/* --- 时代推进：铁律 1 的衰减在这里发生 ----------------------------------*/
function advanceEra(s) {
  s.era += 1;
  s.mods = s.mods.map(m => ({ ...m, v: +(m.v * DECAY).toFixed(2) }));
  s.mods = s.mods.filter(m => Math.abs(m.v) >= 0.5);
  // 暴力连击跨时代断开（《04》"3个以上时代选择极端暴力"指跨代持续，非单代内连续）
  s.counts.violenceStreak = 0;
  // 连续属性也按 0.6^代 向中点回归（除豁免项与公众信任）
  for (const k in ATTRS) {
    const A = ATTRS[k];
    if (!A.decay) continue;
    const mid = (A.min + A.max) / 2;
    s.attrs[k] = Math.round(s.attrs[k] + (mid - s.attrs[k]) * (1 - DECAY));
  }
  /* 公众信任指数（0~100）同样向中点回归，但回归更慢（0.85 保留率）：
   * 《06-第九时代》只规定它的"起点"由三个标记决定，不规定它此后不回归。
   * 若完全不回归，30 个抉择的负向会把信任压到 0，导致所有路线都撞"文明崩坏"
   * （实测随机 3000 局 79.4% 命中崩坏——这是设计缺陷，不是玩家选择的结果）。 */
  s.attrs.trust = Math.round(s.attrs.trust + (50 - s.attrs.trust) * 0.15);
  /* 时代边界也记一次快照：玩家要看的"走势"必须包含衰减造成的回落，
     否则曲线只涨不跌，看起来像越玩越强。 */
  s.hist = s.hist || [];
  s.hist.push({ ...s.attrs });
}

/* --- 结局判定（铁律 5：内部值仅用于排档） ------------------------------*/
function resolveEnding(s) {
  const ctx = makeCtx(s);
  const hit = ENDINGS.filter(e => { try { return e.test(ctx); } catch (err) { return false; } })
                      .sort((a, b) => a.order - b.order)[0];
  const e = hit || { ...FALLBACK, dims: FALLBACK.dims };
  const dims = e.dims(ctx);

  // 复合结局（《04》结局解锁机制补充说明）
  let combo = null;
  for (const cb of COMBOS) {
    if (cb.need.every(n => n === e.name)) combo = cb;
  }
  return { ...e, dims, combo, isFallback: !hit };
}

/* 四维 → 五档（铁律 5） */
function tier(v) { return TIERS.find(t => v >= t.min && v < t.max) || TIERS[0]; }

/* --- 存档进博物馆（《07/04》结局博物馆） ------------------------------*/
function recordMuseum(s, ending) {
  const m = loadMuseum();
  const rec = {
    name: ending.name, id: ending.id,
    era: 10, at: new Date().toISOString().slice(0, 10),
    dims: ending.dims, combo: ending.combo ? ending.combo.name : null,
    unity: tier(ending.dims.unity).label,
    human: tier(ending.dims.human).label,
    sustain: tier(ending.dims.sustain).label,
    rich: tier(ending.dims.rich).label,
  };
  const k = ending.id + (ending.combo ? '_' + ending.combo.id : '');
  m[k] = rec;
  saveMuseum(m);
  return m;
}

function cl(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }
