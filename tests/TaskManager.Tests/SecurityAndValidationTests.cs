using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using TaskManager.Api.Dtos;

namespace TaskManager.Tests;

/// <summary>An API instance with some settings changed, on its own temporary database.</summary>
public sealed class CustomApiFactory(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tm-sec-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}

public class SecurityAndValidationTests : IClassFixture<ApiFactory>
{
    private const string TestKey = "test-secret-key-that-is-at-least-32-characters-long";
    private readonly ApiFactory _factory;

    public SecurityAndValidationTests(ApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, string Email)> NewUserAsync()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Password123"));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return (client, email);
    }

    private static string TokenWith(string key, DateTime expires, string issuer = "TaskManager.Api", string audience = "TaskManager.Client") =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer, audience, [new Claim(ClaimTypes.NameIdentifier, "1")],
            notBefore: expires.AddHours(-2), expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));

    private async Task<HttpStatusCode> CallWithTokenAsync(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync("/api/tasks")).StatusCode;
    }

    // ---- registration and login input ----

    [Theory]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task A_short_password_is_refused(string password)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest($"{Guid.NewGuid():N}@test.com", password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_password_longer_than_bcrypt_can_use_is_refused_not_silently_cut()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"{Guid.NewGuid():N}@test.com", new string('x', 73)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_72_byte_limit_counts_bytes_so_multibyte_passwords_cannot_sneak_past_it()
    {
        // 50 characters, but 100 bytes (each č is two bytes): within the character limit, over the byte limit.
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"{Guid.NewGuid():N}@test.com", new string('č', 50)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("too long", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_72_character_password_works_and_can_log_in()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.com";
        var password = new string('p', 72);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, new string('p', 71)))).StatusCode);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task An_invalid_email_is_refused(string email)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Password123"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Email_is_case_insensitive_so_one_person_cannot_register_twice()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@Test.com";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Password123"));

        var again = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email.ToUpperInvariant(), "Password123"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_get_the_same_answer()
    {
        var (_, email) = await NewUserAsync();
        var client = _factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPassword1"));
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@test.com", "WrongPassword1"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        static async Task<(string?, string?, int)> Shape(HttpResponseMessage r)
        {
            using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync());   // traceId differs per request, so compare the rest
            return (json.RootElement.GetProperty("title").GetString(), json.RootElement.GetProperty("detail").GetString(), json.RootElement.GetProperty("status").GetInt32());
        }
        Assert.Equal(await Shape(wrongPassword), await Shape(unknownUser));
    }

    [Fact]
    public async Task Errors_are_problem_documents_with_a_readable_detail()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@test.com", "WrongPassword1"));

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invalid email or password.", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal(401, json.RootElement.GetProperty("status").GetInt32());
    }

    // ---- task input ----

    [Fact]
    public async Task A_status_that_does_not_exist_is_refused_on_update()
    {
        var (client, _) = await NewUserAsync();
        var created = await (await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("t", null, null))).Content.ReadFromJsonAsync<TaskResponse>();

        var response = await client.PutAsJsonAsync($"/api/tasks/{created!.Id}", new { title = "t", status = 99 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unchanged = await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{created.Id}");
        Assert.Equal(Api.Models.TaskStatus.Todo, unchanged!.Status);
    }

    [Fact]
    public async Task A_status_that_does_not_exist_is_refused_in_the_list_filter()
    {
        var (client, _) = await NewUserAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/tasks?status=99")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tasks?status=2")).StatusCode);
    }

    [Fact]
    public async Task A_description_over_the_limit_is_refused_and_the_limit_itself_is_accepted()
    {
        var (client, _) = await NewUserAsync();

        var tooLong = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("t", new string('d', 4001), null));
        var exactly = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("t", new string('d', 4000), null));

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, exactly.StatusCode);
    }

    [Fact]
    public async Task A_long_description_is_refused_on_update_too()
    {
        var (client, _) = await NewUserAsync();
        var created = await (await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("t", null, null))).Content.ReadFromJsonAsync<TaskResponse>();

        var response = await client.PutAsJsonAsync($"/api/tasks/{created!.Id}", new UpdateTaskRequest("t", new string('d', 4001), Api.Models.TaskStatus.Todo, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- tokens ----

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            await CallWithTokenAsync(TokenWith("another-secret-key-that-is-also-32-characters-long!", DateTime.UtcNow.AddHours(1))));
    }

    [Fact]
    public async Task An_expired_token_is_rejected_even_when_just_expired()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWithTokenAsync(TokenWith(TestKey, DateTime.UtcNow.AddMinutes(-2))));
    }

    [Fact]
    public async Task A_token_for_another_issuer_or_audience_is_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWithTokenAsync(TokenWith(TestKey, DateTime.UtcNow.AddHours(1), issuer: "evil")));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWithTokenAsync(TokenWith(TestKey, DateTime.UtcNow.AddHours(1), audience: "evil")));
    }

    [Fact]
    public async Task A_correctly_signed_token_is_accepted_which_proves_the_checks_above_are_not_just_rejecting_everything()
    {
        Assert.Equal(HttpStatusCode.OK, await CallWithTokenAsync(TokenWith(TestKey, DateTime.UtcNow.AddHours(1))));
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var (client, _) = await NewUserAsync();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var tampered = token[..^3] + (token[^3] == 'A' ? "BBB" : "AAA");

        Assert.Equal(HttpStatusCode.Unauthorized, await CallWithTokenAsync(tampered));
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await _factory.CreateClient().GetAsync("/api/tasks");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }
}

public class ThrottlingTests
{
    private static CustomApiFactory Factory(int perMinute) => new(new()
    {
        ["Jwt:Key"] = "test-secret-key-that-is-at-least-32-characters-long",
        ["RateLimit:AuthPerMinute"] = perMinute.ToString(),
    });

    [Fact]
    public async Task Repeated_wrong_passwords_are_slowed_down_with_429_and_retry_after()
    {
        using var factory = Factory(5);
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 8; i++)
        {
            last = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("victim@test.com", $"guess-{i}"));
            statuses.Add(last.StatusCode);
        }

        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
        Assert.True(last!.Headers.Contains("Retry-After"));
        using var json = JsonDocument.Parse(await last.Content.ReadAsStringAsync());
        Assert.Contains("Too many attempts", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Registration_shares_the_same_limit_and_the_limit_does_not_touch_the_tasks_api()
    {
        using var factory = Factory(3);
        var client = factory.CreateClient();
        string? token = null;

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest($"user{i}@test.com", "Password123"));
            token ??= (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("user99@test.com", "Password123"))).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tasks")).StatusCode);
    }
}

public class StartupValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("31-characters-long-and-no-more")]
    public void Without_a_proper_signing_key_the_app_refuses_to_start_with_a_clear_message(string? key)
    {
        using var factory = new CustomApiFactory(new() { ["Jwt:Key"] = key });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Jwt:Key", ex.ToString());
    }

    [Fact]
    public void A_32_character_key_is_enough()
    {
        using var factory = new CustomApiFactory(new() { ["Jwt:Key"] = new string('k', 32) });

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void A_nonsense_rate_limit_stops_the_app_at_startup()
    {
        using var factory = new CustomApiFactory(new()
        {
            ["Jwt:Key"] = new string('k', 32),
            ["RateLimit:AuthPerMinute"] = "0",
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("AuthPerMinute", ex.ToString());
    }
}
