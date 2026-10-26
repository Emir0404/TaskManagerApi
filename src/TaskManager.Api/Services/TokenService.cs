using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TaskManager.Api.Models;

namespace TaskManager.Api.Services;

public class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>Secret used to sign tokens. At least 32 characters; set it with user secrets or the Jwt__Key environment variable.</summary>
    [Required(ErrorMessage = "Jwt:Key is not set. Provide it through user secrets or the Jwt__Key environment variable.")]
    [MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters long.")]
    public string Key { get; set; } = "";

    [Required] public string Issuer { get; set; } = "TaskManager.Api";
    [Required] public string Audience { get; set; } = "TaskManager.Client";
    [Range(1, 1440)] public int ExpiresMinutes { get; set; } = 60;
}

public class RateLimitSettings
{
    public const string Section = "RateLimit";
    public const string AuthPolicy = "auth";

    /// <summary>Login and register attempts allowed per minute from one address.</summary>
    [Range(1, 10_000)] public int AuthPerMinute { get; set; } = 10;
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(User user);
}

public class TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _opt = options.Value;

    public (string Token, DateTime ExpiresAt) CreateToken(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_opt.ExpiresMinutes);
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _opt.Issuer,
            _opt.Audience,
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Email, user.Email)],
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
