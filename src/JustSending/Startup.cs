using System.Collections.Generic;
using System.IO;
using System.Net;
using Hangfire;
using Hangfire.Storage.SQLite;
using JustSending.Data;
using JustSending.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using Serilog;
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
            services.AddMemoryCache();

            var signalrBuilder = services
                .AddSignalR()
                .AddJsonProtocol();
            var signlrBackplane = Configuration["SignalRBackplane"];
            if (!string.IsNullOrEmpty(signlrBackplane))
            {
                signalrBuilder.AddAzureSignalR(signlrBackplane);
            }

            var redisConfig = Configuration["RedisCache"];
            var hasRedisCache = redisConfig is { Length: > 0 };
            if (hasRedisCache)
            {
                signalrBuilder.AddStackExchangeRedis(redisConfig!);

                // use redis for storage
                services
                    .AddStackExchangeRedisCache(o => o.Configuration = redisConfig)
                    .AddTransient<IDataStore, DataStoreRedis>();

                // Upstash Redis has some issues with
                //services.AddHangfire(x => x.UseRedisStorage(redisConfig));

                // Uses Sqlite for Hangfire storage
                services.AddHangfire(x => x
                    .UseSerilogLogProvider()
                    .UseRecommendedSerializerSettings()
                    .UseSQLiteStorage("Data Source=App_Data\\Hangfire.sqlite;Cache=Shared"));

                // redis lock
                services
                    .AddSingleton<RedLockFactory>(sp => RedLockFactory.Create(new List<RedLockMultiplexer>
                    {
                        new(ConnectionMultiplexer.Connect(redisConfig!))
                    }))
                    .AddTransient<ILock, RedisLock>();
            }
            else
            {
                // in memory hangfire storage
                services.AddHangfire(x => x.UseInMemoryStorage());
                
                // use in memory storage
                services.AddTransient<IDataStore, DataStoreInMemory>();

                // no-op lock
                services.AddTransient<ILock, NoOpLock>();
            }

            services.AddHangfireServer();
            services.AddHttpContextAccessor();

            services
                .AddTransient<AppDbContext>()
                .AddTransient<StatsDbContext>();

            services
                .AddSingleton<ConversationHub>()
                .AddSingleton<SecureLineHub>();

            services.AddTransient<BackgroundJobScheduler>();

            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "App_Data")));
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseSerilogRequestLogging();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/error");
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
            app.UseHangfireDashboard("/jobs", new DashboardOptions
            {
                DisplayStorageConnectionString = false,
                DashboardTitle = "Just An Email",
                Authorization = new[] { new InsecureAuthFilter() }
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
}