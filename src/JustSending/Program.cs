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
using FluentMigrator.Runner;
using Hangfire.States;
using Hangfire.Common;
using Microsoft.Extensions.Logging;

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
            .WriteTo.Console()
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

        // hangfire?
        // Uses Sqlite for Hangfire storage
        if (!Directory.Exists("App_Data"))
            Directory.CreateDirectory("App_Data");

        services.AddHangfire(x => x
            .UseSerilogLogProvider()
            .UseRecommendedSerializerSettings()
            .UseSQLiteStorage("App_Data/Hangfire.db"));

        services.AddTransient<IDataStore, DataStoreSqlite>();

        // no-op lock
        services.AddTransient<ILock, NoOpLock>();

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

        services
            .AddFluentMigratorCore()
            .ConfigureRunner(c => c
                .AddSQLite()
                .WithGlobalConnectionString("StatsCs")
                .ScanIn(typeof(Program).Assembly).For.Migrations());
    }

    static void ConfigureMiddleware(WebApplication app)
    {
        using (var scope = app.Services.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
        }

        app.Use(async (ctx, next) =>
        {
            foreach (var header in ctx.Request.Headers)
            {
                var value = string.Join(",", header.Value.Where(v => v != null).Select(v => v!.ToString()));
                Tracer.CurrentSpan.SetAttribute($"http.header.{header.Key.ToLower()}", value);
            }
            ctx.Response.Headers["trace-id"] = Tracer.CurrentSpan.Context.TraceId.ToHexString();

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
            Authorization = [new InsecureAuthFilter()]
        });
        ConfigureHangfire(app);

        app.UseRouting();

        app.MapHub<ConversationHub>("/signalr/hubs");
        app.MapHub<SecureLineHub>("/signalr/secure-line");
        app.MapControllers();
    }

    public static void ConfigureHangfire(IApplicationBuilder app)
    {
        object automaticRetryAttribute = null;
        foreach (var filter in GlobalJobFilters.Filters)
        {
            if (filter.Instance is Hangfire.AutomaticRetryAttribute)
            {
                automaticRetryAttribute = filter.Instance;
                break;
            }
        }

        if (automaticRetryAttribute == null)
        {
            throw new Exception("Didn't find hangfire automaticRetryAttribute");
        }

        // Remove the default and add your custom implementation
        GlobalJobFilters.Filters.Remove(automaticRetryAttribute);
        GlobalJobFilters.Filters.Add(new CustomExponentialBackoffWithJitterAttribute(app.ApplicationServices.GetService<ILogger<CustomExponentialBackoffWithJitterAttribute>>()!));
    }
}

public class CustomExponentialBackoffWithJitterAttribute : JobFilterAttribute, IElectStateFilter
{
    private const int DefaultRetryAttempts = 10;
    private int _attempts;
    private readonly Random _random = new();
    private readonly ILogger<CustomExponentialBackoffWithJitterAttribute> _logger;

    public CustomExponentialBackoffWithJitterAttribute(ILogger<CustomExponentialBackoffWithJitterAttribute> logger)
    {
        Attempts = DefaultRetryAttempts;
        LogEvents = true;
        _logger = logger;
    }

    public int Attempts
    {
        get { return _attempts; }
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException("value", "Attempts value must be equal or greater than zero.");
            }
            _attempts = value;
        }
    }

    public bool LogEvents { get; set; }

    public void OnStateElection(ElectStateContext context)
    {
        var failedState = context.CandidateState as FailedState;
        if (failedState == null)
        {
            return;
        }

        var retryAttempt = context.GetJobParameter<int>("RetryCount") + 1;
        if (retryAttempt <= Attempts)
        {
            // Calculate delay with exponential backoff and jitter
            var delay = TimeSpan.FromMilliseconds(CalculateDelayWithJitter(retryAttempt));

            context.SetJobParameter("RetryCount", retryAttempt);
            context.CandidateState = new ScheduledState(delay)
            {
                Reason = $"Retry attempt {retryAttempt} of {Attempts} in {delay}."
            };

            if (LogEvents)
            {
                _logger.LogError($"Failed to process the job '{context.BackgroundJob.Id}': an exception occurred. Retry attempt {retryAttempt} of {Attempts} will be performed in {delay}.");
            }
        }
    }

    // Exponential backoff with jitter calculation
    private int CalculateDelayWithJitter(long retryCount)
    {
        // Base delay in milliseconds, starting with 10 seconds
        // This ensures first 3 retries happen within 2 minutes
        double baseDelay = Math.Pow(2, retryCount - 1) * 10000; // 10s, 20s, 40s...

        int jitter = _random.Next(100, 900);

        int delayMs = (int)Math.Min(baseDelay + jitter, 300000);

        return delayMs;
    }
}

