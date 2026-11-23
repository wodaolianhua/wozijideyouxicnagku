const { chromium } = require('playwright-core');
const ROOT = 'E:\\军事指挥游戏\\03_表现层_3D地图';
process.env.TMP = ROOT + '\\.tmp';
process.env.TEMP = process.env.TMP;

(async () => {
  const ctx = await chromium.launchPersistentContext(ROOT + '\\.chrome-profile', {
    executablePath: 'C:\\Users\\13783\\AppData\\Local\\ms-playwright\\chromium-1208\\chrome-win64\\chrome.exe',
    headless: true,
    viewport: { width: 1440, height: 900 },
  });
  const page = ctx.pages()[0] || await ctx.newPage();
  await page.goto('http://localhost:5178', { waitUntil: 'networkidle' });
  await page.waitForFunction(() => typeof layout !== 'undefined' && layout.armies.length === 9);
  await page.waitForTimeout(800);
  const out = await page.evaluate(() => {
    const item = layout.armies.find(a => a.army.Name === '青弋残军');
    const near = layout.labels.filter(l => l.rect.x < 500 && l.rect.y < 320).map(l => ({ t: l.text, ...l.rect }));
    return {
      blockers: uiBlockers(),
      node: layout.byId.get('N01'),
      item: { sx: item.sx, sy: item.sy, tag: item.tag },
      nearLabels: near,
    };
  });
  console.log(JSON.stringify(out, null, 2));
  await ctx.close();
})().catch(e => { console.error(e); process.exit(1); });
