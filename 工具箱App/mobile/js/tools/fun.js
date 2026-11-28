/* 写着玩类工具：每日一言、单位换算、抽卡盲盒、像素画板、秒表、个人主页 */
(function () {
  'use strict';
  var el = UI.el, kids = UI.kids, toast = UI.toast;

  /* ==================== 8. 每日一言 ==================== */
  Tool.register({
    id: 'quote', name: '每日一言', icon: 'quote', group: 'fun',
    desc: '每天写一句',
    render: function (root) {
      var data = { entries: [] };

      Store.get('quote').then(function (d) {
        if (d && d.entries) data = d;
        draw();
      });
      function save() { Store.set('quote', data); }

      function draw() {
        UI.clear(root);
        var ta = el('textarea', { placeholder: '今天想说点什么…' });
        var mood = el('select', {}, ['开心', '平静', '累', '难过', '生气', '兴奋', '无聊'].map(function (m) {
          return el('option', { value: m, text: m });
        }));
        root.appendChild(el('div.card', {}, [
          el('h3', { text: '写一句' }),
          ta,
          el('div.row.mt', {}, [
            mood,
            el('button.btn.pri', {
              text: '记下', onclick: function () {
                var t = ta.value.trim();
                if (!t) return toast('写点啥');
                data.entries.unshift({ id: Date.now() + '', text: t, mood: mood.value, date: UI.dateKey() });
                save(); ta.value = ''; draw();
              }
            })
          ])
        ]));

        if (!data.entries.length) {
          root.appendChild(el('div.card', {}, [el('div.empty', { text: '还没有记录' })]));
          return;
        }

        /* 按月分组 */
        var groups = {};
        data.entries.forEach(function (e) {
          var mk = e.date.slice(0, 7);
          (groups[mk] = groups[mk] || []).push(e);
        });
        Object.keys(groups).sort().reverse().forEach(function (mk) {
          var card = el('div.card', {}, [
            el('h3', {}, [el('span', { text: mk }), el('span.tag', { text: groups[mk].length + ' 条' })])
          ]);
          var list = el('div.list');
          groups[mk].forEach(function (e) {
            list.appendChild(el('div.item', {}, [
              el('div.t', {}, [
                el('b', { text: e.text, style: { fontWeight: '400', whiteSpace: 'pre-wrap' } }),
                el('span', { text: e.date + ' · ' + e.mood })
              ]),
              el('button.x', {
                text: '×', onclick: function () {
                  data.entries = data.entries.filter(function (x) { return x.id !== e.id; });
                  save(); draw();
                }
              })
            ]));
          });
          card.appendChild(list);
          root.appendChild(card);
        });
      }
    }
  });

  /* ==================== 9. 单位换算 ==================== */
  Tool.register({
    id: 'conv', name: '单位换算', icon: 'conv', group: 'fun',
    desc: '长度重量温度汇率',
    render: function (root) {
      var UNITS = {
        length: { name: '长度', base: '米', items: [
          ['米', 1], ['千米', 1000], ['厘米', 0.01], ['毫米', 0.001], ['英里', 1609.344],
          ['码', 0.9144], ['英尺', 0.3048], ['英寸', 0.0254], ['海里', 1852]
        ]},
        weight: { name: '重量', base: '千克', items: [
          ['千克', 1], ['克', 0.001], ['毫克', 0.000001], ['吨', 1000],
          ['斤', 0.5], ['两', 0.05], ['磅', 0.45359237], ['盎司', 0.028349523]
        ]},
        volume: { name: '体积', base: '升', items: [
          ['升', 1], ['毫升', 0.001], ['立方米', 1000], ['杯', 0.236588],
          ['盎司', 0.0295735], ['加仑', 3.78541]
        ]},
        area: { name: '面积', base: '平方米', items: [
          ['平方米', 1], ['平方公里', 1000000], ['公顷', 10000], ['亩', 666.6667],
          ['平方英尺', 0.092903], ['平方英里', 2589988.11]
        ]},
        time: { name: '时间', base: '秒', items: [
          ['秒', 1], ['毫秒', 0.001], ['分钟', 60], ['小时', 3600],
          ['天', 86400], ['周', 604800], ['年', 31557600]
        ]}
      };
      var data = { cat: 'length', from: 0, to: 6, val: 1, rates: { CNY: 1, USD: 7.1, EUR: 7.7, JPY: 0.047, HKD: 0.91, GBP: 9.0, KRW: 0.0052 } };

      Store.get('conv').then(function (d) {
        if (d && d.cat) data = Object.assign(data, d);
        draw();
      });
      function save() { Store.set('conv', data); }

      function draw() {
        UI.clear(root);
        var cat = data.cat === 'rate' ? 'length' : data.cat;
        var u = UNITS[cat];

        var chips = el('div.chips.mb', {}, Object.keys(UNITS).map(function (k) {
          return el('button.chip' + (cat === k ? '.on' : ''), {
            text: UNITS[k].name, onclick: function () {
              data.cat = k; data.from = 0; data.to = 1; save(); draw();
            }
          });
        }).concat([
          el('button.chip' + (data.cat === 'rate' ? '.on' : ''), {
            text: '汇率', onclick: function () { data.cat = 'rate'; save(); draw(); }
          })
        ]));
        root.appendChild(chips);

        if (data.cat === 'rate') return drawRate();

        var valI = el('input', { type: 'number', value: data.val, step: 'any', inputmode: 'decimal', oninput: function () { data.val = this.value; save(); } });
        var fromS = el('select', {}, u.items.map(function (it, i) { return el('option', { value: i, text: it[0], selected: i === data.from }); }));
        var toS = el('select', {}, u.items.map(function (it, i) { return el('option', { value: i, text: it[0], selected: i === data.to }); }));

        function calc() {
          var f = u.items[parseInt(fromS.value, 10)][1];
          var t = u.items[parseInt(toS.value, 10)][1];
          var v = parseFloat(valI.value);
          if (isNaN(v)) return '—';
          var base = v * f;
          var out = base / t;
          /* 数字太长就收一收，最多 4 位小数且不留尾随零 */
          var a = Math.abs(out);
          if (a !== 0 && (a >= 1e12 || a < 1e-6)) return out.toExponential(4);
          var s = out.toFixed(4);
          if (s.indexOf('.') >= 0) s = s.replace(/0+$/, '').replace(/\.$/, '');
          return s;
        }

        var outNode = el('div.big.mono', { text: '—' });
        function refresh() {
          data.from = parseInt(fromS.value, 10);
          data.to = parseInt(toS.value, 10);
          data.val = valI.value;
          save();
          outNode.textContent = calc();
        }
        fromS.onchange = refresh; toS.onchange = refresh; valI.oninput = refresh;

        root.appendChild(el('div.card', {}, [
          el('h3', { text: u.name + '换算' }),
          el('div', {}, [valI]),
          el('div.row.mt', {}, [
            el('div.grow', {}, [fromS]),
            el('button.btn', { text: '⇄', onclick: function () {
              var t = data.from; data.from = data.to; data.to = t;
              fromS.value = data.from; toS.value = data.to; refresh();
            } }),
            el('div.grow', {}, [toS])
          ]),
          el('div.mt', { style: { paddingTop: '12px', borderTop: '1px solid var(--line)' } }, [
            el('div.tiny.dim', { text: '等于' }),
            outNode
          ])
        ]));

        /* 常见对照表 */
        var table = el('div.card', {}, [el('h3', { text: '1 ' + u.base + ' =' })]);
        var tb = el('table.tb');
        u.items.forEach(function (it) {
          tb.appendChild(el('tr', {}, [
            el('td', { text: it[0] }),
            el('td', { text: String(it[1]) })
          ]));
        });
        table.appendChild(tb);
        root.appendChild(table);
        refresh();
      }

      function drawRate() {
        var rates = data.rates;
        var codes = Object.keys(rates);
        var baseI = el('input', { type: 'number', value: 1, step: 'any', inputmode: 'decimal' });
        var baseS = el('select', {}, codes.map(function (c) { return el('option', { value: c, text: c }); }));
        var targetS = el('select', {}, codes.map(function (c) { return el('option', { value: c, text: c }); }));
        var outNode = el('div.big.mono', { text: '—' });

        function refresh() {
          var v = parseFloat(baseI.value);
          if (isNaN(v)) { outNode.textContent = '—'; return; }
          var r = (rates[targetS.value] / rates[baseS.value]) * v;
          var a = Math.abs(r);
          var txt = (a !== 0 && (a >= 1e12 || a < 1e-6))
            ? r.toExponential(4)
            : r.toFixed(4).replace(/0+$/, '').replace(/\.$/, '');
          outNode.textContent = txt + ' ' + targetS.value;
        }
        baseI.oninput = refresh;
        baseS.onchange = refresh;
        targetS.onchange = refresh;

        root.appendChild(el('div.card', {}, [
          el('h3', { text: '汇率换算' }),
          el('p.tiny.dim', { text: '汇率是手动填的，不联网自动更新。要准的话自己改下面的数字。' }),
          el('div', {}, [baseI]),
          el('div.row.mt', {}, [
            el('div.grow', {}, [baseS]),
            el('span.dim', { text: '→' }),
            el('div.grow', {}, [targetS])
          ]),
          el('div.mt', { style: { paddingTop: '12px', borderTop: '1px solid var(--line)' } }, [
            el('div.tiny.dim', { text: '等于' }), outNode
          ])
        ]));

        var card = el('div.card', {}, [el('h3', { text: '汇率表' })]);
        var list = el('div.list');
        codes.forEach(function (c) {
          var inp = el('input', {
            type: 'number', value: rates[c], step: 'any', inputmode: 'decimal',
            style: { width: '110px' },
            oninput: function () { rates[c] = parseFloat(this.value) || 0; save(); refresh(); }
          });
          list.appendChild(el('div.item', {}, [
            el('div.t', {}, [el('b', { text: c })]),
            el('span.tiny.dim', { text: '每 1 CNY =' }),
            inp
          ]));
        });
        card.appendChild(list);
        root.appendChild(card);
        refresh();
      }
    }
  });

  /* ==================== 10. 抽卡 / 盲盒 ==================== */
  Tool.register({
    id: 'gacha', name: '抽卡盲盒', icon: 'gacha', group: 'fun',
    desc: '随机抽一个',
    render: function (root) {
      var data = { pools: { '默认池': [] }, history: [] };

      Store.get('gacha').then(function (d) {
        if (d && d.pools) data = Object.assign(data, d);
        if (!data.pools || !Object.keys(data.pools).length) data.pools = { '默认池': [] };
        if (!data.history) data.history = [];
        draw();
      });
      function save() { Store.set('gacha', data); }

      function draw() {
        UI.clear(root);
        var poolNames = Object.keys(data.pools);
        var cur = data.cur && data.pools[data.cur] ? data.cur : poolNames[0];
        data.cur = cur;

        var poolChips = el('div.chips.mb');
        poolNames.forEach(function (p) {
          poolChips.appendChild(el('button.chip' + (p === cur ? '.on' : ''), {
            text: p, onclick: function () { data.cur = p; save(); draw(); }
          }));
        });
        root.appendChild(poolChips);

        /* 换池/新建池 单独一行，不跟标签挤在一起 */
        root.appendChild(el('div.row.mb', {}, [
          el('button.btn.sm', {
            text: '换池子', onclick: function () {
              var name = prompt('切到哪个池子？\n\n' + poolNames.join('\n'), cur);
              if (name && data.pools[name]) { data.cur = name; save(); draw(); }
            }
          }),
          el('button.btn.sm', {
            text: '+ 新池', onclick: function () {
              var name = prompt('新池子叫什么？');
              if (!name || !name.trim()) return;
              data.pools[name.trim()] = [];
              data.cur = name.trim();
              save(); draw();
            }
          }),
          el('span.grow'),
          el('span.tiny.dim', { text: poolNames.length + ' 个池子' })
        ]));

        var items = (data.pools[cur] || []).filter(function (x) { return x && x.trim(); });

        var cardBox = el('div.card', {}, [
          el('div', {
            id: 'gachaBox',
            style: {
              minHeight: '150px', display: 'grid', placeItems: 'center',
              background: 'var(--bg2)', borderRadius: '12px', padding: '20px',
              fontSize: '30px', fontWeight: '600', textAlign: 'center', lineHeight: '1.4'
            },
            text: items.length ? '？' : '空池子'
          }),
          el('button.btn.pri.block.mt', {
            text: '抽！', onclick: function () {
              if (!items.length) return toast('先加几个东西');
              var box = document.getElementById('gachaBox');
              var t = 0;
              var iv = setInterval(function () {
                box.textContent = items[Math.floor(Math.random() * items.length)];
                t++;
                if (t > 16) {
                  clearInterval(iv);
                  var pick = items[Math.floor(Math.random() * items.length)];
                  box.textContent = pick;
                  box.classList.remove('flash'); void box.offsetWidth; box.classList.add('flash');
                  data.history.unshift({ pool: cur, item: pick, ts: Date.now() });
                  data.history = data.history.slice(0, 100);
                  save();
                  if (navigator.vibrate) navigator.vibrate([30, 40, 30]);
                  renderHistory();
                }
              }, 70);
            }
          })
        ]);
        root.appendChild(cardBox);

        var editCard = el('div.card', {}, [
          el('h3', {}, [el('span', { text: '池子内容' }), el('span.tag', { text: items.length + ' 项' })]),
          el('p.tiny.dim', { text: '一行一个。' })
        ]);
        var ta = el('textarea', { value: items.join('\n'), placeholder: 'SSR 卡\nSR 卡\n普通卡' });
        editCard.appendChild(ta);
        editCard.appendChild(el('button.btn.block.mt', {
          text: '保存', onclick: function () {
            data.pools[cur] = ta.value.split('\n').map(function (x) { return x.trim(); }).filter(Boolean);
            save(); draw();
          }
        }));
        root.appendChild(editCard);

        var hisCard = el('div.card', {}, [el('h3', { text: '抽卡记录' })]);
        var hisList = el('div.list', { id: 'gachaHistory' });
        hisCard.appendChild(hisList);
        root.appendChild(hisCard);
        renderHistory();

        function renderHistory() {
          if (!document.getElementById('gachaHistory')) return;
          UI.clear(hisList);
          if (!data.history.length) {
            hisList.appendChild(el('div.empty', { text: '还没抽过' }));
            return;
          }
          data.history.slice(0, 30).forEach(function (h) {
            var d = new Date(h.ts);
            hisList.appendChild(el('div.item', {}, [
              el('div.t', {}, [
                el('b', { text: h.item }),
                el('span', { text: h.pool + ' · ' + UI.dateKey(d) + ' ' + String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') })
              ])
            ]));
          });
        }
      }
    }
  });

  /* ==================== 11. 像素画板 ==================== */
  Tool.register({
    id: 'pixel', name: '像素画板', icon: 'pixel', group: 'fun',
    desc: '画像素·导出PNG',
    render: function (root) {
      var SIZE = 16, CELL = 16;
      var grid = [];
      for (var y = 0; y < SIZE; y++) { grid.push([]); for (var x = 0; x < SIZE; x++) grid[y].push(null); }
      var color = '#5b8cff';
      var drawing = false;
      var erasing = false;

      var PALETTE = ['#000000', '#ffffff', '#ff6b6b', '#ffb454', '#3ddc97', '#5b8cff', '#a78bfa', '#ff7ab6', '#8b5e3c', '#9aa4b8'];

      function draw() {
        UI.clear(root);
        var canvas = el('canvas', { class: 'pixel', width: SIZE * CELL, height: SIZE * CELL });

        function paint(cx, cy) {
          if (cx < 0 || cy < 0 || cx >= SIZE || cy >= SIZE) return;
          grid[cy][cx] = erasing ? null : color;
          redraw();
        }
        function posOf(e) {
          var r = canvas.getBoundingClientRect();
          var p = e.touches ? e.touches[0] : e;
          var x = Math.floor((p.clientX - r.left) / r.width * SIZE);
          var y = Math.floor((p.clientY - r.top) / r.height * SIZE);
          return [x, y];
        }
        function start(e) {
          e.preventDefault();
          drawing = true;
          var p = posOf(e); paint(p[0], p[1]);
        }
        function move(e) {
          if (!drawing) return;
          e.preventDefault();
          var p = posOf(e); paint(p[0], p[1]);
        }
        function end() { drawing = false; }

        canvas.addEventListener('pointerdown', start);
        canvas.addEventListener('pointermove', move);
        window.addEventListener('pointerup', end);
        canvas.addEventListener('touchstart', start, { passive: false });
        canvas.addEventListener('touchmove', move, { passive: false });
        canvas.addEventListener('touchend', end);

        function redraw() {
          var ctx = canvas.getContext('2d');
          ctx.clearRect(0, 0, canvas.width, canvas.height);
          ctx.fillStyle = '#ffffff';
          ctx.fillRect(0, 0, canvas.width, canvas.height);
          for (var y = 0; y < SIZE; y++) {
            for (var x = 0; x < SIZE; x++) {
              if (!grid[y][x]) continue;
              ctx.fillStyle = grid[y][x];
              ctx.fillRect(x * CELL, y * CELL, CELL, CELL);
            }
          }
          ctx.strokeStyle = 'rgba(0,0,0,.10)';
          ctx.lineWidth = 1;
          for (var i = 0; i <= SIZE; i++) {
            ctx.beginPath(); ctx.moveTo(i * CELL, 0); ctx.lineTo(i * CELL, canvas.height); ctx.stroke();
            ctx.beginPath(); ctx.moveTo(0, i * CELL); ctx.lineTo(canvas.width, i * CELL); ctx.stroke();
          }
        }

        var pal = el('div.chips.mb');
        PALETTE.forEach(function (c) {
          pal.appendChild(el('button', {
            style: {
              width: '30px', height: '30px', borderRadius: '8px', background: c,
              border: c === color ? '2px solid var(--tx)' : '1px solid var(--line)', cursor: 'pointer'
            },
            onclick: function () {
              color = c; erasing = false; draw();
            }
          }));
        });
        pal.appendChild(el('button.btn.sm', {
          text: '橡皮', onclick: function () { erasing = true; draw(); }
        }));

        root.appendChild(el('div.card', {}, [
          el('h3', { text: '像素画板' }),
          el('p.tiny.dim', { text: '手指或鼠标拖着画。' }),
          pal,
          el('div.canvas-wrap', {}, [canvas])
        ]));

        root.appendChild(el('div.card', {}, [
          el('div.row', {}, [
            el('button.btn.grow', {
              text: '导出 PNG', onclick: function () {
                var out = document.createElement('canvas');
                out.width = out.height = SIZE * CELL;
                var ctx = out.getContext('2d');
                for (var y = 0; y < SIZE; y++) {
                  for (var x = 0; x < SIZE; x++) {
                    ctx.fillStyle = grid[y][x] || '#ffffff';
                    ctx.fillRect(x * CELL, y * CELL, CELL, CELL);
                  }
                }
                out.toBlob(function (b) {
                  var a = document.createElement('a');
                  a.href = URL.createObjectURL(b);
                  a.download = '像素画-' + UI.dateKey() + '.png';
                  document.body.appendChild(a); a.click();
                  setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
                });
              }
            }),
            el('button.btn.grow', {
              text: '清空', onclick: function () {
                grid.forEach(function (r) { r.forEach(function (v, i) { r[i] = null; }); });
                draw();
              }
            })
          ]),
          el('div.row.mt', {}, [
            el('label.fl', { text: '画布大小', style: { whiteSpace: 'nowrap' } }),
            el('select', {
              onchange: function () {
                var n = parseInt(this.value, 10);
                var ng = [];
                for (var y = 0; y < n; y++) { ng.push([]); for (var x = 0; x < n; x++) ng[y].push(null); }
                grid = ng; SIZE = n; draw();
              }
            }, [8, 16, 24, 32].map(function (n) {
              return el('option', { value: n, text: n + ' × ' + n, selected: n === SIZE });
            }))
          ])
        ]));
        redraw();
      }
      draw();
    }
  });

  /* ==================== 12. 秒表 / laps ==================== */
  Tool.register({
    id: 'stopwatch', name: '秒表', icon: 'stopwatch', group: 'fun',
    desc: '计圈数',
    render: function (root) {
      var t0 = null, acc = 0, timer = null, running = false;
      var laps = [];

      function fmt(ms) {
        var m = Math.floor(ms / 60000), s = Math.floor(ms % 60000 / 1000), cs = Math.floor(ms % 1000 / 10);
        return String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0') + '.' + String(cs).padStart(2, '0');
      }
      function now() { return acc + (running ? Date.now() - t0 : 0); }

      function draw() {
        UI.clear(root);
        var disp = el('div.big.mono.center', { text: fmt(now()), style: { fontSize: '44px', padding: '18px 0' } });

        root.appendChild(el('div.card', {}, [
          disp,
          el('div.row', {}, [
            el('button.btn.grow' + (running ? '' : '.pri'), {
              text: running ? '暂停' : '开始', onclick: function () {
                if (running) { acc = now(); running = false; stopT(); }
                else { t0 = Date.now(); running = true; startT(); }
                draw();
              }
            }),
            el('button.btn', {
              text: '归零', onclick: function () {
                running = false; stopT(); acc = 0; t0 = null; laps = []; draw();
              }
            }),
            el('button.btn', {
              text: '计圈', onclick: function () {
                if (!t0 && !acc) return toast('先开始');
                laps.unshift(now());
                draw();
              }
            })
          ])
        ]));

        if (laps.length) {
          var card = el('div.card', {}, [el('h3', { text: '分段' })]);
          var tb = el('table.tb');
          tb.appendChild(el('tr', {}, [el('th', { text: '圈' }), el('th', { text: '本圈' }), el('th', { text: '累计' })]));
          var prev = 0;
          laps.forEach(function (l, i) {
            var seg = l - prev; prev = l;
            tb.appendChild(el('tr', {}, [
              el('td', { text: String(laps.length - i) }),
              el('td', { text: fmt(seg) }),
              el('td', { text: fmt(l) })
            ]));
          });
          card.appendChild(tb);
          root.appendChild(card);
        }
      }

      function startT() {
        stopT();
        timer = setInterval(function () {
          var n = document.querySelector('.big.mono.center');
          if (n) n.textContent = fmt(now());
        }, 33);
      }
      function stopT() { if (timer) { clearInterval(timer); timer = null; } }
      draw();
    }
  });

  /* ==================== 13. 个人主页 ==================== */
  Tool.register({
    id: 'home', name: '个人主页', icon: 'home', group: 'fun',
    desc: '放介绍和链接',
    render: function (root) {
      var data = { title: '我', intro: '', links: [], theme: 'blue' };

      Store.get('home').then(function (d) {
        if (d && d.title !== undefined) data = Object.assign(data, d);
        draw();
      });
      function save() { Store.set('home', data); }

      function draw() {
        UI.clear(root);

        var preview = el('div.card', { style: { background: 'linear-gradient(150deg,var(--accbg),var(--card))' } }, [
          el('div.center', {}, [
            el('div', {
              style: {
                width: '58px', height: '58px', borderRadius: '50%', margin: '0 auto 10px',
                background: 'var(--acc)', display: 'grid', placeItems: 'center',
                color: '#fff', fontSize: '24px', fontWeight: '600'
              },
              text: (data.title || '我').slice(0, 1)
            }),
            el('div', { text: data.title || '我', style: { fontSize: '19px', fontWeight: '600' } }),
            el('p.tiny.muted', { text: data.intro || '还没写介绍', style: { whiteSpace: 'pre-wrap', marginTop: '6px' } })
          ])
        ]);
        data.links.filter(Boolean).forEach(function (l) {
          preview.appendChild(el('a', {
            href: l.url || '#', target: '_blank', rel: 'noopener',
            class: 'item', style: { textDecoration: 'none', marginTop: '8px' }
          }, [
            el('div.t', {}, [el('b', { text: l.name })]),
            el('span.tiny.dim', { text: l.url })
          ]));
        });
        root.appendChild(preview);

        var tI = el('input', { type: 'text', value: data.title, placeholder: '名字' });
        var iI = el('textarea', { value: data.intro, placeholder: '介绍一下自己' });
        root.appendChild(el('div.card', {}, [
          el('h3', { text: '编辑' }),
          el('div', {}, [tI]),
          el('div.mt', {}, [iI])
        ]));

        var linkCard = el('div.card', {}, [el('h3', { text: '链接' })]);
        var list = el('div.list');
        data.links.forEach(function (l, i) {
          list.appendChild(el('div.item', {}, [
            el('div.t', {}, [el('b', { text: l.name }), el('span', { text: l.url })]),
            el('button.x', {
              text: '×', onclick: function () { data.links.splice(i, 1); save(); draw(); }
            })
          ]));
        });
        linkCard.appendChild(list);
        var ln = el('input', { type: 'text', placeholder: '名字' });
        var lu = el('input', { type: 'text', placeholder: 'https://...' });
        linkCard.appendChild(el('div.mt', {}, [ln]));
        linkCard.appendChild(el('div.mt', {}, [lu]));
        linkCard.appendChild(el('button.btn.block.mt', {
          text: '加链接', onclick: function () {
            if (!ln.value.trim()) return toast('填个名字');
            data.links.push({ name: ln.value.trim(), url: lu.value.trim() });
            save(); ln.value = ''; lu.value = ''; draw();
          }
        }));
        root.appendChild(linkCard);

        root.appendChild(el('div.card', {}, [
          el('button.btn.block', {
            text: '保存并刷新上面的预览', onclick: function () {
              data.title = tI.value.trim() || '我';
              data.intro = iI.value;
              save(); draw(); toast('好了');
            }
          }),
          el('p.tiny.dim.mt', { text: '这个主页存在你自己手机上。想给别人看的话，可以把内容复制到别的平台。' })
        ]));
      }
    }
  });
})();
