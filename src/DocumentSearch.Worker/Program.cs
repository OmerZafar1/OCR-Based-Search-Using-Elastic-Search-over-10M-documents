using DocumentSearch.Core.Options;
using DocumentSearch.Infrastructure;
using DocumentSearch.Infrastructure.Data;
using DocumentSearch.Worker.Ingestion;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDocumentSearchInfrastructure(builder.Configuration);

var ingestionOptions = builder.Configuration.GetSection(IngestionOptions.SectionName).Get<IngestionOptions>() ?? new IngestionOptions();
var rabbitOptions = builder.Configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<IngestDocumentConsumer>(cfg =>
    {
        cfg.ConcurrentMessageLimit = ingestionOptions.MaxParallelJobs;
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitOptions.Host, rabbitOptions.VirtualHost, h =>
        {
            h.Username(rabbitOptions.Username);
            h.Password(rabbitOptions.Password);
        });

        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
