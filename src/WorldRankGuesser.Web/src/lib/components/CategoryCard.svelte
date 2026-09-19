<script lang="ts">
	import type { Category, Pick } from '$lib/api/client';
	import { describeCell } from '$lib/game/describe';
	import Flag from './Flag.svelte';

	let {
		category,
		pick,
		rankMode,
		disabled = true,
		onpick,
		badge,
		testid
	}: {
		category: Category;
		pick: Pick | undefined;
		rankMode: string;
		disabled?: boolean;
		onpick?: () => void;
		badge?: string;
		testid?: string;
	} = $props();
</script>

<div class="category-card" class:filled={pick !== undefined} data-testid={testid}>
	{#if pick}
		<div class="result">
			<Flag iso2={pick.country.iso2} size="2rem" label={pick.country.name} />
			<div>
				<!-- The server sends what a pick scored only once the game is complete. -->
				{#if pick.result}
					<div class="title">
						{category.name}: <strong data-testid={testid && `${testid}-score`}>{pick.score}</strong>
						{#if badge}<span class="badge">{badge}</span>{/if}
					</div>
					<div class="muted detail">{pick.country.name} · {describeCell(pick.result, rankMode)}</div>
				{:else}
					<div class="title">{category.name}</div>
					<div class="muted detail">{pick.country.name}</div>
				{/if}
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
