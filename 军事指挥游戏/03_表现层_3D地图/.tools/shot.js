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
  await sleep(1000);

  const stats0 = await page.evaluate(() => {
    const labels = layout.labels.map(l => ({ ...l.rect, t: l.text }));
    const tags = layout.armies.filter(a => a.tag).map(a => ({ ...a.tag, n: a.army.Name }));
    const blockers = uiBlockers();
    const hits = r => blockers.some(b => r.x < b.x + b.w && b.x < r.x + r.w && r.y < b.y + b.h && b.y < r.y + r.h);
    let ov = 0;
    for (const l of labels) {
      for (const t of tags) {
        if (l.x < t.x + t.w && t.x < l.x + l.w && l.y < t.y + t.h && t.y < l.y + l.h) ov++;
      }
    }
    return {
      w: innerWidth, h: innerHeight,
      labels: labels.length,
      labelsUnderUi: labels.filter(hits).map(l => l.t),
      tags: tags.length,
      tagsUnderUi: tags.filter(hits).map(t => t.n),
      markersUnderUi: layout.armies.filter(a => hits({ x: a.sx - 15, y: a.sy - 15, w: 30, h: 30 })).map(a => a.army.Name),
      labelTagOverlaps: ov,
    };
  });
  await page.screenshot({ path: OUT + '\\round2_01_初始.png' });

  // 展开说明面板：地名/名条应重新避让
  await page.click('.help summary');
  await sleep(450);
  const helpOpen = await page.evaluate(() => {
    const labels = layout.labels.map(l => l.rect);
    const tags = layout.armies.filter(a => a.tag).map(a => a.tag);
    const blockers = uiBlockers();
    const hits = r => blockers.some(b => r.x < b.x + b.w && b.x < r.x + r.w && r.y < b.y + b.h && b.y < r.y + r.h);
    return { labelsUnderUi: labels.filter(hits).length, tagsUnderUi: tags.filter(hits).length };
  });
  await page.click('.help summary');
  await sleep(400);

  const pos = await page.evaluate(() => {
    const a = layout.armies.find(x => x.army.Faction === 0 && x.army.NodeId === 'N01');
    const n2 = layout.byId.get('N02');
    const n6 = layout.byId.get('N06');
    return {
      tx: a.tag.x + a.tag.w / 2, ty: a.tag.y + a.tag.h / 2,
      n2x: n2.sx, n2y: n2.sy, n6x: n6.sx, n6y: n6.sy,
    };
  });

  // 点名条选中（验证名条可点击）
  await page.mouse.click(pos.tx, pos.ty);
  await sleep(250);
  const selected = (await page.textContent('#selectedName')) || '';
  await page.mouse.click(pos.n2x, pos.n2y);
  await sleep(250);
  await page.mouse.click(pos.n6x, pos.n6y);
  await sleep(600);
  const wp = ((await page.textContent('#waypointList')) || '').trim();

  await page.click('#btnOrder');
  await sleep(800);
  const ordered = await page.evaluate(() => {
    const a = world.Armies.find(x => x.Id === 'A_P01');
    return { step: world.Time, path: a.PlannedPath, selected: document.querySelector('#selectedName').textContent };
  });
  await page.screenshot({ path: OUT + '\\round2_02_计划路径.png' });

  // 逐步推进到行军途中，便于观察粗箭头（避开终点名条遮挡）
  await page.click('#btnStep');
  await sleep(450);
  await page.click('#btnStep');
  await sleep(450);
  await page.screenshot({ path: OUT + '\\round2_03_行军箭头.png' });

  await page.click('#btnFastForward');
  await sleep(1200);
  const after = await page.evaluate(() => ({
    step: world.Time,
    trailPoints: (trailHistory.get('A_P01') || []).length,
    tags: layout.armies.filter(a => a.tag).length,
    modalOpen: !document.querySelector('#modalLayer').classList.contains('hidden'),
  }));

  // 重开（模态确认）
  await page.click('#btnReset');
  await page.waitForSelector('#modalLayer:not(.hidden)');
  await page.getByRole('button', { name: '确认重开' }).click();
  await page.waitForFunction(() => world && world.Time === 0);
  await sleep(1100);

  // 遭遇：N01 -> N05 后快进
  const pos2 = await page.evaluate(() => {
    const a = layout.armies.find(x => x.army.Faction === 0 && x.army.NodeId === 'N01');
    const n5 = layout.byId.get('N05');
    return { ax: a.sx, ay: a.sy, nx: n5.sx, ny: n5.sy };
  });
  await page.mouse.click(pos2.ax, pos2.ay);
  await sleep(250);
  await page.mouse.click(pos2.nx, pos2.ny);
  await sleep(500);
  await page.click('#btnOrder');
  await sleep(700);
  await page.click('#btnFastForward');
  let encounter = null;
  try {
    await page.waitForSelector('#modalLayer:not(.hidden)', { timeout: 5000 });
    await sleep(400);
    encounter = await page.evaluate(() => ({
      step: world.Time,
      text: (document.querySelector('#modalLayer').innerText || '').replace(/\s+/g, ' ').slice(0, 220),
    }));
    await page.screenshot({ path: OUT + '\\round2_04_遭遇.png' });
    await page.getByRole('button', { name: /知道了/ }).click();
    await sleep(300);
  } catch (e) {
    encounter = { error: String(e).slice(0, 140), step: await page.evaluate(() => world.Time) };
  }

  // 关闭战报后重开并验证
  await page.click('#btnReset');
  await page.waitForSelector('#modalLayer:not(.hidden)');
  await page.getByRole('button', { name: '确认重开' }).click();
  await page.waitForFunction(() => world && world.Time === 0);
  await sleep(1100);
  const reset = await page.evaluate(() => ({
    step: world.Time,
    tags: layout.armies.filter(a => a.tag).length,
    trails: trailHistory.size,
    strength: world.Armies.find(a => a.Id === 'A_P01').Strength,
  }));

  console.log(JSON.stringify({ stats0, helpOpen, selected, wp, ordered, after, encounter, reset, errors }, null, 2));
  await ctx.close();
})().catch(e => {
  console.error('FATAL', e);
  process.exit(1);
});
