<script lang="ts">
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import CategoryCard from '$lib/components/CategoryCard.svelte';
	import CountrySpinner from '$lib/components/CountrySpinner.svelte';
	import { GameStore } from '$lib/game/gameStore.svelte';

	const store = new GameStore();

	let missing = $state(false);
	let spinning = $state(true); // cards stay disabled until the spinner has landed on a country

	$effect(() => {
		const id = page.params.gameId;
		if (id) store.load(id).then((found) => (missing = !found));
	});

	$effect(() => {
		if (!store.state?.isComplete) return;

		const id = store.state.id;
		const timer = setTimeout(() => goto(`/results/${id}`), 1200); // long enough to see the last card fill
		return () => clearTimeout(timer);
	});

	async function choose(categoryId: string) {
		spinning = true;
		await store.pick(categoryId);
	}
</script>

{#if missing}
	<p>This game does not exist. <a href="/">Start a new one</a>.</p>
{:else if store.state}
	{#if store.state.currentCountry}
		<CountrySpinner country={store.state.currentCountry} pending={store.busy} onsettled={() => (spinning = false)} />
		<p class="muted turn">Country {store.state.picks.length + 1} of {store.state.categories.length}</p>
	{/if}

	{#if store.error}
		<p class="error" role="alert">{store.error}</p>
	{/if}

	<div class="cards">
		{#each store.state.categories as category (category.id)}
			<CategoryCard
				{category}
				pick={store.pickFor(category.id)}
				rankMode={store.state.rankMode}
				disabled={spinning || store.busy}
				onpick={() => choose(category.id)}
			/>
		{/each}
	</div>
{:else}
	<p class="muted">Loading…</p>
{/if}

<style>
	.turn {
		text-align: center;
		margin: 0 0 1rem;
	}
</style>
