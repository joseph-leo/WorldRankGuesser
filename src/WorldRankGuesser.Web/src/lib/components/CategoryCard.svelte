<script lang="ts">
	import type { Category, Pick } from '$lib/api/client';
	import { describePick } from '$lib/game/describe';
	import Flag from './Flag.svelte';

	let {
		category,
		pick,
		rankMode,
		disabled,
		onpick
	}: { category: Category; pick: Pick | undefined; rankMode: string; disabled: boolean; onpick: () => void } = $props();
</script>

<div class="category-card" class:filled={pick !== undefined}>
	{#if pick}
		<div class="result">
			<Flag iso2={pick.country.iso2} size="2rem" label={pick.country.name} />
			<div>
				<div class="title">{category.name}: <strong>{pick.score}</strong></div>
				<div class="muted detail">{pick.country.name} · {describePick(pick, rankMode)}</div>
			</div>
		</div>
	{:else}
		<button class="category" {disabled} onclick={onpick}>{category.name}</button>
	{/if}
</div>

<style>
	.category-card {
		min-height: 4rem;
		display: flex;
		align-items: stretch;
	}

	button {
		width: 100%;
	}

	.result {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		width: 100%;
		padding: 0.5rem 0.75rem;
		border: 1px solid var(--line);
		border-radius: 0.5rem;
	}

	.detail {
		font-size: 0.85rem;
	}
</style>
