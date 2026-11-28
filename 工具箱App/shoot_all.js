/**
 * 给每个工具截一张真实渲染图（用有头 Edge，字体/合成正常）。
 * 输出到 shots/ 目录，方便肉眼检查每一个页面。
 */
const { spawn } = require('child_process');
const http = require('http');
const fs = require('fs');
const path = require('path');
const WebSocket = require('E:/devtools/npmcache/node_modules/ws');

const EDGE = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const PORT = 9440;
const PROFILE = 'E:/devtools/edge-shots';
const BASE = 'file:///E:/%E6%96%B0%E5%BB%BA%E6%96%87%E4%BB%B6%E5%A4%B9/%E6%88%91%E7%9A%84%E6%B8%85%E5%8D%95/%E5%B7%A5%E5%85%B7%E7%AE%B1App/mobile/index.html';
const OUT = path.join(__dirname, 'shots');

const PAGES = [
  ['', '首页'],
  ['#/tool/money', '记账本'],
  ['#/tool/habit', '习惯打卡'],
  ['#/tool/todo', '待办清单'],
  ['#/tool/countdown', '倒计时'],
  ['#/tool/pomo', '番茄钟'],
  ['#/tool/random', '随机数'],
  ['#/tool/vault', '密码本'],
  ['#/tool/quote', '每日一言'],
  ['#/tool/conv', '单位换算'],
  ['#/tool/gacha', '抽卡盲盒'],
  ['#/tool/pixel', '像素画板'],
  ['#/tool/stopwatch', '秒表'],
  ['#/tool/home', '个人主页'],
  ['#/settings', '设置']
];

const sleep = ms => new Promise(r => setTimeout(r, ms));
function getJSON(u) {
  return new Promise((res, rej) => {
    http.get(u, r => {
      let d = ''; r.on('data', c => d += c);
      r.on('end', () => { try { res(JSON.parse(d)); } catch (e) { rej(e); } });
    }).on('error', rej);
  });
}

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const proc = spawn(EDGE, [
    `--remote-debugging-port=${PORT}`, `--user-data-dir=${PROFILE}`,
    '--new-window', '--window-position=-3000,-3000', '--window-size=420,900', 'about:blank'
  ], { stdio: 'ignore' });
  await sleep(5000);

  const list = await getJSON(`http://127.0.0.1:${PORT}/json/list`);
  const page = list.find(t => t.type === 'page');
  const ws = new WebSocket(page.webSocketDebuggerUrl, { perMessageDeflate: false, maxPayload: 64 * 1024 * 1024 });
  await new Promise(r => ws.on('open', r));
  let id = 0; const pend = new Map();
  ws.on('message', raw => {
    const m = JSON.parse(raw);
    if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); }
  });
  const send = (method, params = {}) => {
    const i = ++id; ws.send(JSON.stringify({ id: i, method, params }));
    return new Promise(r => pend.set(i, r));
  };
  await send('Runtime.enable'); await send('Page.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });

  for (const [hash, name] of PAGES) {
    await send('Page.navigate', { url: BASE + hash });
    await sleep(2200);
    const shot = await send('Page.captureScreenshot', { format: 'png' });
    const file = path.join(OUT, name + '.png');
    fs.writeFileSync(file, Buffer.from(shot.result.data, 'base64'));
    console.log('✓', name);
  }
  console.log('\n输出目录：', OUT);
  proc.kill(); process.exit(0);
})();
