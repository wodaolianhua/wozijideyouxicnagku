/**
 * 经营与人才面板 —— UI 层
 *
 * 只做展示与输入，不含业务逻辑。所有变更都调用 game/economy.ts 与 game/talent.ts
 * 的纯函数，把返回的新状态交回给主循环。
 */

import {
  advanceDay,
  createEconomy,
  dailyFoodProduction,
  dailyGoldIncome,
  startTask,
  startUpgrade,
  type EconomyModifiers,
  type EconomyState,
} from '../game/economy';
import {
  POSTS,
  PERSONALITIES,
  ORIGINS,
  TRAITS,
  advanceTalents as advanceTalentDay,
  appoint,
  createTalentState,
  dismiss,
  postFit,
  totalBonus,
  type Post,
  type TalentState,
} from '../game/talent';

const $ = (id: string) => document.getElementById(id)!;

const el = {
  panel: $('econ'),
  body: $('eco-body'),
  toggle: $('btn-eco-toggle'),
  day: $('econ-day'),
  food: $('eco-food'),
  gold: $('eco-gold'),
  morale: $('eco-morale'),
  pop: $('eco-pop'),
  mp: $('eco-mp'),
  lv: $('eco-lv'),
  foodRate: $('eco-foodrate'),
  goldRate: $('eco-goldrate'),
  dayBtn: $('btn-day'),
  upgradeBtn: $('btn-upgrade'),
  tasks: $('eco-tasks'),
  talents: $('eco-talents'),
  talentCount: $('eco-talentcount'),
};

const POST_KEYS: Post[] = ['governor', 'general', 'treasurer', 'constable'];

export class EconPanel {
  econ: EconomyState = createEconomy();
  talents: TalentState = createTalentState(7, 20261005);
  private collapsed = false;

  constructor(private onLog: (msg: string) => void) {
    el.toggle.onclick = () => this.toggle();
    el.dayBtn.onclick = () => this.advanceDay();
    el.upgradeBtn.onclick = () => this.upgrade();
    this.render();
  }

  toggle() {
    this.collapsed = !this.collapsed;
    el.body.style.display = this.collapsed ? 'none' : 'block';
    el.toggle.textContent = this.collapsed ? '展开' : '收起';
  }

  /** 人才加成 → 经营层参数 */
  mods(): EconomyModifiers {
    const b = totalBonus(this.talents.talents);
    return {
      foodRate: b.foodRate,
      goldRate: b.goldRate,
      moraleRate: b.moraleRate,
      buildRate: b.buildRate,
      combatRate: b.combatRate,
      foodUpkeep: b.foodUpkeep,
      recruitRate: b.recruitRate,
      taskBonus: b.taskBonus,
    };
  }

  advanceDay() {
    const before = this.econ.log.length;
    this.econ = advanceDay(this.econ, this.mods());
    // 人才也日推进
    this.talents = advanceTalentDay(this.talents);
    for (let i = before; i < this.econ.log.length; i++) {
      this.onLog(this.econ.log[i]);
    }
    for (let i = before; i < this.talents.log.length; i++) {
      this.onLog(this.talents.log[i]);
    }
    this.render();
  }

  upgrade() {
    const before = this.econ.log.length;
    this.econ = startUpgrade(this.econ);
    for (let i = before; i < this.econ.log.length; i++) {
      this.onLog(this.econ.log[i]);
    }
    this.render();
  }

  startTaskById(id: string) {
    const before = this.econ.log.length;
    this.econ = startTask(this.econ, id);
    for (let i = before; i < this.econ.log.length; i++) {
      this.onLog(this.econ.log[i]);
    }
    this.render();
  }

  appointById(talentId: number, post: Post) {
    const before = this.talents.log.length;
    this.talents = appoint(this.talents, talentId, post);
    for (let i = before; i < this.talents.log.length; i++) {
      this.onLog(this.talents.log[i]);
    }
    this.render();
  }

  dismissById(talentId: number) {
    this.talents = dismiss(this.talents, talentId);
    this.render();
  }

  // ---------------------------------------------------------------- 渲染

  render() {
    const s = this.econ.settlement;
    el.day.textContent = String(this.econ.day);
    el.food.textContent = String(Math.round(s.food));
    el.gold.textContent = String(Math.round(s.gold));
    el.morale.textContent = s.morale.toFixed(1);
    el.morale.className = s.morale < 30 ? 'val warn' : 'val';
    el.pop.textContent = `${(s.population / 10000).toFixed(1)}万`;
    el.mp.textContent = String(Math.round(s.manpower));
    el.lv.textContent = `${s.stronghold.level} 级${s.stronghold.upgrading ? `（升级中 ${s.stronghold.upgradeDaysLeft} 日）` : ''}`;

    const m = this.mods();
    const netFood = dailyFoodProduction(s, m) - (s.manpower * 0.4 / 1000) * 10 * m.foodUpkeep;
    el.foodRate.textContent = `${netFood >= 0 ? '+' : ''}${netFood.toFixed(0)}`;
    el.foodRate.className = netFood < 0 ? 'val warn' : 'val ok';
    el.goldRate.textContent = `+${dailyGoldIncome(s, m).toFixed(0)}`;

    this.renderTasks();
    this.renderTalents();
  }

  private renderTasks() {
    const rows: string[] = [];
    for (const id of this.econ.activeTaskIds) {
      const task = this.econ.availableTasks.find((t) => t.id === id);
      if (!task) continue;
      const left = this.econ.taskDaysLeft[id] ?? 0;
      rows.push(
        `<div style="display:flex;justify-content:space-between">
           <span>${task.name}</span><span class="val">${Math.max(0, left).toFixed(1)} 日</span>
         </div>`,
      );
    }
    if (rows.length === 0) {
      rows.push('<div style="color:#6e7681">无进行中任务</div>');
    } else {
      // 空闲槽位：可开始新任务
      const free = 2 - this.econ.activeTaskIds.length;
      if (free > 0) {
        const avail = this.econ.availableTasks
          .filter(
            (t) =>
              !this.econ.activeTaskIds.includes(t.id) &&
              !this.econ.completedTaskIds.includes(t.id) &&
              this.econ.settlement.stronghold.level >= t.requiredLevel,
          )
          .slice(0, 4);
        if (avail.length > 0) {
          rows.push('<div style="color:#6e7681;margin-top:4px">可委派：</div>');
          for (const t of avail) {
            rows.push(
              `<button data-task="${t.id}" style="width:100%;margin-top:2px;font-size:11px;padding:3px">
                 ${t.name}（${t.durationDays}日）
               </button>`,
            );
          }
        }
      }
    }
    el.tasks.innerHTML = rows.join('');
    el.tasks.querySelectorAll<HTMLButtonElement>('[data-task]').forEach((b) => {
      b.onclick = () => this.startTaskById(b.dataset.task!);
    });
  }

  private renderTalents() {
    el.talentCount.textContent = String(this.talents.talents.length);
    const rows: string[] = [];

    for (const t of this.talents.talents) {
      const isGuard = t.isPlayerGuard;
      const traits = t.traits.length > 0
        ? t.traits.map((x) => TRAITS[x].name).join('·')
        : '无特长';
      const meta = `${ORIGINS[t.origin].name}·${PERSONALITIES[t.personality].name}·${traits}`;
      const stats = `统${t.stats.command} 智${t.stats.intellect} 管${t.stats.management} 武${t.stats.valor}`;
      const loy = t.loyalty;
      const loyCls = loy < 30 ? ' style="color:#f85149"' : '';

      const postRow = isGuard
        ? '<div class="post-row"><span class="fit" style="color:#3fb950">亲卫（亲自统御）</span></div>'
        : `<div class="post-row">${POST_KEYS.map((p) => {
            const fit = postFit(t, p);
            const on = t.post === p;
            return `<button data-tid="${t.id}" data-post="${p}" class="${on ? 'on' : ''}"
                      title="匹配度 ${(fit * 100).toFixed(0)}%">
                      ${POSTS[p].name}${
                        on ? '' : ` <span class="fit">${(fit * 100) | 0}</span>`
                      }
                    </button>`;
          }).join('')}
          ${t.post ? `<button data-dismiss="${t.id}">免</button>` : ''}
        </div>`;

      rows.push(
        `<div class="talent ${t.post || isGuard ? 'posted' : ''}" data-tid="${t.id}">
           <div class="talent-name">${t.name}
             <span class="talent-meta" style="display:inline">忠诚 <b${loyCls}>${loy}</b></span>
           </div>
           <div class="talent-meta">${meta}</div>
           <div class="talent-stats">${stats}　经验${t.experience}</div>
           ${postRow}
         </div>`,
      );
    }

    el.talents.innerHTML = rows.join('');

    el.talents.querySelectorAll<HTMLButtonElement>('[data-post]').forEach((b) => {
      b.onclick = (ev) => {
        ev.stopPropagation();
        this.appointById(Number(b.dataset.tid), b.dataset.post as Post);
      };
    });
    el.talents.querySelectorAll<HTMLButtonElement>('[data-dismiss]').forEach((b) => {
      b.onclick = (ev) => {
        ev.stopPropagation();
        this.dismissById(Number(b.dataset.dismiss));
      };
    });
  }
}
