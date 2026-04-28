using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LightJSC.Subscriber.Hubs;
using LightJSC.Subscriber.Models;
using LightJSC.Subscriber.Options;
using LightJSC.Subscriber.Services;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SubscriberOptions>(builder.Configuration.GetSection("Subscriber"));
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<FaceEventBuffer>();
builder.Services.AddSingleton<SignatureVerifier>();

var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/v1/events", (FaceEventBuffer buffer) =>
{
    return Results.Ok(buffer.GetSnapshot());
});

app.MapPost("/api/v1/webhooks/face", async (
    HttpContext context,
    FaceEventBuffer buffer,
    SignatureVerifier verifier,
    IMemoryCache cache,
    IOptions<SubscriberOptions> options,
    IHubContext<FaceEventsHub> hub) =>
{
    context.Request.EnableBuffering();
    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
    var body = await reader.ReadToEndAsync();
    context.Request.Body.Position = 0;

    if (string.IsNullOrWhiteSpace(body))
    {
        return Results.BadRequest(new { error = "Payload is empty." });
    }

    if (!verifier.IsValid(context, body))
    {
        return Results.Unauthorized();
    }

    FaceWebhookPayload? payload;
    try
    {
        payload = JsonSerializer.Deserialize<FaceWebhookPayload>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Invalid JSON payload." });
    }

    if (payload is null || string.IsNullOrWhiteSpace(payload.CameraId))
    {
        return Results.BadRequest(new { error = "CameraId is required." });
    }

    var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(idempotencyKey))
    {
        idempotencyKey = BuildFallbackIdempotencyKey(payload);
    }

    if (!string.IsNullOrWhiteSpace(idempotencyKey) && cache.TryGetValue(idempotencyKey, out _))
    {
        return Results.Accepted();
    }

    if (!string.IsNullOrWhiteSpace(idempotencyKey))
    {
        cache.Set(idempotencyKey, true, TimeSpan.FromMinutes(options.Value.DedupMinutes));
    }

    var faceEvent = FaceEventDto.FromPayload(payload, idempotencyKey ?? string.Empty);
    buffer.Add(faceEvent);

    await hub.Clients.All.SendAsync("faceEvent", faceEvent);

    return Results.Ok(new { success = true });
});

app.MapHub<FaceEventsHub>("/hubs/faces");
app.MapFallbackToFile("index.html");

app.Run();

static string BuildFallbackIdempotencyKey(FaceWebhookPayload payload)
{
    var raw = payload.FeatureBase64 ?? string.Empty;
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    return $"{payload.CameraId}:{payload.EventTimeUtc.ToUnixTimeMilliseconds()}:{hash}";
}

