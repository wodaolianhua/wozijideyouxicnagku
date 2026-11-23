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
  await page.waitForFunction(() => typeof layout !== 'undefined' && layout.armies.length === 9);
  await sleep(800);

  const pos = await page.evaluate(() => {
    const a = layout.armies.find(x => x.army.Id === 'A_P01');
    const n11 = layout.byId.get('N11');
    const n16 = layout.byId.get('N16');
    return { ax: a.sx, ay: a.sy, n11x: n11.sx, n11y: n11.sy, n16x: n16.sx, n16y: n16.sy };
  });
  await page.mouse.click(pos.ax, pos.ay);
  await sleep(300);
  const sel1 = await page.evaluate(() => ({ sel: selectedArmyId, name: document.getElementById('selectedName').textContent }));
  await page.mouse.click(pos.n11x, pos.n11y);
  await sleep(300);
  await page.mouse.click(pos.n16x, pos.n16y);
  await sleep(500);
  const pre = await page.evaluate(() => ({
    sel: selectedArmyId,
    wps: waypoints.slice(),
    preview: previewPath.slice(),
    wpText: document.getElementById('waypointList').textContent.trim(),
    orderDisabled: document.getElementById('btnOrder').disabled,
    toasts: Array.from(document.querySelectorAll('.toast')).map(t => t.textContent),
  }));
  await page.click('#btnOrder');
  await sleep(120);
  const t1 = await page.evaluate(() => ({
    busy, autoActive: autoMarch.active, moves: marchAnim.moves.size,
    time: world.Time, onDone: !!marchAnim.onDone,
  }));
  await sleep(900);
  const t2 = await page.evaluate(() => ({
    busy, autoActive: autoMarch.active, autoPaused: autoMarch.paused, autoRunning: autoMarch.running,
    autoSteps: autoMarch.steps, moves: marchAnim.moves.size, onDone: !!marchAnim.onDone,
    time: world.Time, armyNode: world.Armies.find(a => a.Id === 'A_P01').NodeId,
    pauseHidden: document.getElementById('btnPause').hidden,
    toasts: Array.from(document.querySelectorAll('.toast')).map(t => t.textContent),
  }));
  console.log(JSON.stringify({ sel1, pre, t1, t2, errors }, null, 2));
  await ctx.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
