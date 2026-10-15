using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace QueueService.Tests;

public sealed class CompleteTaskIntegrationTest : IAsyncLifetime
{
    private const string RabbitUserName = "queueuser";
    private const string RabbitPassword = "queuepassword";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:4-management-alpine")
        .WithUsername(RabbitUserName)
        .WithPassword(RabbitPassword)
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task CreateAndCompleteTask_PublishesCompletionEvent()
    {
        await using var consumer = await TestConsumer.StartAsync(
            _rabbitMq.Hostname,
            _rabbitMq.GetMappedPublicPort(5672),
            RabbitUserName,
            RabbitPassword);
        using var factory = new TaskApiFactory(
            _postgres.GetConnectionString(),
            _rabbitMq.Hostname,
            _rabbitMq.GetMappedPublicPort(5672),
            RabbitUserName,
            RabbitPassword);
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/tasks", new { title = "Buy milk", priority = "High" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var createdJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var taskId = createdJson.RootElement.GetProperty("id").GetGuid();
        Assert.False(createdJson.RootElement.GetProperty("isCompleted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, createdJson.RootElement.GetProperty("completedAt").ValueKind);

        var completeResponse = await client.PutAsync($"/tasks/{taskId}/complete", content: null);
        Assert.Equal(HttpStatusCode.NoContent, completeResponse.StatusCode);

        var message = await consumer.ReadMessageAsync(TimeSpan.FromSeconds(10));
        using var eventJson = JsonDocument.Parse(message);
        var eventRoot = eventJson.RootElement;
        Assert.Equal(taskId, eventRoot.GetProperty("taskId").GetGuid());
        Assert.Equal("Buy milk", eventRoot.GetProperty("title").GetString());
        Assert.Equal("High", eventRoot.GetProperty("priority").GetString());
        Assert.True(eventRoot.GetProperty("completedAt").GetDateTimeOffset() > DateTimeOffset.MinValue);
    }
}
