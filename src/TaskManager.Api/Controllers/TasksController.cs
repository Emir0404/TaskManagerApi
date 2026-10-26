using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManager.Api.Dtos;
using TaskManager.Api.Models;
using TaskStatus = TaskManager.Api.Models.TaskStatus;

namespace TaskManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tasks")]
public class TasksController(ITaskRepository tasks, IUnitOfWork unitOfWork) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Lists the current user's tasks. Pass deleted=true to list the soft-deleted ones.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<TaskResponse>>> GetAll(
        [FromQuery] TaskStatus? status,
        [FromQuery] string? search,
        [FromQuery] bool deleted = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (status is { } s && !Enum.IsDefined(s))
        {
            ModelState.AddModelError(nameof(status), "Unknown status.");
            return ValidationProblem(ModelState);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await tasks.SearchAsync(UserId, new TaskSearch(status, search, deleted, page, pageSize), ct);
        var items = result.Items.Select(TaskResponse.From).ToList();

        return Ok(new PagedResult<TaskResponse>(items, page, pageSize, result.TotalCount));
    }

    /// <summary>Counts of active tasks per status, overdue tasks and soft-deleted tasks.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<TaskStats>> GetStats(CancellationToken ct)
    {
        var counts = await tasks.CountsAsync(UserId, DateTime.UtcNow.Date, ct);

        return Ok(new TaskStats(counts.Active, counts.Todo, counts.InProgress, counts.Done, counts.Overdue, counts.Deleted));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TaskResponse>> GetById(int id, CancellationToken ct)
    {
        var task = await tasks.FindAsync(UserId, id, deleted: false, ct);
        return task is null ? NotFound() : Ok(TaskResponse.From(task));
    }

    [HttpPost]
    public async Task<ActionResult<TaskResponse>> Create(CreateTaskRequest request, CancellationToken ct)
    {
        var task = new TaskItem
        {
            Title = request.Title,
            Description = request.Description,
            DueDate = request.DueDate,
            UserId = UserId
        };
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = task.Id }, TaskResponse.From(task));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TaskResponse>> Update(int id, UpdateTaskRequest request, CancellationToken ct)
    {
        var task = await tasks.FindAsync(UserId, id, deleted: false, ct);
        if (task is null) return NotFound();

        task.Title = request.Title;
        task.Description = request.Description;
        task.Status = request.Status;
        task.DueDate = request.DueDate;
        await unitOfWork.SaveChangesAsync(ct);
        return Ok(TaskResponse.From(task));
    }

    /// <summary>Soft delete: the task moves to the "deleted" list together with an optional reason.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct, [FromBody] DeleteTaskRequest? request = null)
    {
        var task = await tasks.FindAsync(UserId, id, deleted: false, ct);
        if (task is null) return NotFound();

        task.SoftDelete(request?.Reason, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Brings a soft-deleted task back to the active list.</summary>
    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult<TaskResponse>> Restore(int id, CancellationToken ct)
    {
        var task = await tasks.FindAsync(UserId, id, deleted: true, ct);
        if (task is null) return NotFound();

        task.Restore();
        await unitOfWork.SaveChangesAsync(ct);
        return Ok(TaskResponse.From(task));
    }

    /// <summary>Permanently removes a task that is already in the deleted list.</summary>
    [HttpDelete("{id:int}/permanent")]
    public async Task<IActionResult> DeletePermanently(int id, CancellationToken ct)
    {
        var task = await tasks.FindAsync(UserId, id, deleted: true, ct);
        if (task is null) return NotFound();

        tasks.Remove(task);
        await unitOfWork.SaveChangesAsync(ct);
        return NoContent();
    }
}
