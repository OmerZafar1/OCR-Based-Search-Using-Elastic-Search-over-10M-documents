using DocumentSearch.Core.Interfaces;
using DocumentSearch.Core.Options;
using DocumentSearch.Infrastructure;
using DocumentSearch.Infrastructure.Data;
using DocumentSearch.Infrastructure.Data.Entities;
using DocumentSearch.Infrastructure.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var uploadOptions = builder.Configuration.GetSection(UploadOptions.SectionName).Get<UploadOptions>() ?? new UploadOptions();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = uploadOptions.MaxRequestBodySizeBytes;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = uploadOptions.MaxRequestBodySizeBytes;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDocumentSearchInfrastructure(builder.Configuration);
builder.Services.AddMassTransitWithRabbitMq(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (!await db.Folders.AnyAsync())
    {
        var root = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "root",
            MaterializedPath = "/root/",
            CreatedAt = DateTime.UtcNow
        };
        db.Folders.Add(root);
        await FolderPathHelper.RebuildAncestorsAsync(db, root);
        await db.SaveChangesAsync();
    }

    var searchService = scope.ServiceProvider.GetRequiredService<IElasticsearchService>();
    await searchService.EnsureIndexAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseHttpsRedirection();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
