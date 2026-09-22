import type { Cell, GameState, Pick } from '$lib/api/client';

export function gameState(overrides: Partial<GameState> = {}): GameState {
	return {
		id: '11111111-1111-1111-1111-111111111111',
		mode: 'Practice',
		dailyDate: null,
		rankMode: 'Country',
		cap: 150,
		categories: [
			{ id: 'soccer', name: 'Soccer' },
			{ id: 'cricket', name: 'Cricket' }
		],
		picks: [],
		currentCountry: { iso3: 'JPN', iso2: 'JP', name: 'Japan' },
		deadline: null,
		serverNow: '2026-09-19T12:00:00Z',
		isComplete: false,
		totalScore: null,
		optimalScore: null,
		grid: null,
		...overrides
	};
}

export function cellOf(overrides: Partial<Cell> = {}, score = 3): Cell {
	return {
		score,
		countryRank: 3,
		entryRank: 3,
		unranked: false,
		sport: 'Soccer',
		event: null,
		gender: 'Men',
		competitor: null,
		rankedAs: null,
		...overrides
	};
}

/** A pick as the server sends it during a game: what it scored is hidden until the game is complete. */
export function pickOf(categoryId: string): Pick {
	return {
		turnIndex: 0,
		categoryId,
		country: { iso3: 'JPN', iso2: 'JP', name: 'Japan' },
		score: null,
		wasLate: false,
		result: null
	};
}
