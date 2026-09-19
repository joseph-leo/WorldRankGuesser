<script lang="ts">
	// SVG flags, because Windows browsers do not render flag emoji.
	let { iso2, size = '1em', label }: { iso2: string; size?: string; label?: string } = $props();
</script>

<span class="fi fi-{iso2.toLowerCase()}" style:font-size={size} role="img" aria-label={label ?? iso2}></span>
