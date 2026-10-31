// ============================================================================
//  Battle/Cinema.ts —— 战斗演出（cinematic）。
//  复用回合结算结果（Dispatch 结果 + 军事事件），做镜头飞行 / 火花 / 字幕。
//  不实现任何实时战斗逻辑（那是 05B 包）：一切以 /api/tick 的结算结果为准。
// ============================================================================

import * as THREE from 'three';
import type { GameStateStore } from '../Core/GameStateStore.js';
import type { TickResponse } from '../Core/Types.js';
import { cityPosition } from '../Scene/Adjacency.js';
import type { WorldScene } from '../Scene/WorldScene.js';
import type { Hud } from '../Ui/Hud.js';

const sleep = (ms: number): Promise<void> => new Promise((r) => window.setTimeout(r, ms));

export class BattleCinema {
  private playing = false;

  constructor(
    private readonly scene: WorldScene,
    private readonly store: GameStateStore,
    private readonly hud: Hud,
  ) {}

  /** 本月若有 Dispatch 结算 → 演出；否则直接返回（不阻塞结算面板） */
  async maybePlay(tick: TickResponse): Promise<void> {
    if (this.playing || !this.store.ready) {
      return;
    }

    // 表现层事件筛选（非业务计算）：取本月 Dispatch 的目标城，上限 2 处
    const battleCityIds: number[] = [];
    for (const r of tick.results) {
      if (r.action === 'Dispatch' && !battleCityIds.includes(r.targetId)) {
        battleCityIds.push(r.targetId);
      }
    }
    if (battleCityIds.length === 0) {
      return;
    }
    const shown = battleCityIds.slice(0, 2);

    const militaryMessages = tick.events.filter((e) => e.category === 'Military').map((e) => e.message);

    this.playing = true;
    this.hud.showCinema('战报');
    try {
      for (let i = 0; i < shown.length; i++) {
        const cityId = shown[i]!;
        const city = this.store.city(cityId);
        const p = cityPosition(cityId);
        this.hud.setCinemaText(city ? `${city.name} 方向传来战报……` : '两军交锋……');
        await this.scene.flySequence([
          { pos: new THREE.Vector3(p.x + 34, 30, p.z + 40), look: new THREE.Vector3(p.x, 4, p.z), duration: 1.0 },
        ]);
        this.scene.sparkAt(cityId);
        if (militaryMessages[i]) {
          this.hud.setCinemaText(militaryMessages[i]!);
        }
        await sleep(1100);
      }
    } finally {
      this.hud.hideCinema();
      // 还原相机到当前视角默认机位
      this.scene.setMode(this.scene.mode);
      this.playing = false;
    }
  }
}
