/**
 * 把 dist/ 构建产物合并为单个自包含 HTML 文件。
 *
 * 为什么需要：本机 bash 工具无法维持长驻服务进程（nohup 与 run_in_background
 * 都会在数分钟内被回收，表现为 502），所以「本地服务器」不是可靠交付方式。
 * 单文件HTML 双击即可打开，不依赖任何服务器、不依赖 WebGL 环境。
 *
 * 用法：node build_single.mjs
 */

import { readFileSync, writeFileSync, existsSync, rmSync, readdirSync } from 'fs';
import { join, resolve, extname } from 'path';

const ROOT = process.cwd();
const DIST = join(ROOT, 'dist');
const OUT = join(ROOT, '战争策略游戏原型.html');

if (!existsSync(DIST)) {
  console.error('找不到 dist/，请先运行 vite build');
  process.exit(1);
}

const files = readdirSync(DIST);
const htmlFile = files.find((f) => extname(f) === '.html');
if (!htmlFile) {
  console.error('找不到 HTML 产物');
  process.exit(1);
}

let html = readFileSync(join(DIST, htmlFile), 'utf-8');
const jsFile = files.find((f) => f.endsWith('.js'));
const cssFiles = files.filter((f) => f.endsWith('.css'));

if (!jsFile) {
  console.error('找不到 JS 产物');
  process.exit(1);
}

const js = readFileSync(join(DIST, jsFile), 'utf-8');
const css = cssFiles.map((f) => readFileSync(join(DIST, f), 'utf-8')).join('\n');

// 内联 CSS
if (css.trim().length > 0) {
  html = html.replace(
    /<link\b[^>]*rel=["']?stylesheet["']?[^>]*>/gi,
    `<style>\n${css}\n</style>`,
  );
}

// 内联 JS：匹配任意属性顺序的 module script
const jsTag = `<script type="module">\n${js}\n</script>`;
let replaced = false;
html = html.replace(
  /<script\b[^>]*src=["'][^"']*\.js["'][^>]*>\s*<\/script>/gi,
  () => {
    replaced = true;
    return jsTag;
  },
);
if (!replaced) {
  //兜底：script 标签是自闭合或带内容
  html = html.replace(/<script\b[^>]*src=["'][^"']*\.js["'][^>]*>/gi, jsTag);
  replaced = true;
}

// 校验
const leftover = html.match(/src=["'][^"']*\.js["']/gi);
if (leftover) {
  console.error('内联失败，仍有外链 JS:', leftover.slice(0, 3));
  process.exit(1);
}

writeFileSync(OUT, html, 'utf-8');
const sizeKb = (Buffer.byteLength(html, 'utf-8') / 1024).toFixed(0);
console.log(`已生成单文件：${OUT}`);
console.log(`大小：${sizeKb} KB`);
console.log('双击即可打开，不需要服务器。');

// 清理 dist
try {
  rmSync(DIST, { recursive: true, force: true });
} catch {
  /* 忽略 */
}
void resolve;
