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

test('on a phone the cards stay in two columns and the game fits the screen', async ({ page }) => {
	await page.setViewportSize({ width: 375, height: 667 });
	await page.emulateMedia({ reducedMotion: 'reduce' });
	await page.goto('/');
	await page.getByRole('button', { name: 'Practice game' }).click();

	const cards = page.locator('.category-card');
	await expect(cards).toHaveCount(10);

	// Fill two cards, so both kinds of card are measured.
	for (let turn = 0; turn < 2; turn++) {
		await page.locator('button.category:enabled').first().click();
		await expect(page.locator('button.category:enabled')).toHaveCount(9 - turn);
	}

	const first = (await cards.nth(0).boundingBox())!;
	const second = (await cards.nth(1).boundingBox())!;
	expect(second.y).toBe(first.y);
	expect(second.x).toBeGreaterThan(first.x);

	const overflow = await page.evaluate(() => ({
		down: document.documentElement.scrollHeight - window.innerHeight,
		across: document.documentElement.scrollWidth - window.innerWidth
	}));
	expect(overflow.down).toBeLessThanOrEqual(0);
	expect(overflow.across).toBeLessThanOrEqual(0);
});

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
