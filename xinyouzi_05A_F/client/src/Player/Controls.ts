// ============================================================================
//  Player/Controls.ts —— 亲征操控：键鼠 WASD/拖拽转视角 + 触屏虚拟摇杆。
//  本模块只产出「输入意图」（移动向量 / 视角增量 / 缩放 / 交互键），不含业务逻辑。
// ============================================================================

export type LookApplier = (delta: number) => void;

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

  /** 绑定画布事件（键盘绑 window，避免焦点问题） */
  attach(dom: HTMLElement): void {
    this.dom = dom;

    const isTypingTarget = (t: EventTarget | null): boolean =>
      t instanceof HTMLElement && ['INPUT', 'TEXTAREA', 'SELECT'].includes(t.tagName);

    const onKeyDown = (ev: KeyboardEvent): void => {
      if (isTypingTarget(ev.target)) {
        return;
      }
      const k = ev.key.toLowerCase();
      if (k === 'e' || k === 'enter') {
        this.interactEdge = true;
        ev.preventDefault();
        return;
      }
      this.keys.add(k);
      if (['w', 'a', 's', 'd', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright', ' '].includes(k)) {
        ev.preventDefault();
      }
    };
    const onKeyUp = (ev: KeyboardEvent): void => {
      this.keys.delete(ev.key.toLowerCase());
    };
    const onBlur = (): void => {
      this.keys.clear();
    };

    const onPointerDown = (ev: PointerEvent): void => {
      this.dragging = true;
      this.lastX = ev.clientX;
      this.lastY = ev.clientY;
    };
    const onPointerMove = (ev: PointerEvent): void => {
      if (!this.dragging) {
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
      this.zoomAccum += ev.deltaY;
      ev.preventDefault();
    };

    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('blur', onBlur);
    dom.addEventListener('pointerdown', onPointerDown);
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp);
    dom.addEventListener('wheel', onWheel, { passive: false });

    this.disposers.push(
      () => window.removeEventListener('keydown', onKeyDown),
      () => window.removeEventListener('keyup', onKeyUp),
      () => window.removeEventListener('blur', onBlur),
      () => dom.removeEventListener('pointerdown', onPointerDown),
      () => window.removeEventListener('pointermove', onPointerMove),
      () => window.removeEventListener('pointerup', onPointerUp),
      () => dom.removeEventListener('wheel', onWheel),
    );
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

  /** 交互键（E / Enter / 右下按钮）边沿 */
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
