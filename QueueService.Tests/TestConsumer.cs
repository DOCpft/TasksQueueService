using System.Text;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace QueueService.Tests;

public sealed class TestConsumer : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly Channel<string> _messages;

    private TestConsumer(IConnection connection, IChannel channel, Channel<string> messages)
    {
        _connection = connection;
        _channel = channel;
        _messages = messages;
    }

    public static async Task<TestConsumer> StartAsync(string host, int port, string userName, string password)
    {
        var factory = new ConnectionFactory
        {
            HostName = host,
            Port = port,
            UserName = userName,
            Password = password
        };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        var queueName = $"test.task.completed.{Guid.NewGuid():N}";

        await channel.ExchangeDeclareAsync("task.events", ExchangeType.Topic, durable: true);
        await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queueName, "task.events", "task.completed");

        var consumer = new AsyncEventingBasicConsumer(channel);
        var messages = Channel.CreateUnbounded<string>();
        consumer.ReceivedAsync += (_, args) =>
        {
            messages.Writer.TryWrite(Encoding.UTF8.GetString(args.Body.Span));
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(queueName, autoAck: true, consumer);

        return new TestConsumer(connection, channel, messages);
    }

    public async Task<string> ReadMessageAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        return await _messages.Reader.ReadAsync(cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
