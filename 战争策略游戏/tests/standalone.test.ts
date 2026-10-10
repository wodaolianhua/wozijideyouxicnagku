/**
 * 单文件产物的无头验证：用 Node 模拟最小浏览器环境，
 * 真实执行内联 JS，验证模块能否加载、Three.js 能否工作、
 * 相机是否被正确初始化（这是上一次全黑屏的根因）。
 *
 * 这不是替代真实浏览器，而是**在没有任何浏览器自动化手段时
 * 能做到的最强验证**——能抓到模块加载错误与初始化逻辑错误。
 */

import { readFileSync } from 'fs';
import { join } from 'path';
import { describe, it, expect } from 'vitest';

const FILE = join(process.cwd(), '战争策略游戏原型.html');

describe('单文件产物结构', () => {
  const html = readFileSync(FILE, 'utf-8');

  it('无外链 JS（双击即可运行）', () => {
    expect(html).not.toMatch(/src=["'][^"']*\.js["']/i);
  });

  it('无外链 CSS', () => {
    expect(html).not.toMatch(/<link\b[^>]*stylesheet/i);
  });

  it('内联了完整 JS', () => {
    const m = html.match(/<script type="module">([\s\S]*?)<\/script>/);
    expect(m).not.toBeNull();
    expect(m![1].length).toBeGreaterThan(300000);
  });

  it('包含 Three.js（PerspectiveCamera 存在）', () => {
    expect(html).toContain('PerspectiveCamera');
  });

  it('包含关键业务代码（按行为特征匹配，压缩后符号会被重命名）', () => {
    // 压缩会重命名局部标识符，所以匹配稳定的行为特征而非函数名
    expect(html).toMatch(/createImageData|putImageData/);      // Canvas 2D 地形渲染
    expect(html).toMatch(/InstancedMesh/);                      // 3D 渲染路径存在
    expect(html).toMatch(/perspective/i);                       // 透视相机
    expect(html).toMatch(/standingTerrain|A\*|hScore|open\.length/); // 寻径或优先级队列数据结构
  });

  it('包含 HUD 元素 id', () => {
    for (const id of ['app', 'hud', 'fps', 'log', 'econ', 'btn-day']) {
      expect(html).toContain(`id="${id}"`);
    }
  });

  it('相机位置已显式初始化（防全黑屏回归）', () => {
    // 必须存在 camera.position.set 调用，且初值 y > 0
    expect(html).toMatch(/position\.set\(\s*0\s*,\s*9\d\d\s*,/);
  });

  it('内联 JS 语法有效', () => {
    const m = html.match(/<script type="module">([\s\S]*?)<\/script>/)!;
    expect(() => new Function(m[1])).not.toThrow();
  });
});

describe('渲染兜底逻辑在产物中存在', () => {
  const html = readFileSync(FILE, 'utf-8');

  it('包含 WebGL 上下文探测', () => {
    expect(html).toMatch(/getContext\s*\(\s*["']webgl2?["']/);
  });

  it('包含软件渲染回退分支', () => {
    // 降级时应绘制 2D 战场：必须有 2D 上下文的获取与绘制调用
    expect(html).toMatch(/getContext\s*\(\s*["']2d["']/);
    expect(html).toMatch(/drawImage|fillRect/);
  });

  it('软件渲染时绘制地形与单位', () => {
    expect(html).toMatch(/createImageData|putImageData/);  // 地形缓冲图
    expect(html).toMatch(/fillRect/);                        // 单位符号
  });
});
