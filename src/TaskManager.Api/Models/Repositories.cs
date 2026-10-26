namespace TaskManager.Api.Models;

/// <summary>The database refused a change because it clashes with other data (for example a second account with the same email).</summary>
public class DataConflictException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Saves everything the repositories changed in one all-or-nothing step.
/// Repositories only collect changes (Add, Remove); nothing reaches the database until this is called.
/// </summary>
public interface IUnitOfWork
{
    /// <exception cref="DataConflictException">A unique index or other constraint refused the change.</exception>
    Task SaveChangesAsync(CancellationToken ct = default);
}

public record TaskSearch(TaskStatus? Status, string? Text, bool Deleted, int Page, int PageSize);

public record TaskPage(IReadOnlyList<TaskItem> Items, int TotalCount);

public record TaskCounts(int Todo, int InProgress, int Done, int Overdue, int Deleted)
{
    public int Active => Todo + InProgress + Done;
}

/// <summary>Where tasks are stored. Every method takes the user's id and only ever touches that user's tasks.</summary>
public interface ITaskRepository
{
    Task<TaskPage> SearchAsync(int userId, TaskSearch search, CancellationToken ct = default);

    /// <param name="today">Tasks due before this day (and not done) count as overdue.</param>
    Task<TaskCounts> CountsAsync(int userId, DateTime today, CancellationToken ct = default);

    /// <summary>One task, ready to be changed. <paramref name="deleted"/> picks the active list or the deleted list.</summary>
    Task<TaskItem?> FindAsync(int userId, int id, bool deleted, CancellationToken ct = default);

    void Add(TaskItem task);
    void Remove(TaskItem task);
}

public interface IUserRepository
{
    /// <summary>Emails are stored trimmed and lower case; pass them the same way.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken ct = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);

    void Add(User user);
}
