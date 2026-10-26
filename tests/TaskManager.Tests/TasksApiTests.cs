using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TaskManager.Api.Dtos;

namespace TaskManager.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tm-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
        builder.UseSetting("Jwt:Key", "test-secret-key-that-is-at-least-32-characters-long");
        builder.UseSetting("RateLimit:AuthPerMinute", "10000");   // the many test users must not hit the speed limit
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}

public class TasksApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TasksApiTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Password123"));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    [Fact]
    public async Task Tasks_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var request = new RegisterRequest($"{Guid.NewGuid():N}@test.com", "Password123");
        (await client.PostAsJsonAsync("/api/auth/register", request)).EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.com";
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Password123"))).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTask_ThenGetById_ReturnsTask()
    {
        var client = await AuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Write tests", "desc", null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = await created.Content.ReadFromJsonAsync<TaskResponse>();

        var fetched = await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{task!.Id}");
        Assert.Equal("Write tests", fetched!.Title);
    }

    [Fact]
    public async Task CreateTask_EmptyTitle_ReturnsBadRequest()
    {
        var client = await AuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task User_CannotAccessAnotherUsersTask()
    {
        var owner = await AuthenticatedClientAsync();
        var other = await AuthenticatedClientAsync();

        var created = await owner.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Private", null, null));
        var task = await created.Content.ReadFromJsonAsync<TaskResponse>();

        var response = await other.GetAsync($"/api/tasks/{task!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAndDelete_Task_Works()
    {
        var client = await AuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Old", null, null));
        var task = await created.Content.ReadFromJsonAsync<TaskResponse>();

        var updated = await client.PutAsJsonAsync($"/api/tasks/{task!.Id}",
            new UpdateTaskRequest("New", null, TaskManager.Api.Models.TaskStatus.Done, null));
        updated.EnsureSuccessStatusCode();
        var body = await updated.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.Equal("New", body!.Title);
        Assert.Equal(TaskManager.Api.Models.TaskStatus.Done, body.Status);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_IsSoft_AndKeepsReasonInDeletedList()
    {
        var client = await AuthenticatedClientAsync();
        var task = await (await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Cancel meeting", null, null)))
            .Content.ReadFromJsonAsync<TaskResponse>();

        var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/tasks/{task!.Id}")
        {
            Content = JsonContent.Create(new DeleteTaskRequest("Meeting was cancelled"))
        };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);

        var active = await client.GetFromJsonAsync<PagedResult<TaskResponse>>("/api/tasks");
        Assert.Empty(active!.Items);

        var deleted = await client.GetFromJsonAsync<PagedResult<TaskResponse>>("/api/tasks?deleted=true");
        var item = Assert.Single(deleted!.Items);
        Assert.Equal("Meeting was cancelled", item.DeleteReason);
        Assert.NotNull(item.DeletedAt);
    }

    [Fact]
    public async Task Restore_MovesTaskBackToActive()
    {
        var client = await AuthenticatedClientAsync();
        var task = await (await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Oops", null, null)))
            .Content.ReadFromJsonAsync<TaskResponse>();
        await client.DeleteAsync($"/api/tasks/{task!.Id}");

        var restored = await client.PostAsync($"/api/tasks/{task.Id}/restore", null);
        restored.EnsureSuccessStatusCode();
        var body = await restored.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.Null(body!.DeletedAt);
        Assert.Null(body.DeleteReason);

        var active = await client.GetFromJsonAsync<PagedResult<TaskResponse>>("/api/tasks");
        Assert.Single(active!.Items);
    }

    [Fact]
    public async Task PermanentDelete_OnlyWorksForDeletedTasks()
    {
        var client = await AuthenticatedClientAsync();
        var task = await (await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Temp", null, null)))
            .Content.ReadFromJsonAsync<TaskResponse>();

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/tasks/{task!.Id}/permanent")).StatusCode);

        await client.DeleteAsync($"/api/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tasks/{task.Id}/permanent")).StatusCode);

        var deleted = await client.GetFromJsonAsync<PagedResult<TaskResponse>>("/api/tasks?deleted=true");
        Assert.Empty(deleted!.Items);
    }

    [Fact]
    public async Task Stats_CountsStatusesOverdueAndDeleted()
    {
        var client = await AuthenticatedClientAsync();
        var yesterday = DateTime.UtcNow.AddDays(-2);

        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Late", null, yesterday));
        var doneTask = await (await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Finished", null, yesterday)))
            .Content.ReadFromJsonAsync<TaskResponse>();
        await client.PutAsJsonAsync($"/api/tasks/{doneTask!.Id}",
            new UpdateTaskRequest("Finished", null, TaskManager.Api.Models.TaskStatus.Done, yesterday));
        var removed = await (await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Gone", null, null)))
            .Content.ReadFromJsonAsync<TaskResponse>();
        await client.DeleteAsync($"/api/tasks/{removed!.Id}");

        var stats = await client.GetFromJsonAsync<TaskStats>("/api/tasks/stats");
        Assert.Equal(2, stats!.Total);
        Assert.Equal(1, stats.Todo);
        Assert.Equal(1, stats.Done);
        Assert.Equal(1, stats.Overdue);
        Assert.Equal(1, stats.Deleted);
    }

    [Fact]
    public async Task GetAll_FiltersByStatusAndPaginates()
    {
        var client = await AuthenticatedClientAsync();
        for (var i = 1; i <= 3; i++)
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"Task {i}", null, null));

        var page = await client.GetFromJsonAsync<PagedResult<TaskResponse>>("/api/tasks?pageSize=2&status=Todo");
        Assert.Equal(3, page!.TotalCount);
        Assert.Equal(2, page.Items.Count);
    }
}
