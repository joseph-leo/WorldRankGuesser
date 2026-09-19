import { describe, expect, it } from 'vitest';
import { buildSpinSequence } from './spin';

const decoys = ['AU', 'BR', 'CA', 'DE', 'JP'];

function seeded(seed: number): () => number {
	return () => {
		seed = (seed * 16807) % 2147483647;
		return seed / 2147483647;
	};
}

describe('buildSpinSequence', () => {
	it('has the requested length and lands on the final country', () => {
		const sequence = buildSpinSequence(decoys, 'JP', 12, seeded(1));

		expect(sequence).toHaveLength(12);
		expect(sequence.at(-1)).toBe('JP');
	});

	it('never shows the final country early or the same flag twice in a row', () => {
		for (let seed = 1; seed <= 20; seed++) {
			const sequence = buildSpinSequence(decoys, 'JP', 30, seeded(seed));

			expect(sequence.slice(0, -1)).not.toContain('JP');
			sequence.forEach((code, i) => expect(code).not.toBe(sequence[i - 1]));
		}
	});

	it('is just the final country when there are too few decoys to spin', () => {
		expect(buildSpinSequence(['JP', 'AU'], 'JP', 12)).toEqual(['JP']);
	});
});
