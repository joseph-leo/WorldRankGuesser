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
		<div class="result" class:scored={pick.result != null}>
			<Flag iso2={pick.country.iso2} size="var(--flag-size)" label={pick.country.name} />
			<div class="text">
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
		container-type: inline-size; /* the card lays itself out by its own width, not the viewport's */
		--flag-size: 2rem;
	}

	button {
		width: 100%;
	}

	.result {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		width: 100%;
		box-sizing: border-box;
		padding: 0.5rem 0.75rem;
		border: 1px solid var(--line);
		border-radius: 0.5rem;
	}

	.text {
		min-width: 0;
		overflow-wrap: anywhere;
	}

	.detail {
		font-size: 0.85rem;
	}

	/* A narrow card, as in the two-column grid on a phone. */
	@container (max-width: 13rem) {
		button {
			padding: 0.5rem;
			font-size: 0.9rem;
		}

		.result {
			--flag-size: 1.5rem;
			gap: 0.5rem;
			padding: 0.4rem 0.5rem;
		}

		.title {
			font-size: 0.9rem;
		}

		.detail {
			font-size: 0.75rem;
		}

		/* A scored card carries a long detail line, which needs the card's full width. */
		.result.scored {
			flex-direction: column;
			justify-content: center;
			gap: 0.25rem;
			text-align: center;
		}
	}
</style>
