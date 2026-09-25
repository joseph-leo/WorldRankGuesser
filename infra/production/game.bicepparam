using '../game.bicep'

// The hostname is bound from the first deploy, because the player cookie is bound to it (spec section 2). No
// allow-list ever: production is public, and the certificate authority must reach the app.
param env = 'production'
param image = readEnvironmentVariable('GAME_IMAGE', 'ghcr.io/joseph-leo/worldrankguesser-game:placeholder')
param minReplicas = 0
param customDomain = 'games.foweeti.com'
param allowedIps = []

// The refresh token comes from the deploy workflow's RANKINGS_REFRESH_TOKEN; the placeholder only lets
// `az bicep build-params` run without it.
param rankingsRefreshToken = readEnvironmentVariable('RANKINGS_REFRESH_TOKEN', 'placeholder')
