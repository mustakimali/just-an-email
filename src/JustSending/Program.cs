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
using JustSending.Controllers;
using JustSending.Data.Models.Bson;
using Newtonsoft.Json;

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

        services.AddSingleton<ILock, SemaphoreLock>();

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

            var dbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
            MigrateOldData(dbContext).Wait();
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
        object? automaticRetryAttribute = null;
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

    public static async Task MigrateOldData(StatsDbContext dbContext)
    {
        var file = Path.Combine(Directory.GetCurrentDirectory(), "stats.json");
        var json = await File.ReadAllTextAsync(file);
        var data = StatImport.Stat.FromJson(json) ?? throw new InvalidOperationException("faile");

        var all = data.SelectMany(x => x.Months).SelectMany(x => x.Days)
                .ToList();
        var allTime = dbContext.StatsFindByIdOrNew(-1).Result;

        Console.WriteLine($"Before: {JsonConvert.SerializeObject(allTime)}");

        var sum = all.Aggregate(allTime, (c, n) =>
        {
            c.Devices += (int)n.Devices;
            c.Sessions += (int)n.Sessions;
            c.Messages += (int)n.Messages;
            c.MessagesSizeBytes += (int)n.MessagesSizeBytes;
            c.Files += (int)n.Files;
            c.FilesSizeBytes += (int)n.FilesSizeBytes;
            c.Version += 1;

            return c;
        });

        Console.WriteLine($"After: {JsonConvert.SerializeObject(allTime)}");
        Console.WriteLine($"Recording {all.Count} data points");
        all.ForEach(stat =>
            {
                var s = new Stats
                {
                    Id = (int)stat.Id,
                    Messages = (int)stat.Messages,
                    MessagesSizeBytes = (int)stat.MessagesSizeBytes,
                    Files = (int)stat.Files,
                    FilesSizeBytes = stat.FilesSizeBytes,
                    Devices = (int)stat.Devices,
                    Sessions = (int)stat.Sessions,
                    DateCreatedUtc = stat.DateCreatedUtc
                };
                dbContext.RecordHistoricStat(s).Wait();
            });
        dbContext.UpdateHistoricStat(sum).Wait();
    }
}

public class CustomExponentialBackoffWithJitterAttribute : JobFilterAttribute, IElectStateFilter
{
    private const int DefaultRetryAttempts = 10;
    private long _attempts;
    private readonly Random _random = new();
    private readonly ILogger<CustomExponentialBackoffWithJitterAttribute> _logger;

    public CustomExponentialBackoffWithJitterAttribute(ILogger<CustomExponentialBackoffWithJitterAttribute> logger)
    {
        Attempts = DefaultRetryAttempts;
        LogEvents = true;
        _logger = logger;
    }

    public long Attempts
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

namespace StatImport
{
    using System;
    using System.Collections.Generic;

    using System.Globalization;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    public partial class Stat
    {
        [JsonProperty("year")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Year { get; set; }

        [JsonProperty("months")]
        public Month[] Months { get; set; }
    }

    public partial class Month
    {
        [JsonProperty("month")]
        public string MonthMonth { get; set; }

        [JsonProperty("days")]
        public Day[] Days { get; set; }
    }

    public partial class Day
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("messages")]
        public long Messages { get; set; }

        [JsonProperty("messagesSizeBytes")]
        public long MessagesSizeBytes { get; set; }

        [JsonProperty("files")]
        public long Files { get; set; }

        [JsonProperty("filesSizeBytes")]
        public long FilesSizeBytes { get; set; }

        [JsonProperty("devices")]
        public long Devices { get; set; }

        [JsonProperty("sessions")]
        public long Sessions { get; set; }

        [JsonProperty("dateCreatedUtc")]
        public DateTime DateCreatedUtc { get; set; }
    }

    public partial class Stat
    {
        public static Stat[] FromJson(string json) => JsonConvert.DeserializeObject<Stat[]>(json, StatImport.Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this Stat[] self) => JsonConvert.SerializeObject(self, StatImport.Converter.Settings);
    }

    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
            {
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }

    internal class ParseStringConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(long) || t == typeof(long?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            long l;
            if (Int64.TryParse(value, out l))
            {
                return l;
            }
            throw new Exception("Cannot unmarshal type long");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (long)untypedValue;
            serializer.Serialize(writer, value.ToString());
            return;
        }

        public static readonly ParseStringConverter Singleton = new ParseStringConverter();
    }
}
