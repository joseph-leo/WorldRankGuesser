import type { Category, Cell, Country, GameState, Pick } from '$lib/api/client';

export type ResultRow = { country: Country; category: Category; cell: Cell };

export type Results = {
	/** One row per country in turn order: the category it ranks best in. */
	best: ResultRow[];
	/** One row per category in card order: the country the best possible game puts there. */
	optimal: ResultRow[];
};

/** A row in the shape of a revealed pick, so a category card can show it. */
export function pickOfRow(row: ResultRow, turnIndex: number): Pick {
	return {
		turnIndex,
		categoryId: row.category.id,
		country: row.country,
		score: row.cell.score,
		wasLate: false,
		result: row.cell
	};
}

/**
 * Lays the revealed grid out for the results page. The server chooses the best and optimal categories;
 * this only looks their cells up. Null until the game is complete.
 */
export function resultsOf(state: GameState): Results | null {
	const grid = state.grid;
	if (!grid) return null;

	const indexOf = new Map(state.categories.map((category, index) => [category.id, index]));

	const rows = (categoryIds: string[]): ResultRow[] =>
		categoryIds.map((categoryId, countryIndex) => {
			const categoryIndex = indexOf.get(categoryId)!;
			return {
				country: grid.countries[countryIndex],
				category: state.categories[categoryIndex],
				cell: grid.cells[countryIndex][categoryIndex]
			};
		});

	return {
		best: rows(grid.bestCategoryIds),
		optimal: rows(grid.optimalCategoryIds).sort((a, b) => indexOf.get(a.category.id)! - indexOf.get(b.category.id)!)
	};
}
