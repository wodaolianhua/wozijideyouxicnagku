/**
 * 跨语言一致性检查：TypeScript 实现 vs Python 参考实现
 *
 * 用法：
 *   node dist_check.mjs           （先编译 combat.ts）
 *   python cross_lang_fixtures.py （生成 Python 侧实测值 JSON）
 *   node cross_lang_check.mjs     （对比两侧 JSON）
 *
 * 验收标准：所有数值必须逐位一致（浮点 6 位小数）。
 * 任何一项不一致 → 迁移未完成，必须以 Python 版为参考修改 TS 版。
 */

import { readFileSync, existsSync } from 'node:fs';
import {
  ARMY_CLASS,
  SOLDIERS,
  TERRAINS,
  casualtyMatrix,
  counterMatrix,
  effectivePower,
  hitRate,
  damageMitigation,
  casualtyRate,
  turnsToKill,
  resolveDuel,
  marchCost,
  siegeModifier,
  moraleAfterCombat,
} from './src/model/combat.ts';

const PY_FIXTURES = 'python/fixtures.json';

function round6(v: number): number {
  return Math.round(v * 1e6) / 1e6;
}

function buildTsFixtures() {
  const out: Record<string, unknown> = {
    panels: {},
    counterMatrix: {},
    effectivePower: {},
    hitRate: {},
    mitigation: {},
    casualtyRate: {},
    turnsToKill: {},
    duels: {},
    misc: {},
  };

  for (const cls of ARMY_CLASS) {
    const s = SOLDIERS[cls];
    out.panels[cls] = [s.str, s.mor, s.sta, s.baseHit, s.deff, s.cost];
  }

  const cm = counterMatrix();
  for (const a of ARMY_CLASS) {
    for (const d of ARMY_CLASS) {
      out.counterMatrix[`${a}|${d}`] = round6(cm[a][d]);
    }
  }

  for (const cls of ARMY_CLASS) {
    for (const tname of Object.keys(TERRAINS)) {
      const t = TERRAINS[tname];
      const s = SOLDIERS[cls];
      const k = `${cls}|${tname}`;
      out.effectivePower[k] = round6(effectivePower(s, t));
      out.hitRate[k] = round6(hitRate(s, t));
      out.mitigation[k] = round6(damageMitigation(s));
    }
  }

  for (const tname of Object.keys(TERRAINS)) {
    const mat = casualtyMatrix(tname);
    for (const a of ARMY_CLASS) {
      for (const d of ARMY_CLASS) {
        out.casualtyRate[`${tname}|${a}|${d}`] = round6(mat[a][d]);
      }
    }
  }

  for (const cls of ARMY_CLASS) {
    const tt = turnsToKill(SOLDIERS[cls], SOLDIERS[cls], TERRAINS.plain);
    out.turnsToKill[cls] = round6(tt);
  }

  for (const a of ARMY_CLASS) {
    for (const d of ARMY_CLASS) {
      for (const [tn, count] of [
        ['plain', 1000],
        ['hill', 1000],
        ['forest', 1000],
        ['plain', 100],
        ['hill', 4000],
      ] as [string, number][]) {
        const r = resolveDuel(SOLDIERS[a], SOLDIERS[d], TERRAINS[tn], count, count);
        out.duels[`${a}|${d}|${tn}|${count}`] = [
          r.winner,
          r.turns,
          r.attackerLeft,
          r.defenderLeft,
          r.attackerRouted,
          r.defenderRouted,
        ];
      }
    }
  }

  for (const tname of Object.keys(TERRAINS)) {
    for (const dist of [5, 10, 20]) {
      for (const troops of [500, 1000, 5000]) {
        out.misc[`march|${tname}|${dist}|${troops}`] = round6(
          marchCost(dist, TERRAINS[tname], troops),
        );
      }
    }
  }
  for (const lvl of [1, 2, 3, 4, 5]) {
    for (const cls of ARMY_CLASS) {
      out.misc[`siege|${cls}|${lvl}`] = round6(siegeModifier(cls, lvl));
    }
  }
  for (const c of [0, 0.1, 0.3, 0.5]) {
    for (const won of [true, false]) {
      out.misc[`morale|${c.toFixed(1)}|${won ? 'True' : 'False'}`] = round6(
        moraleAfterCombat(60, c, won),
      );
    }
  }

  return out;
}

function flatten(obj: Record<string, unknown>, prefix = ''): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v !== null && typeof v === 'object' && !Array.isArray(v)) {
      Object.assign(out, flatten(v as Record<string, unknown>, key));
    } else {
      out[key] = v;
    }
  }
  return out;
}

const mode = process.argv[2];

if (mode === 'emit') {
  const json = JSON.stringify(buildTsFixtures(), null, 2);
  const { writeFileSync } = await import('node:fs');
  writeFileSync('ts_fixtures.json', json, 'utf8');
  console.log('已写出ts_fixtures.json');
} else if (mode === 'check') {
  if (!existsSync(PY_FIXTURES)) {
    console.error(`缺少 ${PY_FIXTURES}，请先运行 python cross_lang_fixtures.py`);
    process.exit(2);
  }
  const py = JSON.parse(readFileSync(PY_FIXTURES, 'utf8'));
  const ts = buildTsFixtures();
  const pyFlat = flatten(py);
  const tsFlat = flatten(ts);

  const allKeys = new Set([...Object.keys(pyFlat), ...Object.keys(tsFlat)]);
  const mismatches: string[] = [];
  let checked = 0;

  for (const k of allKeys) {
    const pv = pyFlat[k];
    const tv = tsFlat[k];
    if (pv === undefined) {
      mismatches.push(`${k}: Python 侧缺失，TS=${JSON.stringify(tv)}`);
      continue;
    }
    if (tv === undefined) {
      mismatches.push(`${k}: TS 侧缺失，Python=${JSON.stringify(pv)}`);
      continue;
    }
    if (Array.isArray(pv) || Array.isArray(tv)) {
      const ps = JSON.stringify(pv);
      const tvs = JSON.stringify(tv);
      if (ps !== tvs) mismatches.push(`${k}: Python=${ps} TS=${tvs}`);
      checked++;
      continue;
    }
    const a = round6(Number(pv));
    const b = round6(Number(tv));
    if (Math.abs(a - b) > 1e-9) {
      mismatches.push(`${k}: Python=${a} TS=${b} 差=${Math.abs(a - b).toExponential(3)}`);
    }
    checked++;
  }

  if (mismatches.length === 0) {
    console.log(`跨语言一致性检查通过：${checked} 项数值全部相同（浮点 6 位小数）`);
    process.exit(0);
  } else {
    console.error(`跨语言一致性检查失败：${mismatches.length}/${checked} 项不一致`);
    for (const m of mismatches.slice(0, 40)) console.error('  ' + m);
    process.exit(1);
  }
} else {
  console.error('用法: node cross_lang_check.mjs emit|check');
  process.exit(2);
}