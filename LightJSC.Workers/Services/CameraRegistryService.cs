using System.Security.Cryptography;
using System.Text;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Workers.Health;
using LightJSC.Workers.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightJSC.Workers.Services;

public sealed class CameraRegistryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecretProtector _secretProtector;
    private readonly IRtspMetadataClient _rtspClient;
    private readonly IFaceMetadataParser _parser;
    private readonly IngestPipeline _pipeline;
    private readonly IngestOptions _ingestOptions;
    private readonly ReadinessState _readinessState;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CameraRegistryService> _logger;
    private readonly string _contentRootPath;
    private readonly Dictionary<string, CameraRuntime> _connectors = new(StringComparer.OrdinalIgnoreCase);

    public CameraRegistryService(
        IServiceScopeFactory scopeFactory,
        ISecretProtector secretProtector,
        IRtspMetadataClient rtspClient,
        IFaceMetadataParser parser,
        IngestPipeline pipeline,
        IOptions<IngestOptions> ingestOptions,
        ReadinessState readinessState,
        IHostEnvironment hostEnvironment,
        ILoggerFactory loggerFactory,
        ILogger<CameraRegistryService> logger)
    {
        _scopeFactory = scopeFactory;
        _secretProtector = secretProtector;
        _rtspClient = rtspClient;
        _parser = parser;
        _pipeline = pipeline;
        _ingestOptions = ingestOptions.Value;
        _readinessState = readinessState;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _contentRootPath = hostEnvironment.ContentRootPath;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _readinessState.MarkRegistryRunning();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Camera registry sync failed.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        foreach (var runtime in _connectors.Values)
        {
            await runtime.Connector.StopAsync();
        }
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var cameraRepository = scope.ServiceProvider.GetRequiredService<ICameraRepository>();

        var cameras = await cameraRepository.ListAsync(cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var camera in cameras)
        {
            seen.Add(camera.CameraId);
            if (!camera.Enabled)
            {
                await StopConnectorAsync(camera.CameraId);
                continue;
            }

            var hash = ComputeHash(camera);
            if (_connectors.TryGetValue(camera.CameraId, out var runtime))
            {
                if (runtime.ConfigHash == hash)
                {
                    if (!runtime.Connector.IsRunning)
                    {
                        _logger.LogWarning(
                            "Camera connector not running for {CameraId}. LastErrorAt={LastErrorAt} LastError={LastError}. Restarting.",
                            camera.CameraId,
                            runtime.Connector.LastErrorAt,
                            runtime.Connector.LastErrorMessage ?? "none");
                        runtime.Connector.Start();
                    }
                    continue;
                }

                await runtime.Connector.StopAsync();
                _connectors.Remove(camera.CameraId);
            }

            CameraConnector.CameraConnectionInfo connectionInfo;
            try
            {
                connectionInfo = BuildConnectionInfo(camera);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build connection info for {CameraId}", camera.CameraId);
                continue;
            }
            var connector = new CameraConnector(
                connectionInfo,
                _rtspClient,
                _parser,
                _pipeline,
                _ingestOptions,
                _loggerFactory.CreateLogger<CameraConnector>(),
                _contentRootPath);
            connector.Start();

            _connectors[camera.CameraId] = new CameraRuntime(hash, connector);
            _logger.LogInformation("Camera connector started for {CameraId}", camera.CameraId);
        }

        var removed = _connectors.Keys.Except(seen, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var cameraId in removed)
        {
            await StopConnectorAsync(cameraId);
        }
    }

    private async Task StopConnectorAsync(string cameraId)
    {
        if (_connectors.TryGetValue(cameraId, out var runtime))
        {
            await runtime.Connector.StopAsync();
            _connectors.Remove(cameraId);
            _logger.LogInformation("Camera connector stopped for {CameraId}", cameraId);
        }
    }

    private CameraConnector.CameraConnectionInfo BuildConnectionInfo(CameraCredential camera)
    {
        var password = string.IsNullOrWhiteSpace(camera.RtspPasswordEncrypted)
            ? string.Empty
            : _secretProtector.DecryptFromBase64(camera.RtspPasswordEncrypted);

        return new CameraConnector.CameraConnectionInfo
        {
            CameraId = camera.CameraId,
            IpAddress = camera.IpAddress,
            CameraCode = string.IsNullOrWhiteSpace(camera.Code) ? null : camera.Code.Trim(),
            CameraName = string.IsNullOrWhiteSpace(camera.Code) ? null : camera.Code.Trim(),
            RtspUsername = camera.RtspUsername,
            RtspPassword = password,
            RtspProfile = camera.RtspProfile,
            RtspPath = camera.RtspPath,
            CameraSeries = camera.CameraSeries ?? string.Empty,
            CameraModel = camera.CameraModel ?? string.Empty
        };
    }

    private static string ComputeHash(CameraCredential camera)
    {
        var builder = new StringBuilder();
        builder.Append(camera.CameraId).Append('|')
            .Append(camera.IpAddress).Append('|')
            .Append(camera.RtspUsername).Append('|')
            .Append(camera.RtspPasswordEncrypted).Append('|')
            .Append(camera.RtspProfile).Append('|')
            .Append(camera.RtspPath).Append('|')
            .Append(camera.CameraSeries).Append('|')
            .Append(camera.CameraModel).Append('|')
            .Append(camera.Enabled);

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private sealed record CameraRuntime(string ConfigHash, CameraConnector Connector);
}

