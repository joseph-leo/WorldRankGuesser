using '../scraper.bicep'

// Monday 06:00 UTC. A feed that breaks between Friday and Monday is caught by this run, which keeps the previous
// release for that feed.
param env = 'production'
param image = readEnvironmentVariable('SCRAPER_IMAGE', 'ghcr.io/joseph-leo/worldrankguesser-scraper:placeholder')
param cron = '0 6 * * 1'
