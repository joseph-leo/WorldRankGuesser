import * as api from '$lib/api/client';
import type { GameState, Pick } from '$lib/api/client';

export type GameClient = {
	startGame: typeof api.startGame;
	getGame: typeof api.getGame;
	pick: typeof api.pick;
};

/**
 * Holds the latest state the server sent, plus the one pick that is in flight so its card fills without
 * waiting for the round trip. There are no game rules here: every change to `state` is a server response
 * replacing it.
 */
export class GameStore {
	state = $state<GameState | null>(null);
	pendingPick = $state<Pick | null>(null);
	busy = $state(false);
	error = $state<string | null>(null);

	readonly #client: GameClient;

	constructor(client: GameClient = api) {
		this.#client = client;
	}

	get usedCategoryIds(): Set<string> {
		return new Set(this.#picks.map((p) => p.categoryId));
	}

	pickFor(categoryId: string): Pick | undefined {
		return this.#picks.find((p) => p.categoryId === categoryId);
	}

	/** The server's picks, then the one still in flight. */
	get #picks(): Pick[] {
		const picks = this.state?.picks ?? [];
		return this.pendingPick ? [...picks, this.pendingPick] : picks;
	}

	/** The new game's id, or null (with `error` set) when it could not be started. */
	async start(): Promise<string | null> {
		this.busy = true;
		this.error = null;
		try {
			this.state = await this.#client.startGame();
			return this.state.id;
		} catch {
			this.error = 'Could not start a game. Please try again in a moment.';
			return null;
		} finally {
			this.busy = false;
		}
	}

	/** False when the game does not exist for this player. */
	async load(id: string): Promise<boolean> {
		this.busy = true;
		this.error = null;
		try {
			const loaded = await this.#client.getGame(id);
			if (loaded) this.state = loaded;
			return loaded !== null;
		} catch {
			this.error = 'Could not load the game.';
			return false;
		} finally {
			this.busy = false;
		}
	}

	async pick(categoryId: string): Promise<void> {
		const current = this.state;
		if (!current?.currentCountry || this.busy || this.usedCategoryIds.has(categoryId)) return;

		// Shown at once; the server's answer below replaces it, or takes it back if the pick did not apply.
		this.pendingPick = {
			turnIndex: current.picks.length,
			categoryId,
			country: current.currentCountry,
			score: null,
			wasLate: false,
			result: null
		};
		this.busy = true;
		this.error = null;
		try {
			// A conflict is not an error: the body is the server's truth, and we adopt it.
			this.state = (await this.#client.pick(current.id, categoryId)).state;
		} catch {
			this.error = 'That pick did not go through, so the game was reloaded.';
			try {
				this.state = (await this.#client.getGame(current.id)) ?? current;
			} catch {
				// Still offline: keep what we have; the next action retries.
			}
		} finally {
			this.pendingPick = null;
			this.busy = false;
		}
	}
}
