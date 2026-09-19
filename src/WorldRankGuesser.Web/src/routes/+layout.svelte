<script lang="ts">
	import favicon from '$lib/assets/favicon.svg';
	import 'flag-icons/css/flag-icons.min.css';
	import '../app.css';

	let { children } = $props();
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

<main>
	<header><a href="/">World Rank Guesser</a></header>
	{@render children()}
</main>

<style>
	header {
		padding-bottom: 1rem;
		font-weight: 600;
	}

	header a {
		color: inherit;
		text-decoration: none;
	}
</style>
