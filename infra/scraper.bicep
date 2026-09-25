// The scraper as a scheduled Container Apps Job (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md,
// section 7): never a background service in the API, because scale to zero would kill it mid-run. It runs every
// enabled feed once and exits 1 if any feed failed, which marks the execution Failed; one retry follows. Staging runs
// on Friday so a feed that broke during the week fails there first, production on Monday.
//   SCRAPER_IMAGE=ghcr.io/joseph-leo/worldrankguesser-scraper@sha256:... az deployment group create \
//     --resource-group rg-wrg-staging --parameters infra/staging/scraper.bicepparam
//   az containerapp job start --name caj-wrg-staging-scraper --resource-group rg-wrg-staging      # on demand
@allowed(['staging', 'production'])
param env string

param location string = resourceGroup().location

// The image by digest.
param image string

// Five fields, UTC.
param cron string

// The Worker in proxy/ that the feeds with "Fetcher": "Proxy" go through (WBSC: CloudFront refuses Azure addresses;
// docs/superpowers/specs/2026-09-24-wbsc-proxy-egress-design.md). Public, so a plain parameter.
param proxyUrl string

// The token the Worker checks, from the GitHub secret PROXY_TOKEN: the one source for both ends.
@secure()
param proxyToken string

// The same token the game checks on POST /api/rankings/refresh; the Job calls it after a run that stored a release.
@secure()
param rankingsRefreshToken string

var uniq = uniqueString(resourceGroup().id)

resource cae 'Microsoft.App/managedEnvironments@2026-01-01' existing = {
  name: 'cae-wrg-${env}'
}

resource sql 'Microsoft.Sql/servers@2025-01-01' existing = {
  name: 'sql-wrg-${env}-${uniq}'
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: 'id-wrg-${env}-scraper'
}

resource job 'Microsoft.App/jobs@2026-01-01' = {
  name: 'caj-wrg-${env}-scraper'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: cae.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Schedule'
      replicaTimeout: 1800
      replicaRetryLimit: 1
      secrets: [
        {
          name: 'proxy-token'
          value: proxyToken
        }
        {
          name: 'rankings-refresh-token'
          value: rankingsRefreshToken
        }
      ]
      scheduleTriggerConfig: {
        cronExpression: cron
        parallelism: 1
        replicaCompletionCount: 1
      }
    }
    template: {
      containers: [
        {
          name: 'scraper'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'DOTNET_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__WorldRankGuesserConnection'
              value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=WorldRankGuesser;Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;Connect Timeout=60'
            }
            {
              name: 'Proxy__Url'
              value: proxyUrl
            }
            {
              name: 'Proxy__Token'
              secretRef: 'proxy-token'
            }
            {
              // The game's default hostname: its app name under the environment's default domain, so nothing here
              // depends on the game app existing, and the first-deploy order (scraper, then game) stands.
              name: 'Notify__Url'
              value: 'https://ca-wrg-${env}-game.${cae.properties.defaultDomain}/api/rankings/refresh'
            }
            {
              name: 'Notify__Token'
              secretRef: 'rankings-refresh-token'
            }
          ]
        }
      ]
    }
  }
}

output jobName string = job.name
