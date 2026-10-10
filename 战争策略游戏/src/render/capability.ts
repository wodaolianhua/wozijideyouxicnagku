/**
 * 渲染能力检测与降级
 *
 * 背景：本项目运行环境可能没有 WebGL（无 GPU 的远程桌面、某些内嵌浏览器、
 * 部分预览面板）。Three.js 的 WebGLRenderer 在无 WebGL 时会静默失败或抛错，
 * 表现为**屏幕全黑**——用户只看到面板，看不到战场。
 *
 * 本模块做两件事：
 * 1. 探测 WebGL 是否可用（含 WebGL2 / WebGL1两级回退）
 * 2. 不可用时提供 Canvas 2D 的软件渲染兜底，保证「战场始终可见」
 */

export type RenderMode = 'webgl2' | 'webgl' | 'canvas2d';

/** 检测 WebGL 可用性。返回 null 表示不可用。 */
export function detectWebGL(): RenderMode | null {
  // WebGL2
  try {
    const c = document.createElement('canvas');
    const gl = c.getContext('webgl2');
    if (gl) return 'webgl2';
  } catch {
    // 忽略，继续尝试
  }
  // WebGL1
  try {
    const c = document.createElement('canvas');
    const gl =
      c.getContext('webgl') ||
      c.getContext('experimental-webgl') ||
      c.getContext('webkit-3d');
    if (gl) return 'webgl';
  } catch {
    // 忽略
  }
  return null;
}

/** 人类可读的失败原因，供界面提示 */
export function describeRenderFailure(): string {
  const ua = navigator.userAgent;
  let reason = '未知原因';
  try {
    const c = document.createElement('canvas');
    const gl = c.getContext('webgl2') || c.getContext('webgl');
    if (!gl) {
      reason = '浏览器未提供 WebGL 上下文（通常是显卡驱动缺失或被禁用）';
    } else {
      reason = 'WebGL 上下文创建失败';
    }
  } catch (e) {
    reason = `WebGL 初始化异常：${e instanceof Error ? e.message : String(e)}`;
  }
  return `${reason}（${ua.slice(0, 60)}…）`;
}
