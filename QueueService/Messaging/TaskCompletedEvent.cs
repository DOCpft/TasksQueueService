using QueueService.Models;

namespace QueueService.Messaging;

public sealed record TaskCompletedEvent(Guid TaskId, string Title, DateTimeOffset CompletedAt, Priority Priority);
