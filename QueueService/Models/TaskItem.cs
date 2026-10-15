namespace QueueService.Models;

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
}
