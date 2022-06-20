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
                var _ = _dbContext.Statistics.Count();
                return Task.FromResult(HealthCheckResult.Healthy());
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Healthcheck failed.");
                return Task.FromResult(HealthCheckResult.Unhealthy());
            }
        }
    }
}