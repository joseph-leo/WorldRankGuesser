import { defineConfig } from '@playwright/test';

// With E2E_BASE_URL set, the tests run against an app that is already up (the compose stack, staging) and nothing
// is started here. Without it, the API and Vite start as in development.
// An empty value counts as unset, so a workflow that exports the variable blank still starts the local servers.
const deployed = process.env.E2E_BASE_URL || undefined;

export default defineConfig({
	testDir: 'e2e',
	use: { baseURL: deployed ?? 'http://localhost:5173' },
	webServer: deployed
		? undefined
		: [
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
