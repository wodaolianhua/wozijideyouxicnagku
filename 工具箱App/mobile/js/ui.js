/* 通用 UI 工具函数 */
(function (global) {
  'use strict';

  var UI = {};

  /** 创建元素：el('div.card', {onclick:fn}, [child1, 'text']) */
  UI.el = function (sel, attrs, kids) {
    var parts = String(sel).split('.');
    var tag = parts[0] || 'div';
    var e = document.createElement(tag);
    for (var i = 1; i < parts.length; i++) e.classList.add(parts[i]);
    if (attrs) {
      Object.keys(attrs).forEach(function (k) {
        var v = attrs[k];
        if (v === null || v === undefined || v === false) return;
        if (k === 'html') e.innerHTML = v;
        else if (k === 'text') e.textContent = v;
        else if (k === 'style' && typeof v === 'object') Object.assign(e.style, v);
        else if (k.slice(0, 2) === 'on' && typeof v === 'function') e.addEventListener(k.slice(2), v);
        else if (k === 'value') e.value = v;
        else if (v === true) e.setAttribute(k, '');
        else e.setAttribute(k, v);
      });
    }
    UI.kids(e, kids);
    return e;
  };

  UI.kids = function (e, kids) {
    if (kids === null || kids === undefined) return e;
    if (!Array.isArray(kids)) kids = [kids];
    kids.forEach(function (k) {
      if (k === null || k === undefined || k === false) return;
      e.appendChild(typeof k === 'string' || typeof k === 'number' ? document.createTextNode(String(k)) : k);
    });
    return e;
  };

  UI.clear = function (e) { while (e.firstChild) e.removeChild(e.firstChild); return e; };

  /** 吐司提示 */
  var toastTimer = null;
  UI.toast = function (msg) {
    var t = document.getElementById('toast');
    if (!t) return;
    t.textContent = msg;
    t.classList.add('on');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { t.classList.remove('on'); }, 2000);
  };

  /** 底部弹层：返回 close 函数 */
  UI.sheet = function (content) {
    var wrap = document.getElementById('sheet');
    var body = document.getElementById('sheetBody');
    UI.clear(body);
    UI.kids(body, content);
    wrap.hidden = false;
    function close() { wrap.hidden = true; UI.clear(body); }
    wrap.querySelectorAll('[data-close]').forEach(function (n) {
      n.onclick = close;
    });
    return close;
  };

  UI.closeSheet = function () {
    var wrap = document.getElementById('sheet');
    if (wrap) { wrap.hidden = true; UI.clear(document.getElementById('sheetBody')); }
  };

  /** 确认框 */
  UI.confirm = function (msg, okText) {
    return new Promise(function (res) {
      var close = UI.sheet([
        UI.el('p.muted', { text: msg, style: { margin: '0 0 16px', lineHeight: '1.6' } }),
        UI.el('div.row', {}, [
          UI.el('button.btn.grow', { text: '取消', onclick: function () { close(); res(false); } }),
          UI.el('button.btn.danger.grow', { text: okText || '确定', onclick: function () { close(); res(true); } })
        ])
      ]);
    });
  };

  /** 数字格式化 */
  UI.money = function (n) {
    var v = Math.round((Number(n) || 0) * 100) / 100;
    return v.toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  };

  UI.dateKey = function (d) {
    d = d || new Date();
    return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
  };

  UI.monthKey = function (d) {
    d = d || new Date();
    return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0');
  };

  UI.dayName = function (d) {
    return ['日', '一', '二', '三', '四', '五', '六'][d.getDay()];
  };

  /** 简易 csv 导出 */
  UI.exportFile = function (filename, text, mime) {
    var blob = new Blob([text], { type: mime || 'text/plain;charset=utf-8' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url; a.download = filename;
    document.body.appendChild(a); a.click();
    setTimeout(function () { URL.revokeObjectURL(url); a.remove(); }, 1000);
  };

  UI.readFile = function (accept) {
    return new Promise(function (res) {
      var i = document.createElement('input');
      i.type = 'file'; i.accept = accept || '';
      i.onchange = function () { res(i.files[0] || null); };
      i.click();
    });
  };

  global.UI = UI;
})(window);
