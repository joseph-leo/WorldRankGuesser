<script lang="ts">
	import type { Category, Pick } from '$lib/api/client';
	import { describeCell } from '$lib/game/describe';
	import type { Mark } from '$lib/game/results';
	import Flag from './Flag.svelte';

	let {
		category,
		pick,
		rankMode,
		disabled = true,
		onpick,
		marks = [],
		testid
	}: {
		category: Category;
		pick: Pick | undefined;
		rankMode: string;
		disabled?: boolean;
		onpick?: () => void;
		/** Shown in the card's top right corner: the glyph inside each mark, and what it means. */
		marks?: { kind: Mark; glyph: string; label: string }[];
		testid?: string;
	} = $props();
</script>

<div class="category-card" class:filled={pick !== undefined} data-testid={testid}>
	{#if pick}
		{#if marks.length > 0}
			<div class="marks">
				{#each marks as mark (mark.kind)}
					<span class="mark {mark.kind}" role="img" title={mark.label} aria-label={mark.label} data-testid="mark-{mark.kind}">
						{mark.glyph}
					</span>
				{/each}
			</div>
		{/if}
		<div class="result" class:scored={pick.result != null} class:marked={marks.length > 0} style:--marks={marks.length}>
			<Flag iso2={pick.country.iso2} size="var(--flag-size)" label={pick.country.name} />
			<div class="text">
				<!-- The server sends what a pick scored only once the game is complete. -->
				{#if pick.result}
					<div class="title">
						{category.name}: <strong data-testid={testid && `${testid}-score`}>{pick.score}</strong>
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
		position: relative;
	}

	.marks {
		position: absolute;
		top: 0.3rem;
		right: 0.3rem;
		display: flex;
		gap: 0.2rem;
	}

	/* The title's first line runs beside the marks, so it stops short of them. */
	.marked .title {
		padding-right: calc(var(--marks) * 1.3rem);
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

		/* The marks sit beside the centred flag, above the title. */
		.marked .title {
			padding-right: 0;
		}
	}
</style>
