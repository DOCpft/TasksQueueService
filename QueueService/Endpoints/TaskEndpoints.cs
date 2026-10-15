using Microsoft.EntityFrameworkCore;
using QueueService.Data;
using QueueService.Messaging;
using QueueService.Models;

namespace QueueService.Endpoints;

public static class TaskEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tasks = endpoints.MapGroup("/tasks");

        tasks.MapPost("", CreateTaskAsync);
        tasks.MapGet("", GetTasksAsync);
        tasks.MapPut("/{id:guid}/complete", CompleteTaskAsync);
        tasks.MapDelete("/{id:guid}", DeleteTaskAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateTaskAsync(CreateTaskRequest request, TaskDbContext db, CancellationToken cancellationToken)
    {
        // Enum проверять не нужно: JsonStringEnumConverter(allowIntegerValues: false) отклоняет
        // невалидный Priority ещё на этапе десериализации тела запроса (400).
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            return Results.BadRequest(new { error = "Title is required and must be at most 200 characters." });
        }

        var task = new TaskItem
        {
            Title = title,
            Priority = request.Priority,
            CreatedAt = DateTimeOffset.UtcNow,
            IsCompleted = false,
            CompletedAt = null
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/tasks/{task.Id}", task);
    }

    private static async Task<IResult> GetTasksAsync(
        TaskDbContext db,
        int? page,
        int? pageSize,
        bool? isCompleted,
        Priority? priority,
        string? title,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken)
    {
        var query = db.Tasks.AsNoTracking().AsQueryable();

        if (isCompleted.HasValue)
        {
            query = query.Where(task => task.IsCompleted == isCompleted.Value);
        }

        if (priority.HasValue)
        {
            query = query.Where(task => task.Priority == priority.Value);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            query = query.Where(task => EF.Functions.ILike(task.Title, $"%{title}%"));
        }

        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant(), descending) switch
        {
            ("title", false) => query.OrderBy(task => task.Title),
            ("title", true) => query.OrderByDescending(task => task.Title),
            ("priority", false) => query.OrderBy(task => task.Priority),
            ("priority", true) => query.OrderByDescending(task => task.Priority),
            ("completedat", false) => query.OrderBy(task => task.CompletedAt),
            ("completedat", true) => query.OrderByDescending(task => task.CompletedAt),
            (_, true) => query.OrderByDescending(task => task.CreatedAt),
            _ => query.OrderBy(task => task.CreatedAt)
        };

        var totalCount = await query.CountAsync(cancellationToken);

        // если ни page, ни pageSize не переданы, возвращаем все задачи,
        // как требует ТЗ (pageSize в ответе тогда равен фактическому количеству записей).
        if (page is null && pageSize is null)
        {
            var allTasks = await query.ToListAsync(cancellationToken);
            return Results.Ok(new PagedTasksResponse(allTasks, Page: 1, PageSize: totalCount, TotalCount: totalCount));
        }

        var currentPage = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var items = await query.Skip((currentPage - 1) * size).Take(size).ToListAsync(cancellationToken);

        return Results.Ok(new PagedTasksResponse(items, currentPage, size, totalCount));
    }

    private static async Task<IResult> CompleteTaskAsync(
        Guid id,
        TaskDbContext db,
        ITaskEventPublisher publisher,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (task is null)
        {
            return Results.NotFound();
        }

        if (task.IsCompleted)
        {
            return Results.Conflict(new { error = "Task is already completed." });
        }

        task.IsCompleted = true;
        task.CompletedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "Task was completed by another request." });
        }

        await publisher.PublishCompletedAsync(
            new TaskCompletedEvent(task.Id, task.Title, task.CompletedAt.Value, task.Priority),
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteTaskAsync(Guid id, TaskDbContext db, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.FindAsync([id], cancellationToken);
        if (task is null)
        {
            return Results.NotFound();
        }

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    public sealed record CreateTaskRequest(string? Title, Priority Priority = Priority.Medium);
    public sealed record PagedTasksResponse(IReadOnlyList<TaskItem> Items, int Page, int PageSize, int TotalCount);
}
