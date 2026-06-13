using DocumentSearch.Core.Interfaces;
using DocumentSearch.Core.Options;
using DocumentSearch.Infrastructure.Data;
using DocumentSearch.Infrastructure.Extraction;
using DocumentSearch.Infrastructure.Search;
using DocumentSearch.Infrastructure.Services;
using DocumentSearch.Infrastructure.Storage;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Elastic.Clients.Elasticsearch;

namespace DocumentSearch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDocumentSearchInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<ElasticsearchOptions>(configuration.GetSection(ElasticsearchOptions.SectionName));
        services.Configure<OcrOptions>(configuration.GetSection(OcrOptions.SectionName));
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<UploadOptions>(configuration.GetSection(UploadOptions.SectionName));

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")));

        var elasticsearchUrl = configuration.GetSection(ElasticsearchOptions.SectionName)["Url"] ?? "http://localhost:9200";
        var indexName = configuration.GetSection(ElasticsearchOptions.SectionName)["IndexName"] ?? "documents";
        var settings = new ElasticsearchClientSettings(new Uri(elasticsearchUrl))
            .DefaultIndex(indexName);
        services.AddSingleton(new ElasticsearchClient(settings));

        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IDocumentTextExtractor, DocumentTextExtractor>();
        services.AddScoped<IElasticsearchService, ElasticsearchService>();
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddSingleton<IBulkIngestService, BulkIngestService>();

        return services;
    }

    public static IServiceCollection AddMassTransitWithRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        var rabbitOptions = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();

        services.AddMassTransit(x =>
        {
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

        return services;
    }
}
