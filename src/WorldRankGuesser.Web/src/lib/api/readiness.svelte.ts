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
	#inFlight: Promise<void> | null = null;

	constructor(check: () => Promise<boolean> = api.isReady, { intervalMs = 2000, timeoutMs = 90_000 } = {}) {
		this.#check = check;
		this.#intervalMs = intervalMs;
		this.#timeoutMs = timeoutMs;
	}

	/**
	 * Resolves when the server is ready or the wait has run out; `phase` says which. A check that throws is
	 * treated as not ready rather than as a crash, and a call made while one is already in flight returns that
	 * same wait instead of starting a second one. Call again once it has settled to retry.
	 */
	wait(): Promise<void> {
		if (!this.#inFlight) {
			this.#inFlight = this.#run().finally(() => (this.#inFlight = null));
		}
		return this.#inFlight;
	}

	async #run(): Promise<void> {
		this.phase = 'checking';
		const deadline = Date.now() + this.#timeoutMs;

		// A rejected check (a network error, say) is not ready, same as a false answer — never a crash.
		while (!(await this.#check().catch(() => false))) {
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
