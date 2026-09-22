import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vitest/config';

export default defineConfig({
	plugins: [sveltekit()],
	server: {
		// Same origin in development too, so the player cookie behaves as it does in production.
		// /readyz is proxied as well: the start screen asks it before offering a game, and in production the API serves it.
		proxy: { '/api': 'http://localhost:5170', '/readyz': 'http://localhost:5170' }
	},
	test: {
		include: ['src/**/*.test.ts'],
		environment: 'node'
	}
});
