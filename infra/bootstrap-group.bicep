// The two identities GitHub Actions logs in as, with OIDC and no secret (deployed into the group by bootstrap.bicep).
//   id-wrg-<env>-deploy   trusts the GitHub environment named <env>; Contributor on this group: deploys, migrates,
//                         starts the Job, opens and closes firewall and ingress rules. Not in the other environment.
//   id-wrg-<env>-monitor  trusts the main branch (scheduled workflows run from main, which the production
//                         environment does not admit); Reader on this group: reads the Job's executions, nothing else.
param env string
param location string
param githubRepository string

var contributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
var readerRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f606fba81ae7')
var githubIssuer = 'https://token.actions.githubusercontent.com'
var audiences = ['api://AzureADTokenExchange']

resource deploy 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-wrg-${env}-deploy'
  location: location
}

resource deployCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: deploy
  name: 'github-environment-${env}'
  properties: {
    issuer: githubIssuer
    subject: 'repo:${githubRepository}:environment:${env}'
    audiences: audiences
  }
}

resource deployContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deploy.id, contributorRoleId)
  properties: {
    roleDefinitionId: contributorRoleId
    principalId: deploy.properties.principalId
    principalType: 'ServicePrincipal'
    description: 'GitHub Actions deploys the ${env} environment'
  }
}

resource monitor 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-wrg-${env}-monitor'
  location: location
}

resource monitorCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: monitor
  name: 'github-branch-main'
  properties: {
    issuer: githubIssuer
    subject: 'repo:${githubRepository}:ref:refs/heads/main'
    audiences: audiences
  }
}

resource monitorReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, monitor.id, readerRoleId)
  properties: {
    roleDefinitionId: readerRoleId
    principalId: monitor.properties.principalId
    principalType: 'ServicePrincipal'
    description: 'The scheduled scraper check reads the ${env} Job executions'
  }
}

output deployClientId string = deploy.properties.clientId
output monitorClientId string = monitor.properties.clientId
