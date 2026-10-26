namespace TaskManager.Api.Models;

public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public List<TaskItem> Tasks { get; set; } = [];
}

public enum TaskStatus
{
    Todo,
    InProgress,
    Done
}

public class TaskItem
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Todo;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeleteReason { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Moves the task to the deleted list. A blank reason is stored as "no reason".</summary>
    public void SoftDelete(string? reason, DateTime now)
    {
        IsDeleted = true;
        DeletedAt = now;
        DeleteReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>Brings the task back to the active list.</summary>
    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
        DeleteReason = null;
    }
}
