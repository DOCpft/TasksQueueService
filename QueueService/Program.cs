using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QueueService.Data;
using QueueService.Endpoints;
using QueueService.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TaskDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<ITaskEventPublisher>(serviceProvider => serviceProvider.GetRequiredService<RabbitMqPublisher>());
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<RabbitMqPublisher>());
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapTaskEndpoints();
app.Run();

public partial class Program;