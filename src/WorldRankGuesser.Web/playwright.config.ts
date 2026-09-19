import { defineConfig } from '@playwright/test';

export default defineConfig({
	testDir: 'e2e',
	use: { baseURL: 'http://localhost:5173' },
	webServer: [
		{
			command: 'dotnet run --project ../WorldRankGuesser.Api',
			url: 'http://localhost:5170/readyz',
			reuseExistingServer: true,
			timeout: 120_000
		},
		{
			command: 'npm run dev -- --port 5173 --strictPort',
			url: 'http://localhost:5173',
			reuseExistingServer: true
		}
	]
});
