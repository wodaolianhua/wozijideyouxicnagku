/* 极简 IndexedDB 封装：按命名空间存 JSON 数据，全部留在本机，不上传任何地方。 */
(function (global) {
  'use strict';

  var DB_NAME = 'toolbox';
  var DB_VER = 1;
  var STORE = 'kv';
  var dbp = null;

  function open() {
    if (dbp) return dbp;
    dbp = new Promise(function (res, rej) {
      var req = indexedDB.open(DB_NAME, DB_VER);
      req.onupgradeneeded = function () {
        var db = req.result;
        if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE);
      };
      req.onsuccess = function () { res(req.result); };
      req.onerror = function () { rej(req.error); };
    });
    return dbp;
  }

  function key(ns, k) { return ns + '::' + k; }

  /** 读一个值：结果从 request 自己的 onsuccess 里拿 */
  function read(ns, k, dflt) {
    return open().then(function (db) {
      return new Promise(function (res, rej) {
        var t = db.transaction(STORE, 'readonly');
        var req = t.objectStore(STORE).get(key(ns, k));
        req.onsuccess = function () { res(req.result === undefined ? dflt : req.result); };
        req.onerror = function () { rej(req.error); };
      });
    }).catch(function () { return dflt; });
  }

  /** 写一个值：等事务真正完成才算数 */
  function write(ns, k, v) {
    return open().then(function (db) {
      return new Promise(function (res, rej) {
        var t = db.transaction(STORE, 'readwrite');
        t.objectStore(STORE).put(v, key(ns, k));
        t.oncomplete = function () { res(v); };
        t.onerror = function () { rej(t.error); };
        t.onabort = function () { rej(t.error); };
      });
    });
  }

  function remove(ns, k) {
    return open().then(function (db) {
      return new Promise(function (res, rej) {
        var t = db.transaction(STORE, 'readwrite');
        t.objectStore(STORE).delete(key(ns, k));
        t.oncomplete = function () { res(); };
        t.onerror = function () { rej(t.error); };
      });
    });
  }

  function allKeys() {
    return open().then(function (db) {
      return new Promise(function (res, rej) {
        var t = db.transaction(STORE, 'readonly');
        var req = t.objectStore(STORE).getAllKeys();
        req.onsuccess = function () { res(req.result || []); };
        req.onerror = function () { rej(req.error); };
      });
    }).catch(function () { return []; });
  }

  var Store = {
    /** 读取整个命名空间的对象（不存在返回 {}） */
    get: function (ns) {
      return read(ns, 'all', {}).then(function (v) {
        return (v && typeof v === 'object') ? v : {};
      });
    },

    /** 保存整个命名空间的对象 */
    set: function (ns, obj) {
      return write(ns, 'all', obj);
    },

    /** 读取单个键 */
    getItem: function (ns, k, dflt) {
      return read(ns, k, dflt);
    },

    /** 保存单个键 */
    setItem: function (ns, k, v) {
      return write(ns, k, v);
    },

    /** 删除单个键 */
    delItem: function (ns, k) {
      return remove(ns, k);
    },

    /** 列出全部键（调试用） */
    keys: allKeys
  };

  global.Store = Store;
})(window);
