using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace QueueService.Messaging;

public sealed class RabbitMqPublisher : BackgroundService, ITaskEventPublisher
{
    private const string ExchangeName = "task.events";
    private const string RoutingKey = "task.completed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Channel<TaskCompletedEvent> _messages = Channel.CreateUnbounded<TaskCompletedEvent>();
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public ValueTask PublishCompletedAsync(TaskCompletedEvent message, CancellationToken cancellationToken = default)
    {
        if (!_messages.Writer.TryWrite(message))
        {
            _logger.LogWarning("RabbitMQ publisher is stopping; completion event for task {TaskId} was not queued", message.TaskId);
        }

        return ValueTask.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await PublishAsync(message, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Failed to publish completion event for task {TaskId}", message.TaskId);
                    await ResetConnectionAsync();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await ResetConnectionAsync();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _messages.Writer.TryComplete();
        try
        {
            if (ExecuteTask is { } executeTask)
            {
                await executeTask.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
            }
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timed out while draining queued RabbitMQ events during shutdown");
        }
        finally
        {
            await base.StopAsync(cancellationToken);
        }
    }

    private async Task PublishAsync(TaskCompletedEvent message, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        await _channel!.BasicPublishAsync(ExchangeName, RoutingKey, false, properties, body, cancellationToken);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null && _channel.IsOpen)
        {
            return;
        }

        await ResetConnectionAsync();
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            AutomaticRecoveryEnabled = true
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await _channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
    }

    private async Task ResetConnectionAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
