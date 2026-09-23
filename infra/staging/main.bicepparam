using '../main.bicep'

// The owner's identity and email come from the GitHub environment (or the shell, for the runbook's first deploy),
// never from this public file. The placeholders only let `az bicep build` succeed without them.
param env = 'staging'
param sqlAdminLogin = readEnvironmentVariable('SQL_ADMIN_LOGIN', 'placeholder@example.com')
param sqlAdminObjectId = readEnvironmentVariable('SQL_ADMIN_OBJECT_ID', '00000000-0000-0000-0000-000000000000')
param sqlSku = 'free'
param logDailyCapGb = '0.03'
param budgetAmount = 1
param budgetEmail = readEnvironmentVariable('BUDGET_EMAIL', 'placeholder@example.com')
param budgetEnabled = bool(readEnvironmentVariable('BUDGET_ENABLED', 'true'))
