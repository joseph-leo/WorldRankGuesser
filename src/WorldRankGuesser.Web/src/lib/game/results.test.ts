import { describe, expect, it } from 'vitest';
import { pickOfRow, resultsOf } from './results';
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
