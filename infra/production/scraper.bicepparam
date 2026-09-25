using '../scraper.bicep'

// Monday 06:00 UTC. A feed that breaks between Friday and Monday is caught by this run, which keeps the previous
// release for that feed.
param env = 'production'
param image = readEnvironmentVariable('SCRAPER_IMAGE', 'ghcr.io/joseph-leo/worldrankguesser-scraper:placeholder')
param cron = '0 6 * * 1'

// One Worker serves both environments. The token comes from the deploy workflow's PROXY_TOKEN; the placeholder only
// lets `az bicep build-params` run without it.
param proxyUrl = 'https://wrg-proxy.foweeti.workers.dev'
param proxyToken = readEnvironmentVariable('PROXY_TOKEN', 'placeholder')
param rankingsRefreshToken = readEnvironmentVariable('RANKINGS_REFRESH_TOKEN', 'placeholder')
