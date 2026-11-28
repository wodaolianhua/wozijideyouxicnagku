/* 工具注册表：必须在各工具文件之前加载 */
(function (global) {
  'use strict';

  var TOOLS = [];

  var Tool = {
    /** 注册一个工具 */
    register: function (def) {
      if (!def || !def.id) return;
      TOOLS.push(def);
    },
    all: function () { return TOOLS; },
    get: function (id) {
      for (var i = 0; i < TOOLS.length; i++) if (TOOLS[i].id === id) return TOOLS[i];
      return null;
    },
    /** 按分组取 */
    byGroup: function (key) {
      return TOOLS.filter(function (t) { return t.group === key; });
    }
  };

  global.Tool = Tool;
})(window);
