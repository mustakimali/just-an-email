using System;
using System.Threading;
using System.Threading.Tasks;
using JustSending.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace JustSending.Services
{
    public class DefaultHealthCheck : IHealthCheck
    {
        private readonly StatsDbContext _dbContext;
        private readonly ILogger<DefaultHealthCheck> _logger;

        public DefaultHealthCheck(StatsDbContext dbContext, ILogger<DefaultHealthCheck> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new())
        {
            try
            {
                UpdateMetrics();
                return Task.FromResult(HealthCheckResult.Healthy());
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Healthcheck failed.");
                return Task.FromResult(HealthCheckResult.Unhealthy());
            }
        }

        private void UpdateMetrics()
        {
            var m = _dbContext.Statistics.FindById(-1);
            if (m == null) return;

            Metrics.TotalSessions.Set(m.Sessions);
            Metrics.TotalFiles.Set(m.Files);
            Metrics.TotalFilesSizeBytes.Set(m.FilesSizeBytes);
            Metrics.TotalDevices.Set(m.Devices);
            Metrics.TotalMessages.Set(m.Messages);
            Metrics.TotalMessageBytes.Set(m.MessagesSizeBytes);
        }
    }
}