<script lang="ts">
	import 'flag-icons/css/flag-icons.min.css';
	import '../app.css';

	let { children } = $props();
</script>

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
