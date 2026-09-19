import * as api from '$lib/api/client';
import type { GameState, Pick } from '$lib/api/client';

export type GameClient = {
	startGame: typeof api.startGame;
	getGame: typeof api.getGame;
	pick: typeof api.pick;
};

/**
 * Holds the latest state the server sent, and nothing else. There are no game rules here:
 * every change is a server response replacing `state`.
 */
export class GameStore {
	state = $state<GameState | null>(null);
	busy = $state(false);
	error = $state<string | null>(null);

	readonly #client: GameClient;

	constructor(client: GameClient = api) {
		this.#client = client;
	}

	get usedCategoryIds(): Set<string> {
		return new Set(this.state?.picks.map((p) => p.categoryId) ?? []);
	}

	pickFor(categoryId: string): Pick | undefined {
		return this.state?.picks.find((p) => p.categoryId === categoryId);
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
		if (!current || this.busy || current.isComplete || this.usedCategoryIds.has(categoryId)) return;

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
			this.busy = false;
		}
	}
}
