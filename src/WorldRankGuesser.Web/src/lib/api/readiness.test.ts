import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ServerReadiness } from './readiness.svelte';

describe('ServerReadiness', () => {
	beforeEach(() => vi.useFakeTimers());
	afterEach(() => vi.useRealTimers());

	it('is ready after one answer when the server is up', async () => {
		const check = vi.fn(async () => true);
		const server = new ServerReadiness(check);

		await server.wait();

		expect(server.phase).toBe('ready');
		expect(check).toHaveBeenCalledTimes(1);
	});

	it('says it is waking the server and asks again two seconds after each answer', async () => {
		const answers = [false, false, true];
		const check = vi.fn(async () => answers.shift() ?? true);
		const server = new ServerReadiness(check);

		const waiting = server.wait();
		await vi.advanceTimersByTimeAsync(0);
		expect(server.phase).toBe('waking');
		expect(check).toHaveBeenCalledTimes(1);

		await vi.advanceTimersByTimeAsync(2000);
		expect(check).toHaveBeenCalledTimes(2);
		expect(server.phase).toBe('waking');

		await vi.advanceTimersByTimeAsync(2000);
		await waiting;
		expect(server.phase).toBe('ready');
		expect(check).toHaveBeenCalledTimes(3);
	});

	it('gives up after 90 seconds and can be asked again', async () => {
		const check = vi.fn(async () => false);
		const server = new ServerReadiness(check);

		const waiting = server.wait();
		await vi.advanceTimersByTimeAsync(90_000);
		await waiting;

		expect(server.phase).toBe('unavailable');
		expect(check.mock.calls.length).toBeGreaterThanOrEqual(45);

		check.mockResolvedValue(true);
		await server.wait();
		expect(server.phase).toBe('ready');
	});

	it('waits for each answer before asking again, so a slow /readyz is never stacked', async () => {
		let answer!: (ready: boolean) => void;
		const check = vi.fn(() => new Promise<boolean>((resolve) => (answer = resolve)));
		const server = new ServerReadiness(check);

		const waiting = server.wait();
		await vi.advanceTimersByTimeAsync(10_000);
		expect(check).toHaveBeenCalledTimes(1); // still waiting on the first answer

		answer(true);
		await waiting;
		expect(server.phase).toBe('ready');
	});
});
