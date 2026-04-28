using LightJSC.Api.Health;
using LightJSC.Api.MapTiles;
using LightJSC.Api.Services;
using LightJSC.Api.Subscriber;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Options;
using LightJSC.Infrastructure.Data;
using LightJSC.Infrastructure.Data.Repositories;
using LightJSC.Infrastructure.Discovery;
using LightJSC.Infrastructure.Enrollment;
using LightJSC.Infrastructure.Imaging;
using LightJSC.Infrastructure.Parsers;
using LightJSC.Infrastructure.Processing;
using LightJSC.Infrastructure.Rtsp;
using LightJSC.Infrastructure.Security;
using LightJSC.Infrastructure.Vector;
using LightJSC.Workers.Health;
using LightJSC.Workers.Pipeline;
using LightJSC.Workers.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using Serilog;
using System.Reflection;
using Dapper;

// var builder = WebApplication.CreateBuilder(args);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Look for static files in "client"
    WebRootPath = "client"
});

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

builder.Services.Configure<EncryptionOptions>(builder.Configuration.GetSection("Encryption"));
builder.Services.Configure<RtspOptions>(builder.Configuration.GetSection("Rtsp"));
builder.Services.Configure<IngestOptions>(builder.Configuration.GetSection("Ingest"));
builder.Services.Configure<WatchlistOptions>(builder.Configuration.GetSection("Watchlist"));
builder.Services.Configure<MatchingOptions>(builder.Configuration.GetSection("Matching"));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhook"));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Workers"));
builder.Services.Configure<EnrollmentOptions>(builder.Configuration.GetSection("Enrollment"));
builder.Services.Configure<FaceDetectionOptions>(builder.Configuration.GetSection("FaceDetection"));
builder.Services.Configure<SubscriberServiceOptions>(builder.Configuration.GetSection("SubscriberService"));
builder.Services.Configure<VectorIndexOptions>(builder.Configuration.GetSection("VectorIndex"));
builder.Services.Configure<BestshotOptions>(builder.Configuration.GetSection("Bestshot"));
builder.Services.Configure<MapOptions>(builder.Configuration.GetSection("Map"));
builder.Services.Configure<MapTileOptions>(builder.Configuration.GetSection("MapTiles"));
builder.Services.Configure<RoutingOptions>(builder.Configuration.GetSection("Routing"));
builder.Services.Configure<PipelineWatchdogOptions>(builder.Configuration.GetSection("PipelineWatchdog"));
builder.Services.Configure<PersonScanOptions>(builder.Configuration.GetSection("PersonScan"));

builder.Services.AddSingleton<MapTileRepository>();
builder.Services.AddSingleton<IRoutingService, LightJSC.Infrastructure.Routing.ItineroRoutingService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("MapCors", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowAnyOrigin();
    });
});

var postgresConnection = builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["Postgres:ConnectionString"]
    ?? string.Empty;

builder.Services.AddDbContext<IngestorDbContext>(options =>
    options.UseNpgsql(postgresConnection));

builder.Services.AddScoped<ICameraRepository, CameraRepository>();
builder.Services.AddScoped<ISubscriberRepository, SubscriberRepository>();
builder.Services.AddScoped<IDlqRepository, DlqRepository>();
builder.Services.AddScoped<IRuntimeStateRepository, RuntimeStateRepository>();
builder.Services.AddScoped<IPersonRepository, PersonRepository>();
builder.Services.AddScoped<IFaceTemplateRepository, FaceTemplateRepository>();
builder.Services.AddScoped<IFaceEventRepository, FaceEventRepository>();
builder.Services.AddScoped<FaceEventSearchRepository>();
builder.Services.AddScoped<AttendanceRepository>();
builder.Services.AddScoped<DashboardRepository>();
builder.Services.AddScoped<IMapRepository, MapRepository>();

builder.Services.AddScoped<IWatchlistRepository, LocalWatchlistRepository>();

builder.Services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
builder.Services.AddSingleton<IFaceMetadataParser, FaceMetadataParser>();
builder.Services.AddSingleton<IRtspMetadataClient, RtspMetadataClient>();
builder.Services.AddSingleton<IFaceEventDeduplicator, FaceEventDeduplicator>();
builder.Services.AddSingleton<IVectorIndex, PostgresVectorIndex>();
builder.Services.AddSingleton<IFaceEventIndex, PostgresFaceEventIndex>();
builder.Services.AddSingleton<IFaceEnrollmentClient, AdamFaceEnrollmentClient>();
builder.Services.AddSingleton<IFaceDetectorService, LightJSC.Infrastructure.Detection.ScrfdFaceDetector>();
builder.Services.AddSingleton<IRtspSnapshotService, RtspSnapshotService>();
builder.Services.AddSingleton<ICameraDiscoveryService, OnvifDiscoveryService>();
builder.Services.AddSingleton<PersonScanSessionService>();
builder.Services.AddScoped<BestshotResolver>();

if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IQrCodeDecoder, ZxingQrCodeDecoder>();
}
else
{
    builder.Services.AddSingleton<IQrCodeDecoder, NullQrCodeDecoder>();
}

builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<FaceEventBuffer>();
builder.Services.AddSingleton<SignatureVerifier>();
builder.Services.AddSingleton<SignalRRealtimeEventPublisher>();
builder.Services.AddSingleton<IRealtimeEventPublisher>(sp => sp.GetRequiredService<SignalRRealtimeEventPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SignalRRealtimeEventPublisher>());

builder.Services.AddSingleton<ReadinessState>();
builder.Services.AddSingleton<IngestPipeline>();
builder.Services.AddSingleton<BestshotStorageService>();

builder.Services.AddHostedService<CameraRegistryService>();
builder.Services.AddHostedService<WatchlistSyncService>();
builder.Services.AddHostedService<MatchingWorkerService>();
builder.Services.AddHostedService<WebhookDispatcherService>();
builder.Services.AddHostedService<BestshotRetentionService>();
builder.Services.AddHostedService<PipelineWatchdogService>();

builder.Services.AddHttpClient("webhook", client =>
{
    var timeoutSeconds = builder.Configuration.GetValue("Webhook:TimeoutSeconds", 5);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

builder.Services.AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy())
    .AddCheck<ReadinessHealthCheck>("ready");

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseDefaultFiles();
app.UseCors("MapCors");
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI();
app.UseReDoc(options =>
{
    options.RoutePrefix = "docs";
    options.SpecUrl("/swagger/v1/swagger.json");
    options.DocumentTitle = "LightJSC API Docs";
    options.ExpandResponses("200,201,400,401,403,404");
    options.ScrollYOffset(10);
});

app.UseHttpMetrics();
app.MapMetrics("/metrics");

app.MapControllers();
app.MapHub<FaceEventsHub>("/hubs/faces");

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "live"
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "ready"
});

app.MapFallbackToFile("index.html");

app.Run();
