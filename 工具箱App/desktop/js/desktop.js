/* 电脑版文件工具箱：批量改名 / 图片压缩 / 重复文件查找 / 大文件排行 / 本地笔记搜索 */
(function () {
  'use strict';

  var $ = function (s) { return document.querySelector(s); };

  /** el('div.card', {..}, [..]) —— 支持 tag.class1.class2 语法 */
  function el(sel, attrs, kids) {
    var parts = String(sel).split('.');
    var tag = parts[0] || 'div';
    var e = document.createElement(tag);
    for (var ci = 1; ci < parts.length; ci++) {
      if (parts[ci]) e.classList.add(parts[ci]);
    }
    if (attrs) Object.keys(attrs).forEach(function (k) {
      var v = attrs[k];
      if (v === null || v === undefined || v === false) return;
      if (k === 'html') e.innerHTML = v;
      else if (k === 'text') e.textContent = v;
      else if (k === 'style' && typeof v === 'object') Object.assign(e.style, v);
      else if (k.slice(0, 2) === 'on' && typeof v === 'function') e.addEventListener(k.slice(2), v);
      else if (k === 'checked' || k === 'disabled') e[k] = !!v;
      else e.setAttribute(k, v);
    });
    if (kids !== undefined && kids !== null) {
      (Array.isArray(kids) ? kids : [kids]).forEach(function (c) {
        if (c === null || c === undefined || c === false) return;
        e.appendChild(typeof c === 'object' ? c : document.createTextNode(String(c)));
      });
    }
    return e;
  }
  function clear(e) { while (e.firstChild) e.removeChild(e.firstChild); return e; }
  function fmtSize(b) {
    if (b < 1024) return b + ' B';
    if (b < 1048576) return (b / 1024).toFixed(1) + ' KB';
    if (b < 1073741824) return (b / 1048576).toFixed(2) + ' MB';
    return (b / 1073741824).toFixed(2) + ' GB';
  }
  function fmtTime(ms) {
    var d = new Date(ms);
    function p(n) { return String(n).padStart(2, '0'); }
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + ' ' + p(d.getHours()) + ':' + p(d.getMinutes());
  }
  function msg(kind, text) {
    var m = $('#msg');
    if (!m) return;
    m.className = 'msg on ' + kind;
    m.textContent = text;
  }
  function clearMsg() { var m = $('#msg'); if (m) m.className = 'msg'; }

  /* ============ 通用：选文件夹 ============ */
  var dirHandle = null;
  function supportsFS() { return typeof window.showDirectoryPicker === 'function'; }

  async function pickDir() {
    if (!supportsFS()) {
      msg('bad', '这个浏览器不支持直接操作文件夹。请用电脑上的 Chrome 或 Edge 打开本页面。');
      return null;
    }
    try {
      dirHandle = await window.showDirectoryPicker({ mode: 'readwrite' });
      return dirHandle;
    } catch (e) {
      if (e && e.name !== 'AbortError') msg('bad', '选文件夹失败：' + e.message);
      return null;
    }
  }

  async function walk(dirHandle, path, out, opts) {
    opts = opts || {};
    var dirs = [];
    for await (var it of dirHandle.values()) {
      if (it.kind === 'directory') {
        if (opts.skipDirs && opts.skipDirs[it.name]) continue;
        dirs.push(it);
      } else {
        var file = await it.getFile();
        out.push({
          name: it.name, path: path + it.name, file: file,
          size: file.size, mtime: file.lastModified,
          handle: it, parent: dirHandle
        });
      }
    }
    for (var i = 0; i < dirs.length; i++) {
      if (opts.maxDepth !== undefined && opts.depth >= opts.maxDepth) break;
      await walk(dirs[i], path + dirs[i].name + '/', out, Object.assign({}, opts, { depth: (opts.depth || 0) + 1 }));
    }
  }

  function dropZone(text, onFiles) {
    var dz = el('div.drop', { html: '<b>' + text + '</b>把文件夹拖进来，或者点这里选' });
    dz.addEventListener('click', function () {
      if (onFiles) onFiles();
    });
    dz.addEventListener('dragover', function (e) { e.preventDefault(); dz.classList.add('over'); });
    dz.addEventListener('dragleave', function () { dz.classList.remove('over'); });
    dz.addEventListener('drop', function (e) {
      e.preventDefault(); dz.classList.remove('over');
      var items = e.dataTransfer.items;
      if (!items || !items.length) return;
      var entry = items[0].webkitGetAsEntry && items[0].webkitGetAsEntry();
      if (entry && entry.isDirectory) {
        entry.createReader().readEntries(async function (ents) {
          for (var i = 0; i < ents.length; i++) {
            var sub = await collectFromEntry(ents[i]);
            onFiles(sub);
            return;
          }
        });
      }
    });
    return dz;
  }

  async function collectFromEntry(entry, out) {
    out = out || [];
    if (entry.isFile) {
      var f = await new Promise(function (res) { entry.file(res); });
      out.push({ name: f.name, path: entry.fullPath.replace(/^\//, ''), file: f, size: f.size, mtime: f.lastModified });
    } else if (entry.isDirectory) {
      var reader = entry.createReader();
      var ents;
      do {
        ents = await new Promise(function (res) { reader.readEntries(res); });
        for (var i = 0; i < ents.length; i++) await collectFromEntry(ents[i], out);
      } while (ents.length);
    }
    return out;
  }

  /* ============ 工具 1：批量改名 ============ */
  var renameTool = {
    name: '批量改名',
    desc: '按规则给一批文件统一改名。默认不覆盖原文件——生成新名字给你确认。',
    render: function (panel) {
      var files = [];
      var rules = { prefix: '', suffix: '', start: 1, pad: 2, find: '', replace: '', keepExt: true, mode: 'prefix' };

      var listBox = el('div');
      var outBox = el('div');

      panel.appendChild(el('div.card', {}, [
        el('h2', { text: '1. 选文件' }),
        el('p.desc', { text: '可以整个文件夹，也可以拖一批文件进来。' }),
        dropZone('选一个文件夹', async function () {
          var h = await pickDir();
          if (!h) return;
          var out = [];
          await walk(h, '', out, { depth: 0 });
          files = out;
          renderList();
        }),
        el('div.mt', {}, [el('label', { style: { display: 'block', marginBottom: '8px' } }, [
          el('input', { type: 'file', multiple: 'multiple', onchange: function (e) {
            files = Array.prototype.map.call(e.target.files, function (f) {
              return { name: f.name, path: f.name, file: f, size: f.size, mtime: f.lastModified };
            });
            renderList();
          } })
        ])]),
        el('p.tiny.dim', { text: '也可以直接把文件拖到上面那个框里。' })
      ]));

      panel.appendChild(el('div.card', {}, [
        el('h2', { text: '2. 规则' }),
        el('div.row', {}, [
          el('div', { style: { flex: '1 1 220px' } }, [
            el('label.fl', { text: '方式' }),
            el('select', { onchange: function () { rules.mode = this.value; } }, [
              el('option', { value: 'prefix', text: '加前缀 + 编号' }),
              el('option', { value: 'find', text: '查找替换' }),
              el('option', { value: 'seq', text: '仅编号' })
            ])
          ]),
          el('div', { style: { flex: '1 1 140px' } }, [
            el('label.fl', { text: '前缀' }),
            el('input', { type: 'text', value: '', placeholder: '例如 旅行_', oninput: function () { rules.prefix = this.value; } })
          ]),
          el('div', { style: { flex: '0 1 120px' } }, [
            el('label.fl', { text: '后缀' }),
            el('input', { type: 'text', value: '', placeholder: '_2026', oninput: function () { rules.suffix = this.value; } })
          ])
        ]),
        el('div.row.mt', {}, [
          el('div', { style: { flex: '0 1 110px' } }, [
            el('label.fl', { text: '起始编号' }),
            el('input', { type: 'number', value: 1, oninput: function () { rules.start = parseInt(this.value, 10) || 1; } })
          ]),
          el('div', { style: { flex: '0 1 110px' } }, [
            el('label.fl', { text: '编号位数' }),
            el('input', { type: 'number', value: 2, min: 1, oninput: function () { rules.pad = parseInt(this.value, 10) || 1; } })
          ]),
          el('div', { style: { flex: '1 1 180px' } }, [
            el('label.fl', { text: '查找（模式2）' }),
            el('input', { type: 'text', value: '', placeholder: '要被替换的字', oninput: function () { rules.find = this.value; } })
          ]),
          el('div', { style: { flex: '1 1 180px' } }, [
            el('label.fl', { text: '替换成（模式2）' }),
            el('input', { type: 'text', value: '', placeholder: '新字，留空=删除', oninput: function () { rules.replace = this.value; } })
          ])
        ])
      ]));

      var actionCard = el('div.card', {}, [
        el('h2', { text: '3. 生成并改名' }),
        el('div.row', {}, [
          el('button.btn.pri', { text: '预览新名字', onclick: preview }),
          el('button.btn', { text: '直接改文件', onclick: applyRename }),
          el('button.btn.sm', { text: '导出改名清单 CSV', onclick: exportCsv })
        ]),
        el('div.msg', { id: 'msg' }),
        el('div.mt', { id: 'out' })
      ]);
      panel.appendChild(actionCard);

      function renderList() {
        clear(listBox);
        if (!files.length) { listBox.appendChild(el('div.empty', { text: '还没选文件' })); return; }
        listBox.appendChild(el('p.tiny.muted', { text: '已选 ' + files.length + ' 个文件' }));
        var tb = el('table.tbl');
        tb.appendChild(el('tr', {}, [el('th', { text: '原名' }), el('th', { text: '大小' }), el('th', { text: '位置' })]));
        files.slice(0, 300).forEach(function (f) {
          tb.appendChild(el('tr', {}, [
            el('td', { text: f.name }),
            el('td', { class: 'num', text: fmtSize(f.size) }),
            el('td', { class: 'dim tiny', text: f.path })
          ]));
        });
        listBox.appendChild(el('div.scroll', {}, [tb]));
      }
      renderList();

      function splitName(name) {
        var i = name.lastIndexOf('.');
        if (i <= 0) return [name, ''];
        return [name.slice(0, i), name.slice(i)];
      }
      function pad(n) { return String(n).padStart(rules.pad, '0'); }

      function newName(f, idx) {
        var parts = splitName(f.name);
        var base = parts[0], ext = parts[1];
        if (rules.mode === 'find') {
          base = rules.find ? base.split(rules.find).join(rules.replace) : base;
        } else {
          base = (rules.mode === 'seq' ? '' : rules.prefix) + pad(rules.start + idx) + (rules.mode === 'seq' ? '' : rules.suffix);
          if (rules.mode === 'prefix' && rules.prefix) base = rules.prefix + pad(rules.start + idx) + (rules.suffix || '');
        }
        return base + (ext ? '.' + ext : '');
      }

      var pairs = [];
      function preview() {
        clearMsg();
        pairs = files.map(function (f, i) { return { from: f, to: newName(f, i) }; });
        clear(outBox);
        if (!pairs.length) { msg('warn', '先选文件'); return; }
        var changed = pairs.filter(function (p) { return p.from.name !== p.to; });
        if (!changed.length) { msg('warn', '按当前规则算出来名字都没变'); }
        else msg('ok', '共 ' + changed.length + ' 个文件会改名。先看看下面的清单。');

        var tb = el('table.tbl');
        tb.appendChild(el('tr', {}, [el('th', { text: '原名' }), el('th', { text: '新名' })]));
        changed.slice(0, 300).forEach(function (p) {
          tb.appendChild(el('tr', {}, [
            el('td', { text: p.from.name }),
            el('td', { style: { color: 'var(--ok)' }, text: p.to })
          ]));
        });
        outBox.appendChild(el('div.scroll', {}, [tb]));
      }

      /**
       * 浏览器没有「重命名文件」的 API（File System Access API 只能新建/写入）。
       * 所以这里生成一个 PowerShell 脚本，存到目标文件夹里，
       * 用户双击运行就完成真改名 —— 文件还在原地，只是名字变了。
       */
      async function applyRename() {
        clearMsg();
        if (!files.length) return msg('warn', '先选文件');
        if (!pairs.length) preview();
        var changed = pairs.filter(function (p) { return p.from.name !== p.to; });
        if (!changed.length) return msg('warn', '没有需要改名的文件');

        var ok = confirm('将生成一个改名脚本并放到文件夹里。\n\n' +
          changed.length + ' 个文件会改名，脚本会先备份一份清单，随时可以反悔。\n\n继续？');
        if (!ok) return;

        try {
          var root = dirHandle ? dirHandle : null;
          if (!root) {
            msg('warn', '改名脚本需要先选文件夹（拖进来的文件没有目录信息）');
            return;
          }
          await writeScript(root, changed);
          msg('ok', '脚本已生成。回到文件夹里双击「改名脚本.cmd」就会执行。');
        } catch (e) {
          msg('bad', '生成失败：' + (e.message || e));
        }
      }

      /** 生成并写入改名脚本（逻辑在 rename-script.js 里，可单独测试） */
      async function writeScript(root, changed) {
        var files = RenameScript.buildFiles(changed, rules);
        await RenameScript.writeFiles(root, files);
      }

      function exportCsv() {
        if (!pairs.length) preview();
        var rows = [['原名', '新名', '大小', '位置']].concat(pairs.map(function (p) {
          return [p.from.name, p.to, p.from.size, p.from.path];
        }));
        var csv = '\ufeff' + rows.map(function (r) {
          return r.map(function (c) { return '"' + String(c).replace(/"/g, '""') + '"'; }).join(',');
        }).join('\r\n');
        var blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
        var a = el('a', { href: URL.createObjectURL(blob), download: '改名清单.csv' });
        document.body.appendChild(a); a.click();
        setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 800);
      }
    }
  };

  /* ============ 工具 2：图片批量压缩 ============ */
  var compressTool = {
    name: '图片压缩',
    desc: '一批图片压小省空间。绝不动你的原图，压好的另存到新文件夹。',
    render: function (panel) {
      var files = [];
      var quality = 0.8;
      var maxW = 0;
      var format = 'image/jpeg';

      panel.appendChild(el('div.card', {}, [
        el('h2', { text: '1. 选图片' }),
        el('p.desc', { text: '选文件夹或者直接选一堆图片。原图只读不写。' }),
        dropZone('选一个文件夹', async function () {
          var h = await pickDir();
          if (!h) return;
          var out = [];
          await walk(h, '', out, { depth: 0 });
          files = out.filter(function (f) { return /\.(jpe?g|png|webp|bmp)$/i.test(f.name); });
          render();
        }),
        el('div.mt', {}, [el('input', { type: 'file', accept: 'image/*', multiple: 'multiple', onchange: function (e) {
          files = Array.prototype.map.call(e.target.files, function (f) {
            return { name: f.name, path: f.name, file: f, size: f.size, mtime: f.lastModified };
          });
          render();
        } })]),
        el('div.mt', { id: 'cList' })
      ]));

      panel.appendChild(el('div.card', {}, [
        el('h2', { text: '2. 参数' }),
        el('div.row', {}, [
          el('div', { style: { flex: '1 1 220px' } }, [
            el('label.fl', { text: '输出格式' }),
            el('select', { onchange: function () { format = this.value; } }, [
              el('option', { value: 'image/jpeg', text: 'JPG（兼容性最好）' }),
              el('option', { value: 'image/webp', text: 'WebP（更小）' })
            ])
          ]),
          el('div', { style: { flex: '1 1 220px' } }, [
            el('label.fl', { text: '质量（0.5 很小 / 0.9 很清晰）' }),
            el('input', { type: 'range', min: '0.3', max: '0.95', step: '0.05', value: '0.8', oninput: function () {
              quality = parseFloat(this.value);
              $('#qLabel').textContent = quality.toFixed(2);
            } })
          ]),
          el('div', { style: { flex: '1 1 200px' } }, [
            el('label.fl', { text: '最长边（0 = 不缩放）' }),
            el('input', { type: 'number', value: '0', min: '0', placeholder: '例如 1920', oninput: function () { maxW = parseInt(this.value, 10) || 0; } })
          ])
        ]),
        el('p.tiny.dim.mt', { text: '当前质量：<span id="qLabel">0.80</span>' }),
        el('div.row.mt', {}, [
          el('button.btn.pri', { text: '开始压缩', onclick: run }),
          el('button.btn', { text: '全部下载为 ZIP', onclick: downloadZip })
        ]),
        el('div.msg', { id: 'msg' })
      ]));

      var results = [];

      function render() {
        var box = $('#cList');
        clear(box);
        if (!files.length) { box.appendChild(el('div.empty', { text: '还没选图片' })); return; }
        box.appendChild(el('p.tiny.muted', { text: '共 ' + files.length + ' 张，总大小 ' + fmtSize(files.reduce(function (a, f) { return a + f.size; }, 0)) }));
      }
      render();

      async function compressOne(f) {
        var bmp = await createImageBitmap(f.file);
        var w = bmp.width, h = bmp.height;
        if (maxW && Math.max(w, h) > maxW) {
          var k = maxW / Math.max(w, h);
          w = Math.round(w * k); h = Math.round(h * k);
        }
        var cv = document.createElement('canvas');
        cv.width = w; cv.height = h;
        var ctx = cv.getContext('2d');
        if (format === 'image/jpeg') {
          ctx.fillStyle = '#fff';
          ctx.fillRect(0, 0, w, h);
        }
        ctx.drawImage(bmp, 0, 0, w, h);
        bmp.close && bmp.close();
        var blob = await new Promise(function (res) { cv.toBlob(res, format, quality); });
        return { name: f.name.replace(/\.[^.]+$/, '') + (format === 'image/jpeg' ? '.jpg' : '.webp'), blob: blob, orig: f.size };
      }

      async function run() {
        clearMsg();
        if (!files.length) return msg('warn', '先选图片');
        results = [];
        msg('ok', '正在处理…');
        var totalBefore = 0, totalAfter = 0;
        for (var i = 0; i < files.length; i++) {
          try {
            var r = await compressOne(files[i]);
            results.push(r);
            totalBefore += r.orig; totalAfter += r.blob.size;
          } catch (e) { /* 单张失败不影响其他 */ }
          if ((i + 1) % 10 === 0 || i === files.length - 1) {
            msg('ok', '处理中 ' + (i + 1) + '/' + files.length);
          }
        }
        var saved = totalBefore > 0 ? Math.round((1 - totalAfter / totalBefore) * 100) : 0;
        msg('ok', '完成：' + results.length + ' 张，' + fmtSize(totalBefore) + ' → ' + fmtSize(totalAfter) + '，省了 ' + saved + '%');
      }

      async function downloadZip() {
        if (!results.length) return msg('warn', '先点「开始压缩」');
        msg('ok', '正在打包…');
        var zip = await makeZip(results.map(function (r) {
          return { name: r.name, blob: r.blob };
        }));
        var a = el('a', { href: URL.createObjectURL(zip), download: '压缩图片.zip' });
        document.body.appendChild(a); a.click();
        setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1500);
      }
    }
  };

  /* 简易 ZIP 打包（store 模式，不压缩） */
  async function makeZip(entries) {
    var enc = new TextEncoder();
    var parts = [], central = [], offset = 0;
    var dosTime = 0, dosDate = 0x21;
    for (var i = 0; i < entries.length; i++) {
      var nameBytes = enc.encode(entries[i].name);
      var data = new Uint8Array(await entries[i].blob.arrayBuffer());
      var crc = crc32(data);
      var lh = new Uint8Array(30 + nameBytes.length);
      var dv = new DataView(lh.buffer);
      dv.setUint32(0, 0x04034b50, true);
      dv.setUint16(4, 20, true);
      dv.setUint16(6, 0, true);
      dv.setUint16(8, 0, true);
      dv.setUint16(10, dosTime, true);
      dv.setUint16(12, dosDate, true);
      dv.setUint32(14, crc, true);
      dv.setUint32(18, data.length, true);
      dv.setUint32(22, data.length, true);
      dv.setUint16(26, nameBytes.length, true);
      dv.setUint16(28, 0, true);
      lh.set(nameBytes, 30);
      parts.push(lh, data);

      var ch = new Uint8Array(46 + nameBytes.length);
      var cv = new DataView(ch.buffer);
      cv.setUint32(0, 0x02014b50, true);
      cv.setUint16(4, 20, true);
      cv.setUint16(6, 20, true);
      cv.setUint16(10, dosTime, true);
      cv.setUint16(12, dosDate, true);
      cv.setUint32(16, crc, true);
      cv.setUint32(20, data.length, true);
      cv.setUint32(24, data.length, true);
      cv.setUint16(28, nameBytes.length, true);
      cv.setUint32(42, offset, true);
      ch.set(nameBytes, 46);
      central.push(ch);
      offset += lh.length + data.length;
    }
    var cdSize = central.reduce(function (a, c) { return a + c.length; }, 0);
    var end = new Uint8Array(22);
    var ev = new DataView(end.buffer);
    ev.setUint32(0, 0x06054b50, true);
    ev.setUint16(8, entries.length, true);
    ev.setUint16(10, entries.length, true);
    ev.setUint32(12, cdSize, true);
    ev.setUint32(16, offset, true);
    var all = parts.concat(central, [end]);
    var totalLen = all.reduce(function (a, c) { return a + c.length; }, 0);
    var buf = new Uint8Array(totalLen);
    var p = 0;
    all.forEach(function (a) { buf.set(a, p); p += a.length; });
    return new Blob([buf], { type: 'application/zip' });
  }
  var CRC_TABLE = null;
  function crc32(buf) {
    if (!CRC_TABLE) {
      CRC_TABLE = new Uint32Array(256);
      for (var n = 0; n < 256; n++) {
        var c = n;
        for (var k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
        CRC_TABLE[n] = c >>> 0;
      }
    }
    var crc = 0xFFFFFFFF;
    for (var i = 0; i < buf.length; i++) crc = CRC_TABLE[(crc ^ buf[i]) & 0xFF] ^ (crc >>> 8);
    return (crc ^ 0xFFFFFFFF) >>> 0;
  }

  /* ============ 工具 3：重复文件查找 ============ */
  var dupTool = {
    name: '重复文件查找',
    desc: '扫出哪些文件内容重复。只出报告，一个都不删。你自己决定怎么处理。',
    render: function (panel) {
      var files = [];
      var minSize = 1;

      panel.appendChild(el('div.card', {}, [
        el('h2', { text: '选一个文件夹' }),
        el('p.desc', { text: '按文件内容算哈希判定重复，不靠文件名。' }),
        dropZone('选一个文件夹', async function () {
          var h = await pickDir();
          if (!h) return;
          files = [];
          var out = [];
          await walk(h, '', out, { depth: 0 });
          files = out;
          msg('ok', '扫到 ' + files.length + ' 个文件，点下面开始比对');
        }),
        el('div.mt', {}, [el('input', { type: 'file', multiple: 'multiple', onchange: function (e) {
          files = Array.prototype.map.call(e.target.files, function (f) {
            return { name: f.name, path: f.name, file: f, size: f.size, mtime: f.lastModified };
          });
          msg('ok', '已选 ' + files.length + ' 个文件');
        } })]),
        el('div.row.mt', {}, [
          el('div', { style: { flex: '0 1 200px' } }, [
            el('label.fl', { text: '忽略小于（KB）的文件' }),
            el('input', { type: 'number', value: '1', min: '0', oninput: function () { minSize = parseInt(this.value, 10) || 0; } })
          ]),
          el('button.btn.pri', { text: '开始查重', onclick: scan })
        ]),
        el('div.msg', { id: 'msg' })
      ]));

      var outCard = el('div.card', {}, [el('h2', { text: '报告' }), el('div', { id: 'dupOut' })]);
      panel.appendChild(outCard);

      async function hashFile(f) {
        var buf = await f.file.arrayBuffer();
        var h = await crypto.subtle.digest('SHA-256', buf);
        var a = new Uint8Array(h);
        var s = '';
        for (var i = 0; i < a.length; i++) s += a[i].toString(16).padStart(2, '0');
        return s;
      }

      async function scan() {
        clearMsg();
        if (!files.length) return msg('warn', '先选文件夹或文件');
        var cands = files.filter(function (f) { return f.size >= minSize * 1024; });
        msg('ok', '正在算哈希… 0/' + cands.length);
        var bySize = {};
        cands.forEach(function (f) { (bySize[f.size] = bySize[f.size] || []).push(f); });
        var groups = Object.keys(bySize).filter(function (k) { return bySize[k].length > 1; });
        var dupGroups = [];
        var checked = 0;
        for (var gi = 0; gi < groups.length; gi++) {
          var g = bySize[groups[gi]];
          var byHash = {};
          for (var i = 0; i < g.length; i++) {
            try {
              var h = await hashFile(g[i]);
              (byHash[h] = byHash[h] || []).push(g[i]);
            } catch (e) {}
            checked++;
            if (checked % 20 === 0) msg('ok', '正在算哈希… ' + checked + '/' + cands.length);
          }
          Object.keys(byHash).forEach(function (h) {
            if (byHash[h].length > 1) dupGroups.push(byHash[h]);
          });
        }
        render(dupGroups);
        msg('ok', '查完了：发现 ' + dupGroups.length + ' 组重复，共可清理 ' + wasted(dupGroups));
      }

      function wasted(groups) {
        var sum = 0;
        groups.forEach(function (g) {
          g.forEach(function (f, i) { if (i > 0) sum += f.size; });
        });
        return fmtSize(sum);
      }

      function render(groups) {
        var box = $('#dupOut');
        clear(box);
        if (!groups.length) { box.appendChild(el('div.empty', { text: '没发现重复文件，干净。' })); return; }
        var tb = el('table.tbl');
        tb.appendChild(el('tr', {}, [el('th', { text: '文件' }), el('th', { class: 'num', text: '大小' }), el('th', { text: '修改时间' }), el('th', { text: '位置' })]));
        groups.forEach(function (g, gi) {
          tb.appendChild(el('tr', {}, [el('td', { colspan: '4', style: { color: 'var(--warn)', fontWeight: '600' } }, [
            '第 ' + (gi + 1) + ' 组 · ' + g.length + ' 个相同 · 每份 ' + fmtSize(g[0].size)
          ])]));
          g.forEach(function (f, i) {
            tb.appendChild(el('tr', {}, [
              el('td', { text: f.name, style: i > 0 ? { color: 'var(--bad)' } : {} }),
              el('td', { class: 'num', text: fmtSize(f.size) }),
              el('td', { class: 'dim tiny', text: fmtTime(f.mtime) }),
              el('td', { class: 'dim tiny', text: f.path })
            ]));
          });
        });
        box.appendChild(el('div.scroll', {}, [tb]));
        box.appendChild(el('p.tiny.dim.mt', { text: '红色的是除第一份以外的重复文件。重复内容：点文件所在的位置手动去删就行，这个工具不会替你动任何文件。' }));
      }
    }
  };

  /* ============ 工具 4：大文件排行 ============ */
  var bigTool = {
    name: '大文件排行',
    desc: '列出文件夹里最大的文件，看看空间被谁吃了。只看不改。',
    render: function (panel) {
      var files = [];
      var topN = 20;

      panel.appendChild(el('div.card', {}, [
        el('h2', { text: '选一个文件夹' }),
        el('p.desc', { text: '可以选子文件夹，把所有子目录一起算进去。' }),
        dropZone('选一个文件夹', async function () {
          var h = await pickDir();
          if (!h) return;
          var out = [];
          await walk(h, '', out, { depth: 0 });
          files = out;
          msg('ok', '扫到 ' + files.length + ' 个文件');
        }),
        el('div.row.mt', {}, [
          el('div', { style: { flex: '0 1 170px' } }, [
            el('label.fl', { text: '显示前几个' }),
            el('input', { type: 'number', value: '20', min: '5', oninput: function () { topN = parseInt(this.value, 10) || 20; } })
          ]),
          el('button.btn.pri', { text: '生成排行', onclick: run })
        ]),
        el('div.msg', { id: 'msg' })
      ]));

      panel.appendChild(el('div.card', {}, [el('h2', { text: '排行' }), el('div', { id: 'bigOut' })]));

      function run() {
        clearMsg();
        if (!files.length) return msg('warn', '先选文件夹');
        var sorted = files.slice().sort(function (a, b) { return b.size - a.size; });
        var total = files.reduce(function (a, f) { return a + f.size; }, 0);
        var top = sorted.slice(0, topN);
        var box = $('#bigOut');
        clear(box);

        var stat = el('div', {}, [
          el('div.kv', {}, [el('span.muted', { text: '文件总数' }), el('b', { text: String(files.length) })]),
          el('div.kv', {}, [el('span.muted', { text: '总大小' }), el('b', { text: fmtSize(total) })]),
          el('div.kv', {}, [el('span.muted', { text: '前 ' + top.length + ' 个占了' }), el('b', {
            text: total ? Math.round(top.reduce(function (a, f) { return a + f.size; }, 0) / total * 100) + '%' : '0%'
          })])
        ]);
        box.appendChild(stat);

        var max = top.length ? top[0].size : 1;
        var tb = el('table.tbl');
        tb.appendChild(el('tr', {}, [el('th', { text: '#' }), el('th', { text: '文件' }), el('th', { class: 'num', text: '大小' }), el('th', { text: '占比' })]));
        top.forEach(function (f, i) {
          tb.appendChild(el('tr', {}, [
            el('td', { class: 'dim', text: String(i + 1) }),
            el('td', {}, [
              el('div', { text: f.name }),
              el('div.tiny.dim', { text: f.path })
            ]),
            el('td', { class: 'num', text: fmtSize(f.size) }),
            el('td', { style: { width: '160px' } }, [
              el('div.bar', {}, [el('i', { style: { width: (f.size / max * 100).toFixed(1) + '%' } })])
            ])
          ]));
        });
        box.appendChild(el('div.scroll.mt', {}, [tb]));
        msg('ok', '好了。空间主要被上面这几个吃了。');
      }
    }
  };

  /* ============ 工具 5：本地笔记搜索 ============ */
  var searchTool = {
    name: '笔记搜索',
    desc: '在你自己的一堆 txt / md 里搜关键词，直接跳到那一段。',
    render: function (panel) {
      var files = [];
      var keyword = '';
      var results = [];

      panel.appendChild(el('div.card', {}, [
        el('h2', { text: '选一个文件夹' }),
        el('p.desc', { text: '只读文本类文件（txt / md / json / log / csv）。' }),
        dropZone('选一个文件夹', async function () {
          var h = await pickDir();
          if (!h) return;
          var out = [];
          await walk(h, '', out, { depth: 0 });
          files = out.filter(function (f) { return /\.(txt|md|markdown|json|log|csv|ini|conf|yaml|yml|js|py|java|c|cpp|h|html|css)$/i.test(f.name); });
          msg('ok', '扫到 ' + files.length + ' 个文本文件');
        }),
        el('div.row.mt', {}, [
          el('div', { style: { flex: '1 1 260px' } }, [
            el('label.fl', { text: '搜什么' }),
            el('input', { type: 'text', placeholder: '关键词', oninput: function () { keyword = this.value; } })
          ]),
          el('button.btn.pri', { text: '搜索', onclick: run })
        ]),
        el('div.msg', { id: 'msg' })
      ]));

      panel.appendChild(el('div.card', {}, [el('h2', { text: '结果' }), el('div', { id: 'sOut' })]));

      async function run() {
        clearMsg();
        if (!files.length) return msg('warn', '先选文件夹');
        if (!keyword.trim()) return msg('warn', '输入关键词');
        var kw = keyword.toLowerCase();
        results = [];
        for (var i = 0; i < files.length; i++) {
          try {
            var text = await files[i].file.text();
            var lines = text.split(/\r?\n/);
            for (var li = 0; li < lines.length; li++) {
              if (lines[li].toLowerCase().indexOf(kw) >= 0) {
                results.push({ file: files[i], line: li + 1, text: lines[li].trim() });
                if (results.length >= 800) break;
              }
            }
          } catch (e) {}
          if (results.length >= 800) break;
        }
        render();
        msg('ok', '找到 ' + results.length + ' 处');
      }

      function render() {
        var box = $('#sOut');
        clear(box);
        if (!results.length) { box.appendChild(el('div.empty', { text: '没有匹配的内容' })); return; }
        var byFile = {};
        results.forEach(function (r) {
          var k = r.file.path;
          (byFile[k] = byFile[k] || []).push(r);
        });
        Object.keys(byFile).forEach(function (k) {
          var g = byFile[k];
          var card = el('div', { style: { marginBottom: '16px' } });
          card.appendChild(el('div.row', { style: { marginBottom: '8px' } }, [
            el('b', { text: g[0].file.name }),
            el('span.tiny.dim', { text: g.length + ' 处 · ' + g[0].file.path })
          ]));
          var tb = el('table.tbl');
          g.slice(0, 100).forEach(function (r) {
            tb.appendChild(el('tr', {}, [
              el('td', { class: 'dim tiny', style: { width: '52px' }, text: 'L' + r.line }),
              el('td', { class: 'mono tiny' }, [
                el('span', { html: highlight(r.text, keyword) })
              ])
            ]));
          });
          card.appendChild(el('div.scroll', {}, [tb]));
          box.appendChild(card);
        });
      }

      function highlight(text, kw) {
        var esc = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        var re = new RegExp('(' + kw.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + ')', 'ig');
        return esc.replace(re, '<mark style="background:var(--acc);color:#fff;border-radius:3px">$1</mark>');
      }
    }
  };

  /* ============ 装配 ============ */
  var TOOLS = [renameTool, compressTool, dupTool, bigTool, searchTool];
  var tabs = $('#tabs'), panel = $('#panel');
  var current = 0;

  TOOLS.forEach(function (t, i) {
    tabs.appendChild(el('button' + (i === 0 ? '.on' : ''), {
      text: t.name, onclick: function () {
        Array.prototype.forEach.call(tabs.children, function (b) { b.className = ''; });
        this.className = 'on';
        current = i;
        clear(panel);
        t.render(panel);
      }
    }));
  });

  var badge = $('#browserBadge');
  badge.textContent = supportsFS() ? '浏览器支持直接操作文件夹' : '浏览器不支持直接操作文件夹';
  badge.style.color = supportsFS() ? 'var(--ok)' : 'var(--warn)';
  $('#hostNote').textContent = supportsFS()
    ? '（当前浏览器可以直接读写你选的文件夹，功能完整。）'
    : '（当前浏览器不能直接操作文件夹，请换电脑上的 Chrome 或 Edge。）';

  clear(panel);
  TOOLS[0].render(panel);
})();
