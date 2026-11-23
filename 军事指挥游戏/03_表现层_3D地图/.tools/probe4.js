const { chromium } = require('playwright-core');
const ROOT = 'E:\\军事指挥游戏\\03_表现层_3D地图';
process.env.TMP = ROOT + '\\.tmp';
process.env.TEMP = process.env.TMP;
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const ctx = await chromium.launchPersistentContext(ROOT + '\\.chrome-profile', {
    executablePath: 'C:\\Users\\13783\\AppData\\Local\\ms-playwright\\chromium-1208\\chrome-win64\\chrome.exe',
    headless: true,
    viewport: { width: 1440, height: 900 },
    args: ['--no-first-run', '--disable-features=Translate'],
  });
  const page = ctx.pages()[0] || await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text()); });
  await page.goto('http://localhost:5178', { waitUntil: 'networkidle' });
  await page.evaluate(() => api('/api/reset', {}));
  await page.reload({ waitUntil: 'networkidle' });
  await page.waitForFunction(() => typeof layout !== 'undefined' && layout.armies.length === 9 && world.Time === 0);
  await sleep(800);
  const out = {};

  // ===== A. 遭遇 → 关闭战报 → 重新下令，状态应解除「遭遇中」 =====
  const pos = await page.evaluate(() => {
    const a = layout.armies.find(x => x.army.Id === 'A_P01');
    const n5 = layout.byId.get('N05');
    const n2 = layout.byId.get('N02');
    return { ax: a.sx, ay: a.sy, n5x: n5.sx, n5y: n5.sy, n2x: n2.sx, n2y: n2.sy };
  });
  await page.mouse.click(pos.ax, pos.ay);
  await sleep(240);
  await page.mouse.click(pos.n5x, pos.n5y);
  await sleep(400);
  await page.click('#btnOrder');
  await page.waitForSelector('#modalLayer:not(.hidden)', { timeout: 25000 });
  await sleep(400);
  out.afterEncounter = await page.evaluate(() => ({
    status: armyStatus(world.Armies.find(x => x.Id === 'A_P01'), performance.now()).text,
    autoActive: autoMarch.active,
  }));
  await page.getByRole('button', { name: /知道了/ }).click();
  await sleep(300);

  // 重新下令：A_P01 → N02
  await page.mouse.click(pos.ax, pos.ay);
  await sleep(240);
  await page.mouse.click(pos.n2x, pos.n2y);
  await sleep(400);
  await page.click('#btnOrder');
  await sleep(700);
  out.afterReorder = await page.evaluate(() => ({
    status: armyStatus(world.Armies.find(x => x.Id === 'A_P01'), performance.now()).text,
    banner: document.getElementById('orderBanner').textContent,
    autoActive: autoMarch.active,
    time: world.Time,
  }));
  // 等它走完（可能再次遭遇，先关战报）
  await page.waitForFunction(() => !autoMarch.active, { timeout: 30000 });
  if (!(await page.evaluate(() => document.getElementById('modalLayer').classList.contains('hidden')))) {
    await page.getByRole('button', { name: /知道了/ }).click();
    await sleep(300);
  }
  out.reorderDone = await page.evaluate(() => {
    const a = world.Armies.find(x => x.Id === 'A_P01');
    return { node: a.NodeId, status: armyStatus(a, performance.now()).text, time: world.Time };
  });

  // ===== B. 暂停中重开 =====
  await page.click('#btnReset');
  await page.waitForSelector('#modalLayer:not(.hidden)');
  await page.getByRole('button', { name: '确认重开' }).click();
  await page.waitForFunction(() => world && world.Time === 0);
  await sleep(800);
  await page.mouse.click(pos.ax, pos.ay);
  await sleep(240);
  await page.mouse.click(pos.n5x, pos.n5y);
  await sleep(400);
  await page.click('#btnOrder');
  await page.waitForFunction(() => !document.getElementById('btnPause').hidden, { timeout: 10000 });
  await sleep(400);
  await page.click('#btnPause');
  await sleep(300);
  out.pausedThenReset = await page.evaluate(() => ({ paused: autoMarch.paused, active: autoMarch.active }));
  await page.click('#btnReset');
  await page.waitForSelector('#modalLayer:not(.hidden)');
  await page.getByRole('button', { name: '确认重开' }).click();
  await page.waitForFunction(() => world && world.Time === 0);
  await sleep(700);
  out.afterReset = await page.evaluate(() => ({
    time: world.Time,
    autoActive: autoMarch.active,
    paused: autoMarch.paused,
    pauseHidden: document.getElementById('btnPause').hidden,
    bannerHidden: document.getElementById('orderBanner').classList.contains('hidden'),
    trails: trailHistory.size,
  }));

  out.errors = errors;
  console.log(JSON.stringify(out, null, 2));
  await ctx.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
