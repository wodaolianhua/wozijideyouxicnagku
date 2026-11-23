/* 第 3 轮 · 行军过程与状态反馈 浏览器实测（1440×900） */
const { chromium } = require('playwright-core');
const fs = require('fs');

const ROOT = 'E:\\军事指挥游戏\\03_表现层_3D地图';
const OUT = ROOT + '\\.screenshots';
const PROFILE = ROOT + '\\.chrome-profile';
process.env.TMP = ROOT + '\\.tmp';
process.env.TEMP = process.env.TMP;
fs.mkdirSync(OUT, { recursive: true });
fs.mkdirSync(process.env.TMP, { recursive: true });

const sleep = ms => new Promise(r => setTimeout(r, ms));
const report = {};

(async () => {
  const ctx = await chromium.launchPersistentContext(PROFILE, {
    executablePath: 'C:\\Users\\13783\\AppData\\Local\\ms-playwright\\chromium-1208\\chrome-win64\\chrome.exe',
    headless: true,
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 1,
    args: ['--no-first-run', '--disable-features=Translate'],
  });
  const page = ctx.pages()[0] || await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text()); });

  await page.goto('http://localhost:5178', { waitUntil: 'networkidle' });
  await page.waitForFunction(() => typeof layout !== 'undefined' && layout.armies.length === 9);
  // 确定性起点：重置世界并重载页面
  await page.evaluate(() => api('/api/reset', {}));
  await page.reload({ waitUntil: 'networkidle' });
  await page.waitForFunction(() => typeof layout !== 'undefined' && layout.armies.length === 9 && world.Time === 0);
  await sleep(900);

  // ===== 1. 初始状态：9 支部队全部「待命」，横幅/暂停按钮隐藏 =====
  report.initial = await page.evaluate(() => ({
    statuses: world.Armies.map(a => armyStatus(a, performance.now()).text),
    bannerHidden: document.getElementById('orderBanner').classList.contains('hidden'),
    pauseHidden: document.getElementById('btnPause').hidden,
    stepDisabled: document.getElementById('btnStep').disabled,
  }));
  await page.screenshot({ path: OUT + '\\round3_01_初始.png' });

  const pos = await page.evaluate(() => {
    const a = layout.armies.find(x => x.army.Id === 'A_P01');
    const n2 = layout.byId.get('N02');
    const n6 = layout.byId.get('N06');
    return { ax: a.sx, ay: a.sy, n2x: n2.sx, n2y: n2.sy, n6x: n6.sx, n6y: n6.sy };
  });

  // ===== 2. 选中 + 计划路径 =====
  await page.mouse.click(pos.ax, pos.ay);
  await sleep(260);
  await page.mouse.click(pos.n2x, pos.n2y);
  await sleep(240);
  await page.mouse.click(pos.n6x, pos.n6y);
  await sleep(420);
  report.selected = await page.evaluate(() => ({
    selected: document.getElementById('selectedName').textContent,
    detail: document.getElementById('selectedDetail').textContent,
    preview: previewPath.join('>'),
    waypoints: document.getElementById('waypointList').textContent.trim(),
  }));
  await page.screenshot({ path: OUT + '\\round3_02_选中与计划路径.png' });

  // ===== 3. 下令 → 连续行军：采样补间动画（不瞬移） =====
  await page.click('#btnOrder');
  const anim = await page.evaluate(async () => {
    const out = [];
    const t0 = performance.now();
    let sawMoves = 0;
    let timeDuringAnim = null;
    while (performance.now() - t0 < 520) {
      const m = marchAnim.moves.get('A_P01');
      if (m) {
        sawMoves++;
        if (timeDuringAnim === null) timeDuringAnim = world.Time;
        const e = moveEased(m, performance.now());
        const p = pointAtDistance(m.pts, m.cum, e * m.total);
        out.push({ ms: Math.round(performance.now() - t0), e: +e.toFixed(3), x: Math.round(p[0]), y: Math.round(p[1]) });
      }
      await new Promise(r => setTimeout(r, 55));
    }
    return {
      samples: out,
      sawMoves,
      timeDuringAnim,
      autoActive: autoMarch.active,
      pauseVisible: !document.getElementById('btnPause').hidden,
      banner: document.getElementById('orderBanner').textContent,
    };
  });
  report.anim = anim;
  await page.screenshot({ path: OUT + '\\round3_03_行军中.png' });

  // 等自动行军结束（抵达）
  await page.waitForFunction(() => !autoMarch.active, { timeout: 30000 });
  await sleep(120);
  report.arrived = await page.evaluate(() => {
    const a = world.Armies.find(x => x.Id === 'A_P01');
    return {
      time: world.Time,
      node: a.NodeId,
      idle: isArmyIdle(a),
      status: armyStatus(a, performance.now()).text,
      banner: document.getElementById('orderBanner').textContent,
      trailPoints: (trailHistory.get('A_P01') || []).length,
      pauseHidden: document.getElementById('btnPause').hidden,
    };
  });
  await page.screenshot({ path: OUT + '\\round3_04_已抵达.png' });

  // ===== 4. 暂停 / 单步 / 继续（多段长路径，留足暂停窗口） =====
  await page.click('#btnReset');
  await page.waitForSelector('#modalLayer:not(.hidden)');
  await page.getByRole('button', { name: '确认重开' }).click();
  await page.waitForFunction(() => world && world.Time === 0);
  await sleep(900);

  const pos2 = await page.evaluate(() => {
    const a = layout.armies.find(x => x.army.Id === 'A_P01');
    const n11 = layout.byId.get('N11');
    const n16 = layout.byId.get('N16');
    return { ax: a.sx, ay: a.sy, n11x: n11.sx, n11y: n11.sy, n16x: n16.sx, n16y: n16.sy };
  });
  await page.mouse.click(pos2.ax, pos2.ay);
  await sleep(240);
  await page.mouse.click(pos2.n11x, pos2.n11y);
  await sleep(240);
  await page.mouse.click(pos2.n16x, pos2.n16y);
  await sleep(420);
  await page.click('#btnOrder');
  await page.waitForFunction(() => !document.getElementById('btnPause').hidden, { timeout: 10000 });
  await sleep(500); // 走到中途再暂停
  await page.click('#btnPause');
  // 当前步动画播完后再测冻结（暂停语义：走完当前步即停）
  await page.waitForFunction(() => autoMarch.paused && marchAnim.moves.size === 0, { timeout: 5000 });
  await sleep(120);
  const tPause = await page.evaluate(() => world.Time);
  await sleep(750);
  const tPause2 = await page.evaluate(() => world.Time);
  report.pause = await page.evaluate(([a, b]) => ({
    paused: autoMarch.paused,
    active: autoMarch.active,
    btnText: document.getElementById('btnPause').textContent,
    timeFrozen: a === b,
    status: armyStatus(world.Armies.find(x => x.Id === 'A_P01'), performance.now()).text,
  }), [tPause, tPause2]);
  await page.screenshot({ path: OUT + '\\round3_05_暂停.png' });

  // 暂停中推进 1 步：应播放单步动画且时间 +1
  const stepBefore = await page.evaluate(() => world.Time);
  await page.click('#btnStep');
  const stepAnim = await page.evaluate(async () => {
    let saw = 0;
    const t0 = performance.now();
    while (performance.now() - t0 < 500) {
      if (marchAnim.moves.has('A_P01')) saw++;
      await new Promise(r => setTimeout(r, 40));
    }
    return saw;
  });
  const stepAfter = await page.evaluate(() => world.Time);
  report.singleStep = { before: stepBefore, after: stepAfter, animFrames: stepAnim };

  // 继续行军 → 抵达
  await page.click('#btnPause');
  await page.waitForFunction(() => !autoMarch.active, { timeout: 30000 });
  report.resumed = await page.evaluate(() => {
    const a = world.Armies.find(x => x.Id === 'A_P01');
    return { time: world.Time, node: a.NodeId, idle: isArmyIdle(a) };
  });

  // ===== 5. 快进 = 加速播放 + 遭遇停弹战报 =====
  await page.click('#btnReset');
  await page.waitForSelector('#modalLayer:not(.hidden)');
  await page.getByRole('button', { name: '确认重开' }).click();
  await page.waitForFunction(() => world && world.Time === 0);
  await sleep(900);

  const pos3 = await page.evaluate(() => {
    const a = layout.armies.find(x => x.army.Id === 'A_P01');
    const n5 = layout.byId.get('N05');
    return { ax: a.sx, ay: a.sy, n5x: n5.sx, n5y: n5.sy };
  });
  await page.mouse.click(pos3.ax, pos3.ay);
  await sleep(240);
  await page.mouse.click(pos3.n5x, pos3.n5y);
  await sleep(400);
  await page.click('#btnOrder');
  await page.waitForFunction(() => !document.getElementById('btnPause').hidden, { timeout: 10000 });
  await page.click('#btnPause'); // 先暂停自动行军，单独测快进
  await sleep(150);

  await page.click('#btnFastForward');
  const ff = await page.evaluate(async () => {
    const times = [];
    let sawMoves = 0;
    const t0 = performance.now();
    while (performance.now() - t0 < 15000) {
      times.push(world.Time);
      if (marchAnim.moves.size > 0) sawMoves++;
      if (!document.getElementById('modalLayer').classList.contains('hidden')) break;
      if (!autoMarch.active) break;
      await new Promise(r => setTimeout(r, 90));
    }
    return { times, sawMoves };
  });
  let encounter = null;
  try {
    await page.waitForSelector('#modalLayer:not(.hidden)', { timeout: 8000 });
    await sleep(420);
    encounter = await page.evaluate(() => ({
      step: world.Time,
      buttons: Array.from(document.querySelectorAll('.modal-actions .btn')).map(b => b.textContent),
      text: (document.querySelector('#modalLayer').innerText || '').replace(/\s+/g, ' ').slice(0, 200),
      status: armyStatus(world.Armies.find(x => x.Id === 'A_P01'), performance.now()).text,
    }));
    await page.screenshot({ path: OUT + '\\round3_06_快进遭遇.png' });
    await page.getByRole('button', { name: /知道了/ }).click();
    await sleep(300);
  } catch (e) {
    encounter = { error: String(e).slice(0, 160) };
  }
  report.fastForward = {
    distinctTimes: [...new Set(ff.times)].filter(t => t > 0),
    sawAnimFrames: ff.sawMoves,
    gradual: new Set(ff.times).size >= 3,
    encounter,
  };

  // ===== 6. 收尾：重开恢复 =====
  await page.click('#btnReset');
  await page.waitForSelector('#modalLayer:not(.hidden)');
  await page.getByRole('button', { name: '确认重开' }).click();
  await page.waitForFunction(() => world && world.Time === 0);
  await sleep(900);
  report.finalReset = await page.evaluate(() => ({
    time: world.Time,
    strength: world.Armies.find(a => a.Id === 'A_P01').Strength,
    trails: trailHistory.size,
    bannerHidden: document.getElementById('orderBanner').classList.contains('hidden'),
    pauseHidden: document.getElementById('btnPause').hidden,
  }));

  report.errors = errors;
  console.log(JSON.stringify(report, null, 2));
  await ctx.close();
})().catch(e => {
  console.error('FATAL', e);
  process.exit(1);
});
