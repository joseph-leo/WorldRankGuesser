/**
 * The flags the spinner shows before landing: `length - 1` decoys, never the final country,
 * never the same flag twice in a row, then the final country.
 */
export function buildSpinSequence(
	decoys: readonly string[],
	finalIso2: string,
	length: number,
	random: () => number = Math.random
): string[] {
	const pool = decoys.filter((code) => code !== finalIso2);
	if (pool.length < 2) return [finalIso2];

	const sequence: string[] = [];
	while (sequence.length < length - 1) {
		const candidate = pool[Math.floor(random() * pool.length)];
		if (candidate !== sequence.at(-1)) sequence.push(candidate);
	}

	sequence.push(finalIso2);
	return sequence;
}
