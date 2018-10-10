using Hangfire;
using Hangfire.Dashboard;
using Hangfire.LiteDB;
using JustSending;
using JustSending.Data;
using JustSending.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JustALink
{
    public class Startup
    {
        private readonly IHostingEnvironment _hostingEnvironment;

        public Startup(IConfiguration configuration, IHostingEnvironment hostingEnvironment)
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
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();
            app.UseWebSockets();
            app.UseSignalR(routes =>
            {
                routes.MapHub<ConversationHub>("/signalr/hubs");
                routes.MapHub<SecureLineHub>("/signalr/secure-line");
            });

            app.UseHangfireServer();
            app.UseHangfireDashboard("/jobs", new DashboardOptions
            {
                Authorization = new[] { new CookieAuthFilter(Configuration["HangfireToken"]) }
            });

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
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
