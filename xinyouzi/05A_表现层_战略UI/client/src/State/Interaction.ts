// ============================================================================
//  State/Interaction.ts —— 亲征交互：走近城池/部队 → 气泡 → 右下「交互」/E 打开面板。
//  只做「邻近检测 + 展示入口」，所有操作仍经 OrderSender 下发，不碰业务规则。
// ============================================================================

import * as THREE from 'three';
import type { GameStateStore } from '../Core/GameStateStore.js';
import type { Hud } from '../Ui/Hud.js';
import type { WorldScene } from '../Scene/WorldScene.js';
import { cityPosition } from '../Scene/Adjacency.js';

const CITY_RADIUS = 19;
const ARMY_RADIUS = 13;
const CITY_LEAVE_RADIUS = 24;

type InteractTarget =
  | { kind: 'city'; id: number }
  | { kind: 'army'; id: number };

export class InteractionController {
  private target: InteractTarget | null = null;

  constructor(
    private readonly scene: WorldScene,
    private readonly store: GameStateStore,
    private readonly hud: Hud,
  ) {}

  /** 每帧调用：气泡定位 + 交互键消费 */
  update(): void {
    if (this.scene.mode !== 'field' || !this.store.ready) {
      this.target = null;
      this.hud.hideBubble();
      this.hud.setInteractEnabled(false);
      return;
    }

    const lordPos = this.scene.lordWorldPos;
    const state = this.store.state;
    const pf = this.store.playerFaction;
    if (!pf) {
      return;
    }

    // 最近交互目标：己方城池优先，其次己方部队
    let best: InteractTarget | null = null;
    let bestDist = Infinity;

    for (const city of state.cities) {
      if (city.ownerFactionId !== pf.id) {
        continue;
      }
      const p = cityPosition(city.id);
      const d = Math.hypot(p.x - lordPos.x, p.z - lordPos.z);
      if (d < CITY_RADIUS && d < bestDist) {
        best = { kind: 'city', id: city.id };
        bestDist = d;
      }
    }

    for (const army of state.armies) {
      if (army.ownerFactionId !== pf.id) {
        continue;
      }
      const ag = this.scene.armyWorldPos(army.id);
      if (!ag) {
        continue;
      }
      const d = Math.hypot(ag.x - lordPos.x, ag.z - lordPos.z);
      if (d < ARMY_RADIUS && d < bestDist) {
        best = { kind: 'army', id: army.id };
        bestDist = d;
      }
    }

    // 目标变化 → 切气泡；目标丢失且面板开着 → 收起
    if (best?.id !== this.target?.id || best?.kind !== this.target?.kind) {
      this.target = best;
    }

    if (best) {
      const anchor = new THREE.Vector3();
      let text = '';
      if (best.kind === 'city') {
        const city = this.store.city(best.id);
        anchor.set(cityPosition(best.id).x, 15.5, cityPosition(best.id).z);
        text = `E · 进入 ${city?.name ?? '城池'}`;
      } else {
        const ag = this.scene.armyWorldPos(best.id) ?? new THREE.Vector3();
        anchor.set(ag.x, 6.4, ag.z);
        text = `E · 军团交互（#${best.id}）`;
      }
      const sp = this.scene.screenPos(anchor);
      if (sp) {
        this.hud.showBubble(text, sp.x, sp.y);
      } else {
        this.hud.hideBubble();
      }
      this.hud.setInteractEnabled(true);
    } else {
      this.hud.hideBubble();
      this.hud.setInteractEnabled(false);
    }

    // 打开的面板随目标变化/丢失而关闭
    this.hud.closeEntityPanelUnless(best);

    if (this.scene.consumeInteract()) {
      this.onInteract();
    }
  }

  onInteract(): void {
    if (!this.target) {
      return;
    }
    if (this.target.kind === 'city') {
      this.hud.openCityPanel(this.target.id);
    } else {
      this.hud.openArmyPanel(this.target.id);
    }
  }
}
