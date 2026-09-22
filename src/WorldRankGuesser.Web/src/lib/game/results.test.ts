import { describe, expect, it } from 'vitest';
import { marksOf, pickOfRow, resultsOf } from './results';
import { cellOf, gameState } from './testState';

const denmark = { iso3: 'DNK', iso2: 'DK', name: 'Denmark' };
const korea = { iso3: 'KOR', iso2: 'KR', name: 'Korea' };
const badminton = { id: 'badminton', name: 'Badminton' };
const soccer = { id: 'soccer', name: 'Soccer' };

// Denmark's best is badminton, but Korea is 1 there, so the optimal chart gives Denmark soccer.
const complete = gameState({
	categories: [badminton, soccer],
	currentCountry: null,
	isComplete: true,
	totalScore: 93,
	optimalScore: 21,
	grid: {
		countries: [denmark, korea],
		cells: [
			[cellOf({}, 3), cellOf({}, 20)],
			[cellOf({}, 1), cellOf({}, 90)]
		],
		bestCategoryIds: ['badminton', 'badminton'],
		optimalCategoryIds: ['soccer', 'badminton']
	}
});

describe('resultsOf', () => {
	it('is null until the grid is revealed', () => {
		expect(resultsOf(gameState())).toBeNull();
	});

	it("lists each country's best category in turn order", () => {
		const best = resultsOf(complete)!.best;

		expect(best.map((row) => [row.country.name, row.category.name, row.cell.score])).toEqual([
			['Denmark', 'Badminton', 3],
			['Korea', 'Badminton', 1]
		]);
	});

	it('lists the optimal assignment in category order', () => {
		const optimal = resultsOf(complete)!.optimal;

		expect(optimal.map((row) => [row.category.name, row.country.name, row.cell.score])).toEqual([
			['Badminton', 'Korea', 1],
			['Soccer', 'Denmark', 20]
		]);
	});
});

describe('marksOf', () => {
	const results = resultsOf(complete)!;
	const picked = (country: typeof denmark, categoryId: string, score: number) => ({
		...pickOfRow({ country, category: badminton, cell: cellOf({}, score) }, 0),
		categoryId
	});

	it('marks a pick that went to its optimal category', () => {
		expect(marksOf(results, picked(denmark, 'soccer', 20))).toEqual(['optimal']);
	});

	it("marks a pick that scored its country's best, and both when it is optimal too", () => {
		expect(marksOf(results, picked(denmark, 'badminton', 3))).toEqual(['best']);
		expect(marksOf(results, picked(korea, 'badminton', 1))).toEqual(['best', 'optimal']);
	});

	it('marks a best score reached in another category, since the player could not have done better', () => {
		expect(marksOf(results, picked(denmark, 'soccer', 3))).toContain('best');
	});

	it('marks nothing otherwise', () => {
		expect(marksOf(results, picked(korea, 'soccer', 90))).toEqual([]);
	});
});

describe('pickOfRow', () => {
	it('shapes a row as a revealed pick, so a category card can show it', () => {
		const row = resultsOf(complete)!.optimal[1];

		expect(pickOfRow(row, 1)).toEqual({
			turnIndex: 1,
			categoryId: 'soccer',
			country: denmark,
			score: 20,
			wasLate: false,
			result: row.cell
		});
	});
});
