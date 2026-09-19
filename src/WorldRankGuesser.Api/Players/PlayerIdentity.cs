using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace WorldRankGuesser.Api.Players;

public static class PlayerIdentity
{
    public const string CookieName = "wrg_player";

    private const string PlayerIdClaim = "player_id";

    public static Guid? GetPlayerId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(PlayerIdClaim), out var id) ? id : null;

    /// <summary>The caller's player, created and signed in on the spot when the cookie is missing or stale.</summary>
    public static async Task<Guid> EnsurePlayerAsync(HttpContext http, PlayerService players, CancellationToken ct)
    {
        if (http.User.GetPlayerId() is { } existing && await players.TouchAsync(existing, ct))
        {
            return existing;
        }

        var player = await players.CreateAsync(ct);
        var identity = new ClaimsIdentity(
            [new Claim(PlayerIdClaim, player.Id.ToString())], CookieAuthenticationDefaults.AuthenticationScheme);

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return player.Id;
    }
}
