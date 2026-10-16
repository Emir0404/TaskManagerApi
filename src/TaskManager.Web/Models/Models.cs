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
    DateTime? DueDate,
    DateTime? DeletedAt,
    string? DeleteReason);

public record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

public record TaskStats(int Total, int Todo, int InProgress, int Done, int Overdue, int Deleted);

public record CreateTaskRequest(string Title, string? Description, DateTime? DueDate);

public record UpdateTaskRequest(string Title, string? Description, TaskStatus Status, DateTime? DueDate);
