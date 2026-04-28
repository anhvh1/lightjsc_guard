using Prometheus;

namespace LightJSC.Workers.Metrics;

public static class MetricsRegistry
{
    public static readonly Counter IngestEvents = Prometheus.Metrics.CreateCounter(
        "ipro_ingest_events_total",
        "Total ingest events parsed from RTSP metadata.",
        new CounterConfiguration { LabelNames = new[] { "camera_id" } });

    public static readonly Counter IngestDropped = Prometheus.Metrics.CreateCounter(
        "ipro_ingest_dropped_total",
        "Total ingest events dropped.",
        new CounterConfiguration { LabelNames = new[] { "reason" } });

    public static readonly Gauge QueueLength = Prometheus.Metrics.CreateGauge(
        "ipro_queue_length",
        "Current queue length.",
        new GaugeConfiguration { LabelNames = new[] { "queue" } });

    public static readonly Histogram MatchLatency = Prometheus.Metrics.CreateHistogram(
        "ipro_match_latency_seconds",
        "Matching latency in seconds.");

    public static readonly Counter WebhookSuccess = Prometheus.Metrics.CreateCounter(
        "ipro_webhook_success_total",
        "Total webhook deliveries succeeded.",
        new CounterConfiguration { LabelNames = new[] { "subscriber_id" } });

    public static readonly Counter WebhookFail = Prometheus.Metrics.CreateCounter(
        "ipro_webhook_fail_total",
        "Total webhook deliveries failed.",
        new CounterConfiguration { LabelNames = new[] { "subscriber_id" } });

    public static readonly Gauge WatchlistSize = Prometheus.Metrics.CreateGauge(
        "ipro_watchlist_size",
        "Number of entries in watchlist index.");
}

