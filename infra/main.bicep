// The shared resources of one environment (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md, section 7).
// First deployed by the owner from the runbook; afterwards by infra.yml on every change to this file or its
// parameter files. Both environments cost $0: the database is the free offer that pauses when its monthly
// allowance is spent, and the app scales to zero. No SQL password exists anywhere: the server is Entra-only.
//   az deployment group create --resource-group rg-wrg-staging --parameters infra/staging/main.bicepparam
@allowed(['staging', 'production'])
param env string

param location string = resourceGroup().location

// The owner's account is the SQL server's Entra admin: the user principal name and its object id.
param sqlAdminLogin string
param sqlAdminObjectId string

// free: General Purpose serverless, 0.5 to 1 vCore, the free monthly limit, auto-pause when it is spent (stage 1).
// basic: Basic DTU, about $5 a month flat (stage 2, production only). A free database converts in place; not back.
@allowed(['free', 'basic'])
param sqlSku string = 'free'

// Log Analytics ingestion cap in GB per day, as a string because Bicep has no decimal literals. The two
// environments together stay inside the free 5 GB a month.
param logDailyCapGb string = '0.03'

// The budget only alerts; a fixed-price tier is the real cap. Cost Management can refuse a budget for up to 48 hours
// on a new subscription: deploy with budgetEnabled=false then, and again with true later.
param budgetAmount int = 1
param budgetEmail string
param budgetEnabled bool = true
param budgetStartDate string = '${substring(utcNow('yyyy-MM-dd'), 0, 7)}-01T00:00:00Z'

var uniq = uniqueString(resourceGroup().id)

resource log 'Microsoft.OperationalInsights/workspaces@2026-03-01' = {
  name: 'log-wrg-${env}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json(logDailyCapGb)
    }
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

// A workload-profiles environment with only the built-in Consumption profile has no environment charge; only
// replicas are billed. Every app and job in it says workloadProfileName: 'Consumption'.
resource cae 'Microsoft.App/managedEnvironments@2026-07-01' = {
  name: 'cae-wrg-${env}'
  location: location
  properties: {
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: log.properties.customerId
        sharedKey: log.listKeys().primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

resource sql 'Microsoft.Sql/servers@2025-01-01' = {
  name: 'sql-wrg-${env}-${uniq}'
  location: location
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// A Consumption environment without a virtual network has no fixed outbound address, so the app and the Job reach
// the server through the "allow Azure services" rule. It admits any Azure tenant to the login endpoint, which is
// acceptable only because authentication is Entra-only. The runner's own rule is added and removed per deploy.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2025-01-01' = {
  parent: sql
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource db 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: sql
  name: 'WorldRankGuesser'
  location: location
  sku: sqlSku == 'free'
    ? {
        name: 'GP_S_Gen5'
        tier: 'GeneralPurpose'
        family: 'Gen5'
        capacity: 1
      }
    : {
        name: 'Basic'
        tier: 'Basic'
        capacity: 5
      }
  properties: sqlSku == 'free'
    ? {
        useFreeLimit: true
        freeLimitExhaustionBehavior: 'AutoPause'
        autoPauseDelay: 15
        minCapacity: json('0.5')
        maxSizeBytes: 34359738368
        requestedBackupStorageRedundancy: 'Local'
        zoneRedundant: false
        collation: 'SQL_Latin1_General_CP1_CI_AS'
      }
    : {
        maxSizeBytes: 2147483648
        requestedBackupStorageRedundancy: 'Local'
        zoneRedundant: false
        collation: 'SQL_Latin1_General_CP1_CI_AS'
      }
}

resource gameIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-wrg-${env}-game'
  location: location
}

resource scraperIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-wrg-${env}-scraper'
  location: location
}

resource budget 'Microsoft.Consumption/budgets@2026-06-01' = if (budgetEnabled) {
  name: 'budget-wrg-${env}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      actual: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [budgetEmail]
      }
      forecast: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [budgetEmail]
      }
    }
  }
}

output environmentDefaultDomain string = cae.properties.defaultDomain
output customDomainVerificationId string = cae.properties.customDomainConfiguration.customDomainVerificationId
output sqlServerName string = sql.name
output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName
output gameIdentityClientId string = gameIdentity.properties.clientId
output scraperIdentityClientId string = scraperIdentity.properties.clientId
