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

    public int UserId { get; set; }
    public User? User { get; set; }
}
