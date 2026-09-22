import { describe, expect, it } from 'vitest';
import { describeCell } from './describe';
import { cellOf } from './testState';

const axelsen = { countryRank: 2, entryRank: 3, sport: 'Badminton', event: 'Singles', competitor: 'Viktor Axelsen' };

describe('describeCell', () => {
	it('leaves out a team rank, which is the score', () => {
		expect(describeCell(cellOf(), 'Country')).toBe('Soccer Men');
		expect(describeCell(cellOf(), 'Entry')).toBe('Soccer Men');
	});

	it('names the athlete and their world rank in country mode', () => {
		expect(describeCell(cellOf(axelsen, 2), 'Country')).toBe('Viktor Axelsen, world #3 · Badminton Singles Men');
	});

	it('leaves out a world rank that is the score', () => {
		const cell = cellOf({ ...axelsen, countryRank: 3 }, 3);

		expect(describeCell(cell, 'Country')).toBe('Viktor Axelsen · Badminton Singles Men');
	});

	it('names only the athlete in entry mode', () => {
		expect(describeCell(cellOf(axelsen, 3), 'Entry')).toBe('Viktor Axelsen · Badminton Singles Men');
	});

	it('names the team an inherited rank came from, where an athlete would be', () => {
		const cell = cellOf({ sport: 'Cricket', event: 'ODI', rankedAs: 'West Indies' });

		expect(describeCell(cell, 'Country')).toBe('West Indies · Cricket ODI Men');
		expect(describeCell(cell, 'Entry')).toBe('West Indies · Cricket ODI Men');
	});

	it('names the team before its athlete when an inherited rank has both', () => {
		const cell = cellOf({ ...axelsen, rankedAs: 'England' }, 2);

		expect(describeCell(cell, 'Country')).toBe('England · Viktor Axelsen, world #3 · Badminton Singles Men');
	});

	it('shows a rank the cap replaced', () => {
		const cell = cellOf({ countryRank: 180, entryRank: 180 }, 150);

		expect(describeCell(cell, 'Country')).toBe('#180 · Soccer Men');
		expect(describeCell(cell, 'Entry')).toBe('#180 · Soccer Men');
	});

	it('shows both ranks of a capped athlete in country mode', () => {
		const cell = cellOf({ ...axelsen, countryRank: 160, entryRank: 400 }, 150);

		expect(describeCell(cell, 'Country')).toBe('#160 · Viktor Axelsen, world #400 · Badminton Singles Men');
	});

	it('says unranked', () => {
		const cell = cellOf({ unranked: true, countryRank: null, entryRank: null, sport: null, gender: null }, 150);

		expect(describeCell(cell, 'Country')).toBe('Unranked');
	});
});
