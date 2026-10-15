using Microsoft.EntityFrameworkCore;
using QueueService.Models;

namespace QueueService.Data;

public sealed class TaskDbContext(DbContextOptions<TaskDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskItem>();
        task.HasKey(item => item.Id);
        task.Property(item => item.Title).HasMaxLength(200).IsRequired();
        task.Property(item => item.Priority).HasConversion<string>().HasMaxLength(16);
        task.Property<uint>("xmin").IsRowVersion();
    }
}
