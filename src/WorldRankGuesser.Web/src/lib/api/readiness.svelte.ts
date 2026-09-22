import * as api from './client';

export type ReadinessPhase = 'checking' | 'waking' | 'ready' | 'unavailable';

/**
 * Whether a game can be started: the start screen asks /readyz before offering one. A scaled-to-zero app and a
 * paused database take a while after a quiet spell, and one /readyz call can itself wait on the database, so the
 * calls go one at a time, `intervalMs` after each answer, for `timeoutMs`. This is server state, not a game rule.
 */
export class ServerReadiness {
	phase = $state<ReadinessPhase>('checking');

	readonly #check: () => Promise<boolean>;
	readonly #intervalMs: number;
	readonly #timeoutMs: number;

	constructor(check: () => Promise<boolean> = api.isReady, { intervalMs = 2000, timeoutMs = 90_000 } = {}) {
		this.#check = check;
		this.#intervalMs = intervalMs;
		this.#timeoutMs = timeoutMs;
	}

	/** Resolves when the server is ready or the wait has run out; `phase` says which. Call again to retry. */
	async wait(): Promise<void> {
		this.phase = 'checking';
		const deadline = Date.now() + this.#timeoutMs;

		while (!(await this.#check())) {
			if (Date.now() >= deadline) {
				this.phase = 'unavailable';
				return;
			}
			this.phase = 'waking';
			await new Promise((resolve) => setTimeout(resolve, this.#intervalMs));
		}

		this.phase = 'ready';
	}
}
