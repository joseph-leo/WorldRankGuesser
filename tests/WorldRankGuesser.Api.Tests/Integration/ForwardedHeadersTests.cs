using System.Net;
using System.Net.Http.Json;
using WorldRankGuesser.Api.Games;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// Behind the ingress every player arrives from the proxy's address, so the per-IP limit must key on X-Forwarded-For,
/// but only when the app is told the header can be trusted; a directly exposed container must ignore it.
/// </summary>
[Collection("sql")]
public class ForwardedHeadersTests(SqlServerFixture sql)
{
    private static Task<HttpResponseMessage> StartFrom(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/games")
        {
            Content = JsonContent.Create(new StartGameRequest("practice")),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        return client.SendAsync(request);
    }

    private static Dictionary<string, string?> TwoStartsPerIp(bool trustForwardedHeaders) => new()
    {
        ["RateLimits:GameStartsPerIpPerHour"] = "2",
        ["Hosting:TrustForwardedHeaders"] = trustForwardedHeaders ? "true" : "false",
    };

    [Fact]
    public async Task With_the_setting_on_each_forwarded_address_has_its_own_limit()
    {
        await using var factory = new ApiFactory(sql.ConnectionString, TwoStartsPerIp(trustForwardedHeaders: true));
        await factory.WaitUntilReadyAsync();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await StartFrom(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StartFrom(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await StartFrom(client, "203.0.113.1")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await StartFrom(client, "203.0.113.2")).StatusCode);
    }

    [Fact]
    public async Task With_the_setting_off_the_header_is_ignored()
    {
        await using var factory = new ApiFactory(sql.ConnectionString, TwoStartsPerIp(trustForwardedHeaders: false));
        await factory.WaitUntilReadyAsync();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await StartFrom(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StartFrom(client, "203.0.113.1")).StatusCode);

        // The same connection as far as the app can see, so another header value is over the limit too.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await StartFrom(client, "203.0.113.2")).StatusCode);
    }
}
