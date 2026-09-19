import type { Pick } from '$lib/api/client';

/** One line under a filled category card, for example "#2 — Viktor Axelsen, world #3 · Badminton Singles Men". */
export function describePick(pick: Pick, rankMode: string): string {
	const result = pick.result;
	if (result.unranked) return 'Unranked';

	const entryMode = rankMode === 'Entry';
	const lead = entryMode ? result.entryRank : result.countryRank;
	const other = entryMode ? `nation #${result.countryRank}` : `world #${result.entryRank}`;

	const who = result.competitor ? ` — ${result.competitor}, ${other}` : '';
	const capped = lead != null && lead > pick.score ? ` (scores ${pick.score})` : '';
	const feed = [result.sport, result.event, result.gender].filter(Boolean).join(' ');

	return `#${lead}${who}${capped} · ${feed}`;
}
