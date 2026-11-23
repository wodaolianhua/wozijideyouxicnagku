// ============================================================================
//  Scene/CityText.ts —— 城池悬停卡文案。
//
//  从旧的 CityMesh.ts 拆出来：这段只拼 HTML 字符串，跟渲染无关，
//  留在 3D 网格文件里会被一起删掉。2D 化后由 Hud 直接调用。
// ============================================================================

import type { City, WorldState } from '../Core/Types.js';

export function cityHoverText(city: Readonly<City>, state: Readonly<WorldState>): string {
  const owner = state.factions.find((f) => f.id === city.ownerFactionId);
  const armies = state.armies.filter((a) => a.positionCityId === city.id);
  const lines = [
    `<b>${city.isCapital ? '★' : ''}${city.name}</b>（${owner ? owner.name : '无主'}）`,
    `人口 ${city.population}/${city.maxPopulation} · 城防 ${city.defense}/${city.maxDefense}（等级 ${city.fortifyLevel}）`,
    `屯田等级 ${city.farmLevel} · 相邻 ${city.adjacentCityIds.map((id) => state.cities.find((c) => c.id === id)?.name ?? id).join('、')}`,
  ];
  for (const a of armies) {
    lines.push(`驻军 #${a.id} ${a.soldiers} 人 · 士气 ${a.morale}`);
  }
  return lines.join('<br/>');
}
