using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Dashboard;
using JustSending.Data;
using JustSending.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace JustSending
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public static IConfiguration Configuration { get; private set; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(Configuration);

            services.Configure<FormOptions>(x =>
            {
                x.ValueLengthLimit = int.MaxValue;
                x.MultipartBodyLengthLimit = int.MaxValue;
                x.MultipartHeadersLengthLimit = int.MaxValue;
            });

            services.AddMvc();
            services.AddHealthChecks().AddCheck<DefaultHealthCheck>("database");
            services.AddSignalR();
            services.AddMemoryCache();

            var redisCache = Configuration["RedisCache"];
            var hasRedisCache = redisCache is {Length: >0}; 
            if (hasRedisCache)
            {
                // use redis for storage
                services
                    .AddStackExchangeRedisCache(o => o.Configuration = Configuration["RedisCache"])
                    .AddTransient<IDataStore, DataStoreRedis>();
                
                // redis for hangfire jobs
                services.AddHangfire(x => x.UseRedisStorage(redisCache));
                
                // redis lock
                services
                    .AddSingleton<RedLockFactory>(sp => RedLockFactory.Create(new List<RedLockMultiplexer>
                    {
                        new(ConnectionMultiplexer.Connect(redisCache))
                    }))
                    .AddTransient<ILock, RedisLock>();
            }
            else
            {
                // use in memory storage
                services.AddTransient<IDataStore, DataStoreInMemory>();
                
                // in memory hangfire storage
                services.AddHangfire(x => x.UseInMemoryStorage());
                
                // no-op lock
                services.AddTransient<ILock, NoOpLock>();
            }

            services.AddHttpContextAccessor();

            services
                .AddTransient<AppDbContext>()
                .AddTransient<StatsDbContext>();

            services
                .AddSingleton<ConversationHub>()
                .AddSingleton<SecureLineHub>();

            services.AddTransient<BackgroundJobScheduler>();

            services.AddHealthChecks();
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "App_Data")));
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseHealthChecks("/api/test");

            app.UseCors(c =>
            {
                c
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowAnyOrigin()
                    .Build();
            });

            app.UseStaticFiles();

            app.UseHangfireServer();
            app.UseHangfireDashboard("/jobs", new DashboardOptions
            {
                Authorization = new[] { new CookieAuthFilter(Configuration["HangfireToken"]) }
            });

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<ConversationHub>("/signalr/hubs");
                endpoints.MapHub<SecureLineHub>("/signalr/secure-line");

                endpoints.MapControllers();
            });
        }
    }

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

    public class CookieAuthFilter : IDashboardAuthorizationFilter
    {
        private readonly string _token;

        public CookieAuthFilter(string token)
        {
            _token = token;
        }
        public bool Authorize(DashboardContext context)
        {
#if DEBUG
            return true;
#endif

            return context.GetHttpContext().Request.Cookies.TryGetValue("HangfireToken", out var tokenFromCookie)
                   && tokenFromCookie == _token;
        }
    }
}
