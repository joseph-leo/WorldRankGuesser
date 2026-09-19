import type { Cell } from '$lib/api/client';

/**
 * One line about a country in a category, for example "Viktor Axelsen, world #3 · Badminton Singles Men".
 * It is always shown beside the score, so a rank that equals the score is left out.
 */
export function describeCell(cell: Cell, rankMode: string): string {
	if (cell.unranked) return 'Unranked';

	const entryMode = rankMode === 'Entry';
	const scored = entryMode ? cell.entryRank : cell.countryRank;
	const capped = scored != null && scored !== cell.score ? `#${scored}` : null;

	const world = !entryMode && cell.entryRank !== cell.score ? `, world #${cell.entryRank}` : '';
	const who = cell.competitor ? `${cell.competitor}${world}` : null;
	const feed = [cell.sport, cell.event, cell.gender].filter(Boolean).join(' ');

	return [capped, who, feed].filter(Boolean).join(' · ');
}
