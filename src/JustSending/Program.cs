using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System.Collections.Generic;
using System.IO;
using Hangfire;
using Hangfire.Storage.SQLite;
using JustSending.Data;
using JustSending.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;
using Serilog.Core;
using Prometheus;
using OpenTelemetry.Trace;
using System.Threading.Tasks;
using System.Linq;

public class Program
{
    private static async Task Main(string[] args)
    {
        Log.Logger = CreateLogger();

        var app = InitializeApp(args);

        try
        {
            Log.Information("Starting web host");
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static WebApplication InitializeApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog();
        builder.WebHost.UseSentry(c => c.AutoSessionTracking = true);
        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        ConfigureMiddleware(app);
        return app;
    }

    static Logger CreateLogger()
        => new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
            .WriteTo.Sentry(o =>
            {
                o.MinimumBreadcrumbLevel = LogEventLevel.Debug;
                o.MinimumEventLevel = LogEventLevel.Warning;
            })
            .Filter.ByExcluding(logEvent =>
            {
                if (logEvent.Properties.TryGetValue("RequestPath", out var p))
                {
                    var path = p.ToString();
                    return path.Contains("/api/test");
                }

                return false;
            })
            .CreateLogger();

    static void ConfigureServices(IServiceCollection services, IConfigurationRoot config)
    {
        services.AddSingleton(config);

        var honeycombOptions = config.GetHoneycombOptions();
        services
            .AddOpenTelemetryTracing(builder => builder
                .AddHoneycomb(honeycombOptions)
                .AddAspNetCoreInstrumentation(c =>
                {
                    c.Filter = (ctx) => (!ctx.Request.Path.Value?.StartsWith("/api/test") ?? true) && (!ctx.Request.Path.Value?.StartsWith("/metrics") ?? true);
                    c.EnableGrpcAspNetCoreSupport = true;
                    c.RecordException = true;
                    c.EnrichWithBaggage();
                })
                .AddAutoInstrumentations()
                .AddHttpClientInstrumentation())
            .AddSingleton(TracerProvider.Default.GetTracer(honeycombOptions.ServiceName));

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

        var redisConfig = config["RedisCache"];
        var hasRedisCache = redisConfig is { Length: > 0 };
        if (hasRedisCache)
        {
            signalrBuilder.AddStackExchangeRedis(redisConfig!);

            // use redis for storage

            services
                .AddStackExchangeRedisCache(c => c.Configuration = redisConfig)
                .AddTransient<IDataStore, DataStoreRedis>();

            // Upstash Redis has some issues with
            services.AddHangfire(x => x
                .UseSerilogLogProvider()
                .UseRecommendedSerializerSettings()
                .UseRedisStorage(redisConfig));

            // redis lock
            services
                .AddSingleton(sp => RedLockFactory.Create(new List<RedLockMultiplexer>
                {
                new(ConnectionMultiplexer.Connect(redisConfig!))
                }))
                .AddTransient<ILock, RedisLock>();
        }
        else
        {
            // hangfire?
            // Uses Sqlite for Hangfire storage
            if (!Directory.Exists("App_Data"))
                Directory.CreateDirectory("App_Data");

            services.AddHangfire(x => x
                .UseSerilogLogProvider()
                .UseRecommendedSerializerSettings()
                .UseSQLiteStorage("App_Data/Hangfire.db"));

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

        services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "App_Data")));

        services
            .AddMetricServer(c => c.Port = 9091);
    }

    static void ConfigureMiddleware(WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            foreach (var header in ctx.Request.Headers)
            {
                var value = string.Join(",", header.Value.Where(v => v != null).Select(v => v!.ToString()));
                Tracer.CurrentSpan.SetAttribute($"http.header.{header.Key.ToLower()}", value);
            }
            ctx.Response.Headers.Add("trace-id", Tracer.CurrentSpan.Context.TraceId.ToHexString());

            await next(ctx);
        });

        app
            .UseHttpMetrics()
            .UseSerilogRequestLogging();

        if (app.Environment.IsDevelopment())
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