<script lang="ts">
	import { goto } from '$app/navigation';
	import { GameStore } from '$lib/game/gameStore.svelte';

	const store = new GameStore();

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

<button class="primary" disabled={store.busy} onclick={startPractice}>Practice game</button>

{#if store.error}
	<p class="error" role="alert">{store.error}</p>
{/if}
