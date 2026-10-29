using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Globeweigh.Api.Auth;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "Globeweigh.Api";
    public string Audience { get; set; } = "Globeweigh.Admin";
    // 0 (or less) = tokens never expire. TauriAdmin/TauriTouch run as long-lived kiosk
    // sessions on the server and shop floor: a signed-in user must stay signed in.
    public int ExpireHours { get; set; }
}

public class JwtTokenService
{
    private readonly JwtOptions _opt;
    public JwtTokenService(IOptions<JwtOptions> opt) => _opt = opt.Value;

    /// <param name="impersonatedBy">Set when an admin is signed in AS this user (Switch user):
    /// the admin's own id and name, carried as signed claims so "Return to my account" can
    /// trust them (AuthController.SwitchBack).</param>
    public (string token, DateTime expiresUtc) Create(int userId, string fullName, (int Id, string Name)? impersonatedBy = null)
    {
        var neverExpires = _opt.ExpireHours <= 0;
        // No exp claim at all when non-expiring — a far-future exp would still expire eventually.
        DateTime? expires = neverExpires ? null : DateTime.UtcNow.AddHours(_opt.ExpireHours);
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("name", fullName ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (impersonatedBy is { } by)
        {
            claims.Add(new Claim(ClaimsExtensions.ImpersonatedByClaim, by.Id.ToString()));
            claims.Add(new Claim(ClaimsExtensions.ImpersonatedByNameClaim, by.Name ?? string.Empty));
        }
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_opt.Issuer, _opt.Audience, claims, expires: expires, signingCredentials: creds);
        // Clients store ExpiresUtc and re-check it on restore, so report "never" as far-future.
        var reportedExpiry = expires ?? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), reportedExpiry);
    }
}

public static class ClaimsExtensions
{
    public const string ImpersonatedByClaim = "impersonatedBy";
    public const string ImpersonatedByNameClaim = "impersonatedByName";

    // The admin's id when this token is a Switch-user session, otherwise null.
    public static int? GetImpersonatedBy(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(ImpersonatedByClaim)?.Value, out var id) ? id : null;

    // Token is created with sub = userId; JwtBearer is configured with MapInboundClaims = false.
    public static int GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(sub, out var id) ? id : 0;
    }
}
