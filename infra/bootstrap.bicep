// Owner only, once per environment (infra/README.md): the resource group and the two identities GitHub logs in as.
// Separate from main.bicep because a Contributor cannot write role assignments: this file holds every role
// assignment, so everything else stays deployable by id-wrg-<env>-deploy, which has Contributor on this group only.
//   az deployment sub create --location centralus --name bootstrap-staging --parameters infra/staging/bootstrap.bicepparam
targetScope = 'subscription'

@allowed(['staging', 'production'])
param env string

// Central US for everything (East US 2 refused new SQL servers on this subscription on 2026-09-23, and Central US is
// nearer the owner): the first free SQL database fixes the region of every later one in the subscription.
param location string = 'centralus'

// The federated credentials trust this repository's GitHub environments and its main branch. Renaming or
// transferring the repository breaks that trust silently (GitHub's OIDC subject carries the name): redeploy then.
param githubRepository string = 'joseph-leo/WorldRankGuesser'

resource rg 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: 'rg-wrg-${env}'
  location: location
}

module identities 'bootstrap-group.bicep' = {
  name: 'bootstrap-${env}'
  scope: rg
  params: {
    env: env
    location: location
    githubRepository: githubRepository
  }
}

output resourceGroupName string = rg.name
output tenantId string = subscription().tenantId
output subscriptionId string = subscription().subscriptionId
output deployClientId string = identities.outputs.deployClientId
output monitorClientId string = identities.outputs.monitorClientId
