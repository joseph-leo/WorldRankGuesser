using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WorldRankGuesser.Api.Persistence;
using WorldRankGuesser.Api.Rankings;

namespace WorldRankGuesser.Api.Tests.Integration;

public sealed class ApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    /// <summary>
    /// The rankings load in the background after the host starts (RankingsRefreshService), so a test that needs
    /// them waits here first. A host whose database has no rankings never becomes ready; those tests do not wait.
    /// </summary>
    public async Task WaitUntilReadyAsync()
    {
        var store = Services.GetRequiredService<IRankingsStore>();     // resolving Services starts the host
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (store.Current is null)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The rankings did not load within 30 seconds; see the host's log.");
            await Task.Delay(25);
        }
    }

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
