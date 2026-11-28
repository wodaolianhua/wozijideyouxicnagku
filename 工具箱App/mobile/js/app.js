/* 应用核心：工具注册、路由、首页、设置 */
(function (global) {
  'use strict';

  var el = UI.el, kids = UI.kids, toast = UI.toast;

  /* ---------- 工具注册表（在 registry.js 里定义，这里直接用） ---------- */
  var TOOLS = Tool.all();
  var GROUPS = [
    { key: 'life', name: '过日子' },
    { key: 'fun', name: '写着玩' }
  ];

  /* ---------- 主题 ---------- */
  function applyTheme(t) {
    document.documentElement.setAttribute('data-theme', t);
    var m = document.querySelector('meta[name=theme-color]');
    if (m) m.setAttribute('content', t === 'light' ? '#f4f6fa' : '#111318');
  }
  function loadTheme() {
    var t = localStorage.getItem('tb.theme') || 'dark';
    applyTheme(t);
  }
  global.TB = global.TB || {};
  TB.toggleTheme = function () {
    var t = document.documentElement.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
    localStorage.setItem('tb.theme', t);
    applyTheme(t);
  };
  TB.theme = function () { return document.documentElement.getAttribute('data-theme'); };

  /* ---------- 路由 ---------- */
  var view = document.getElementById('view');
  var topTitle = document.getElementById('topTitle');
  var btnBack = document.getElementById('btnBack');

  function route() {
    var hash = location.hash.replace(/^#\/?/, '');
    UI.closeSheet();
    if (!hash) return renderHome();
    var parts = hash.split('/');
    if (parts[0] === 'tool' && parts[1]) return renderTool(parts[1]);
    if (parts[0] === 'settings') return renderSettings();
    if (parts[0] === 'about') return renderAbout();
    renderHome();
  }

  function go(h) { location.hash = h; }
  TB.go = go;

  function setTop(title, showBack) {
    topTitle.textContent = title;
    btnBack.hidden = !showBack;
  }

  btnBack.onclick = function () {
    if (history.length > 1) history.back();
    else go('');
  };

  /* ---------- 首页 ---------- */
  function renderHome() {
    setTop('我的工具箱', false);
    UI.clear(view);
    var total = TOOLS.length;

    var hero = el('div.hero', {}, [
      el('h2', { text: '都在这儿了' }),
      el('p', { text: '点哪个用哪个。数据只存在这台手机上。' }),
      el('div.stats', {}, [
        el('div.stat', {}, [el('b', { text: String(total) }), el('span', { text: '个工具' })]),
        el('div.stat', {}, [el('b', { text: '本机' }), el('span', { text: '数据存放' })]),
        el('div.stat', {}, [el('b', { text: '离线' }), el('span', { text: '断网可用' })])
      ])
    ]);
    view.appendChild(hero);

    GROUPS.forEach(function (g) {
      var items = TOOLS.filter(function (t) { return t.group === g.key; });
      if (!items.length) return;
      var sec = el('section.group', {}, [
        el('h3', { text: g.name + ' · ' + items.length })
      ]);
      var grid = el('div.grid');
      items.forEach(function (t) {
        var tile = el('button.tile', {
          onclick: function () { go('tool/' + t.id); },
          type: 'button'
        }, [
          el('span.ic', { html: Icons.svg(t.icon) }),
          el('span.nm', { text: t.name }),
          el('span.ds', { text: t.desc || '' })
        ]);
        grid.appendChild(tile);
      });
      sec.appendChild(grid);
      view.appendChild(sec);
    });

    view.appendChild(el('p.tiny.dim.center', {
      text: '想加新工具？跟 AI 说一声就行',
      style: { marginTop: '18px' }
    }));
  }

  /* ---------- 工具页 ---------- */
  function renderTool(id) {
    var t = Tool.get(id);
    if (!t) return renderHome();
    setTop(t.name, true);
    UI.clear(view);
    var page = el('div');
    view.appendChild(page);
    try {
      t.render(page);
    } catch (e) {
      UI.clear(page);
      page.appendChild(el('div.card', {}, [
        el('h3', { text: '这个工具出错了' }),
        el('p.tiny.dim', { text: String(e && e.message || e) }),
        el('button.btn.mt', { text: '返回首页', onclick: function () { go(''); } })
      ]));
      console.error(e);
    }
    view.scrollTop = 0;
    window.scrollTo(0, 0);
  }

  /* ---------- 设置 ---------- */
  /* ---------- 更新检查 ----------
     离线优先：App 代码已经装在手机里，日常使用永远不联网。
     只有「想用新功能」时才查一次版本。
     - 在 APK 里：走原生桥，原生负责下载/解压/重启
     - 在浏览器里：走 fetch，提示后手动刷新
     服务器挂了、没网了，都不影响正常使用。 */
  var APP_VERSION = '1.0.0';

  function isNative() {
    return typeof window.ToolboxNative !== 'undefined' && window.ToolboxNative &&
      typeof window.ToolboxNative.checkUpdate === 'function';
  }

  function checkUpdate(manual) {
    /* APK 里：交给原生，UI 由原生弹窗 */
    if (isNative()) {
      window.ToolboxNative.checkUpdate();
      return;
    }
    if (!navigator.onLine) {
      if (manual) toast('现在没网，等有网了再检查');
      return;
    }
    var url = 'version.json?t=' + Date.now();
    fetch(url, { cache: 'no-store' }).then(function (r) {
      if (!r.ok) throw new Error('http ' + r.status);
      return r.json();
    }).then(function (info) {
      var latest = String(info.version || '').replace(/^v/, '');
      if (!latest) return;
      if (latest === APP_VERSION) {
        if (manual) toast('已经是最新版了');
        else console.log('已是最新', APP_VERSION);
        return;
      }
      UI.confirm('有新版本 ' + latest + '\n当前 ' + APP_VERSION + '\n\n要现在下载吗？下载完重新打开就是新版了。', '下载更新')
        .then(function (ok) {
          if (!ok) return;
          toast('正在下载…');
          return fetch(info.url, { cache: 'no-store' })
            .then(function (r) {
              if (!r.ok) throw new Error('下载失败 ' + r.status);
              return r.text();
            })
            .then(function (html) {
              return Store.setItem('sys', 'update', { html: html, version: latest, at: Date.now() });
            })
            .then(function () {
              toast('下好了，下次打开生效');
            })
            .catch(function (e) { toast('更新失败：' + (e.message || e)); });
        });
    }).catch(function (e) {
      console.warn('更新检查失败（不影响使用）', e);
      if (manual) toast('检查失败，不影响使用');
    });
  }
  TB.checkUpdate = checkUpdate;

  function renderSettings() {
    setTop('设置', true);
    UI.clear(view);

    var card1 = el('div.card', {}, [
      el('h3', { text: '外观' }),
      el('div.setrow', {}, [
        el('div.lb', {}, [
          el('b', { text: '深色模式' }),
          el('span', { text: '关掉就是浅色' })
        ]),
        el('label.switch', {}, [
          el('input', {
            type: 'checkbox',
            checked: TB.theme() === 'dark',
            onchange: function () {
              localStorage.setItem('tb.theme', this.checked ? 'dark' : 'light');
              applyTheme(this.checked ? 'dark' : 'light');
            }
          }),
          el('i')
        ])
      ])
    ]);

    var card2 = el('div.card', {}, [
      el('h3', { text: '数据' }),
      el('p.tiny.dim', { text: '所有工具的数据都存在这台手机的浏览器里。导出成一个文件保存好，换手机或清缓存时可以导回来。' }),
      el('div.row.wrap.mt', {}, [
        el('button.btn', { text: '导出全部备份', onclick: exportAll }),
        el('button.btn', { text: '导入备份', onclick: importAll })
      ])
    ]);

    var card3 = el('div.card', {}, [
      el('h3', { text: '离线与更新' }),
      el('p.tiny.dim', { text: '这个 App 的代码已经装在你手机里了，日常使用完全离线，不连服务器也能用——服务器关停了也一样能用。下面这个按钮只是「想用新功能时」才需要点。' }),
      el('div.kv', {}, [el('span.muted', { text: '当前版本' }), el('b', { text: APP_VERSION })]),
      el('div.kv', {}, [el('span.muted', { text: '运行方式' }), el('b', { text: isNative() ? 'APK（完全离线）' : '网页版' })]),
      el('div.kv', {}, [el('span.muted', { text: '联网状态' }), el('b', { id: 'netState', text: navigator.onLine ? '已联网' : '离线（正常使用）' })]),
      el('button.btn.block.mt', { text: '检查新版本', onclick: function () { checkUpdate(true); } })
    ]);

    var card4 = el('div.card', {}, [
      el('h3', { text: '关于' }),
      el('div.kv', {}, [el('span.muted', { text: '工具数量' }), el('b', { text: String(TOOLS.length) })]),
      el('div.kv', {}, [el('span.muted', { text: '数据存放' }), el('b', { text: '本机，不上云' })]),
      el('button.btn.block.mt', { text: '关于这个工具箱', onclick: function () { go('about'); } })
    ]);

    view.appendChild(card1);
    view.appendChild(card2);
    view.appendChild(card3);
    view.appendChild(card4);
  }

  function renderAbout() {
    setTop('关于', true);
    UI.clear(view);
    view.appendChild(el('div.card', {}, [
      el('h3', { text: '我的工具箱' }),
      el('p.tiny.muted', { text: '一个把 13 个小工具装在一起的离线 App。全部数据只存在你这台手机上，不联网、不上传、不需要账号。' }),
      el('p.tiny.dim', { text: isNative()
        ? '你现在用的是 APK 版本：代码整个装在手机里，不连服务器照样能用。服务器哪天关停了也不影响你。'
        : '你现在用的是网页版本：第一次打开后已经缓存到手机里，之后断网也能用。想更像原生 App 就把它添加到主屏幕。' }),
      el('p.tiny.dim', { text: '装好后如果提示有新版本，重新打开一下就行，不用重装。' })
    ]));
  }

  /* ---------- 备份导入导出 ---------- */
  /* 密码本用的是独立 key（加密存储），单独备份 */
  function exportAll() {
    var ns = {};
    Promise.all(TOOLS.filter(function (t) { return t.id !== 'vault'; }).map(function (t) {
      return Store.get(t.id).then(function (v) {
        if (v && Object.keys(v).length) ns[t.id] = v;
      });
    })).then(function () {
      return Promise.all([
        Store.getItem('vault', 'data', null),
        Store.getItem('vault', 'salt', null)
      ]).then(function (r) {
        if (r[0]) ns['vault'] = { encrypted: r[0], salt: r[1] };
      });
    }).then(function () {
      var payload = {
        app: 'toolbox', version: 1,
        exportedAt: new Date().toISOString(),
        data: ns
      };
      UI.exportFile('工具箱备份-' + UI.dateKey() + '.json', JSON.stringify(payload, null, 2), 'application/json');
      toast('备份已导出');
    }).catch(function (e) {
      toast('导出失败：' + (e.message || e));
    });
  }

  function importAll() {
    UI.readFile('application/json,.json').then(function (f) {
      if (!f) return;
      return f.text().then(function (txt) {
        var payload = JSON.parse(txt);
        if (!payload || payload.app !== 'toolbox' || !payload.data) throw new Error('不是本工具箱的备份文件');
        var jobs = [];
        Object.keys(payload.data).forEach(function (ns) {
          var v = payload.data[ns];
          if (ns === 'vault' && v && v.encrypted) {
            jobs.push(Store.setItem('vault', 'data', v.encrypted));
            jobs.push(Store.setItem('vault', 'salt', v.salt));
          } else {
            jobs.push(Store.set(ns, v));
          }
        });
        return Promise.all(jobs).then(function () { return Object.keys(payload.data).length; });
      }).then(function (n) {
        toast('已导入 ' + n + ' 个工具的数据');
        setTimeout(function () { location.reload(); }, 800);
      }).catch(function (e) { toast('导入失败：' + (e.message || e)); });
    });
  }

  /* ---------- 启动 ---------- */
  loadTheme();
  document.getElementById('btnTheme').onclick = TB.toggleTheme;
  document.getElementById('btnSettings').onclick = function () { go('settings'); };
  window.addEventListener('hashchange', route);
  route();

  /* 通知外层容器（手机模拟器）当前状态。
     用 postMessage 而不是直接读 iframe，同源策略下也能通信。 */
  function reportState() {
    try {
      var title = topTitle.textContent;
      parent.postMessage({
        type: 'toolbox-state',
        title: title,
        toolCount: TOOLS.length,
        route: location.hash,
        theme: document.documentElement.getAttribute('data-theme')
      }, '*');
    } catch (e) {}
  }
  TB.reportState = reportState;
  window.addEventListener('message', function (e) {
    var d = e.data;
    if (!d || d.type !== 'toolbox-command') return;
    if (d.action === 'theme') {
      localStorage.setItem('tb.theme', d.theme);
      applyTheme(d.theme);
      reportState();
    }
  });
  var origRoute = route;
  route = function () {
    origRoute();
    setTimeout(reportState, 60);
  };
  setTimeout(reportState, 200);
  window.addEventListener('hashchange', function () { setTimeout(reportState, 80); });

  /* Service Worker：离线可用 */
  if ('serviceWorker' in navigator && location.protocol !== 'file:') {
    window.addEventListener('load', function () {
      navigator.serviceWorker.register('sw.js').catch(function () {});
    });
  }
})(window);
