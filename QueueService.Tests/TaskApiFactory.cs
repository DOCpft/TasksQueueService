using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace QueueService.Tests;

public sealed class TaskApiFactory(string postgresConnectionString, string rabbitHost, int rabbitPort, string rabbitUser, string rabbitPassword)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = postgresConnectionString,
                ["RabbitMq:HostName"] = rabbitHost,
                ["RabbitMq:Port"] = rabbitPort.ToString(),
                ["RabbitMq:UserName"] = rabbitUser,
                ["RabbitMq:Password"] = rabbitPassword
            });
        });
    }
}
