<script lang="ts">
	import { untrack } from 'svelte';
	import type { Country } from '$lib/api/client';
	import iso2Codes from '$lib/countries/iso2.json';
	import { buildSpinSequence } from '$lib/game/spin';
	import Flag from './Flag.svelte';

	// While `pending` (a pick is in flight) the flags cycle; when the next country arrives they land on it.
	// The spin therefore hides the network round trip.
	let { country, pending, onsettled }: { country: Country; pending: boolean; onsettled: () => void } = $props();

	let shown = $state(untrack(() => country.iso2));
	let settled = $state(false);

	$effect(() => {
		const target = country.iso2;
		const waiting = pending;
		settled = false;

		if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
			if (!waiting) {
				shown = target;
				settled = true;
				untrack(onsettled);
			}
			return;
		}

		const landing = waiting ? null : buildSpinSequence(iso2Codes, target, 14);
		let index = 0;

		const timer = setInterval(() => {
			if (landing === null) {
				shown = iso2Codes[Math.floor(Math.random() * iso2Codes.length)];
				return;
			}

			shown = landing[index++];
			if (index === landing.length) {
				clearInterval(timer);
				settled = true;
				onsettled();
			}
		}, 60);

		return () => clearInterval(timer);
	});
</script>

<div class="spinner">
	<Flag iso2={shown} size="6rem" label={settled ? country.name : 'Drawing a country'} />
	<p class="name" class:hidden={!settled}>{country.name}</p>
</div>

<style>
	.spinner {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.5rem;
		padding: 1rem 0;
	}

	.name {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.hidden {
		visibility: hidden;
	}
</style>
