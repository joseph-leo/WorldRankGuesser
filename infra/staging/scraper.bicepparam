using '../scraper.bicep'

// Friday 06:00 UTC: a feed that broke during the week fails here first, and the weekend is there to fix it before
// production's Monday run.
param env = 'staging'
param image = readEnvironmentVariable('SCRAPER_IMAGE', 'ghcr.io/joseph-leo/worldrankguesser-scraper:placeholder')
param cron = '0 6 * * 5'

// One Worker serves both environments. The token comes from the deploy workflow's PROXY_TOKEN; the placeholder only
// lets `az bicep build-params` run without it.
param proxyUrl = 'https://wrg-proxy.foweeti.workers.dev'
param proxyToken = readEnvironmentVariable('PROXY_TOKEN', 'placeholder')
