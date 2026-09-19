import { describe, expect, it, vi } from 'vitest';
import type { GameState } from '$lib/api/client';
import { GameStore, type GameClient } from './gameStore.svelte';
import { gameState, pickOf } from './testState';

function client(overrides: Partial<GameClient> = {}): GameClient {
	return {
		startGame: vi.fn(async () => gameState()),
		getGame: vi.fn(async () => gameState()),
		pick: vi.fn(async () => ({ state: gameState({ picks: [pickOf('soccer')] }), conflict: false })),
		...overrides
	};
}

describe('GameStore', () => {
	it('start adopts the new game and returns its id', async () => {
		const store = new GameStore(client());

		expect(await store.start()).toBe(gameState().id);
		expect(store.state?.currentCountry?.iso3).toBe('JPN');
		expect(store.busy).toBe(false);
	});

	it('start reports a failure instead of throwing', async () => {
		const store = new GameStore(client({ startGame: vi.fn(async () => Promise.reject(new Error('503'))) }));

		expect(await store.start()).toBeNull();
		expect(store.error).not.toBeNull();
	});

	it('load says whether the game exists', async () => {
		expect(await new GameStore(client()).load('x')).toBe(true);
		expect(await new GameStore(client({ getGame: vi.fn(async () => null) })).load('x')).toBe(false);
	});

	it('pick replaces the state with the server state', async () => {
		const api = client();
		const store = new GameStore(api);
		await store.start();

		await store.pick('soccer');

		expect(api.pick).toHaveBeenCalledWith(gameState().id, 'soccer');
		expect(store.usedCategoryIds.has('soccer')).toBe(true);
		expect(store.pickFor('soccer')?.country.iso3).toBe('JPN');
		expect(store.pickFor('cricket')).toBeUndefined();
	});

	it('shows a pick before the server answers', async () => {
		let answer!: (result: { state: GameState; conflict: boolean }) => void;
		const api = client({ pick: vi.fn(() => new Promise<{ state: GameState; conflict: boolean }>((resolve) => (answer = resolve))) });
		const store = new GameStore(api);
		await store.start();

		const picking = store.pick('soccer');

		expect(store.pickFor('soccer')).toEqual(pickOf('soccer'));
		expect(store.usedCategoryIds.has('soccer')).toBe(true);
		expect(store.state?.picks).toEqual([]); // `state` is still only what the server said

		answer({ state: gameState({ picks: [pickOf('soccer')] }), conflict: false });
		await picking;

		expect(store.pendingPick).toBeNull();
		expect(store.pickFor('soccer')).toEqual(pickOf('soccer'));
	});

	it('takes the pick back when the server did not apply it', async () => {
		const store = new GameStore(client({ pick: vi.fn(async () => ({ state: gameState(), conflict: true })) }));
		await store.start();

		await store.pick('soccer');

		expect(store.pendingPick).toBeNull();
		expect(store.pickFor('soccer')).toBeUndefined();
		expect(store.usedCategoryIds.has('soccer')).toBe(false);
	});

	it('does not send a pick for a used category', async () => {
		const api = client({ getGame: vi.fn(async () => gameState({ picks: [pickOf('soccer')] })) });
		const store = new GameStore(api);
		await store.load('x');

		await store.pick('soccer');

		expect(api.pick).not.toHaveBeenCalled();
	});

	it('adopts the server state on a conflict', async () => {
		const serverState = gameState({ picks: [pickOf('cricket')] });
		const store = new GameStore(client({ pick: vi.fn(async () => ({ state: serverState, conflict: true })) }));
		await store.start();

		await store.pick('soccer');

		expect(store.usedCategoryIds.has('cricket')).toBe(true);
		expect(store.error).toBeNull();
	});

	it('reloads from the server when a pick fails', async () => {
		const reloaded = gameState({ picks: [pickOf('soccer')] });
		const api = client({
			pick: vi.fn(async () => Promise.reject(new Error('network'))),
			getGame: vi.fn(async () => reloaded)
		});
		const store = new GameStore(api);
		await store.start();

		await store.pick('soccer');

		expect(api.getGame).toHaveBeenCalledWith(gameState().id);
		expect(store.state).toEqual(reloaded);
		expect(store.error).not.toBeNull();
		expect(store.busy).toBe(false);
		expect(store.pendingPick).toBeNull();
	});
});
