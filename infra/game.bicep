// The game's Container App (docs/superpowers/specs/2026-09-19-phase-2-go-live-design.md, section 7). Deployed by
// deploy-game.yml with the image by digest; the shared resources come from main.bicep by name.
//   GAME_IMAGE=ghcr.io/joseph-leo/worldrankguesser-game@sha256:... az deployment group create \
//     --resource-group rg-wrg-staging --parameters infra/staging/game.bicepparam
@allowed(['staging', 'production'])
param env string

param location string = resourceGroup().location

// The image by digest, so a rankings refresh or a rebuilt tag never changes what runs.
param image string

// 0 in stage 1 (scale to zero, $0), 1 in stage 2 (no cold starts, about $5 a month).
param minReplicas int = 0
param maxReplicas int = 2

// '' = the default hostname only. With a name, the hostname is added and bound to a managed certificate declared
// below, which needs the DNS records first (infra/README.md) and the app reachable from the certificate authority.
param customDomain string = ''

// [] = public. Any entry makes the ingress refuse every other address (an Allow list denies the rest), which is
// how staging is private; never set on production.
param allowedIps array = []

// 12 hours: an hourly refresh would wake the paused database and spend the free allowance in two weeks.
param refreshMinutes int = 720

// The token POST /api/rankings/refresh requires, from the GitHub secret RANKINGS_REFRESH_TOKEN, which the scraper's
// Job also gets (docs/superpowers/specs/2026-09-25-rankings-refresh-notification-design.md).
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
  name: 'id-wrg-${env}-game'
}

resource app 'Microsoft.App/containerApps@2026-01-01' = {
  name: 'ca-wrg-${env}-game'
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
      activeRevisionsMode: 'Single'
      secrets: [
        {
          name: 'rankings-refresh-token'
          value: rankingsRefreshToken
        }
      ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        ipSecurityRestrictions: [
          for (cidr, i) in allowedIps: {
            name: 'allow-${i}'
            ipAddressRange: cidr
            action: 'Allow'
          }
        ]
        customDomains: empty(customDomain)
          ? []
          : [
              {
                name: customDomain
                bindingType: 'Auto'
              }
            ]
      }
    }
    template: {
      containers: [
        {
          name: 'game'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              // No password: the app is the identity. Connect Timeout 60 rides out a resuming database, with the
              // context's own retries behind it.
              name: 'ConnectionStrings__WorldRankGuesserConnection'
              value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=WorldRankGuesser;Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;Connect Timeout=60'
            }
            {
              // The environment's ingress rewrites X-Forwarded-For, so the per-IP limit keys on the caller.
              name: 'Hosting__TrustForwardedHeaders'
              value: 'true'
            }
            {
              name: 'Rankings__RefreshMinutes'
              value: string(refreshMinutes)
            }
            {
              name: 'Rankings__RefreshToken'
              secretRef: 'rankings-refresh-token'
            }
          ]
          // All three on /healthz, never /readyz: a visitor arriving while the database resumes must get the front
          // end's waking-up state, not an ingress error.
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              initialDelaySeconds: 2
              periodSeconds: 3
              timeoutSeconds: 3
              failureThreshold: 10
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              periodSeconds: 10
              timeoutSeconds: 3
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              periodSeconds: 10
              timeoutSeconds: 3
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

// A free managed certificate for the custom hostname, validated through the CNAME. The hostname must be on the app
// first (dependsOn) and the DNS records must exist before this deployment (infra/README.md).
resource certificate 'Microsoft.App/managedEnvironments/managedCertificates@2026-01-01' = if (!empty(customDomain)) {
  parent: cae
  name: 'cert-${replace(customDomain, '.', '-')}'
  location: location
  properties: {
    subjectName: customDomain
    domainControlValidation: 'CNAME'
  }
  dependsOn: [app]
}

output fqdn string = app.properties.configuration.ingress.fqdn
