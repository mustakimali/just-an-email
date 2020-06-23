using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace JustSending
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
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
            
            try
            {
                Log.Information("Starting web host");
                await BuildWebHost(args).Build().RunAsync();
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

        public static IHostBuilder BuildWebHost(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(builder =>
                {
                    builder
                        .UseSentry()
                        .UseStartup<Startup>();
                })
                .UseSerilog();
    }
}
