using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Tests.Integration;

public sealed class ApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, so the player cookie is not Secure-only and works over the test server's http.
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{GameDbContext.ConnectionStringName}", connectionString);
        builder.UseSetting("RateLimits:GameStartsPerPlayerPerHour", "100000");
        builder.UseSetting("RateLimits:GameStartsPerIpPerHour", "100000");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
    }
}
