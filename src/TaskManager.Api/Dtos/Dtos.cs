using System.ComponentModel.DataAnnotations;
using TaskManager.Api.Models;
using TaskStatus = TaskManager.Api.Models.TaskStatus;

namespace TaskManager.Api.Dtos;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password);

public record LoginRequest([Required] string Email, [Required] string Password);

public record AuthResponse(string Token, DateTime ExpiresAt);

public record CreateTaskRequest(
    [Required, MaxLength(200)] string Title,
    string? Description,
    DateTime? DueDate);

public record UpdateTaskRequest(
    [Required, MaxLength(200)] string Title,
    string? Description,
    TaskStatus Status,
    DateTime? DueDate);

public record TaskResponse(
    int Id,
    string Title,
    string? Description,
    TaskStatus Status,
    DateTime CreatedAt,
    DateTime? DueDate,
    DateTime? DeletedAt,
    string? DeleteReason)
{
    public static TaskResponse From(TaskItem t) =>
        new(t.Id, t.Title, t.Description, t.Status, t.CreatedAt, t.DueDate, t.DeletedAt, t.DeleteReason);
}

public record DeleteTaskRequest([MaxLength(500)] string? Reason);

public record TaskStats(int Total, int Todo, int InProgress, int Done, int Overdue, int Deleted);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
