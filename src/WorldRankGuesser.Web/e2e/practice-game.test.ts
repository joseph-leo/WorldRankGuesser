import { expect, test } from '@playwright/test';

test('a full practice game ends on the results page', async ({ page }) => {
	await page.emulateMedia({ reducedMotion: 'reduce' }); // no spin, so the test does not wait on animation
	await page.goto('/');

	await page.getByRole('button', { name: 'Practice game' }).click();
	await expect(page).toHaveURL(/\/play\/[0-9a-f-]{36}$/);

	const categories = 10;
	await expect(page.locator('.category-card')).toHaveCount(categories); // waits for the game to load

	for (let turn = 0; turn < categories; turn++) {
		await page.locator('button.category:enabled').first().click();
		await expect(page.locator('.category-card.filled')).toHaveCount(turn + 1);

		if (turn < categories - 1) {
			// No rank or score anywhere on a filled card until the game is over.
			await expect(page.locator('.category-card.filled', { hasText: /\d/ })).toHaveCount(0);
		}
	}

	await expect(page).toHaveURL(/\/results\//);
	await expect(page.getByTestId('total')).toHaveText(/^\d+$/);
	await expect(page.getByTestId('optimal')).toHaveText(/^\d+$/);

	// The completed grid, now with every score.
	await expect(page.getByTestId('result-card').filter({ hasText: /\d/ })).toHaveCount(categories);

	await expect(page.getByTestId('best-row')).toHaveCount(categories);

	// The optimal chart adds up to the optimal score above it.
	await expect(page.getByTestId('optimal-row')).toHaveCount(categories);
	const scores = await page.getByTestId('optimal-row-score').allTextContents();
	const optimal = Number(await page.getByTestId('optimal').textContent());
	expect(scores.reduce((sum, score) => sum + Number(score), 0)).toBe(optimal);});

test('a refresh in the middle of a game resumes it', async ({ page }) => {
	await page.emulateMedia({ reducedMotion: 'reduce' });
	await page.goto('/');
	await page.getByRole('button', { name: 'Practice game' }).click();
	await page.locator('button.category:enabled').first().click();
	await expect(page.locator('.category-card.filled')).toHaveCount(1);
	// The card fills before the server answers; the other cards open again only once it has.
	await expect(page.locator('button.category:enabled')).toHaveCount(9);

	await page.reload();

	await expect(page.locator('.category-card.filled')).toHaveCount(1);
	await expect(page.locator('button.category:enabled')).toHaveCount(9);
});
