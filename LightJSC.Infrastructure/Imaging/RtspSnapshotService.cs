using System.Diagnostics;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightJSC.Infrastructure.Imaging;

public sealed class RtspSnapshotService : IRtspSnapshotService
{
    private readonly PersonScanOptions _options;
    private readonly ILogger<RtspSnapshotService> _logger;

    public RtspSnapshotService(
        IOptions<PersonScanOptions> options,
        ILogger<RtspSnapshotService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<byte[]> CaptureJpegAsync(Uri rtspUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rtspUri);

        using var process = new Process
        {
            StartInfo = BuildStartInfo(rtspUri)
        };

        _logger.LogInformation("Capturing RTSP snapshot from {RtspUri}", rtspUri);
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start ffmpeg for RTSP snapshot.");
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(3, _options.SnapshotTimeoutSeconds));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        await using var output = new MemoryStream();
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output, timeoutCts.Token);
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await Task.WhenAll(copyTask, process.WaitForExitAsync(timeoutCts.Token));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException($"RTSP snapshot timed out after {timeout.TotalSeconds:0} seconds.");
        }

        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg exited with code {process.ExitCode}: {TrimError(error)}");
        }

        var bytes = output.ToArray();
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg returned an empty snapshot: {TrimError(error)}");
        }

        return bytes;
    }

    private ProcessStartInfo BuildStartInfo(Uri rtspUri)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(_options.FfmpegPath) ? "ffmpeg" : _options.FfmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-rtsp_transport");
        startInfo.ArgumentList.Add("tcp");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(rtspUri.ToString());
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("image2pipe");
        startInfo.ArgumentList.Add("-vcodec");
        startInfo.ArgumentList.Add("mjpeg");
        startInfo.ArgumentList.Add("-");
        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static string TrimError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "unknown ffmpeg error";
        }

        return error.Trim().Length <= 300 ? error.Trim() : error.Trim()[..300];
    }
}
