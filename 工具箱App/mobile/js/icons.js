/* 工具图标（内联 SVG，无外部依赖） */
(function (global) {
  'use strict';

  var P = {
    money: '<path d="M3 7h18v10H3z"/><path d="M12 10.5v3"/><path d="M7.5 12h9"/>',
    habit: '<path d="M4 6h16v15H4z"/><path d="M4 10h16"/><path d="M8 3v4M16 3v4"/><path d="m9 15 2 2 4-4"/>',
    todo: '<path d="M9 6h11M9 12h11M9 18h11"/><path d="m3 6 1.5 1.5L7.5 4.5"/><path d="m3 12 1.5 1.5L7.5 10.5"/><path d="m3 18 1.5 1.5L7.5 16.5"/>',
    timer: '<circle cx="12" cy="13" r="8"/><path d="M12 9v4l2.5 1.5M9 2h6"/>',
    pomo: '<circle cx="12" cy="13" r="8"/><path d="M12 9v4M9 2h6M18.5 7.5 20 6"/>',
    wheel: '<circle cx="12" cy="12" r="9"/><path d="M12 3v9l6.4 6.4M12 12l-6.4 6.4"/>',
    vault: '<rect x="3" y="5" width="18" height="14" rx="2.5"/><circle cx="11" cy="12" r="3.2"/><path d="M11 8.8V7M15.2 12H17"/>',
    quote: '<path d="M4 4h16v12H8l-4 4z"/><path d="M8 9h8M8 12h5"/>',
    conv: '<rect x="3" y="4" width="18" height="16" rx="2.5"/><path d="M7 9h4M7 13h7M15 9h2"/>',
    gacha: '<rect x="3" y="8" width="18" height="11" rx="2"/><path d="M2 8h20v3H2z"/><path d="M12 8v11"/><circle cx="12" cy="5" r="2.5"/>',
    pixel: '<path d="M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4z"/><path d="M14 14h6v6h-6z" stroke-dasharray="2 2"/>',
    stopwatch: '<circle cx="12" cy="13" r="8"/><path d="M12 9v4l3 2M9 2h6M18.5 7.5 20 6M6 6h6"/>',
    home: '<path d="M4 11 12 4l8 7"/><path d="M6 10v10h12V10"/><path d="M10 20v-6h4v6"/>',
    gear: '<circle cx="12" cy="12" r="3.2"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9 7 7M17 17l2.1 2.1M19.1 4.9 17 7M7 17l-2.1 2.1"/>',
    back: '<path d="M15 5l-7 7 7 7"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    trash: '<path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13"/>',
    down: '<path d="M12 4v11M7 11l5 5 5-5M5 20h14"/>',
    up: '<path d="M12 20V9M7 13l5-5 5 5M5 4h14"/>',
    clock: '<circle cx="12" cy="12" r="8.5"/><path d="M12 7v5l3.5 2"/>',
    cal: '<path d="M4 6h16v15H4z"/><path d="M4 10h16M8 3v4M16 3v4"/>',
    dice: '<rect x="3" y="3" width="18" height="18" rx="3"/><circle cx="8.5" cy="8.5" r="1.4" fill="currentColor" stroke="none"/><circle cx="15.5" cy="15.5" r="1.4" fill="currentColor" stroke="none"/><circle cx="12" cy="12" r="1.4" fill="currentColor" stroke="none"/>',
    bulb: '<path d="M9 18h6M10 21h4"/><path d="M12 3a6 6 0 0 0-3.5 10.9V15h7v-1.1A6 6 0 0 0 12 3z"/>',
    lock: '<rect x="4" y="10" width="16" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/>',
    copy: '<rect x="8" y="8" width="13" height="13" rx="2"/><path d="M16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3"/>',
    refresh: '<path d="M20 11a8 8 0 1 0-.6 4"/><path d="M20 4v7h-7"/>',
    chart: '<path d="M4 20V9M10 20V4M16 20v-7M22 20H2"/>',
    folder: '<path d="M3 7h6l2 2h10v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
    search: '<circle cx="11" cy="11" r="6.5"/><path d="m16 16 4 4"/>',
    img: '<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="8.5" cy="9.5" r="1.6"/><path d="m4 18 5-5 4 4 3-3 4 4"/>',
    save: '<path d="M5 3h11l3 3v15H5z"/><path d="M8 3v6h7V3M8 21v-7h8v7"/>',
    key: '<circle cx="8" cy="14" r="4"/><path d="m11 11 9-9 2 2-2 2 2 2-2 2-3-3-2 2"/>'
  };

  global.Icons = {
    /** 返回 <svg> 字符串 */
    svg: function (name, cls) {
      return '<svg viewBox="0 0 24 24" class="' + (cls || '') + '" aria-hidden="true">' + (P[name] || P.gear) + '</svg>';
    },
    /** 返回元素 */
    el: function (name, cls) {
      var d = document.createElement('span');
      d.innerHTML = global.Icons.svg(name, cls);
      return d.firstChild;
    }
  };
})(window);
