<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { ServerReadiness } from '$lib/api/readiness.svelte';
	import { GameStore } from '$lib/game/gameStore.svelte';

	const store = new GameStore();
	const server = new ServerReadiness();

	// Ask before offering a game: after a quiet spell the server and its database take a while to wake.
	onMount(() => {
		server.wait();
	});

	async function startPractice() {
		const id = await store.start();
		if (id) await goto(`/play/${id}`);
	}
</script>

<h1>Guess where they rank</h1>
<p class="muted">
	You are dealt ten countries, one at a time. Put each into a different sport. You score the country's world rank in
	that sport, and the lowest total wins. Unranked, or ranked below 150th, scores 150.
</p>

{#if server.phase === 'unavailable'}
	<p class="error" role="alert">
		The server is not answering. It may be resting until the 1st of the month; please try again in a moment.
	</p>
	<button class="primary" onclick={() => server.wait()}>Try again</button>
{:else}
	<button class="primary" disabled={store.busy || server.phase !== 'ready'} onclick={startPractice}>Practice game</button>
	{#if server.phase === 'waking'}
		<p class="muted" role="status">Waking up the server… this can take up to a minute.</p>
	{/if}
{/if}

{#if store.error}
	<p class="error" role="alert">{store.error}</p>
{/if}
