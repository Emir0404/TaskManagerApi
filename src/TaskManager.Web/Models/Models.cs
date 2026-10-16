namespace TaskManager.Web.Models;

public enum TaskStatus
{
    Todo,
    InProgress,
    Done
}

public record AuthResponse(string Token, DateTime ExpiresAt);

public record TaskItem(
    int Id,
    string Title,
    string? Description,
    TaskStatus Status,
    DateTime CreatedAt,
    DateTime? DueDate);

public record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

public record CreateTaskRequest(string Title, string? Description, DateTime? DueDate);

public record UpdateTaskRequest(string Title, string? Description, TaskStatus Status, DateTime? DueDate);
