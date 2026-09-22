import type { components } from './schema';

export type GameState = components['schemas']['GameStateDto'];
export type Pick = components['schemas']['PickDto'];
export type Category = components['schemas']['CategoryDto'];
export type Country = components['schemas']['CountryDto'];
export type Cell = components['schemas']['CellDto'];

export class ApiError extends Error {
	readonly status: number;

	constructor(status: number, message: string) {
		super(message);
		this.status = status;
	}
}

function send(path: string, init?: RequestInit): Promise<Response> {
	return fetch(path, {
		credentials: 'same-origin',
		headers: { 'Content-Type': 'application/json' },
		...init
	});
}

export async function startGame(): Promise<GameState> {
	const response = await send('/api/games', { method: 'POST', body: JSON.stringify({ mode: 'practice' }) });
	if (!response.ok) throw new ApiError(response.status, 'Could not start a game.');

	return response.json();
}

/** Null when the game does not exist or is not this player's. */
export async function getGame(id: string): Promise<GameState | null> {
	const response = await send(`/api/games/${id}`);
	if (response.status === 404) return null;
	if (!response.ok) throw new ApiError(response.status, 'Could not load the game.');

	return response.json();
}

/** A 409 is not an error: its body is the server's current state, which the caller adopts. */
export async function pick(id: string, categoryId: string): Promise<{ state: GameState; conflict: boolean }> {
	const response = await send(`/api/games/${id}/picks`, { method: 'POST', body: JSON.stringify({ categoryId }) });
	if (response.ok || response.status === 409) {
		return { state: await response.json(), conflict: response.status === 409 };
	}

	throw new ApiError(response.status, 'The pick was rejected.');
}

/** Whether the API has rankings and can reach its database (`/readyz`). False on any failure, a network error included. */
export async function isReady(): Promise<boolean> {
	try {
		return (await fetch('/readyz', { credentials: 'same-origin' })).ok;
	} catch {
		return false;
	}
}
