// ============================================================================
//  main.ts —— 装配入口：client / store / scene / controls / hud / interaction /
//  cinema / sender 的接线，开局与回合流程。
// ============================================================================

import { BattleCinema } from './Battle/Cinema.js';
import { GameClient, OrderSender, actionLabel } from './Core/GameClient.js';
import { GameStateStore } from './Core/GameStateStore.js';
import type { StateResponse, TickResponse } from './Core/Types.js';
import { Controls } from './Player/Controls.js';
import { InteractionController } from './State/Interaction.js';
import { CityInterior } from './Scene/CityInterior.js';
import type { HoverTarget, ViewMode } from './Scene/WorldScene.js';
import { WorldScene } from './Scene/WorldScene.js';
import { Hud } from './Ui/Hud.js';

const SEED_DEFAULT = 12345;
const container = document.getElementById('scene-root') as HTMLElement;
const bootLoading = document.getElementById('boot-loading') as HTMLDivElement;

const client = new GameClient();
const store = new GameStateStore();
const controls = new Controls();

/** 第 3 轮：城内视图（Canvas 2D）。盖在疆域图之上，退出即恢复。 */
const interior = new CityInterior();

let scene: WorldScene | null = null;
let hud: Hud | null = null;
let interaction: InteractionController | null = null;
let cinema: BattleCinema | null = null;
// R2：指令唯一入口。notify 通过间接引用接到 Hud（Hud 需要 sender 才能构造，见下）
let toastFn: ((msg: string, kind: 'info' | 'ok' | 'error') => void) | null = null;
const sender = new OrderSender(client, (msg, kind) => toastFn?.(msg, kind) ?? undefined, () => {
  // 下令后刷新状态镜像：顶栏「待结算 N 条」即时更新（R1：一切显示源自 store）
  void store.refresh(client).catch(() => undefined);
}, (r) => {
  // 即时指令（部门任命）已经把新 state 回传了，直接吃进去重画，
  // 不走二次 GET：否则面板会闪一下旧数据。
  if ('state' in r && 'meta' in r) {
    store.applySnapshot(r.state, r.meta);
    scene?.refreshFromStore();
    hud?.refreshPanel();
  }
});

// ---------------------------------------------------------------------------
//  城内视图（第 3 轮）：进出城内
// ---------------------------------------------------------------------------

/** 进入城内：盖住疆域图并暂停它的渲染，城池侧栏照旧可用（下令仍走侧栏）。 */
function enterInterior(cityId: number): void {
  if (!interior.open(cityId)) {
    hud?.toast('进不去这座城。', 'error');
    return;
  }
  hud?.setInteriorOpen(true);
  scene?.setVisible(false);
  // 地图上的悬停信息卡是疆域图的产物，进内景就撤掉，别压在内景上面
  hud?.unpinHoverCard();
  hud?.hideBubble();
  const city = store.city(cityId);
  hud?.toast(`城内：${city?.name ?? ''} · 建筑按当前等级呈现`, 'info');
}

/** 退出城内：回到疆域图，原有面板与地图交互照旧。 */
function exitInterior(): void {
  if (!interior.isOpen) {
    return;
  }
  interior.close();
  hud?.setInteriorOpen(false);
  scene?.setVisible(true);
}

// ---------------------------------------------------------------------------
//  场景构建 / 重建（重新开局 / 读档时整体换血）
// ---------------------------------------------------------------------------

/**
 * 世界代次：每次重建场景（重新开局 / 读档）+1。
 *
 * 【为什么需要】回合推进是异步的（等后端 tick、等战斗演出）。
 * 如果玩家在演出进行中读档，那条旧回合流程醒来后会把「旧世界的结算」
 * 覆盖到新世界上（弹一个对不上的结算面板、把旧状态写回 store）。
 * 拿代次号做门禁：一旦世界被换掉，旧流程的结果直接作废。
 */
let worldGen = 0;

async function buildWorld(): Promise<void> {
  worldGen += 1;
  scene?.dispose();

  const newScene = new WorldScene();
  scene = newScene;
  // 场景换血后同步 HUD 手里的 scene 引用（重新开局 / 读档都会走到这里），
  // 否则 HUD 还指着已销毁的旧场景，军议箭头与「镜头飞向」会画到空画布上。
  hud?.setScene(newScene);
  newScene.setVisible(!interior.isOpen);
  await newScene.init(container, store, {
    onHover: (target: HoverTarget | null) => hud?.showHoverCard(target),
    onSelectCity: (cityId: number) => {
      // 选中城池：信息卡钉住 + 打开侧栏（信息与指令面板）
      hud?.pinHoverCard({ kind: 'city', id: cityId });
      hud?.openCityPanel(cityId);
    },
    onSelectArmy: (armyId: number) => {
      hud?.pinHoverCard({ kind: 'army', id: armyId });
      hud?.openArmyPanel(armyId);
    },
    onClickEmpty: () => {
      hud?.unpinHoverCard();
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
      onTick: () => doTick(),
      onNewGame: () => void newGame(SEED_DEFAULT),
      onEnterCityInterior: (cityId: number) => enterInterior(cityId),
      onExitCityInterior: () => exitInterior(),
      onRenameOfficer: async (officerId: number, name: string) => {
        try {
          const r = await client.renameOfficer(officerId, name);
          store.applySnapshot(r.state, r.meta);
          scene?.refreshFromStore();
          hud?.refreshPanel();
          hud?.toast(`「${r.oldName}」已赐名「${r.name}」，此后不再更改。`, 'ok');
          return true;
        } catch (err) {
          hud?.toast(err instanceof Error ? err.message : String(err), 'error');
          return false;
        }
      },
      // ---- 存档（第 5 轮）----
      client,
      onSaveGame: (name: string) => saveGame(name),
      onLoadSave: (id: string) => loadGame(id),
      onDeleteSave: (id: string) => deleteGame(id),
    });
    hud.setInteractHandler(() => interaction?.onInteract());
    toastFn = (msg, kind) => hud?.toast(msg, kind);
  }
  interaction = new InteractionController(newScene, store, hud);
  cinema = new BattleCinema(newScene, store, hud);

  newScene.refreshFromStore();
  hud.renderVolatile();
  setViewMode('god');

  // 演示钩子：?cinema-demo=1 时自动播一场战斗演出（无头截图 / 联调用）
  if (new URLSearchParams(window.location.search).has('cinema-demo')) {
    window.setTimeout(() => void cinema?.playDemo(3), 1800);
  }
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
    hud.toast('疆域图：拖拽平移 · 滚轮切三档缩放 · 点击城池下令（快捷键 1）', 'info');
  } else {
    // v1.9：2D 疆域图没有亲征视角，点错了就退回上帝视角，不让界面停在一个不存在的模式上
    hud.toast('2D 疆域图暂不支持亲征视角，已停留在全图视角', 'info');
    scene.setMode('god');
    hud.setViewModeUI('god');
  }
}

// ---------------------------------------------------------------------------
//  开局
// ---------------------------------------------------------------------------
async function newGame(seed: number = SEED_DEFAULT): Promise<void> {
  // 重新开局前先退出城内：旧城的等级与在建状态已经没有意义了
  if (interior.isOpen) {
    interior.close();
    hud?.setInteriorOpen(false);
  }
  if (!hud) {
    bootLoading.textContent = '正在生成天下……';
  } else {
    hud.toast('重新开局中……', 'info');
  }
  try {
    const resp: StateResponse = await client.newGame(seed);
    store.applySnapshot(resp.state, resp.meta);
    await buildWorld();
    hud?.toast(`开局成功（seed=${seed}）：7 城 3 势力，你执掌 薪火（2 城）`, 'ok');
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
//  存档（第 5 轮）：存 / 读 / 删
//
//  这三个动作都不是「指令」：不排队、不结算、不产生 StateDelta，
//  所以不走 OrderSender，直接调后端已经就绪的四个存档端点。
//  读档成功后走和 newGame 同一套流程：applySnapshot → buildWorld → toast，
//  页面不刷新即可回到存档那一刻的天下。
// ---------------------------------------------------------------------------

/** 保存当前进度。name 为空 → 后端用默认名「存档X年Y月」。 */
async function saveGame(name: string): Promise<void> {
  try {
    const r = await client.save(name);
    hud?.toast(`已保存「${r.name}」：${r.year}年${r.month}月 · ${r.cities} 城`, 'ok');
  } catch (err) {
    hud?.toast(`保存失败：${err instanceof Error ? err.message : String(err)}`, 'error');
  }
}

/** 读档：把存档里的 state/meta 吃进 store，重建场景，不刷新页面。 */
async function loadGame(id: string): Promise<void> {
  try {
    // 战斗演出进行中读档：演出的画布挂在 body 上，不收掉会盖住读档后的画面。
    // BattleCinema 把 window 的 pointerdown 当「跳过」，借它把演出收尾
    //（不改 Battle/Cinema.ts——本轮只做存档界面）。没在演出会没有任何副作用。
    window.dispatchEvent(new PointerEvent('pointerdown'));
    const r = await client.load(id);
    // 读档前先退出城内：旧城的等级与在建状态已经没有意义了
    if (interior.isOpen) {
      interior.close();
      hud?.setInteriorOpen(false);
    }
    store.applySnapshot(r.state, r.meta);
    await buildWorld();
    hud?.closeSavePanel();
    hud?.toast(`已读档「${r.name}」：${r.state.year}年${r.state.month}月`, 'ok');
  } catch (err) {
    hud?.toast(`读档失败：${err instanceof Error ? err.message : String(err)}`, 'error');
  }
}

/** 删除存档（浮层里已经做过二次确认，这里只负责调接口 + 提示）。 */
async function deleteGame(id: string): Promise<void> {
  try {
    await client.deleteSave(id);
    hud?.toast('存档已删除。', 'ok');
  } catch (err) {
    hud?.toast(`删除失败：${err instanceof Error ? err.message : String(err)}`, 'error');
  }
}

// ---------------------------------------------------------------------------
//  回合推进（R5）：tick → 状态镜像 → 场景刷新 → 战斗演出 → 结算面板
// ---------------------------------------------------------------------------
async function doTick(): Promise<void> {
  if (!store.ready || !hud || !scene) {
    return;
  }
  const gen = worldGen;
  try {
    const tick: TickResponse = await client.tick();
    if (gen !== worldGen) {
      // 等 tick 的时候玩家读档 / 重新开局了：这次结算属于旧世界，直接作废
      return;
    }
    store.applyTick(tick);
    scene.refreshFromStore();
    hud.renderVolatile();
    // 【第 8 轮】战斗不再自动弹出打断玩家。
    // 改成：本月有几场战斗就记几条战报，玩家想看哪场点哪场「观看回放」。
    const scripts = cinema?.scriptsFor(tick) ?? [];
    if (scripts.length > 0) {
      hud.addBattleReports(
        scripts.map((s) => {
          const city = store.city(s.cityId);
          const atk = store.faction(s.attacker.factionId)?.name ?? '未知';
          const def = store.faction(s.defender.factionId)?.name ?? '未知';
          const outcome =
            s.outcome === 'capture' ? '城破' : s.outcome === 'repelled' ? '守住' : '僵持';
          const atkAfter = Math.max(0, s.attacker.before - s.attacker.loss);
          const defAfter = Math.max(0, s.defender.before - s.defender.loss);
          return {
            title: `${city?.name ?? s.cityId} · ${atk} 攻 ${def}（${outcome}）`,
            detail: `攻方 ${s.attacker.before} → ${atkAfter}（损 ${s.attacker.loss}）　守方 ${s.defender.before} → ${defAfter}（损 ${s.defender.loss}）`,
            play: () => void cinema?.playScript(s),
          };
        }),
      );
      hud.log(`⚔ 本月 ${scripts.length} 场战斗，已记入战报（想看回放点顶栏「战报」）`, 'info');
    }
    // 结算结果逐条写进底部日志（R4），供玩家回看「刚才发生了什么」
    for (const r of tick.results) {
      const ok = r.code === 'Success';
      const head = `${ok ? '✓' : '✗'} ${r.actorType === 'Player' ? '玩家' : r.actorType === 'AiFaction' ? `势力 ${r.actorId}` : `人才 ${r.actorId}`} · ${actionLabel(r.action)}`;
      hud.log(`${head}${r.message ? ' · ' + r.message : ''}`, ok ? 'ok' : 'error');
    }
    for (const e of tick.events) {
      hud.log(`${e.category} · ${e.message}`, 'info');
    }
    // 自动推进模式下不打断：结算面板要点「继续」才能关，
    // 弹出来会把后续 tick 全堵住。只在最后一次弹，供人核对结果。
    if (autoTickRemaining <= 0 && !hud.isAutoTickOn()) {
      hud.showSettlement(tick);
    } else {
      hud.toast(`${tick.year}年${tick.month}月 · 结算 ${tick.results.length} 条`, 'info');
    }
    if (tick.meta.isFinished) {
      hud.showGameOver(tick.meta);
    }
  } catch (err) {
    hud.toast(err instanceof Error ? err.message : String(err), 'error');
  }
}

// ---------------------------------------------------------------------------
//  自动推进（演示/联调用）：?autotick=N 让游戏自己往下走 N 个月。
//  用途：录屏演示、无头截图、联调时不想手动点。正式游玩不受影响（不传参则不启用）。
// ---------------------------------------------------------------------------
let autoTickRemaining = Number(new URLSearchParams(window.location.search).get('autotick') ?? 0) || 0;

function startAutoTick(): void {
  if (autoTickRemaining <= 0) {
    return;
  }
  const total = autoTickRemaining;
  const timer = window.setInterval(() => {
    if (autoTickRemaining <= 0) {
      window.clearInterval(timer);
      return;
    }
    autoTickRemaining -= 1;
    void doTick();
  }, 2200);
  hud?.toast(`自动推进 ${total} 个月（演示模式）`, 'info');
}

// ---------------------------------------------------------------------------
//  主循环（交互检测每帧运行）
// ---------------------------------------------------------------------------
function loop(): void {
  requestAnimationFrame(loop);
  interaction?.update();
}

// ---------------------------------------------------------------------------
// 城内视图画布：与疆域图共用 #scene-root，靠显隐进出
void interior
  .init(container, store, { onExit: () => exitInterior() })
  .catch(() => undefined);

void newGame(SEED_DEFAULT).then(() => startAutoTick());
loop();
