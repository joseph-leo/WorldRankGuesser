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
	}

	await expect(page).toHaveURL(/\/results\//);
	await expect(page.getByTestId('total')).toHaveText(/^\d+$/);
	await expect(page.getByTestId('optimal')).toHaveText(/^\d+$/);
});

test('a refresh in the middle of a game resumes it', async ({ page }) => {
	await page.emulateMedia({ reducedMotion: 'reduce' });
	await page.goto('/');
	await page.getByRole('button', { name: 'Practice game' }).click();
	await page.locator('button.category:enabled').first().click();
	await expect(page.locator('.category-card.filled')).toHaveCount(1);

	await page.reload();

	await expect(page.locator('.category-card.filled')).toHaveCount(1);
	await expect(page.locator('button.category:enabled')).toHaveCount(9);
});
