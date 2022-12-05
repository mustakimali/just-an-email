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
        private readonly AppDbContext _appDb;

        public DefaultHealthCheck(StatsDbContext dbContext, AppDbContext appDb, ILogger<DefaultHealthCheck> logger)
        {
            _appDb = appDb;
            _dbContext = dbContext;
            _logger = logger;
        }
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new())
        {
            try
            {
                UpdateMetrics();
                await _appDb.GetInternal<int>("does-not-exist");
                return HealthCheckResult.Healthy();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Healthcheck failed.");
                return HealthCheckResult.Unhealthy();
            }
        }

        private void UpdateMetrics()
        {
            var m = _dbContext.Statistics.FindById(-1);
            if (m == null)return;

            Metrics.TotalSessions.Set(m.Sessions);
            Metrics.TotalFiles.Set(m.Files);
            Metrics.TotalFilesSizeBytes.Set(m.FilesSizeBytes);
            Metrics.TotalDevices.Set(m.Devices);
            Metrics.TotalMessages.Set(m.Messages);
            Metrics.TotalMessageBytes.Set(m.MessagesSizeBytes);
        }
    }
}