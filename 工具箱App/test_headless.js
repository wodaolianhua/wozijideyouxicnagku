/**
 * 用 CDP 驱动 Edge 无头模式跑一遍手机版。
 * 验证：首页 13 个工具、每个工具页能打开、核心交互能跑、无 console error。
 */
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const http = require('http');
const WebSocket = require(process.env.WS_PATH || 'E:/devtools/npmcache/node_modules/ws');

const EDGE = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const BASE = process.env.TB_BASE || 'http://127.0.0.1:8791';
const PORT = 9333;
const PROFILE = path.join('E:/devtools', 'edge-test-profile');

const TOOL_IDS = [
  'money', 'habit', 'todo', 'countdown', 'pomo', 'random', 'vault',
  'quote', 'conv', 'gacha', 'pixel', 'stopwatch', 'home'
];

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

function getJSON(url) {
  return new Promise((res, rej) => {
    http.get(url, r => {
      let d = '';
      r.on('data', c => d += c);
      r.on('end', () => { try { res(JSON.parse(d)); } catch (e) { rej(e); } });
    }).on('error', rej);
  });
}

class CDP {
  constructor(ws) { this.ws = ws; this.id = 0; this.pending = new Map(); this.consoleErrors = []; }
  static async attach() {
    const list = await getJSON(`http://127.0.0.1:${PORT}/json/list`);
    /* 挑一个真正的空白页，别用 Edge 自带的 edge:// 弹窗页 */
    let page = list.find(t => t.type === 'page' && (t.url === 'about:blank' || t.url.startsWith('http://127.0.0.1')));
    if (!page) {
      try {
        page = await getJSON(`http://127.0.0.1:${PORT}/json/new?about:blank`);
      } catch (e) { /* ignore */ }
    }
    if (!page) throw new Error('找不到可用的 page target');
    const ws = new WebSocket(page.webSocketDebuggerUrl, { perMessageDeflate: false, maxPayload: 64 * 1024 * 1024 });
    await new Promise((res, rej) => { ws.on('open', res); ws.on('error', rej); });
    const c = new CDP(ws);
    ws.on('message', raw => {
      let m;
      try { m = JSON.parse(raw); } catch (e) { return; }
      if (m.id && c.pending.has(m.id)) {
        const { res, rej } = c.pending.get(m.id);
        c.pending.delete(m.id);
        m.error ? rej(new Error(JSON.stringify(m.error))) : res(m.result);
      } else if (m.method === 'Runtime.exceptionThrown') {
        const d = m.params.exceptionDetails;
        c.consoleErrors.push(d.exception?.description || d.text || 'exception');
      } else if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') {
        c.consoleErrors.push(m.params.args.map(a => a.value || a.description || '').join(' '));
      }
    });
    return c;
  }
  send(method, params) {
    const id = ++this.id;
    this.ws.send(JSON.stringify({ id, method, params: params || {} }));
    return new Promise((res, rej) => {
      this.pending.set(id, { res, rej });
      setTimeout(() => { if (this.pending.has(id)) { this.pending.delete(id); rej(new Error('timeout ' + method)); } }, 20000);
    });
  }
  async eval(expr) {
    const r = await this.send('Runtime.evaluate', {
      expression: expr, returnByValue: true, awaitPromise: true
    });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  }
  async goto(url) {
    this.consoleErrors = [];
    const r = await this.send('Page.navigate', { url });
    if (r.errorText) throw new Error('导航失败：' + r.errorText);
    await sleep(1200);
    // 等页面稳定
    for (let i = 0; i < 20; i++) {
      const ready = await this.eval('document.readyState').catch(() => null);
      if (ready === 'complete') break;
      await sleep(250);
    }
    await sleep(500);
    const at = await this.eval('location.href').catch(() => '');
    if (!String(at).startsWith('http://127.0.0.1')) throw new Error('页面没加载起来，当前：' + at);
  }
  close() { try { this.ws.close(); } catch (e) {} }
}

(async () => {
  fs.rmSync(PROFILE, { recursive: true, force: true });
  const proc = spawn(EDGE, [
    '--headless=new', '--disable-gpu', '--no-sandbox',
    '--disable-extensions', '--no-first-run', '--disable-features=msEdgeSync',
    `--remote-debugging-port=${PORT}`, `--user-data-dir=${PROFILE}`,
    'about:blank'
  ], { stdio: 'ignore', detached: false });

  let cdp;
  for (let i = 0; i < 30; i++) {
    await sleep(700);
    try { cdp = await CDP.attach(); break; } catch (e) {}
  }
  if (!cdp) { console.log('连不上 Edge'); proc.kill(); process.exit(1); }

  await cdp.send('Page.enable');
  await cdp.send('Runtime.enable');
  await cdp.send('Log.enable');

  let failures = 0;
  const fail = (m) => { failures++; console.log('   ✗ ' + m); };

  // ---------- 1. 首页 ----------
  console.log('\n=== 首页 ===');
  await cdp.goto(BASE + '/index.html');
  const home = await cdp.eval(`(function(){
    return {
      tiles: document.querySelectorAll('.tile').length,
      title: (document.getElementById('topTitle')||{}).textContent,
      names: Array.from(document.querySelectorAll('.tile .nm')).map(e=>e.textContent)
    };
  })()`);
  console.log('工具卡片：', home.tiles, home.tiles === 13 ? 'OK' : 'FAIL');
  console.log('标题：', home.title);
  if (home.tiles !== 13) fail('卡片数不对：' + home.tiles);
  if (cdp.consoleErrors.length) fail('首页 console error: ' + cdp.consoleErrors[0]);

  // ---------- 2. 每个工具页 ----------
  console.log('\n=== 工具页 ===');
  /* 每个工具页必须有的关键文字，证明真的渲染了内容 */
  const EXPECT = {
    money: '记一笔', habit: '加一个习惯', todo: '加一条', countdown: '加一个日子',
    pomo: '专注', random: '转一下', vault: '密码本', quote: '写一句',
    conv: '换算', gacha: '抽', pixel: '像素画板', stopwatch: '归零', home: '编辑'
  };
  for (const id of TOOL_IDS) {
    await cdp.goto(BASE + '/index.html#/tool/' + id);
    const r = await cdp.eval(`(function(){
      var v = document.getElementById('view');
      var t = v ? v.textContent : '';
      return {
        title: (document.getElementById('topTitle')||{}).textContent,
        crashed: /这个工具出错了/.test(t),
        len: t.length,
        html: v ? v.innerHTML.length : 0
      };
    })()`);
    const txt = await cdp.eval(`document.getElementById('view').textContent`);
    const need = EXPECT[id];
    const hasContent = txt.indexOf(need) >= 0;
    const bad = r.crashed || !hasContent || cdp.consoleErrors.length > 0;
    console.log((bad ? '✗ ' : '✓ ') + id.padEnd(11) + ' 「' + r.title + '」 ' + r.len + ' 字');
    if (bad) {
      failures++;
      if (r.crashed) fail(id + ' 页面崩溃');
      if (!hasContent) fail(id + ' 缺少关键内容「' + need + '」，实际：' + txt.slice(0, 40));
      if (cdp.consoleErrors.length) fail(id + ' console error: ' + cdp.consoleErrors[0]);
    }
  }

  // ---------- 3. 核心交互 ----------
  console.log('\n=== 交互测试 ===');

  // 记账：填金额 → 记一笔 → 出现在明细里
  await cdp.goto(BASE + '/index.html#/tool/money');
  const money = await cdp.eval(`(function(){
    var cards = document.querySelectorAll('#view .card');
    var amt = null, note = null;
    for (var i=0;i<cards.length;i++){
      var ins = cards[i].querySelectorAll('input');
      for (var j=0;j<ins.length;j++){
        if (ins[j].type==='number') { if(!amt) amt=ins[j]; }
        else if (ins[j].placeholder && ins[j].placeholder.indexOf('备注')>=0) note=ins[j];
      }
    }
    if(!amt) return {ok:false, why:'找不到金额输入框'};
    amt.value='25.5';
    amt.dispatchEvent(new Event('input',{bubbles:true}));
    var btns = document.querySelectorAll('#view button');
    var add=null;
    btns.forEach(function(b){ if(b.textContent==='记上') add=b; });
    if(!add) return {ok:false, why:'找不到记上按钮'};
    add.click();
    return new Promise(function(res){
      setTimeout(function(){
        res({ok:true, has25: /25\\.50/.test(document.getElementById('view').textContent)});
      }, 600);
    });
  })()`);
  console.log((money.ok && money.has25 ? '✓' : '✗') + ' 记账：记一笔 25.50 元并显示在明细');
  if (!money.ok || !money.has25) { failures++; console.log('   ' + (money.why || '明细里没找到 25.50')); }

  // 待办：加一条 → 出现在列表
  await cdp.goto(BASE + '/index.html#/tool/todo');
  const todo = await cdp.eval(`(function(){
    var cards = document.querySelectorAll('#view .card');
    var inp = cards[0].querySelector('input[type=text]');
    if(!inp) return {ok:false,why:'没有输入框'};
    inp.value='测试待办事项';
    var btn=null;
    document.querySelectorAll('#view button').forEach(function(b){ if(b.textContent==='添加') btn=b; });
    if(!btn) return {ok:false,why:'没有添加按钮'};
    btn.click();
    return new Promise(function(res){ setTimeout(function(){
      res({ok:true, found:/测试待办事项/.test(document.getElementById('view').textContent)});
    },600);});
  })()`);
  console.log((todo.ok && todo.found ? '✓' : '✗') + ' 待办：添加一条并显示');
  if (!todo.ok || !todo.found) { failures++; console.log('   ' + (todo.why || '列表里没找到')); }

  // 习惯打卡：加习惯 → 打卡 → 显示连续天数
  await cdp.goto(BASE + '/index.html#/tool/habit');
  const habit = await cdp.eval(`(function(){
    var cards=document.querySelectorAll('#view .card');
    var inp=cards[0].querySelector('input[type=text]');
    inp.value='喝水';
    var btn=null;
    cards[0].querySelectorAll('button').forEach(function(b){ if(b.textContent==='添加') btn=b; });
    btn.click();
    return new Promise(function(res){ setTimeout(function(){
      var cb=null;
      document.querySelectorAll('#view button').forEach(function(b){ if(b.textContent==='打卡') cb=b; });
      if(!cb) return res({ok:false,why:'没找到打卡按钮'});
      cb.click();
      setTimeout(function(){
        res({ok:true, streak:/连续 1 天/.test(document.getElementById('view').textContent)});
      },500);
    },600);});
  })()`);
  console.log((habit.ok && habit.streak ? '✓' : '✗') + ' 打卡：加习惯 + 打卡 + 显示连击天数');
  if (!habit.ok || !habit.streak) { failures++; console.log('   ' + (habit.why || '连击天数没更新')); }

  // 随机数：转一下 → 出数字
  await cdp.goto(BASE + '/index.html#/tool/random');
  const rnd = await cdp.eval(`(function(){
    var btn=null;
    document.querySelectorAll('#view button').forEach(function(b){ if(b.textContent==='转一下') btn=b; });
    if(!btn) return {ok:false,why:'没有转一下按钮'};
    btn.click();
    return new Promise(function(res){ setTimeout(function(){
      var t=(document.getElementById('randOut')||{}).textContent||'';
      res({ok:true, val:t, isNum:/^\\d+$/.test(t)});
    },1600);});
  })()`);
  console.log((rnd.ok && rnd.isNum ? '✓' : '✗') + ' 随机数：转一下出结果 ' + (rnd.val || ''));
  if (!rnd.ok || !rnd.isNum) failures++;

  // 番茄钟：点开始 → 计时走字
  await cdp.goto(BASE + '/index.html#/tool/pomo');
  const pomo = await cdp.eval(`(function(){
    var btn=null;
    document.querySelectorAll('#view button').forEach(function(b){ if(b.textContent==='开始') btn=b; });
    if(!btn) return {ok:false,why:'没有开始按钮'};
    btn.click();
    return new Promise(function(res){ setTimeout(function(){
      var t=(document.querySelector('#view .big.mono')||{}).textContent||'';
      res({ok:true, val:t, running:/^24:5\\d$/.test(t)});
    },2500);});
  })()`);
  console.log((pomo.ok && pomo.running ? '✓' : '✗') + ' 番茄钟：开始后倒计时走动 ' + (pomo.val || ''));
  if (!pomo.ok || !pomo.running) failures++;

  // 单位换算：3 米 → 英尺（默认单位）
  await cdp.goto(BASE + '/index.html#/tool/conv');
  const conv = await cdp.eval(`(function(){
    var num=document.querySelector('#view input[type=number]');
    if(!num) return {ok:false,why:'没有输入框'};
    num.value='3';
    num.dispatchEvent(new Event('input',{bubbles:true}));
    return new Promise(function(res){ setTimeout(function(){
      var out=(document.querySelector('#view .big.mono')||{}).textContent||'';
      res({ok:true, val:out});
    },400);});
  })()`);
  console.log((conv.ok && /9\.8/.test(conv.val) ? '✓' : '✗') + ' 单位换算：3 米 = ' + (conv.val || '?') + ' 英尺');
  if (!conv.ok || !/9\.8/.test(conv.val)) failures++;

  // 像素画板：画一下 → canvas 有内容
  await cdp.goto(BASE + '/index.html#/tool/pixel');
  const pixel = await cdp.eval(`(function(){
    var cv=document.querySelector('canvas.pixel');
    if(!cv) return {ok:false,why:'没有画布'};
    var r=cv.getBoundingClientRect();
    var x=r.left+r.width*0.3, y=r.top+r.height*0.3;
    cv.dispatchEvent(new PointerEvent('pointerdown',{clientX:x,clientY:y,bubbles:true,pointerId:1}));
    cv.dispatchEvent(new PointerEvent('pointerup',{clientX:x,clientY:y,bubbles:true,pointerId:1}));
    var ctx=cv.getContext('2d');
    var d=ctx.getImageData(0,0,cv.width,cv.height).data;
    var colored=0;
    for(var i=0;i<d.length;i+=4){ if(d[i]!==255||d[i+1]!==255||d[i+2]!==255){colored++;} }
    return {ok:true, colored:colored};
  })()`);
  console.log((pixel.ok && pixel.colored > 0 ? '✓' : '✗') + ' 像素画板：能画上色 (' + (pixel.colored || 0) + ' 像素)');
  if (!pixel.ok || pixel.colored === 0) failures++;

  // 密码本：设置主密码 → 解锁 → 加一条记录
  await cdp.goto(BASE + '/index.html#/tool/vault');
  const vault = await cdp.eval(`(function(){
    var pw=document.querySelector('#view input[type=password]');
    if(!pw) return {ok:false,why:'没有密码框'};
    pw.value='testpass123';
    var btn=null;
    document.querySelectorAll('#view button').forEach(function(b){ if(b.textContent==='解锁') btn=b; });
    btn.click();
    return new Promise(function(res){ setTimeout(function(){
      var site=document.querySelector('#view input[type=text]');
      if(!site) return res({ok:false,why:'解锁后没有出现输入框'});
      site.value='github.com';
      var ins=document.querySelectorAll('#view input[type=text]');
      var pass=null;
      ins.forEach(function(x){ if(x.placeholder==='密码') pass=x; });
      if(pass) pass.value='secret';
      var save=null;
      document.querySelectorAll('#view button').forEach(function(b){ if(b.textContent==='保存') save=b; });
      if(!save) return res({ok:false,why:'没有保存按钮'});
      save.click();
      setTimeout(function(){
        res({ok:true, found:/github\\.com/.test(document.getElementById('view').textContent)});
      },900);
    },1500);});
  })()`);
  console.log((vault.ok && vault.found ? '✓' : '✗') + ' 密码本：设主密码解锁 + 加密存一条');
  if (!vault.ok || !vault.found) { failures++; console.log('   ' + (vault.why || '记录没出现')); }

  // 设置页：有离线说明 + 检查更新按钮
  await cdp.goto(BASE + '/index.html#/settings');
  const set = await cdp.eval(`(function(){
    var t=document.getElementById('view').textContent;
    return {offline:/离线/.test(t), btn:/检查新版本/.test(t)};
  })()`);
  console.log((set.offline && set.btn ? '✓' : '✗') + ' 设置页：离线说明 + 检查更新按钮');
  if (!set.offline || !set.btn) failures++;

  console.log('\n' + (failures === 0 ? '全部通过 ✅' : failures + ' 项失败 ❌'));
  cdp.close();
  proc.kill();
  process.exit(failures === 0 ? 0 : 1);
})().catch(e => { console.error('测试脚本出错:', e); process.exit(1); });
