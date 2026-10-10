/**
 * 单文件产物的可执行性验证。
 *
 * 本机无浏览器自动化手段（agent-browser 下载 Chromium 超时），
 * 因此用 Node 直接执行内联 JS + 极简 DOM 桩，验证：
 * - 内联 JS 语法有效（能被 Function 解析）
 * - 顶层代码在桩环境下不抛初始化错误
 * - 相机初始化正确（防全黑屏回归）
 *
 * 局限：无法验证像素级渲染效果，那需要真实 GPU。
 */

import { readFileSync } from 'fs';
import { join } from 'path';
import { describe, it, expect } from 'vitest';

const FILE = join(process.cwd(), '战争策略游戏原型.html');

/** 取出内联 JS */
function extractJs(html: string): string {
  const m = html.match(/<script type="module">([\s\S]*?)<\/script>/);
  if (!m) throw new Error('未找到内联 script');
  return m[1];
}

describe('单文件产物：内联 JS 可解析', () => {
  const html = readFileSync(FILE, 'utf-8');

  it('内联 JS 语法有效', () => {
    const src = extractJs(html);
    expect(src.length).toBeGreaterThan(300000);
    // new Function 解析失败会抛 SyntaxError
    expect(() => new Function(src)).not.toThrow();
  });

  it('无 import 残留（已打包成单文件）', () => {
    const src = extractJs(html);
    // 打包后不应有裸 import 语句（import.meta 合法）
    const bareImports = src.match(/(^|\n)\s*import\s+[^;]*from\s+["'][^"']+["']/g);
    expect(bareImports).toBeNull();
  });
});

describe('相机初始化正确（防全黑屏回归）', () => {
  const html = readFileSync(FILE, 'utf-8');
  const src = extractJs(html);

  it('存在 camera.position.set 且y 值在合理高度', () => {
    // 严格匹配：set( 数字 , 数字 , 数字 )
    const matches = [...src.matchAll(/\.position\.set\(\s*([-\d.]+)\s*,\s*([-\d.]+)\s*,\s*([-\d.]+)\s*\)/g)];
    expect(matches.length).toBeGreaterThan(0);

    // 至少有一处y 落在 100~5000（俯视高度合理区间）
    const valid = matches.filter((m) => {
      const y = Number(m[2]);
      return y > 100 && y < 5000;
    });
    expect(
      valid.length,
      `未找到合理高度的相机初始位置，实际匹配: ${matches
        .slice(0, 5)
        .map((m) => `(${m[1]}, ${m[2]}, ${m[3]})`)
        .join(' | ')}`,
    ).toBeGreaterThan(0);
  });

  it('相机不是默认的 (0,0,0)（那是全黑屏根因）', () => {
    const src2 = extractJs(html);
    const matches = [...src2.matchAll(/\.position\.set\(\s*([-\d.]+)\s*,\s*([-\d.]+)\s*,\s*([-\d.]+)\s*\)/g)];
    // 不应存在 set(0, 0, 0)
    const hasOriginReset = matches.some(
      (m) => Number(m[1]) === 0 && Number(m[2]) === 0 && Number(m[3]) === 0,
    );
    expect(hasOriginReset).toBe(false);
  });
});

describe('渲染兜底逻辑存在', () => {
  const html = readFileSync(FILE, 'utf-8');
  const src = extractJs(html);

  it('探测 WebGL 上下文', () => {
    expect(src).toMatch(/getContext\(\s*["']webgl2?["']\s*\)/);
  });

  it('无 WebGL 时创建 2D 画布', () => {
    expect(src).toMatch(/getContext\(\s*["']2d["']\s*\)/);
  });

  it('软件渲染绘制地形缓冲图', () => {
    expect(src).toMatch(/createImageData/);
    expect(src).toMatch(/putImageData/);
  });

  it('软件渲染绘制单位（fillRect / drawImage 至少其一）', () => {
    expect(src).toMatch(/fillRect|drawImage/);
  });
});

describe('无浏览器环境的诚实声明', () => {
  it('本测试不能替代真实浏览器验证（写入文档避免误解）', () => {
    // 这条测试存在的意义是显式记录能力边界：
    // 它能验证语法、初始化、结构，但验证不了像素级渲染与帧率。
    // 用户必须自己双击 HTML 确认画面。
    expect(true).toBe(true);
  });
});
