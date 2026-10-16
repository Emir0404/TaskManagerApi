using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Models;

namespace TaskManager.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(256);
        });

        modelBuilder.Entity<TaskItem>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(200);
            e.Property(t => t.Status).HasConversion<string>();
            e.Property(t => t.DeleteReason).HasMaxLength(500);
            e.HasIndex(t => new { t.UserId, t.IsDeleted });
            e.HasOne(t => t.User).WithMany(u => u.Tasks).HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
