using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Models;
using TaskStatus = TaskManager.Api.Models.TaskStatus;

namespace TaskManager.Api.Data.Repositories;

public sealed class EfTaskRepository(AppDbContext db) : ITaskRepository
{
    public async Task<TaskPage> SearchAsync(int userId, TaskSearch search, CancellationToken ct = default)
    {
        var query = db.Tasks.AsNoTracking().Where(t => t.UserId == userId && t.IsDeleted == search.Deleted);
        if (search.Status is not null) query = query.Where(t => t.Status == search.Status);
        if (!string.IsNullOrWhiteSpace(search.Text)) query = query.Where(t => t.Title.Contains(search.Text));

        var total = await query.CountAsync(ct);
        var ordered = search.Deleted
            ? query.OrderByDescending(t => t.DeletedAt)
            : query.OrderByDescending(t => t.CreatedAt);

        var items = await ordered
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(ct);

        return new TaskPage(items, total);
    }

    public async Task<TaskCounts> CountsAsync(int userId, DateTime today, CancellationToken ct = default)
    {
        var mine = db.Tasks.AsNoTracking().Where(t => t.UserId == userId);
        var active = mine.Where(t => !t.IsDeleted);

        var perStatus = await active
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(TaskStatus s) => perStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        var overdue = await active.CountAsync(t => t.DueDate != null && t.DueDate < today && t.Status != TaskStatus.Done, ct);
        var deleted = await mine.CountAsync(t => t.IsDeleted, ct);

        return new TaskCounts(CountOf(TaskStatus.Todo), CountOf(TaskStatus.InProgress), CountOf(TaskStatus.Done), overdue, deleted);
    }

    public Task<TaskItem?> FindAsync(int userId, int id, bool deleted, CancellationToken ct = default) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId && t.IsDeleted == deleted, ct);

    public void Add(TaskItem task) => db.Tasks.Add(task);

    public void Remove(TaskItem task) => db.Tasks.Remove(task);
}
