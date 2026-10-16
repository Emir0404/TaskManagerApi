using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TaskManager.Api.Models;

namespace TaskManager.Api.Services;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "TaskManager.Api";
    public string Audience { get; set; } = "TaskManager.Client";
    public int ExpiresMinutes { get; set; } = 60;
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
