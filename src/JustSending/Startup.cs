using System.IO;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.LiteDB;
using JustSending.Data;
using JustSending.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JustSending
{
    public class Startup
    {
        private readonly IWebHostEnvironment _hostingEnvironment;

        public Startup(IConfiguration configuration, IWebHostEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
            Configuration = configuration;
        }

        public static IConfiguration Configuration { get; private set; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IConfiguration>(Configuration);

            services.Configure<FormOptions>(x =>
            {
                x.ValueLengthLimit = int.MaxValue;
                x.MultipartBodyLengthLimit = int.MaxValue;
                x.MultipartHeadersLengthLimit = int.MaxValue;
            });

            services.AddMvc();
            services.AddSignalR();
            services.AddMemoryCache();
            services.AddHttpContextAccessor();

            services.AddSingleton<AppDbContext>();
            services.AddSingleton<ConversationHub>();
            services.AddSingleton<SecureLineHub>();
            services.AddTransient<BackgroundJobScheduler>();

            services.AddHangfire(x => x.UseLiteDbStorage(Helper.BuildDbConnectionString("BackgroundJobs", _hostingEnvironment)));
            services.AddHealthChecks();
            services.AddApplicationInsightsTelemetry();
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
