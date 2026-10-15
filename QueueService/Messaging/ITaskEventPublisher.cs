namespace QueueService.Messaging;

public interface ITaskEventPublisher
{
    ValueTask PublishCompletedAsync(TaskCompletedEvent message, CancellationToken cancellationToken = default);
}
