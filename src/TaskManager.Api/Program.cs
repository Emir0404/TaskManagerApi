using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using TaskManager.Api.Data;
using TaskManager.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- configuration, checked when the app starts ----
// A missing or weak signing key must stop the app right away with a clear message,
// not let it run and answer every request with a 500.
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<RateLimitSettings>()
    .Bind(builder.Configuration.GetSection(RateLimitSettings.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=taskmanager.db"));

builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) => bearer.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Value.Issuer,
        ValidAudience = jwt.Value.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Key)),
        ClockSkew = TimeSpan.FromSeconds(30),   // default is 5 minutes: a token would live 5 minutes after "expiry"
    });
builder.Services.AddAuthorization();

// Login and registration are the doors a password-guessing script knocks on, so they get a speed limit per client address.
builder.Services.AddRateLimiter(_ => { });
builder.Services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitSettings>>((limiter, settings) =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(RateLimitSettings.AuthPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        (context.Connection.RemoteIpAddress ?? IPAddress.None).ToString(),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.Value.AuthPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    limiter.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many attempts",
            Detail = "Too many attempts. Please wait a minute and try again.",
        }, ct);
    };
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["https://localhost:7151", "http://localhost:5092"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Every request body of this API is tiny JSON; refuse anything big before it is even read.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    app.UseExceptionHandler();   // errors become a short problem document, never a stack trace
    app.UseHsts();
}

if (app.Configuration.GetValue("HttpsRedirection", true))
    app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
