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

    [HttpGet]
    public async Task<ActionResult<PagedResult<TaskResponse>>> GetAll(
        [FromQuery] TaskStatus? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Tasks.AsNoTracking().Where(t => t.UserId == UserId);
        if (status is not null) query = query.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(t => t.Title.Contains(search));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => TaskResponse.From(t))
            .ToListAsync();

        return Ok(new PagedResult<TaskResponse>(items, page, pageSize, total));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TaskResponse>> GetById(int id)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
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
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (task is null) return NotFound();

        task.Title = request.Title;
        task.Description = request.Description;
        task.Status = request.Status;
        task.DueDate = request.DueDate;
        await db.SaveChangesAsync();
        return Ok(TaskResponse.From(task));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await db.Tasks.Where(t => t.Id == id && t.UserId == UserId).ExecuteDeleteAsync();
        return deleted == 0 ? NotFound() : NoContent();
    }
}
