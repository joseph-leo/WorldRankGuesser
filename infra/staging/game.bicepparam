using '../game.bicep'

// The image is set by the deploy workflow (by digest). The owner's address comes from the staging environment's
// ALLOWED_IPS secret, a JSON array of CIDRs such as ["203.0.113.5/32"]; the placeholder [] would make staging
// public, so the workflow always sets it. Staging stays on stage 1 for good.
param env = 'staging'
param image = readEnvironmentVariable('GAME_IMAGE', 'ghcr.io/joseph-leo/worldrankguesser-game:placeholder')
param minReplicas = 0
param allowedIps = json(readEnvironmentVariable('ALLOWED_IPS', '[]'))
