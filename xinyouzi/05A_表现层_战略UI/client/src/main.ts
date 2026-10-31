// ============================================================================
//  main.ts —— 装配入口：client / store / scene / controls / hud / interaction /
//  cinema / sender 的接线，开局与回合流程。
// ============================================================================

import { BattleCinema } from './Battle/Cinema.js';
import { GameClient, OrderSender } from './Core/GameClient.js';
import { GameStateStore } from './Core/GameStateStore.js';
import type { StateResponse, TickResponse } from './Core/Types.js';
import { Controls } from './Player/Controls.js';
import { InteractionController } from './State/Interaction.js';
import type { HoverTarget, ViewMode } from './Scene/WorldScene.js';
import { WorldScene } from './Scene/WorldScene.js';
import { Hud } from './Ui/Hud.js';

const SEED_DEFAULT = 12345;
const container = document.getElementById('scene-root') as HTMLElement;
const bootLoading = document.getElementById('boot-loading') as HTMLDivElement;

const client = new GameClient();
const store = new GameStateStore();
const controls = new Controls();

let scene: WorldScene | null = null;
let hud: Hud | null = null;
let interaction: InteractionController | null = null;
let cinema: BattleCinema | null = null;
// R2：指令唯一入口。notify 通过间接引用接到 Hud（Hud 需要 sender 才能构造，见下）
let toastFn: ((msg: string, kind: 'info' | 'ok' | 'error') => void) | null = null;
const sender = new OrderSender(client, (msg, kind) => toastFn?.(msg, kind) ?? undefined, () => {
  // 下令后刷新状态镜像：顶栏「待结算 N 条」即时更新（R1：一切显示源自 store）
  void store.refresh(client).catch(() => undefined);
});

// ---------------------------------------------------------------------------
//  场景构建 / 重建（重新开局时整体换血）
// ---------------------------------------------------------------------------
async function buildWorld(): Promise<void> {
  scene?.dispose();

  const newScene = new WorldScene();
  scene = newScene;
  await newScene.init(container, store, {
    onHover: (target: HoverTarget | null) => hud?.showHoverCard(target),
    onSelectCity: (cityId: number) => {
      hud?.openCityPanel(cityId);
    },
    onSelectArmy: (armyId: number) => {
      hud?.openArmyPanel(armyId);
    },
    onClickEmpty: () => {
      hud?.closeSide();
    },
  }, controls);

  if (!hud) {
    hud = new Hud({
      store,
      sender,
      scene: newScene,
      controls,
      onSetViewMode: setViewMode,
      onTick: () => void doTick(),
      onNewGame: () => void newGame(SEED_DEFAULT),
    });
    hud.setInteractHandler(() => interaction?.onInteract());
    toastFn = (msg, kind) => hud?.toast(msg, kind);
  }
  interaction = new InteractionController(newScene, store, hud);
  cinema = new BattleCinema(newScene, store, hud);

  newScene.refreshFromStore();
  hud.renderVolatile();
  setViewMode('god');
}

// ---------------------------------------------------------------------------
//  视角切换（R4 双层视角）
// ---------------------------------------------------------------------------
function setViewMode(mode: ViewMode): void {
  if (!scene || !hud) {
    return;
  }
  scene.setMode(mode);
  hud.setViewModeUI(mode);
  if (mode === 'god') {
    hud.toast('上帝视角：轨道相机 · 点击城池下达宏观指令', 'info');
  } else {
    hud.toast('亲征视角：WASD / 摇杆 行走 · E 或右下「交互」键与城池/军团交互', 'info');
  }
}

// ---------------------------------------------------------------------------
//  开局
// ---------------------------------------------------------------------------
async function newGame(seed: number = SEED_DEFAULT): Promise<void> {
  if (!hud) {
    bootLoading.textContent = '正在生成天下……';
  } else {
    hud.toast('重新开局中……', 'info');
  }
  try {
    const resp: StateResponse = await client.newGame(seed);
    store.applySnapshot(resp.state, resp.meta);
    await buildWorld();
    hud?.toast(`开局成功（seed=${seed}）：6 城 3 势力，你执掌 槽0 · 汉`, 'ok');
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    if (!hud) {
      bootLoading.textContent = `开局失败：${msg}`;
    } else {
      hud.toast(`开局失败：${msg}`, 'error');
    }
  } finally {
    if (hud) {
      bootLoading.classList.add('sg-hidden');
    } else {
      bootLoading.style.opacity = '0';
      window.setTimeout(() => bootLoading.remove(), 500);
    }
  }
}

// ---------------------------------------------------------------------------
//  回合推进（R5）：tick → 状态镜像 → 场景刷新 → 战斗演出 → 结算面板
// ---------------------------------------------------------------------------
async function doTick(): Promise<void> {
  if (!store.ready || !hud || !scene) {
    return;
  }
  try {
    const tick: TickResponse = await client.tick();
    store.applyTick(tick);
    scene.refreshFromStore();
    hud.renderVolatile();
    await cinema?.maybePlay(tick);
    hud.showSettlement(tick);
    if (tick.meta.isFinished) {
      hud.showGameOver(tick.meta);
    }
  } catch (err) {
    hud.toast(err instanceof Error ? err.message : String(err), 'error');
  }
}

// ---------------------------------------------------------------------------
//  主循环（交互检测每帧运行）
// ---------------------------------------------------------------------------
function loop(): void {
  requestAnimationFrame(loop);
  interaction?.update();
}

// ---------------------------------------------------------------------------
void newGame(SEED_DEFAULT);
loop();
