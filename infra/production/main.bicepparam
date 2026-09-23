using '../main.bicep'

// Stage 1 (launch): the free database and, in game.bicepparam, no always-on replica: $0. Stage 2, when the game has
// players: sqlSku = 'basic' here and minReplicas = 1 there, about $10 a month fixed (spec section 7).
param env = 'production'
param sqlAdminLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN', 'placeholder@example.com')
param sqlAdminObjectId = readEnvironmentVariable('SQL_ADMIN_OBJECT_ID', '00000000-0000-0000-0000-000000000000')
param sqlSku = 'free'
param logDailyCapGb = '0.12'
param budgetAmount = 5
param budgetEmail = readEnvironmentVariable('BUDGET_EMAIL', 'placeholder@example.com')
param budgetEnabled = bool(readEnvironmentVariable('BUDGET_ENABLED', 'true'))
