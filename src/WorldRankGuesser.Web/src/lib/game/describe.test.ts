import { describe, expect, it } from 'vitest';
import { describePick } from './describe';
import { pickOf } from './testState';

describe('describePick', () => {
	it('describes a team ranking', () => {
		expect(describePick(pickOf('soccer'), 'Country')).toBe('#3 · Soccer Men');
	});

	it('names the athlete and their world rank in country mode', () => {
		const pick = pickOf(
			'badminton',
			{ countryRank: 2, entryRank: 3, sport: 'Badminton', event: 'Singles', competitor: 'Viktor Axelsen' },
			2
		);

		expect(describePick(pick, 'Country')).toBe('#2 — Viktor Axelsen, world #3 · Badminton Singles Men');
	});

	it('leads with the world rank in entry mode', () => {
		const pick = pickOf(
			'badminton',
			{ countryRank: 2, entryRank: 3, sport: 'Badminton', event: 'Singles', competitor: 'Viktor Axelsen' },
			3
		);

		expect(describePick(pick, 'Entry')).toBe('#3 — Viktor Axelsen, nation #2 · Badminton Singles Men');
	});

	it('says when the score was capped', () => {
		const pick = pickOf('soccer', { countryRank: 180, entryRank: 180 }, 150);

		expect(describePick(pick, 'Country')).toBe('#180 (scores 150) · Soccer Men');
	});

	it('says unranked', () => {
		const pick = pickOf('cricket', { unranked: true, countryRank: null, entryRank: null, sport: null, gender: null }, 150);

		expect(describePick(pick, 'Country')).toBe('Unranked');
	});
});
