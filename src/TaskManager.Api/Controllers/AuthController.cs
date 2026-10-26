using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskManager.Api.Dtos;
using TaskManager.Api.Models;
using TaskManager.Api.Services;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitSettings.AuthPolicy)]
public class AuthController(IUserRepository users, IUnitOfWork unitOfWork, ITokenService tokens) : ControllerBase
{
    // A real bcrypt hash of a throwaway password. When the email is unknown we still check against it,
    // so "no such user" takes as long as "wrong password" and timing does not reveal which emails exist.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("not-a-real-password");

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        // 72 *bytes*, not characters: letters like č, ć or emoji take more than one byte each.
        if (Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            ModelState.AddModelError(nameof(request.Password), "The password is too long (at most 72 bytes).");
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.EmailExistsAsync(email, ct))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered", detail: "Email is already registered.");

        var user = new User { Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password) };
        users.Add(user);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DataConflictException)
        {
            // Two registrations with the same email at the same moment: the unique index stopped the second one.
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered", detail: "Email is already registered.");
        }

        var (token, expires) = tokens.CreateToken(user);
        return Ok(new AuthResponse(token, expires));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(email, ct);

        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyHash);
        if (user is null || !passwordOk)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials", detail: "Invalid email or password.");

        var (token, expires) = tokens.CreateToken(user);
        return Ok(new AuthResponse(token, expires));
    }
}
