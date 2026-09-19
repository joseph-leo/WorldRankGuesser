import adapter from '@sveltejs/adapter-static';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	preprocess: vitePreprocess(),
	kit: {
		// A single-page app: every route falls back to index.html, which the API serves.
		adapter: adapter({ fallback: 'index.html' })
	}
};

export default config;
