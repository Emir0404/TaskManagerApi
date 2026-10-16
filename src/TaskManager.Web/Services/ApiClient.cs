using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using TaskManager.Web.Models;
using TaskStatus = TaskManager.Web.Models.TaskStatus;

namespace TaskManager.Web.Services;

public class ApiException(string message, HttpStatusCode statusCode) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public class ApiClient(HttpClient http, IJSRuntime js)
{
    private const string TokenKey = "taskmanager.token";
    private string? _token;

    public event Action? AuthChanged;

    public bool IsLoggedIn => !string.IsNullOrEmpty(_token);

    public async Task InitAsync()
    {
        _token ??= await js.InvokeAsync<string?>("localStorage.getItem", TokenKey);
    }

    public Task LoginAsync(string email, string password) => AuthenticateAsync("login", email, password);

    public Task RegisterAsync(string email, string password) => AuthenticateAsync("register", email, password);

    public async Task LogoutAsync()
    {
        _token = null;
        await js.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        AuthChanged?.Invoke();
    }

    public async Task<PagedResult<TaskItem>> GetTasksAsync(TaskStatus? status, string? search, int page, int pageSize)
    {
        var query = $"api/tasks?page={page}&pageSize={pageSize}";
        if (status is not null) query += $"&status={status}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";
        return await SendAsync<PagedResult<TaskItem>>(HttpMethod.Get, query);
    }

    public Task<TaskItem> CreateTaskAsync(CreateTaskRequest request) =>
        SendAsync<TaskItem>(HttpMethod.Post, "api/tasks", request);

    public Task<TaskItem> UpdateTaskAsync(int id, UpdateTaskRequest request) =>
        SendAsync<TaskItem>(HttpMethod.Put, $"api/tasks/{id}", request);

    public async Task DeleteTaskAsync(int id) => await SendAsync<object>(HttpMethod.Delete, $"api/tasks/{id}");

    private async Task AuthenticateAsync(string action, string email, string password)
    {
        var auth = await SendAsync<AuthResponse>(HttpMethod.Post, $"api/auth/{action}", new { email, password });
        _token = auth.Token;
        await js.InvokeVoidAsync("localStorage.setItem", TokenKey, _token);
        AuthChanged?.Invoke();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (IsLoggedIn) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("Cannot reach the API. Is it running?", HttpStatusCode.ServiceUnavailable);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized && IsLoggedIn)
            {
                await LogoutAsync();
                throw new ApiException("Your session has expired. Please log in again.", response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
                throw new ApiException(await ReadErrorAsync(response), response.StatusCode);

            if (typeof(T) == typeof(object) || response.StatusCode == HttpStatusCode.NoContent)
                return default!;

            return (await response.Content.ReadFromJsonAsync<T>())!;
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("error", out var error)) return error.GetString() ?? "Request failed.";
            if (json.TryGetProperty("errors", out var errors))
            {
                var first = errors.EnumerateObject().FirstOrDefault().Value;
                if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() > 0)
                    return first[0].GetString() ?? "Validation failed.";
            }
        }
        catch (JsonException) { }
        return $"Request failed ({(int)response.StatusCode}).";
    }
}
