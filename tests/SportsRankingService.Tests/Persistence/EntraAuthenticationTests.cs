namespace SportsRankingService.Tests.Persistence;

public class EntraAuthenticationTests
{
    /// <summary>
    /// The scraper runs in Azure as a managed identity (Authentication=Active Directory Managed Identity). Since
    /// SqlClient 7 that provider lives in Microsoft.Data.SqlClient.Extensions.Azure; referencing it is the only wiring.
    /// </summary>
    [Fact]
    public void The_entra_authentication_provider_ships_with_the_scraper()
    {
        var provider = Type.GetType("Microsoft.Data.SqlClient.ActiveDirectoryAuthenticationProvider, Microsoft.Data.SqlClient.Extensions.Azure");

        Assert.NotNull(provider);
    }
}
