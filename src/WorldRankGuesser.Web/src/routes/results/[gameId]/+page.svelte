<script lang="ts">
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import CategoryCard from '$lib/components/CategoryCard.svelte';
	import Flag from '$lib/components/Flag.svelte';
	import { describeCell } from '$lib/game/describe';
	import { GameStore } from '$lib/game/gameStore.svelte';
	import { pickOfRow, resultsOf, type ResultRow } from '$lib/game/results';

	const store = new GameStore();
	let missing = $state(false);

	const results = $derived(store.state ? resultsOf(store.state) : null);

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

	function wasPicked(row: ResultRow): boolean {
		return store.pickFor(row.category.id)?.country.iso3 === row.country.iso3;
	}
</script>

{#if missing}
	<p>This game does not exist. <a href="/">Start a new one</a>.</p>
{:else if store.state?.isComplete && results}
	<h1>Your score: <span data-testid="total">{store.state.totalScore}</span></h1>
	<p class="muted">
		The best possible score for these ten countries was
		<strong data-testid="optimal">{store.state.optimalScore}</strong>.
	</p>

	<div class="cards">
		{#each store.state.categories as category (category.id)}
			<CategoryCard {category} pick={store.pickFor(category.id)} rankMode={store.state.rankMode} testid="result-card" />
		{/each}
	</div>

	<h2>Each country's best sport</h2>
	<ol>
		{#each results.best as row (row.country.iso3)}
			<li data-testid="best-row">
				<Flag iso2={row.country.iso2} size="1.5rem" label={row.country.name} />
				<div class="what">
					<div>
						<strong>{row.country.name}</strong> · {row.category.name}
						{#if wasPicked(row)}<span class="badge">your pick</span>{/if}
					</div>
					<div class="muted detail">{describeCell(row.cell, store.state.rankMode)}</div>
				</div>
				<strong class="score">{row.cell.score}</strong>
			</li>
		{/each}
	</ol>

	<h2>The best possible game</h2>
	<p class="muted">
		Every sport can be used once, so a country beaten in its best sport takes the next one where it costs least.
		This adds up to {store.state.optimalScore}.
	</p>
	<div class="cards optimal">
		{#each results.optimal as row, index (row.category.id)}
			<CategoryCard
				category={row.category}
				pick={pickOfRow(row, index)}
				rankMode={store.state.rankMode}
				badge={wasPicked(row) ? 'your pick' : undefined}
				testid="optimal-row"
			/>
		{/each}
	</div>

	<button class="primary" disabled={store.busy} onclick={playAgain}>Play again</button>

	{#if store.error}
		<p class="error" role="alert">{store.error}</p>
	{/if}
{:else}
	<p class="muted">Loading…</p>
{/if}

<style>
	h2 {
		font-size: 1.1rem;
		margin: 2rem 0 0.5rem;
	}

	ol {
		list-style: none;
		padding: 0;
		margin: 0 0 1.5rem;
		display: grid;
		gap: 0.5rem;
	}

	li {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.what {
		flex: 1;
		min-width: 0;
	}

	.detail {
		font-size: 0.85rem;
	}

	.optimal {
		margin-bottom: 1.5rem;
	}

	.score {
		font-variant-numeric: tabular-nums;
	}
</style>
