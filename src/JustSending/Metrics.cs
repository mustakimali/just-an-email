using Prometheus;

namespace JustSending
{
    public static class Metrics
    {
        public static readonly Gauge TotalSessions = Prometheus.Metrics.CreateGauge("stat_total_sessions", "Number of sessions.");
        public static readonly Gauge TotalFiles = Prometheus.Metrics.CreateGauge("stat_total_files", "Number of files.");
        public static readonly Gauge TotalFilesSizeBytes = Prometheus.Metrics.CreateGauge("stat_total_file_size", "Number of files.");
        public static readonly Gauge TotalDevices = Prometheus.Metrics.CreateGauge("stat_total_devices", "Number of devices.");
        public static readonly Gauge TotalMessages = Prometheus.Metrics.CreateGauge("stat_total_messages", "Number of messages.");
        public static readonly Gauge TotalMessageBytes = Prometheus.Metrics.CreateGauge("stat_total_message_size", "Number of messages.");
    }
}