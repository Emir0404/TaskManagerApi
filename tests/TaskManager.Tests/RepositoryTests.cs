using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Controllers;
using TaskManager.Api.Data;
using TaskManager.Api.Data.Repositories;
using TaskManager.Api.Models;
using TaskStatus = TaskManager.Api.Models.TaskStatus;

namespace TaskManager.Tests;

/// <summary>The repositories and the unit of work, tested directly against a real (in-memory) SQLite database.</summary>
public class RepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly EfTaskRepository _tasks;
    private readonly EfUserRepository _users;
    private readonly EfUnitOfWork _unitOfWork;
    private readonly int _alice;
    private readonly int _bob;

    public RepositoryTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _tasks = new EfTaskRepository(_db);
        _users = new EfUserRepository(_db);
        _unitOfWork = new EfUnitOfWork(_db);

        var alice = new User { Email = "alice@example.com", PasswordHash = "x" };
        var bob = new User { Email = "bob@example.com", PasswordHash = "x" };
        _db.Users.AddRange(alice, bob);
        _db.SaveChanges();
        _alice = alice.Id;
        _bob = bob.Id;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<TaskItem> AddTask(int userId, string title, TaskStatus status = TaskStatus.Todo, DateTime? due = null)
    {
        var task = new TaskItem { Title = title, UserId = userId, Status = status, DueDate = due };
        _tasks.Add(task);
        await _unitOfWork.SaveChangesAsync();
        return task;
    }

    [Fact]
    public async Task A_user_only_ever_sees_their_own_tasks()
    {
        var aliceTask = await AddTask(_alice, "Alice's task");
        await AddTask(_bob, "Bob's task");

        var page = await _tasks.SearchAsync(_alice, new TaskSearch(null, null, false, 1, 20));

        Assert.Equal(["Alice's task"], page.Items.Select(t => t.Title));
        Assert.Null(await _tasks.FindAsync(_bob, aliceTask.Id, deleted: false));
        Assert.Equal(1, (await _tasks.CountsAsync(_bob, DateTime.UtcNow.Date)).Todo);
    }

    [Fact]
    public async Task Active_and_deleted_tasks_are_kept_apart()
    {
        var task = await AddTask(_alice, "Soon gone");
        task.SoftDelete("not needed", DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync();

        Assert.Null(await _tasks.FindAsync(_alice, task.Id, deleted: false));
        Assert.NotNull(await _tasks.FindAsync(_alice, task.Id, deleted: true));
        Assert.Empty((await _tasks.SearchAsync(_alice, new TaskSearch(null, null, false, 1, 20))).Items);
        Assert.Single((await _tasks.SearchAsync(_alice, new TaskSearch(null, null, true, 1, 20))).Items);
    }

    [Fact]
    public async Task Search_filters_by_status_and_title_and_pages_the_result()
    {
        await AddTask(_alice, "Write report", TaskStatus.Todo);
        await AddTask(_alice, "Write invoice", TaskStatus.Done);
        await AddTask(_alice, "Call supplier", TaskStatus.Todo);

        Assert.Equal(2, (await _tasks.SearchAsync(_alice, new TaskSearch(TaskStatus.Todo, null, false, 1, 20))).TotalCount);
        Assert.Equal(2, (await _tasks.SearchAsync(_alice, new TaskSearch(null, "Write", false, 1, 20))).TotalCount);

        var firstPage = await _tasks.SearchAsync(_alice, new TaskSearch(null, null, false, 1, 2));
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(3, firstPage.TotalCount);
    }

    [Fact]
    public async Task Counts_split_tasks_by_status_and_count_overdue_and_deleted()
    {
        var today = new DateTime(2026, 10, 26);
        await AddTask(_alice, "a", TaskStatus.Todo, due: today.AddDays(-1));           // overdue
        await AddTask(_alice, "b", TaskStatus.InProgress, due: today.AddDays(-5));     // overdue
        await AddTask(_alice, "c", TaskStatus.Done, due: today.AddDays(-5));           // done, so not overdue
        await AddTask(_alice, "d", TaskStatus.Todo, due: today.AddDays(3));            // not yet due
        var gone = await AddTask(_alice, "e", TaskStatus.Todo, due: today.AddDays(-9)); // deleted: counts only as deleted
        gone.SoftDelete(null, today);
        await _unitOfWork.SaveChangesAsync();

        var counts = await _tasks.CountsAsync(_alice, today);

        Assert.Equal(new TaskCounts(Todo: 2, InProgress: 1, Done: 1, Overdue: 2, Deleted: 1), counts);
        Assert.Equal(4, counts.Active);
    }

    [Fact]
    public async Task Removing_a_task_deletes_it_for_good_once_saved()
    {
        var task = await AddTask(_alice, "Temporary");
        task.SoftDelete(null, DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync();

        _tasks.Remove((await _tasks.FindAsync(_alice, task.Id, deleted: true))!);
        Assert.Equal(1, await _db.Tasks.CountAsync()); // not yet: nothing is saved until the unit of work says so
        await _unitOfWork.SaveChangesAsync();

        Assert.Equal(0, await _db.Tasks.CountAsync());
    }

    [Fact]
    public async Task Soft_delete_and_restore_are_the_tasks_own_behaviour()
    {
        var task = await AddTask(_alice, "Flip flop");

        task.SoftDelete("  duplicate  ", new DateTime(2026, 10, 26));
        Assert.True(task.IsDeleted);
        Assert.Equal("duplicate", task.DeleteReason);
        Assert.Equal(new DateTime(2026, 10, 26), task.DeletedAt);

        task.Restore();
        Assert.False(task.IsDeleted);
        Assert.Null(task.DeleteReason);
        Assert.Null(task.DeletedAt);

        task.SoftDelete("   ", DateTime.UtcNow);
        Assert.Null(task.DeleteReason); // a blank reason means no reason
    }

    [Fact]
    public async Task Users_are_found_by_email()
    {
        Assert.True(await _users.EmailExistsAsync("alice@example.com"));
        Assert.False(await _users.EmailExistsAsync("nobody@example.com"));
        Assert.Equal(_alice, (await _users.FindByEmailAsync("alice@example.com"))!.Id);
        Assert.Null(await _users.FindByEmailAsync("nobody@example.com"));
    }

    [Fact]
    public async Task A_second_account_with_the_same_email_surfaces_as_a_DataConflictException()
    {
        _users.Add(new User { Email = "alice@example.com", PasswordHash = "y" });

        await Assert.ThrowsAsync<DataConflictException>(() => _unitOfWork.SaveChangesAsync());
    }

    [Fact]
    public void Controllers_depend_on_repositories_never_on_Entity_Framework()
    {
        var controllers = typeof(TasksController).Assembly.GetTypes()
            .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var type in controllers)
            foreach (var parameter in type.GetConstructors().SelectMany(c => c.GetParameters()))
                Assert.False(
                    typeof(DbContext).IsAssignableFrom(parameter.ParameterType),
                    $"{type.Name} takes a DbContext. Go through a repository instead.");
    }
}
