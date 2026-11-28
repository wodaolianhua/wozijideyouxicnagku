/* 生活类工具：记账本、习惯打卡、待办清单、倒计时、番茄钟、转盘、密码本 */
(function () {
  'use strict';
  var el = UI.el, kids = UI.kids, toast = UI.toast;

  /* ==================== 1. 记账本 ==================== */
  Tool.register({
    id: 'money', name: '记账本', icon: 'money', group: 'life',
    desc: '记每天花了啥',
    render: function (root) {
      var data = { items: [], cats: ['吃饭', '交通', '购物', '娱乐', '学习', '其他'] };

      Store.get('money').then(function (d) {
        if (d && d.items) data = Object.assign(data, d);
        draw();
      });

      function save() { Store.set('money', data); }

      function monthTotals() {
        var mk = UI.monthKey();
        var inc = 0, exp = 0;
        data.items.forEach(function (it) {
          if (it.date.slice(0, 7) !== mk) return;
          if (it.type === 'in') inc += it.amt; else exp += it.amt;
        });
        return { inc: inc, exp: exp };
      }

      function draw() {
        UI.clear(root);
        var mk = UI.monthKey();
        var t = monthTotals();

        root.appendChild(el('div.card', {}, [
          el('h3', {}, [el('span', { text: '本月' }), el('span.tag', { text: mk })]),
          el('div.big', { text: '¥' + UI.money(t.exp) }),
          el('p.tiny.dim', { text: '支出　收入 ¥' + UI.money(t.inc) + '　结余 ¥' + UI.money(t.inc - t.exp) })
        ]));

        /* 分类占比 */
        var byCat = {};
        var total = 0;
        data.items.forEach(function (it) {
          if (it.date.slice(0, 7) !== mk || it.type === 'in') return;
          byCat[it.cat] = (byCat[it.cat] || 0) + it.amt;
          total += it.amt;
        });
        var cats = Object.keys(byCat);
        if (cats.length && total > 0) {
          var card = el('div.card', {}, [el('h3', { text: '花在哪了' })]);
          cats.sort(function (a, b) { return byCat[b] - byCat[a]; }).forEach(function (c) {
            var pct = Math.round(byCat[c] / total * 100);
            card.appendChild(el('div', { style: { marginBottom: '10px' } }, [
              el('div.row', { style: { marginBottom: '4px' } }, [
                el('span', { text: c, style: { fontSize: '13.5px' } }),
                el('span.grow'),
                el('span.tiny.muted', { text: '¥' + UI.money(byCat[c]) + ' · ' + pct + '%' })
              ]),
              el('div.bar', {}, [el('i', { style: { width: pct + '%' } })])
            ]));
          });
          root.appendChild(card);
        }

        /* 添加 */
        var amtI = el('input', { type: 'number', placeholder: '金额', step: '0.01', inputmode: 'decimal' });
        var catI = el('select', {}, data.cats.map(function (c) { return el('option', { value: c, text: c }); }));
        var typeI = el('select', {}, [el('option', { value: 'out', text: '支出' }), el('option', { value: 'in', text: '收入' })]);
        var noteI = el('input', { type: 'text', placeholder: '备注（可不填）' });
        var dateI = el('input', { type: 'date', value: UI.dateKey() });

        root.appendChild(el('div.card', {}, [
          el('h3', { text: '记一笔' }),
          el('div.row', {}, [typeI, catI]),
          el('div.mt', {}, [amtI]),
          el('div.mt', {}, [noteI]),
          el('div.row.mt', {}, [
            dateI,
            el('button.btn.pri', {
              text: '记上', onclick: function () {
                var a = parseFloat(amtI.value);
                if (!a || a <= 0) return toast('填个金额');
                data.items.unshift({
                  id: Date.now() + Math.random(),
                  amt: a, type: typeI.value, cat: catI.value,
                  note: noteI.value.trim(), date: dateI.value || UI.dateKey(),
                  ts: Date.now()
                });
                save(); amtI.value = ''; noteI.value = '';
                draw(); toast('记上了');
              }
            })
          ])
        ]));

        /* 明细 */
        var listCard = el('div.card', {}, [el('h3', {}, [
          el('span', { text: '明细' }),
          el('span.tag', { text: data.items.length + ' 条' })
        ])]);
        if (!data.items.length) {
          listCard.appendChild(el('div.empty', { text: '还没有记录' }));
        } else {
          var list = el('div.list');
          data.items.slice(0, 80).forEach(function (it) {
            list.appendChild(el('div.item', {}, [
              el('div.t', {}, [
                el('b', { text: it.cat + (it.note ? ' · ' + it.note : '') }),
                el('span', { text: it.date })
              ]),
              el('b', {
                text: (it.type === 'in' ? '+' : '-') + '¥' + UI.money(it.amt),
                style: { color: it.type === 'in' ? 'var(--ok)' : 'var(--tx)' }
              }),
              el('button.x', {
                text: '×', title: '删除', onclick: function () {
                  data.items = data.items.filter(function (x) { return x.id !== it.id; });
                  save(); draw();
                }
              })
            ]));
          });
          listCard.appendChild(list);
        }
        root.appendChild(listCard);
      }
    }
  });

  /* ==================== 2. 习惯打卡 ==================== */
  Tool.register({
    id: 'habit', name: '习惯打卡', icon: 'habit', group: 'life',
    desc: '连击天数·日历热力图',
    render: function (root) {
      var data = { habits: [], logs: {} };

      Store.get('habit').then(function (d) {
        if (d && d.habits) data = Object.assign(data, d);
        draw();
      });
      function save() { Store.set('habit', data); }

      function streak(hid) {
        var log = data.logs[hid] || {};
        var n = 0;
        var d = new Date();
        if (!log[UI.dateKey(d)]) d.setDate(d.getDate() - 1);
        while (log[UI.dateKey(d)]) { n++; d.setDate(d.getDate() - 1); }
        return n;
      }

      function draw() {
        UI.clear(root);

        var nameI = el('input', { type: 'text', placeholder: '习惯名字，比如 喝水' });
        root.appendChild(el('div.card', {}, [
          el('h3', { text: '加一个习惯' }),
          el('div.row', {}, [
            nameI,
            el('button.btn.pri', {
              text: '添加', onclick: function () {
                var n = nameI.value.trim();
                if (!n) return toast('先起个名字');
                data.habits.push({ id: Date.now() + '', name: n, color: data.habits.length % 6 });
                data.logs = data.logs || {};
                save(); nameI.value = ''; draw();
              }
            })
          ])
        ]));

        if (!data.habits.length) {
          root.appendChild(el('div.card', {}, [el('div.empty', { text: '还没有习惯，先加一个' })]));
          return;
        }

        var today = UI.dateKey();
        data.habits.forEach(function (h) {
          var log = data.logs[h.id] || (data.logs[h.id] = {});
          var done = !!log[today];
          var card = el('div.card');
          card.appendChild(el('div.row', {}, [
            el('div.grow', {}, [
              el('b', { text: h.name, style: { fontSize: '15px' } }),
              el('div.tiny.dim', { text: '连续 ' + streak(h.id) + ' 天 · 累计 ' + Object.keys(log).length + ' 天' })
            ]),
            el('button.btn' + (done ? '' : '.pri'), {
              text: done ? '已完成' : '打卡',
              onclick: function () {
                if (log[today]) delete log[today];
                else log[today] = 1;
                save(); draw();
              }
            }),
            el('button.x', {
              text: '×', onclick: function () {
                data.habits = data.habits.filter(function (x) { return x.id !== h.id; });
                delete data.logs[h.id];
                save(); draw();
              }
            })
          ]));

          /* 近 8 周热力图 */
          var heat = el('div.heat', { style: { marginTop: '12px' } });
          var start = new Date();
          start.setDate(start.getDate() - 55);
          start.setDate(start.getDate() - (start.getDay() + 6) % 7);
          for (var i = 0; i < 56; i++) {
            var dd = new Date(start.getTime() + i * 86400000);
            var key = UI.dateKey(dd);
            var lvl = log[key] ? (i > 48 ? 'l3' : 'l2') : '';
            heat.appendChild(el('i', { class: lvl, title: key }));
          }
          card.appendChild(heat);
          root.appendChild(card);
        });
      }
    }
  });

  /* ==================== 3. 待办清单 ==================== */
  Tool.register({
    id: 'todo', name: '待办清单', icon: 'todo', group: 'life',
    desc: '今天/本周分开',
    render: function (root) {
      var data = { items: [] };

      Store.get('todo').then(function (d) {
        if (d && d.items) data = d;
        draw();
      });
      function save() { Store.set('todo', data); }

      var filter = 'today';

      function dueInfo(it) {
        if (!it.due) return { key: 'none', label: '' };
        var k = it.due.slice(0, 10);
        var today = UI.dateKey();
        if (k === today) return { key: 'today', label: '今天' };
        var t = new Date(); t.setDate(t.getDate() + 1);
        if (k === UI.dateKey(t)) return { key: 'today', label: '明天' };
        var w = new Date(); w.setDate(w.getDate() + (7 - w.getDay()) % 7);
        if (k <= UI.dateKey(w)) return { key: 'week', label: '本周' };
        return { key: 'later', label: it.due.slice(5) };
      }

      function draw() {
        UI.clear(root);
        var inp = el('input', { type: 'text', placeholder: '要做什么' });
        var due = el('input', { type: 'date' });
        root.appendChild(el('div.card', {}, [
          el('h3', { text: '加一条' }),
          el('div.row', {}, [
            inp,
            el('button.btn.pri', {
              text: '添加', onclick: function () {
                var t = inp.value.trim();
                if (!t) return toast('写点啥');
                data.items.unshift({ id: Date.now() + '', text: t, due: due.value || '', done: false, ts: Date.now() });
                save(); inp.value = ''; due.value = ''; draw();
              }
            })
          ]),
          el('div.mt', {}, [due])
        ]));

        var tabs = el('div.tabs');
        [['today', '今天'], ['week', '本周'], ['all', '全部'], ['done', '已完成']].forEach(function (f) {
          tabs.appendChild(el('button' + (filter === f[0] ? '.on' : ''), {
            text: f[1], onclick: function () { filter = f[0]; draw(); }
          }));
        });
        root.appendChild(tabs);

        var shown = data.items.filter(function (it) {
          var d = dueInfo(it);
          if (filter === 'done') return it.done;
          if (it.done) return false;
          if (filter === 'all') return true;
          if (filter === 'today') return d.key === 'today' || d.key === 'none';
          if (filter === 'week') return d.key === 'today' || d.key === 'week' || d.key === 'none';
          return true;
        });

        if (!shown.length) {
          root.appendChild(el('div.card', {}, [el('div.empty', { text: '这儿空着，挺好' })]));
          return;
        }

        var card = el('div.card', {}, [el('h3', {}, [
          el('span', { text: '事项' }), el('span.tag', { text: shown.length + ' 条' })
        ])]);
        var list = el('div.list');
        shown.forEach(function (it) {
          var d = dueInfo(it);
          var row = el('div.item' + (it.done ? '.done' : ''), {}, [
            el('input', {
              type: 'checkbox', checked: it.done,
              style: { width: '20px', height: '20px', flex: '0 0 auto', accentColor: 'var(--acc)' },
              onchange: function () { it.done = this.checked; save(); draw(); }
            }),
            el('div.t', {}, [
              el('b', { text: it.text }),
              el('span', { text: d.label || '随时' })
            ]),
            el('button.x', {
              text: '×', onclick: function () {
                data.items = data.items.filter(function (x) { return x.id !== it.id; });
                save(); draw();
              }
            })
          ]);
          list.appendChild(row);
        });
        card.appendChild(list);
        root.appendChild(card);
      }
    }
  });

  /* ==================== 4. 倒计时 / 纪念日 ==================== */
  Tool.register({
    id: 'countdown', name: '倒计时', icon: 'cal', group: 'life',
    desc: '还剩几天',
    render: function (root) {
      var data = { items: [] };

      Store.get('countdown').then(function (d) {
        if (d && d.items) data = d;
        draw();
      });
      function save() { Store.set('countdown', data); }

      function daysLeft(d) {
        var t = new Date(d + 'T00:00:00');
        var now = new Date(); now.setHours(0, 0, 0, 0);
        return Math.round((t - now) / 86400000);
      }

      function draw() {
        UI.clear(root);
        var nm = el('input', { type: 'text', placeholder: '事件，比如 期末考试' });
        var dt = el('input', { type: 'date' });
        var lb = el('input', { type: 'text', placeholder: '标签（可不填）' });
        root.appendChild(el('div.card', {}, [
          el('h3', { text: '加一个日子' }),
          el('div.row', {}, [
            nm,
            el('button.btn.pri', {
              text: '添加', onclick: function () {
                if (!nm.value.trim()) return toast('写个名字');
                if (!dt.value) return toast('选个日期');
                data.items.push({ id: Date.now() + '', name: nm.value.trim(), date: dt.value, label: lb.value.trim() });
                save(); nm.value = ''; dt.value = ''; lb.value = ''; draw();
              }
            })
          ]),
          el('div.row.mt', {}, [dt, lb])
        ]));

        if (!data.items.length) {
          root.appendChild(el('div.card', {}, [el('div.empty', { text: '还没有日子' })]));
          return;
        }

        var sorted = data.items.slice().sort(function (a, b) { return a.date < b.date ? -1 : 1; });
        sorted.forEach(function (it) {
          var left = daysLeft(it.date);
          var passed = left < 0;
          var card = el('div.card', {}, [
            el('div.row', {}, [
              el('div.grow', {}, [
                el('b', { text: it.name, style: { fontSize: '15.5px' } }),
                el('div.tiny.dim', { text: it.date + (it.label ? ' · ' + it.label : '') })
              ]),
              el('div', { style: { textAlign: 'right' } }, [
                el('div.big', {
                  text: passed ? '已过 ' + Math.abs(left) : String(left),
                  style: { color: passed ? 'var(--tx3)' : (left <= 7 ? 'var(--warn)' : 'var(--acc)'), fontSize: '26px' }
                }),
                el('div.tiny.dim', { text: passed ? '天' : (left === 0 ? '就是今天' : '天') })
              ]),
              el('button.x', {
                text: '×', onclick: function () {
                  data.items = data.items.filter(function (x) { return x.id !== it.id; });
                  save(); draw();
                }
              })
            ])
          ]);
          root.appendChild(card);
        });
      }
    }
  });

  /* ==================== 5. 番茄钟 ==================== */
  Tool.register({
    id: 'pomo', name: '番茄钟', icon: 'pomo', group: 'life',
    desc: '25分钟专注',
    render: function (root) {
      var WORK = 25 * 60, BREAK = 5 * 60;
      var state = { left: WORK, running: false, mode: 'work', timer: null, done: 0 };

      Store.getItem('pomo', 'done', 0).then(function (n) { state.done = n; draw(); });

      function ring(frac) {
        var r = 82, c = 2 * Math.PI * r;
        return '<svg width="190" height="190" viewBox="0 0 190 190">' +
          '<circle cx="95" cy="95" r="' + r + '" fill="none" stroke="var(--card2)" stroke-width="12"/>' +
          '<circle cx="95" cy="95" r="' + r + '" fill="none" stroke="' + (state.mode === 'work' ? 'var(--acc)' : 'var(--ok)') + '" stroke-width="12" stroke-linecap="round" stroke-dasharray="' + c + '" stroke-dashoffset="' + (c * (1 - frac)) + '"/></svg>';
      }

      function fmt(s) {
        return String(Math.floor(s / 60)).padStart(2, '0') + ':' + String(s % 60).padStart(2, '0');
      }

      function draw() {
        UI.clear(root);
        var total = state.mode === 'work' ? WORK : BREAK;
        var frac = total ? (total - state.left) / total : 0;
        root.appendChild(el('div.card', {}, [
          el('div.ring', { html: ring(frac) + '<div class="val"><div><div class="big mono">' + fmt(state.left) + '</div>' +
            '<div class="tiny dim">' + (state.mode === 'work' ? '专注中' : '休息中') + '</div></div></div>' }),
          el('div.row.mt', { style: { justifyContent: 'center' } }, [
            el('button.btn.grow', {
              text: state.running ? '暂停' : '开始',
              onclick: function () {
                state.running = !state.running;
                if (state.running) start(); else stop();
                draw();
              }
            }),
            el('button.btn', {
              text: '重置', onclick: function () {
                stop(); state.running = false; state.left = total; draw();
              }
            })
          ]),
          el('div.row.mt', { style: { justifyContent: 'center' } }, [
            el('button.btn.sm' + (state.mode === 'work' ? '.pri' : ''), {
              text: '专注 25 分', onclick: function () {
                stop(); state.running = false; state.mode = 'work'; state.left = WORK; draw();
              }
            }),
            el('button.btn.sm' + (state.mode === 'break' ? '.pri' : ''), {
              text: '休息 5 分', onclick: function () {
                stop(); state.running = false; state.mode = 'break'; state.left = BREAK; draw();
              }
            })
          ])
        ]));

        root.appendChild(el('div.card', {}, [
          el('h3', {}, [el('span', { text: '今天完成' }), el('span.tag', { text: state.done + ' 个番茄' })]),
          el('p.tiny.dim', { text: '专注结束会响一下提醒你休息。' })
        ]));
      }

      function start() {
        stop();
        state.timer = setInterval(function () {
          state.left--;
          if (state.left <= 0) {
            stop();
            state.running = false;
            if (state.mode === 'work') {
              state.done++;
              Store.setItem('pomo', 'done', state.done);
              state.mode = 'break'; state.left = BREAK;
            } else {
              state.mode = 'work'; state.left = WORK;
            }
            try {
              var ctx = new (window.AudioContext || window.webkitAudioContext)();
              var osc = ctx.createOscillator(), g = ctx.createGain();
              osc.connect(g); g.connect(ctx.destination);
              osc.frequency.value = 880; osc.type = 'sine';
              g.gain.setValueAtTime(0.001, ctx.currentTime);
              g.gain.exponentialRampToValueAtTime(0.35, ctx.currentTime + 0.05);
              g.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.9);
              osc.start(); osc.stop(ctx.currentTime + 1);
            } catch (e) {}
            if (navigator.vibrate) navigator.vibrate([200, 100, 200]);
            toast(state.mode === 'break' ? '一个番茄完成，休息一下' : '休息结束，继续');
            draw();
          } else if (state.left % 60 === 0) {
            if (navigator.vibrate) navigator.vibrate(120);
          }
          draw();
        }, 1000);
      }
      function stop() { if (state.timer) { clearInterval(state.timer); state.timer = null; } }

      draw();
    }
  });

  /* ==================== 6. 随机数 / 转盘 ==================== */
  Tool.register({
    id: 'random', name: '随机数', icon: 'wheel', group: 'life',
    desc: '选择困难时转一下',
    render: function (root) {
      var data = { min: 1, max: 100, options: ['今天吃什么', '先做哪个'], mode: 'num' };

      Store.get('random').then(function (d) {
        if (d && d.options) data = Object.assign(data, d);
        draw();
      });
      function save() { Store.set('random', data); }

      function draw() {
        UI.clear(root);
        var res = el('div.card', {}, [
          el('div.big.center', { id: 'randOut', text: '—', style: { padding: '26px 0', fontSize: '40px' } }),
          el('button.btn.pri.block', {
            text: '转一下', onclick: function () {
              var out = document.getElementById('randOut');
              if (data.mode === 'num') {
                var min = parseInt(data.min, 10), max = parseInt(data.max, 10);
                if (isNaN(min) || isNaN(max) || min > max) return toast('范围不对');
                var n = Math.floor(Math.random() * (max - min + 1)) + min;
                roll(out, String(n));
              } else {
                var opts = data.options.filter(function (o) { return o.trim(); });
                if (!opts.length) return toast('先加几个选项');
                roll(out, opts[Math.floor(Math.random() * opts.length)]);
              }
            }
          })
        ]);
        root.appendChild(res);

        var modeCard = el('div.card', {}, [
          el('h3', { text: '模式' }),
          el('div.chips', {}, [
            el('button.chip' + (data.mode === 'num' ? '.on' : ''), {
              text: '随机数', onclick: function () { data.mode = 'num'; save(); draw(); }
            }),
            el('button.chip' + (data.mode === 'opt' ? '.on' : ''), {
              text: '随机选项', onclick: function () { data.mode = 'opt'; save(); draw(); }
            })
          ])
        ]);
        root.appendChild(modeCard);

        if (data.mode === 'num') {
          var mi = el('input', { type: 'number', value: data.min, oninput: function () { data.min = this.value; save(); } });
          var ma = el('input', { type: 'number', value: data.max, oninput: function () { data.max = this.value; save(); } });
          root.appendChild(el('div.card', {}, [
            el('h3', { text: '范围' }),
            el('div.row', {}, [mi, el('span.dim', { text: '到' }), ma])
          ]));
        } else {
          var listCard = el('div.card', {}, [el('h3', { text: '选项' })]);
          var list = el('div.list');
          data.options.forEach(function (o, i) {
            list.appendChild(el('div.item', {}, [
              el('div.t', {}, [el('b', { text: o })]),
              el('button.x', {
                text: '×', onclick: function () {
                  data.options.splice(i, 1); save(); draw();
                }
              })
            ]));
          });
          listCard.appendChild(list);
          var ni = el('input', { type: 'text', placeholder: '加一个选项' });
          listCard.appendChild(el('div.row.mt', {}, [
            ni,
            el('button.btn', {
              text: '加', onclick: function () {
                var v = ni.value.trim();
                if (!v) return;
                data.options.push(v); save(); ni.value = ''; draw();
              }
            })
          ]));
          root.appendChild(listCard);
        }
      }

      function roll(node, text) {
        var t = 0, iv = setInterval(function () {
          node.textContent = Math.random().toString(36).slice(2, 6).toUpperCase();
          t++;
          if (t > 14) {
            clearInterval(iv);
            node.textContent = text;
            node.classList.remove('flash');
            void node.offsetWidth;
            node.classList.add('flash');
            if (navigator.vibrate) navigator.vibrate(60);
          }
        }, 55);
      }
    }
  });

  /* ==================== 7. 个人密码本 ==================== */
  Tool.register({
    id: 'vault', name: '密码本', icon: 'vault', group: 'life',
    desc: '本地加密·不上云',
    render: function (root) {
      var NS = 'vault';
      var key = null;      /* CryptoKey */
      var unlocked = false;
      var saltB64 = null;

      function b64(buf) {
        var b = new Uint8Array(buf), s = '';
        for (var i = 0; i < b.length; i++) s += String.fromCharCode(b[i]);
        return btoa(s);
      }
      function unb64(s) {
        var bin = atob(s), a = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) a[i] = bin.charCodeAt(i);
        return a;
      }      function randBytes(n) {
        var a = new Uint8Array(n);
        crypto.getRandomValues(a);
        return a;
      }

      function deriveKey(pw, salt, usage) {
        return crypto.subtle.importKey('raw', new TextEncoder().encode(pw), 'PBKDF2', false, ['deriveKey'])
          .then(function (base) {
            return crypto.subtle.deriveKey(
              { name: 'PBKDF2', salt: salt, iterations: 150000, hash: 'SHA-256' },
              base, { name: 'AES-GCM', length: 256 }, false, usage);
          });
      }

      function encryptJSON(obj) {
        var iv = randBytes(12);
        return crypto.subtle.encrypt({ name: 'AES-GCM', iv: iv },
          key, new TextEncoder().encode(JSON.stringify(obj)))
          .then(function (ct) {
            return { iv: b64(iv), ct: b64(ct) };
          });
      }
      function decryptJSON(blob) {
        return crypto.subtle.decrypt({ name: 'AES-GCM', iv: unb64(blob.iv) },
          key, unb64(blob.ct))
          .then(function (pt) { return JSON.parse(new TextDecoder().decode(pt)); })
          .catch(function () { throw new Error('密码不对，或数据损坏'); });
      }

      function draw() {
        UI.clear(root);

        if (!unlocked) {
          var pwI = el('input', { type: 'password', placeholder: '主密码', autocomplete: 'current-password' });
          var err = el('p.tiny', { text: '', style: { color: 'var(--bad)', minHeight: '16px' } });
          root.appendChild(el('div.card', {}, [
            el('h3', { text: '密码本' }),
            el('p.tiny.dim', { text: '第一次用请设置一个主密码。密码只在你手机里算，不联网、不上传。忘了主密码就真的打不开了。' }),
            pwI,
            err,
            el('button.btn.pri.block.mt', {
              text: '解锁', onclick: function () {
                var pw = pwI.value;
                if (!pw) return;
                err.textContent = '';
                Store.getItem(NS, 'salt', null).then(function (s) {
                  if (!s) {
                    saltB64 = s = b64(randBytes(16));
                    return Store.setItem(NS, 'salt', s).then(function () {
                      return deriveKey(pw, unb64(s), ['encrypt', 'decrypt']);
                    }).then(function (k) {
                      key = k;
                      return encryptJSON({ items: [] });
                    }).then(function (blob) {
                      return Store.setItem(NS, 'data', blob);
                    }).then(function () {
                      unlocked = true; draw();
                    });
                  }
                  saltB64 = s;
                  return deriveKey(pw, unb64(s), ['encrypt', 'decrypt']).then(function (k) {
                    return Store.getItem(NS, 'data', null).then(function (blob) {
                      if (!blob) throw new Error('没有数据');
                      return decryptJSON(blob).then(function (obj) {
                        key = k; unlocked = true; dataCache = obj; draw();
                      });
                    });
                  });
                }).catch(function (e) {
                  err.textContent = e.message || '打不开';
                });
              }
            }),
            el('p.tiny.dim.mt', { text: '提示：把主密码记在纸上放好，比记在脑子里靠谱。' })
          ]));
          return;
        }

        /* 已解锁 */
        if (!dataCache) dataCache = { items: [] };

        root.appendChild(el('div.card', {}, [
          el('div.row', {}, [
            el('div.grow', {}, [
              el('h3', { text: '密码本' }),
              el('div.tiny.dim', { text: dataCache.items.length + ' 条记录 · 锁上' })
            ]),
            el('button.btn.sm', {
              text: '锁上', onclick: function () {
                unlocked = false; key = null; dataCache = null; draw();
              }
            })
          ])
        ]));

        var form = el('div.card', {}, [el('h3', { text: '加一条' })]);
        var siteI = el('input', { type: 'text', placeholder: '网站 / 名字' });
        var userI = el('input', { type: 'text', placeholder: '账号' });
        var pwI2 = el('input', { type: 'text', placeholder: '密码' });
        var noteI = el('input', { type: 'text', placeholder: '备注（可不填）' });
        form.appendChild(el('div', {}, [siteI]));
        form.appendChild(el('div.mt', {}, [userI]));
        form.appendChild(el('div.mt', {}, [pwI2]));
        form.appendChild(el('div.mt', {}, [noteI]));
        form.appendChild(el('button.btn.pri.block.mt', {
          text: '保存', onclick: function () {
            if (!siteI.value.trim()) return toast('填个名字');
            dataCache.items.push({
              site: siteI.value.trim(), user: userI.value.trim(),
              pass: pwI2.value, note: noteI.value.trim(), ts: Date.now()
            });
            persist();
            siteI.value = userI.value = pwI2.value = noteI.value = '';
            draw();
          }
        }));
        root.appendChild(form);

        var listCard = el('div.card', {}, [el('h3', { text: '记录' })]);
        if (!dataCache.items.length) {
          listCard.appendChild(el('div.empty', { text: '还没有记录' }));
        } else {
          var list = el('div.list');
          dataCache.items.forEach(function (it, i) {
            var revealed = false;
            var pwNode = el('b.mono', { text: '••••••' });
            var row = el('div.item', {}, [
              el('div.t', {}, [
                el('b', { text: it.site }),
                el('span', { text: it.user + (it.note ? ' · ' + it.note : '') }),
                pwNode
              ]),
              el('button.btn.sm', {
                text: '看', onclick: function () {
                  revealed = !revealed;
                  pwNode.textContent = revealed ? it.pass : '••••••';
                }
              }),
              el('button.btn.sm', {
                text: '复制', onclick: function () {
                  copyText(it.pass);
                }
              }),
              el('button.x', {
                text: '×', onclick: function () {
                  dataCache.items.splice(i, 1); persist(); draw();
                }
              })
            ]);
            list.appendChild(row);
          });
          listCard.appendChild(list);
        }
        root.appendChild(listCard);
      }

      var dataCache = null;
      function persist() {
        encryptJSON(dataCache).then(function (blob) {
          Store.setItem(NS, 'data', blob);
          toast('已保存');
        });
      }
      function copyText(t) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(t).then(function () { toast('复制了'); }, function () { toast('复制失败'); });
        } else {
          var ta = document.createElement('textarea');
          ta.value = t; document.body.appendChild(ta); ta.select();
          try { document.execCommand('copy'); toast('复制了'); } catch (e) { toast('复制失败'); }
          ta.remove();
        }
      }

      draw();
    }
  });
})();
