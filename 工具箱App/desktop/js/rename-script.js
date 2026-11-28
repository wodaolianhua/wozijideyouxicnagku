/* 批量改名：生成可执行的 PowerShell 脚本
 *
 * 为什么用脚本：浏览器的 File System Access API 只能新建/写入文件，
 * 没有「重命名」这个接口。所以这里生成一个 .cmd + .ps1 放进目标文件夹，
 * 用户双击运行就完成真改名 —— 文件还在原地，只是名字变了。
 *
 * 这个模块不碰 DOM，单独可测。
 */
(function (global) {
  'use strict';

  function esc(s) {
    /* PowerShell 双引号字符串里要转义的反引号和 $ */
    return String(s).replace(/`/g, '``').replace(/\$/g, '`$').replace(/"/g, '`"');
  }

  /** 按所在目录分组 */
  function groupByDir(changed) {
    var byDir = {};
    changed.forEach(function (p) {
      var d = p.from.path.slice(0, p.from.path.length - p.from.name.length);
      (byDir[d] = byDir[d] || []).push(p);
    });
    return byDir;
  }

  /** 生成单个目录的 ps1 内容 */
  function buildDirScript(dir, group, rules, idx) {
    var sl = [];
    sl.push('$ErrorActionPreference = "Continue"');
    sl.push('chcp 65001 > $null');
    /* 脚本就放在根目录，$PSScriptRoot 就是根；子目录用相对路径拼 */
    var dirArg = dir ? dir.replace(/\/+$/, '').replace(/\//g, '\\') : '.';
    sl.push('Set-Location -LiteralPath (Join-Path -Path $PSScriptRoot -ChildPath "' + esc(dirArg) + '")');
    sl.push('$backup = @()');
    group.forEach(function (p, j) {
      sl.push('try {');
      sl.push('  if (Test-Path -LiteralPath "' + esc(p.from.name) + '") {');
      sl.push('    $backup += [PSCustomObject]@{ Dir = "' + esc(dir) + '"; Old = "' + esc(p.from.name) + '"; New = "' + esc(p.to) + '" }');
      sl.push('    Rename-Item -LiteralPath "' + esc(p.from.name) + '" -NewName "' + esc(p.to) + '"');
      sl.push('    Write-Host ("[{0}/{1}] {2}" -f ' + (j + 1) + ', ' + group.length + ', "' + esc(p.to) + '")');
      sl.push('  } else { Write-Host ("跳过(找不到): ' + esc(p.from.name) + '") -ForegroundColor Yellow }');
      sl.push('} catch { Write-Host ("失败: ' + esc(p.from.name) + ' -> " + $_.Exception.Message) -ForegroundColor Red }');
    });
    sl.push('');
    sl.push('if ($backup.Count -gt 0) {');
    sl.push('  $out = Join-Path $PSScriptRoot ("改名备份_' + idx + '.csv")');
    sl.push('  $backup | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding UTF8');
    sl.push('}');
    return sl.join('\r\n');
  }

  /** 生成入口 cmd 内容 */
  function buildEntry(dirCount) {
    var lines = ['@echo off', 'chcp 65001 > nul', 'title 批量改名'];
    for (var i = 0; i < dirCount; i++) {
      lines.push('powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0_rename_' + i + '.ps1"');
    }
    lines.push('echo.');
    lines.push('powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0_merge.ps1"');
    lines.push('echo.');
    lines.push('pause');
    return lines.join('\r\n');
  }

  /** 生成合并脚本 */
  function buildMerge() {
    return [
      '$ErrorActionPreference = "Continue"',
      '$all = @()',
      'Get-ChildItem -LiteralPath $PSScriptRoot -Filter "改名备份_*.csv" -ErrorAction SilentlyContinue | ForEach-Object {',
      '  $all += Import-Csv -LiteralPath $_.FullName',
      '}',
      'if ($all.Count -gt 0) {',
      '  $all | Export-Csv -LiteralPath (Join-Path $PSScriptRoot "改名备份.csv") -NoTypeInformation -Encoding UTF8',
      '  Write-Host ("全部完成，共 {0} 个。原名清单在 改名备份.csv" -f $all.Count) -ForegroundColor Green',
      '} else { Write-Host "没有需要改名的文件" }',
      ''
    ].join('\r\n');
  }

  /** 生成 CSV 预览 */
  function buildCsv(changed) {
    var rows = changed.map(function (p) {
      var d = p.from.path.slice(0, p.from.path.length - p.from.name.length);
      return '"' + d + '","' + p.from.name + '","' + p.to + '"';
    });
    return '﻿目录,原名,新名\r\n' + rows.join('\r\n');
  }

  /**
   * 生成所有要写入的文件。
   * 返回 [{name, content}]，name 里的 '/' 表示子目录。
   */
  function buildFiles(changed, rules) {
    rules = rules || {};
    var byDir = groupByDir(changed);
    var dirs = Object.keys(byDir);
    var out = [];

    out.push({ name: '改名脚本.cmd', content: buildEntry(dirs.length) });
    out.push({ name: '改名预览.csv', content: buildCsv(changed) });

    dirs.forEach(function (d, i) {
      out.push({
        name: '_rename_' + i + '.ps1',
        content: buildDirScript(d, byDir[d], rules, i)
      });
    });
    out.push({ name: '_merge.ps1', content: buildMerge() });

    return out;
  }

  /** 把文件写进目录（File System Access API） */
  async function writeFiles(rootHandle, files) {
    for (var i = 0; i < files.length; i++) {
      var f = files[i];
      var dir = rootHandle;
      var parts = f.name.split('/');
      var fileName = parts.pop();
      for (var j = 0; j < parts.length; j++) {
        dir = await dir.getDirectoryHandle(parts[j], { create: true });
      }
      var fh = await dir.getFileHandle(fileName, { create: true });
      var w = await fh.createWritable();
      /* 带 BOM，PowerShell / cmd 读中文才不会乱码 */
      await w.write(new TextEncoder().encode('﻿' + f.content));
      await w.close();
    }
  }

  global.RenameScript = {
    buildFiles: buildFiles,
    writeFiles: writeFiles,
    buildDirScript: buildDirScript,
    buildEntry: buildEntry
  };

  if (typeof module !== 'undefined' && module.exports) module.exports = global.RenameScript;
})(typeof window !== 'undefined' ? window : globalThis);
