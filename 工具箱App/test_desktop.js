/** 电脑版 5 个工具的无头测试 */
const { spawn } = require('child_process');
const path = require('path');
const http = require('http');
const WebSocket = require('E:/devtools/npmcache/node_modules/ws');

const EDGE = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const BASE = process.env.TB_BASE || 'http://127.0.0.1:8792';
const PORT = 9388;
const PROFILE = 'E:/devtools/edge-desktop-test';

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }
function getJSON(u) {
  return new Promise((res, rej) => {
    http.get(u, r => {
      let d = ''; r.on('data', c => d += c);
      r.on('end', () => { try { res(JSON.parse(d)); } catch (e) { rej(e); } });
    }).on('error', rej);
  });
}

class CDP {
  constructor(ws) { this.ws = ws; this.id = 0; this.pending = new Map(); this.errs = []; }
  static async attach() {
    const list = await getJSON(`http://127.0.0.1:${PORT}/json/list`);
    let page = list.find(t => t.type === 'page' && (t.url === 'about:blank' || t.url.startsWith('http://127.0.0.1')));
    if (!page) page = await getJSON(`http://127.0.0.1:${PORT}/json/new?about:blank`).catch(() => null);
    const ws = new WebSocket(page.webSocketDebuggerUrl, { perMessageDeflate: false, maxPayload: 64 * 1024 * 1024 });
    await new Promise((res, rej) => { ws.on('open', res); ws.on('error', rej); });
    const c = new CDP(ws);
    ws.on('message', raw => {
      let m; try { m = JSON.parse(raw); } catch (e) { return; }
      if (m.id && c.pending.has(m.id)) {
        const { res, rej } = c.pending.get(m.id); c.pending.delete(m.id);
        m.error ? rej(new Error(JSON.stringify(m.error))) : res(m.result);
      } else if (m.method === 'Runtime.exceptionThrown') {
        c.errs.push(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text);
      } else if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') {
        c.errs.push(m.params.args.map(a => a.value || a.description || '').join(' '));
      }
    });
    return c;
  }
  send(method, params) {
    const id = ++this.id;
    this.ws.send(JSON.stringify({ id, method, params: params || {} }));
    return new Promise((res, rej) => {
      this.pending.set(id, { res, rej });
      setTimeout(() => { if (this.pending.has(id)) { this.pending.delete(id); rej(new Error('timeout')); } }, 20000);
    });
  }
  async eval(expr) {
    const r = await this.send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  }
  async goto(url) {
    this.errs = [];
    const r = await this.send('Page.navigate', { url });
    if (r.errorText) throw new Error('导航失败：' + r.errorText);
    await sleep(1500);
  }
  close() { try { this.ws.close(); } catch (e) {} }
}

(async () => {
  const proc = spawn(EDGE, [
    '--headless=new', '--disable-gpu', '--no-sandbox', '--disable-extensions',
    '--no-first-run', `--remote-debugging-port=${PORT}`, `--user-data-dir=${PROFILE}`, 'about:blank'
  ], { stdio: 'ignore' });

  let cdp;
  for (let i = 0; i < 30; i++) {
    await sleep(700);
    try { cdp = await CDP.attach(); break; } catch (e) {}
  }
  if (!cdp) { console.log('连不上 Edge'); proc.kill(); process.exit(1); }

  await cdp.send('Page.enable');
  await cdp.send('Runtime.enable');

  let failures = 0;

  await cdp.goto(BASE + '/index.html');

  const tabs = await cdp.eval(`Array.from(document.querySelectorAll('#tabs button')).map(b=>b.textContent)`);
  console.log('工具标签：', tabs.join(' / '));
  const expectTabs = ['批量改名', '图片压缩', '重复文件查找', '大文件排行', '笔记搜索'];
  const tabsOk = expectTabs.every(t => tabs.includes(t));
  console.log((tabsOk ? '✓' : '✗') + ' 5 个工具都在');
  if (!tabsOk) failures++;

  // 逐个切换，看内容是否渲染
  const markers = {
    '批量改名': '选文件', '图片压缩': '选图片', '重复文件查找': '选一个文件夹',
    '大文件排行': '选一个文件夹', '笔记搜索': '选一个文件夹'
  };
  for (let i = 0; i < tabs.length; i++) {
    await cdp.eval(`document.querySelectorAll('#tabs button')[${i}].click()`);
    await sleep(400);
    const info = await cdp.eval(`(function(){
      var p=document.getElementById('panel');
      return { txt:p?p.textContent:'', hasDrop: !!document.querySelector('#panel .drop') };
    })()`);
    const need = markers[tabs[i]];
    const ok = info.txt.indexOf(need) >= 0 && info.hasDrop;
    console.log((ok ? '✓' : '✗') + ' ' + tabs[i].padEnd(7) + ' 内容渲染正常');
    if (!ok) { failures++; console.log('   实际：' + info.txt.slice(0, 60)); }
    if (cdp.errs.length) { failures++; console.log('   console error: ' + cdp.errs[0]); }
  }

  // 改名脚本模块可用性
  const rs = await cdp.eval(`(function(){
    if (!window.RenameScript) return {ok:false};
    var files = RenameScript.buildFiles(
      [{from:{name:'a.txt',path:'a.txt'},to:'b.txt'}],
      {mode:'prefix',prefix:'x_',suffix:'',start:1,pad:2});
    return {ok:true, names: files.map(f=>f.name), hasCmd: files.some(f=>f.name==='改名脚本.cmd')};
  })()`);
  console.log((rs.ok && rs.hasCmd ? '✓' : '✗') + ' 改名脚本模块可用：' + (rs.names || []).join(', '));
  if (!rs.ok || !rs.hasCmd) failures++;

  // 浏览器支持检测
  const badge = await cdp.eval(`document.getElementById('browserBadge').textContent`);
  console.log('  浏览器检测：' + badge);

  console.log('\n' + (failures === 0 ? '电脑版全部通过 ✅' : failures + ' 项失败 ❌'));
  cdp.close(); proc.kill();
  process.exit(failures === 0 ? 0 : 1);
})().catch(e => { console.error('测试出错:', e); process.exit(1); });
