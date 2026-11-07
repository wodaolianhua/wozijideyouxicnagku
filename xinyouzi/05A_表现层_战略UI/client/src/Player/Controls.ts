// ============================================================================
//  Player/Controls.ts —— 桌面端输入层：全局键盘快捷键 + 鼠标视角 + 触屏虚拟摇杆。
//
//  桌面化改造（R2）要点：
//  · 键盘不再只有 WASD：新增「快捷键意图」上报（推进下月 / 关闭面板 / 切视角 /
//    人才面板 / 帮助）。本层只上报意图，不执行动作 —— 动作由 Hud / main 接线，
//    保持「输入层不含业务逻辑」。
//  · 输入按视角分模式：上帝视角不吞 WASD / 滚轮（整留给 OrbitControls 的
//    旋转·缩放·平移）；亲征视角才接管 WASD / 滚轮缩放 / 左键拖拽转视角。
//  · 触屏检测：只有真·触摸设备（pointer:coarse 或发生过真实 touchstart）才启用
//    虚拟摇杆；桌面端不显示摇杆、也不拦截鼠标事件。
// ============================================================================

export type LookApplier = (delta: number) => void;

/** 输入模式：god=轨道相机（不吞 WASD/滚轮）｜field=亲征跟随相机（接管 WASD/滚轮） */
export type InputMode = 'god' | 'field';

/** 全局快捷键意图（由 Hud / main 消费） */
export type ShortcutIntent =
  | 'tick' // 推进下月
  | 'escape' // 关闭当前面板 / 浮层
  | 'viewGod' // 切上帝视角
  | 'viewField' // 切亲征视角
  | 'officers' // 人才面板
  | 'help'; // 快捷键帮助

export interface ShortcutEvent {
  intent: ShortcutIntent;
}

const MOVE_KEYS = new Set(['w', 'a', 's', 'd', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright']);

export class Controls {
  private keys = new Set<string>();
  private lookAccumX = 0;
  private lookAccumY = 0;
  private zoomAccum = 0;
  private joyVec = { x: 0, y: 0 };
  private interactEdge = false;
  private dragging = false;
  private lastX = 0;
  private lastY = 0;
  private dom: HTMLElement | null = null;
  private disposers: Array<() => void> = [];
  private mode: InputMode = 'god';
  private shortcutHandler: ((e: ShortcutEvent) => void) | null = null;
  /** 主指针为粗指针（触屏优先） */
  private touchPrimary = false;
  /** 发生过真实触摸（混合设备：笔记本 + 触摸屏，鼠标操作时不显示摇杆） */
  private touchSeen = false;

  constructor() {
    try {
      this.touchPrimary = window.matchMedia?.('(pointer: coarse)')?.matches ?? false;
    } catch {
      this.touchPrimary = false;
    }
  }

  /** 当前是否应展示触屏 UI（虚拟摇杆） */
  get touchUi(): boolean {
    return this.touchPrimary || this.touchSeen;
  }

  /** 注册全局快捷键处理器（Hud 消费） */
  onShortcut(fn: (e: ShortcutEvent) => void): void {
    this.shortcutHandler = fn;
  }

  /** 视角模式切换（上帝 → 亲征） */
  setMode(mode: InputMode): void {
    if (this.mode === mode) {
      return;
    }
    this.mode = mode;
    // 切模式清空累积量，避免把上帝视角里误攒的视角/缩放增量带进亲征视角
    this.lookAccumX = 0;
    this.lookAccumY = 0;
    this.zoomAccum = 0;
    this.keys.clear();
    this.dragging = false;
  }

  get inputMode(): InputMode {
    return this.mode;
  }

  /** 绑定画布事件（键盘绑 window，避免焦点问题） */
  attach(dom: HTMLElement): void {
    // 幂等：重新开局会再次 attach，先摘掉旧监听，避免重复绑定
    this.detach();
    this.dom = dom;

    const isTypingTarget = (t: EventTarget | null): boolean =>
      t instanceof HTMLElement && ['INPUT', 'TEXTAREA', 'SELECT'].includes(t.tagName);

    const onKeyDown = (ev: KeyboardEvent): void => {
      if (isTypingTarget(ev.target)) {
        return;
      }
      // 不劫持浏览器自身的组合键（Ctrl+W / Ctrl+R / Alt+Tab …）
      if (ev.ctrlKey || ev.metaKey || ev.altKey) {
        return;
      }
      const k = ev.key.toLowerCase();

      // 1) 全局快捷键（意图上报，不执行）
      const intent = this.mapShortcut(k);
      if (intent) {
        ev.preventDefault();
        if (!ev.repeat) {
          this.shortcutHandler?.({ intent });
        }
        return;
      }

      // 2) 亲征交互键（E / F）—— 仅亲征视角有效
      if (k === 'e' || k === 'f') {
        if (this.mode === 'field') {
          this.interactEdge = true;
          ev.preventDefault();
        }
        return;
      }

      // 3) 行走键（仅亲征视角接管，避免上帝视角吞掉快捷键）
      if (MOVE_KEYS.has(k)) {
        this.keys.add(k);
        if (this.mode === 'field') {
          ev.preventDefault();
        }
      }
    };

    const onKeyUp = (ev: KeyboardEvent): void => {
      this.keys.delete(ev.key.toLowerCase());
    };

    const onBlur = (): void => {
      this.keys.clear();
    };

    const onPointerDown = (ev: PointerEvent): void => {
      if (this.mode !== 'field' || ev.button !== 0) {
        return;
      }
      this.dragging = true;
      this.lastX = ev.clientX;
      this.lastY = ev.clientY;
    };

    const onPointerMove = (ev: PointerEvent): void => {
      if (!this.dragging || this.mode !== 'field') {
        return;
      }
      this.lookAccumX += (ev.clientX - this.lastX) * 0.005;
      this.lookAccumY += (ev.clientY - this.lastY) * 0.004;
      this.lastX = ev.clientX;
      this.lastY = ev.clientY;
    };

    const onPointerUp = (): void => {
      this.dragging = false;
    };

    const onWheel = (ev: WheelEvent): void => {
      // 上帝视角的滚轮缩放交给 OrbitControls，这里只服务亲征视角
      if (this.mode !== 'field') {
        return;
      }
      this.zoomAccum += ev.deltaY;
      ev.preventDefault();
    };

    // 触屏检测：混合设备首次真实触摸后才启用虚拟摇杆
    const onFirstTouch = (): void => {
      this.touchSeen = true;
    };

    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('blur', onBlur);
    dom.addEventListener('pointerdown', onPointerDown);
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp);
    dom.addEventListener('wheel', onWheel, { passive: false });
    window.addEventListener('touchstart', onFirstTouch, { passive: true });

    this.disposers.push(
      () => window.removeEventListener('keydown', onKeyDown),
      () => window.removeEventListener('keyup', onKeyUp),
      () => window.removeEventListener('blur', onBlur),
      () => dom.removeEventListener('pointerdown', onPointerDown),
      () => window.removeEventListener('pointermove', onPointerMove),
      () => window.removeEventListener('pointerup', onPointerUp),
      () => dom.removeEventListener('wheel', onWheel),
      () => window.removeEventListener('touchstart', onFirstTouch),
    );
  }

  /** 键位 → 快捷键意图。焦点在按钮上时，空格/回车交还按钮自身（避免与「推进下月」打架）。 */
  private mapShortcut(key: string): ShortcutIntent | null {
    const active = document.activeElement;
    const onButton = active instanceof HTMLElement && active.tagName === 'BUTTON';
    switch (key) {
      case ' ':
      case 'spacebar':
        if (onButton) {
          return null;
        }
        return 'tick';
      case 'enter':
        if (onButton) {
          return null;
        }
        return 'tick';
      case 'escape':
        return 'escape';
      case '1':
        return 'viewGod';
      case '2':
        return 'viewField';
      case '3':
        return 'officers';
      case 'h':
      case '?':
        return 'help';
      default:
        return null;
    }
  }

  /** HUD 虚拟摇杆回填（-1..1，y=+1 前进） */
  setJoystick(x: number, y: number): void {
    this.joyVec.x = x;
    this.joyVec.y = y;
  }

  /** 移动意图：x=右(+)/左(-)，y=前(+)/后(-)。键盘与摇杆取并集（键优先）。 */
  get moveVector(): { x: number; y: number } {
    let x = 0;
    let y = 0;
    if (this.keys.has('w') || this.keys.has('arrowup')) {
      y += 1;
    }
    if (this.keys.has('s') || this.keys.has('arrowdown')) {
      y -= 1;
    }
    if (this.keys.has('d') || this.keys.has('arrowright')) {
      x += 1;
    }
    if (this.keys.has('a') || this.keys.has('arrowleft')) {
      x -= 1;
    }
    if (x === 0 && y === 0) {
      x = this.joyVec.x;
      y = this.joyVec.y;
    }
    const len = Math.hypot(x, y);
    if (len > 1) {
      x /= len;
      y /= len;
    }
    return { x, y };
  }

  /** 帧末消费视角增量 */
  consumeLook(onYaw: LookApplier, onPitch: LookApplier): void {
    if (this.lookAccumX !== 0 || this.lookAccumY !== 0) {
      onYaw(this.lookAccumX);
      onPitch(this.lookAccumY);
      this.lookAccumX = 0;
      this.lookAccumY = 0;
    }
  }

  consumeZoom(): number {
    const z = this.zoomAccum;
    this.zoomAccum = 0;
    return z;
  }

  /** 交互键（E / F / 右下按钮）边沿 */
  consumeInteract(): boolean {
    const e = this.interactEdge;
    this.interactEdge = false;
    return e;
  }

  detach(): void {
    for (const d of this.disposers) {
      d();
    }
    this.disposers = [];
    this.dom = null;
  }
}
