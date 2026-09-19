<script lang="ts">
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import Flag from '$lib/components/Flag.svelte';
	import { describePick } from '$lib/game/describe';
	import { GameStore } from '$lib/game/gameStore.svelte';

	const store = new GameStore();
	let missing = $state(false);

	$effect(() => {
		const id = page.params.gameId;
		if (!id) return;

		store.load(id).then((found) => {
			missing = !found;
			if (found && !store.state?.isComplete) goto(`/play/${id}`); // not finished: back to the game
		});
	});

	async function playAgain() {
		const id = await store.start();
		if (id) await goto(`/play/${id}`);
	}
</script>

{#if missing}
	<p>This game does not exist. <a href="/">Start a new one</a>.</p>
{:else if store.state?.isComplete}
	<h1>Your score: <span data-testid="total">{store.state.totalScore}</span></h1>
	<p class="muted">
		The best possible score for these ten countries was
		<strong data-testid="optimal">{store.state.optimalScore}</strong>.
	</p>

	<ol>
		{#each store.state.picks as pick (pick.turnIndex)}
			<li>
				<Flag iso2={pick.country.iso2} size="1.5rem" label={pick.country.name} />
				<span>
					<strong>{pick.country.name}</strong> in {store.state.categories.find((c) => c.id === pick.categoryId)?.name}:
					<strong>{pick.score}</strong>
					<span class="muted">({describePick(pick, store.state.rankMode)})</span>
				</span>
			</li>
		{/each}
	</ol>

	<button class="primary" disabled={store.busy} onclick={playAgain}>Play again</button>

	{#if store.error}
		<p class="error" role="alert">{store.error}</p>
	{/if}
{:else}
	<p class="muted">Loading…</p>
{/if}

<style>
	ol {
		list-style: none;
		padding: 0;
		display: grid;
		gap: 0.5rem;
	}

	li {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}
</style>
