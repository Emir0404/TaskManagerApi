using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Data;
using TaskManager.Api.Dtos;
using TaskManager.Api.Models;
using TaskStatus = TaskManager.Api.Models.TaskStatus;

namespace TaskManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tasks")]
public class TasksController(AppDbContext db) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Lists the current user's tasks. Pass deleted=true to list the soft-deleted ones.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<TaskResponse>>> GetAll(
        [FromQuery] TaskStatus? status,
        [FromQuery] string? search,
        [FromQuery] bool deleted = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (status is { } s && !Enum.IsDefined(s))
        {
            ModelState.AddModelError(nameof(status), "Unknown status.");
            return ValidationProblem(ModelState);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Tasks.AsNoTracking().Where(t => t.UserId == UserId && t.IsDeleted == deleted);
        if (status is not null) query = query.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(t => t.Title.Contains(search));

        var total = await query.CountAsync();
        var ordered = deleted
            ? query.OrderByDescending(t => t.DeletedAt)
            : query.OrderByDescending(t => t.CreatedAt);

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => TaskResponse.From(t))
            .ToListAsync();

        return Ok(new PagedResult<TaskResponse>(items, page, pageSize, total));
    }

    /// <summary>Counts of active tasks per status, overdue tasks and soft-deleted tasks.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<TaskStats>> GetStats()
    {
        var userId = UserId;
        var today = DateTime.UtcNow.Date;

        var mine = db.Tasks.AsNoTracking().Where(t => t.UserId == userId);
        var active = mine.Where(t => !t.IsDeleted);

        var perStatus = await active
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int CountOf(TaskStatus s) => perStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        var overdue = await active.CountAsync(t => t.DueDate != null && t.DueDate < today && t.Status != TaskStatus.Done);
        var deleted = await mine.CountAsync(t => t.IsDeleted);

        return Ok(new TaskStats(
            perStatus.Sum(x => x.Count),
            CountOf(TaskStatus.Todo),
            CountOf(TaskStatus.InProgress),
            CountOf(TaskStatus.Done),
            overdue,
            deleted));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TaskResponse>> GetById(int id)
    {
        var task = await db.Tasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId && !t.IsDeleted);
        return task is null ? NotFound() : Ok(TaskResponse.From(task));
    }

    [HttpPost]
    public async Task<ActionResult<TaskResponse>> Create(CreateTaskRequest request)
    {
        var task = new TaskItem
        {
            Title = request.Title,
            Description = request.Description,
            DueDate = request.DueDate,
            UserId = UserId
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = task.Id }, TaskResponse.From(task));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TaskResponse>> Update(int id, UpdateTaskRequest request)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId && !t.IsDeleted);
        if (task is null) return NotFound();

        task.Title = request.Title;
        task.Description = request.Description;
        task.Status = request.Status;
        task.DueDate = request.DueDate;
        await db.SaveChangesAsync();
        return Ok(TaskResponse.From(task));
    }

    /// <summary>Soft delete: the task moves to the "deleted" list together with an optional reason.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromBody] DeleteTaskRequest? request = null)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId && !t.IsDeleted);
        if (task is null) return NotFound();

        task.IsDeleted = true;
        task.DeletedAt = DateTime.UtcNow;
        task.DeleteReason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Brings a soft-deleted task back to the active list.</summary>
    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult<TaskResponse>> Restore(int id)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId && t.IsDeleted);
        if (task is null) return NotFound();

        task.IsDeleted = false;
        task.DeletedAt = null;
        task.DeleteReason = null;
        await db.SaveChangesAsync();
        return Ok(TaskResponse.From(task));
    }

    /// <summary>Permanently removes a task that is already in the deleted list.</summary>
    [HttpDelete("{id:int}/permanent")]
    public async Task<IActionResult> DeletePermanently(int id)
    {
        var removed = await db.Tasks
            .Where(t => t.Id == id && t.UserId == UserId && t.IsDeleted)
            .ExecuteDeleteAsync();
        return removed == 0 ? NotFound() : NoContent();
    }
}
